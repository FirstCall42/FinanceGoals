-- Invitations to a household profile (#26). The link carries a random token; only its SHA-256
-- hash is stored, so the table can't be used to join a profile. An invite is for one email
-- address, works once, and expires after 7 days. Guarded so it is safe even if the journal is
-- lost and the script runs again.

IF OBJECT_ID(N'app.ProfileInvite', N'U') IS NULL
    CREATE TABLE app.ProfileInvite
    (
        Id         int           NOT NULL IDENTITY (1, 1) CONSTRAINT PK_ProfileInvite PRIMARY KEY,
        ProfileId  int           NOT NULL CONSTRAINT FK_ProfileInvite_Profile REFERENCES app.Profile (Id),
        Email      nvarchar(320) NOT NULL,
        TokenHash  binary(32)    NOT NULL CONSTRAINT UQ_ProfileInvite_TokenHash UNIQUE,
        ExpiresAt  datetime2(0)  NOT NULL,
        AcceptedAt datetime2(0)  NULL,
        AcceptedBy int           NULL CONSTRAINT FK_ProfileInvite_AcceptedBy REFERENCES app.AppUser (Id),
        CreatedAt  datetime2(0)  NOT NULL CONSTRAINT DF_ProfileInvite_CreatedAt DEFAULT SYSUTCDATETIME(),
        CreatedBy  int           NOT NULL CONSTRAINT FK_ProfileInvite_CreatedBy REFERENCES app.AppUser (Id),
        UpdatedAt  datetime2(0)  NOT NULL CONSTRAINT DF_ProfileInvite_UpdatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedBy  int           NOT NULL CONSTRAINT FK_ProfileInvite_UpdatedBy REFERENCES app.AppUser (Id)
    );

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ProfileInvite_ProfileId' AND object_id = OBJECT_ID(N'app.ProfileInvite'))
    CREATE INDEX IX_ProfileInvite_ProfileId ON app.ProfileInvite (ProfileId);
