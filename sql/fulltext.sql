USE FilmDb;
GO

-- 1) KATALOG: full-text indekslerin durduğu kap
IF NOT EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE name = 'FilmKatalog')
    CREATE FULLTEXT CATALOG FilmKatalog;
GO

-- 2) Varsa eskisini kaldır (script tekrar çalıştırılabilir olsun)
IF EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID('Filmler'))
    DROP FULLTEXT INDEX ON Filmler;
GO

-- 3) İndeksi kur
CREATE FULLTEXT INDEX ON Filmler
(
    PrimaryTitle LANGUAGE 1033,
    OriginalTitle LANGUAGE 1033
)
KEY INDEX PK__Filmler__B5D0ECA40864EDEF   -- ingilizce kelime ayrıcı + gövdeleyici
ON FilmKatalog
WITH (CHANGE_TRACKING = AUTO, STOPLIST = SYSTEM);   -- CHANGE_TRACKING = AUTO-> Tabloda bir satır değişirse indeksi otomatik güncelle

GO