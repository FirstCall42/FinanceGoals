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
        var schedule = LoanSchedule.Calculate(Thirty360(Annuity(1_000_000m, 0.05m, 300, new RateChange(new DateOnly(2026, 7, 1), 0.06m))));

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

        var first = LoanSchedule.Calculate(loan)[0];
        first.Total.ShouldBe(first.Payment + new Money(50m, _nok));
    }

    [TestMethod]
    public void Interest_counts_the_actual_days_over_365_like_the_bank()
    {
        // 1 000 000 at 6 %: the level payment with real month lengths is 6 442,26 (plain PMT(0.5 %, 300)
        // is 6 443,01). January has 31 days of interest (5 095,89), February 28 (4 596,54); principal is
        // what the payment leaves.
        var schedule = LoanSchedule.Calculate(Annuity(1_000_000m, 0.06m, 300));

        schedule[0].Interest.ShouldBe(new Money(5_095.89m, _nok));
        schedule[0].Principal.ShouldBe(new Money(1_346.37m, _nok));
        schedule[1].Interest.ShouldBe(new Money(4_596.54m, _nok));
        schedule[1].Principal.ShouldBe(new Money(1_845.72m, _nok));
        schedule.SkipLast(1).ShouldAllBe(i => i.Payment.Amount == 6_442.26m);
        schedule.Count.ShouldBe(300);
        schedule[^1].Balance.Amount.ShouldBe(0m);
    }

    [TestMethod]
    [DataRow(1_000_000, 6, 300)]
    [DataRow(1_000_000, 15, 360)]
    [DataRow(1_000_000, 20, 360)]
    [DataRow(1_000_000, 20, 600)]
    public void Without_rate_changes_the_payment_repays_the_loan_on_time(int principal, int ratePercent, int months)
    {
        // The level payment counts each month's real days, so month lengths leave no residue to
        // compound; the last payment differs only by the øre the payment was rounded by, grown at the rate.
        var schedule = LoanSchedule.Calculate(Annuity(principal, ratePercent / 100m, months));

        var payment = schedule[0].Payment.Amount;
        schedule.Count.ShouldBe(months);
        schedule.SkipLast(1).ShouldAllBe(i => i.Payment.Amount == payment);
        Math.Abs(schedule[^1].Payment.Amount - payment).ShouldBeLessThanOrEqualTo(0.01m * months * Growth(ratePercent / 100m / 12m, months));
    }

    private static decimal Growth(decimal monthlyRate, int months)
    {
        var growth = 1m;
        for (var i = 0; i < months; i++)
        {
            growth *= 1 + monthlyRate;
        }

        return growth;
    }

    [TestMethod]
    public void A_period_across_new_year_counts_each_year_by_its_own_length()
    {
        // 15 December 2027 to 15 January 2028: 17 days of 365 and 14 of 366 (2028 is a leap year).
        var paidOut = new DateOnly(2027, 12, 15);
        var loan = new Loan("Mortgage", new Money(1_000_000m, _nok), new DateOnly(2028, 1, 15), 300,
            [new LoanPeriod(paidOut, RateType.Variable, RepaymentType.Annuity, [new RateChange(paidOut, 0.06m)])], paidOut: paidOut);

        LoanSchedule.Calculate(loan)[0].Interest.ShouldBe(new Money(5_089.60m, _nok));
    }

    [TestMethod]
    public void An_exact_half_ore_of_interest_rounds_up()
    {
        // 143 554,50 at 5 % for 31 days is exactly 609,615; 1 001,00 at 6 % for a 30/360 month is 5,005.
        LoanSchedule.Calculate(Annuity(143_554.50m, 0.05m, 300))[0].Interest.ShouldBe(new Money(609.62m, _nok));
        LoanSchedule.Calculate(Thirty360(Annuity(1_001m, 0.06m, 300)))[0].Interest.ShouldBe(new Money(5.01m, _nok));
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
        first.Principal.ShouldBe(new Money(1_838.90m, _nok));
        first.Total.ShouldBe(new Money(9_236.16m, _nok));
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

    private static Loan StraightLine(decimal principal, decimal rate, int months) =>
        new("Serie", new Money(principal, _nok), _firstPayment, months,
            [new LoanPeriod(_start, RateType.Variable, RepaymentType.StraightLine, [new RateChange(_start, rate)])]);

    [TestMethod]
    public void A_straight_line_loan_repays_the_same_principal_so_payments_fall()
    {
        // Known answer: 120 000 over 12 months at 6 % (30/360): 10 000 a month plus interest on what is
        // left, 600 in the first month, 50 less each month; 0,005 x 10 000 x (12 + 11 + ... + 1) = 3 900 in all.
        var schedule = LoanSchedule.Calculate(Thirty360(StraightLine(120_000m, 0.06m, 12)));

        schedule.Count.ShouldBe(12);
        schedule.ShouldAllBe(i => i.Principal.Amount == 10_000m);
        schedule[0].Payment.ShouldBe(new Money(10_600m, _nok));
        schedule[1].Payment.ShouldBe(new Money(10_550m, _nok));
        schedule[11].Payment.ShouldBe(new Money(10_050m, _nok));
        schedule.Sum(i => i.Interest.Amount).ShouldBe(3_900m);
        schedule[^1].Balance.Amount.ShouldBe(0m);
    }

    [TestMethod]
    public void An_extra_payment_on_a_straight_line_loan_lowers_the_later_payments_and_keeps_the_end()
    {
        var extras = new ExtraPayments([new ExtraPayment(new DateOnly(2026, 4, 1), new Money(20_000m, _nok))], []);

        var schedule = LoanSchedule.Calculate(Thirty360(StraightLine(120_000m, 0.06m, 12)), extras);

        // After two payments 100 000 is left; the third pays 10 000 and the extra 20 000, leaving 70 000 over 9 months.
        schedule[3].Principal.ShouldBe(new Money(7_777.78m, _nok));
        schedule.Count.ShouldBe(12);
        schedule[^1].Date.ShouldBe(new DateOnly(2027, 1, 1));
        schedule[^1].Balance.Amount.ShouldBe(0m);
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
    public void A_fixed_rate_period_gives_way_to_the_next_period_at_its_boundary()
    {
        var variableFrom = new DateOnly(2027, 1, 1);
        var loan = new Loan("Mortgage", new Money(1_200_000m, _nok), _firstPayment, 120,
        [
            new LoanPeriod(_start, RateType.Fixed, RepaymentType.Annuity, [new RateChange(_start, 0.06m)]),
            new LoanPeriod(variableFrom, RateType.Variable, RepaymentType.Annuity, [new RateChange(variableFrom, 0.03m)]),
        ]);

        var schedule = LoanSchedule.Calculate(Thirty360(loan));

        // Interest runs from the day after the previous payment, so the payment on the boundary day still
        // belongs to the fixed period and the next one is the first at the new rate.
        schedule.Where(i => i.Date <= variableFrom).ShouldAllBe(i => i.AnnualRate == 0.06m);
        schedule.Where(i => i.Date > variableFrom).ShouldAllBe(i => i.AnnualRate == 0.03m);
        var before = schedule.Single(i => i.Date == variableFrom);
        var after = schedule.Single(i => i.Date == new DateOnly(2027, 2, 1));
        after.Interest.ShouldBe(new Money(decimal.Round(before.Balance.Amount * 0.03m / 12m, 2, MidpointRounding.AwayFromZero), _nok));
        // The payment is recalculated at the boundary: lower rate, lower payment; and level again afterwards.
        after.Payment.Amount.ShouldBeLessThan(before.Payment.Amount);
        schedule.Where(i => i.Date > variableFrom).Take(100).Select(i => i.Payment.Amount).Distinct().Count().ShouldBe(1);
        schedule[^1].Balance.Amount.ShouldBe(0m);
    }

    [TestMethod]
    public void An_interest_only_period_gives_way_to_straight_line_repayment_at_its_boundary()
    {
        var repaymentFrom = new DateOnly(2027, 1, 1);
        var loan = new Loan("Serie", new Money(100_000m, _nok), _firstPayment, 24,
        [
            new LoanPeriod(_start, RateType.Variable, RepaymentType.InterestOnly, [new RateChange(_start, 0.06m)]),
            new LoanPeriod(repaymentFrom, RateType.Variable, RepaymentType.StraightLine, [new RateChange(repaymentFrom, 0.06m)]),
        ]);

        var schedule = LoanSchedule.Calculate(Thirty360(loan));

        schedule.Where(i => i.Date <= repaymentFrom).ShouldAllBe(i => i.Principal.Amount == 0m);
        // The first payment after the boundary leaves 12 months: 100 000 over 12 is 8 333,33 a month plus 500 interest.
        var first = schedule.Single(i => i.Date == new DateOnly(2027, 2, 1));
        first.Principal.ShouldBe(new Money(8_333.33m, _nok));
        first.Interest.ShouldBe(new Money(500m, _nok));
        schedule.Count.ShouldBe(24);
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
                    && schedule.All(i => i.Balance.Amount >= 0m);
            })
            .QuickCheckThrowOnFailure();
    }
}
