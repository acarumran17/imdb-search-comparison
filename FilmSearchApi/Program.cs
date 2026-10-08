using Elastic.Clients.Elasticsearch;
using Microsoft.Data.SqlClient;
using System.Diagnostics;   // Stopwatch için
using Elastic.Ingest.Elasticsearch;
using Elastic.Ingest.Elasticsearch.DataStreams;
using Elastic.Serilog.Sinks;
using Serilog;
using DataStreamName = Elastic.Ingest.Elasticsearch.DataStreams.DataStreamName;


var logEsUrl = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .Build()["Elasticsearch:Url"] ?? "http://localhost:9200";

// Log.Logger = serilog'un genel kaydedicisi
// kurulurken hata olursa yakalayabilsin diye builder'dan önce yazıldı
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()  // bundan düşük seviyeli loglar (debug,verbose) hiç üretilmiyor
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()     // log bağlamına eklenen alanları her kayda otomatik iliştiriyor
    .WriteTo.Console()     // WriteTo.... -> üç hedef
    .WriteTo.File(
        "logs/filmsearchapi-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
    .WriteTo.Elasticsearch(new[] { new Uri(logEsUrl) }, opts =>
    {
        opts.DataStream = new DataStreamName("logs", "filmsearchapi", "default");  // veri akışını(data stream) oluşturur
        opts.BootstrapMethod = BootstrapMethod.Silent;
    })
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);

var esUrl = builder.Configuration["Elasticsearch:Url"] ?? "http://localhost:9200";
var esIndex = builder.Configuration["Elasticsearch:Index"] ?? "filmler";

var esSettings = new ElasticsearchClientSettings(new Uri(esUrl))
    .DefaultIndex(esIndex);

builder.Services.AddSerilog(); // .NET'in varsayılan kaydedicisini çıkarıp yerine Serilog koyuyoruz
    
builder.Services.AddSingleton(new ElasticsearchClient(esSettings));
// AddSingleton->  bu nesneden tek bir tane üret, isteyen herkese aynısını ver 
builder.Services.AddHostedService<SyncWorker>();

var app = builder.Build();
app.UseSerilogRequestLogging();   // middleware - her isteğin başında devreye girip sonunda tek bir satır yazıyor
                                  // ->  HTTP "GET" "/api/search/es" responded 200 in 27.9854 ms

// endpoint       // string q-> URL'deki sorgu dizesinden → ?q=inception
// ElasticsearchClient -> Dependency Injection'dan 
// basit tipler (string, int) URL'den okunur; kayıtlı bir servis tipi ise DI'dan gelir
// async ve await -> cevap beklerken iş parçacığını (thread) meşgul etme
// ES'e ağ üzerinden gidiyoruz; o sırada sunucu başka isteklere bakabilsin diye
app.MapGet("/api/search/es", async (string q, ElasticsearchClient es, ILogger<Program> logger) =>
    {                      // SearchAsync<Film> → "sonuçları Film tipine çevir
        var answer = await es.SearchAsync<Film>(s => s
            .Query(sorgu => sorgu
                .Match(m => m
                    .Field(f => f.PrimaryTitle)
                    .Query(q)
                )
            )
            .Size(10)
            .TrackTotalHits(true)
        );
        if (!answer.IsValidResponse)
        {
             logger.LogError("Elasticsearch araması başarısız. {DebugInfo}", answer.DebugInformation);
            return Results.Problem("Arama servisi şu anda kullanılamıyor");
        }

        return Results.Ok(new               // new { ... } → anonim nesne. Adı olmayan, oracıkta üretilen bir tip
        {                                   // Sırf JSON'a çevrilecek diye ayrı bir sınıf yazmaya değmez
            source = "elasticsearch",
            scoreType = "BM25 (üst sınır yok)",
            timeMs = answer.Took,           // ES'in kendi ölçtüğü süre
            sum = answer.Total,
            returned = answer.Hits.Count,
            result = answer.Hits.Select(h => h.Source! with { Score = h.Score })
        });
    });

// -----------------  SQL SERVER  ----------------------------------------------------

// IConfiguration config parametresi DI'dan geliyor , appsettings.json'a erişim
app.MapGet("/api/search/sql", async (string q, IConfiguration config) =>
{
    const string query = """
        SELECT TOP 10 f.Tconst, f.PrimaryTitle, f.StartYear, f.Rating, ft.RANK
        FROM FREETEXTTABLE(Filmler, PrimaryTitle, @q) AS ft 
        JOIN Filmler AS f ON f.Tconst = ft.[KEY]
        ORDER BY ft.RANK DESC 
        """;
    // toplam eşleşme sayısı için ikinci sorgu
    // TOP 10 yok, JOIN yok — sadece kaç satır eşleşti
    const string countQuery = """
        SELECT COUNT(*)
        FROM FREETEXTTABLE(Filmler, PrimaryTitle, @q) AS ft
        """;
    var connectionString = config.GetConnectionString("FilmDb");   //(appsettings.json'dan adresi al)
    var results = new List<Film>();    // filmleri dolduracağımız boş liste
    
    var time = Stopwatch.StartNew();
    
    // await using-> blok bitince otomatik kapanıyor
    await using var connection= new SqlConnection(connectionString);  // bağlantı nesnesi oluşturuyor
    await connection.OpenAsync();  // bağlanıyor
    
    var sayimSuresi = Stopwatch.StartNew();
    // önce toplam sayıyı al (okuyucu açılmadan ÖNCE olmak zorunda)
    
    int toplam;
    await using (var sayimKomutu = new SqlCommand(countQuery, connection))
    {
        sayimKomutu.Parameters.AddWithValue("@q", q);
        toplam = Convert.ToInt32(await sayimKomutu.ExecuteScalarAsync());
    }

    sayimSuresi.Stop();

    // şu sorgu, şu bağlantı üzerinden çalıştırılacak
    await using var command = new SqlCommand(query, connection);
    command.Parameters.AddWithValue("@q", q);
    // sorgudaki @q yerine, kullanıcının yazdığı kelimeyi koyar

    await using var reader = await command.ExecuteReaderAsync();
    // sorguyu çalıştır ve bir okuyucu al

    // ReadAsync -> bir sonraki satıra geçiyor ve satır varsa true dönüyor, satır sayısı kez çalışır
    while (await reader.ReadAsync())
    {
        var film = new Film(
            reader.GetString(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : (int?)reader.GetInt16(2),
            reader.IsDBNull(3) ? null : (double?)reader.GetDecimal(3),
            reader.GetInt32(4)  // RANK
        );
        results.Add(film);   // listeye ekle
    }
    time.Stop();
    
    return Results.Ok(new   // kronometreyi durdur, sonucu paketle, gönder.
        {
            source = "sqlserver",
            scoreType = "RANK (0-1000)",
            timeMs = time.ElapsedMilliseconds,
            countMs = sayimSuresi.ElapsedMilliseconds,
            sum = toplam,
            returned = results.Count,
            result = results
        });
});
    
app.Run();
Log.CloseAndFlush();  // uyg. kapanırken tamponda bekleyenler bu satır olmadan kaybolur
    
    // Elasticsearch'ten dönen belgenin C# karşılığı 
// record → sadece veri taşıyan, kısa yazılan sınıf
public record Film(
    string Tconst,
    string PrimaryTitle,
    int? StartYear,       // ? -> bu alan null olabilir
    double? Rating,
    double? Score = null
);