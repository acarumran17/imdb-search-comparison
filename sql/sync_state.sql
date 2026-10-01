USE FilmDb;
GO

IF OBJECT_ID('SyncState') IS NULL 
BEGIN
    CREATE TABLE SyncState (
        TabloAdi VARCHAR(100) NOT NULL PRIMARY KEY,
        SonVersiyon BIGINT NOT NULL,
        GuncellemeZamani DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
END 
GO

-- Filmler için başlangıç kaydı (yoksa ekle)
IF NOT EXISTS (SELECT 1 FROM SyncState WHERE TabloAdi = 'Filmler')
    INSERT INTO SyncState (TabloAdi, SonVersiyon) VALUES ('Filmler',0);
GO

SELECT * FROM SyncState;
GO
