using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;

namespace FinanceGoals.Migrator.Tests;

/// <summary>
/// Checks this branch's migrator against the base branch's. CI builds the base migrator and
/// points FINANCEGOALS_BASE_MIGRATOR at its FinanceGoals.Migrator.dll. There are no releases
/// yet, so the base branch stands in for the last release (#21 revisits multi-version jumps).
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
        var baseMigrator = BaseMigrator();
        var password = SqlServer.NewPassword();

        var upgraded = await SqlServer.CreateDatabaseAsync();
        var result = await MigratorProcess.RunAsync(baseMigrator, new Dictionary<string, string>
        {
            ["ConnectionStrings__SchemaOwner"] = upgraded,
            ["Migrator__AppUser"] = _appUser,
            ["Migrator__AppUserKind"] = "password",
            ["Migrator__AppUserPassword"] = password,
        });
        result.ExitCode.ShouldBe(0, $"Base migrator failed: {result}");
        new DatabaseMigrator(upgraded, TextWriter.Null).Run(AppUser.WithPassword(_appUser, password));

        var fresh = await SqlServer.CreateDatabaseAsync();
        new DatabaseMigrator(fresh, TextWriter.Null).Run(AppUser.WithPassword(_appUser, password));

        var expected = await SchemaSnapshot.TakeAsync(fresh, includeTimestamps: false);
        var actual = await SchemaSnapshot.TakeAsync(upgraded, includeTimestamps: false);
        actual.Except(expected).ShouldBeEmpty("only in the upgraded database");
        expected.Except(actual).ShouldBeEmpty("only in the fresh build");
        actual.ShouldBe(expected, "same rows, including duplicates");
    }

    [TestMethod]
    public void Merged_migrations_and_seeds_ship_unchanged()
    {
        // The CI path guard looks at db/migrations and db/seed. This compares what the migrators
        // actually embed, so a csproj that embeds a different file under the same name fails too.
        var baseScripts = EmbeddedScripts(BaseMigrator());
        var currentScripts = EmbeddedScripts(MigratorProcess.Current);

        var changed = baseScripts
            .Where(script => !currentScripts.TryGetValue(script.Key, out var hash) || hash != script.Value)
            .Select(script => script.Key)
            .ToList();

        baseScripts.ShouldNotBeEmpty();
        changed.ShouldBeEmpty("merged scripts that were changed, renamed or removed");
    }

    private static string BaseMigrator()
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

        return baseMigrator;
    }

    /// <summary>SHA-256 of every embedded migration and seed script, by resource name.</summary>
    private static Dictionary<string, string> EmbeddedScripts(string migratorDll)
    {
        // A separate, collectible context: the base build has the same assembly name as ours.
        var context = new AssemblyLoadContext($"scripts:{migratorDll}", isCollectible: true);
        try
        {
            var assembly = context.LoadFromAssemblyPath(Path.GetFullPath(migratorDll));
            return assembly.GetManifestResourceNames()
                .Where(name => name.StartsWith(DatabaseMigrator.Folder.Migrations, StringComparison.Ordinal)
                    || name.StartsWith(DatabaseMigrator.Folder.Seed, StringComparison.Ordinal))
                .ToDictionary(name => name, name => Hash(assembly, name), StringComparer.Ordinal);
        }
        finally
        {
            context.Unload();
        }
    }

    private static string Hash(Assembly assembly, string resource)
    {
        using var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Resource {resource} not found.");
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
