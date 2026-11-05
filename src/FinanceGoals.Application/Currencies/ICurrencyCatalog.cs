namespace FinanceGoals.Application.Currencies;

/// <summary>A currency the app knows, as seeded from ISO 4217.</summary>
public sealed record CurrencyInfo(string Code, short NumericCode, byte MinorUnits, string Name);

/// <summary>The currencies the app supports.</summary>
public interface ICurrencyCatalog
{
    /// <summary>All currencies, ordered by code.</summary>
    Task<IReadOnlyList<CurrencyInfo>> ListAsync(CancellationToken cancellationToken);
}
