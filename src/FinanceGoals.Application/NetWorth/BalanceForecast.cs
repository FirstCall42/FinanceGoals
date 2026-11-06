using FinanceGoals.Application.Accounts;
using FinanceGoals.Domain.Currencies;

namespace FinanceGoals.Application.NetWorth;

/// <summary>One month of the forecast: the total with a low, an expected and a high return.</summary>
public sealed record ForecastPoint(DateOnly Date, decimal Low, decimal Expected, decimal High);

/// <summary>The forecast in one currency, at today's exchange rates (a forecast does not guess future rates).</summary>
public sealed record ForecastResult(
    string Currency, DateOnly From, decimal ReturnSpread, IReadOnlyList<string> MissingRates, IReadOnlyList<ForecastPoint> Points);

/// <summary>
/// Balances month by month from each account's latest balance (#119). An account grows by its expected
/// yearly return, compounded monthly, and gets its monthly contribution at the end of each month;
/// without a return it only receives contributions. Returns are a guess, so the low and high lines use
/// the expected return minus and plus <see cref="ReturnSpread"/>, only for accounts that have one.
/// Each account's month is rounded to its currency's minor units, converted at today's rate, rounded to
/// the target's, and the totals add those rounded figures. A currency without a rate is listed and left out.
/// Loans (#107) join later.
/// </summary>
public static class BalanceForecast
{
    /// <summary>How far the low and high lines sit from the expected return, as a fraction (0.02 is two percentage points).</summary>
    public const decimal ReturnSpread = 0.02m;

    public const int MaxMonths = 600;

    public static ForecastResult Project(
        DateOnly today,
        int months,
        Currency target,
        IReadOnlyList<Account> accounts,
        IReadOnlyList<AccountValue> values,
        IReadOnlyList<AccountAssumption> assumptions,
        IReadOnlyDictionary<string, int> minorUnits,
        CurrencyConverter converter)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(assumptions);
        ArgumentNullException.ThrowIfNull(minorUnits);
        ArgumentNullException.ThrowIfNull(converter);
        ArgumentOutOfRangeException.ThrowIfLessThan(months, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(months, MaxMonths);

        var latest = values
            .Where(value => value.Date <= today)
            .GroupBy(value => value.AccountId)
            .ToDictionary(group => group.Key, group => group.MaxBy(value => value.Date)!);
        var byAccount = assumptions.ToDictionary(a => a.AccountId);

        var low = new decimal[months + 1];
        var expected = new decimal[months + 1];
        var high = new decimal[months + 1];
        var missing = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var account in accounts)
        {
            if ((account.ArchivedOn is { } archived && archived <= today) || !latest.TryGetValue(account.Id, out var balance))
            {
                continue;
            }

            var own = new Currency(account.CurrencyCode, minorUnits[account.CurrencyCode]);
            byAccount.TryGetValue(account.Id, out var assumption);
            var contribution = assumption?.MonthlyContribution ?? 0m;
            var scenarios = new (decimal[] Totals, decimal? Rate)[]
            {
                (low, assumption?.ExpectedYearlyReturn is { } l ? Math.Max(l - ReturnSpread, -0.99m) : null),
                (expected, assumption?.ExpectedYearlyReturn),
                (high, assumption?.ExpectedYearlyReturn is { } h ? h + ReturnSpread : null),
            };

            foreach (var (totals, rate) in scenarios)
            {
                var amount = balance.Amount;
                for (var month = 0; month <= months; month++)
                {
                    if (month > 0)
                    {
                        amount = decimal.Round(amount * (1m + MonthlyRate(rate ?? 0m)) + contribution, own.MinorUnits, MidpointRounding.AwayFromZero);
                    }

                    switch (converter.Convert(new Money(amount, own), target, today))
                    {
                        case Conversion.Converted converted:
                            totals[month] += converted.Amount.Round().Amount;
                            break;
                        case Conversion.RateMissing rateMissing:
                            foreach (var code in rateMissing.CurrencyCodes)
                            {
                                missing.Add(code);
                            }

                            break;
                    }
                }
            }
        }

        var points = Enumerable.Range(0, months + 1)
            .Select(month => new ForecastPoint(today.AddMonths(month), low[month], expected[month], high[month]))
            .ToArray();
        return new ForecastResult(target.Code, today, ReturnSpread, [.. missing], points);
    }

    /// <summary>The monthly rate that compounds to the yearly one over twelve months.</summary>
    private static decimal MonthlyRate(decimal yearly) =>
        yearly == 0m ? 0m : decimal.Round((decimal)(Math.Pow((double)(1m + yearly), 1d / 12d) - 1d), 12);
}
