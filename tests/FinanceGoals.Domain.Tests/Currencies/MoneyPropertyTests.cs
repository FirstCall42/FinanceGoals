using FinanceGoals.Domain.Currencies;
using FsCheck;
using FsCheck.Fluent;

namespace FinanceGoals.Domain.Tests.Currencies;

/// <summary>Properties that must hold for any amounts, checked on generated inputs.</summary>
[TestClass]
public sealed class MoneyPropertyTests
{
    private static readonly Currency _nok = new("NOK", 2);
    private static readonly Currency _usd = new("USD", 2);

    // Amounts with up to 6 decimals, well inside decimal's range so sums never overflow.
    private static Money Nok(long units) => new(units / 1_000_000m, _nok);

    [TestMethod]
    public void Addition_is_commutative_and_associative()
    {
        Prop.ForAll<long, long, long>((a, b, c) =>
                Nok(a) + Nok(b) == Nok(b) + Nok(a)
                && (Nok(a) + Nok(b)) + Nok(c) == Nok(a) + (Nok(b) + Nok(c)))
            .QuickCheckThrowOnFailure();
    }

    [TestMethod]
    public void Subtracting_what_was_added_gives_the_original()
    {
        Prop.ForAll<long, long>((a, b) => Nok(a) + Nok(b) - Nok(b) == Nok(a))
            .QuickCheckThrowOnFailure();
    }

    [TestMethod]
    public void Rounding_keeps_minor_units_and_moves_at_most_half_a_minor_unit()
    {
        Prop.ForAll<long>(a =>
            {
                var rounded = Nok(a).Round();
                return rounded.Amount == decimal.Round(rounded.Amount, 2)
                    && Math.Abs(rounded.Amount - Nok(a).Amount) <= 0.005m
                    && rounded.Round() == rounded;
            })
            .QuickCheckThrowOnFailure();
    }

    [TestMethod]
    public void Any_two_different_currencies_never_combine()
    {
        Prop.ForAll<long, long>((a, b) =>
            {
                var nok = Nok(a);
                var usd = new Money(b / 1_000_000m, _usd);
                return Throws(() => nok + usd) && Throws(() => nok - usd) && Throws(() => nok.CompareTo(usd));
            })
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
