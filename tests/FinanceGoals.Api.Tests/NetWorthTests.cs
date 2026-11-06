extern alias migrator;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FinanceGoals.Application.Accounts;
using FinanceGoals.Application.NetWorth;
using FinanceGoals.Migrator.Tests;
using migrator::FinanceGoals.Migrator;

namespace FinanceGoals.Api.Tests;

[TestClass]
public sealed class NetWorthTests
{
    private static string _database = string.Empty;
    private static ApiFactory? _api;

    [ClassInitialize]
    public static async Task StartAsync(TestContext context)
    {
        _database = await SqlServer.CreateDatabaseAsync();
        var password = SqlServer.NewPassword();
        new DatabaseMigrator(_database, TextWriter.Null).Run(AppUser.WithPassword("financegoals_app", password));
        // Round-number rates, units per euro: 1 000 USD = 800 EUR = 8 000 NOK. There is no RON rate.
        await SqlServer.ExecuteAsync(_database, """
            INSERT INTO app.ExchangeRate (CurrencyCode, RateDate, UnitsPerEur) VALUES
                ('NOK', '2026-01-01', 10), ('USD', '2026-01-01', 1.25), ('USD', '2026-06-01', 1.0);
            """);
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

    private static async Task<Account> AccountAsync(int profileId, string subject, string name, string type, string currency)
    {
        var response = await SendAsync(HttpMethod.Post, $"/api/profiles/{profileId}/accounts", subject, new { name, type, currencyCode = currency });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<Account>()).ShouldNotBeNull();
    }

