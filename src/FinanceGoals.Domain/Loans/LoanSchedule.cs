using FinanceGoals.Domain.Currencies;

namespace FinanceGoals.Domain.Loans;

/// <summary>One monthly payment of a loan.</summary>
public sealed record Installment(
    int Number,
    DateOnly Date,
    decimal AnnualRate,
    Money OpeningBalance,
    Money Interest,
    Money Principal,
    Money Extra,
    Money Fee,
    Money Balance)
{
    /// <summary>The regular payment (terminbeløp): interest and principal.</summary>
    public Money Payment => Interest + Principal;

    /// <summary>What is paid on the day: the regular payment, any extra repayment and the fee.</summary>
    public Money Total => Payment + Extra + Fee;
}

/// <summary>The outcome of a schedule: when it ends and what it costs.</summary>
public sealed record PayoffSummary(DateOnly LastPayment, int Payments, Money TotalInterest, Money TotalExtra);

/// <summary>A schedule without and with extra payments, side by side.</summary>
public sealed record PayoffComparison(PayoffSummary WithoutExtra, PayoffSummary WithExtra)
{
    public int MonthsSaved => WithoutExtra.Payments - WithExtra.Payments;

    public Money InterestSaved => WithoutExtra.TotalInterest - WithExtra.TotalInterest;
}

/// <summary>
/// Computes a loan's payment schedule. Conventions (defaults, see the needs-decision issue on #41):
/// - monthly interest is the balance times the annual rate / 12, using the rate in effect on the first
///   day of each interest month (the day after the previous payment); a rate change applies from the
///   first interest month that starts on or after its date;
/// - a first interest period that is not a whole month (a mid-month payout) is prorated by 30/360 days,
///   and the difference from a whole month's interest changes the first payment, not the loan;
/// - an annuity payment is recalculated over the remaining term whenever the rate changes, like
///   Excel's PMT;
/// - an extra payment is paid with the regular payment on or after its date and keeps the payment:
///   the remaining term shrinks (NPER at the current rate, rounded up to whole months);
/// - amounts are rounded to the currency's minor units; the last payment clears the balance, so the
///   principal and extra payments always sum exactly to the loan.
/// </summary>
public static class LoanSchedule
{
    public static IReadOnlyList<Installment> Calculate(Loan loan, ExtraPayments? extras = null)
    {
        ArgumentNullException.ThrowIfNull(loan);
        extras ??= ExtraPayments.None;

        var currency = loan.Principal.Currency;
        var balance = loan.Principal.Amount;
        var installments = new List<Installment>(loan.TermMonths);
        decimal? annuityRate = null;
        decimal annuityPayment = 0;
        var previous = loan.Start;
        // Payments left, this one included: the contract term until an extra payment shortens it.
        var remaining = loan.TermMonths;

        for (var number = 1; balance > 0 && number <= loan.TermMonths; number++, remaining--)
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

            if (remaining == 1 || principal > balance)
            {
                principal = balance;
            }

            var opening = balance;
            balance -= principal;
            var extra = Math.Min(extras.On(previous, date, currency).Amount, balance);
            balance -= extra;
            if (extra > 0 && balance > 0 && period.RepaymentType == RepaymentType.Annuity)
            {
                // Keep the payment, shorten the term: the months the payment needs to clear what is left.
                remaining = Math.Min(remaining, PaymentsToRepay(balance, rate / 12m, annuityPayment) + 1);
            }

            installments.Add(new Installment(
                number,
                date,
                rate,
                new Money(opening, currency),
                new Money(interest, currency),
                new Money(principal, currency),
                new Money(extra, currency),
                loan.FeePerPayment,
                new Money(balance, currency)));
            previous = date;
        }

        return installments;
    }

    /// <summary>When the loan ends and its total interest, without and with the extra payments.</summary>
    public static PayoffComparison Compare(Loan loan, ExtraPayments extras) =>
        new(Summarize(Calculate(loan)), Summarize(Calculate(loan, extras)));

    public static PayoffSummary Summarize(IReadOnlyList<Installment> schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        if (schedule.Count == 0)
        {
            throw new ArgumentException("The schedule has no payments.", nameof(schedule));
        }

        var zero = Money.Zero(schedule[0].Interest.Currency);
        return new PayoffSummary(
            schedule[^1].Date,
            schedule.Count,
            schedule.Aggregate(zero, (sum, i) => sum + i.Interest),
            schedule.Aggregate(zero, (sum, i) => sum + i.Extra));
    }

    /// <summary>
    /// The level payment that repays a balance in the given number of months at a monthly rate, like
    /// Excel's PMT (with the sign flipped).
    /// </summary>
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

    /// <summary>
    /// The whole number of monthly payments a level payment needs to repay a balance (Excel's NPER,
    /// rounded up; the last payment is then smaller).
    /// </summary>
    public static int PaymentsToRepay(decimal balance, decimal monthlyRate, decimal payment)
    {
        if (payment <= balance * monthlyRate)
        {
            throw new ArgumentOutOfRangeException(nameof(payment), payment, "The payment does not cover the interest.");
        }

        // n = -ln(1 - B r / P) / ln(1 + r). Only the whole number of months is needed, so double is
        // precise enough. A payment rounded down to øre leaves a few øre after the last month; the
        // tolerance (a thousandth of a payment) lets the last payment take them instead of adding a month.
        var months = monthlyRate == 0
            ? (double)(balance / payment)
            : -Math.Log(1 - (double)(balance * monthlyRate / payment)) / Math.Log(1 + (double)monthlyRate);
        return Math.Max(1, (int)Math.Ceiling(months - 1e-3));
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
