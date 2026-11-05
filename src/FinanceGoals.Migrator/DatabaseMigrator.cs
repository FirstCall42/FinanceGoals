using System.Reflection;
using DbUp;
using DbUp.Builder;
using DbUp.Helpers;
using Microsoft.Data.SqlClient;

namespace FinanceGoals.Migrator;

/// <summary>
/// Applies the embedded database scripts in four steps:
/// 1. migrations, journaled in dbo.SchemaVersions, each run once;
/// 2. the app user, created if missing and added to the app_data role;
/// 3. repeatable scripts (permissions, views, procedures), run every time and idempotent;
/// 4. seed data, journaled in dbo.SeedVersions, each run once.
/// Connects as the schema owner. Every script runs in its own transaction.
/// </summary>
internal sealed class DatabaseMigrator
{
    // Refuses any principal that is, or could be, more than a data user, so a configuration
    // mistake (e.g. naming the schema owner) fails instead of granting or resetting it.
    // Then creates the user if missing; for a password user, sets the configured password every
    // run so rotating it in configuration takes effect. Adding an existing member is a no-op.
    private const string _ensureAppUserSql = """
        DECLARE @sql nvarchar(max);
        DECLARE @id int = DATABASE_PRINCIPAL_ID(@name);

        IF @name IN (N'dbo', N'guest', N'sys', N'INFORMATION_SCHEMA', N'public') OR @name = USER_NAME()
            THROW 50001, N'The app user must be a dedicated user, not dbo, guest, a system user or the schema owner.', 1;

        IF @id IS NOT NULL AND (SELECT type FROM sys.database_principals WHERE principal_id = @id) NOT IN ('S', 'E', 'X')
            THROW 50002, N'The app user name belongs to a role or another kind of principal, not a user.', 1;

        IF EXISTS (SELECT 1 FROM sys.database_role_members AS rm
                   JOIN sys.database_principals AS r ON r.principal_id = rm.role_principal_id
                   WHERE rm.member_principal_id = @id AND r.name <> N'app_data')
           OR EXISTS (SELECT 1 FROM sys.database_permissions
                      WHERE grantee_principal_id = @id AND NOT (class = 0 AND permission_name = N'CONNECT'))
            THROW 50003, N'The app user already has other roles or permissions; it may only be a member of app_data.', 1;

        IF @id IS NULL
            SET @sql = N'CREATE USER ' + QUOTENAME(@name) +
                CASE WHEN @password IS NULL THEN N' FROM EXTERNAL PROVIDER'
                     ELSE N' WITH PASSWORD = ' + QUOTENAME(@password, N'''') END;
        ELSE IF @password IS NOT NULL
            SET @sql = N'ALTER USER ' + QUOTENAME(@name) + N' WITH PASSWORD = ' + QUOTENAME(@password, N'''');

        IF @sql IS NOT NULL
            EXEC (@sql);

        SET @sql = N'ALTER ROLE app_data ADD MEMBER ' + QUOTENAME(@name);
        EXEC (@sql);
        """;

    private readonly string _connectionString;
    private readonly Assembly _scripts;
    private readonly TextWriter _log;

    public DatabaseMigrator(string connectionString, TextWriter log)
        : this(connectionString, log, typeof(DatabaseMigrator).Assembly)
    {
    }

    internal DatabaseMigrator(string connectionString, TextWriter log, Assembly scripts)
    {
        ArgumentException.ThrowIfNullOrEmpty(connectionString);
        _connectionString = connectionString;
        _log = log;
        _scripts = scripts;
    }

    public MigrationResult Run(AppUser? appUser)
    {
        var migrations = Upgrade(Folder.Migrations, builder => builder.JournalToSqlTable("dbo", "SchemaVersions"));

        if (appUser is not null)
        {
            EnsureAppUser(appUser);
        }

        var repeatable = Upgrade(Folder.Repeatable, builder => builder.JournalTo(new NullJournal()));
        var seeds = Upgrade(Folder.Seed, builder => builder.JournalToSqlTable("dbo", "SeedVersions"));

        return new MigrationResult(migrations, repeatable, seeds);
    }

    internal IReadOnlyList<string> EmbeddedScripts(string folder) =>
        _scripts.GetManifestResourceNames()
            .Where(name => name.StartsWith(folder, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();

    private List<string> Upgrade(string folder, Func<UpgradeEngineBuilder, UpgradeEngineBuilder> journal)
    {
        _log.WriteLine($"== {folder}");
        var builder = DeployChanges.To
            .SqlDatabase(_connectionString)
            .WithScriptsEmbeddedInAssembly(_scripts, name => name.StartsWith(folder, StringComparison.Ordinal))
            .WithTransactionPerScript()
            // Scripts are plain T-SQL; '$' must never be treated as a DbUp variable.
            .WithVariablesDisabled()
            .LogToConsole();

        var result = journal(builder).Build().PerformUpgrade();
        if (!result.Successful)
        {
            throw new MigrationException($"{result.ErrorScript?.Name ?? folder} failed: {result.Error.Message}", result.Error);
        }

        return result.Scripts.Select(script => script.Name).ToList();
    }

    private void EnsureAppUser(AppUser appUser)
    {
        _log.WriteLine($"== app user {appUser.Name}");
        using var connection = new SqlConnection(_connectionString);
        connection.Open();
        using var command = new SqlCommand(_ensureAppUserSql, connection);
        command.Parameters.Add(new SqlParameter("@name", System.Data.SqlDbType.NVarChar, 128) { Value = appUser.Name });
        command.Parameters.Add(new SqlParameter("@password", System.Data.SqlDbType.NVarChar, 128)
        {
            Value = (object?)appUser.Password ?? DBNull.Value,
        });
        command.ExecuteNonQuery();
    }

    internal static class Folder
    {
        public const string Migrations = "migrations/";
        public const string Repeatable = "repeatable/";
        public const string Seed = "seed/";
    }
}

/// <summary>The scripts each step executed. Repeatable scripts always run.</summary>
internal sealed record MigrationResult(
    IReadOnlyList<string> Migrations,
    IReadOnlyList<string> Repeatable,
    IReadOnlyList<string> Seeds);

internal sealed class MigrationException : Exception
{
    public MigrationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
