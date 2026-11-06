-- Loans of a household (#107): the loan, its periods (rate type and repayment type) and the dated
-- rate changes of each period, so the schedule engine can work from saved data. Extra payments and
-- the actual balance follow in later scripts. Every table carries ProfileId, and the child tables
-- reference the loan together with it, so a row can never point at another household's loan.
-- Guarded so it is safe even if the journal is lost and the script runs again.

IF OBJECT_ID(N'app.Loan', N'U') IS NULL
    CREATE TABLE app.Loan
    (
        Id               int            NOT NULL IDENTITY (1, 1) CONSTRAINT PK_Loan PRIMARY KEY,
        ProfileId        int            NOT NULL CONSTRAINT FK_Loan_Profile REFERENCES app.Profile (Id),
        Name             nvarchar(100)  NOT NULL CONSTRAINT CK_Loan_Name CHECK (LEN(Name) > 0),
        CurrencyCode     char(3)        NOT NULL CONSTRAINT FK_Loan_Currency REFERENCES app.Currency (Code),
        Principal        decimal(19, 4) NOT NULL CONSTRAINT CK_Loan_Principal CHECK (Principal > 0 AND Principal <= 1000000000000),
        PaidOut          date           NOT NULL,
        FirstPaymentDate date           NOT NULL,
        TermMonths       int            NOT NULL CONSTRAINT CK_Loan_TermMonths CHECK (TermMonths BETWEEN 1 AND 600),
        FeePerPayment    decimal(19, 4) NOT NULL CONSTRAINT DF_Loan_FeePerPayment DEFAULT 0 CONSTRAINT CK_Loan_FeePerPayment CHECK (FeePerPayment >= 0),
        DayCount         varchar(12)    NOT NULL CONSTRAINT CK_Loan_DayCount CHECK (DayCount IN ('ActualActual', 'Thirty360')),
        CreatedAt        datetime2(0)   NOT NULL CONSTRAINT DF_Loan_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy        int            NOT NULL CONSTRAINT FK_Loan_CreatedBy REFERENCES app.AppUser (Id),
        UpdatedAt        datetime2(0)   NOT NULL CONSTRAINT DF_Loan_UpdatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedBy        int            NOT NULL CONSTRAINT FK_Loan_UpdatedBy REFERENCES app.AppUser (Id),
        CONSTRAINT UQ_Loan_ProfileId_Name UNIQUE (ProfileId, Name),
        CONSTRAINT UQ_Loan_Id_ProfileId UNIQUE (Id, ProfileId),
        CONSTRAINT CK_Loan_PaidOut_BeforeFirstPayment CHECK (PaidOut < FirstPaymentDate)
    );

IF OBJECT_ID(N'app.LoanPeriod', N'U') IS NULL
    CREATE TABLE app.LoanPeriod
    (
        LoanId        int          NOT NULL,
        StartDate     date         NOT NULL,
        ProfileId     int          NOT NULL,
        RateType      varchar(10)  NOT NULL CONSTRAINT CK_LoanPeriod_RateType CHECK (RateType IN ('Variable', 'Fixed')),
        RepaymentType varchar(12)  NOT NULL CONSTRAINT CK_LoanPeriod_RepaymentType CHECK (RepaymentType IN ('Annuity', 'StraightLine', 'InterestOnly')),
        CreatedAt     datetime2(0) NOT NULL CONSTRAINT DF_LoanPeriod_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy     int          NOT NULL CONSTRAINT FK_LoanPeriod_CreatedBy REFERENCES app.AppUser (Id),
        UpdatedAt     datetime2(0) NOT NULL CONSTRAINT DF_LoanPeriod_UpdatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedBy     int          NOT NULL CONSTRAINT FK_LoanPeriod_UpdatedBy REFERENCES app.AppUser (Id),
        CONSTRAINT PK_LoanPeriod PRIMARY KEY (LoanId, StartDate),
        CONSTRAINT FK_LoanPeriod_Loan FOREIGN KEY (LoanId, ProfileId) REFERENCES app.Loan (Id, ProfileId)
    );

IF OBJECT_ID(N'app.LoanRate', N'U') IS NULL
    CREATE TABLE app.LoanRate
    (
        LoanId        int           NOT NULL,
        EffectiveFrom date          NOT NULL,
        ProfileId     int           NOT NULL,
        PeriodStart   date          NOT NULL,
        AnnualRate    decimal(9, 6) NOT NULL CONSTRAINT CK_LoanRate_AnnualRate CHECK (AnnualRate BETWEEN 0 AND 1),
        CreatedAt     datetime2(0)  NOT NULL CONSTRAINT DF_LoanRate_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy     int           NOT NULL CONSTRAINT FK_LoanRate_CreatedBy REFERENCES app.AppUser (Id),
        UpdatedAt     datetime2(0)  NOT NULL CONSTRAINT DF_LoanRate_UpdatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedBy     int           NOT NULL CONSTRAINT FK_LoanRate_UpdatedBy REFERENCES app.AppUser (Id),
        CONSTRAINT PK_LoanRate PRIMARY KEY (LoanId, EffectiveFrom),
        CONSTRAINT FK_LoanRate_Loan FOREIGN KEY (LoanId, ProfileId) REFERENCES app.Loan (Id, ProfileId),
        CONSTRAINT FK_LoanRate_Period FOREIGN KEY (LoanId, PeriodStart) REFERENCES app.LoanPeriod (LoanId, StartDate)
    );
