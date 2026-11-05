using System.Reflection;
using System.Xml.Linq;
using NetArchTest.Rules;

namespace FinanceGoals.Architecture.Tests;

/// <summary>
/// Each layer may depend only on the layers below it:
/// Domain on nothing, Application on Domain, Infrastructure on Application,
/// Api on Application and Infrastructure. The Migrator stands alone.
/// </summary>
[TestClass]
public sealed class LayerDependencyTests
{
    private const string _domain = "FinanceGoals.Domain";
    private const string _application = "FinanceGoals.Application";
    private const string _infrastructure = "FinanceGoals.Infrastructure";
    private const string _api = "FinanceGoals.Api";
    private const string _migrator = "FinanceGoals.Migrator";

    private static readonly string[] _allProjects = [_domain, _application, _infrastructure, _api, _migrator];

    private static readonly Dictionary<string, string[]> _allowedReferences = new()
    {
        [_domain] = [],
        [_application] = [_domain],
        [_infrastructure] = [_application],
        [_api] = [_application, _infrastructure],
        [_migrator] = [],
    };

    // A project reference is what the build allows; a type dependency is what the code uses.
    // Checking both catches a forbidden reference before any code starts using it.

    [TestMethod]
    [DataRow(_domain)]
    [DataRow(_application)]
    [DataRow(_infrastructure)]
    [DataRow(_api)]
    [DataRow(_migrator)]
    public void Project_references_only_the_layers_below_it(string project)
    {
        var forbidden = ProjectReferences(project).Except(_allowedReferences[project]).ToList();

        forbidden.ShouldBeEmpty($"{project} references {string.Join(", ", forbidden)}");
    }

    [TestMethod]
    [DataRow(_domain)]
    [DataRow(_application)]
    [DataRow(_infrastructure)]
    [DataRow(_api)]
    [DataRow(_migrator)]
    public void Code_depends_only_on_the_layers_below_it(string project)
    {
        var forbidden = _allProjects
            .Where(other => other != project && !_allowedReferences[project].Contains(other))
            .ToArray();

        var result = Types.InAssembly(Assembly.Load(project))
            .ShouldNot().HaveDependencyOnAny(forbidden)
            .GetResult();

        (result.FailingTypeNames ?? []).ShouldBeEmpty(
            $"{project} types depend on one of {string.Join(", ", forbidden)}");
    }

    [TestMethod]
    public void Reference_check_reports_a_reference_that_is_not_allowed()
    {
        var csproj = XDocument.Parse("""
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <ProjectReference Include="..\FinanceGoals.Application\FinanceGoals.Application.csproj" />
              </ItemGroup>
            </Project>
            """);

        ProjectReferences(csproj).Except(_allowedReferences[_domain]).ShouldBe([_application]);
    }

    private static List<string> ProjectReferences(string project)
    {
        var path = Path.Combine(RepositoryRoot.SourceDirectory, project, $"{project}.csproj");
        return ProjectReferences(XDocument.Load(path));
    }

    private static List<string> ProjectReferences(XDocument csproj) =>
        csproj.Descendants("ProjectReference")
            .Select(reference => (string?)reference.Attribute("Include") ?? string.Empty)
            .Select(include => Path.GetFileNameWithoutExtension(include.Replace('\\', '/')))
            .ToList();
}
