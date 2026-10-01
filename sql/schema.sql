--FilmDb diye veritabanı varsa id'sini yoksa NULL döner
IF  DB_ID('FilmDb') IS NULL
    CREATE DATABASE FilmDb;
GO
-- GO: buraya kadar olanları çalıştır sonra devam (SQL Server özel araç)

-- bu veritabanında çalış
USE FilmDb 
GO

IF OBJECT_ID('Filmler') IS NOT NULL
    DROP TABLE Filmler;
GO

CREATE TABLE Filmler(
   Tconst         VARCHAR(20)  COLLATE Latin1_General_100_CI_AS_SC_UTF8 NOT NULL PRIMARY KEY,
    TitleType      VARCHAR(30)  COLLATE Latin1_General_100_CI_AS_SC_UTF8,
    PrimaryTitle   VARCHAR(500) COLLATE Latin1_General_100_CI_AS_SC_UTF8,
    OriginalTitle  VARCHAR(500) COLLATE Latin1_General_100_CI_AS_SC_UTF8,
    StartYear      SMALLINT,
    EndYear        SMALLINT,
    RuntimeMinutes INT,
    Genres         VARCHAR(200) COLLATE Latin1_General_100_CI_AS_SC_UTF8,
    Rating         DECIMAL(3,1),
    Votes          INT
);
GO