USE FilmDb;
GO

-- veritabanı seviyesinde açma:
IF NOT EXISTS (SELECT 1 FROM sys.change_tracking_database
                WHERE database_id = DB_ID('FilmDb'))
    ALTER DATABASE FilmDb
    SET CAHNGE_TRACKING = ON (CHANGE_RETENTION = 7 DAYS, AUTO_CLEANUP = ON);
GO

-- tablo seviyesinde açma:
IF NOT EXISTS (SELECT 1 FROM sys.change_tracking_tables
                WHERE object_id = OBJECT_ID('Filmler'))
    ALTER TABLE Filmler
    ENABLE CAHNGE_TRACKING WITH (TRACK_COLUMNS_UPDATED = OFF);
GO

-- açıldı mı kontrolü
SELECT DB_NAME(database_id) AS Veritabani,
        is_auto_cleanup_on AS OtomatikTemizlik,
        retention_period AS SaklamaSuresi,
        retention_period_units_desc AS Birim
FROM sys.change_tracking_databases;

SELECT OBJECT_NAME(object_id) AS Tablo
FROM sys.change_tracking_tables;

SELECT CHANGE_TRACKING_CURRENT_VERSION() AS SuAnkiVersiyon;
GO
