-- Balances of accounts on dates (#36): one amount per account per day, in the account's currency
-- (amounts carry no currency of their own, so an account's currency is fixed once it has values).
-- A snapshot is the set of balances recorded on one date. Guarded so it is safe even if the journal
-- is lost and the script runs again.

-- Lets a value point at (account, profile) together, so it can never belong to another household.
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UQ_Account_Id_ProfileId' AND parent_object_id = OBJECT_ID(N'app.Account'))
    ALTER TABLE app.Account ADD CONSTRAINT UQ_Account_Id_ProfileId UNIQUE (Id, ProfileId);

IF OBJECT_ID(N'app.AccountValue', N'U') IS NULL
    CREATE TABLE app.AccountValue
    (
        AccountId int           NOT NULL,
        ValueDate date          NOT NULL,
        ProfileId int           NOT NULL CONSTRAINT FK_AccountValue_Profile REFERENCES app.Profile (Id),
        Amount    decimal(19, 4) NOT NULL CONSTRAINT CK_AccountValue_Amount CHECK (Amount BETWEEN -10000000000000 AND 10000000000000),
        CreatedAt datetime2(0)  NOT NULL CONSTRAINT DF_AccountValue_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy int           NOT NULL CONSTRAINT FK_AccountValue_CreatedBy REFERENCES app.AppUser (Id),
        UpdatedAt datetime2(0)  NOT NULL CONSTRAINT DF_AccountValue_UpdatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedBy int           NOT NULL CONSTRAINT FK_AccountValue_UpdatedBy REFERENCES app.AppUser (Id),
        CONSTRAINT PK_AccountValue PRIMARY KEY (AccountId, ValueDate),
        CONSTRAINT FK_AccountValue_Account FOREIGN KEY (AccountId, ProfileId) REFERENCES app.Account (Id, ProfileId)
    );

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AccountValue_ProfileId_ValueDate' AND object_id = OBJECT_ID(N'app.AccountValue'))
    CREATE INDEX IX_AccountValue_ProfileId_ValueDate ON app.AccountValue (ProfileId, ValueDate);
