extern alias migrator;
using System.Net;
using FinanceGoals.Migrator.Tests;
using migrator::FinanceGoals.Migrator;

namespace FinanceGoals.Api.Tests;

[TestClass]
public sealed class HealthEndpointTests
{
    private const string _appUser = "financegoals_app";

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
        (await response.Content.ReadAsStringAsync()).ShouldBe("Unhealthy");
    }

    [TestMethod]
    public async Task Readiness_is_healthy_as_the_app_user_on_a_migrated_database()
    {
        // As deployed: a contained database migrated by the real scripts, and the API signed in
        // as the app user the migrator created, not as sa.
        var (database, password) = await MigratedDatabaseAsync();
        await using var api = new ApiFactory(SqlServer.As(database, _appUser, password));

        var response = await api.CreateClient().GetAsync(new Uri("/health/ready", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldBe("Healthy");
    }

    [TestMethod]
    public async Task Readiness_fails_when_the_app_user_cannot_sign_in()
    {
        var (database, _) = await MigratedDatabaseAsync();
        await using var api = new ApiFactory(SqlServer.As(database, _appUser, SqlServer.NewPassword()));

        var response = await api.CreateClient().GetAsync(new Uri("/health/ready", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    private static async Task<(string Database, string Password)> MigratedDatabaseAsync()
    {
        var database = await SqlServer.CreateDatabaseAsync();
        var password = SqlServer.NewPassword();
        new DatabaseMigrator(database, TextWriter.Null).Run(AppUser.WithPassword(_appUser, password));
        return (database, password);
    }
}
