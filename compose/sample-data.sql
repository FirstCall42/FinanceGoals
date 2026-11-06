-- Sample data for the LOCAL stack only (compose.yaml). This is not a DbUp script, so it never runs
-- in the cloud. It is safe to run again: every insert checks first. Features that add tables
-- (accounts, loans, ...) extend this file so a fresh `docker compose up` shows something.
--
-- The sample user can't sign in (no Google account matches), so sign in with your own account to
-- get your own household; this one is there to show what a populated database looks like.

SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF NOT EXISTS (SELECT 1 FROM app.AppUser WHERE GoogleSubject = 'sample-user')
    INSERT INTO app.AppUser (GoogleSubject, Email, Name) VALUES ('sample-user', 'sample@example.com', 'Sample User');

DECLARE @UserId int = (SELECT Id FROM app.AppUser WHERE GoogleSubject = 'sample-user');

IF NOT EXISTS (SELECT 1 FROM app.ProfileMember WHERE UserId = @UserId)
BEGIN
    INSERT INTO app.Profile (Name, CreatedBy, UpdatedBy) VALUES (N'Sample household', @UserId, @UserId);
    INSERT INTO app.ProfileMember (ProfileId, UserId, Role, CreatedBy, UpdatedBy)
    VALUES (SCOPE_IDENTITY(), @UserId, 'Owner', @UserId, @UserId);
END;

COMMIT TRANSACTION;
