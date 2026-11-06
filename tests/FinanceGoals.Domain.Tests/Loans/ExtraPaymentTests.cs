using FinanceGoals.Domain.Currencies;
using FinanceGoals.Domain.Loans;
using FsCheck;
using FsCheck.Fluent;

namespace FinanceGoals.Domain.Tests.Loans;

[TestClass]
public sealed class ExtraPaymentTests
{
    private static readonly Currency _nok = new("NOK", 2);
    private static readonly DateOnly _start = new(2026, 1, 1);
    private static readonly DateOnly _firstPayment = new(2026, 2, 1);

    private static Loan Annuity(decimal principal, decimal rate, int months, params RateChange[] changes) =>
        new("Mortgage", new Money(principal, _nok), _firstPayment, months,
            [new LoanPeriod(_start, RateType.Variable, RepaymentType.Annuity, [new RateChange(_start, rate), .. changes])]);

    private static Money Nok(decimal amount) => new(amount, _nok);

    private static ExtraPayments OneOff(params ExtraPayment[] extras) => new(extras, []);

    [TestMethod]
    public void An_extra_payment_keeps_the_payment_and_shortens_the_loan()
    {
        var loan = Annuity(1_000_000m, 0.05m, 300);

        var schedule = LoanSchedule.Calculate(loan, OneOff(new ExtraPayment(new DateOnly(2026, 6, 15), Nok(100_000m))));

        var july = schedule.Single(i => i.Date == new DateOnly(2026, 7, 1));
        july.Extra.ShouldBe(Nok(100_000m));
        july.Balance.ShouldBe(july.OpeningBalance - july.Principal - july.Extra);
        schedule.Take(schedule.Count - 1).ShouldAllBe(i => i.Payment.Amount == 5_845.90m);
        schedule.Count.ShouldBeLessThan(300);
        schedule[^1].Balance.Amount.ShouldBe(0m);
    }

    [TestMethod]
    public void The_extra_is_paid_with_the_next_payment_and_lowers_the_following_interest()
    {
        var schedule = LoanSchedule.Calculate(Annuity(1_000_000m, 0.06m, 300), OneOff(new ExtraPayment(new DateOnly(2026, 2, 2), Nok(10_000m))));

        schedule[0].Extra.Amount.ShouldBe(0m);
        schedule[1].Extra.ShouldBe(Nok(10_000m));
        schedule[2].Interest.ShouldBe(new Money(decimal.Round(schedule[2].OpeningBalance.Amount * 0.005m, 2, MidpointRounding.AwayFromZero), _nok));
        schedule[2].OpeningBalance.ShouldBe(schedule[1].Balance);
    }

    [TestMethod]
    public void A_rate_change_after_an_extra_recalculates_the_payment_over_the_shortened_term()
    {
        var loan = Annuity(1_000_000m, 0.05m, 300, new RateChange(new DateOnly(2027, 1, 1), 0.06m));

        var schedule = LoanSchedule.Calculate(loan, OneOff(new ExtraPayment(new DateOnly(2026, 6, 1), Nok(200_000m))));

        // After the June extra the 5 845,90 payment needs fewer months; from the January 2027 interest
        // month (paid 1 February) the payment is PMT(6 % / 12, those months less the ones paid, balance).
        var june = schedule.Single(i => i.Date == new DateOnly(2026, 6, 1));
        var january = schedule.Single(i => i.Date == new DateOnly(2027, 1, 1));
        var february = schedule.Single(i => i.Date == new DateOnly(2027, 2, 1));
        var monthsLeft = LoanSchedule.PaymentsToRepay(june.Balance.Amount, 0.05m / 12m, 5_845.90m) - 7;
        var expected = decimal.Round(LoanSchedule.AnnuityPayment(january.Balance.Amount, 0.06m / 12m, monthsLeft), 2, MidpointRounding.AwayFromZero);
        february.Payment.Amount.ShouldBe(expected);
        schedule.Count.ShouldBe(february.Number + monthsLeft - 1);
        schedule[^1].Balance.Amount.ShouldBe(0m);
    }

    [TestMethod]
    public void A_monthly_extra_runs_from_its_start_to_its_end()
    {
        var monthly = new MonthlyExtraPayment(Nok(2_000m), new DateOnly(2026, 4, 1), new DateOnly(2026, 12, 31));

        var schedule = LoanSchedule.Calculate(Annuity(1_000_000m, 0.05m, 300), new ExtraPayments([], [monthly]));

        schedule.Where(i => i.Extra.Amount > 0).Select(i => i.Date.Month).ShouldBe([4, 5, 6, 7, 8, 9, 10, 11, 12]);
        schedule.ShouldAllBe(i => i.Extra.Amount == 0m || i.Extra.Amount == 2_000m);
    }

