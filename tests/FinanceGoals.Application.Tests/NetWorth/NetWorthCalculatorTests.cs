using FinanceGoals.Application.Accounts;
using FinanceGoals.Application.Currencies;
using FinanceGoals.Application.NetWorth;
using FinanceGoals.Domain.Currencies;

namespace FinanceGoals.Application.Tests.NetWorth;

[TestClass]
public sealed class NetWorthCalculatorTests
{
    private static readonly Currency _nok = new("NOK", 2);
    private static readonly Dictionary<string, int> _minorUnits = new() { ["NOK"] = 2, ["USD"] = 2, ["EUR"] = 2, ["RON"] = 2, ["JPY"] = 0 };

    // Round-number rates, units per euro: 1 000 USD = 800 EUR = 8 000 NOK.
    private static readonly CurrencyConverter _converter = new(new InMemoryEurRates(
    [
        new EurRate("NOK", new DateOnly(2026, 1, 1), 10m),
        new EurRate("USD", new DateOnly(2026, 1, 1), 1.25m),
        new EurRate("USD", new DateOnly(2026, 6, 1), 1m),
    ]));

    private static Account Acc(int id, string name, AccountType type, string currency, DateOnly? archivedOn = null) =>
        new(id, name, type, currency, archivedOn is not null, archivedOn);

