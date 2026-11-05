-- ECB reference rates: how many units of a currency one euro buys on a date. Other pairs are
-- cross rates through EUR (#32). Guarded so it is safe even if the journal is lost and the script
-- runs again. The app_data role reaches the table through its schema-level grants on app.

IF OBJECT_ID(N'app.ExchangeRate', N'U') IS NULL
    CREATE TABLE app.ExchangeRate
    (
        CurrencyCode char(3)        NOT NULL CONSTRAINT FK_ExchangeRate_Currency REFERENCES app.Currency (Code),
        RateDate     date           NOT NULL,
        UnitsPerEur  decimal(19, 6) NOT NULL CONSTRAINT CK_ExchangeRate_UnitsPerEur CHECK (UnitsPerEur > 0),
        CONSTRAINT PK_ExchangeRate PRIMARY KEY (CurrencyCode, RateDate)
    );
