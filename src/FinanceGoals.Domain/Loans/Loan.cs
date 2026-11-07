using FinanceGoals.Domain.Currencies;

namespace FinanceGoals.Domain.Loans;

/// <summary>How the rate of a period behaves.</summary>
public enum RateType
{
    /// <summary>The bank can change it; each change has an effective date.</summary>
    Variable,

    /// <summary>One rate for the whole period.</summary>
    Fixed,
}

/// <summary>How interest days are counted.</summary>
public enum DayCount
{
    /// <summary>
    /// Actual days over the days in that year (365, or 366 in a leap year): the bank's count in a
    /// normal year (checked against a statement), and a whole year is exactly the annual rate. The default.
    /// </summary>
    ActualActual,

    /// <summary>Every month is 30 days of a 360-day year: each whole month is exactly 1/12 of the rate.</summary>
    Thirty360,
}

/// <summary>How principal is repaid in a period.</summary>
public enum RepaymentType
{
    /// <summary>Equal payments (annuitetslån), recalculated when the rate changes.</summary>
    Annuity,

    /// <summary>Equal principal each payment, so payments fall (serielån).</summary>
    StraightLine,

    /// <summary>Interest only; the principal stays (avdragsfrihet).</summary>
    InterestOnly,
}

/// <summary>A nominal annual interest rate taking effect on a date, e.g. 5.25 % (0.0525) from 1 March 2026.</summary>
public sealed record RateChange
{
    public RateChange(DateOnly effectiveFrom, decimal annualRate)
    {
        if (annualRate < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(annualRate), annualRate, "A rate cannot be negative.");
        }

        EffectiveFrom = effectiveFrom;
        AnnualRate = annualRate;
    }

    public DateOnly EffectiveFrom { get; }

    /// <summary>The nominal annual rate as a fraction: 0.0525 is 5.25 %.</summary>
    public decimal AnnualRate { get; }
}

/// <summary>
/// A stretch of the loan with one rate type and one repayment type. It runs from <see cref="Start"/>
/// to the next period's start (the last period runs to the end of the loan).
/// </summary>
public sealed record LoanPeriod
{
    public LoanPeriod(DateOnly start, RateType rateType, RepaymentType repaymentType, IReadOnlyList<RateChange> rates)
    {
        ArgumentNullException.ThrowIfNull(rates);
        if (rates.Count == 0)
        {
            throw new ArgumentException("A period needs its starting rate.", nameof(rates));
        }

        var ordered = rates.OrderBy(rate => rate.EffectiveFrom).ToArray();
        if (ordered[0].EffectiveFrom != start)
        {
            throw new ArgumentException("The first rate must take effect when the period starts.", nameof(rates));
        }

        if (ordered.Select(rate => rate.EffectiveFrom).Distinct().Count() != ordered.Length)
        {
            throw new ArgumentException("Two rates take effect on the same day.", nameof(rates));
        }

        if (rateType == RateType.Fixed && ordered.Length > 1)
        {
            throw new ArgumentException("A fixed-rate period has exactly one rate.", nameof(rates));
        }

        Start = start;
        RateType = rateType;
        RepaymentType = repaymentType;
        Rates = Array.AsReadOnly(ordered);
    }

    public DateOnly Start { get; }

    public RateType RateType { get; }

    public RepaymentType RepaymentType { get; }

    /// <summary>The rate changes in this period, oldest first; the first takes effect at <see cref="Start"/>.</summary>
    public IReadOnlyList<RateChange> Rates { get; }

    /// <summary>The rate in effect on a day within this period.</summary>
    public decimal RateOn(DateOnly day) => Rates.Last(rate => rate.EffectiveFrom <= day).AnnualRate;
}

/// <summary>
/// A loan or mortgage as a sequence of periods, so every loan type (annuity, straight-line,
/// fixed-rate and interest-only periods) is computed by one schedule engine. The loan is paid out on
/// <see cref="Start"/>; payments are monthly from <see cref="FirstPaymentDate"/> to
/// <see cref="LastPaymentDate"/>, and every period and rate change falls in that span.
/// </summary>
public sealed class Loan
{
    private readonly LoanPeriod[] _periods;

    // paidOut: the day the loan was paid out. Defaults to a month before the first payment; banks
    // often pay out mid-month, making the first interest period longer or shorter than a month.
    // dayCount: how interest days are counted; the bank's actual/365 unless a test needs round months.
    public Loan(
        string name,
        Money principal,
        DateOnly firstPaymentDate,
        int termMonths,
        IReadOnlyList<LoanPeriod> periods,
        Money? feePerPayment = null,
        DateOnly? paidOut = null,
        DayCount dayCount = DayCount.ActualActual,
        bool securedOnProperty = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(periods);
        if (principal.Amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(principal), principal.Amount, "The principal must be positive.");
        }

