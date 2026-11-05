using Microsoft.Data.SqlClient;

namespace FinanceGoals.Migrator.Tests;

[TestClass]
public sealed class DatabaseMigratorTests
{
    // Everything a migration can change: objects, principals, role membership, permissions,
    // the journals and the seeded rows. Two runs must leave all of it identical.
    // A contained database's catalog collation differs from its data collation, so every row is
    // converted to one collation before the UNION.
    private const string _snapshotSql = """
        SELECT CONVERT(nvarchar(600), row) COLLATE Latin1_General_100_BIN2 AS row
        FROM (
            SELECT CONCAT('object|', SCHEMA_NAME(o.schema_id), '.', o.name, '|', o.type, '|', CONVERT(varchar(30), o.modify_date, 126)) AS row
            FROM sys.objects AS o WHERE o.is_ms_shipped = 0
            UNION ALL
            SELECT CONCAT('principal|', p.name, '|', p.type)
            FROM sys.database_principals AS p WHERE p.principal_id > 4 AND p.is_fixed_role = 0
            UNION ALL
            SELECT CONCAT('member|', r.name, '|', m.name)
            FROM sys.database_role_members AS rm
            JOIN sys.database_principals AS r ON r.principal_id = rm.role_principal_id
            JOIN sys.database_principals AS m ON m.principal_id = rm.member_principal_id
            UNION ALL
            SELECT CONCAT('permission|', USER_NAME(dp.grantee_principal_id), '|', dp.permission_name, '|', dp.class_desc, ':', SCHEMA_NAME(dp.major_id))
            FROM sys.database_permissions AS dp WHERE dp.class = 3
            UNION ALL
            SELECT CONCAT('journal|SchemaVersions|', ScriptName) FROM dbo.SchemaVersions
            UNION ALL
            SELECT CONCAT('journal|SeedVersions|', ScriptName) FROM dbo.SeedVersions
            UNION ALL
            SELECT CONCAT('currency|', Code, '|', NumericCode, '|', MinorUnits, '|', Name) FROM app.Currency
        ) AS snapshot (row)
        ORDER BY 1;
        """;

    [TestMethod]
    public async Task Fresh_database_gets_every_script()
    {
        var database = await SqlServer.CreateDatabaseAsync();
        var migrator = new DatabaseMigrator(database, TextWriter.Null);

        var result = migrator.Run(appUser: null);

        result.Migrations.ShouldBe(migrator.EmbeddedScripts(DatabaseMigrator.Folder.Migrations));
        result.Repeatable.ShouldBe(migrator.EmbeddedScripts(DatabaseMigrator.Folder.Repeatable));
        result.Seeds.ShouldBe(migrator.EmbeddedScripts(DatabaseMigrator.Folder.Seed));
        (await SqlServer.QueryAsync(database, "SELECT Code FROM app.Currency ORDER BY Code"))
            .ShouldBe(["EUR", "NOK", "RON", "USD"]);
    }

    [TestMethod]
    public async Task Second_run_changes_nothing()
    {
        var database = await SqlServer.CreateDatabaseAsync();
        var appUser = AppUser.WithPassword("financegoals_app", SqlServer.NewPassword());
        var migrator = new DatabaseMigrator(database, TextWriter.Null);
        migrator.Run(appUser);
        var before = await SqlServer.QueryAsync(database, _snapshotSql);
        before.ShouldContain("member|app_data|financegoals_app");
        before.ShouldContain("permission|app_data|SELECT|SCHEMA:app");
        before.ShouldContain("journal|SeedVersions|seed/S0001_household_currencies.sql");
        before.ShouldContain("currency|NOK|578|2|Norwegian krone");

        var second = migrator.Run(appUser);

        second.Migrations.ShouldBeEmpty();
        second.Seeds.ShouldBeEmpty();
        (await SqlServer.QueryAsync(database, _snapshotSql)).ShouldBe(before);
    }

    [TestMethod]
    public async Task App_user_reads_and_writes_data_but_cannot_change_the_schema()
    {
        var database = await SqlServer.CreateDatabaseAsync();
        var password = SqlServer.NewPassword();
        new DatabaseMigrator(database, TextWriter.Null).Run(AppUser.WithPassword("financegoals_app", password));
        var app = SqlServer.As(database, "financegoals_app", password);

        (await SqlServer.QueryAsync(app, "SELECT COUNT(*) FROM app.Currency")).ShouldBe(["4"]);
        await SqlServer.ExecuteAsync(app, """
            INSERT INTO app.Currency (Code, NumericCode, MinorUnits, Name) VALUES ('SEK', 752, 2, N'Swedish krona');
            UPDATE app.Currency SET Name = N'Swedish krona' WHERE Code = 'SEK';
            DELETE FROM app.Currency WHERE Code = 'SEK';
            """);

        await Should.ThrowAsync<SqlException>(
            () => SqlServer.ExecuteAsync(app, "CREATE TABLE app.Sneaky (Id int);"));
        await Should.ThrowAsync<SqlException>(
            () => SqlServer.ExecuteAsync(app, "ALTER TABLE app.Currency ADD Sneaky int NULL;"));
        await Should.ThrowAsync<SqlException>(
            () => SqlServer.QueryAsync(app, "SELECT COUNT(*) FROM dbo.SchemaVersions"));
    }

    [TestMethod]
    public async Task Failing_migration_rolls_back_and_stops_the_run()
    {
        // TestScripts: 0001 succeeds, 0002 creates a table and then fails, 0003 must not run.
        var database = await SqlServer.CreateDatabaseAsync();
        var migrator = new DatabaseMigrator(database, TextWriter.Null, typeof(DatabaseMigratorTests).Assembly);

        var error = Should.Throw<MigrationException>(() => migrator.Run(appUser: null));

        error.Message.ShouldContain("0002_fails_halfway.sql");
        (await SqlServer.QueryAsync(database, "SELECT name FROM sys.tables WHERE name IN ('First', 'Second', 'Third') ORDER BY name"))
            .ShouldBe(["First"]);
        (await SqlServer.QueryAsync(database, "SELECT ScriptName FROM dbo.SchemaVersions"))
            .ShouldBe(["migrations/0001_creates_first.sql"]);
    }
}
