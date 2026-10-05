# IMDb Film Arama: Elasticsearch ve SQL Server Karşılaştırması

2.722.191 IMDb kaydı üzerinde Elasticsearch ile SQL Server Full-Text Search'ü
aynı veriyle karşılaştıran bir çalışma. Projede ayrıca iki motora da sorgu atan
bir .NET arama API'si ve SQL Server'daki değişiklikleri Change Tracking ile
Elasticsearch'e yansıtan bir arka plan servisi var.

Solvera staj projesi olarak geliştirildi.

## Mimari

```
  Tarayıcı
     |
     |  GET /api/search/es   |  GET /api/search/sql
     v                       v
  FilmSearchApi  (ASP.NET Core + SyncWorker)
     |                       |
     v                       v
  Elasticsearch           SQL Server

  SQL Server --[Change Tracking, 5 sn]--> SyncWorker --> Elasticsearch

```

Üç Docker konteyneri (`elasticsearch`, `kibana`, `sqlserver`) aynı Compose ağında
çalışıyor. .NET uygulaması Docker'ın dışında, doğrudan makinede.

## Kullanılan teknolojiler

| Bileşen | Sürüm / Not |
|---|---|
| Elasticsearch | 9.3.0 — tek node, güvenlik kapalı, 2 GB heap |
| Kibana | 9.3.0 |
| SQL Server | 2022 — Full-Text Search bileşeni eklenmiş özel imaj |
| .NET | 9.0 — Minimal API + BackgroundService |
| Python | 3.x — veri hazırlama ve toplu yükleme |

> SQL Server imajı `linux/amd64`, Apple Silicon'da Rosetta emülasyonuyla çalışıyor.
> Ölçümlerde SQL Server'ın aleyhine bir dezavantaj.

## Gereksinimler

