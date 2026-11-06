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
/// Computes a loan's payment schedule. Conventions (decided with the owner in #109):
/// - interest is the balance times the annual rate times the days since the previous payment over the
///   days in the year (actual/actual, as the bank charges; <see cref="DayCount.Thirty360"/> gives 1/12 a month), using the
///   rate in effect on the first day of each interest month (the day after the previous payment); a rate
///   change applies from the first interest month that starts on or after its date;
/// - the annuity payment is fixed (PMT at rate / 12), so principal is what the payment leaves after
///   that month's interest. A first period longer or shorter than a month changes the first payment by
///   the extra (or missing) days of interest, not the loan;
/// - an annuity payment is recalculated over the remaining term whenever the rate changes, like
///   Excel's PMT;
/// - an extra payment is paid with the regular payment on or after its date and keeps the payment:
///   the remaining term shrinks (NPER at the current rate, rounded up to whole months). Interest-only
///   months push that shortened end back; an extra during interest-only lowers the later annuity instead;
/// - amounts are rounded to the currency's minor units; the last payment clears the balance, so the
///   principal and extra payments always sum exactly to the loan. Months of 28 to 31 days differ from
///   the even months PMT assumes, so without rate changes the last payment differs by a few percent;
///   each rate change recalculates the payment and resets that residue.
/// </summary>
public static class LoanSchedule
{
    public static IReadOnlyList<Installment> Calculate(Loan loan, ExtraPayments? extras = null)
    {
        ArgumentNullException.ThrowIfNull(loan);
        extras ??= ExtraPayments.None;
        extras.CheckFits(loan);

        var currency = loan.Principal.Currency;
        var balance = loan.Principal.Amount;
        var installments = new List<Installment>(loan.TermMonths);
        decimal? annuityRate = null;
        decimal annuityPayment = 0;
        var previous = loan.Start;
        // Repayment months left on the term extra payments have shortened, this one included. Only
        // annuity months count, so an interest-only period pushes the shortened end back.
        var repaymentMonthsLeft = loan.TermMonths;

        for (var number = 1; balance > 0 && number <= loan.TermMonths; number++)
        {
            var contractLeft = loan.TermMonths - number + 1;
            var remaining = Math.Min(contractLeft, repaymentMonthsLeft);
            var date = loan.FirstPaymentDate.AddMonths(number - 1);
            var accrualStart = number == 1 ? loan.Start : previous.AddDays(1);
            var period = loan.PeriodOn(accrualStart);
            var rate = period.RateOn(accrualStart);
            // Interest runs from the previous payment (or the payout) to this payment. The regular
            // month is the one an annuity payment is made for: the same, except for a first period
            // that starts more or less than a month before the first payment.
            // A payout exactly a month before the first payment is a whole month (also at month ends).
            var wholeMonth = number > 1 || loan.Start == date.AddMonths(-1);
            var periodStart = number == 1 ? loan.Start : previous;
            var interest = Round(Interest(balance, rate, loan.DayCount, periodStart, date, wholeMonth), currency);
            var monthInterest = wholeMonth
                ? interest
                : Round(Interest(balance, rate, loan.DayCount, date.AddMonths(-1), date, wholeMonth: true), currency);

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
                    // The payment stays fixed: at very high rates a 31-day month's interest can exceed it,
                    // and then the shortfall (negative principal) is added to the balance.
                    principal = annuityPayment - monthInterest;
                    break;
                case RepaymentType.InterestOnly:
                    principal = 0;
                    annuityRate = null;
                    break;
                default:
                    throw new NotSupportedException($"{period.RepaymentType} schedules come with #42.");
            }

            // The contract end clears the loan in any period; a shortened end only in a repayment month.
            var isAnnuity = period.RepaymentType == RepaymentType.Annuity;
            if (contractLeft == 1 || (isAnnuity && remaining == 1) || principal > balance)
            {
                principal = balance;
            }

            var opening = balance;
            balance -= principal;
            var extra = Math.Min(extras.On(previous, date, currency).Amount, balance);
            balance -= extra;
            if (extra > 0 && balance > 0 && isAnnuity)
            {
                // Keep the payment, shorten the term: the months the payment needs to clear what is left.
                repaymentMonthsLeft = Math.Min(remaining, PaymentsToRepay(balance, rate / 12m, annuityPayment, currency.MinorUnits) + 1);
            }

            if (isAnnuity)
            {
                repaymentMonthsLeft--;
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
    public static int PaymentsToRepay(decimal balance, decimal monthlyRate, decimal payment, int minorUnits = 2)
    {
        if (payment <= balance * monthlyRate)
        {
            throw new ArgumentOutOfRangeException(nameof(payment), payment, "The payment does not cover the interest.");
        }

        // n = -ln(1 - B r / P) / ln(1 + r) estimates the months in double; the whole number is then
        // settled exactly in decimal. k months are enough if what is left after them is no more than a
        // payment rounded down by half a minor unit leaves; the last payment takes that residue.
        var estimate = monthlyRate == 0
            ? (double)(balance / payment)
            : -Math.Log(1 - (double)(balance * monthlyRate / payment)) / Math.Log(1 + (double)monthlyRate);
        var months = Math.Max(1, (int)Math.Floor(estimate));
        var halfMinorUnit = 0.5m / (decimal)Math.Pow(10, minorUnits);
        var growth = 1m;
        for (var i = 0; i < months; i++)
        {
            growth *= 1 + monthlyRate;
        }

        var annuityFactor = monthlyRate == 0 ? months : (growth - 1) / monthlyRate;
        var left = (balance * growth) - (payment * annuityFactor);
        return left <= halfMinorUnit * annuityFactor ? months : months + 1;
    }

    /// <summary>
    /// Interest on a balance between two dates, unrounded. It multiplies before it divides, so an
    /// exact half minor unit stays exact and rounds away from zero.
    /// </summary>
    internal static decimal Interest(decimal balance, decimal annualRate, DayCount dayCount, DateOnly from, DateOnly to, bool wholeMonth)
    {
        switch (dayCount)
        {
            case DayCount.ActualActual:
                // Days in each calendar year over that year's length (365 or 366), so a year's
                // interest is exactly the annual rate and a leap day adds no drift.
                var interest = 0m;
                for (var start = from; start < to;)
                {
                    var yearEnd = new DateOnly(start.Year + 1, 1, 1);
                    var end = to < yearEnd ? to : yearEnd;
                    var daysInYear = DateTime.IsLeapYear(start.Year) ? 366 : 365;
                    interest += balance * annualRate * (end.DayNumber - start.DayNumber) / daysInYear;
                    start = end;
                }

                return interest;
            case DayCount.Thirty360:
                // A whole schedule month is exactly 1/12, also from the 31st or to the end of February.
                return wholeMonth ? balance * annualRate / 12m : balance * annualRate * Days30360(from, to) / 360m;
            default:
                throw new ArgumentOutOfRangeException(nameof(dayCount), dayCount, "Unknown day count.");
        }
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
