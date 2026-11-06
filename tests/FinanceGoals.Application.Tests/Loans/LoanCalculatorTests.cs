using FinanceGoals.Application.Loans;

namespace FinanceGoals.Application.Tests.Loans;

[TestClass]
public sealed class LoanCalculatorTests
{
    private static readonly DateOnly _firstPayment = new(2026, 2, 1);

    private static LoanCalculationRequest Request(
        decimal principal = 1_000_000m,
        int termMonths = 300,
        IReadOnlyList<RateChangeInput>? rateChanges = null,
        IReadOnlyList<ExtraPaymentInput>? extras = null,
        IReadOnlyList<MonthlyExtraInput>? monthly = null) =>
        new("NOK", 2, principal, _firstPayment, termMonths, 5m, RateChanges: rateChanges, Extras: extras, MonthlyExtras: monthly);

    [TestMethod]
    public void Calculates_both_schedules_and_the_saving()
    {
        var result = LoanCalculator.Calculate(
            Request(rateChanges: [new RateChangeInput(new DateOnly(2027, 1, 1), 6m)], monthly: [new MonthlyExtraInput(2_000m, _firstPayment, null)]),
            out var error);

        error.ShouldBeNull();
        result.ShouldNotBeNull();
        result.Currency.ShouldBe("NOK");
        result.WithoutExtra.Payments.ShouldBe(300);
        result.WithoutExtraSchedule[0].Payment.ShouldBe(5_845.90m);
        result.WithoutExtraSchedule[0].AnnualRatePercent.ShouldBe(5m);
        result.WithExtraSchedule[0].Extra.ShouldBe(2_000m);
        result.WithExtraSchedule.Count.ShouldBe(result.WithExtra.Payments);
        result.MonthsSaved.ShouldBe(result.WithoutExtra.Payments - result.WithExtra.Payments);
        result.MonthsSaved.ShouldBeGreaterThan(0);
        result.InterestSaved.ShouldBe(result.WithoutExtra.TotalInterest - result.WithExtra.TotalInterest);
        result.WithExtraSchedule.Single(row => row.Date == new DateOnly(2027, 2, 1)).AnnualRatePercent.ShouldBe(6m);
    }

    [TestMethod]
    public void A_term_beyond_the_limit_is_refused()
    {
        LoanCalculator.Calculate(Request(termMonths: 601), out var error).ShouldBeNull();
        error.ShouldBe("The term must be 1 to 600 months.");
    }

    [TestMethod]
    public void Invalid_input_returns_the_domain_message_without_parameter_details()
    {
        LoanCalculator.Calculate(Request(principal: -5m), out var error).ShouldBeNull();
        error.ShouldBe("The principal must be positive.");
    }

    [TestMethod]
    public void An_extra_outside_the_loan_is_refused()
    {
        LoanCalculator.Calculate(Request(extras: [new ExtraPaymentInput(new DateOnly(2090, 1, 1), 1_000m)]), out var error).ShouldBeNull();
        error.ShouldNotBeNull().ShouldStartWith("A one-off extra payment falls outside the loan");
    }

    [TestMethod]
    public void A_rate_change_on_or_before_the_payout_is_refused()
    {
        LoanCalculator.Calculate(Request(rateChanges: [new RateChangeInput(new DateOnly(2026, 1, 1), 6m)]), out var error).ShouldBeNull();
        error.ShouldBe("Every rate change must come after the loan is paid out.");
    }
}
