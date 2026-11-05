extern alias migrator;

using FinanceGoals.Application.Currencies;
using FinanceGoals.Domain.Currencies;
using FinanceGoals.Infrastructure.Currencies;
using FinanceGoals.Migrator.Tests;
using Microsoft.Extensions.Configuration;
using migrator::FinanceGoals.Migrator;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace FinanceGoals.Infrastructure.Tests.Currencies;

/// <summary>The ECB import end to end: a stubbed feed, and the store on a migrated SQL Server.</summary>
[TestClass]
public sealed class ExchangeRateImportTests
{
    // Thursday and Friday, 5 and 6 November 2026, in the ECB's format. XQA is not an ISO currency
    // the app knows, so it must be skipped.
    private const string _feed = """
        <?xml version="1.0" encoding="UTF-8"?>
        <gesmes:Envelope xmlns:gesmes="http://www.gesmes.org/xml/2002-08-01" xmlns="http://www.ecb.int/vocabulary/2002-08-01/eurofxref">
          <gesmes:subject>Reference rates</gesmes:subject>
          <Cube>
            <Cube time='2026-11-06'>
              <Cube currency='USD' rate='1.1250'/>
              <Cube currency='NOK' rate='11.7010'/>
            </Cube>
            <Cube time='2026-11-05'>
              <Cube currency='USD' rate='1.1224'/>
              <Cube currency='NOK' rate='11.6835'/>
              <Cube currency='XQA' rate='2.5'/>
            </Cube>
          </Cube>
        </gesmes:Envelope>
        """;

    private static readonly DateOnly _thursday = new(2026, 11, 5);
    private static readonly DateOnly _friday = new(2026, 11, 6);

    [TestMethod]
    public async Task Reads_the_ecb_feed()
    {
        using var ecb = Ecb(_feed);

        var rates = await Source(ecb).FetchAsync(CancellationToken.None);

        rates.Count.ShouldBe(5);
        rates.ShouldContain(new EurRate("NOK", _friday, 11.7010m));
        rates.ShouldContain(new EurRate("USD", _thursday, 1.1224m));
    }

    [TestMethod]
    public async Task Stores_rates_and_a_rerun_changes_nothing()
    {
        using var ecb = Ecb(_feed);
        var store = await StoreOnMigratedDatabaseAsync();
        var import = new ExchangeRateImport(Source(ecb), store);

        var first = await import.RunAsync(CancellationToken.None);
        var second = await import.RunAsync(CancellationToken.None);

        first.ShouldBe(new ImportResult(Fetched: 5, Changed: 4)); // XQA is skipped
        second.ShouldBe(new ImportResult(Fetched: 5, Changed: 0));
    }

    [TestMethod]
    public async Task A_corrected_rate_replaces_the_stored_one()
    {
        var store = await StoreOnMigratedDatabaseAsync();
        await store.SaveAsync([new EurRate("NOK", _friday, 11.70m)], CancellationToken.None);

        var changed = await store.SaveAsync([new EurRate("NOK", _friday, 11.71m)], CancellationToken.None);

        changed.ShouldBe(1);
        store.LatestOnOrBefore("NOK", _friday).ShouldBe(new EurRate("NOK", _friday, 11.71m));
    }

    [TestMethod]
    public async Task Finds_the_latest_rate_on_or_before_a_day()
    {
        var store = await StoreOnMigratedDatabaseAsync();
        await store.SaveAsync(
            [new EurRate("NOK", _thursday, 11.6835m), new EurRate("NOK", _friday, 11.7010m), new EurRate("USD", _friday, 1.1250m)],
            CancellationToken.None);

        store.LatestOnOrBefore("NOK", _thursday).ShouldBe(new EurRate("NOK", _thursday, 11.6835m));
        store.LatestOnOrBefore("NOK", _friday).ShouldBe(new EurRate("NOK", _friday, 11.7010m));
        // Saturday and Sunday have no ECB rate: Friday's applies.
        store.LatestOnOrBefore("NOK", new DateOnly(2026, 11, 8)).ShouldBe(new EurRate("NOK", _friday, 11.7010m));
        store.LatestOnOrBefore("NOK", new DateOnly(2026, 11, 4)).ShouldBeNull();
        store.LatestOnOrBefore("RON", _friday).ShouldBeNull();
    }

    [TestMethod]
    public async Task Stored_rates_drive_the_converter()
    {
        var store = await StoreOnMigratedDatabaseAsync();
        await store.SaveAsync([new EurRate("NOK", _friday, 11.70m), new EurRate("USD", _friday, 1.125m)], CancellationToken.None);

        var result = new CurrencyConverter(store).Convert(new Money(1170m, new Currency("NOK", 2)), new Currency("USD", 2), new DateOnly(2026, 11, 7));

        result.ShouldBeOfType<Conversion.Converted>().Amount.ShouldBe(new Money(112.5m, new Currency("USD", 2)));
    }

    private static WireMockServer Ecb(string body)
    {
        var server = WireMockServer.Start();
        server.Given(Request.Create().WithPath("/eurofxref-hist-90d.xml").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "text/xml").WithBody(body));
        return server;
    }

    private static EcbExchangeRateSource Source(WireMockServer ecb) =>
        new(new HttpClient { BaseAddress = new Uri($"{ecb.Url}/eurofxref-hist-90d.xml") });

    /// <summary>A database migrated by the real scripts, read and written as the app user, as in Azure.</summary>
    private static async Task<SqlExchangeRateStore> StoreOnMigratedDatabaseAsync()
    {
        var database = await SqlServer.CreateDatabaseAsync();
        var password = SqlServer.NewPassword();
        new DatabaseMigrator(database, TextWriter.Null).Run(AppUser.WithPassword("financegoals_app", password));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{InfrastructureServices.ConnectionStringName}"] = SqlServer.As(database, "financegoals_app", password),
            })
            .Build();
        return new SqlExchangeRateStore(configuration);
    }
}