    private static async Task BalanceAsync(int profileId, string subject, string date, params (int AccountId, decimal Amount)[] values)
    {
        var response = await SendAsync(HttpMethod.Post, $"/api/profiles/{profileId}/snapshots", subject, new { date, values = values.Select(v => new { accountId = v.AccountId, amount = v.Amount }) });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static async Task<NetWorthResult> NetWorthAsync(int profileId, string subject, string query = "") =>
        (await (await SendAsync(HttpMethod.Get, $"/api/profiles/{profileId}/net-worth{query}", subject)).Content.ReadFromJsonAsync<NetWorthResult>()).ShouldNotBeNull();

    [TestMethod]
    public async Task Net_worth_converts_each_account_and_splits_out_the_liquid_part()
    {
        var profileId = await ProfileOfAsync("nw-owner-1");
        var savings = await AccountAsync(profileId, "nw-owner-1", "Savings", "Savings", "NOK");
        var brokerage = await AccountAsync(profileId, "nw-owner-1", "Brokerage", "Brokerage", "USD");
        var pension = await AccountAsync(profileId, "nw-owner-1", "Pension", "Pension", "USD");
        var house = await AccountAsync(profileId, "nw-owner-1", "House", "Property", "NOK");
        await BalanceAsync(profileId, "nw-owner-1", "2026-02-28", (savings.Id, 50_000m), (brokerage.Id, 1_000m), (pension.Id, 2_000m), (house.Id, 4_000_000m));

        var inNok = await NetWorthAsync(profileId, "nw-owner-1", "?date=2026-03-01");
        var inUsd = await NetWorthAsync(profileId, "nw-owner-1", "?date=2026-03-01&currency=USD");

        inNok.Currency.ShouldBe("NOK");
        inNok.Total.ShouldBe(4_074_000m);
        inNok.Liquid.ShouldBe(58_000m);
        inNok.MissingRates.ShouldBeEmpty();
        inNok.Lines.Select(l => l.Name).ShouldBe(["Brokerage", "House", "Pension", "Savings"]);
        inNok.Lines.Single(l => l.Name == "Brokerage").Converted.ShouldBe(8_000m);
        inNok.Lines.Single(l => l.Name == "Brokerage").BalanceDate.ShouldBe(new DateOnly(2026, 2, 28));
        // NOK to USD through EUR: 4 074 000 NOK = 407 400 EUR = 509 250 USD.
        inUsd.Currency.ShouldBe("USD");
        inUsd.Total.ShouldBe(509_250m);
    }

    [TestMethod]
    public async Task A_currency_without_a_rate_is_listed_and_left_out_never_counted_at_1()
    {
        var profileId = await ProfileOfAsync("nw-owner-2");
        var savings = await AccountAsync(profileId, "nw-owner-2", "Savings", "Savings", "NOK");
        var romania = await AccountAsync(profileId, "nw-owner-2", "Romania", "Current", "RON");
        await BalanceAsync(profileId, "nw-owner-2", "2026-02-28", (savings.Id, 1_000m), (romania.Id, 20_000m));

        var result = await NetWorthAsync(profileId, "nw-owner-2", "?date=2026-03-01");

        result.MissingRates.ShouldBe(["RON"]);
        result.Total.ShouldBe(1_000m);
        result.Lines.Single(l => l.Name == "Romania").Converted.ShouldBeNull();
        result.Lines.Single(l => l.Name == "Romania").Amount.ShouldBe(20_000m);
    }

    [TestMethod]
    public async Task The_balance_and_the_rate_are_those_of_the_date_asked_for()
    {
        var profileId = await ProfileOfAsync("nw-owner-3");
        var brokerage = await AccountAsync(profileId, "nw-owner-3", "Brokerage", "Brokerage", "USD");
        await BalanceAsync(profileId, "nw-owner-3", "2026-02-28", (brokerage.Id, 1_000m));
        await BalanceAsync(profileId, "nw-owner-3", "2026-07-31", (brokerage.Id, 2_000m));

        var march = await NetWorthAsync(profileId, "nw-owner-3", "?date=2026-03-15");
        var august = await NetWorthAsync(profileId, "nw-owner-3", "?date=2026-08-15");
        var beforeAnything = await NetWorthAsync(profileId, "nw-owner-3", "?date=2026-01-15");

        march.Total.ShouldBe(8_000m);
        august.Total.ShouldBe(20_000m);
        beforeAnything.Lines.ShouldBeEmpty();
        beforeAnything.Total.ShouldBe(0m);
    }

    [TestMethod]
    public async Task The_monthly_history_follows_the_balances_and_the_main_currency_is_the_default()
    {
        var profileId = await ProfileOfAsync("nw-owner-4");
        var savings = await AccountAsync(profileId, "nw-owner-4", "Savings", "Savings", "NOK");
        await BalanceAsync(profileId, "nw-owner-4", "2026-01-31", (savings.Id, 100m));
        await BalanceAsync(profileId, "nw-owner-4", "2026-03-31", (savings.Id, 150m));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var history = await (await SendAsync(HttpMethod.Get, $"/api/profiles/{profileId}/net-worth/history?to=2026-04-15", "nw-owner-4")).Content.ReadFromJsonAsync<List<NetWorthPoint>>();
        var current = await NetWorthAsync(profileId, "nw-owner-4");

        history.ShouldNotBeNull().Select(p => (p.Date, p.Total)).ShouldBe(
        [
            (new DateOnly(2026, 1, 31), 100m), (new DateOnly(2026, 2, 28), 100m), (new DateOnly(2026, 3, 31), 150m), (new DateOnly(2026, 4, 15), 150m),
        ]);
        current.Currency.ShouldBe("NOK");
        current.Date.ShouldBe(today);
        current.Total.ShouldBe(150m);
    }

    [TestMethod]
    public async Task An_archived_account_stops_counting_from_the_day_it_was_archived()
    {
        var profileId = await ProfileOfAsync("nw-owner-5");
        var closed = await AccountAsync(profileId, "nw-owner-5", "Closed", "Deposit", "NOK");
        await BalanceAsync(profileId, "nw-owner-5", "2026-01-31", (closed.Id, 700m));
        await SendAsync(HttpMethod.Patch, $"/api/profiles/{profileId}/accounts/{closed.Id}", "nw-owner-5", new { archived = true });
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var before = await NetWorthAsync(profileId, "nw-owner-5", "?date=2026-06-01");
        var after = await NetWorthAsync(profileId, "nw-owner-5", $"?date={today:yyyy-MM-dd}");

        before.Total.ShouldBe(700m);
        after.Total.ShouldBe(0m);
        after.Lines.ShouldBeEmpty();
    }

    [TestMethod]
    public async Task Another_household_sees_none_of_it_and_bad_input_is_refused()
    {
        var profileA = await ProfileOfAsync("nw-owner-6a");
        var profileB = await ProfileOfAsync("nw-owner-6b");
        var savings = await AccountAsync(profileA, "nw-owner-6a", "Savings", "Savings", "NOK");
        await BalanceAsync(profileA, "nw-owner-6a", "2026-01-31", (savings.Id, 100m));

        var viaA = await SendAsync(HttpMethod.Get, $"/api/profiles/{profileA}/net-worth", "nw-owner-6b");
        var historyViaA = await SendAsync(HttpMethod.Get, $"/api/profiles/{profileA}/net-worth/history", "nw-owner-6b");
        var future = DateTime.UtcNow.AddDays(10).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        viaA.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        historyViaA.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await NetWorthAsync(profileB, "nw-owner-6b")).Total.ShouldBe(0m);
        (await SendAsync(HttpMethod.Get, $"/api/profiles/{profileB}/net-worth?currency=XXQ", "nw-owner-6b")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await SendAsync(HttpMethod.Get, $"/api/profiles/{profileB}/net-worth?currency=nok", "nw-owner-6b")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await SendAsync(HttpMethod.Get, $"/api/profiles/{profileB}/net-worth?date={future}", "nw-owner-6b")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await SendAsync(HttpMethod.Get, $"/api/profiles/{profileB}/net-worth/history?to=1980-01-01", "nw-owner-6b")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
