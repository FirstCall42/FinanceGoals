extern alias migrator;

using System.Net;
using System.Net.Http.Json;
using FinanceGoals.Application.Currencies;
using FinanceGoals.Migrator.Tests;
using migrator::FinanceGoals.Migrator;

namespace FinanceGoals.Api.Tests;

[TestClass]
public sealed class CurrencyEndpointTests
{
    [TestMethod]
    public async Task Lists_the_seeded_currencies_as_the_app_user()
    {
        var database = await SqlServer.CreateDatabaseAsync();
        var password = SqlServer.NewPassword();
        new DatabaseMigrator(database, TextWriter.Null).Run(AppUser.WithPassword("financegoals_app", password));
        await using var api = new ApiFactory(SqlServer.As(database, "financegoals_app", password));

        var currencies = await api.CreateClient().GetFromJsonAsync<List<CurrencyInfo>>(new Uri("/api/currencies", UriKind.Relative));

        currencies.ShouldNotBeNull().Select(currency => currency.Code).ShouldBe(["EUR", "NOK", "RON", "USD"]);
        currencies.ShouldContain(new CurrencyInfo("NOK", 578, 2, "Norwegian krone"));
    }

    [TestMethod]
    public async Task Fails_with_a_server_error_when_the_database_is_unreachable()
    {
        await using var api = new ApiFactory(ApiFactory.UnreachableDatabase);

        var response = await api.CreateClient().GetAsync(new Uri("/api/currencies", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldNotContain("SqlException");
        body.ShouldNotContain("127.0.0.1");
    }
}
