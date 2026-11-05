using FinanceGoals.Domain.Currencies;
using FsCheck;
using FsCheck.Fluent;

namespace FinanceGoals.Domain.Tests.Currencies;

/// <summary>Properties that must hold for any amounts, checked on generated inputs.</summary>
[TestClass]
public sealed class MoneyPropertyTests
{
    // Currencies with 0, 2, 3 and 4 minor units, so rounding is checked against each.
    private static readonly Gen<Currency> _currencies =
        Gen.Elements(new Currency("JPY", 0), new Currency("NOK", 2), new Currency("BHD", 3), new Currency("CLF", 4));

    // Amounts up to a billion with up to 6 decimals: realistic sizes, including midpoints such as
    // 0.005 or 0.5, and far from decimal's limits so sums never overflow.
    private static readonly Gen<decimal> _amounts =
        from whole in Gen.Choose(-1_000_000_000, 1_000_000_000)
        from micros in Gen.Choose(0, 999_999)
        from negative in Gen.Elements(true, false)
        select (whole + micros / 1_000_000m) * (negative && whole == 0 ? -1 : 1);

    private static readonly Arbitrary<(Currency Currency, decimal A, decimal B, decimal C)> _triples = Arb.From(
        from currency in _currencies
        from a in _amounts
        from b in _amounts
        from c in _amounts
        select (currency, a, b, c));

    [TestMethod]
    public void Addition_is_commutative_and_associative()
    {
        Prop.ForAll(_triples, t =>
            {
                Money a = new(t.A, t.Currency), b = new(t.B, t.Currency), c = new(t.C, t.Currency);
                return a + b == b + a && (a + b) + c == a + (b + c);
            })
            .QuickCheckThrowOnFailure();
    }

    [TestMethod]
    public void Subtracting_or_negating_undoes_adding()
    {
        Prop.ForAll(_triples, t =>
            {
                Money a = new(t.A, t.Currency), b = new(t.B, t.Currency);
                return a + b - b == a && a + -a == Money.Zero(t.Currency) && -(-a) == a;
            })
            .QuickCheckThrowOnFailure();
    }

    [TestMethod]
    public void Multiplying_is_the_same_from_either_side()
    {
        Prop.ForAll(_triples, t => new Money(t.A, t.Currency) * (t.B / 1000m) == t.B / 1000m * new Money(t.A, t.Currency))
            .QuickCheckThrowOnFailure();
    }

    [TestMethod]
    public void Rounding_keeps_the_currency_minor_units_and_moves_at_most_half_a_minor_unit()
    {
        Prop.ForAll(_triples, t =>
            {
                var money = new Money(t.A, t.Currency);
                var rounded = money.Round();
                var halfUnit = 0.5m / (decimal)Math.Pow(10, t.Currency.MinorUnits);
                return rounded.Currency == t.Currency
                    && rounded.Amount == decimal.Round(rounded.Amount, t.Currency.MinorUnits)
                    && Math.Abs(rounded.Amount - money.Amount) <= halfUnit
                    && rounded.Round() == rounded;
            })
            .QuickCheckThrowOnFailure();
    }

    [TestMethod]
    public void Ordering_follows_the_amounts_in_one_currency()
    {
        Prop.ForAll(_triples, t =>
            {
                Money a = new(t.A, t.Currency), b = new(t.B, t.Currency);
                return (a < b) == (t.A < t.B)
                    && (a > b) == (t.A > t.B)
                    && (a <= b) == (t.A <= t.B)
                    && (a >= b) == (t.A >= t.B)
                    && Math.Sign(a.CompareTo(b)) == -Math.Sign(b.CompareTo(a));
            })
            .QuickCheckThrowOnFailure();
    }

    [TestMethod]
    public void Any_two_different_currencies_never_combine()
    {
        var pairs = Arb.From(
            from left in _currencies
            from right in _currencies
            where left != right
            from a in _amounts
            from b in _amounts
            select (Left: new Money(a, left), Right: new Money(b, right)));

        Prop.ForAll(pairs, p =>
                Throws(() => p.Left + p.Right) && Throws(() => p.Left - p.Right) && Throws(() => p.Left.CompareTo(p.Right)))
            .QuickCheckThrowOnFailure();
    }

    private static bool Throws(Func<object> action)
    {
        try
        {
            action();
            return false;
        }
        catch (CurrencyMismatchException)
        {
            return true;
        }
    }
}
