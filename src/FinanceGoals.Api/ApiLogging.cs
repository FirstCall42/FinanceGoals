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
    /// fill the logs (and the free Log Analytics allowance) with noise. They stay out of the
    /// request log even when they fail: the health check service already logs a failing check
    /// once per probe, and a second event per probe during an outage would only double the volume.
    /// </summary>
    public static LogEventLevel RequestLevel(HttpContext context, Exception? error)
    {
        if (context.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase))
        {
            return LogEventLevel.Verbose;
        }

        return error is not null || context.Response.StatusCode >= 500 ? LogEventLevel.Error : LogEventLevel.Information;
    }

    /// <summary>
    /// Where the logs go. The API registers none and logs to standard output; tests register
    /// their own to read what the running API writes.
    /// </summary>
    internal sealed class Output
    {
        public Output(TextWriter writer)
        {
            Writer = writer;
        }

        public TextWriter Writer { get; }
    }

    /// <summary>
    /// Writes each event as one line of compact JSON to any TextWriter, so tests can capture
    /// it. Requests log concurrently, so writes are serialized.
    /// </summary>
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
            }
        }
    }
}
