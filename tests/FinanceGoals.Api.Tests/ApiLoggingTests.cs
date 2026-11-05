using System.Text.Json;
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
    [DataRow("/health/ready", 503, LogEventLevel.Error)]
    [DataRow("/api/accounts", 200, LogEventLevel.Information)]
    [DataRow("/api/accounts", 500, LogEventLevel.Error)]
    public void Successful_health_probes_stay_out_of_the_request_log(string path, int status, LogEventLevel expected)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.StatusCode = status;

        ApiLogging.RequestLevel(context, error: null).ShouldBe(expected);
    }
}
