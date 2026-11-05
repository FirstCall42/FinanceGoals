-- The currencies the household holds accounts in. #30 extends this to every ISO 4217
-- currency in a later seed script. Guarded: inserts only the rows that are missing.

INSERT INTO app.Currency (Code, NumericCode, MinorUnits, Name)
SELECT v.Code, v.NumericCode, v.MinorUnits, v.Name
FROM (VALUES
    ('NOK', 578, 2, N'Norwegian krone'),
    ('EUR', 978, 2, N'Euro'),
    ('USD', 840, 2, N'US dollar'),
    ('RON', 946, 2, N'Romanian leu')
) AS v (Code, NumericCode, MinorUnits, Name)
WHERE NOT EXISTS (SELECT 1 FROM app.Currency AS c WHERE c.Code = v.Code);
