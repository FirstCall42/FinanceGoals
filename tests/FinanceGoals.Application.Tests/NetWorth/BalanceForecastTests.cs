using FinanceGoals.Application.Accounts;
using FinanceGoals.Application.Currencies;
using FinanceGoals.Application.NetWorth;
using FinanceGoals.Domain.Currencies;

namespace FinanceGoals.Application.Tests.NetWorth;

[TestClass]
public sealed class BalanceForecastTests
{
    private static readonly DateOnly _today = new(2026, 3, 1);
    private static readonly Currency _nok = new("NOK", 2);
    private static readonly Dictionary<string, int> _minorUnits = new() { ["NOK"] = 2, ["USD"] = 2, ["RON"] = 2 };

    // Units per euro: 1 000 USD = 800 EUR = 8 000 NOK. There is no RON rate.
    private static readonly CurrencyConverter _converter = new(new InMemoryEurRates(
    [
        new EurRate("NOK", new DateOnly(2026, 1, 1), 10m),
        new EurRate("USD", new DateOnly(2026, 1, 1), 1.25m),
    ]));

    private static Account Acc(int id, string currency = "NOK", DateOnly? archivedOn = null) =>
        new(id, $"Account {id}", AccountType.Savings, currency, archivedOn is not null, archivedOn);

    private static AccountValue Val(int accountId, decimal amount) => new(accountId, new DateOnly(2026, 2, 28), amount);

    private static ForecastResult Run(int months, Account[] accounts, AccountValue[] values, params AccountAssumption[] assumptions) =>
        BalanceForecast.Project(_today, months, _nok, accounts, values, assumptions, _minorUnits, _converter);

    [TestMethod]
    public void Without_expectations_the_balance_stays_and_all_three_lines_agree()
    {
        var result = Run(12, [Acc(1)], [Val(1, 1_000m)]);

        result.Points.Count.ShouldBe(13);
        result.Points[0].Date.ShouldBe(_today);
        result.Points.ShouldAllBe(p => p.Low == 1_000m && p.Expected == 1_000m && p.High == 1_000m);
    }

    [TestMethod]
    public void A_monthly_contribution_is_added_at_the_end_of_each_month()
    {
        var result = Run(3, [Acc(1)], [Val(1, 1_000m)], new AccountAssumption(1, null, 100m));

        result.Points.Select(p => p.Expected).ShouldBe([1_000m, 1_100m, 1_200m, 1_300m]);
        result.Points[3].Low.ShouldBe(1_300m);
        result.Points[3].High.ShouldBe(1_300m);
    }

    [TestMethod]
    public void The_expected_return_compounds_and_the_range_is_two_points_each_way()
    {
        var result = Run(12, [Acc(1)], [Val(1, 1_000m)], new AccountAssumption(1, 0.12m, null));

        var year = result.Points[12];
        year.Expected.ShouldBeInRange(1_119.90m, 1_120.10m);
        year.Low.ShouldBeInRange(1_099.90m, 1_100.10m);
        year.High.ShouldBeInRange(1_139.90m, 1_140.10m);
        result.ReturnSpread.ShouldBe(0.02m);
    }

    [TestMethod]
    public void Other_currencies_are_converted_at_todays_rate_and_a_missing_rate_is_listed_not_counted_at_1()
    {
        var result = Run(
            1,
            [Acc(1, "USD"), Acc(2, "RON"), Acc(3)],
            [Val(1, 1_000m), Val(2, 20_000m), Val(3, 500m)],
            new AccountAssumption(1, null, 100m));

        result.Points[0].Expected.ShouldBe(8_500m);
        result.Points[1].Expected.ShouldBe(9_300m);
        result.MissingRates.ShouldBe(["RON"]);
    }

    [TestMethod]
    public void Extreme_inputs_stop_at_a_cap_instead_of_overflowing()
    {
        // 100 % a year and a huge contribution for fifty years, converted USD to NOK.
        var result = Run(600, [Acc(1, "USD")], [Val(1, 10_000_000_000_000m)], new AccountAssumption(1, 1m, 1_000_000_000_000m));

        result.Capped.ShouldBeTrue();
        result.Points.Count.ShouldBe(601);
        result.Points[^1].High.ShouldBeGreaterThan(0m);
        Run(12, [Acc(1)], [Val(1, 1_000m)]).Capped.ShouldBeFalse();
    }

    [TestMethod]
    public void Archived_accounts_and_accounts_without_a_balance_are_left_out()
    {
        var result = Run(2, [Acc(1, archivedOn: new DateOnly(2026, 1, 1)), Acc(2), Acc(3)], [Val(1, 900m), Val(3, 50m)]);

        result.Points.ShouldAllBe(p => p.Expected == 50m);
    }
}
