using FinanceGoals.Domain.Currencies;
using FinanceGoals.Domain.Loans;
using FsCheck;
using FsCheck.Fluent;

namespace FinanceGoals.Domain.Tests.Loans;

[TestClass]
public sealed class LoanScheduleTests
{
    private static readonly Currency _nok = new("NOK", 2);
    private static readonly DateOnly _start = new(2026, 1, 1);
    private static readonly DateOnly _firstPayment = new(2026, 2, 1);

    // The textbook figures below assume 30/360 (every month 1/12 of the rate); the bank counts actual/365.
    private static Loan Thirty360(Loan loan) =>
        new(loan.Name, loan.Principal, loan.FirstPaymentDate, loan.TermMonths, loan.Periods, loan.FeePerPayment, loan.Start, DayCount.Thirty360);

    private static Loan Annuity(decimal principal, decimal rate, int months, params RateChange[] changes) =>
        new("Mortgage", new Money(principal, _nok), _firstPayment, months,
            [new LoanPeriod(_start, RateType.Variable, RepaymentType.Annuity, [new RateChange(_start, rate), .. changes])]);

    [TestMethod]
    public void Matches_the_standard_annuity_table()
    {
        // Known answer: 1 000 000 at 5 % nominal over 25 years is 5 845,90 a month, the figure in
        // published mortgage tables (r = 0.05 / 12, n = 300).
        var schedule = LoanSchedule.Calculate(Thirty360(Annuity(1_000_000m, 0.05m, 300)));

        var first = schedule[0];
        first.Date.ShouldBe(_firstPayment);
        first.Interest.ShouldBe(new Money(4_166.67m, _nok));
        first.Principal.ShouldBe(new Money(1_679.23m, _nok));
        first.Balance.ShouldBe(new Money(998_320.77m, _nok));
        schedule.Take(299).ShouldAllBe(installment => installment.Interest.Amount + installment.Principal.Amount == 5_845.90m);
        schedule.Count.ShouldBe(300);
        schedule[^1].Date.ShouldBe(new DateOnly(2051, 1, 1));
        schedule[^1].Balance.Amount.ShouldBe(0m);
    }

    [TestMethod]
    public void A_rate_change_recalculates_the_payment_over_the_remaining_term()
    {
        // From 1 July the rate rises to 6 %; the July payment (interest month from 2 June) still uses
        // 5 %, the August payment (interest month from 2 July) uses 6 % and a new payment.
        var schedule = LoanSchedule.Calculate(Annuity(1_000_000m, 0.05m, 300, new RateChange(new DateOnly(2026, 7, 1), 0.06m)));

        var july = schedule.Single(i => i.Date == new DateOnly(2026, 7, 1));
        var august = schedule.Single(i => i.Date == new DateOnly(2026, 8, 1));
        july.AnnualRate.ShouldBe(0.05m);
        august.AnnualRate.ShouldBe(0.06m);

        var expected = decimal.Round(LoanSchedule.AnnuityPayment(july.Balance.Amount, 0.06m / 12m, 300 - 6), 2, MidpointRounding.AwayFromZero);
        (august.Interest + august.Principal).Amount.ShouldBe(expected);
        expected.ShouldBeGreaterThan(5_845.90m);
        schedule[^1].Balance.Amount.ShouldBe(0m);
    }

    [TestMethod]
    public void The_fee_is_added_to_each_payment()
    {
        var loan = new Loan("Mortgage", new Money(1_000_000m, _nok), _firstPayment, 300,
            [new LoanPeriod(_start, RateType.Variable, RepaymentType.Annuity, [new RateChange(_start, 0.05m)])],
            new Money(50m, _nok));

        LoanSchedule.Calculate(loan)[0].Total.ShouldBe(new Money(5_895.90m, _nok));
    }

    [TestMethod]
    public void Interest_counts_the_actual_days_over_365_like_the_bank()
    {
        // 1 000 000 at 6 %: the payment is PMT(0.5 %, 300) = 6 443,01. January has 31 days of
        // interest (5 095,89), February 28 (4 596,54); principal is what the payment leaves.
        var schedule = LoanSchedule.Calculate(Annuity(1_000_000m, 0.06m, 300));

        schedule[0].Interest.ShouldBe(new Money(5_095.89m, _nok));
        schedule[0].Principal.ShouldBe(new Money(1_347.12m, _nok));
        schedule[1].Interest.ShouldBe(new Money(4_596.54m, _nok));
        schedule[1].Principal.ShouldBe(new Money(1_846.47m, _nok));
        schedule.SkipLast(1).ShouldAllBe(i => i.Payment.Amount == 6_443.01m);
        schedule.Count.ShouldBe(300);
        schedule[^1].Balance.Amount.ShouldBe(0m);
    }

