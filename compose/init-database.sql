-- Local stack only (compose.yaml): a contained database, so the migrator can create the app user
-- with a password. In Azure the database comes from infra/ and users sign in with Entra ID.
EXEC sp_configure 'contained database authentication', 1;
RECONFIGURE;
IF DB_ID(N'FinanceGoals') IS NULL
    CREATE DATABASE FinanceGoals CONTAINMENT = PARTIAL;
