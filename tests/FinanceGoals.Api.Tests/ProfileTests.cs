extern alias migrator;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FinanceGoals.Application.Profiles;
using FinanceGoals.Migrator.Tests;
using Microsoft.Data.SqlClient;
using migrator::FinanceGoals.Migrator;

namespace FinanceGoals.Api.Tests;

[TestClass]
public sealed class ProfileTests
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

    private static async Task<SignedIn> SignInAsync(string subject) =>
        (await (await SendAsync(HttpMethod.Post, "/api/me", subject)).Content.ReadFromJsonAsync<SignedIn>()).ShouldNotBeNull();

    [TestMethod]
    public async Task The_first_sign_in_creates_a_profile_owned_by_the_user_and_later_ones_keep_it()
    {
        var first = await SignInAsync("first-owner");
        var again = await SignInAsync("first-owner");

        var profile = first.Profiles.ShouldHaveSingleItem();
        profile.Name.ShouldBe(IProfileStore.DefaultName);
        profile.Role.ShouldBe(ProfileRole.Owner);
        again.Profiles.ShouldBe(first.Profiles);
        (await (await SendAsync(HttpMethod.Post, "/api/me", "first-owner")).Content.ReadAsStringAsync()).ShouldContain("\"role\":\"Owner\"");
    }

    [TestMethod]
    public async Task Simultaneous_first_sign_ins_create_one_profile()
    {
        var signIns = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => SignInAsync("racing-owner")));

        signIns.SelectMany(s => s.Profiles).Select(p => p.Id).Distinct().ShouldHaveSingleItem();
        signIns.Select(s => s.User.Id).Distinct().ShouldHaveSingleItem();
    }

    [TestMethod]
    public async Task An_owner_reads_renames_and_lists_their_profile()
    {
        var profileId = (await SignInAsync("owner-a")).Profiles[0].Id;

        var renamed = await SendAsync(HttpMethod.Patch, $"/api/profiles/{profileId}", "owner-a", new { name = "  The Lunds  " });
        var profile = await (await SendAsync(HttpMethod.Get, $"/api/profiles/{profileId}", "owner-a")).Content.ReadFromJsonAsync<ProfileSummary>();
        var members = await (await SendAsync(HttpMethod.Get, $"/api/profiles/{profileId}/members", "owner-a")).Content.ReadFromJsonAsync<List<ProfileMember>>();

        renamed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        profile.ShouldBe(new ProfileSummary(profileId, "The Lunds", ProfileRole.Owner));
        members.ShouldNotBeNull().ShouldHaveSingleItem().Email.ShouldBe("owner-a@example.com");
        (await SendAsync(HttpMethod.Patch, $"/api/profiles/{profileId}", "owner-a", new { name = " " })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [TestMethod]
    public async Task A_member_can_read_but_not_manage_the_profile()
    {
        var profileId = (await SignInAsync("owner-b")).Profiles[0].Id;
        var memberId = (await SignInAsync("member-b")).User.Id;
        await AddMemberAsync(profileId, memberId);

        (await SendAsync(HttpMethod.Get, $"/api/profiles/{profileId}", "member-b")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Get, $"/api/profiles/{profileId}/members", "member-b")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Patch, $"/api/profiles/{profileId}", "member-b", new { name = "Mine" })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await SignInAsync("member-b")).Profiles.Select(p => (p.Id, p.Role)).ShouldContain((profileId, ProfileRole.Member));
    }

    [TestMethod]
    public async Task Someone_outside_the_profile_gets_not_found_on_every_endpoint()
    {
        var profileId = (await SignInAsync("owner-c")).Profiles[0].Id;
        await SignInAsync("stranger-c");

        (await SendAsync(HttpMethod.Get, $"/api/profiles/{profileId}", "stranger-c")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await SendAsync(HttpMethod.Get, $"/api/profiles/{profileId}/members", "stranger-c")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await SendAsync(HttpMethod.Patch, $"/api/profiles/{profileId}", "stranger-c", new { name = "Taken" })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await SendAsync(HttpMethod.Get, "/api/profiles/999999", "owner-c")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [TestMethod]
    public async Task Profile_endpoints_need_a_signed_in_user()
    {
        var response = await _api!.CreateClient().GetAsync(new Uri("/api/profiles/1", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // Until invitations exist (#26), members are added directly.
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
