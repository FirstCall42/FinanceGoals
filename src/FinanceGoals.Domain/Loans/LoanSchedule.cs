using FinanceGoals.Domain.Currencies;

namespace FinanceGoals.Domain.Loans;

/// <summary>One monthly payment of a loan.</summary>
public sealed record Installment(
    int Number,
    DateOnly Date,
    decimal AnnualRate,
    Money Interest,
    Money Principal,
    Money Fee,
    Money Balance)
{
    /// <summary>What is paid on the day: interest, principal and the fee.</summary>
    public Money Total => Interest + Principal + Fee;
}

/// <summary>
/// Computes a loan's payment schedule. Conventions (defaults, see the needs-decision issue on #41):
/// - monthly interest is the balance times the annual rate / 12, using the rate in effect on the first
///   day of each interest month (the day after the previous payment); a rate change applies from the
///   first interest month that starts on or after its date;
/// - a first interest period that is not a whole month (a mid-month payout) is prorated by 30/360 days,
///   and the difference from a whole month's interest changes the first payment, not the loan;
/// - an annuity payment is recalculated over the remaining term whenever the rate changes;
/// - amounts are rounded to the currency's minor units; the last payment clears the balance, so the
///   principal repaid always sums exactly to the loan.
/// </summary>
public static class LoanSchedule
{
    public static IReadOnlyList<Installment> Calculate(Loan loan)
    {
        ArgumentNullException.ThrowIfNull(loan);

        var currency = loan.Principal.Currency;
        var balance = loan.Principal.Amount;
        var installments = new List<Installment>(loan.TermMonths);
        decimal? annuityRate = null;
        decimal annuityPayment = 0;
        var previous = loan.Start;

        for (var number = 1; number <= loan.TermMonths; number++)
        {
            var date = loan.FirstPaymentDate.AddMonths(number - 1);
            var accrualStart = number == 1 ? loan.Start : previous.AddDays(1);
            var period = loan.PeriodOn(accrualStart);
            var rate = period.RateOn(accrualStart);
            // A payout exactly a month before the first payment is a whole month (also at month ends).
            var monthsOfInterest = number == 1 && loan.Start != loan.FirstPaymentDate.AddMonths(-1)
                ? Days30360(loan.Start, date) / 30m
                : 1m;
            var monthInterest = Round(balance * rate / 12m, currency);
            var interest = monthsOfInterest == 1m ? monthInterest : Round(balance * rate / 12m * monthsOfInterest, currency);
            var remaining = loan.TermMonths - number + 1;

            decimal principal;
            switch (period.RepaymentType)
            {
                case RepaymentType.Annuity:
                    if (annuityRate != rate)
                    {
                        annuityPayment = Round(AnnuityPayment(balance, rate / 12m, remaining), currency);
                        annuityRate = rate;
                    }

                    // Principal is repaid as if the month were whole; the extra (or missing) interest days
                    // of a prorated first period are added to (or taken off) the payment, not the loan.
                    principal = Math.Max(annuityPayment - monthInterest, 0);
                    break;
                case RepaymentType.InterestOnly:
                    principal = 0;
                    annuityRate = null;
                    break;
                default:
                    throw new NotSupportedException($"{period.RepaymentType} schedules come with #42.");
            }

            if (number == loan.TermMonths || principal > balance)
            {
                principal = balance;
            }

            balance -= principal;
            installments.Add(new Installment(
                number,
                date,
                rate,
                new Money(interest, currency),
                new Money(principal, currency),
                loan.FeePerPayment,
                new Money(balance, currency)));
            previous = date;
        }

        return installments;
    }

    /// <summary>The level payment that repays a balance in the given number of months at a monthly rate.</summary>
    public static decimal AnnuityPayment(decimal balance, decimal monthlyRate, int months)
    {
        if (monthlyRate == 0)
        {
            return balance / months;
        }

        // balance * r / (1 - (1 + r)^-n), computed in decimal by repeated multiplication.
        var growth = 1m;
        for (var i = 0; i < months; i++)
        {
            growth *= 1 + monthlyRate;
        }

        return balance * monthlyRate * growth / (growth - 1);
    }

    /// <summary>Days between two dates on a 30/360 basis (every month counts as 30 days).</summary>
    internal static int Days30360(DateOnly from, DateOnly to)
    {
        var d1 = Math.Min(from.Day, 30);
        var d2 = to.Day == 31 && d1 == 30 ? 30 : Math.Min(to.Day, 30);
        return ((to.Year - from.Year) * 360) + ((to.Month - from.Month) * 30) + (d2 - d1);
    }

    private static decimal Round(decimal amount, Currency currency) =>
        decimal.Round(amount, currency.MinorUnits, MidpointRounding.AwayFromZero);
}
