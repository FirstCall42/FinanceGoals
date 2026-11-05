-- Schema for application data, the role the app user gets, and the currency table.
-- Guarded so it is safe even if the journal is lost and the script runs again.

IF SCHEMA_ID(N'app') IS NULL
    EXEC (N'CREATE SCHEMA app AUTHORIZATION dbo;');

IF DATABASE_PRINCIPAL_ID(N'app_data') IS NULL
    CREATE ROLE app_data AUTHORIZATION dbo;

IF OBJECT_ID(N'app.Currency', N'U') IS NULL
    CREATE TABLE app.Currency
    (
        Code        char(3)       NOT NULL CONSTRAINT PK_Currency PRIMARY KEY,
        NumericCode smallint      NOT NULL CONSTRAINT UQ_Currency_NumericCode UNIQUE,
        MinorUnits  tinyint       NOT NULL CONSTRAINT CK_Currency_MinorUnits CHECK (MinorUnits <= 4),
        Name        nvarchar(100) NOT NULL
    );
