using FinanceGoals.Application.Currencies;

namespace FinanceGoals.Api;

/// <summary>
/// Imports the ECB rates when the API starts and every six hours while it runs. The API scales to
/// zero when idle, so the import only runs while the app is in use: it never wakes the free
/// database on its own (docs/free-tier.md). The 90-day feed fills any days that were missed.
/// Turned off with ExchangeRates:ImportEnabled=false (the tests do).
/// </summary>
internal sealed partial class ExchangeRateImportService : BackgroundService
{
    public const string EnabledSetting = "ExchangeRates:ImportEnabled";

    private static readonly TimeSpan _interval = TimeSpan.FromHours(6);

    private readonly IServiceProvider _services;
    private readonly ILogger<ExchangeRateImportService> _logger;

    public ExchangeRateImportService(IServiceProvider services, ILogger<ExchangeRateImportService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);
        do
        {
            await ImportOnceAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task ImportOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _services.GetRequiredService<ExchangeRateImport>().RunAsync(cancellationToken);
            LogImported(result.Fetched, result.Changed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A failed import must not stop the API; the next run (or a restart) tries again, and
            // conversions keep using the latest stored rate.
            LogFailed(ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Imported exchange rates: {Fetched} fetched, {Changed} changed")]
    private partial void LogImported(int fetched, int changed);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Exchange rate import failed")]
    private partial void LogFailed(Exception exception);
}
