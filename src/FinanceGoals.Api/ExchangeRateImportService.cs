using System.Threading.Channels;
using FinanceGoals.Application.Currencies;

namespace FinanceGoals.Api;

/// <summary>
/// Signals that the API is serving real requests (/api/...), which use the database anyway, so an
/// exchange-rate import can run without waking the free database on its own. Health checks and
/// CORS preflights never signal (docs/free-tier.md: uptime pings must not wake the database).
/// </summary>
internal sealed class ExchangeRateImportTrigger
{
    private readonly Channel<bool> _signals = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    /// <summary>True while a signal waits to be handled (for tests).</summary>
    public bool IsPending => _signals.Reader.Count > 0;

    public void Signal() => _signals.Writer.TryWrite(true);

    public ValueTask<bool> WaitAsync(CancellationToken cancellationToken) => _signals.Reader.ReadAsync(cancellationToken);

    /// <summary>
    /// Signals only for requests that matched an API endpoint. Not for unknown paths (a scanner's
    /// probes would otherwise start imports that wake the database), health checks or preflights.
    /// </summary>
    public static bool ShouldSignal(HttpContext context) =>
        context.GetEndpoint() is not null
        && context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
        && !HttpMethods.IsOptions(context.Request.Method);
}

/// <summary>
/// Imports the ECB rates when the app is in use: after an API request signals, it imports if the
/// last successful import is more than 7 days old (30 minutes after a failure). The 90-day feed
/// fills any days that were missed. Turned off with ExchangeRates:ImportEnabled=false (the tests do).
/// </summary>
internal sealed partial class ExchangeRateImportService : BackgroundService
{
    public const string EnabledSetting = "ExchangeRates:ImportEnabled";

    // Weekly for now (owner decision, #100).
    internal static readonly TimeSpan AfterSuccess = TimeSpan.FromDays(7);
    internal static readonly TimeSpan AfterFailure = TimeSpan.FromMinutes(30);

    private readonly IServiceProvider _services;
    private readonly ExchangeRateImportTrigger _trigger;
    private readonly TimeProvider _time;
    private readonly ILogger<ExchangeRateImportService> _logger;
    private DateTimeOffset? _nextImport;

    public ExchangeRateImportService(
        IServiceProvider services,
        ExchangeRateImportTrigger trigger,
        TimeProvider time,
        ILogger<ExchangeRateImportService> logger)
    {
        _services = services;
        _trigger = trigger;
        _time = time;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await _trigger.WaitAsync(stoppingToken);
            await ImportIfDueAsync(stoppingToken);
        }
    }

    /// <summary>Imports unless the next import isn't due yet. Never throws, except when the API stops.</summary>
    internal async Task ImportIfDueAsync(CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        if (_nextImport is { } next && now < next)
        {
            return;
        }

        try
        {
            var result = await _services.GetRequiredService<ExchangeRateImport>().RunAsync(cancellationToken);
            LogImported(result.Fetched, result.Changed);
            _nextImport = now + AfterSuccess;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Includes HttpClient timeouts (TaskCanceledException). A failed import must not stop the
            // API; conversions keep using the latest stored rate, and a later request retries.
            LogFailed(ex);
            _nextImport = now + AfterFailure;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Imported exchange rates: {Fetched} fetched, {Changed} changed")]
    private partial void LogImported(int fetched, int changed);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Exchange rate import failed")]
    private partial void LogFailed(Exception exception);
}
