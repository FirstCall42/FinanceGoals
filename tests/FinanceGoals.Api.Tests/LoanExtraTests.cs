extern alias migrator;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FinanceGoals.Migrator.Tests;
using migrator::FinanceGoals.Migrator;

namespace FinanceGoals.Api.Tests;

[TestClass]
public sealed class LoanExtraTests
{
    private static string _database = string.Empty;
    private static ApiFactory? _api;

    [ClassInitialize]
    public static async Task StartAsync(TestContext context)
    {
        _database = await SqlServer.CreateDatabaseAsync();
        var password = SqlServer.NewPassword();
        new DatabaseMigrator(_database, TextWriter.Null).Run(AppUser.WithPassword("financegoals_app", password));
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

    private static async Task<(int ProfileId, int LoanId)> LoanAsync(string subject)
    {
        var profileId = await ProfileOfAsync(subject);
        var created = await SendAsync(HttpMethod.Post, $"/api/profiles/{profileId}/loans", subject, new
        {
            name = "Home",
            currencyCode = "NOK",
            principal = 3_000_000m,
            paidOut = "2026-01-01",
            firstPaymentDate = "2026-02-01",
            termMonths = 300,
            periods = new object[]
            {
                new { start = "2026-01-01", rateType = "Variable", repaymentType = "Annuity", rates = new object[] { new { effectiveFrom = "2026-01-01", annualRatePercent = 5.24m } } },
            },
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (profileId, (int)(await ReadAsync(created))["id"]!);
    }

    [TestMethod]
    public async Task Extra_payments_and_balances_are_stored_read_back_and_deleted()
    {
        var (profileId, loanId) = await LoanAsync("extra-owner-1");
        var path = $"/api/profiles/{profileId}/loans/{loanId}";

        (await SendAsync(HttpMethod.Post, $"{path}/extras/one-off", "extra-owner-1", new { date = "2026-06-15", amount = 100_000m })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Post, $"{path}/extras/monthly", "extra-owner-1", new { amount = 2_500.50m, from = "2026-03-01" })).StatusCode.ShouldBe(HttpStatusCode.OK);
        var afterBalance = await SendAsync(HttpMethod.Put, $"{path}/balances/2026-09-30", "extra-owner-1", new { amount = 2_850_000.25m });
        (await SendAsync(HttpMethod.Put, $"{path}/balances/2026-09-30", "extra-owner-1", new { amount = 2_840_000m })).StatusCode.ShouldBe(HttpStatusCode.OK);
        var extras = await ReadAsync(await SendAsync(HttpMethod.Get, $"{path}/extras", "extra-owner-1"));

        afterBalance.StatusCode.ShouldBe(HttpStatusCode.OK);
        ((string)extras["currencyCode"]!).ShouldBe("NOK");
        extras["oneOff"]!.AsArray().Select(e => ((string)e!["date"]!, (decimal)e["amount"]!)).ShouldBe([("2026-06-15", 100_000m)]);
        var monthly = extras["monthly"]!.AsArray().Single()!;
        ((decimal)monthly["amount"]!).ShouldBe(2_500.50m);
        monthly["until"].ShouldBeNull();
        // The second save of the same day replaced the first.
        extras["balances"]!.AsArray().Select(b => ((string)b!["date"]!, (decimal)b["amount"]!)).ShouldBe([("2026-09-30", 2_840_000m)]);

        (await SendAsync(HttpMethod.Delete, $"{path}/extras/one-off/2026-06-15", "extra-owner-1")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Delete, $"{path}/extras/monthly/{(int)monthly["id"]!}", "extra-owner-1")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var emptied = await ReadAsync(await SendAsync(HttpMethod.Delete, $"{path}/balances/2026-09-30", "extra-owner-1"));
        emptied["oneOff"]!.AsArray().Count.ShouldBe(0);
        emptied["monthly"]!.AsArray().Count.ShouldBe(0);
        emptied["balances"]!.AsArray().Count.ShouldBe(0);
        (await SendAsync(HttpMethod.Delete, $"{path}/balances/2026-09-30", "extra-owner-1")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [TestMethod]
    public async Task Extras_and_balances_that_do_not_fit_are_refused()
    {
        var (profileId, loanId) = await LoanAsync("extra-owner-2");
        var path = $"/api/profiles/{profileId}/loans/{loanId}";

        (await SendAsync(HttpMethod.Post, $"{path}/extras/one-off", "extra-owner-2", new { date = "2026-06-15", amount = 1_000m })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Post, $"{path}/extras/one-off", "extra-owner-2", new { date = "2026-06-15", amount = 2_000m })).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        // Before the loan was paid out, after its last payment, half an øre, nothing.
        (await SendAsync(HttpMethod.Post, $"{path}/extras/one-off", "extra-owner-2", new { date = "2025-01-01", amount = 1_000m })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await SendAsync(HttpMethod.Post, $"{path}/extras/one-off", "extra-owner-2", new { date = "2090-01-01", amount = 1_000m })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await SendAsync(HttpMethod.Post, $"{path}/extras/one-off", "extra-owner-2", new { date = "2026-07-15", amount = 10.005m })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await SendAsync(HttpMethod.Post, $"{path}/extras/one-off", "extra-owner-2", new { date = "2026-07-15" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await SendAsync(HttpMethod.Post, $"{path}/extras/monthly", "extra-owner-2", new { amount = 500m, from = "2026-05-01", until = "2026-04-01" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await SendAsync(HttpMethod.Put, $"{path}/balances/2025-12-31", "extra-owner-2", new { amount = 1m })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await SendAsync(HttpMethod.Put, $"{path}/balances/2026-06-30", "extra-owner-2", new { amount = -1m })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await SendAsync(HttpMethod.Put, $"{path}/balances/2026-06-30", "extra-owner-2", new { amount = 100.001m })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await SendAsync(HttpMethod.Put, $"{path}/balances/2099-06-30", "extra-owner-2", new { amount = 100m })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [TestMethod]
    public async Task Another_household_cannot_read_or_change_a_loans_extras()
    {
        var (profileA, loanId) = await LoanAsync("extra-owner-3a");
        var profileB = await ProfileOfAsync("extra-owner-3b");
        await SendAsync(HttpMethod.Post, $"/api/profiles/{profileA}/loans/{loanId}/extras/one-off", "extra-owner-3a", new { date = "2026-06-15", amount = 1_000m });

        // Through A's profile id the membership check says 404; through their own, the loan isn't theirs.
        foreach (var profile in new[] { profileA, profileB })
        {
            var path = $"/api/profiles/{profile}/loans/{loanId}";
            (await SendAsync(HttpMethod.Get, $"{path}/extras", "extra-owner-3b")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await SendAsync(HttpMethod.Post, $"{path}/extras/one-off", "extra-owner-3b", new { date = "2026-07-15", amount = 1_000m })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await SendAsync(HttpMethod.Post, $"{path}/extras/monthly", "extra-owner-3b", new { amount = 1_000m, from = "2026-07-01" })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await SendAsync(HttpMethod.Put, $"{path}/balances/2026-06-30", "extra-owner-3b", new { amount = 1m })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await SendAsync(HttpMethod.Delete, $"{path}/extras/one-off/2026-06-15", "extra-owner-3b")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        var still = await ReadAsync(await SendAsync(HttpMethod.Get, $"/api/profiles/{profileA}/loans/{loanId}/extras", "extra-owner-3a"));
        still["oneOff"]!.AsArray().Count.ShouldBe(1);
    }
}
