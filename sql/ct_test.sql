USE FilmDb
GO

-- yeni film ekleme
INSERT INTO Filmler (Tconst, TitleType, PrimaryTitle, OriginalTitle, StartYear, 
                    RuntimeMinutes, Genres, Rating, Votes)
VALUES ('tt9999001', 'movie', 'Test Filmi','Test Filmi', 2026, 100, 'Drama', 7.5, 1000);
GO

-- aynı filmi güncelle
UPDATE Filmler SET Rating = 9.9 WHERE Tconst = 'tt9999001';
GO

-- ikinci filmi ekle, sonra sil
INSERT INTO Filmler  (Tconst, TitleType, PrimaryTitle, StartYear)
VALUES ('tt9999002', 'movie', 'Silinecek Film', 2026);
GO

DELETE FROM Filmler WHERE Tconst = "tt9999002";
GO

-- CT ne diyor
DECLARE @son BIGINT =0;
 
SELECT ct.Tconst,
       ct.SYS_CHANGE_OPERATION AS Islem,
       ct.SYS_CHANGE_VERSION AS Versiyon,
       f.PrimaryTitle AS Baslik,
       f.Rating AS Puan
FROM CHANGETABLE (CHANGES Filmler, @son) AS ct
LEFT JOIN Filmler AS f ON f.Tconst = ct.Tconst
ORDER BY ct.SYS_CHANGE_VERSION;

SELECT CHANGE_TRACKING_CURRENT_VERSION() AS YeniVersiyon;
GO
