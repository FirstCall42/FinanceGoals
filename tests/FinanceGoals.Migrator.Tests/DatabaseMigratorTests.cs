using Microsoft.Data.SqlClient;

namespace FinanceGoals.Migrator.Tests;

[TestClass]
public sealed class DatabaseMigratorTests
{
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
        var before = await SchemaSnapshot.TakeAsync(database, includeTimestamps: true);
        before.ShouldContain("member|app_data|financegoals_app");
        before.ShouldContain("permission|app_data|SELECT|GRANT|SCHEMA:app|grantor=dbo");
        before.ShouldContain("journal|SeedVersions|seed/S0001_household_currencies.sql");
        before.ShouldContain(row => row.StartsWith("column|app.Currency.Code|position=1|char(3,", StringComparison.Ordinal));
        before.ShouldContain(row => row.StartsWith("data|[app].[Currency]|4|", StringComparison.Ordinal));

        var second = migrator.Run(appUser);

        second.Migrations.ShouldBeEmpty();
        second.Seeds.ShouldBeEmpty();
        (await SchemaSnapshot.TakeAsync(database, includeTimestamps: true)).ShouldBe(before);
    }

    [TestMethod]
    public async Task Migrations_and_seeds_are_guarded_against_running_again()
    {
        // The journal normally stops a script from running twice. If the journal is lost or a
        // script is renamed, the script's own guards must make a second run change nothing.
        var database = await SqlServer.CreateDatabaseAsync();
        new DatabaseMigrator(database, TextWriter.Null).Run(AppUser.WithPassword("financegoals_app", SqlServer.NewPassword()));
        var before = await SchemaSnapshot.TakeAsync(database, includeTimestamps: true);

        var rerun = DbUp.DeployChanges.To
            .SqlDatabase(database)
            .WithScriptsEmbeddedInAssembly(
                typeof(DatabaseMigrator).Assembly,
                name => name.StartsWith(DatabaseMigrator.Folder.Migrations, StringComparison.Ordinal)
                    || name.StartsWith(DatabaseMigrator.Folder.Seed, StringComparison.Ordinal))
            .JournalTo(new DbUp.Helpers.NullJournal())
            .WithTransactionPerScript()
            .WithVariablesDisabled()
            .LogToNowhere()
            .Build()
            .PerformUpgrade();

        rerun.Successful.ShouldBeTrue(rerun.Error?.Message);
        rerun.Scripts.Count().ShouldBe(
            new DatabaseMigrator(database, TextWriter.Null).EmbeddedScripts(DatabaseMigrator.Folder.Migrations).Count
            + new DatabaseMigrator(database, TextWriter.Null).EmbeddedScripts(DatabaseMigrator.Folder.Seed).Count);
        (await SchemaSnapshot.TakeAsync(database, includeTimestamps: true)).ShouldBe(before);
    }

    [TestMethod]
    public async Task Rotating_the_app_user_password_takes_effect()
    {
        var database = await SqlServer.CreateDatabaseAsync();
        var migrator = new DatabaseMigrator(database, TextWriter.Null);
        migrator.Run(AppUser.WithPassword("financegoals_app", SqlServer.NewPassword()));
        var rotated = SqlServer.NewPassword();

        migrator.Run(AppUser.WithPassword("financegoals_app", rotated));

        (await SqlServer.QueryAsync(SqlServer.As(database, "financegoals_app", rotated), "SELECT COUNT(*) FROM app.Currency"))
            .ShouldBe(["4"]);
    }

    // Each row expects the guard's own error number, so a row cannot pass because some other
    // statement failed first (e.g. ALTER USER dbo WITH PASSWORD).
    [TestMethod]
    [DataRow("schema_owner", null, 50001)]
    [DataRow("dbo", null, 50001)]
    [DataRow("app_data", null, 50002)]
    [DataRow("reporting", "CREATE USER reporting WITHOUT LOGIN; ALTER ROLE db_datareader ADD MEMBER reporting;", 50003)]
    [DataRow("auditor", "CREATE USER auditor WITHOUT LOGIN; GRANT VIEW DEFINITION TO auditor;", 50003)]
    [DataRow("keeper", "CREATE USER keeper WITHOUT LOGIN; EXEC (N'CREATE SCHEMA kept AUTHORIZATION keeper');", 50003)]
    [DataRow("tableowner", "CREATE USER tableowner WITHOUT LOGIN; ALTER AUTHORIZATION ON OBJECT::app.Currency TO tableowner;", 50003)]
    [DataRow("roleowner", "CREATE USER roleowner WITHOUT LOGIN; CREATE ROLE owned AUTHORIZATION roleowner;", 50003)]
    public async Task Refuses_an_app_user_with_more_than_data_access(string name, string? setup, int error)
    {
        var database = await SqlServer.CreateDatabaseAsync();
        var migrator = new DatabaseMigrator(database, TextWriter.Null);
        migrator.Run(appUser: null);
        if (setup is not null)
        {
            await SqlServer.ExecuteAsync(database, setup);
        }

        Should.Throw<SqlException>(() => migrator.Run(AppUser.WithPassword(name, SqlServer.NewPassword())))
            .Number.ShouldBe(error);

        (await AppDataMembersAsync(database)).ShouldBeEmpty();
    }

    [TestMethod]
    public async Task Refuses_the_connecting_user_even_when_it_is_not_in_db_owner()
    {
        // A deployer with just enough rights to manage users and no roles. Without the USER_NAME()
        // check it would be refused with 50003 for its own grants; 50001 shows that check fired.
        var database = await SqlServer.CreateDatabaseAsync();
        new DatabaseMigrator(database, TextWriter.Null).Run(appUser: null);
        var password = SqlServer.NewPassword();
        await SqlServer.ExecuteAsync(database, $"""
            CREATE USER deployer WITH PASSWORD = '{password}';
            GRANT ALTER ANY USER TO deployer;
            GRANT ALTER ON ROLE::app_data TO deployer;
            """);
        var deployer = new DatabaseMigrator(SqlServer.As(database, "deployer", password), TextWriter.Null);

        Should.Throw<SqlException>(() => deployer.EnsureAppUser(AppUser.WithPassword("deployer", SqlServer.NewPassword())))
            .Number.ShouldBe(50001);

        (await AppDataMembersAsync(database)).ShouldBeEmpty();
        (await SqlServer.QueryAsync(SqlServer.As(database, "deployer", password), "SELECT USER_NAME()")).ShouldBe(["deployer"]);
    }

    [TestMethod]
    public async Task A_deployer_without_db_owner_can_create_the_app_user()
    {
        // The positive case for the test above: the same deployer may create another user.
        var database = await SqlServer.CreateDatabaseAsync();
        new DatabaseMigrator(database, TextWriter.Null).Run(appUser: null);
        var password = SqlServer.NewPassword();
        await SqlServer.ExecuteAsync(database, $"""
            CREATE USER deployer WITH PASSWORD = '{password}';
            GRANT ALTER ANY USER TO deployer;
            GRANT ALTER ON ROLE::app_data TO deployer;
            """);

        new DatabaseMigrator(SqlServer.As(database, "deployer", password), TextWriter.Null)
            .EnsureAppUser(AppUser.WithPassword("financegoals_app", SqlServer.NewPassword()));

        (await AppDataMembersAsync(database)).ShouldBe(["financegoals_app"]);
    }

    [TestMethod]
    public async Task An_existing_user_with_another_identity_is_refused()
    {
        // An Entra app user is created from its client ID, which becomes its SID. If a user with
        // that name already exists with another SID, another identity holds the name and must not
        // be granted app_data. (TYPE = E users exist only in Azure SQL; any other user with the
        // name has a different SID, which is what this checks. The matching case runs in Azure.)
        var database = await SqlServer.CreateDatabaseAsync();
        var migrator = new DatabaseMigrator(database, TextWriter.Null);
        migrator.Run(appUser: null);
        await SqlServer.ExecuteAsync(database, $"CREATE USER [id-financegoals-api] WITH PASSWORD = '{SqlServer.NewPassword()}';");

        Should.Throw<SqlException>(() => migrator.EnsureAppUser(AppUser.FromEntra("id-financegoals-api", Guid.NewGuid())))
            .Number.ShouldBe(50004);

        (await AppDataMembersAsync(database)).ShouldBeEmpty();
    }

    [TestMethod]
    public async Task Every_constraint_is_named()
    {
        // SQL Server gives an unnamed constraint a random name, so an upgraded database and a
        // fresh build would differ, and later scripts could not refer to it by name.
        var database = await SqlServer.CreateDatabaseAsync();
        new DatabaseMigrator(database, TextWriter.Null).Run(appUser: null);

        (await SqlServer.QueryAsync(database, """
            SELECT OBJECT_SCHEMA_NAME(c.parent_object_id) + '.' + OBJECT_NAME(c.parent_object_id), c.type_desc
            FROM (
                SELECT parent_object_id, type_desc, is_system_named FROM sys.check_constraints
                UNION ALL SELECT parent_object_id, type_desc, is_system_named FROM sys.default_constraints
                UNION ALL SELECT parent_object_id, type_desc, is_system_named FROM sys.key_constraints
                UNION ALL SELECT parent_object_id, type_desc, is_system_named FROM sys.foreign_keys
            ) AS c
            WHERE c.is_system_named = 1
            """)).ShouldBeEmpty();
    }

    [TestMethod]
    public async Task App_data_role_has_no_database_level_permissions()
    {
        var database = await SqlServer.CreateDatabaseAsync();
        new DatabaseMigrator(database, TextWriter.Null).Run(appUser: null);

        (await SqlServer.QueryAsync(database, """
            SELECT CONCAT(class_desc, ' ', permission_name)
            FROM sys.database_permissions
            WHERE grantee_principal_id = DATABASE_PRINCIPAL_ID(N'app_data') AND class <> 3
            """)).ShouldBeEmpty();
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

    private static Task<List<string>> AppDataMembersAsync(string database) =>
        SqlServer.QueryAsync(database, """
            SELECT USER_NAME(member_principal_id) FROM sys.database_role_members
            WHERE role_principal_id = DATABASE_PRINCIPAL_ID(N'app_data')
            ORDER BY 1
            """);
}
