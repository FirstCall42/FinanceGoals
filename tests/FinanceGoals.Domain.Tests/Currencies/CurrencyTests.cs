using FinanceGoals.Domain.Currencies;

namespace FinanceGoals.Domain.Tests.Currencies;

[TestClass]
public sealed class CurrencyTests
{
    [TestMethod]
    [DataRow("NOK", 2)]
    [DataRow("JPY", 0)]
    [DataRow("BHD", 3)]
    public void Accepts_iso_codes_and_minor_units(string code, int minorUnits)
    {
        var currency = new Currency(code, minorUnits);

        currency.Code.ShouldBe(code);
        currency.MinorUnits.ShouldBe(minorUnits);
    }

    [TestMethod]
    [DataRow("nok")]
    [DataRow("NO")]
    [DataRow("NOKK")]
    [DataRow("N0K")]
    [DataRow("")]
    public void Rejects_codes_that_are_not_three_upper_case_letters(string code)
    {
        Should.Throw<ArgumentException>(() => new Currency(code, 2));
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(5)]
    public void Rejects_minor_units_outside_0_to_4(int minorUnits)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new Currency("NOK", minorUnits));
    }
}
