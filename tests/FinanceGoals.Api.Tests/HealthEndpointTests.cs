using System.Net;
using Testcontainers.MsSql;

namespace FinanceGoals.Api.Tests;

[TestClass]
public sealed class HealthEndpointTests
{
    private const string _image = "mcr.microsoft.com/mssql/server:2022-CU27-ubuntu-22.04";

    private static MsSqlContainer? _sqlServer;

    [ClassInitialize]
    public static async Task StartAsync(TestContext context)
    {
        _sqlServer = new MsSqlBuilder(_image).Build();
        await _sqlServer.StartAsync(context.CancellationToken);
    }

    [ClassCleanup]
    public static async Task StopAsync()
    {
        if (_sqlServer is not null)
        {
            await _sqlServer.DisposeAsync();
        }
    }

    [TestMethod]
    [DataRow(ApiFactory.UnreachableDatabase)]
    [DataRow(null)]
    public async Task Liveness_is_healthy_whatever_the_database_state(string? connectionString)
    {
        // If liveness ran the database check, these would be 503: nothing answers on port 1.
        await using var api = new ApiFactory(connectionString);

        var response = await api.CreateClient().GetAsync(new Uri("/health/live", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldBe("Healthy");
    }

    [TestMethod]
    [DataRow(ApiFactory.UnreachableDatabase)]
    [DataRow(null)]
    [DataRow("not a connection string")]
    public async Task Readiness_fails_when_the_database_cannot_be_reached(string? connectionString)
    {
        await using var api = new ApiFactory(connectionString);

        var response = await api.CreateClient().GetAsync(new Uri("/health/ready", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldBe("Unhealthy");
    }

    [TestMethod]
    public async Task Readiness_is_healthy_when_the_database_answers()
    {
        await using var api = new ApiFactory(SqlServer.GetConnectionString());

        var response = await api.CreateClient().GetAsync(new Uri("/health/ready", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldBe("Healthy");
    }

    private static MsSqlContainer SqlServer =>
        _sqlServer ?? throw new InvalidOperationException("The SQL Server container has not started.");
}
