namespace FinanceGoals.Domain.Currencies;

/// <summary>Amounts in different currencies were combined without converting them first.</summary>
public sealed class CurrencyMismatchException : InvalidOperationException
{
    public CurrencyMismatchException(Currency left, Currency right)
        : base($"Cannot combine {left.Code} and {right.Code} without converting one of them first.")
    {
        Left = left;
        Right = right;
    }

    public Currency Left { get; }

    public Currency Right { get; }
}
