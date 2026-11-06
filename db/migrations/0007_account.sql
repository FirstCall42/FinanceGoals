-- Accounts of a household (#35): brokerage, savings, deposit, current, pension and property
-- accounts, each in one currency. No individual holdings. An archived account keeps its row (and,
-- later, its value history). Guarded so it is safe even if the journal is lost and the script
-- runs again.

IF OBJECT_ID(N'app.Account', N'U') IS NULL
    CREATE TABLE app.Account
    (
        Id           int           NOT NULL IDENTITY (1, 1) CONSTRAINT PK_Account PRIMARY KEY,
        ProfileId    int           NOT NULL CONSTRAINT FK_Account_Profile REFERENCES app.Profile (Id),
        Name         nvarchar(100) NOT NULL CONSTRAINT CK_Account_Name CHECK (LEN(Name) > 0),
        Type         varchar(20)   NOT NULL CONSTRAINT CK_Account_Type CHECK (Type IN ('Brokerage', 'Savings', 'Deposit', 'Current', 'Pension', 'Property')),
        CurrencyCode char(3)       NOT NULL CONSTRAINT FK_Account_Currency REFERENCES app.Currency (Code),
        ArchivedAt   datetime2(0)  NULL,
        CreatedAt    datetime2(0)  NOT NULL CONSTRAINT DF_Account_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy    int           NOT NULL CONSTRAINT FK_Account_CreatedBy REFERENCES app.AppUser (Id),
        UpdatedAt    datetime2(0)  NOT NULL CONSTRAINT DF_Account_UpdatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedBy    int           NOT NULL CONSTRAINT FK_Account_UpdatedBy REFERENCES app.AppUser (Id),
        CONSTRAINT UQ_Account_ProfileId_Name UNIQUE (ProfileId, Name)
    );
