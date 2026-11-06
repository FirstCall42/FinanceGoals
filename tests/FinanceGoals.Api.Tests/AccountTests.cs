extern alias migrator;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FinanceGoals.Application.Accounts;
using FinanceGoals.Migrator.Tests;
using Microsoft.Data.SqlClient;
using migrator::FinanceGoals.Migrator;

namespace FinanceGoals.Api.Tests;

[TestClass]
public sealed class AccountTests
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

    private static async Task<Account> CreateAsync(int profileId, string subject, string name, string type = "Savings", string currency = "NOK")
    {
        var response = await SendAsync(HttpMethod.Post, $"/api/profiles/{profileId}/accounts", subject, new { name, type, currencyCode = currency });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<Account>()).ShouldNotBeNull();
    }

    private static async Task<List<Account>> ListAsync(int profileId, string subject, bool includeArchived = false) =>
        (await (await SendAsync(HttpMethod.Get, $"/api/profiles/{profileId}/accounts?includeArchived={includeArchived}", subject)).Content.ReadFromJsonAsync<List<Account>>()).ShouldNotBeNull();

    [TestMethod]
    public async Task Creates_edits_archives_and_restores_an_account()
    {
        var profileId = await ProfileOfAsync("acc-owner-1");

        var created = await CreateAsync(profileId, "acc-owner-1", "  Savings DNB  ", "Savings");
        var renamed = await SendAsync(HttpMethod.Patch, $"/api/profiles/{profileId}/accounts/{created.Id}", "acc-owner-1", new { name = "Buffer", type = "Deposit", currencyCode = "EUR" });
        var archived = await SendAsync(HttpMethod.Patch, $"/api/profiles/{profileId}/accounts/{created.Id}", "acc-owner-1", new { archived = true });
        var active = await ListAsync(profileId, "acc-owner-1");
        var all = await ListAsync(profileId, "acc-owner-1", includeArchived: true);
        var restored = await SendAsync(HttpMethod.Patch, $"/api/profiles/{profileId}/accounts/{created.Id}", "acc-owner-1", new { archived = false });

        created.ShouldBe(new Account(created.Id, "Savings DNB", AccountType.Savings, "NOK", false));
        (await renamed.Content.ReadFromJsonAsync<Account>()).ShouldBe(new Account(created.Id, "Buffer", AccountType.Deposit, "EUR", false));
        (await archived.Content.ReadFromJsonAsync<Account>()).ShouldNotBeNull().Archived.ShouldBeTrue();
        active.ShouldBeEmpty();
        all.ShouldHaveSingleItem().Archived.ShouldBeTrue();
        (await restored.Content.ReadFromJsonAsync<Account>()).ShouldNotBeNull().Archived.ShouldBeFalse();
        (await ListAsync(profileId, "acc-owner-1")).ShouldHaveSingleItem().Name.ShouldBe("Buffer");
    }

    [TestMethod]
    public async Task Any_member_sees_and_edits_the_households_accounts()
    {
        var profileId = await ProfileOfAsync("acc-owner-2");
        var memberId = (await (await SendAsync(HttpMethod.Post, "/api/me", "acc-member-2")).Content.ReadFromJsonAsync<SignedIn>()).ShouldNotBeNull().User.Id;
        await AddMemberAsync(profileId, memberId);
        var account = await CreateAsync(profileId, "acc-owner-2", "Brokerage");

        var byMember = await SendAsync(HttpMethod.Patch, $"/api/profiles/{profileId}/accounts/{account.Id}", "acc-member-2", new { name = "Stocks" });

        byMember.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ListAsync(profileId, "acc-member-2")).ShouldHaveSingleItem().Name.ShouldBe("Stocks");
        (await CreateAsync(profileId, "acc-member-2", "Pension", "Pension", "USD")).CurrencyCode.ShouldBe("USD");
    }

    [TestMethod]
    public async Task Another_household_cannot_see_or_change_an_account()
    {
        var profileA = await ProfileOfAsync("acc-owner-3a");
        var profileB = await ProfileOfAsync("acc-owner-3b");
        var account = await CreateAsync(profileA, "acc-owner-3a", "Mine");

        // The account id exists, but not in household B: through B's own group it is not found.
        var asB = await SendAsync(HttpMethod.Patch, $"/api/profiles/{profileB}/accounts/{account.Id}", "acc-owner-3b", new { name = "Stolen" });
        var viaA = await SendAsync(HttpMethod.Get, $"/api/profiles/{profileA}/accounts", "acc-owner-3b");

        asB.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        viaA.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ListAsync(profileB, "acc-owner-3b")).ShouldBeEmpty();
        (await ListAsync(profileA, "acc-owner-3a")).ShouldHaveSingleItem().Name.ShouldBe("Mine");
    }

    [TestMethod]
    public async Task Names_are_unique_within_a_household_but_not_across_households()
    {
        var profileA = await ProfileOfAsync("acc-owner-4a");
        var profileB = await ProfileOfAsync("acc-owner-4b");
        var first = await CreateAsync(profileA, "acc-owner-4a", "Main");
        var other = await CreateAsync(profileA, "acc-owner-4a", "Other");

        var duplicate = await SendAsync(HttpMethod.Post, $"/api/profiles/{profileA}/accounts", "acc-owner-4a", new { name = "Main", type = "Current", currencyCode = "NOK" });
        var renameClash = await SendAsync(HttpMethod.Patch, $"/api/profiles/{profileA}/accounts/{other.Id}", "acc-owner-4a", new { name = "Main" });
        await CreateAsync(profileB, "acc-owner-4b", "Main");

        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        renameClash.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        first.Name.ShouldBe("Main");
    }

    [TestMethod]
    public async Task Invalid_accounts_are_refused()
    {
        var profileId = await ProfileOfAsync("acc-owner-5");
        var account = await CreateAsync(profileId, "acc-owner-5", "Valid");
        var path = $"/api/profiles/{profileId}/accounts";

        (await SendAsync(HttpMethod.Post, path, "acc-owner-5", new { name = " ", type = "Savings", currencyCode = "NOK" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await SendAsync(HttpMethod.Post, path, "acc-owner-5", new { name = "A", currencyCode = "NOK" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await SendAsync(HttpMethod.Post, path, "acc-owner-5", new { name = "B", type = "Crypto", currencyCode = "NOK" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await SendAsync(HttpMethod.Post, path, "acc-owner-5", new { name = "C", type = "Savings", currencyCode = "XXQ" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await SendAsync(HttpMethod.Patch, $"{path}/{account.Id}", "acc-owner-5", new { })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await SendAsync(HttpMethod.Patch, $"{path}/{account.Id}", "acc-owner-5", new { currencyCode = "nok" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await SendAsync(HttpMethod.Patch, $"{path}/999999", "acc-owner-5", new { name = "Nope" })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ListAsync(profileId, "acc-owner-5")).ShouldHaveSingleItem().ShouldBe(account);
    }

    // Until invitations are used here, members are added directly.
    private static async Task AddMemberAsync(int profileId, int userId)
    {
        await using var connection = new SqlConnection(_database);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "INSERT INTO app.ProfileMember (ProfileId, UserId, Role, CreatedBy, UpdatedBy) VALUES (@ProfileId, @UserId, 'Member', @UserId, @UserId);",
            connection);
        command.Parameters.AddWithValue("@ProfileId", profileId);
        command.Parameters.AddWithValue("@UserId", userId);
        await command.ExecuteNonQueryAsync();
    }
}
