using FinanceGoals.Domain.Currencies;

namespace FinanceGoals.Application.Currencies;

/// <summary>Every rate a calculation can need, in one query, so converting many amounts on many dates doesn't ask the database each time.</summary>
public interface IRateHistory
{
    /// <summary>The rates of these currencies up to and including a day, oldest first.</summary>
    Task<IReadOnlyList<EurRate>> ListAsync(IReadOnlyCollection<string> currencyCodes, DateOnly upTo, CancellationToken cancellationToken);
}

/// <summary>
/// Rates held in memory, answering the question the converter asks: the latest rate of a currency on
/// or before a day. A currency without a rate that early has none: it is never invented.
/// </summary>
public sealed class InMemoryEurRates : IEurRates
{
    private readonly Dictionary<string, EurRate[]> _byCurrency;

    public InMemoryEurRates(IEnumerable<EurRate> rates)
    {
        ArgumentNullException.ThrowIfNull(rates);
        _byCurrency = rates
            .GroupBy(rate => rate.CurrencyCode)
            .ToDictionary(group => group.Key, group => group.OrderBy(rate => rate.Date).ToArray());
    }

    public EurRate? LatestOnOrBefore(string currencyCode, DateOnly day)
    {
        if (!_byCurrency.TryGetValue(currencyCode, out var rates))
        {
            return null;
        }

        // Binary search for the last rate dated on or before the day.
        int low = 0, high = rates.Length - 1, found = -1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            if (rates[middle].Date <= day)
            {
                found = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return found < 0 ? null : rates[found];
    }
}
