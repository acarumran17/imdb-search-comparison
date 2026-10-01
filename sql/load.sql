USE FilmDb;
GO

TRUNCATE TABLE Filmler;   -- TRUNCATE: tabloyu komple boşalt (DELETE: her satırı tek tek silerdi)
GO

BULK INSERT Filmler
FROM '/tmp/filmler_sql_u16.tsv'
WITH (
    DATAFILETYPE = 'widechar',  -- dosya UTF-16, geniş karakterli
    FIELDTERMINATOR = '\t',     -- sütunlar tab ile ayrılır
    ROWTERMINATOR = '\n',       -- satırlar \n ile bitiyor
    KEEPNULLS,                  -- boş alanlar NULL olsun
    TABLOCK                     -- tabloyu kilitle, yükleme hızlansın
);
GO

SELECT COUNT(*) AS YuklenenSatir FROM Filmler;
GO