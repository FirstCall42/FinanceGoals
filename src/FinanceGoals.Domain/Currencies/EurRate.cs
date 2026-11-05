namespace FinanceGoals.Domain.Currencies;

/// <summary>
/// How many units of a currency one euro buys on a date, as the ECB publishes its reference
/// rates (e.g. NOK 11.65 on 2026-11-05). Rates are stored against EUR; any other pair is a cross rate.
/// </summary>
public sealed record EurRate
{
    public EurRate(string currencyCode, DateOnly date, decimal unitsPerEur)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currencyCode);
        if (unitsPerEur <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(unitsPerEur), unitsPerEur, "A rate must be positive.");
        }

        CurrencyCode = currencyCode;
        Date = date;
        UnitsPerEur = unitsPerEur;
    }

    public string CurrencyCode { get; }

    public DateOnly Date { get; }

    public decimal UnitsPerEur { get; }
}

/// <summary>Where conversion finds its rates (the ECB import, #31, stores them).</summary>
public interface IEurRates
{
    /// <summary>The latest rate for the currency on or before the given day, or null if there is none.</summary>
    EurRate? LatestOnOrBefore(string currencyCode, DateOnly day);
}
