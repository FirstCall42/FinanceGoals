using System.Text.RegularExpressions;

namespace FinanceGoals.Migrator.Tests;

/// <summary>Script files follow the naming scheme and every one of them ships in the migrator.</summary>
[TestClass]
public sealed partial class ScriptConventionTests
{
    private static readonly DatabaseMigrator _migrator = new("Server=unused", TextWriter.Null);

    [TestMethod]
    [DataRow("migrations")]
    [DataRow("repeatable")]
    [DataRow("seed")]
    public void Every_script_on_disk_is_embedded(string folder)
    {
        var onDisk = Directory.GetFiles(Path.Combine(RepositoryRoot(), "db", folder), "*.sql")
            .Select(path => $"{folder}/{Path.GetFileName(path)}")
            .Order(StringComparer.Ordinal);

        _migrator.EmbeddedScripts($"{folder}/").ShouldBe(onDisk);
    }

    [TestMethod]
    [DataRow("migrations", @"^migrations/\d{4}_[a-z0-9_]+\.sql$")]
    [DataRow("repeatable", @"^repeatable/R\d{3}_[a-z0-9_]+\.sql$")]
    [DataRow("seed", @"^seed/S\d{4}_[a-z0-9_]+\.sql$")]
    public void Script_names_are_numbered_and_unique(string folder, string pattern)
    {
        var scripts = _migrator.EmbeddedScripts($"{folder}/");

        scripts.ShouldNotBeEmpty();
        scripts.ShouldAllBe(name => Regex.IsMatch(name, pattern));
        scripts.Select(name => NumberPrefix().Match(name).Value).ShouldBeUnique();
    }

    [TestMethod]
    [DataRow("financegoals_app")]
    [DataRow("financegoals-api-test")]
    [DataRow("id.financegoals")]
    public void App_user_accepts_sql_and_entra_names(string name)
    {
        AppUser.FromExternalProvider(name).Name.ShouldBe(name);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("app]; DROP USER dbo; --")]
    [DataRow("1app")]
    [DataRow("app user")]
    public void App_user_rejects_names_that_are_not_plain_identifiers(string name)
    {
        Should.Throw<ArgumentException>(() => AppUser.FromExternalProvider(name));
    }

    [TestMethod]
    public void App_user_rejects_empty_and_overlong_passwords()
    {
        Should.Throw<ArgumentException>(() => AppUser.WithPassword("financegoals_app", ""));
        Should.Throw<ArgumentException>(() => AppUser.WithPassword("financegoals_app", new string('x', 129)));
    }

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "FinanceGoals.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("FinanceGoals.slnx not found.");
    }

    [GeneratedRegex(@"[RS]?\d+")]
    private static partial Regex NumberPrefix();
}