    [TestMethod]
    public void A_mid_month_payout_adds_the_extra_days_of_actual_interest_to_the_first_payment()
    {
        // 15 January to 1 March is 45 days (7 397,26); principal is the payment less the 28 days of
        // a regular February (4 602,74), so the 17 extra days are paid on top.
        var paidOut = new DateOnly(2026, 1, 15);
        var loan = new Loan("Mortgage", new Money(1_000_000m, _nok), new DateOnly(2026, 3, 1), 300,
            [new LoanPeriod(paidOut, RateType.Variable, RepaymentType.Annuity, [new RateChange(paidOut, 0.06m)])], paidOut: paidOut);

        var first = LoanSchedule.Calculate(loan)[0];

        first.Interest.ShouldBe(new Money(7_397.26m, _nok));
        first.Principal.ShouldBe(new Money(1_840.27m, _nok));
        first.Total.ShouldBe(new Money(9_237.53m, _nok));
    }

    [TestMethod]
    public void A_mid_month_payout_prorates_the_first_interest_period()
    {
        // Paid out 15 January, first payment 1 March: 46 days on 30/360, so 46/30 months of interest.
        var paidOut = new DateOnly(2026, 1, 15);
        var loan = new Loan("Mortgage", new Money(1_000_000m, _nok), new DateOnly(2026, 3, 1), 300,
            [new LoanPeriod(paidOut, RateType.Variable, RepaymentType.Annuity, [new RateChange(paidOut, 0.06m)])], paidOut: paidOut);

        // The annuity (6 443,01) repays principal as if the month were whole (6 443,01 - 5 000,00);
        // the 16 extra days of interest are paid on top, not added to the loan.
        var first = LoanSchedule.Calculate(Thirty360(loan))[0];
        first.Interest.ShouldBe(new Money(7_666.67m, _nok));
        first.Principal.ShouldBe(new Money(1_443.01m, _nok));
        first.Balance.ShouldBe(new Money(998_556.99m, _nok));
        first.Total.ShouldBe(new Money(9_109.68m, _nok));
    }

    [TestMethod]
    public void An_interest_only_period_repays_nothing_then_the_annuity_starts()
    {
        var annuityFrom = new DateOnly(2027, 1, 1);
        var loan = new Loan("Mortgage", new Money(1_000_000m, _nok), _firstPayment, 120,
        [
            new LoanPeriod(_start, RateType.Variable, RepaymentType.InterestOnly, [new RateChange(_start, 0.05m)]),
            new LoanPeriod(annuityFrom, RateType.Variable, RepaymentType.Annuity, [new RateChange(annuityFrom, 0.05m)]),
        ]);

        var schedule = LoanSchedule.Calculate(Thirty360(loan));

        schedule.Where(i => i.Date <= annuityFrom).ShouldAllBe(i => i.Principal.Amount == 0m && i.Interest.Amount == 4_166.67m);
        schedule.Single(i => i.Date == new DateOnly(2027, 2, 1)).Principal.Amount.ShouldBeGreaterThan(0m);
        schedule[^1].Balance.Amount.ShouldBe(0m);
    }

    [TestMethod]
    public void Principal_always_sums_to_the_loan_amount()
    {
        var loans = Arb.From(
            from principal in Gen.Choose(10_000, 10_000_000)
            from basisPoints in Gen.Choose(0, 1500)
            from months in Gen.Choose(1, 360)
            from changeMonth in Gen.Choose(1, 359)
            from newBasisPoints in Gen.Choose(0, 1500)
            from payoutShift in Gen.Choose(-27, 27)
            select (Principal: (decimal)principal, Rate: basisPoints / 10_000m, Months: months,
                ChangeMonth: Math.Min(changeMonth, months), NewRate: newBasisPoints / 10_000m,
                PaidOut: _start.AddDays(payoutShift)));

        Prop.ForAll(loans, l =>
            {
                RateChange[] rates = l.ChangeMonth < l.Months
                    ? [new RateChange(l.PaidOut, l.Rate), new RateChange(_start.AddMonths(l.ChangeMonth), l.NewRate)]
                    : [new RateChange(l.PaidOut, l.Rate)];
                var loan = new Loan("Mortgage", new Money(l.Principal, _nok), _firstPayment, l.Months,
                    [new LoanPeriod(l.PaidOut, RateType.Variable, RepaymentType.Annuity, rates)], paidOut: l.PaidOut);
                var schedule = LoanSchedule.Calculate(loan);
                return schedule.Count == l.Months
                    && schedule.Sum(i => i.Principal.Amount) == l.Principal
                    && schedule[^1].Balance.Amount == 0m
                    && schedule.All(i => i.Balance.Amount >= 0m && i.Principal.Amount >= 0m);
            })
            .QuickCheckThrowOnFailure();
    }
}
