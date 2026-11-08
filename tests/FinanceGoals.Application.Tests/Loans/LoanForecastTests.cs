using FinanceGoals.Application.Currencies;
using FinanceGoals.Application.Loans;
using FinanceGoals.Domain.Currencies;
using FinanceGoals.Domain.Loans;

namespace FinanceGoals.Application.Tests.Loans;

[TestClass]
public sealed class LoanForecastTests
{
    private static readonly Currency _nok = new("NOK", 2);
    private static readonly Dictionary<string, int> _minorUnits = new() { ["NOK"] = 2, ["USD"] = 2, ["EUR"] = 2, ["RON"] = 2 };
    private static readonly CurrencyConverter _converter = new(new InMemoryEurRates(
    [
        new EurRate("NOK", new DateOnly(2026, 1, 1), 10m),
        new EurRate("USD", new DateOnly(2026, 1, 1), 1.25m),
    ]));

    // A straight-line loan of 12 000 over 12 months from 1 Feb 2026: 1 000 off the balance each payment, no interest.
    private static Loan Straight(string currency = "NOK") =>
        new("Car", new Money(12_000m, new Currency(currency, 2)), new DateOnly(2026, 2, 1), 12,
            [new LoanPeriod(new DateOnly(2026, 1, 1), RateType.Variable, RepaymentType.StraightLine, [new RateChange(new DateOnly(2026, 1, 1), 0m)])]);

    private static LoanExtras Extras(params (string Date, decimal Amount)[] balances) =>
        new([], [], [.. balances.Select(b => new StoredBalance(DateOnly.Parse(b.Date, System.Globalization.CultureInfo.InvariantCulture), b.Amount))]);

    [TestMethod]
    public void A_loan_amortises_from_its_latest_balance_one_payment_a_month()
    {
        // 10 000 owed on 1 Mar 2026 (after the payment that day): 10 payments left from 1 Apr.
        var owed = LoanForecast.Owed(new DateOnly(2026, 3, 15), 12, _nok, [new LoanWithExtras(Straight(), Extras(("2026-03-01", 10_000m)))], _minorUnits, _converter);

        owed.ByMonth[0].ShouldBe(9_000m + 1_000m);
        owed.ByMonth[1].ShouldBe(9_000m);
        owed.ByMonth[2].ShouldBe(8_000m);
        owed.ByMonth[10].ShouldBe(0m);
        owed.ByMonth[12].ShouldBe(0m);
        owed.MissingRates.ShouldBeEmpty();
    }

    [TestMethod]
    public void Extra_payments_after_the_balance_shorten_the_loan_and_earlier_ones_are_ignored()
    {
        var extras = new LoanExtras(
            [new StoredExtra(new DateOnly(2026, 1, 15), 5_000m), new StoredExtra(new DateOnly(2026, 5, 1), 2_000m)],
            [],
            [new StoredBalance(new DateOnly(2026, 3, 1), 10_000m)]);

        var owed = LoanForecast.Owed(new DateOnly(2026, 3, 15), 3, _nok, [new LoanWithExtras(Straight(), extras)], _minorUnits, _converter);

        // 1 Apr 9 000 left, 1 May: one more payment and the 2 000 extra.
        owed.ByMonth[1].ShouldBe(9_000m);
        owed.ByMonth[2].ShouldBe(6_000m);
    }

    [TestMethod]
    public void Loans_are_converted_a_missing_rate_is_listed_and_a_loan_without_a_balance_is_left_out()
    {
        LoanWithExtras[] loans =
        [
            new(Straight("USD"), Extras(("2026-03-01", 1_000m))),
            new(Straight("RON"), Extras(("2026-03-01", 1_000m))),
            new(Straight(), Extras()),
        ];

        var owed = LoanForecast.Owed(new DateOnly(2026, 3, 15), 1, _nok, loans, _minorUnits, _converter);

        // 1 000 USD is 8 000 NOK; the RON loan has no rate.
        owed.ByMonth[0].ShouldBe(8_000m);
        owed.MissingRates.ShouldBe(["RON"]);
    }

    [TestMethod]
    public void A_loan_that_cannot_be_continued_stays_at_its_recorded_balance()
    {
        // The loan ended on 1 Jan 2027; a balance recorded after that has nothing left to continue.
        var owed = LoanForecast.Owed(new DateOnly(2027, 3, 15), 2, _nok, [new LoanWithExtras(Straight(), Extras(("2027-03-01", 500m)))], _minorUnits, _converter);

        owed.ByMonth.ShouldBe([500m, 500m, 500m]);
    }

    [TestMethod]
    public void A_loan_paid_on_the_31st_still_follows_its_schedule_from_a_balance_before_a_short_month()
    {
        var start = new DateOnly(2025, 12, 31);
        var loan = new Loan("Month end", new Money(12_000m, _nok), new DateOnly(2026, 1, 31), 12,
            [new LoanPeriod(start, RateType.Variable, RepaymentType.StraightLine, [new RateChange(start, 0m)])], paidOut: start);

        // 9 000 owed on 5 April, after the payments of 31 Jan, 28 Feb and 31 Mar: 9 payments of 1 000 are left.
        var owed = LoanForecast.Owed(new DateOnly(2026, 4, 5), 10, _nok, [new LoanWithExtras(loan, Extras(("2026-04-05", 9_000m)))], _minorUnits, _converter);

        owed.ByMonth[0].ShouldBe(9_000m);
        owed.ByMonth[1].ShouldBe(8_000m);
        owed.ByMonth[9].ShouldBe(0m);
    }
}
