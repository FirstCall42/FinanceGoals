-- What a household expects of an account (#119): an optional yearly return and an optional monthly
-- contribution, in the account's currency, used by the balance forecast. They are guesses, so they
-- live apart from the recorded balances. Guarded so it is safe if the script runs again.

IF OBJECT_ID(N'app.AccountAssumption', N'U') IS NULL
    CREATE TABLE app.AccountAssumption
    (
        AccountId           int            NOT NULL,
        ProfileId           int            NOT NULL,
        ExpectedYearlyReturn decimal(9, 6) NULL CONSTRAINT CK_AccountAssumption_Return CHECK (ExpectedYearlyReturn BETWEEN -0.5 AND 1),
        MonthlyContribution decimal(19, 4) NULL CONSTRAINT CK_AccountAssumption_Contribution CHECK (MonthlyContribution BETWEEN 0 AND 1000000000000),
        CreatedAt           datetime2(0)   NOT NULL CONSTRAINT DF_AccountAssumption_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy           int            NOT NULL CONSTRAINT FK_AccountAssumption_CreatedBy REFERENCES app.AppUser (Id),
        UpdatedAt           datetime2(0)   NOT NULL CONSTRAINT DF_AccountAssumption_UpdatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedBy           int            NOT NULL CONSTRAINT FK_AccountAssumption_UpdatedBy REFERENCES app.AppUser (Id),
        CONSTRAINT PK_AccountAssumption PRIMARY KEY (AccountId),
        CONSTRAINT FK_AccountAssumption_Account FOREIGN KEY (AccountId, ProfileId) REFERENCES app.Account (Id, ProfileId)
    );
