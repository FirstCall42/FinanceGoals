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
            [Variable(_start, rate), Variable(new DateOnly(2027, 1, 2), new RateChange(new DateOnly(2027, 1, 2), 0.05m))]));
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
    public void A_month_end_loan_has_one_consistent_end()
    {
        // First payment on the 31st: payments fall on each month's last day, and the last one bounds everything.
        var firstPayment = new DateOnly(2026, 3, 31);
        var start = firstPayment.AddMonths(-1);
        var loan = new Loan("Mortgage", _threeMillion, firstPayment, 2,
        [
            Variable(start, new RateChange(start, 0.05m)),
            Variable(new DateOnly(2026, 4, 29), new RateChange(new DateOnly(2026, 4, 29), 0.06m)),
        ]);

        loan.Start.ShouldBe(new DateOnly(2026, 2, 28));
        loan.LastPaymentDate.ShouldBe(new DateOnly(2026, 4, 30));
        loan.WithRateChange(new RateChange(new DateOnly(2026, 4, 30), 0.07m)).RateOn(new DateOnly(2026, 4, 30)).ShouldBe(0.07m);
        Should.Throw<ArgumentOutOfRangeException>(() => loan.WithRateChange(new RateChange(new DateOnly(2026, 5, 1), 0.07m)));
        Should.Throw<ArgumentException>(() => new Loan("Mortgage", _threeMillion, firstPayment, 2,
            [Variable(start, new RateChange(start, 0.05m)), Variable(new DateOnly(2026, 5, 1), new RateChange(new DateOnly(2026, 5, 1), 0.06m))]))
            .ParamName.ShouldBe("periods");
    }

    [TestMethod]
    public void A_mid_month_payout_starts_the_loan_on_that_day()
    {
        var paidOut = new DateOnly(2026, 1, 15);

        var loan = new Loan("Mortgage", _threeMillion, new DateOnly(2026, 2, 28), 300,
            [Variable(paidOut, new RateChange(paidOut, 0.05m))], paidOut: paidOut);

        loan.Start.ShouldBe(paidOut);
        loan.WithRateChange(new RateChange(new DateOnly(2026, 6, 1), 0.055m)).Start.ShouldBe(paidOut);
        Should.Throw<ArgumentException>(() => new Loan("Mortgage", _threeMillion, _firstPayment, 300,
            [Variable(_firstPayment, new RateChange(_firstPayment, 0.05m))], paidOut: _firstPayment)).ParamName.ShouldBe("paidOut");
    }

    [TestMethod]
    public void A_rate_change_on_a_period_boundary_belongs_to_the_next_period()
    {
        var boundary = new DateOnly(2031, 1, 1);
        var loan = new Loan("Mortgage", _threeMillion, _firstPayment, 300,
        [
            Variable(boundary, new RateChange(boundary, 0.052m)),
            Variable(_start, new RateChange(_start, 0.05m)), // out of order on purpose: the loan sorts them
        ]);

        var changed = loan.WithRateChange(new RateChange(boundary, 0.053m)).WithRateChange(new RateChange(new DateOnly(2032, 1, 1), 0.06m));

        changed.Periods[0].Rates.ShouldHaveSingleItem().AnnualRate.ShouldBe(0.05m);
        changed.Periods[1].Rates.Select(rate => rate.AnnualRate).ShouldBe([0.053m, 0.06m]);
        Should.Throw<ArgumentException>(() => new Loan("Mortgage", _threeMillion, _firstPayment, 300,
            [Variable(_start, new RateChange(_start, 0.05m), new RateChange(boundary, 0.06m)), Variable(boundary, new RateChange(boundary, 0.052m))]))
            .ParamName.ShouldBe("periods");
    }

    [TestMethod]
    public void Periods_and_rates_cannot_be_changed_from_outside()
    {
        var loan = new Loan("Mortgage", _threeMillion, _firstPayment, 300, [Variable(_start, new RateChange(_start, 0.05m))]);

        loan.Periods.ShouldNotBeAssignableTo<LoanPeriod[]>();
        loan.Periods[0].Rates.ShouldNotBeAssignableTo<RateChange[]>();
        Should.Throw<ArgumentOutOfRangeException>(() => loan.RateOn(new DateOnly(2025, 12, 31)));
        Should.Throw<ArgumentOutOfRangeException>(() => loan.RateOn(new DateOnly(2051, 1, 2)));
    }

    [TestMethod]
    public void The_fee_defaults_to_zero_in_the_loan_currency()
    {
        var loan = new Loan("Mortgage", _threeMillion, _firstPayment, 300, [Variable(_start, new RateChange(_start, 0.05m))]);

        loan.FeePerPayment.ShouldBe(Money.Zero(_nok));
    }

    [TestMethod]
    public void Starting_from_a_balance_continues_with_the_rate_in_force_the_later_changes_and_the_payments_left()
    {
        var loan = new Loan("Mortgage", _threeMillion, _firstPayment, 300,
            [Variable(_start, new RateChange(_start, 0.05m), new RateChange(new DateOnly(2026, 4, 1), 0.06m), new RateChange(new DateOnly(2026, 9, 1), 0.07m))],
            new Money(55m, _nok), dayCount: DayCount.Thirty360, securedOnProperty: true);

        var continued = loan.StartingFrom(new DateOnly(2026, 6, 15), 2_900_000m)!;

        // Payments on 1 Feb to 1 Jun are made (5); the next is 1 Jul and 295 are left.
        continued.Principal.ShouldBe(new Money(2_900_000m, _nok));
        continued.Start.ShouldBe(new DateOnly(2026, 6, 15));
        continued.FirstPaymentDate.ShouldBe(new DateOnly(2026, 7, 1));
        continued.TermMonths.ShouldBe(295);
        continued.LastPaymentDate.ShouldBe(loan.LastPaymentDate);
        continued.RateOn(new DateOnly(2026, 6, 15)).ShouldBe(0.06m);
        continued.RateOn(new DateOnly(2026, 9, 1)).ShouldBe(0.07m);
        continued.FeePerPayment.ShouldBe(new Money(55m, _nok));
        continued.DayCount.ShouldBe(DayCount.Thirty360);
        continued.SecuredOnProperty.ShouldBeTrue();
    }

    [TestMethod]
    public void Starting_from_a_balance_keeps_later_periods()
    {
        var later = new DateOnly(2031, 1, 1);
        var loan = new Loan("Mortgage", _threeMillion, _firstPayment, 300,
        [
            new LoanPeriod(_start, RateType.Fixed, RepaymentType.Annuity, [new RateChange(_start, 0.0449m)]),
            new LoanPeriod(later, RateType.Variable, RepaymentType.StraightLine, [new RateChange(later, 0.052m)]),
        ]);

        var continued = loan.StartingFrom(new DateOnly(2027, 3, 10), 2_500_000m)!;

        continued.Periods.Count.ShouldBe(2);
        continued.Periods[0].RateType.ShouldBe(RateType.Fixed);
        continued.RateOn(new DateOnly(2027, 3, 10)).ShouldBe(0.0449m);
        continued.Periods[1].RepaymentType.ShouldBe(RepaymentType.StraightLine);
    }

    [TestMethod]
    public void Starting_from_a_balance_is_refused_when_nothing_is_left_or_the_next_payment_day_does_not_exist()
    {
        var loan = new Loan("Mortgage", _threeMillion, _firstPayment, 12, [Variable(_start, new RateChange(_start, 0.05m))]);
        var endOfMonth = new Loan("Month end", _threeMillion, new DateOnly(2026, 1, 31), 12, [Variable(new DateOnly(2025, 12, 31), new RateChange(new DateOnly(2025, 12, 31), 0.05m))], paidOut: new DateOnly(2025, 12, 31));

        loan.StartingFrom(new DateOnly(2027, 1, 1), 100m).ShouldBeNull();
        loan.StartingFrom(new DateOnly(2026, 6, 1), 0m).ShouldBeNull();
        loan.StartingFrom(new DateOnly(2025, 12, 31), 100m).ShouldBeNull();
        endOfMonth.StartingFrom(new DateOnly(2026, 1, 30), 100m).ShouldNotBeNull();
        endOfMonth.StartingFrom(new DateOnly(2026, 2, 10), 100m).ShouldBeNull();
    }
}
