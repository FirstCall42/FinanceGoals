extern alias migrator;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FinanceGoals.Application.Users;
using FinanceGoals.Migrator.Tests;
using migrator::FinanceGoals.Migrator;

namespace FinanceGoals.Api.Tests;

[TestClass]
public sealed class SignInTests
{
    private static readonly Uri _me = new("/api/me", UriKind.Relative);

    private static HttpRequestMessage SignIn(string? token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, _me);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return request;
    }

    [TestMethod]
    public async Task Records_the_user_on_first_sign_in_and_refreshes_them_after()
    {
        var database = await SqlServer.CreateDatabaseAsync();
        var password = SqlServer.NewPassword();
        new DatabaseMigrator(database, TextWriter.Null).Run(AppUser.WithPassword("financegoals_app", password));
        await using var api = new ApiFactory(SqlServer.As(database, "financegoals_app", password));
        var client = api.CreateClient();

        var first = await client.SendAsync(SignIn(TestTokens.For(email: "old@example.com", name: "Old name")));
        var again = await client.SendAsync(SignIn(TestTokens.For(email: "new@example.com", name: null)));
        var other = await client.SendAsync(SignIn(TestTokens.For(subject: "google-subject-2")));

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        var firstUser = (await first.Content.ReadFromJsonAsync<User>()).ShouldNotBeNull();
        firstUser.Email.ShouldBe("old@example.com");
        firstUser.Name.ShouldBe("Old name");
        var againUser = (await again.Content.ReadFromJsonAsync<User>()).ShouldNotBeNull();
        againUser.ShouldBe(new User(firstUser.Id, "new@example.com", null));
        (await other.Content.ReadFromJsonAsync<User>()).ShouldNotBeNull().Id.ShouldNotBe(firstUser.Id);
    }

    [TestMethod]
    public async Task Refuses_a_missing_or_invalid_token()
    {
        await using var api = new ApiFactory(ApiFactory.UnreachableDatabase);
        var client = api.CreateClient();

        string?[] tokens =
        [
            null,
            "not-a-token",
            TestTokens.For(audience: "someone-else.apps.googleusercontent.com"),
            TestTokens.For(issuer: "https://evil.example.com"),
            TestTokens.For(expires: DateTime.UtcNow.AddMinutes(-10)),
            TestTokens.For(otherKey: true),
        ];
        foreach (var token in tokens)
        {
            (await client.SendAsync(SignIn(token))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized, token ?? "no token");
        }
    }

    [TestMethod]
    public async Task Refuses_an_unverified_email()
    {
        await using var api = new ApiFactory(ApiFactory.UnreachableDatabase);

        var response = await api.CreateClient().SendAsync(SignIn(TestTokens.For(emailVerified: false)));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [TestMethod]
    public async Task Refuses_every_token_when_no_client_id_is_set()
    {
        await using var api = new ApiFactory(ApiFactory.UnreachableDatabase, settings: new Dictionary<string, string> { [GoogleSignIn.ClientIdSetting] = "" });

        var response = await api.CreateClient().SendAsync(SignIn(TestTokens.For()));
        var config = await api.CreateClient().GetFromJsonAsync<SignInConfig>(new Uri("/api/auth/config", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        config.ShouldNotBeNull().GoogleClientId.ShouldBeNull();
    }

    [TestMethod]
    public async Task Tells_the_web_app_the_client_id_without_sign_in()
    {
        await using var api = new ApiFactory(ApiFactory.UnreachableDatabase);

        var config = await api.CreateClient().GetFromJsonAsync<SignInConfig>(new Uri("/api/auth/config", UriKind.Relative));

        config.ShouldNotBeNull().GoogleClientId.ShouldBe(TestTokens.ClientId);
    }
}
