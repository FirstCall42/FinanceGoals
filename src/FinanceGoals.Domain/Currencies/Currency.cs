using System.Text.RegularExpressions;

namespace FinanceGoals.Domain.Currencies;

/// <summary>
/// An ISO 4217 currency: its three-letter code and how many minor units (decimal places) it has,
/// e.g. NOK with 2, JPY with 0. The list of currencies lives in the database (app.Currency).
/// </summary>
public readonly partial record struct Currency
{
    public Currency(string code, int minorUnits)
    {
        if (code is null || !CodePattern().IsMatch(code))
        {
            throw new ArgumentException($"'{code}' is not an ISO 4217 code (three upper-case letters).", nameof(code));
        }

        if (minorUnits is < 0 or > 4)
        {
            throw new ArgumentOutOfRangeException(nameof(minorUnits), minorUnits, "Minor units must be between 0 and 4.");
        }

        Code = code;
        MinorUnits = minorUnits;
    }

    public string Code { get; }

    public int MinorUnits { get; }

    public override string ToString() => Code;

    [GeneratedRegex(@"\A[A-Z]{3}\z")]
    private static partial Regex CodePattern();
}
