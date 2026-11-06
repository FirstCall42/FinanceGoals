-- The household's main currency (#105): totals and charts start in it, for every member and device.
-- NOK by default; it must be a currency the app knows. Guarded so it is safe even if the journal
-- is lost and the script runs again.

IF COL_LENGTH(N'app.Profile', N'MainCurrency') IS NULL
    ALTER TABLE app.Profile ADD MainCurrency char(3) NOT NULL
        CONSTRAINT DF_Profile_MainCurrency DEFAULT 'NOK'
        CONSTRAINT FK_Profile_MainCurrency REFERENCES app.Currency (Code);
