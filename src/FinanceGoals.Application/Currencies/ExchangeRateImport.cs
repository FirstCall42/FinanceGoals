using FinanceGoals.Domain.Currencies;

namespace FinanceGoals.Application.Currencies;

/// <summary>Where rates come from (the ECB's published reference rates).</summary>
public interface IExchangeRateSource
{
    Task<IReadOnlyList<EurRate>> FetchAsync(CancellationToken cancellationToken);
}

/// <summary>Where rates are kept. Reading follows <see cref="IEurRates"/>: the latest rate on or before a day.</summary>
public interface IExchangeRateStore : IEurRates
{
    /// <summary>Adds new rates and corrects changed ones; returns how many rows changed.</summary>
    Task<int> SaveAsync(IReadOnlyList<EurRate> rates, CancellationToken cancellationToken);
}

/// <summary>Fetches the published rates and stores them. Running it again for the same day changes nothing.</summary>
public sealed class ExchangeRateImport
{
    private readonly IExchangeRateSource _source;
    private readonly IExchangeRateStore _store;

    public ExchangeRateImport(IExchangeRateSource source, IExchangeRateStore store)
    {
        _source = source;
        _store = store;
    }

    public async Task<ImportResult> RunAsync(CancellationToken cancellationToken)
    {
        var rates = await _source.FetchAsync(cancellationToken);
        var changed = await _store.SaveAsync(rates, cancellationToken);
        return new ImportResult(rates.Count, changed);
    }
}

public sealed record ImportResult(int Fetched, int Changed);