        if (termMonths <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(termMonths), termMonths, "The term must be at least one month.");
        }

        var fee = feePerPayment ?? Money.Zero(principal.Currency);
        if (fee.Currency != principal.Currency || fee.Amount < 0)
        {
            throw new ArgumentException("The fee must be a non-negative amount in the loan's currency.", nameof(feePerPayment));
        }

        var ordered = periods.OrderBy(period => period.Start).ToArray();
        if (ordered.Length == 0)
        {
            throw new ArgumentException("A loan has at least one period.", nameof(periods));
        }

        var start = paidOut ?? firstPaymentDate.AddMonths(-1);
        if (start >= firstPaymentDate)
        {
            throw new ArgumentException("The loan must be paid out before the first payment.", nameof(paidOut));
        }

        if (ordered[0].Start != start)
        {
            throw new ArgumentException($"The first period must start when the loan is paid out, {start:yyyy-MM-dd}.", nameof(periods));
        }

        // One end for every check: the last payment day. AddMonths keeps month-end payment days.
        var lastPayment = firstPaymentDate.AddMonths(termMonths - 1);
        if (ordered.Select(period => period.Start).Distinct().Count() != ordered.Length)
        {
            throw new ArgumentException("Two periods start on the same day.", nameof(periods));
        }

        if (ordered[^1].Start > lastPayment || ordered[^1].Rates[^1].EffectiveFrom > lastPayment)
        {
            throw new ArgumentException($"A period or rate change falls after the last payment, {lastPayment:yyyy-MM-dd}.", nameof(periods));
        }

        for (var i = 0; i < ordered.Length - 1; i++)
        {
            if (ordered[i].Rates[^1].EffectiveFrom >= ordered[i + 1].Start)
            {
                throw new ArgumentException("A rate change falls after its period has ended.", nameof(periods));
            }
        }

        Name = name;
        Principal = principal;
        FirstPaymentDate = firstPaymentDate;
        TermMonths = termMonths;
        FeePerPayment = fee;
        Start = start;
        DayCount = dayCount;
        SecuredOnProperty = securedOnProperty;
        _periods = ordered;
    }

    public string Name { get; }

    public Money Principal { get; }

    public DateOnly FirstPaymentDate { get; }

    public int TermMonths { get; }

    /// <summary>The bank's charge on each payment (termingebyr); zero if none.</summary>
    public Money FeePerPayment { get; }

    /// <summary>The periods, in order; contiguous from the loan's start to its end.</summary>
    public IReadOnlyList<LoanPeriod> Periods => Array.AsReadOnly(_periods);

    /// <summary>The day the loan was paid out; interest runs from here to the first payment.</summary>
    public DateOnly Start { get; }

    public DayCount DayCount { get; }

    /// <summary>
    /// Secured on the home or another illiquid asset (#135): liquid net worth leaves such a loan out, since
    /// the property it is secured on is left out too.
    /// </summary>
    public bool SecuredOnProperty { get; }

    /// <summary>The day of the last payment.</summary>
    public DateOnly LastPaymentDate => FirstPaymentDate.AddMonths(TermMonths - 1);

    /// <summary>The period a day falls in, from <see cref="Start"/> to <see cref="LastPaymentDate"/>.</summary>
    public LoanPeriod PeriodOn(DateOnly day)
    {
        if (day < Start || day > LastPaymentDate)
        {
            throw new ArgumentOutOfRangeException(nameof(day), day, $"The loan runs from {Start:yyyy-MM-dd} to {LastPaymentDate:yyyy-MM-dd}.");
        }

        return _periods.Last(period => period.Start <= day);
    }

    /// <summary>The nominal annual rate in effect on a day of the loan.</summary>
    public decimal RateOn(DateOnly day) => PeriodOn(day).RateOn(day);

    /// <summary>
    /// The same loan with a new variable rate from a date (the bank changed the rate). A change on a day
    /// that already has one replaces it. Refused in a fixed-rate period or outside the loan.
    /// </summary>
    public Loan WithRateChange(RateChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (change.EffectiveFrom < Start || change.EffectiveFrom > LastPaymentDate)
        {
            throw new ArgumentOutOfRangeException(nameof(change), change.EffectiveFrom, "The rate change falls outside the loan.");
        }

        var period = PeriodOn(change.EffectiveFrom);
        if (period.RateType == RateType.Fixed)
        {
            throw new InvalidOperationException($"The rate is fixed from {period.Start:yyyy-MM-dd}; it cannot change on {change.EffectiveFrom:yyyy-MM-dd}.");
        }

        var rates = period.Rates.Where(rate => rate.EffectiveFrom != change.EffectiveFrom).Append(change).ToArray();
        var updated = new LoanPeriod(period.Start, period.RateType, period.RepaymentType, rates);
        var periods = _periods.Select(p => ReferenceEquals(p, period) ? updated : p).ToArray();
        return new Loan(Name, Principal, FirstPaymentDate, TermMonths, periods, FeePerPayment, Start, DayCount, SecuredOnProperty);
    }
}
