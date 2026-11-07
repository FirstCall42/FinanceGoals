using FinanceGoals.Domain.Currencies;
using FinanceGoals.Domain.Loans;

namespace FinanceGoals.Application.Loans;

/// <summary>A stored loan with its extra payments and recorded balances.</summary>
public sealed record LoanWithExtras(Loan Loan, LoanExtras Extras);

/// <summary>What the loans owe month by month in a currency, and the currencies without a rate.</summary>
public sealed record LoansOwed(IReadOnlyList<decimal> ByMonth, IReadOnlyList<string> MissingRates);

/// <summary>
/// What the loans owe at each forecast month (#142). A loan continues from its latest recorded balance on
/// or before today along its own schedule, with the stored extra payments and the rate changes still to
/// come; a loan with no recorded balance is not counted (net worth says so). Where the schedule can't be
/// continued the balance stays as recorded. Each month is converted at today's rate and rounded to the
/// target currency, and a currency without a rate is listed, never counted at 1.0.
/// </summary>
public static class LoanForecast
{
    public static LoansOwed Owed(
        DateOnly today,
        int months,
        Currency target,
        IReadOnlyList<LoanWithExtras> loans,
        IReadOnlyDictionary<string, int> minorUnits,
        CurrencyConverter converter)
    {
        ArgumentNullException.ThrowIfNull(loans);
        ArgumentNullException.ThrowIfNull(minorUnits);
        ArgumentNullException.ThrowIfNull(converter);

        var owed = new decimal[months + 1];
        var missing = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (loan, extras) in loans)
        {
            var balance = extras.Balances.Where(b => b.Date <= today).MaxBy(b => b.Date);
            if (balance is null || loan.Start > today)
            {
                continue;
            }

            var own = new Currency(loan.Principal.Currency.Code, minorUnits[loan.Principal.Currency.Code]);
            var amounts = Balances(loan, extras, balance, today, months);
            for (var month = 0; month <= months; month++)
            {
                switch (converter.Convert(new Money(amounts[month], own), target, today))
                {
                    case Conversion.Converted converted:
                        owed[month] += converted.Amount.Round().Amount;
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

        return new LoansOwed(owed, [.. missing]);
    }

    private static decimal[] Balances(Loan loan, LoanExtras extras, StoredBalance balance, DateOnly today, int months)
    {
        var amounts = new decimal[months + 1];
        Array.Fill(amounts, balance.Amount);
        try
        {
            var continued = loan.StartingFrom(balance.Date, balance.Amount);
            if (continued is null)
            {
                return amounts;
            }

            var currency = loan.Principal.Currency;
            var next = continued.FirstPaymentDate;
            var plan = new ExtraPayments(
                extras.OneOff.Where(e => e.Date > balance.Date).Select(e => new ExtraPayment(e.Date, new Money(e.Amount, currency))),
                extras.Monthly
                    .Where(e => e.Until is null || e.Until >= next)
                    .Select(e => new MonthlyExtraPayment(new Money(e.Amount, currency), e.From > next ? e.From : next, e.Until)));
            var schedule = LoanSchedule.Calculate(continued, plan);
            for (var month = 0; month <= months; month++)
            {
                var day = today.AddMonths(month);
                var paid = schedule.LastOrDefault(installment => installment.Date <= day);
                amounts[month] = paid?.Balance.Amount ?? balance.Amount;
            }
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or OverflowException or NotSupportedException)
        {
            Array.Fill(amounts, balance.Amount);
        }

        return amounts;
    }
}
