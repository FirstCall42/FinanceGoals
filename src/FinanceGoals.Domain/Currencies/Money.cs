using System.Globalization;

namespace FinanceGoals.Domain.Currencies;

/// <summary>
/// An amount in one currency. Amounts in different currencies never mix: adding, subtracting
/// or comparing them throws <see cref="CurrencyMismatchException"/>; convert first.
/// Arithmetic keeps full decimal precision (interest calculations need it); <see cref="Round"/>
/// rounds to the currency's minor units when an amount is stored, shown or paid.
/// </summary>
public readonly record struct Money : IComparable<Money>
{
    public Money(decimal amount, Currency currency)
    {
        if (currency.Code is null)
        {
            throw new ArgumentException("The currency is missing.", nameof(currency));
        }

        Amount = amount;
        Currency = currency;
    }

    public decimal Amount { get; }

    public Currency Currency { get; }

    public static Money Zero(Currency currency) => new(0m, currency);

    /// <summary>
    /// Rounds to the currency's minor units, halves away from zero (0.005 NOK becomes 0.01), the
    /// way prices and bank amounts are usually rounded.
    /// </summary>
    public Money Round() => new(decimal.Round(Amount, Currency.MinorUnits, MidpointRounding.AwayFromZero), Currency);

    public Money Add(Money other) => new(Amount + SameCurrency(other).Amount, Currency);

    public Money Subtract(Money other) => new(Amount - SameCurrency(other).Amount, Currency);

    public Money Multiply(decimal factor) => new(Amount * factor, Currency);

    public Money Negate() => new(-Amount, Currency);

    public int CompareTo(Money other) => Amount.CompareTo(SameCurrency(other).Amount);

    public static Money operator +(Money left, Money right) => left.Add(right);

    public static Money operator -(Money left, Money right) => left.Subtract(right);

    public static Money operator -(Money value) => value.Negate();

    public static Money operator *(Money left, decimal right) => left.Multiply(right);

    public static Money operator *(decimal left, Money right) => right.Multiply(left);

    public static bool operator <(Money left, Money right) => left.CompareTo(right) < 0;

    public static bool operator >(Money left, Money right) => left.CompareTo(right) > 0;

    public static bool operator <=(Money left, Money right) => left.CompareTo(right) <= 0;

    public static bool operator >=(Money left, Money right) => left.CompareTo(right) >= 0;

    /// <summary>Invariant, for logs and debugging, e.g. "1234.5 NOK". The UI formats amounts itself.</summary>
    public override string ToString() => $"{Amount.ToString(CultureInfo.InvariantCulture)} {Currency.Code}";

    private Money SameCurrency(Money other) =>
        other.Currency == Currency ? other : throw new CurrencyMismatchException(Currency, other.Currency);
}
