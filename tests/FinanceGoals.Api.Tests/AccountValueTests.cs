extern alias migrator;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FinanceGoals.Application.Accounts;
using FinanceGoals.Migrator.Tests;
using migrator::FinanceGoals.Migrator;

namespace FinanceGoals.Api.Tests;

[TestClass]
public sealed class AccountValueTests
{
    private static ApiFactory? _api;

    [ClassInitialize]
    public static async Task StartAsync(TestContext context)
    {
        var database = await SqlServer.CreateDatabaseAsync();
        var password = SqlServer.NewPassword();
        new DatabaseMigrator(database, TextWriter.Null).Run(AppUser.WithPassword("financegoals_app", password));
        _api = new ApiFactory(SqlServer.As(database, "financegoals_app", password));
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

    private static async Task<Account> AccountAsync(int profileId, string subject, string name, string currency = "NOK")
    {
        var response = await SendAsync(HttpMethod.Post, $"/api/profiles/{profileId}/accounts", subject, new { name, type = "Savings", currencyCode = currency });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<Account>()).ShouldNotBeNull();
    }

    private static Task<HttpResponseMessage> SnapshotAsync(int profileId, string subject, string date, params (int AccountId, decimal Amount)[] values) =>
        SendAsync(HttpMethod.Post, $"/api/profiles/{profileId}/snapshots", subject, new { date, values = values.Select(v => new { accountId = v.AccountId, amount = v.Amount }) });

    private static async Task<List<AccountValue>> ValuesAsync(int profileId, string subject, int? accountId = null) =>
        (await (await SendAsync(HttpMethod.Get, $"/api/profiles/{profileId}/values{(accountId is null ? string.Empty : $"?accountId={accountId}")}", subject)).Content.ReadFromJsonAsync<List<AccountValue>>()).ShouldNotBeNull();

