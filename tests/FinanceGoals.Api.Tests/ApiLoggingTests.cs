using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Events;

namespace FinanceGoals.Api.Tests;

[TestClass]
public sealed class ApiLoggingTests
{
    [TestMethod]
    public void Events_are_written_as_json_with_their_properties()
    {
        var output = new StringWriter();
        var configuration = new ConfigurationBuilder().Build();
        using (var logger = ApiLogging.Configure(new LoggerConfiguration(), configuration, output).CreateLogger())
        {
            logger.Information("Converted {Amount} {Currency}", 125.50m, "NOK");
        }

        using var line = JsonDocument.Parse(output.ToString().Trim());
        line.RootElement.GetProperty("@m").GetString().ShouldBe("Converted 125.50 \"NOK\"");
        line.RootElement.GetProperty("Amount").GetDecimal().ShouldBe(125.50m);
        line.RootElement.GetProperty("Currency").GetString().ShouldBe("NOK");
    }

    [TestMethod]
    public void Minimum_level_comes_from_configuration()
    {
        var output = new StringWriter();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Serilog:MinimumLevel:Default"] = "Warning" })
            .Build();
        using (var logger = ApiLogging.Configure(new LoggerConfiguration(), configuration, output).CreateLogger())
        {
            logger.Information("hidden");
            logger.Warning("shown");
        }

        output.ToString().ShouldNotContain("hidden");
        output.ToString().ShouldContain("shown");
    }

    [TestMethod]
    [DataRow("/health/live", 200, LogEventLevel.Verbose)]
    [DataRow("/health/ready", 503, LogEventLevel.Verbose)]
    [DataRow("/api/accounts", 200, LogEventLevel.Information)]
    [DataRow("/api/accounts", 404, LogEventLevel.Information)]
    [DataRow("/api/accounts", 500, LogEventLevel.Error)]
    public void Health_probes_stay_out_of_the_request_log(string path, int status, LogEventLevel expected)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.StatusCode = status;

        ApiLogging.RequestLevel(context, error: null).ShouldBe(expected);
    }

    [TestMethod]
    public async Task Running_api_logs_requests_as_json_but_not_health_probes()
    {
        await using var api = new ApiFactory(ApiFactory.UnreachableDatabase);
        var client = api.CreateClient();

        await client.GetAsync(new Uri("/health/live", UriKind.Relative));
        await client.GetAsync(new Uri("/health/ready", UriKind.Relative));
        await client.GetAsync(new Uri("/no-such-page", UriKind.Relative));

        // The request log is written as the request completes, so wait briefly for it.
        var requests = await RequestLogAsync(api, until: lines => lines.Any(line => line["RequestPath"]?.GetValue<string>() == "/no-such-page"));
        var paths = requests.Select(line => line["RequestPath"]!.GetValue<string>()).ToList();
        paths.ShouldBe(["/no-such-page"]);
        requests.Single()["StatusCode"]!.GetValue<int>().ShouldBe(404);
    }

    private static async Task<List<JsonNode>> RequestLogAsync(ApiFactory api, Func<List<JsonNode>, bool> until)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (true)
        {
            // Every line must be JSON: the API logs nothing as plain text.
            var lines = api.Log.ToString()
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(line => JsonNode.Parse(line)!)
                // Request-log events carry the status and elapsed time; other events logged during a
                // request (e.g. the health check service reporting a failed check) only share its path.
                .Where(line => line["RequestPath"] is not null && line["StatusCode"] is not null && line["Elapsed"] is not null)
                .ToList();
            if (until(lines) || DateTime.UtcNow > deadline)
            {
                return lines;
            }

            await Task.Delay(50);
        }
    }
}
