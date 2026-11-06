using FinanceGoals.Domain.Currencies;
using FinanceGoals.Domain.Loans;

namespace FinanceGoals.Application.Loans;

/// <summary>A rate change: the nominal annual rate in percent (5.24 is 5.24 %) from a date.</summary>
public sealed record RateChangeInput(DateOnly From, decimal AnnualRatePercent);

/// <summary>A one-off extra repayment.</summary>
public sealed record ExtraPaymentInput(DateOnly Date, decimal Amount);

/// <summary>An extra repayment with every payment from a date, optionally until a date.</summary>
public sealed record MonthlyExtraInput(decimal Amount, DateOnly From, DateOnly? Until);

/// <summary>
/// A variable-rate annuity loan to calculate, nothing stored. Amounts are in <see cref="Currency"/>,
/// which has <see cref="MinorUnits"/> decimals (the web app has both from GET /api/currencies).
/// </summary>
public sealed record LoanCalculationRequest(
    string Currency,
    int MinorUnits,
    decimal Principal,
    DateOnly FirstPayment,
    int TermMonths,
    decimal StartRatePercent,
    DateOnly? PaidOut = null,
    decimal FeePerPayment = 0,
    IReadOnlyList<RateChangeInput>? RateChanges = null,
    IReadOnlyList<ExtraPaymentInput>? Extras = null,
    IReadOnlyList<MonthlyExtraInput>? MonthlyExtras = null);

/// <summary>One payment; every amount is in the result's currency.</summary>
public sealed record ScheduleRow(
    int Number,
    DateOnly Date,
    decimal AnnualRatePercent,
    decimal OpeningBalance,
    decimal Interest,
    decimal Principal,
    decimal Payment,
    decimal Extra,
    decimal Fee,
    decimal Balance);

/// <summary>When a schedule ends and what it costs.</summary>
public sealed record ScheduleSummary(DateOnly LastPayment, int Payments, decimal TotalInterest, decimal TotalExtra);

/// <summary>The schedule without and with the extra payments, and what the extras save.</summary>
public sealed record LoanCalculationResult(
    string Currency,
    ScheduleSummary WithoutExtra,
    ScheduleSummary WithExtra,
    int MonthsSaved,
    decimal InterestSaved,
    IReadOnlyList<ScheduleRow> WithoutExtraSchedule,
    IReadOnlyList<ScheduleRow> WithExtraSchedule);

/// <summary>Calculates a loan from a request, without storing anything (the mortgage calculator).</summary>
public static class LoanCalculator
{
    public const int MaxTermMonths = 600;
    public const int MaxEntries = 600;

    /// <summary>The result, or null with an error a person can act on when the input is not a valid loan.</summary>
    public static LoanCalculationResult? Calculate(LoanCalculationRequest request, out string? error)
    {
        ArgumentNullException.ThrowIfNull(request);
        var rateChanges = request.RateChanges ?? [];
        var extras = request.Extras ?? [];
        var monthly = request.MonthlyExtras ?? [];
        if (request.TermMonths is < 1 or > MaxTermMonths)
        {
            error = $"The term must be 1 to {MaxTermMonths} months.";
            return null;
        }

        if (rateChanges.Count > MaxEntries || extras.Count > MaxEntries || monthly.Count > MaxEntries)
        {
            error = $"At most {MaxEntries} rate changes and {MaxEntries} extra payments of each kind.";
            return null;
        }

        try
        {
            var currency = new Currency(request.Currency, request.MinorUnits);
            Money Amount(decimal amount) => new(amount, currency);
            var paidOut = request.PaidOut ?? request.FirstPayment.AddMonths(-1);
            RateChange[] rates =
            [
                new RateChange(paidOut, request.StartRatePercent / 100m),
                .. rateChanges.Select(change => new RateChange(change.From, change.AnnualRatePercent / 100m)),
            ];
            if (rates.Skip(1).Any(rate => rate.EffectiveFrom <= paidOut))
            {
                error = "Every rate change must come after the loan is paid out.";
                return null;
            }

            var loan = new Loan(
                "Calculator",
                Amount(request.Principal),
                request.FirstPayment,
                request.TermMonths,
                [new LoanPeriod(paidOut, RateType.Variable, RepaymentType.Annuity, rates)],
                Amount(request.FeePerPayment),
                paidOut);
            var extraPayments = new ExtraPayments(
                extras.Select(extra => new ExtraPayment(extra.Date, Amount(extra.Amount))),
                monthly.Select(extra => new MonthlyExtraPayment(Amount(extra.Amount), extra.From, extra.Until)));

            var without = LoanSchedule.Calculate(loan);
            var with = LoanSchedule.Calculate(loan, extraPayments);
            var comparison = new PayoffComparison(LoanSchedule.Summarize(without), LoanSchedule.Summarize(with));
            error = null;
            return new LoanCalculationResult(
                currency.Code,
                Summary(comparison.WithoutExtra),
                Summary(comparison.WithExtra),
                comparison.MonthsSaved,
                comparison.InterestSaved.Amount,
                [.. without.Select(Row)],
                [.. with.Select(Row)]);
        }
        catch (ArgumentException exception)
        {
            // The domain's messages describe the input ("The principal must be positive."), not the code.
            error = FirstSentence(exception.Message);
            return null;
        }
    }

    private static ScheduleSummary Summary(PayoffSummary summary) =>
        new(summary.LastPayment, summary.Payments, summary.TotalInterest.Amount, summary.TotalExtra.Amount);

    private static ScheduleRow Row(Installment i) => new(
        i.Number,
        i.Date,
        i.AnnualRate * 100m,
        i.OpeningBalance.Amount,
        i.Interest.Amount,
        i.Principal.Amount,
        i.Payment.Amount,
        i.Extra.Amount,
        i.Fee.Amount,
        i.Balance.Amount);

    // ArgumentException appends " (Parameter 'x')" and the actual value; people only need the sentence.
    private static string FirstSentence(string message)
    {
        var end = message.IndexOf(" (Parameter", StringComparison.Ordinal);
        var text = end < 0 ? message : message[..end];
        var newline = text.IndexOf('\n', StringComparison.Ordinal);
        return (newline < 0 ? text : text[..newline]).Trim();
    }
}
