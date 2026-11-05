namespace FinanceGoals.Migrator.Tests;

/// <summary>The migrator executable, run as a process with environment settings.</summary>
[TestClass]
public sealed class ProgramTests
{
    [TestMethod]
    public async Task Without_an_app_user_the_step_is_skipped_and_logged()
    {
        var database = await SqlServer.CreateDatabaseAsync();

        var result = await MigratorProcess.RunAsync(MigratorProcess.Current, new Dictionary<string, string>
        {
            ["ConnectionStrings__SchemaOwner"] = database,
        });

        result.ExitCode.ShouldBe(0, result.ToString());
        result.Output.ShouldContain("No Migrator__AppUser set; skipping the app user step.");
        result.Output.ShouldNotContain("WARNING");
    }

    [TestMethod]
    [DataRow("Migrator__AppUserKind", "password")]
    [DataRow("Migrator__AppUserPassword", "Pw1!x")]
    [DataRow("Migrator__AppUserClientId", "7cf7c15a-2f50-4610-bd13-86a3012da730")]
    public async Task App_user_settings_without_an_app_user_log_a_warning(string setting, string value)
    {
        var database = await SqlServer.CreateDatabaseAsync();

        var result = await MigratorProcess.RunAsync(MigratorProcess.Current, new Dictionary<string, string>
        {
            ["ConnectionStrings__SchemaOwner"] = database,
            [setting] = value,
        });

        result.ExitCode.ShouldBe(0, result.ToString());
        result.Output.ShouldContain(
            "WARNING: Migrator__AppUserKind, Migrator__AppUserPassword or Migrator__AppUserClientId is set but Migrator__AppUser is not; no app user step.");
        (await SqlServer.QueryAsync(database, """
            SELECT COUNT(*) FROM sys.database_role_members WHERE role_principal_id = DATABASE_PRINCIPAL_ID(N'app_data')
            """)).ShouldBe(["0"]);
    }

    [TestMethod]
    public async Task Missing_connection_string_exits_with_2()
    {
        var result = await MigratorProcess.RunAsync(MigratorProcess.Current, new Dictionary<string, string>());

        result.ExitCode.ShouldBe(2, result.ToString());
        result.Error.ShouldContain("ConnectionStrings__SchemaOwner is not set.");
    }
}
