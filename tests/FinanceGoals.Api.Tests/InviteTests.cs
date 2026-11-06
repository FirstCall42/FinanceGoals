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
public sealed class InviteTests
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

    private static async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string subject, object? body = null, string? email = null)
    {
        var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative))
        {
            Content = body is null ? null : JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.For(subject: subject, email: email ?? $"{subject}@example.com"));
        return await _api!.CreateClient().SendAsync(request);
    }

    private static async Task<SignedIn> SignInAsync(string subject, bool createProfile = true) =>
        (await (await SendAsync(HttpMethod.Post, createProfile ? "/api/me" : "/api/me?createProfile=false", subject)).Content.ReadFromJsonAsync<SignedIn>()).ShouldNotBeNull();

    private static async Task<(int ProfileId, CreatedInvite Invite)> InviteAsync(string owner, string email)
    {
        var profileId = (await SignInAsync(owner)).Profiles[0].Id;
        var response = await SendAsync(HttpMethod.Post, $"/api/profiles/{profileId}/invites", owner, new { email });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (profileId, (await response.Content.ReadFromJsonAsync<CreatedInvite>()).ShouldNotBeNull());
    }

    private static Task<HttpResponseMessage> AcceptAsync(string token, string subject, string? email = null) =>
        SendAsync(HttpMethod.Post, $"/api/invites/{token}/accept", subject, email: email);

    [TestMethod]
    public async Task An_invite_adds_the_member_once_and_lasts_seven_days()
    {
        var (profileId, invite) = await InviteAsync("owner-1", "Invitee-1@Example.com");
        var invitee = await SignInAsync("invitee-1", createProfile: false);

        var pending = await (await SendAsync(HttpMethod.Get, $"/api/profiles/{profileId}/invites", "owner-1")).Content.ReadFromJsonAsync<List<PendingInvite>>();
        var accepted = await AcceptAsync(invite.Token, "invitee-1");
        var again = await AcceptAsync(invite.Token, "invitee-1");

        invitee.Profiles.ShouldBeEmpty();
        invite.ExpiresAt.ShouldBeInRange(DateTime.UtcNow.AddDays(7).AddMinutes(-5), DateTime.UtcNow.AddDays(7).AddMinutes(5));
        pending.ShouldNotBeNull().ShouldHaveSingleItem().Id.ShouldBe(invite.Id);
        accepted.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await accepted.Content.ReadFromJsonAsync<AcceptedInvite>()).ShouldBe(new AcceptedInvite(profileId));
        again.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var profiles = (await SignInAsync("invitee-1")).Profiles;
        profiles.ShouldHaveSingleItem().ShouldBe(new ProfileSummary(profileId, IProfileStore.DefaultName, ProfileRole.Member));
        (await SendAsync(HttpMethod.Get, $"/api/profiles/{profileId}/invites", "owner-1")).Content.ReadFromJsonAsync<List<PendingInvite>>().Result.ShouldNotBeNull().ShouldBeEmpty();
    }

    [TestMethod]
    public async Task Another_google_account_cannot_use_the_invite()
    {
        var (_, invite) = await InviteAsync("owner-2", "invitee-2@example.com");

        var wrong = await AcceptAsync(invite.Token, "someone-else-2");
        var right = await AcceptAsync(invite.Token, "invitee-2");

        wrong.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        right.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [TestMethod]
    public async Task An_expired_or_withdrawn_invite_does_not_work()
    {
        var (profileId, expired) = await InviteAsync("owner-3", "invitee-3@example.com");
        var (_, withdrawn) = await InviteAsync("owner-3", "invitee-3@example.com");
        await ExecuteAsync($"UPDATE app.ProfileInvite SET ExpiresAt = DATEADD(minute, -1, SYSUTCDATETIME()) WHERE Id = {expired.Id};");

        var revoked = await SendAsync(HttpMethod.Delete, $"/api/profiles/{profileId}/invites/{withdrawn.Id}", "owner-3");
        var revokedAgain = await SendAsync(HttpMethod.Delete, $"/api/profiles/{profileId}/invites/{withdrawn.Id}", "owner-3");

        revoked.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        revokedAgain.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await AcceptAsync(expired.Token, "invitee-3")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await AcceptAsync(withdrawn.Token, "invitee-3")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await AcceptAsync("not-a-real-token", "invitee-3")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [TestMethod]
    public async Task Only_owners_invite_and_the_email_must_look_like_one()
    {
        var (profileId, invite) = await InviteAsync("owner-4", "member-4@example.com");
        await AcceptAsync(invite.Token, "member-4");

        (await SendAsync(HttpMethod.Post, $"/api/profiles/{profileId}/invites", "member-4", new { email = "x@example.com" })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Get, $"/api/profiles/{profileId}/invites", "member-4")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Post, $"/api/profiles/{profileId}/invites", "owner-4", new { email = "not an email" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [TestMethod]
    public async Task Only_the_hash_of_the_token_is_stored()
    {
        var (_, invite) = await InviteAsync("owner-5", "invitee-5@example.com");

        var stored = await SqlServer.QueryAsync(_database, $"SELECT CONVERT(varchar(64), TokenHash, 2) FROM app.ProfileInvite WHERE Id = {invite.Id};");

        stored.ShouldHaveSingleItem().ShouldBe(Convert.ToHexString(InviteTokens.Hash(invite.Token)));
        invite.Token.Length.ShouldBeGreaterThanOrEqualTo(43);
    }

    private static async Task ExecuteAsync(string sql)
    {
        await using var connection = new SqlConnection(_database);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
