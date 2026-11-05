using System.Net;
using System.Text.Json;

namespace FinanceGoals.Api.Tests;

[TestClass]
public sealed class OpenApiTests
{
    [TestMethod]
    public void Build_writes_the_openapi_document()
    {
        var path = Path.Combine(RepositoryRoot(), "src", "FinanceGoals.Api", "openapi", "FinanceGoals.Api.json");

        File.Exists(path).ShouldBeTrue($"{path} is written by the build");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        document.RootElement.GetProperty("openapi").GetString().ShouldStartWith("3.");
        document.RootElement.GetProperty("info").GetProperty("title").GetString().ShouldBe("FinanceGoals.Api | v1");
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
