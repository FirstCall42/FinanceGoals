using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace FinanceGoals.Api;

/// <summary>
/// Structured logging: one JSON object per event on standard output, which Container Apps
/// collects. Levels come from the "Serilog" configuration section.
/// </summary>
internal static class ApiLogging
{
    public static LoggerConfiguration Configure(LoggerConfiguration logger, IConfiguration configuration, TextWriter output) =>
        logger
            .ReadFrom.Configuration(configuration)
            .Enrich.FromLogContext()
            .WriteTo.Sink(new JsonLinesSink(output));

    /// <summary>
    /// Request log level. Health probes hit the API every few seconds; logging each one would
    /// fill the logs (and the free Log Analytics allowance) with noise, so they are logged only
    /// when they fail.
    /// </summary>
    public static LogEventLevel RequestLevel(HttpContext context, Exception? error)
    {
        if (error is not null || context.Response.StatusCode >= 500)
        {
            return LogEventLevel.Error;
        }

        return context.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase)
            ? LogEventLevel.Verbose
            : LogEventLevel.Information;
    }

    /// <summary>Writes each event as one line of compact JSON. Requests log concurrently, so writes are serialized.</summary>
    private sealed class JsonLinesSink : ILogEventSink
    {
        private readonly RenderedCompactJsonFormatter _formatter = new();
        private readonly TextWriter _output;
        private readonly Lock _lock = new();

        public JsonLinesSink(TextWriter output)
        {
            _output = output;
        }

        public void Emit(LogEvent logEvent)
        {
            lock (_lock)
            {
                _formatter.Format(logEvent, _output);
                _output.Flush();
            }
        }
    }
}
