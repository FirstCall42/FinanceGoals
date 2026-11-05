using FinanceGoals.Domain.Currencies;
using FinanceGoals.Domain.Loans;

namespace FinanceGoals.Domain.Tests.Loans;

[TestClass]
public sealed class LoanTests
{
    private static readonly Currency _nok = new("NOK", 2);
    private static readonly Money _threeMillion = new(3_000_000m, _nok);
    private static readonly DateOnly _start = new(2026, 1, 1);
    private static readonly DateOnly _firstPayment = new(2026, 2, 1);

    private static LoanPeriod Variable(DateOnly start, params RateChange[] rates) =>
        new(start, RateType.Variable, RepaymentType.Annuity, rates);

    [TestMethod]
    public void A_loan_has_one_or_more_periods()
    {
        var single = new Loan("Mortgage", _threeMillion, _firstPayment, 300, [Variable(_start, new RateChange(_start, 0.05m))]);
        var fixedThenVariable = new Loan("Mortgage", _threeMillion, _firstPayment, 300,
        [
            new LoanPeriod(_start, RateType.Fixed, RepaymentType.Annuity, [new RateChange(_start, 0.0449m)]),
            Variable(new DateOnly(2031, 1, 1), new RateChange(new DateOnly(2031, 1, 1), 0.052m)),
        ]);

        single.Periods.Count.ShouldBe(1);
        fixedThenVariable.Periods.Count.ShouldBe(2);
        fixedThenVariable.RateOn(new DateOnly(2030, 12, 31)).ShouldBe(0.0449m);
        fixedThenVariable.RateOn(new DateOnly(2031, 1, 1)).ShouldBe(0.052m);
        single.Start.ShouldBe(_start);
        single.LastPaymentDate.ShouldBe(new DateOnly(2051, 1, 1));
    }

    [TestMethod]
    public void Variable_rate_changes_are_kept_with_their_effective_dates()
    {
        var loan = new Loan("Mortgage", _threeMillion, _firstPayment, 300, [Variable(_start, new RateChange(_start, 0.05m))]);

        var changed = loan
            .WithRateChange(new RateChange(new DateOnly(2026, 6, 15), 0.0525m))
            .WithRateChange(new RateChange(new DateOnly(2026, 3, 20), 0.0475m));

        changed.Periods[0].Rates.Select(rate => (rate.EffectiveFrom, rate.AnnualRate)).ShouldBe(
        [
            (_start, 0.05m),
            (new DateOnly(2026, 3, 20), 0.0475m),
            (new DateOnly(2026, 6, 15), 0.0525m),
        ]);
        changed.RateOn(new DateOnly(2026, 3, 19)).ShouldBe(0.05m);
        changed.RateOn(new DateOnly(2026, 4, 1)).ShouldBe(0.0475m);
        changed.RateOn(new DateOnly(2027, 1, 1)).ShouldBe(0.0525m);
        loan.RateOn(new DateOnly(2027, 1, 1)).ShouldBe(0.05m); // the original is unchanged
    }

    [TestMethod]
    public void A_correction_on_the_same_day_replaces_the_rate()
    {
        var loan = new Loan("Mortgage", _threeMillion, _firstPayment, 300, [Variable(_start, new RateChange(_start, 0.05m))]);

        var corrected = loan.WithRateChange(new RateChange(_start, 0.051m));

        corrected.Periods[0].Rates.ShouldHaveSingleItem().AnnualRate.ShouldBe(0.051m);
    }

    [TestMethod]
    public void A_fixed_rate_cannot_change()
    {
        var loan = new Loan("Mortgage", _threeMillion, _firstPayment, 300,
            [new LoanPeriod(_start, RateType.Fixed, RepaymentType.Annuity, [new RateChange(_start, 0.0449m)])]);

        Should.Throw<InvalidOperationException>(() => loan.WithRateChange(new RateChange(new DateOnly(2027, 1, 1), 0.05m)));
        Should.Throw<ArgumentException>(() => new LoanPeriod(_start, RateType.Fixed, RepaymentType.Annuity,
            [new RateChange(_start, 0.04m), new RateChange(new DateOnly(2027, 1, 1), 0.05m)]));
    }

    [TestMethod]
    public void A_rate_change_outside_the_loan_is_refused()
    {
        var loan = new Loan("Mortgage", _threeMillion, _firstPayment, 12, [Variable(_start, new RateChange(_start, 0.05m))]);

        Should.Throw<ArgumentOutOfRangeException>(() => loan.WithRateChange(new RateChange(new DateOnly(2025, 12, 31), 0.05m)));
        Should.Throw<ArgumentOutOfRangeException>(() => loan.WithRateChange(new RateChange(new DateOnly(2027, 1, 2), 0.05m)));
    }

    [TestMethod]
    public void Inconsistent_loans_are_refused()
    {
        var rate = new RateChange(_start, 0.05m);

        // No periods; a first period that doesn't start a month before the first payment.
        Should.Throw<ArgumentException>(() => new Loan("Mortgage", _threeMillion, _firstPayment, 300, []));
        Should.Throw<ArgumentException>(() => new Loan("Mortgage", _threeMillion, _firstPayment, 300,
            [Variable(new DateOnly(2026, 1, 15), new RateChange(new DateOnly(2026, 1, 15), 0.05m))]));
        // Two periods starting the same day; a period starting after the loan ends.
        Should.Throw<ArgumentException>(() => new Loan("Mortgage", _threeMillion, _firstPayment, 300, [Variable(_start, rate), Variable(_start, rate)]));
        Should.Throw<ArgumentException>(() => new Loan("Mortgage", _threeMillion, _firstPayment, 12,
            [Variable(_start, rate), Variable(new DateOnly(2027, 1, 1), new RateChange(new DateOnly(2027, 1, 1), 0.05m))]));
        // A rate change dated after its period has ended.
        Should.Throw<ArgumentException>(() => new Loan("Mortgage", _threeMillion, _firstPayment, 300,
        [
            Variable(_start, rate, new RateChange(new DateOnly(2027, 6, 1), 0.06m)),
            Variable(new DateOnly(2027, 1, 1), new RateChange(new DateOnly(2027, 1, 1), 0.05m)),
        ]));
        // Principal, term, fee and rates.
        Should.Throw<ArgumentOutOfRangeException>(() => new Loan("Mortgage", new Money(0m, _nok), _firstPayment, 300, [Variable(_start, rate)]));
        Should.Throw<ArgumentOutOfRangeException>(() => new Loan("Mortgage", _threeMillion, _firstPayment, 0, [Variable(_start, rate)]));
        Should.Throw<ArgumentException>(() => new Loan("Mortgage", _threeMillion, _firstPayment, 300, [Variable(_start, rate)], new Money(50m, new Currency("EUR", 2))));
        Should.Throw<ArgumentOutOfRangeException>(() => new RateChange(_start, -0.01m));
        Should.Throw<ArgumentException>(() => Variable(_start, new RateChange(new DateOnly(2026, 2, 1), 0.05m)));
        Should.Throw<ArgumentException>(() => Variable(_start, rate, new RateChange(_start, 0.06m)));
    }

    [TestMethod]
    public void The_fee_defaults_to_zero_in_the_loan_currency()
    {
        var loan = new Loan("Mortgage", _threeMillion, _firstPayment, 300, [Variable(_start, new RateChange(_start, 0.05m))]);

        loan.FeePerPayment.ShouldBe(Money.Zero(_nok));
    }
}
