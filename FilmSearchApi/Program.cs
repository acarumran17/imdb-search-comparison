using Elastic.Clients.Elasticsearch;
using Microsoft.Data.SqlClient;
using System.Diagnostics;   // Stopwatch için

var builder = WebApplication.CreateBuilder(args);

// Elasticsearch istemcisini bir kere kur, uygulama boyunca kullan
var esSettings = new ElasticsearchClientSettings(new Uri("http://localhost:9200"))
    .DefaultIndex("filmler");
    
builder.Services.AddSingleton(new ElasticsearchClient(esSettings));
// AddSingleton->  bu nesneden tek bir tane üret, isteyen herkese aynısını ver 

var app = builder.Build();

// endpoint       // string q-> URL'deki sorgu dizesinden → ?q=inception
// ElasticsearchClient -> Dependency Injection'dan 
// basit tipler (string, int) URL'den okunur; kayıtlı bir servis tipi ise DI'dan gelir
// async ve await -> cevap beklerken iş parçacığını (thread) meşgul etme
// ES'e ağ üzerinden gidiyoruz; o sırad sunucu başka isteklere bakabilsin diye
app.MapGet("/api/search/es", async (string q, ElasticsearchClient es) =>
    {                                       // SearchAsync<Film> → "sonuçları Film tipine çevir
        var answer = await es.SearchAsync<Film>(s => s
            .Query(sorgu => sorgu
                .Match(m => m
                    .Field(f => f.PrimaryTitle)
                    .Query(q)
                )
            )
            .Size(10)
        );
        return Results.Ok(new               // new { ... } → anonim nesne. Adı olmayan, oracıkta üretilen bir tip
        {                                   // Sırf JSON'a çevrilecek diye ayrı bir sınıf yazmaya değmez
            source = "elasticsearch",
            timeMs = answer.Took,           // ES'in kendi ölçtüğü süre
            sum = answer.Total,
            result = answer.Hits.Select(h=> h.Source! with { Score = h.Score })
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
    var connectionString = config.GetConnectionString("FilmDb");   //(appsettings.json'dan adresi al)
    var results = new List<Film>();    // filmleri dolduracağımız boş liste
    
    var time = Stopwatch.StartNew();
    
  // await using-> blok bitince otomatik kapanıyor
    await using var connection= new SqlConnection(connectionString);  // bağlantı nesnesi oluşturuyor
    await connection.OpenAsync();  // bağlanıyor
    
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
            timeMs = time.ElapsedMilliseconds,
            sum = results.Count,
            result = results
        });
});
    
    app.Run();
    
    // Elasticsearch'ten dönen belgenin C# karşılığı 
    // record → sadece veri taşıyan, kısa yazılan sınıf
    public record Film(
        string Tconst,
        string PrimaryTitle,
        int? StartYear,       // ? -> bu alan null olabilir
        double? Rating,
        double? Score = null
    );