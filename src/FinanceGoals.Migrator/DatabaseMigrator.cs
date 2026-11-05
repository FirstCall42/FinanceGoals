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
    // Creates the user if missing; for a password user, sets the configured password every run
    // so rotating it in configuration takes effect. Adding an existing member is a no-op.
    private const string _ensureAppUserSql = """
        DECLARE @sql nvarchar(max);

        IF DATABASE_PRINCIPAL_ID(@name) IS NULL
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
