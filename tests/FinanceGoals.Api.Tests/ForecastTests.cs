extern alias migrator;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FinanceGoals.Application.Accounts;
using FinanceGoals.Migrator.Tests;
using migrator::FinanceGoals.Migrator;

namespace FinanceGoals.Api.Tests;

[TestClass]
public sealed class ForecastTests
{
    private static string _database = string.Empty;
    private static ApiFactory? _api;

    [ClassInitialize]
    public static async Task StartAsync(TestContext context)
    {
        _database = await SqlServer.CreateDatabaseAsync();
        var password = SqlServer.NewPassword();
        new DatabaseMigrator(_database, TextWriter.Null).Run(AppUser.WithPassword("financegoals_app", password));
        await SqlServer.ExecuteAsync(_database, "INSERT INTO app.ExchangeRate (CurrencyCode, RateDate, UnitsPerEur) VALUES ('NOK', '2026-01-01', 10);");
        _api = new ApiFactory(SqlServer.As(_database, "financegoals_app", password));
    }

    [ClassCleanup]
    public static async Task StopAsync()
    {
        if (_api is not null)
        {
            await _api.DisposeAsync();
        }
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string subject, object? body = null)
    {
        var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative))
        {
            Content = body is null ? null : JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.For(subject: subject, email: $"{subject}@example.com"));
        return await _api!.CreateClient().SendAsync(request);
    }

    private static async Task<int> ProfileOfAsync(string subject) =>
        (await (await SendAsync(HttpMethod.Post, "/api/me", subject)).Content.ReadFromJsonAsync<SignedIn>()).ShouldNotBeNull().Profiles[0].Id;

    private static async Task<JsonNode> ReadAsync(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

    private static async Task<(int ProfileId, int AccountId)> AccountWithBalanceAsync(string subject, decimal balance)
    {
        var profileId = await ProfileOfAsync(subject);
        var response = await SendAsync(HttpMethod.Post, $"/api/profiles/{profileId}/accounts", subject, new { name = "Fund", type = "Brokerage", currencyCode = "NOK" });
        var account = (await response.Content.ReadFromJsonAsync<Account>()).ShouldNotBeNull();
        var snapshot = await SendAsync(HttpMethod.Post, $"/api/profiles/{profileId}/snapshots", subject, new { date = "2026-01-31", values = new[] { new { accountId = account.Id, amount = balance } } });
        snapshot.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (profileId, account.Id);
    }

    [TestMethod]
    public async Task The_forecast_follows_the_saved_return_and_contribution_and_the_assumptions_can_be_removed()
    {
        var (profileId, accountId) = await AccountWithBalanceAsync("fc-owner-1", 100_000m);
        var path = $"/api/profiles/{profileId}";

        var saved = await SendAsync(HttpMethod.Put, $"{path}/accounts/{accountId}/assumption", "fc-owner-1", new { expectedYearlyReturnPercent = 6m, monthlyContribution = 5_000m });
        var again = await SendAsync(HttpMethod.Put, $"{path}/accounts/{accountId}/assumption", "fc-owner-1", new { expectedYearlyReturnPercent = 7m });
        var listed = await ReadAsync(await SendAsync(HttpMethod.Get, $"{path}/account-assumptions", "fc-owner-1"));
        var forecast = await ReadAsync(await SendAsync(HttpMethod.Get, $"{path}/net-worth/forecast?months=12", "fc-owner-1"));

        saved.StatusCode.ShouldBe(HttpStatusCode.OK);
        again.StatusCode.ShouldBe(HttpStatusCode.OK);
        listed.AsArray().Count.ShouldBe(1);
        ((decimal)listed[0]!["expectedYearlyReturnPercent"]!).ShouldBe(7m);
        listed[0]!["monthlyContribution"].ShouldBeNull();
        var points = forecast["points"]!.AsArray();
        points.Count.ShouldBe(13);
        ((decimal)points[0]!["expected"]!).ShouldBe(100_000m);
        // 7 % a year on 100 000 for a year, no contribution (the second save replaced the first).
        ((decimal)points[12]!["expected"]!).ShouldBeInRange(106_990m, 107_010m);
        ((decimal)points[12]!["low"]!).ShouldBeLessThan((decimal)points[12]!["expected"]!);
        ((decimal)points[12]!["high"]!).ShouldBeGreaterThan((decimal)points[12]!["expected"]!);

        (await SendAsync(HttpMethod.Delete, $"{path}/accounts/{accountId}/assumption", "fc-owner-1")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await SendAsync(HttpMethod.Delete, $"{path}/accounts/{accountId}/assumption", "fc-owner-1")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [TestMethod]
    public async Task The_forecast_subtracts_what_the_loans_owe_following_their_schedules()
    {
        var (profileId, _) = await AccountWithBalanceAsync("fc-owner-4", 100_000m);
        var path = $"/api/profiles/{profileId}";
        var created = await SendAsync(HttpMethod.Post, $"{path}/loans", "fc-owner-4", new
        {
            name = "Car",
            currencyCode = "NOK",
            principal = 40_000m,
            paidOut = "2026-01-01",
            firstPaymentDate = "2026-02-01",
            termMonths = 24,
            periods = new object[]
            {
                new { start = "2026-01-01", rateType = "Variable", repaymentType = "Annuity", rates = new object[] { new { effectiveFrom = "2026-01-01", annualRatePercent = 5m } } },
            },
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var loanId = (int)(await ReadAsync(created))["id"]!;
        (await SendAsync(HttpMethod.Put, $"{path}/loans/{loanId}/balances/2026-01-31", "fc-owner-4", new { amount = 30_000m })).StatusCode.ShouldBe(HttpStatusCode.OK);

        var forecast = await ReadAsync(await SendAsync(HttpMethod.Get, $"{path}/net-worth/forecast?months=6", "fc-owner-4"));

        // The balance recorded in January is old: the loan has been paid since, so less than 30 000 is owed, and less each month.
        var first = (decimal)forecast["points"]![0]!["expected"]!;
        var last = (decimal)forecast["points"]![6]!["expected"]!;
        first.ShouldBeGreaterThan(70_000m);
        last.ShouldBeGreaterThan(first);
        last.ShouldBeLessThanOrEqualTo(100_000m);
    }

    [TestMethod]
    public async Task Bad_assumptions_and_bad_forecast_requests_are_a_400()
    {
        var (profileId, accountId) = await AccountWithBalanceAsync("fc-owner-2", 1_000m);
        var path = $"/api/profiles/{profileId}";

        (await SendAsync(HttpMethod.Put, $"{path}/accounts/{accountId}/assumption", "fc-owner-2", new { })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await SendAsync(HttpMethod.Put, $"{path}/accounts/{accountId}/assumption", "fc-owner-2", new { expectedYearlyReturnPercent = 150m })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await SendAsync(HttpMethod.Put, $"{path}/accounts/{accountId}/assumption", "fc-owner-2", new { monthlyContribution = -1m })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await SendAsync(HttpMethod.Put, $"{path}/accounts/{accountId}/assumption", "fc-owner-2", new { monthlyContribution = 10.005m })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await SendAsync(HttpMethod.Put, $"{path}/accounts/{accountId}/assumption", "fc-owner-2", new { monthlyContribution = 2_000_000_000_000m })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await SendAsync(HttpMethod.Get, $"{path}/net-worth/forecast?months=0", "fc-owner-2")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await SendAsync(HttpMethod.Get, $"{path}/net-worth/forecast?months=601", "fc-owner-2")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await SendAsync(HttpMethod.Get, $"{path}/net-worth/forecast?currency=XXQ", "fc-owner-2")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await SendAsync(HttpMethod.Put, $"{path}/accounts/999999/assumption", "fc-owner-2", new { monthlyContribution = 1m })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [TestMethod]
    public async Task Another_household_cannot_read_or_change_the_expectations_of_an_account()
    {
        var (profileA, accountId) = await AccountWithBalanceAsync("fc-owner-3a", 1_000m);
        var profileB = await ProfileOfAsync("fc-owner-3b");
        await SendAsync(HttpMethod.Put, $"/api/profiles/{profileA}/accounts/{accountId}/assumption", "fc-owner-3a", new { monthlyContribution = 100m });

        foreach (var profile in new[] { profileA, profileB })
        {
            var path = $"/api/profiles/{profile}";
            (await SendAsync(HttpMethod.Put, $"{path}/accounts/{accountId}/assumption", "fc-owner-3b", new { monthlyContribution = 1m })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await SendAsync(HttpMethod.Delete, $"{path}/accounts/{accountId}/assumption", "fc-owner-3b")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        (await SendAsync(HttpMethod.Get, $"/api/profiles/{profileA}/net-worth/forecast", "fc-owner-3b")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ReadAsync(await SendAsync(HttpMethod.Get, $"/api/profiles/{profileB}/account-assumptions", "fc-owner-3b"))).AsArray().Count.ShouldBe(0);
        (await ReadAsync(await SendAsync(HttpMethod.Get, $"/api/profiles/{profileA}/account-assumptions", "fc-owner-3a"))).AsArray().Count.ShouldBe(1);
    }
}
