namespace FinanceGoals.Domain.Currencies;

/// <summary>The outcome of a conversion: an amount, or a missing rate the UI must warn about.</summary>
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
    /// No rate for the currency on or before the date. There is deliberately no amount: a missing
    /// rate never becomes 1.0.
    /// </summary>
    public sealed record RateMissing : Conversion
    {
        public RateMissing(string currencyCode, DateOnly date)
        {
            CurrencyCode = currencyCode;
            Date = date;
        }

        public string CurrencyCode { get; }

        public DateOnly Date { get; }

        public string Warning => $"No exchange rate for {CurrencyCode} on or before {Date:yyyy-MM-dd}.";
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
        if (from.Missing is not null)
        {
            return from.Missing;
        }

        if (to.Missing is not null)
        {
            return to.Missing;
        }

        var converted = amount.Amount / from.UnitsPerEur * to.UnitsPerEur;
        return new Conversion.Converted(new Money(converted, target), Oldest(from.Date, to.Date));
    }

    private (decimal UnitsPerEur, DateOnly? Date, Conversion.RateMissing? Missing) Rate(string code, DateOnly date)
    {
        if (code == _eur)
        {
            return (1m, null, null);
        }

        var rate = _rates.LatestOnOrBefore(code, date);
        return rate is null
            ? (0m, null, new Conversion.RateMissing(code, date))
            : (rate.UnitsPerEur, rate.Date, null);
    }

    private static DateOnly? Oldest(DateOnly? a, DateOnly? b) =>
        a is null ? b : b is null ? a : a < b ? a : b;
}
