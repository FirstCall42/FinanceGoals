-- Household profiles (#25): a profile owns all data; users belong to profiles as Owner or Member.
-- A user can belong to several profiles. Audit columns record who changed a row and when (#27).
-- Guarded so it is safe even if the journal is lost and the script runs again.

IF OBJECT_ID(N'app.Profile', N'U') IS NULL
    CREATE TABLE app.Profile
    (
        Id        int           NOT NULL IDENTITY (1, 1) CONSTRAINT PK_Profile PRIMARY KEY,
        Name      nvarchar(100) NOT NULL CONSTRAINT CK_Profile_Name CHECK (LEN(Name) > 0),
        CreatedAt datetime2(0)  NOT NULL CONSTRAINT DF_Profile_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy int           NOT NULL CONSTRAINT FK_Profile_CreatedBy REFERENCES app.AppUser (Id),
        UpdatedAt datetime2(0)  NOT NULL CONSTRAINT DF_Profile_UpdatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedBy int           NOT NULL CONSTRAINT FK_Profile_UpdatedBy REFERENCES app.AppUser (Id)
    );

IF OBJECT_ID(N'app.ProfileMember', N'U') IS NULL
    CREATE TABLE app.ProfileMember
    (
        ProfileId int          NOT NULL CONSTRAINT FK_ProfileMember_Profile REFERENCES app.Profile (Id),
        UserId    int          NOT NULL CONSTRAINT FK_ProfileMember_User REFERENCES app.AppUser (Id),
        Role      varchar(10)  NOT NULL CONSTRAINT CK_ProfileMember_Role CHECK (Role IN ('Owner', 'Member')),
        CreatedAt datetime2(0) NOT NULL CONSTRAINT DF_ProfileMember_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy int          NOT NULL CONSTRAINT FK_ProfileMember_CreatedBy REFERENCES app.AppUser (Id),
        UpdatedAt datetime2(0) NOT NULL CONSTRAINT DF_ProfileMember_UpdatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedBy int          NOT NULL CONSTRAINT FK_ProfileMember_UpdatedBy REFERENCES app.AppUser (Id),
        CONSTRAINT PK_ProfileMember PRIMARY KEY (ProfileId, UserId)
    );

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ProfileMember_UserId' AND object_id = OBJECT_ID(N'app.ProfileMember'))
    CREATE INDEX IX_ProfileMember_UserId ON app.ProfileMember (UserId);
