namespace FinanceGoals.Api.Tests;

/// <summary>The web app runs on another origin; only the configured origins may call the API.</summary>
[TestClass]
public sealed class CorsTests
{
    private const string _webOrigin = "https://purple-beach-0ca81590f.3.azurestaticapps.net";

    [TestMethod]
    public async Task Allows_a_configured_origin()
    {
        var response = await PreflightAsync(_webOrigin, configured: _webOrigin);

        response.Headers.GetValues("Access-Control-Allow-Origin").ShouldBe([_webOrigin]);
    }

    [TestMethod]
    [DataRow("https://evil.example")]
    [DataRow("http://purple-beach-0ca81590f.3.azurestaticapps.net")]
    public async Task Refuses_any_other_origin(string origin)
    {
        var response = await PreflightAsync(origin, configured: _webOrigin);

        response.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
    }

    [TestMethod]
    public async Task Allows_no_origin_unless_configured()
    {
        var response = await PreflightAsync(_webOrigin, configured: null);

        response.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
    }

    private static async Task<HttpResponseMessage> PreflightAsync(string origin, string? configured)
    {
        var settings = configured is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string> { ["Cors:AllowedOrigins:0"] = configured };
        await using var api = new ApiFactory(ApiFactory.UnreachableDatabase, settings: settings);

        using var request = new HttpRequestMessage(HttpMethod.Options, new Uri("/api/currencies", UriKind.Relative));
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        return await api.CreateClient().SendAsync(request);
    }
}
