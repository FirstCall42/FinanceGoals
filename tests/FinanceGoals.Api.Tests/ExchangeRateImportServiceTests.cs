using FinanceGoals.Application.Currencies;
using FinanceGoals.Domain.Currencies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanceGoals.Api.Tests;

[TestClass]
public sealed class ExchangeRateImportServiceTests
{
    private readonly IExchangeRateSource _source = Substitute.For<IExchangeRateSource>();
    private readonly IExchangeRateStore _store = Substitute.For<IExchangeRateStore>();
    private readonly ManualTime _time = new(new DateTimeOffset(2026, 11, 6, 8, 0, 0, TimeSpan.Zero));

    [TestMethod]
    public async Task A_timed_out_import_is_logged_and_retried_later_without_stopping_the_api()
    {
        // HttpClient reports its own timeout as a TaskCanceledException.
        _source.FetchAsync(Arg.Any<CancellationToken>()).Returns<IReadOnlyList<EurRate>>(_ => throw new TaskCanceledException("timeout"));
        var service = Service();

        await Should.NotThrowAsync(() => service.ImportIfDueAsync(CancellationToken.None));
        await service.ImportIfDueAsync(CancellationToken.None);
        _time.Advance(ExchangeRateImportService.AfterFailure);
        await service.ImportIfDueAsync(CancellationToken.None);

        await _source.Received(2).FetchAsync(Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task A_successful_import_waits_a_week_before_the_next()
    {
        _source.FetchAsync(Arg.Any<CancellationToken>()).Returns([new EurRate("NOK", new DateOnly(2026, 11, 5), 11.68m)]);
        var service = Service();

        await service.ImportIfDueAsync(CancellationToken.None);
        _time.Advance(ExchangeRateImportService.AfterSuccess - TimeSpan.FromMinutes(1));
        await service.ImportIfDueAsync(CancellationToken.None);
        await _source.Received(1).FetchAsync(Arg.Any<CancellationToken>());

        _time.Advance(TimeSpan.FromMinutes(1));
        await service.ImportIfDueAsync(CancellationToken.None);
        await _source.Received(2).FetchAsync(Arg.Any<CancellationToken>());
        await _store.Received(2).SaveAsync(Arg.Any<IReadOnlyList<EurRate>>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task A_new_instance_does_not_import_when_the_store_has_rates_from_the_last_week()
    {
        _store.LatestOnOrBefore("NOK", Arg.Any<DateOnly>()).Returns(new EurRate("NOK", new DateOnly(2026, 11, 4), 11.68m));

        await Service().ImportIfDueAsync(CancellationToken.None);

        await _source.DidNotReceive().FetchAsync(Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task A_new_instance_imports_when_the_newest_stored_rate_is_a_week_old()
    {
        _store.LatestOnOrBefore("NOK", Arg.Any<DateOnly>()).Returns(new EurRate("NOK", new DateOnly(2026, 10, 30), 11.68m));
        _source.FetchAsync(Arg.Any<CancellationToken>()).Returns([new EurRate("NOK", new DateOnly(2026, 11, 5), 11.7m)]);

        await Service().ImportIfDueAsync(CancellationToken.None);

        await _source.Received(1).FetchAsync(Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Stopping_the_api_cancels_the_import()
    {
        using var stopping = new CancellationTokenSource();
        await stopping.CancelAsync();
        _source.FetchAsync(Arg.Any<CancellationToken>()).Returns<IReadOnlyList<EurRate>>(call => throw new OperationCanceledException(call.Arg<CancellationToken>()));

        await Should.ThrowAsync<OperationCanceledException>(() => Service().ImportIfDueAsync(stopping.Token));
    }

    [TestMethod]
    [DataRow("GET", "/api/currencies", true, true)]
    [DataRow("GET", "/api/v1/users", false, false)]
    [DataRow("OPTIONS", "/api/currencies", true, false)]
    [DataRow("GET", "/health/live", true, false)]
    [DataRow("GET", "/health/ready", true, false)]
    [DataRow("GET", "/", false, false)]
    public void Only_requests_that_matched_an_api_endpoint_may_start_an_import(string method, string path, bool matched, bool expected)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        if (matched)
        {
            context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, EndpointMetadataCollection.Empty, path));
        }

        ExchangeRateImportTrigger.ShouldSignal(context).ShouldBe(expected);
    }

    [TestMethod]
    public async Task A_liveness_ping_or_unknown_path_does_not_signal_but_an_api_request_does()
    {
        await using var api = new ApiFactory(ApiFactory.UnreachableDatabase);
        var client = api.CreateClient();
        var trigger = api.Services.GetRequiredService<ExchangeRateImportTrigger>();

        await client.GetAsync(new Uri("/health/live", UriKind.Relative));
        await client.GetAsync(new Uri("/api/v1/users", UriKind.Relative));
        trigger.IsPending.ShouldBeFalse();

        await client.GetAsync(new Uri("/api/currencies", UriKind.Relative));
        trigger.IsPending.ShouldBeTrue();
    }

    private ExchangeRateImportService Service()
    {
        var services = new ServiceCollection()
            .AddSingleton(new ExchangeRateImport(_source, _store))
            .AddSingleton<IEurRates>(_store)
            .BuildServiceProvider();
        return new ExchangeRateImportService(services, new ExchangeRateImportTrigger(), _time, NullLogger<ExchangeRateImportService>.Instance);
    }

    private sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset _now;

        public ManualTime(DateTimeOffset start)
        {
            _now = start;
        }

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
