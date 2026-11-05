using System.Net;
using System.Text.Json.Nodes;

namespace FinanceGoals.Api.Tests;

[TestClass]
public sealed class OpenApiTests
{
    private static readonly string _apiProject = Path.Combine(RepositoryRoot(), "src", "FinanceGoals.Api");
    private static readonly string _committedDocument = Path.Combine(_apiProject, "openapi", "FinanceGoals.Api.json");

    [TestMethod]
    public void Build_generates_the_document()
    {
        // The build-time generator records what it wrote in obj/. A fresh CI checkout has no
        // obj/, so this fails if the build stops generating the document.
        var record = Path.Combine(_apiProject, "obj", "FinanceGoals.Api.OpenApiFiles.cache");

        File.Exists(record).ShouldBeTrue($"{record} is written when the build generates the OpenAPI document");
        File.ReadAllLines(record).Select(Path.GetFullPath).ShouldContain(Path.GetFullPath(_committedDocument));
    }

    [TestMethod]
    public async Task Committed_document_matches_the_api()
    {
        // The committed document must describe the API as built, so an endpoint added without
        // committing the regenerated document fails here.
        await using var api = new ApiFactory(ApiFactory.UnreachableDatabase, "Development");
        var served = JsonNode.Parse(await api.CreateClient().GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative)))!.AsObject();
        // Only the running API knows the address it is served from.
        served.Remove("servers");

        var committed = JsonNode.Parse(await File.ReadAllTextAsync(_committedDocument));

        JsonNode.DeepEquals(served, committed).ShouldBeTrue(
            $"{_committedDocument} is out of date; build the API and commit the regenerated document.\nServed:\n{served}");
    }

    [TestMethod]
    [DataRow("Development", HttpStatusCode.OK)]
    [DataRow("Production", HttpStatusCode.NotFound)]
    public async Task Document_is_served_only_while_developing(string environment, HttpStatusCode expected)
    {
        await using var api = new ApiFactory(ApiFactory.UnreachableDatabase, environment);

        var response = await api.CreateClient().GetAsync(new Uri("/openapi/v1.json", UriKind.Relative));

        response.StatusCode.ShouldBe(expected);
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
}
