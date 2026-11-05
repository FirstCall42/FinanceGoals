namespace FinanceGoals.Domain.Currencies;

/// <summary>The outcome of a conversion: an amount, or missing rates the UI must warn about.</summary>
public abstract record Conversion
{
    private Conversion()
    {
    }

    /// <summary>
    /// The converted amount at full precision (round it for display), and the date of the oldest
    /// rate used, so the UI can say which day's rate it is. Same-currency conversions use no rate:
    /// <see cref="RateDate"/> is then null.
    /// </summary>
    public sealed record Converted : Conversion
    {
        public Converted(Money amount, DateOnly? rateDate)
        {
            Amount = amount;
            RateDate = rateDate;
        }

        public Money Amount { get; }

        public DateOnly? RateDate { get; }
    }

    /// <summary>
    /// No rate on or before the date for one or both currencies. There is deliberately no amount:
    /// a missing rate never becomes 1.0.
    /// </summary>
    public sealed record RateMissing : Conversion
    {
        public RateMissing(IReadOnlyList<string> currencyCodes, DateOnly date)
        {
            CurrencyCodes = currencyCodes;
            Date = date;
        }

        public IReadOnlyList<string> CurrencyCodes { get; }

        public DateOnly Date { get; }

        public string Warning => $"No exchange rate for {string.Join(" and ", CurrencyCodes)} on or before {Date:yyyy-MM-dd}.";
    }
}

/// <summary>
/// Converts amounts between currencies on a date, through EUR: every rate is "units per euro", so
/// X to Y is amount / rate(X) * rate(Y), each the latest rate on or before the date.
/// </summary>
public sealed class CurrencyConverter
{
    private const string _eur = "EUR";

    private readonly IEurRates _rates;

    public CurrencyConverter(IEurRates rates)
    {
        _rates = rates;
    }

    public Conversion Convert(Money amount, Currency target, DateOnly date)
    {
        if (amount.Currency == target)
        {
            return new Conversion.Converted(amount, rateDate: null);
        }

        if (amount.Currency.Code == target.Code)
        {
            // Same code, different minor units: two definitions disagree; never guess.
            throw new CurrencyMismatchException(amount.Currency, target);
        }

        var from = Rate(amount.Currency.Code, date);
        var to = Rate(target.Code, date);
        if (from is null || to is null)
        {
            string[] missing = [.. new[] { (amount.Currency.Code, from), (target.Code, to) }
                .Where(pair => pair.Item2 is null)
                .Select(pair => pair.Item1)];
            return new Conversion.RateMissing(missing, date);
        }

        var (fromRate, fromDate) = from.Value;
        var (toRate, toDate) = to.Value;
        var converted = amount.Amount / fromRate * toRate;
        return new Conversion.Converted(new Money(converted, target), Oldest(fromDate, toDate));
    }

    /// <summary>
    /// EUR itself is 1 by definition; any other rate comes from the store, which must return the
    /// latest rate on or before the date. A rate that breaks that contract is a bug in the store, so
    /// it fails loudly instead of converting at the wrong day's rate.
    /// </summary>
    private (decimal UnitsPerEur, DateOnly? Date)? Rate(string code, DateOnly date)
    {
        if (code == _eur)
        {
            return (1m, null);
        }

        var rate = _rates.LatestOnOrBefore(code, date);
        if (rate is null)
        {
            return null;
        }

        if (rate.CurrencyCode != code || rate.Date > date)
        {
            throw new InvalidOperationException(
                $"The rate store returned {rate.CurrencyCode} on {rate.Date:yyyy-MM-dd} when asked for {code} on or before {date:yyyy-MM-dd}.");
        }

        return (rate.UnitsPerEur, rate.Date);
    }

    private static DateOnly? Oldest(DateOnly? a, DateOnly? b) =>
        a is null ? b : b is null ? a : a < b ? a : b;
}
