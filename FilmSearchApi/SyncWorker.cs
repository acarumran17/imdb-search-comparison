using Elastic.Clients.Elasticsearch;
using Microsoft.Data.SqlClient;


public class SyncWorker : BackgroundService
{
    private readonly IConfiguration _config;
    private readonly ILogger<SyncWorker> _logger;
    private readonly ElasticsearchClient _es;
    private const int KalpAtisiAraligi = 60;  // 60 tur x 5 sn = 5 dk
    private int _turSayaci;

    public SyncWorker(IConfiguration config, ILogger<SyncWorker> logger, ElasticsearchClient es)
    {
        _config = config;
        _logger = logger;
        _es = es;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Senkronizasyon servisi başladı");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var versiyon = await SenkronizeEt(stoppingToken);

                _turSayaci++;
                if (_turSayaci >= KalpAtisiAraligi)
                {
                    _logger.LogInformation(
                        "Servis çalışıyor. {Tur} tur tamamlandı, güncel versiyon {Versiyon}",
                        _turSayaci, versiyon);
                    _turSayaci = 0;
                }
            }
            catch (OperationCanceledException)
            {
                break;   // normal kapanış, hata değil
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Senkronizasyon turunda hata!");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;   // bekleme sırasında kapanma isteği geldi
            }
        }
    }

    private async Task<long> SenkronizeEt(CancellationToken ct)
    {
        var baglantiDizesi = _config.GetConnectionString("FilmDb");

        await using var baglanti = new SqlConnection(baglantiDizesi);
        await baglanti.OpenAsync(ct);

        // son kalınan yer 
        long sonVersiyon = await TekDegerOku(baglanti,
            "SELECT SonVersiyon FROM SyncState WHERE TabloAdi = 'Filmler'", ct);

        // şuan nerede (sorgudan önce alıyoruz)
        long suAnkiVersiyon = await TekDegerOku(baglanti,
            "SELECT CHANGE_TRACKING_CURRENT_VERSION()", ct);

        if (suAnkiVersiyon == sonVersiyon)
            return suAnkiVersiyon;   // boş çıkış, versiyon aynı

        // kaydettiğim versiyon hala geçerli mi 
        long minGecerli = await TekDegerOku(baglanti,
            "SELECT CHANGE_TRACKING_MIN_VALID_VERSION(OBJECT_ID('Filmler'))", ct);

        if (sonVersiyon < minGecerli)
        {
            _logger.LogWarning(
                "Versiyon {Son} artık geçersiz (en eski geçerli: {Min}). Tam senkronizasyon gerekiyor.",
                sonVersiyon, minGecerli);
            return sonVersiyon;
        }

        // ne değişti (her 5 saniyede bir çalışıyor, servisin asıl sorgusu)
        const string sorgu = """
                             SELECT ct.Tconst,
                             ct.SYS_CHANGE_OPERATION,
                             f.TitleType,
                             f.PrimaryTitle,
                             f.OriginalTitle,
                             f.StartYear,
                             f.EndYear,
                             f.RuntimeMinutes,
                             f.Genres,
                             f.Rating,
                             f.Votes
                             FROM CHANGETABLE (CHANGES Filmler, @son) AS ct
                             LEFT JOIN Filmler AS f ON f.Tconst = ct.Tconst
                             """;
        var yazilacaklar = new List<FilmDoc>();
        var silinecekler = new List<string>();

        await using (var komut = new SqlCommand(sorgu, baglanti))
        {
            komut.Parameters.AddWithValue("@son", sonVersiyon);

            await using var okuyucu = await komut.ExecuteReaderAsync(ct);

            while (await okuyucu.ReadAsync(ct))
            {
                var tconst = okuyucu.GetString(0);
                var islem = okuyucu.GetString(1);


                if (islem == "D")
                {
                    silinecekler.Add(tconst);
                    _logger.LogInformation("SIL {Tconst}", tconst);
                }
                else
                {
                    var belge = new FilmDoc(
                        tconst,
                        okuyucu.IsDBNull(2) ? null : okuyucu.GetString(2),
                        okuyucu.IsDBNull(3) ? null : okuyucu.GetString(3),
                        okuyucu.IsDBNull(4) ? null : okuyucu.GetString(4),
                        okuyucu.IsDBNull(5) ? null : (int?)okuyucu.GetInt16(5),
                        okuyucu.IsDBNull(6) ? null : (int?)okuyucu.GetInt16(6),
                        okuyucu.IsDBNull(7) ? null : okuyucu.GetInt32(7),
                        TurleriAyir(okuyucu.IsDBNull(8) ? null : okuyucu.GetString(8)),
                        okuyucu.IsDBNull(9) ? null : (double?)okuyucu.GetDecimal(9),
                        okuyucu.IsDBNull(10) ? null : okuyucu.GetInt32(10)
                    );
                    yazilacaklar.Add(belge);
                    _logger.LogInformation("YAZ {Tconst} {Baslik}", tconst, belge.PrimaryTitle);
                }
            }
        }
        // ES'e yaz
        foreach (var belge in yazilacaklar)
        {
            var cevap = await _es.IndexAsync(belge, i => i.Id(belge.Tconst), ct);

            if (!cevap.IsValidResponse)
                throw new Exception($"ES yazma hatası: {belge.Tconst} - {cevap.DebugInformation}");
        }

        // ES'ten sil
        foreach (var id in silinecekler)
        {
            var cevap = await _es.DeleteAsync<FilmDoc>(id, ct);

            if (!cevap.IsValidResponse && cevap.Result != Result.NotFound)
                throw new Exception($"ES silme hatası: {id} - {cevap.DebugInformation}");
        }

        // nerede kaldığımı kaydet
        await VersiyonKaydet(baglanti, suAnkiVersiyon, ct);

        _logger.LogInformation(
            "Tur bitti. Yazılan: {Yaz}, Silinen: {Sil}. Versiyon {Eski} -> {Yeni}",
            yazilacaklar.Count, silinecekler.Count, sonVersiyon, suAnkiVersiyon);

        return suAnkiVersiyon;
    }

    private static async Task<long> TekDegerOku(SqlConnection baglanti, string sorgu, CancellationToken ct)
    {
        await using var komut = new SqlCommand(sorgu, baglanti);
        var sonuc = await komut.ExecuteScalarAsync(ct);
        return sonuc is null or DBNull ? 0 : Convert.ToInt64(sonuc);
    }
    
    
    private static async Task VersiyonKaydet(SqlConnection baglanti, long versiyon, CancellationToken ct)
    {
        await using var komut = new SqlCommand(
            "UPDATE SyncState SET SonVersiyon = @v, GuncellemeZamani = SYSUTCDATETIME() " +
            "WHERE TabloAdi = 'Filmler'",
            baglanti);
        
        komut.Parameters.AddWithValue("@v", versiyon);
        await komut.ExecuteNonQueryAsync(ct);
    }

    private static string[] TurleriAyir(string? ham)
    {
        if (string.IsNullOrWhiteSpace(ham))
            return Array.Empty<string>();
        
        return ham.Split(',');
    }
}

public record FilmDoc(
    string Tconst, 
    string? TitleType,
    string? PrimaryTitle,
    string? OriginalTitle,
    int? StartYear,
    int? EndYear,
    int? RuntimeMinutes,
    string[] Genres,
    double? Rating,
    int? Votes
);