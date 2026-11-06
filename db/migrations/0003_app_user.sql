-- People who have signed in with Google (#24). GoogleSubject is Google's stable id for the account
-- (the token's "sub"); email and name are refreshed at each sign-in. Household profiles (#25) link
-- users to their data. Guarded so it is safe even if the journal is lost and the script runs again.

IF OBJECT_ID(N'app.AppUser', N'U') IS NULL
    CREATE TABLE app.AppUser
    (
        Id            int           NOT NULL IDENTITY (1, 1) CONSTRAINT PK_AppUser PRIMARY KEY,
        GoogleSubject varchar(255)  NOT NULL CONSTRAINT UQ_AppUser_GoogleSubject UNIQUE,
        Email         nvarchar(320) NOT NULL,
        Name          nvarchar(200) NULL,
        CreatedAt     datetime2(0)  NOT NULL CONSTRAINT DF_AppUser_CreatedAt DEFAULT SYSUTCDATETIME(),
        LastSignInAt  datetime2(0)  NOT NULL CONSTRAINT DF_AppUser_LastSignInAt DEFAULT SYSUTCDATETIME()
    );