    [TestMethod]
    public async Task A_snapshot_records_several_accounts_and_the_same_date_replaces_them()
    {
        var profileId = await ProfileOfAsync("val-owner-1");
        var savings = await AccountAsync(profileId, "val-owner-1", "Savings");
        var usd = await AccountAsync(profileId, "val-owner-1", "US retirement", "USD");
        var skipped = await AccountAsync(profileId, "val-owner-1", "Not changed");

        var january = await SnapshotAsync(profileId, "val-owner-1", "2026-01-31", (savings.Id, 50_000m), (usd.Id, 350_000.25m));
        var february = await SnapshotAsync(profileId, "val-owner-1", "2026-02-28", (savings.Id, 52_500m));
        var corrected = await SnapshotAsync(profileId, "val-owner-1", "2026-01-31", (savings.Id, 51_000m));

        january.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await january.Content.ReadFromJsonAsync<SnapshotSaved>()).ShouldBe(new SnapshotSaved(new DateOnly(2026, 1, 31), 2));
        february.StatusCode.ShouldBe(HttpStatusCode.OK);
        corrected.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ValuesAsync(profileId, "val-owner-1")).ShouldBe(
        [
            new AccountValue(savings.Id, new DateOnly(2026, 2, 28), 52_500m),
            new AccountValue(savings.Id, new DateOnly(2026, 1, 31), 51_000m),
            new AccountValue(usd.Id, new DateOnly(2026, 1, 31), 350_000.25m),
        ]);
        (await ValuesAsync(profileId, "val-owner-1", usd.Id)).ShouldHaveSingleItem().Amount.ShouldBe(350_000.25m);
        (await ValuesAsync(profileId, "val-owner-1", skipped.Id)).ShouldBeEmpty();
    }

    [TestMethod]
    public async Task Balances_are_deleted_one_at_a_time_or_a_whole_snapshot_at_once()
    {
        var profileId = await ProfileOfAsync("val-owner-2");
        var first = await AccountAsync(profileId, "val-owner-2", "First");
        var second = await AccountAsync(profileId, "val-owner-2", "Second");
        await SnapshotAsync(profileId, "val-owner-2", "2026-03-31", (first.Id, 100m), (second.Id, -250m));
        await SnapshotAsync(profileId, "val-owner-2", "2026-04-30", (first.Id, 110m), (second.Id, 5m));

        var oneValue = await SendAsync(HttpMethod.Delete, $"/api/profiles/{profileId}/accounts/{second.Id}/values/2026-03-31", "val-owner-2");
        var oneAgain = await SendAsync(HttpMethod.Delete, $"/api/profiles/{profileId}/accounts/{second.Id}/values/2026-03-31", "val-owner-2");
        var wholeSnapshot = await SendAsync(HttpMethod.Delete, $"/api/profiles/{profileId}/snapshots/2026-04-30", "val-owner-2");
        var snapshotAgain = await SendAsync(HttpMethod.Delete, $"/api/profiles/{profileId}/snapshots/2026-04-30", "val-owner-2");

        oneValue.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        oneAgain.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        wholeSnapshot.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        snapshotAgain.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ValuesAsync(profileId, "val-owner-2")).ShouldBe([new AccountValue(first.Id, new DateOnly(2026, 3, 31), 100m)]);
    }

    [TestMethod]
    public async Task An_account_with_balances_keeps_its_currency_and_nothing_else_changes_with_a_refused_edit()
    {
        var profileId = await ProfileOfAsync("val-owner-3");
        var account = await AccountAsync(profileId, "val-owner-3", "Locked");
        var empty = await AccountAsync(profileId, "val-owner-3", "Empty");
        await SnapshotAsync(profileId, "val-owner-3", "2026-01-31", (account.Id, 1_000m));
        var path = $"/api/profiles/{profileId}/accounts";

        var refused = await SendAsync(HttpMethod.Patch, $"{path}/{account.Id}", "val-owner-3", new { name = "Renamed", currencyCode = "EUR" });
        var sameCurrency = await SendAsync(HttpMethod.Patch, $"{path}/{account.Id}", "val-owner-3", new { name = "Renamed", currencyCode = "NOK" });
        var withoutValues = await SendAsync(HttpMethod.Patch, $"{path}/{empty.Id}", "val-owner-3", new { currencyCode = "EUR" });
        await SendAsync(HttpMethod.Delete, $"/api/profiles/{profileId}/snapshots/2026-01-31", "val-owner-3");
        var afterDelete = await SendAsync(HttpMethod.Patch, $"{path}/{account.Id}", "val-owner-3", new { currencyCode = "EUR" });

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await refused.Content.ReadAsStringAsync()).ShouldContain("currency can no longer change");
        sameCurrency.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await sameCurrency.Content.ReadFromJsonAsync<Account>()).ShouldNotBeNull().Name.ShouldBe("Renamed");
        withoutValues.StatusCode.ShouldBe(HttpStatusCode.OK);
        afterDelete.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [TestMethod]
    public async Task A_refused_currency_change_leaves_the_other_fields_as_they_were()
    {
        var profileId = await ProfileOfAsync("val-owner-4");
        var account = await AccountAsync(profileId, "val-owner-4", "Keep me");
        await SnapshotAsync(profileId, "val-owner-4", "2026-01-31", (account.Id, 1m));

        var refused = await SendAsync(HttpMethod.Patch, $"/api/profiles/{profileId}/accounts/{account.Id}", "val-owner-4", new { name = "Changed", archived = true, currencyCode = "USD" });
        var list = await (await SendAsync(HttpMethod.Get, $"/api/profiles/{profileId}/accounts", "val-owner-4")).Content.ReadFromJsonAsync<List<Account>>();

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        list.ShouldNotBeNull().ShouldHaveSingleItem().ShouldBe(new Account(account.Id, "Keep me", AccountType.Savings, "NOK", false));
    }

    [TestMethod]
    public async Task Invalid_snapshots_are_refused_and_nothing_is_saved()
    {
        var profileId = await ProfileOfAsync("val-owner-5");
        var nok = await AccountAsync(profileId, "val-owner-5", "NOK account");
        var jpy = await AccountAsync(profileId, "val-owner-5", "Yen account", "JPY");
        var future = DateTime.UtcNow.AddDays(10).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        var results = new[]
        {
            await SnapshotAsync(profileId, "val-owner-5", future, (nok.Id, 1m)),
            await SnapshotAsync(profileId, "val-owner-5", "1980-01-01", (nok.Id, 1m)),
            await SnapshotAsync(profileId, "val-owner-5", "2026-01-31"),
            await SnapshotAsync(profileId, "val-owner-5", "2026-01-31", (nok.Id, 1.234m)),
            await SnapshotAsync(profileId, "val-owner-5", "2026-01-31", (nok.Id, 1m), (jpy.Id, 100.5m)),
            await SnapshotAsync(profileId, "val-owner-5", "2026-01-31", (nok.Id, 1m), (nok.Id, 2m)),
            await SnapshotAsync(profileId, "val-owner-5", "2026-01-31", (nok.Id, 20_000_000_000_000m)),
            await SnapshotAsync(profileId, "val-owner-5", "2026-01-31", (999_999, 1m)),
        };

        results.ShouldAllBe(r => r.StatusCode == HttpStatusCode.BadRequest);
        (await ValuesAsync(profileId, "val-owner-5")).ShouldBeEmpty();
        (await SnapshotAsync(profileId, "val-owner-5", "2026-01-31", (nok.Id, 1.25m), (jpy.Id, 100m))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [TestMethod]
    public async Task Another_household_cannot_record_read_or_delete_balances_of_an_account()
    {
        var profileA = await ProfileOfAsync("val-owner-6a");
        var profileB = await ProfileOfAsync("val-owner-6b");
        var mine = await AccountAsync(profileA, "val-owner-6a", "Mine");
        await SnapshotAsync(profileA, "val-owner-6a", "2026-01-31", (mine.Id, 500m));

        var record = await SnapshotAsync(profileB, "val-owner-6b", "2026-02-28", (mine.Id, 1m));
        var delete = await SendAsync(HttpMethod.Delete, $"/api/profiles/{profileB}/accounts/{mine.Id}/values/2026-01-31", "val-owner-6b");
        var deleteSnapshot = await SendAsync(HttpMethod.Delete, $"/api/profiles/{profileB}/snapshots/2026-01-31", "val-owner-6b");
        var readViaA = await SendAsync(HttpMethod.Get, $"/api/profiles/{profileA}/values", "val-owner-6b");

        record.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        delete.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        deleteSnapshot.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        readViaA.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ValuesAsync(profileB, "val-owner-6b")).ShouldBeEmpty();
        (await ValuesAsync(profileA, "val-owner-6a")).ShouldHaveSingleItem().Amount.ShouldBe(500m);
    }
}