    [TestMethod]
    public void An_extra_larger_than_the_balance_clears_the_loan()
    {
        var schedule = LoanSchedule.Calculate(Annuity(100_000m, 0.05m, 120), OneOff(new ExtraPayment(new DateOnly(2026, 3, 1), Nok(1_000_000m))));

        schedule.Count.ShouldBe(2);
        (schedule.Sum(i => i.Principal.Amount) + schedule.Sum(i => i.Extra.Amount)).ShouldBe(100_000m);
        schedule[^1].Balance.Amount.ShouldBe(0m);
    }

    [TestMethod]
    public void An_extra_in_another_currency_is_rejected()
    {
        var eur = new Currency("EUR", 2);

        Should.Throw<CurrencyMismatchException>(() =>
            LoanSchedule.Calculate(Annuity(100_000m, 0.05m, 120), OneOff(new ExtraPayment(_firstPayment, new Money(100m, eur)))));
    }

    [TestMethod]
    public void Compare_shows_the_months_and_interest_saved()
    {
        var loan = Annuity(1_000_000m, 0.05m, 300);
        var extras = new ExtraPayments([], [new MonthlyExtraPayment(Nok(5_000m), _firstPayment)]);

        var comparison = LoanSchedule.Compare(loan, extras);

        comparison.WithoutExtra.Payments.ShouldBe(300);
        comparison.WithoutExtra.LastPayment.ShouldBe(new DateOnly(2051, 1, 1));
        comparison.WithoutExtra.TotalExtra.Amount.ShouldBe(0m);
        comparison.WithExtra.LastPayment.ShouldBe(_firstPayment.AddMonths(comparison.WithExtra.Payments - 1));
        comparison.MonthsSaved.ShouldBeGreaterThan(150);
        comparison.InterestSaved.Amount.ShouldBeGreaterThan(0m);
        (comparison.WithExtra.TotalInterest + comparison.InterestSaved).ShouldBe(comparison.WithoutExtra.TotalInterest);
    }

    [TestMethod]
    [DataRow(1_000_000, 0.05, 5_845.90, 300)]
    [DataRow(1_000_000, 0.05, 6_000.00, 286)]
    [DataRow(1_200, 0.0, 100.00, 12)]
    [DataRow(1_250, 0.0, 100.00, 13)]
    public void Payments_to_repay_matches_nper_rounded_up(double balance, double annualRate, double payment, int expected)
    {
        LoanSchedule.PaymentsToRepay((decimal)balance, (decimal)annualRate / 12m, (decimal)payment).ShouldBe(expected);
    }

    [TestMethod]
    public void Extra_payments_never_raise_the_payment_or_lengthen_the_loan()
    {
        var cases = Arb.From(
            from principal in Gen.Choose(10_000, 10_000_000)
            from basisPoints in Gen.Choose(0, 1500)
            from months in Gen.Choose(1, 360)
            from extraMonth in Gen.Choose(1, 360)
            from extraPercent in Gen.Choose(1, 120)
            from monthly in Gen.Choose(0, 20_000)
            select (Principal: (decimal)principal, Rate: basisPoints / 10_000m, Months: months,
                ExtraDate: _start.AddMonths(extraMonth).AddDays(-3), Extra: principal * extraPercent / 100m, Monthly: (decimal)monthly));

        Prop.ForAll(cases, c =>
            {
                var loan = Annuity(c.Principal, c.Rate, c.Months);
                var extras = new ExtraPayments(
                    [new ExtraPayment(c.ExtraDate, Nok(c.Extra))],
                    c.Monthly > 0 ? [new MonthlyExtraPayment(Nok(c.Monthly), _firstPayment)] : []);
                var baseline = LoanSchedule.Calculate(loan);
                var schedule = LoanSchedule.Calculate(loan, extras);
                var payment = baseline[0].Payment.Amount;
                return schedule.Count <= baseline.Count
                    && schedule.Sum(i => i.Principal.Amount + i.Extra.Amount) == c.Principal
                    && schedule[^1].Balance.Amount == 0m
                    && schedule.All(i => i.Balance.Amount >= 0m && i.Principal.Amount >= 0m)
                    && schedule.SkipLast(1).All(i => i.Payment.Amount <= payment)
                    && schedule.Sum(i => i.Interest.Amount) <= baseline.Sum(i => i.Interest.Amount);
            })
            .QuickCheckThrowOnFailure();
    }
}
