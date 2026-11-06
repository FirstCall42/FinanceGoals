-- Extra payments and the actual balance of a loan (#137). One-off extras are keyed by loan and day,
-- monthly extras have their own id and an optional end, and a recorded balance is the bank's figure
-- on a day (real balances drift from the plan once extras are paid). Every table carries ProfileId
-- and references the loan together with it. Guarded so it is safe if the script runs again.

IF OBJECT_ID(N'app.LoanExtraPayment', N'U') IS NULL
    CREATE TABLE app.LoanExtraPayment
    (
        LoanId     int            NOT NULL,
        ExtraDate  date           NOT NULL,
        ProfileId  int            NOT NULL,
        Amount     decimal(19, 4) NOT NULL CONSTRAINT CK_LoanExtraPayment_Amount CHECK (Amount > 0 AND Amount <= 1000000000000),
        CreatedAt  datetime2(0)   NOT NULL CONSTRAINT DF_LoanExtraPayment_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy  int            NOT NULL CONSTRAINT FK_LoanExtraPayment_CreatedBy REFERENCES app.AppUser (Id),
        UpdatedAt  datetime2(0)   NOT NULL CONSTRAINT DF_LoanExtraPayment_UpdatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedBy  int            NOT NULL CONSTRAINT FK_LoanExtraPayment_UpdatedBy REFERENCES app.AppUser (Id),
        CONSTRAINT PK_LoanExtraPayment PRIMARY KEY (LoanId, ExtraDate),
        CONSTRAINT FK_LoanExtraPayment_Loan FOREIGN KEY (LoanId, ProfileId) REFERENCES app.Loan (Id, ProfileId)
    );

IF OBJECT_ID(N'app.LoanMonthlyExtra', N'U') IS NULL
    CREATE TABLE app.LoanMonthlyExtra
    (
        Id         int            NOT NULL IDENTITY (1, 1) CONSTRAINT PK_LoanMonthlyExtra PRIMARY KEY,
        LoanId     int            NOT NULL,
        ProfileId  int            NOT NULL,
        Amount     decimal(19, 4) NOT NULL CONSTRAINT CK_LoanMonthlyExtra_Amount CHECK (Amount > 0 AND Amount <= 1000000000000),
        FromDate   date           NOT NULL,
        UntilDate  date           NULL,
        CreatedAt  datetime2(0)   NOT NULL CONSTRAINT DF_LoanMonthlyExtra_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy  int            NOT NULL CONSTRAINT FK_LoanMonthlyExtra_CreatedBy REFERENCES app.AppUser (Id),
        UpdatedAt  datetime2(0)   NOT NULL CONSTRAINT DF_LoanMonthlyExtra_UpdatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedBy  int            NOT NULL CONSTRAINT FK_LoanMonthlyExtra_UpdatedBy REFERENCES app.AppUser (Id),
        CONSTRAINT CK_LoanMonthlyExtra_Dates CHECK (UntilDate IS NULL OR UntilDate >= FromDate),
        CONSTRAINT FK_LoanMonthlyExtra_Loan FOREIGN KEY (LoanId, ProfileId) REFERENCES app.Loan (Id, ProfileId)
    );

IF OBJECT_ID(N'app.LoanBalance', N'U') IS NULL
    CREATE TABLE app.LoanBalance
    (
        LoanId      int            NOT NULL,
        BalanceDate date           NOT NULL,
        ProfileId   int            NOT NULL,
        Amount      decimal(19, 4) NOT NULL CONSTRAINT CK_LoanBalance_Amount CHECK (Amount >= 0 AND Amount <= 1000000000000),
        CreatedAt   datetime2(0)   NOT NULL CONSTRAINT DF_LoanBalance_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy   int            NOT NULL CONSTRAINT FK_LoanBalance_CreatedBy REFERENCES app.AppUser (Id),
        UpdatedAt   datetime2(0)   NOT NULL CONSTRAINT DF_LoanBalance_UpdatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedBy   int            NOT NULL CONSTRAINT FK_LoanBalance_UpdatedBy REFERENCES app.AppUser (Id),
        CONSTRAINT PK_LoanBalance PRIMARY KEY (LoanId, BalanceDate),
        CONSTRAINT FK_LoanBalance_Loan FOREIGN KEY (LoanId, ProfileId) REFERENCES app.Loan (Id, ProfileId)
    );
