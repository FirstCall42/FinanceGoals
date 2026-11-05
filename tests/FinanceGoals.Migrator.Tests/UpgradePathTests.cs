using System.Diagnostics;

namespace FinanceGoals.Migrator.Tests;

/// <summary>
/// A database built by the base branch's migrator and then upgraded by this branch's migrator
/// must match a fresh build by this branch's migrator. CI builds the base migrator and points
/// FINANCEGOALS_BASE_MIGRATOR at its FinanceGoals.Migrator.dll.
/// </summary>
[TestClass]
public sealed class UpgradePathTests
{
    private const string _baseMigratorVariable = "FINANCEGOALS_BASE_MIGRATOR";
    private const string _requireVariable = "FINANCEGOALS_REQUIRE_UPGRADE_CHECK";
    private const string _appUser = "financegoals_app";

    [TestMethod]
    public async Task Upgrading_from_the_base_matches_a_fresh_build()
    {
        var baseMigrator = Environment.GetEnvironmentVariable(_baseMigratorVariable);
        if (string.IsNullOrEmpty(baseMigrator))
        {
            if (Environment.GetEnvironmentVariable(_requireVariable) == "1")
            {
                Assert.Fail($"{_requireVariable} is set but {_baseMigratorVariable} is not.");
            }

            Assert.Inconclusive($"Set {_baseMigratorVariable} to the base branch's FinanceGoals.Migrator.dll to run this check.");
        }

        var password = SqlServer.NewPassword();

        var upgraded = await SqlServer.CreateDatabaseAsync();
        await RunBaseMigratorAsync(baseMigrator, upgraded, password);
        new DatabaseMigrator(upgraded, TextWriter.Null).Run(AppUser.WithPassword(_appUser, password));

        var fresh = await SqlServer.CreateDatabaseAsync();
        new DatabaseMigrator(fresh, TextWriter.Null).Run(AppUser.WithPassword(_appUser, password));

        var expected = await SchemaSnapshot.TakeAsync(fresh, includeTimestamps: false);
        var actual = await SchemaSnapshot.TakeAsync(upgraded, includeTimestamps: false);
        actual.Except(expected).ShouldBeEmpty("only in the upgraded database");
        expected.Except(actual).ShouldBeEmpty("only in the fresh build");
    }

    private static async Task RunBaseMigratorAsync(string baseMigrator, string connectionString, string password)
    {
        var start = new ProcessStartInfo("dotnet", [baseMigrator])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.Environment["ConnectionStrings__SchemaOwner"] = connectionString;
        start.Environment["Migrator__AppUser"] = _appUser;
        start.Environment["Migrator__AppUserKind"] = "password";
        start.Environment["Migrator__AppUserPassword"] = password;

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the base migrator.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        await process.WaitForExitAsync(timeout.Token);

        process.ExitCode.ShouldBe(0, $"Base migrator failed:\n{await output}\n{await error}");
    }
}