- Docker Desktop
- .NET SDK 9.0
- Python 3.x
- IMDb veri setleri `title.basics.tsv.gz` ve `title.ratings.tsv.gz`
  (https://datasets.imdbws.com/) — `data/` klasörüne

`data/` ve `.env` gitignore'da: ilki GitHub için fazla büyük, ikincisi şifre içeriyor.

## Kurulum

### 1. Şifreler ve ayarlar

Proje kökünde `.env`:

```env
MSSQL_SA_PASSWORD=GucluBirSifre1!
```

`FilmSearchApi/appsettings.Development.json` (gitignore'da):

```json
{
  "ConnectionStrings": {
    "FilmDb": "Server=localhost,1433;Database=FilmDb;User Id=sa;Password=GucluBirSifre1!;TrustServerCertificate=True"
  }
}
```

Sır olmayan ayarlar `appsettings.json`'da ve depoya giriyor:

```json
"Elasticsearch": {
  "Url": "http://localhost:9200",
  "Index": "filmler"
}
```

Bağlantı dizesinin bilinçli olarak varsayılanı yok — eksikse uygulamanın sessizce
başka bir yere bağlanması yerine hata vermesi gerekiyor.

### 2. Konteynerleri başlat

```bash
docker compose up -d --build
```

`sqlserver` servisi `sqlserver-fts/Dockerfile`'dan inşa ediliyor. Microsoft'un
hazır imajında Full-Text Search yok (`SERVERPROPERTY('IsFullTextInstalled')` → 0),
o yüzden üstüne `mssql-server-fts` paketi kuruluyor.

### 3. Elasticsearch'e yükle

```bash
python load_to_es.py
```

İndeks `mapping.json`'dan kuruluyor: standard tokenizer, Türkçe lowercase ve
`asciifolding` içeren özel bir analyzer, `dynamic: strict`.
Süre ~78 saniye, indeks 407 MB.

### 4. SQL Server'a yükle

```bash
python export_to_tsv.py
docker cp data/filmler_sql_u16.tsv sqlserver:/tmp/
# schema.sql, load.sql ve fulltext.sql dosyalarını sqlcmd ile çalıştır
```

`export_to_tsv.py`, Elasticsearch yükleyicisiyle **aynı** `iter_docs()` üreticisini
kullanıyor — karşılaştırmanın adil olmasının sebebi bu. Dosya **UTF-16 + BOM**
olarak yazılıyor, çünkü Linux'taki `BULK INSERT` `CODEPAGE` seçeneğini desteklemiyor;
okuma `DATAFILETYPE = 'widechar'` ile yapılıyor.

Yükleme ~10 saniye. Full-text indeksin dolması arka planda ~15 dakika sürüyor:

```sql
SELECT FULLTEXTCATALOGPROPERTY('FilmKatalog', 'PopulateStatus');  -- 1 = sürüyor, 0 = bitti
```

### 5. Change Tracking'i aç

`sql/change_tracking.sql` ve `sql/sync_state.sql` dosyalarını çalıştır.

### 6. API'yi başlat

```bash
cd FilmSearchApi
dotnet run
```

## Uç noktalar

| Uç nokta | Motor | Sorgu |
|---|---|---|
| `GET /api/search/es?q=inception` | Elasticsearch | `primaryTitle` üzerinde `match` |
| `GET /api/search/sql?q=inception` | SQL Server | `FREETEXTTABLE(Filmler, PrimaryTitle, @q)` |

İkisi de aynı biçimde cevap dönüyor, böylece tarayıcıda yan yana karşılaştırılabiliyor:

| Alan | Anlamı |
|---|---|
| `sum` | toplam eşleşme sayısı (dönen satır sayısı değil) |
| `returned` | `result` içindeki satır sayısı, en fazla 10 |
| `scoreType` | `score`'un hangi ölçekte olduğu. BM25'in üst sınırı yok, SQL Server'ın `RANK`'ı 0–1000. İkisi karşılaştırılabilir değil, o yüzden aynı ölçeğe çekilmek yerine etiketleniyor |
| `countMs` | sadece SQL uç noktasında: `timeMs`'in ne kadarı ek `COUNT(*)` sorgusuna gitti |

SQL uç noktası iki sorgu çalıştırıyor: biri sonuç sayfası, biri toplam sayı. SQL Server
toplamı satırlarla birlikte vermiyor. Sayım sorgusunun okuyucu açılmadan bitmesi
gerekiyor, çünkü tek bağlantı üzerinde aynı anda tek sonuç kümesi açık olabiliyor.

Elasticsearch'e ulaşılamadığında ES uç noktası boş liste değil **HTTP 500** dönüyor —
"hiçbir şey eşleşmedi" ile "motor çalışmıyor" ayrı şeyler.

Çok kelimeli aramalarda URL kodlaması gerekiyor: `?q=star%20wars`.

## Senkronizasyon servisi

`FilmSearchApi/SyncWorker.cs`, API ile aynı uygulama içinde çalışan bir
`BackgroundService`. Her 5 saniyede bir `SyncState` tablosundaki son versiyonu okuyor,
`CHANGETABLE` ile o versiyondan sonra değişen satırları alıyor, `I`/`U` olanları
Elasticsearch'e yazıp `D` olanları siliyor, **sonra** yeni versiyonu kaydediyor.

Bu sıra kasıtlı: versiyon ancak yazmalar başarılı olunca kaydediliyor, yani tur
ortasında çökme olursa aynı değişiklikler tekrar işleniyor. Aynı `_id` ile yazmak
üzerine yazdığı için tekrar zararsız — kayıp yerine tekrar.

Change Tracking tercih edildi çünkü yalnızca değişen satırın anahtarını ve işlem
tipini saklıyor (~40 bayt/satır) ve ek servis istemiyor. CDC satırın eski ve yeni
halini birden tutuyor, ~10 kat yer kaplıyor ve SQL Server Agent gerektiriyor.
Debezium zaten CDC üzerine kurulu. Elasticsearch'e her zaman kaydın son hali
yazıldığı için değişiklik geçmişine ihtiyaç yok.

## Karşılaştırma sonuçları

| Ölçüt | Elasticsearch | SQL Server FTS |
|---|---|---|
| Aramaya hazır olma | ~78 sn | ~15 dk |
| Disk | 407 MB | 614 MB |
| `inception` — eşleşme / süre | 50 / ~3 ms | 50 / ~3 ms |
| `inception` — gerçek *Inception* sırası | 1. | ilk 10'da yok |
| `amelie` → `Amélie` | bulundu | bulunamadı |
| `inceptoin` (yazım hatası) | 55 (fuzzy) | 0 |
| Tür filtresi (`Comedy`) | 1–2 ms | ~337 ms |
| Türlere göre gruplama | 1–2 ms | ~790 ms |
| Toplam eşleşme sayısı | aramayla birlikte geliyor | ayrı `COUNT(*)`, süreyi ~2 katına çıkarıyor (medyan 11,5 → 24,5 ms) |

Basit tek kelimelik aramada iki motor da hızlı. Fark **arama kalitesinde**
(sıralama, aksan, yazım hatası) ve **filtreleme/gruplama hızında** (200–500 kat).

Sıralama farkının sebebi somut: `FREETEXTTABLE`'ın döndürdüğü 50 kaydın hepsi
`RANK = 143`, çünkü SQL Server'ın sıralamasında **alan uzunluğu normalizasyonu yok**.
BM25'te var, bu yüzden Elasticsearch kısa olan *Inception*'ı *Alien Inception*'ın
üstüne koyuyor.

### Ölçümün sınırları

- SQL Server Rosetta emülasyonuyla, Elasticsearch doğal çalışıyor.
- Tür filtresi ve gruplama testlerinde veri modeli Elasticsearch'e göre kurulmuştu
  (`Genres` virgüllü metin). İlişkisel bir tasarımda türler ayrı indeksli bir tabloda
  olurdu; bu fark motorun değil veri modelinin farkı.
- İki `timeMs` aynı şeyi ölçmüyor: Elasticsearch'ün `took` değeri sadece arama süresi,
  `Stopwatch` ise bağlantı + sorgu + satır okuma toplamı.

## Proje yapısı

```
.
├── docker-compose.yml          # elasticsearch, kibana, sqlserver
├── sqlserver-fts/Dockerfile    # SQL Server 2022 + Full-Text Search
├── mapping.json                # ES indeks tasarımı
├── prepare_data.py             # iki IMDb dosyasını birleştirip belge üretir
├── load_to_es.py               # Elasticsearch'e toplu yükleme
├── export_to_tsv.py            # aynı belgeleri UTF-16 TSV'ye yazar
├── queries.json                # 8 örnek ES sorgusu
├── sql/
│   ├── schema.sql              # FilmDb + Filmler
│   ├── load.sql                # BULK INSERT
│   ├── fulltext.sql            # full-text katalog + indeks
│   ├── change_tracking.sql     # Change Tracking'i açar
│   ├── sync_state.sql          # versiyon takip tablosu
│   └── ct_test.sql             # insert/update/delete testi
└── FilmSearchApi/
    ├── Program.cs              # DI kurulumu + iki arama uç noktası
    └── SyncWorker.cs           # Change Tracking → Elasticsearch senkronizasyonu
```

