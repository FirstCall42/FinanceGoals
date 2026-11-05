using System.Globalization;
using FinanceGoals.Domain.Currencies;

namespace FinanceGoals.Domain.Tests.Currencies;

[TestClass]
public sealed class MoneyTests
{
    private static readonly Currency _nok = new("NOK", 2);
    private static readonly Currency _usd = new("USD", 2);
    private static readonly Currency _jpy = new("JPY", 0);

    [TestMethod]
    public void Adds_and_subtracts_amounts_in_the_same_currency()
    {
        var total = new Money(100.50m, _nok) + new Money(20.25m, _nok) - new Money(0.75m, _nok);

        total.ShouldBe(new Money(120.00m, _nok));
    }

    [TestMethod]
    public void Add_rejects_different_currencies()
    {
        var error = Should.Throw<CurrencyMismatchException>(() => new Money(1m, _nok) + new Money(1m, _usd));

        error.Left.ShouldBe(_nok);
        error.Right.ShouldBe(_usd);
    }

    [TestMethod]
    public void Subtract_and_compare_reject_different_currencies()
    {
        Should.Throw<CurrencyMismatchException>(() => new Money(1m, _nok) - new Money(1m, _usd));
        Should.Throw<CurrencyMismatchException>(() => new Money(1m, _nok) < new Money(2m, _usd));
    }

    [TestMethod]
    public void Equal_amounts_in_different_currencies_are_not_equal()
    {
        new Money(1m, _nok).ShouldNotBe(new Money(1m, _usd));
    }

    [TestMethod]
    [DataRow("0.005", "0.01")]
    [DataRow("-0.005", "-0.01")]
    [DataRow("0.004", "0.00")]
    [DataRow("1234.5649", "1234.56")]
    public void Rounds_to_minor_units_with_halves_away_from_zero(string amount, string expected)
    {
        new Money(decimal.Parse(amount, CultureInfo.InvariantCulture), _nok).Round()
            .ShouldBe(new Money(decimal.Parse(expected, CultureInfo.InvariantCulture), _nok));
    }

    [TestMethod]
    public void Rounds_a_currency_without_minor_units_to_whole_units()
    {
        new Money(1234.5m, _jpy).Round().ShouldBe(new Money(1235m, _jpy));
    }

    [TestMethod]
    public void Multiplying_keeps_full_precision_until_rounded()
    {
        var interest = new Money(1000m, _nok) * (0.0375m / 12);

        interest.Amount.ShouldBe(3.125m);
        interest.Round().Amount.ShouldBe(3.13m);
    }

    [TestMethod]
    public void Money_without_a_currency_is_rejected()
    {
        Should.Throw<ArgumentException>(() => new Money(1m, default));
    }

    [TestMethod]
    public void Prints_amount_and_code_invariantly()
    {
        new Money(-1234.5m, _nok).ToString().ShouldBe("-1234.5 NOK");
    }
}