    private static AccountValue Val(int accountId, string date, decimal amount) =>
        new(accountId, DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture), amount);

    private static NetWorthResult On(string date, IReadOnlyList<Account> accounts, IReadOnlyList<AccountValue> values) =>
        NetWorthCalculator.On(DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture), _nok, accounts, values, _minorUnits, _converter);

    [TestMethod]
    public void Adds_the_accounts_in_the_target_currency_and_leaves_property_and_pensions_out_of_liquid()
    {
        Account[] accounts =
        [
            Acc(1, "Savings", AccountType.Savings, "NOK"),
            Acc(2, "Brokerage", AccountType.Brokerage, "USD"),
            Acc(3, "Pension", AccountType.Pension, "USD"),
            Acc(4, "House", AccountType.Property, "NOK"),
        ];
        AccountValue[] values = [Val(1, "2026-02-28", 50_000m), Val(2, "2026-02-28", 1_000m), Val(3, "2026-02-28", 2_000m), Val(4, "2026-02-28", 4_000_000m)];

        var result = On("2026-03-01", accounts, values);

        result.Currency.ShouldBe("NOK");
        result.Lines.Select(l => (l.Name, l.Converted)).ShouldBe(
        [
            ("Brokerage", 8_000m), ("House", 4_000_000m), ("Pension", 16_000m), ("Savings", 50_000m),
        ]);
        result.Total.ShouldBe(4_074_000m);
        result.Liquid.ShouldBe(58_000m);
        result.MissingRates.ShouldBeEmpty();
        result.Lines.Single(l => l.Name == "Brokerage").RateDate.ShouldBe(new DateOnly(2026, 1, 1));
        result.Lines.Single(l => l.Name == "Savings").RateDate.ShouldBeNull();
    }

    [TestMethod]
    public void Uses_the_latest_balance_on_or_before_the_date_and_the_rate_of_that_date()
    {
        Account[] accounts = [Acc(1, "Brokerage", AccountType.Brokerage, "USD")];
        AccountValue[] values = [Val(1, "2026-02-28", 1_000m), Val(1, "2026-07-31", 2_000m)];

        var march = On("2026-03-15", accounts, values);
        var august = On("2026-08-15", accounts, values);

        // March: 1 000 USD at 1.25 per euro. August: 2 000 USD at 1.00 per euro (the June rate).
        march.Total.ShouldBe(8_000m);
        march.Lines[0].BalanceDate.ShouldBe(new DateOnly(2026, 2, 28));
        august.Total.ShouldBe(20_000m);
        august.Lines[0].BalanceDate.ShouldBe(new DateOnly(2026, 7, 31));
        august.Lines[0].RateDate.ShouldBe(new DateOnly(2026, 1, 1), "the oldest rate used: NOK's, from January");
    }

    [TestMethod]
    public void An_account_without_a_balance_yet_is_not_counted()
    {
        Account[] accounts = [Acc(1, "Old", AccountType.Savings, "NOK"), Acc(2, "New", AccountType.Savings, "NOK")];
        AccountValue[] values = [Val(1, "2026-01-31", 100m), Val(2, "2026-06-30", 900m)];

        On("2026-03-01", accounts, values).Total.ShouldBe(100m);
        On("2026-07-01", accounts, values).Total.ShouldBe(1_000m);
        On("2025-12-31", accounts, values).Lines.ShouldBeEmpty();
    }

    [TestMethod]
    public void An_archived_account_counts_only_before_the_day_it_was_archived()
    {
        Account[] accounts = [Acc(1, "Closed", AccountType.Deposit, "NOK", archivedOn: new DateOnly(2026, 5, 10))];
        AccountValue[] values = [Val(1, "2026-04-30", 700m)];

        On("2026-05-09", accounts, values).Total.ShouldBe(700m);
        On("2026-05-10", accounts, values).Total.ShouldBe(0m);
        On("2026-12-31", accounts, values).Lines.ShouldBeEmpty();
    }

    [TestMethod]
    public void A_missing_rate_leaves_the_account_out_names_the_currency_and_never_uses_1()
    {
        Account[] accounts = [Acc(1, "Savings", AccountType.Savings, "NOK"), Acc(2, "Romania", AccountType.Current, "RON")];
        AccountValue[] values = [Val(1, "2026-02-28", 1_000m), Val(2, "2026-02-28", 20_000m)];

        var result = On("2026-03-01", accounts, values);

        result.MissingRates.ShouldBe(["RON"]);
        result.Total.ShouldBe(1_000m, "20 000 RON must not be added as if the rate were 1");
        var romania = result.Lines.Single(l => l.Name == "Romania");
        romania.Converted.ShouldBeNull();
        romania.Amount.ShouldBe(20_000m);
    }

    [TestMethod]
    public void Nothing_to_convert_needs_no_rates_and_a_rate_before_the_date_is_required()
    {
        var empty = new CurrencyConverter(new InMemoryEurRates([]));
        Account[] accounts = [Acc(1, "Savings", AccountType.Savings, "NOK")];
        AccountValue[] values = [Val(1, "2026-02-28", 1_000m)];

        NetWorthCalculator.On(new DateOnly(2026, 3, 1), _nok, accounts, values, _minorUnits, empty).Total.ShouldBe(1_000m);
        // USD has a rate from 1 January 2026 only: on 31 December 2025 there is none.
        Account[] usd = [Acc(2, "Brokerage", AccountType.Brokerage, "USD")];
        AccountValue[] usdValues = [Val(2, "2025-12-01", 1_000m)];
        On("2025-12-31", usd, usdValues).MissingRates.ShouldBe(["NOK", "USD"]);
    }

    [TestMethod]
    public void The_total_is_the_sum_of_the_rounded_lines_so_what_is_shown_adds_up()
    {
        // 3 x 1 000.004 USD is 3 x 8 000.032 NOK: each line rounds to 8 000.03, and the total is 24 000.09.
        Account[] accounts = [Acc(1, "A", AccountType.Brokerage, "USD"), Acc(2, "B", AccountType.Brokerage, "USD"), Acc(3, "C", AccountType.Brokerage, "USD")];
        AccountValue[] values = [Val(1, "2026-02-28", 1_000.004m), Val(2, "2026-02-28", 1_000.004m), Val(3, "2026-02-28", 1_000.004m)];

        var result = On("2026-03-01", accounts, values);

        result.Lines.ShouldAllBe(l => l.Converted == 8_000.03m);
        result.Total.ShouldBe(24_000.09m);
        result.Total.ShouldBe(result.Lines.Sum(l => l.Converted!.Value));
    }

    [TestMethod]
    public void The_monthly_history_ends_each_month_and_at_the_given_day()
    {
        Account[] accounts = [Acc(1, "Savings", AccountType.Savings, "NOK"), Acc(2, "House", AccountType.Property, "NOK")];
        AccountValue[] values = [Val(1, "2026-01-31", 100m), Val(2, "2026-01-31", 1_000m), Val(1, "2026-03-31", 150m)];

        var points = NetWorthCalculator.Monthly(new DateOnly(2026, 4, 15), _nok, accounts, values, _minorUnits, _converter);

        points.Select(p => p.Date).ShouldBe(
        [
            new DateOnly(2026, 1, 31), new DateOnly(2026, 2, 28), new DateOnly(2026, 3, 31), new DateOnly(2026, 4, 15),
        ]);
        points.Select(p => p.Total).ShouldBe([1_100m, 1_100m, 1_150m, 1_150m]);
        points.Select(p => p.Liquid).ShouldBe([100m, 100m, 150m, 150m]);
    }

    [TestMethod]
    public void Each_history_point_names_the_oldest_rate_behind_it_and_none_when_nothing_was_converted()
    {
        var usd = new Currency("USD", 2);
        var to = new DateOnly(2026, 7, 15);
        Account[] nok = [Acc(1, "Savings", AccountType.Savings, "NOK")];
        Account[] same = [Acc(2, "Brokerage", AccountType.Brokerage, "USD")];
        Account[] ron = [Acc(3, "Romania", AccountType.Current, "RON")];

        // NOK in USD uses the NOK rate (2026-01-01) and the USD rate (2026-01-01, then 2026-06-01): the oldest wins.
        var converted = NetWorthCalculator.Monthly(to, usd, nok, [Val(1, "2026-01-31", 100m)], _minorUnits, _converter);
        var unconverted = NetWorthCalculator.Monthly(to, usd, same, [Val(2, "2026-01-31", 100m)], _minorUnits, _converter);
        var missing = NetWorthCalculator.Monthly(to, usd, ron, [Val(3, "2026-01-31", 100m)], _minorUnits, _converter);

        converted.ShouldAllBe(p => p.RateDate == new DateOnly(2026, 1, 1));
        unconverted.ShouldAllBe(p => p.RateDate == null);
        missing.ShouldAllBe(p => p.RateDate == null && p.MissingRates.Count == 1 && p.MissingRates[0] == "RON");
    }

    [TestMethod]
    public void The_history_of_no_balances_is_empty()
    {
        NetWorthCalculator.Monthly(new DateOnly(2026, 4, 15), _nok, [], [], _minorUnits, _converter).ShouldBeEmpty();
    }

    [TestMethod]
    [DataRow("2025-12-31", null)]
    [DataRow("2026-01-01", "2026-01-01")]
    [DataRow("2026-03-15", "2026-01-01")]
    [DataRow("2026-06-01", "2026-06-01")]
    [DataRow("2027-01-01", "2026-06-01")]
    public void In_memory_rates_answer_with_the_latest_on_or_before_the_day(string day, string? expected)
    {
        var rates = new InMemoryEurRates([new EurRate("USD", new DateOnly(2026, 6, 1), 1m), new EurRate("USD", new DateOnly(2026, 1, 1), 1.25m)]);

        var found = rates.LatestOnOrBefore("USD", DateOnly.Parse(day, System.Globalization.CultureInfo.InvariantCulture));

        found?.Date.ShouldBe(expected is null ? default : DateOnly.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));
        (found is null).ShouldBe(expected is null);
        rates.LatestOnOrBefore("NOK", new DateOnly(2026, 3, 1)).ShouldBeNull();
    }
}
