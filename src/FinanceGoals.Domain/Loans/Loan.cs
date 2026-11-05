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
        Rates = ordered;
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
/// fixed-rate and interest-only periods) is computed by one schedule engine. Payments are monthly,
/// starting on <see cref="FirstPaymentDate"/>.
/// </summary>
public sealed class Loan
{
    private readonly LoanPeriod[] _periods;

    public Loan(string name, Money principal, DateOnly firstPaymentDate, int termMonths, IReadOnlyList<LoanPeriod> periods, Money? feePerPayment = null)
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

        // The loan starts one month before the first payment (interest accrues for that month).
        var start = firstPaymentDate.AddMonths(-1);
        if (ordered[0].Start != start)
        {
            throw new ArgumentException($"The first period must start on {start:yyyy-MM-dd}, a month before the first payment.", nameof(periods));
        }

        var end = start.AddMonths(termMonths);
        if (ordered.Select(period => period.Start).Distinct().Count() != ordered.Length || ordered[^1].Start >= end)
        {
            throw new ArgumentException("Periods must start on different days, before the loan ends.", nameof(periods));
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
        _periods = ordered;
    }

    public string Name { get; }

    public Money Principal { get; }

    public DateOnly FirstPaymentDate { get; }

    public int TermMonths { get; }

    /// <summary>The bank's charge on each payment (termingebyr); zero if none.</summary>
    public Money FeePerPayment { get; }

    /// <summary>The periods, in order; contiguous from the loan's start to its end.</summary>
    public IReadOnlyList<LoanPeriod> Periods => _periods;

    /// <summary>The day the loan starts: a month before the first payment.</summary>
    public DateOnly Start => FirstPaymentDate.AddMonths(-1);

    /// <summary>The day of the last payment.</summary>
    public DateOnly LastPaymentDate => FirstPaymentDate.AddMonths(TermMonths - 1);

    /// <summary>The period a day falls in.</summary>
    public LoanPeriod PeriodOn(DateOnly day) => _periods.Last(period => period.Start <= day);

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
        return new Loan(Name, Principal, FirstPaymentDate, TermMonths, periods, FeePerPayment);
    }
}
