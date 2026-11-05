using FinanceGoals.Domain.Currencies;
using FsCheck;
using FsCheck.Fluent;

namespace FinanceGoals.Domain.Tests.Currencies;

[TestClass]
public sealed class CurrencyConverterTests
{
    private static readonly Currency _nok = new("NOK", 2);
    private static readonly Currency _usd = new("USD", 2);
    private static readonly Currency _eur = new("EUR", 2);
    private static readonly Currency _ron = new("RON", 2);
    private static readonly DateOnly _nov1 = new(2026, 11, 1);
    private static readonly DateOnly _nov5 = new(2026, 11, 5);

    private static readonly InMemoryEurRates _rates = new(
        new EurRate("NOK", _nov1, 11.50m),
        new EurRate("NOK", _nov5, 11.65m),
        new EurRate("USD", _nov1, 1.07m),
        new EurRate("USD", _nov5, 1.08m));

    private readonly CurrencyConverter _converter = new(_rates);

    [TestMethod]
    public void Same_currency_needs_no_rate()
    {
        var result = _converter.Convert(new Money(100m, _ron), _ron, _nov5).ShouldBeOfType<Conversion.Converted>();

        result.Amount.ShouldBe(new Money(100m, _ron));
        result.RateDate.ShouldBeNull();
    }

    [TestMethod]
    public void Converts_to_and_from_eur()
    {
        Converted(new Money(100m, _eur), _nok, _nov5).Amount.ShouldBe(new Money(1165m, _nok));
        Converted(new Money(1165m, _nok), _eur, _nov5).Amount.ShouldBe(new Money(100m, _eur));
    }

    [TestMethod]
    public void Converts_between_other_currencies_through_eur()
    {
        // 1165 NOK = 100 EUR = 108 USD at the 5 November rates.
        var result = Converted(new Money(1165m, _nok), _usd, _nov5);

        result.Amount.ShouldBe(new Money(108m, _usd));
        result.RateDate.ShouldBe(_nov5);
    }

    [TestMethod]
    public void Uses_the_latest_rate_on_or_before_the_date()
    {
        Converted(new Money(100m, _eur), _nok, new DateOnly(2026, 11, 3)).Amount.ShouldBe(new Money(1150m, _nok));
        Converted(new Money(100m, _eur), _nok, new DateOnly(2026, 12, 24)).Amount.ShouldBe(new Money(1165m, _nok));
    }

    [TestMethod]
    public void Reports_the_oldest_rate_used()
    {
        var rates = new InMemoryEurRates(new EurRate("NOK", _nov1, 11.50m), new EurRate("USD", _nov5, 1.08m));

        new CurrencyConverter(rates).Convert(new Money(1m, _nok), _usd, _nov5)
            .ShouldBeOfType<Conversion.Converted>().RateDate.ShouldBe(_nov1);
    }

    [TestMethod]
    public void A_missing_rate_is_reported_and_never_treated_as_1()
    {
        var beforeAnyRate = new DateOnly(2026, 10, 31);

        var missing = _converter.Convert(new Money(100m, _nok), _usd, beforeAnyRate).ShouldBeOfType<Conversion.RateMissing>();

        missing.CurrencyCodes.ShouldBe(["NOK", "USD"]);
        missing.Warning.ShouldBe("No exchange rate for NOK and USD on or before 2026-10-31.");
        _converter.Convert(new Money(100m, _eur), _ron, _nov5).ShouldBeOfType<Conversion.RateMissing>()
            .CurrencyCodes.ShouldBe(["RON"]);
    }

    [TestMethod]
    public void A_store_that_returns_a_later_rate_is_refused()
    {
        // A store bug (e.g. "<" instead of "<=", or ignoring the date) must not convert at the wrong day's rate.
        var broken = new FixedEurRates(new EurRate("NOK", _nov5, 11.65m));

        Should.Throw<InvalidOperationException>(() => new CurrencyConverter(broken).Convert(new Money(100m, _eur), _nok, _nov1));
    }

    [TestMethod]
    public void A_store_that_returns_another_currency_is_refused()
    {
        var broken = new FixedEurRates(new EurRate("USD", _nov1, 1.07m));

        Should.Throw<InvalidOperationException>(() => new CurrencyConverter(broken).Convert(new Money(100m, _eur), _nok, _nov5));
    }

    [TestMethod]
    public void Two_definitions_of_the_same_code_are_never_converted()
    {
        Should.Throw<CurrencyMismatchException>(() => _converter.Convert(new Money(1m, _nok), new Currency("NOK", 3), _nov5));
    }

    [TestMethod]
    public void Rates_must_be_positive()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new EurRate("NOK", _nov5, 0m));
        Should.Throw<ArgumentOutOfRangeException>(() => new EurRate("NOK", _nov5, -1m));
    }

    [TestMethod]
    [DataRow("nok")]
    [DataRow("NOKK")]
    [DataRow("")]
    public void Rates_need_an_iso_code(string code)
    {
        Should.Throw<ArgumentException>(() => new EurRate(code, _nov5, 11.65m));
    }

    [TestMethod]
    public void Converting_matches_the_cross_rate()
    {
        // NOK to USD on 5 November is amount * (1.08 / 11.65); a swapped formula would not match.
        var amounts = Arb.From(
            from whole in Gen.Choose(-1_000_000_000, 1_000_000_000)
            from cents in Gen.Choose(0, 99)
            select whole + cents / 100m);

        Prop.ForAll(amounts, amount =>
                Converted(new Money(amount, _nok), _usd, _nov5).Amount.Round()
                    == new Money(amount * (1.08m / 11.65m), _usd).Round())
            .QuickCheckThrowOnFailure();
    }

    [TestMethod]
    public void Converting_there_and_back_returns_the_amount()
    {
        var amounts = Arb.From(
            from whole in Gen.Choose(-1_000_000_000, 1_000_000_000)
            from cents in Gen.Choose(0, 99)
            select whole + cents / 100m);

        Prop.ForAll(amounts, amount =>
            {
                var there = Converted(new Money(amount, _nok), _usd, _nov5).Amount;
                var back = Converted(there, _nok, _nov5).Amount;
                return back.Round() == new Money(amount, _nok).Round();
            })
            .QuickCheckThrowOnFailure();
    }

    private Conversion.Converted Converted(Money amount, Currency target, DateOnly date) =>
        _converter.Convert(amount, target, date).ShouldBeOfType<Conversion.Converted>();

    /// <summary>Returns the same rate whatever is asked, to test how the converter handles a broken store.</summary>
    private sealed class FixedEurRates : IEurRates
    {
        private readonly EurRate _rate;

        public FixedEurRates(EurRate rate)
        {
            _rate = rate;
        }

        public EurRate? LatestOnOrBefore(string currencyCode, DateOnly day) => _rate;
    }

    private sealed class InMemoryEurRates : IEurRates
    {
        private readonly EurRate[] _rates;

        public InMemoryEurRates(params EurRate[] rates)
        {
            _rates = rates;
        }

        public EurRate? LatestOnOrBefore(string currencyCode, DateOnly day) =>
            _rates.Where(rate => rate.CurrencyCode == currencyCode && rate.Date <= day)
                .MaxBy(rate => rate.Date);
    }
}
