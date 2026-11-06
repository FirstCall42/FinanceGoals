extern alias migrator;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FinanceGoals.Migrator.Tests;
using migrator::FinanceGoals.Migrator;

namespace FinanceGoals.Api.Tests;

[TestClass]
public sealed class LoanTests
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

    // A variable-rate annuity from 1 Jan 2026 (first payment 1 Feb) with a later fixed period.
    private static object Mortgage(string name = "Home") => new
    {
        name,
        currencyCode = "NOK",
        principal = 3_000_000.50m,
        paidOut = "2026-01-01",
        firstPaymentDate = "2026-02-01",
        termMonths = 300,
        feePerPayment = 55m,
        periods = new object[]
        {
            new
            {
                start = "2026-01-01", rateType = "Variable", repaymentType = "Annuity",
                rates = new object[] { new { effectiveFrom = "2026-01-01", annualRatePercent = 5.24m }, new { effectiveFrom = "2026-07-01", annualRatePercent = 5.5m } },
            },
            new
            {
                start = "2027-01-01", rateType = "Fixed", repaymentType = "Annuity",
                rates = new object[] { new { effectiveFrom = "2027-01-01", annualRatePercent = 4.9m } },
            },
        },
    };

    private static async Task<JsonNode> ReadAsync(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

    [TestMethod]
    public async Task A_loan_with_periods_and_rate_changes_round_trips()
    {
        var profileId = await ProfileOfAsync("loan-owner-1");

        var created = await SendAsync(HttpMethod.Post, $"/api/profiles/{profileId}/loans", "loan-owner-1", Mortgage());
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = (int)(await ReadAsync(created))["id"]!;
        var loaded = await ReadAsync(await SendAsync(HttpMethod.Get, $"/api/profiles/{profileId}/loans/{id}", "loan-owner-1"));
        var listed = await ReadAsync(await SendAsync(HttpMethod.Get, $"/api/profiles/{profileId}/loans", "loan-owner-1"));

        ((string)loaded["name"]!).ShouldBe("Home");
        ((string)loaded["currencyCode"]!).ShouldBe("NOK");
        ((decimal)loaded["principal"]!).ShouldBe(3_000_000.50m);
        ((decimal)loaded["feePerPayment"]!).ShouldBe(55m);
        ((int)loaded["termMonths"]!).ShouldBe(300);
        ((string)loaded["dayCount"]!).ShouldBe("ActualActual");
        var periods = loaded["periods"]!.AsArray();
        periods.Count.ShouldBe(2);
        ((string)periods[0]!["rateType"]!).ShouldBe("Variable");
        periods[0]!["rates"]!.AsArray().Select(r => ((string)r!["effectiveFrom"]!, (decimal)r["annualRatePercent"]!)).ShouldBe([("2026-01-01", 5.24m), ("2026-07-01", 5.5m)]);
        ((string)periods[1]!["rateType"]!).ShouldBe("Fixed");
        periods[1]!["rates"]!.AsArray().Count.ShouldBe(1);
        listed.AsArray().Select(l => (int)l!["id"]!).ShouldContain(id);
    }

    [TestMethod]
    public async Task A_rate_change_is_stored_with_its_effective_date_in_the_period_it_falls_in()
    {
        var profileId = await ProfileOfAsync("loan-owner-2");
        var id = (int)(await ReadAsync(await SendAsync(HttpMethod.Post, $"/api/profiles/{profileId}/loans", "loan-owner-2", Mortgage()))).AsObject()["id"]!;

        var changed = await SendAsync(HttpMethod.Post, $"/api/profiles/{profileId}/loans/{id}/rate-changes", "loan-owner-2", new { effectiveFrom = "2026-10-01", annualRatePercent = 5.75m });
        var again = await SendAsync(HttpMethod.Post, $"/api/profiles/{profileId}/loans/{id}/rate-changes", "loan-owner-2", new { effectiveFrom = "2026-10-01", annualRatePercent = 6m });
        var inFixed = await SendAsync(HttpMethod.Post, $"/api/profiles/{profileId}/loans/{id}/rate-changes", "loan-owner-2", new { effectiveFrom = "2027-03-01", annualRatePercent = 4m });
        var outside = await SendAsync(HttpMethod.Post, $"/api/profiles/{profileId}/loans/{id}/rate-changes", "loan-owner-2", new { effectiveFrom = "2090-01-01", annualRatePercent = 4m });
        var loaded = await ReadAsync(await SendAsync(HttpMethod.Get, $"/api/profiles/{profileId}/loans/{id}", "loan-owner-2"));

        changed.StatusCode.ShouldBe(HttpStatusCode.OK);
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        inFixed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        outside.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        loaded["periods"]![0]!["rates"]!.AsArray().Select(r => ((string)r!["effectiveFrom"]!, (decimal)r["annualRatePercent"]!))
            .ShouldBe([("2026-01-01", 5.24m), ("2026-07-01", 5.5m), ("2026-10-01", 5.75m)]);
    }

    [TestMethod]
    public async Task Bad_loans_are_refused_and_names_are_unique_per_household()
    {
        var profileId = await ProfileOfAsync("loan-owner-3");
        var path = $"/api/profiles/{profileId}/loans";

        (await SendAsync(HttpMethod.Post, path, "loan-owner-3", Mortgage("Once"))).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await SendAsync(HttpMethod.Post, path, "loan-owner-3", Mortgage("Once"))).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await SendAsync(HttpMethod.Post, path, "loan-owner-3", new { name = "", currencyCode = "NOK" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        // Paid out after the first payment, and a principal with half an øre.
        (await SendAsync(HttpMethod.Post, path, "loan-owner-3", new { name = "Late", currencyCode = "NOK", principal = 100m, paidOut = "2026-03-01", firstPaymentDate = "2026-02-01", termMonths = 12, periods = new object[] { new { start = "2026-03-01", rateType = "Variable", repaymentType = "Annuity", rates = new object[] { new { effectiveFrom = "2026-03-01", annualRatePercent = 5m } } } } }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await SendAsync(HttpMethod.Post, path, "loan-owner-3", new { name = "Half", currencyCode = "NOK", principal = 100.005m, paidOut = "2026-01-01", firstPaymentDate = "2026-02-01", termMonths = 12, periods = new object[] { new { start = "2026-01-01", rateType = "Variable", repaymentType = "Annuity", rates = new object[] { new { effectiveFrom = "2026-01-01", annualRatePercent = 5m } } } } }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [TestMethod]
    public async Task Another_household_sees_none_of_it_and_cannot_change_it()
    {
        var profileA = await ProfileOfAsync("loan-owner-4a");
        var profileB = await ProfileOfAsync("loan-owner-4b");
        var id = (int)(await ReadAsync(await SendAsync(HttpMethod.Post, $"/api/profiles/{profileA}/loans", "loan-owner-4a", Mortgage()))).AsObject()["id"]!;

        (await SendAsync(HttpMethod.Get, $"/api/profiles/{profileA}/loans", "loan-owner-4b")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await SendAsync(HttpMethod.Post, $"/api/profiles/{profileA}/loans/{id}/rate-changes", "loan-owner-4b", new { effectiveFrom = "2026-10-01", annualRatePercent = 5m })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        // The household's own endpoints don't reach A's loan by id either.
        (await SendAsync(HttpMethod.Get, $"/api/profiles/{profileB}/loans/{id}", "loan-owner-4b")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await SendAsync(HttpMethod.Post, $"/api/profiles/{profileB}/loans/{id}/rate-changes", "loan-owner-4b", new { effectiveFrom = "2026-10-01", annualRatePercent = 5m })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ReadAsync(await SendAsync(HttpMethod.Get, $"/api/profiles/{profileB}/loans", "loan-owner-4b"))).AsArray().Count.ShouldBe(0);
    }
}
