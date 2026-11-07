using FinanceGoals.Application.Accounts;
using FinanceGoals.Application.Loans;
using FinanceGoals.Domain.Currencies;

namespace FinanceGoals.Application.NetWorth;

/// <summary>One account's part of the net worth on a date.</summary>
/// <param name="AccountId"></param>
/// <param name="Name"></param>
/// <param name="Type"></param>
/// <param name="CurrencyCode"></param>
/// <param name="Amount">The balance in the account's own currency.</param>
/// <param name="BalanceDate">The day of that balance: the latest on or before the date asked for.</param>
/// <param name="Converted">The balance in the target currency, rounded to its minor units; null when a rate is missing.</param>
/// <param name="RateDate">The date of the oldest exchange rate used; null when no conversion was needed or possible.</param>
/// <param name="Liquid"></param>
public sealed record NetWorthLine(
    int AccountId,
    string Name,
    AccountType Type,
    string CurrencyCode,
    decimal Amount,
    DateOnly BalanceDate,
    decimal? Converted,
    DateOnly? RateDate,
    bool Liquid);

/// <summary>A loan with its recorded balances, as net worth counts it (#135).</summary>
public sealed record LoanDebtInput(int LoanId, string Name, string CurrencyCode, bool SecuredOnProperty, DateOnly PaidOut, IReadOnlyList<StoredBalance> Balances);

/// <summary>One loan's part of the net worth on a date: a debt at its latest recorded balance, shown as a positive amount owed.</summary>
/// <param name="LoanId"></param>
/// <param name="Name"></param>
/// <param name="CurrencyCode"></param>
/// <param name="Amount">The balance owed in the loan's own currency.</param>
/// <param name="BalanceDate">The day of that balance: the latest on or before the date asked for.</param>
/// <param name="Converted">The balance in the target currency, rounded to its minor units; null when a rate is missing.</param>
/// <param name="RateDate">The date of the exchange rate used; null when no conversion was needed or possible.</param>
/// <param name="SecuredOnProperty">Left out of liquid net worth, like the property it is secured on.</param>
public sealed record NetWorthDebt(
    int LoanId,
    string Name,
    string CurrencyCode,
    decimal Amount,
    DateOnly BalanceDate,
    decimal? Converted,
    DateOnly? RateDate,
    bool SecuredOnProperty);

/// <summary>
/// Net worth on a date in one currency. Without a rate for some account's currency that account can't be
/// converted: it is left out of the totals and its currency listed in <see cref="MissingRates"/>, so the
/// totals are incomplete and the UI must say so. A missing rate is never treated as 1.0.
/// </summary>
/// <param name="Date"></param>
/// <param name="Currency"></param>
/// <param name="Total">The converted account lines minus the converted debts.</param>
/// <param name="Liquid">The liquid lines minus the debts not secured on property: property and pensions are left out.</param>
/// <param name="MissingRates"></param>
/// <param name="Lines"></param>
/// <param name="Debts">The loans counted as debts.</param>
/// <param name="LoansWithoutBalance">Loans paid out by the date with no recorded balance yet. They are not counted, so the totals are too high by what they owe.</param>
public sealed record NetWorthResult(
    DateOnly Date,
    string Currency,
    decimal Total,
    decimal Liquid,
    IReadOnlyList<string> MissingRates,
    IReadOnlyList<NetWorthLine> Lines,
    IReadOnlyList<NetWorthDebt> Debts,
    IReadOnlyList<string> LoansWithoutBalance);

/// <summary>Net worth at the end of a month (or today), for the history.</summary>
// RateDate: the date of the oldest exchange rate behind this point; null when nothing was converted.
public sealed record NetWorthPoint(DateOnly Date, decimal Total, decimal Liquid, IReadOnlyList<string> MissingRates, DateOnly? RateDate);

/// <summary>
/// Net worth from account balances (#37). The balance of an account on a date is its latest recorded one
/// on or before that date; an account with none yet isn't counted, and an archived account counts only
/// before the day it was archived. Each balance is converted to the target currency with the day's
/// rates and rounded to that currency's minor units, and the totals add up those rounded lines, so what
/// is shown always sums. Loans (#135) count as debts at their latest recorded balance.
/// </summary>
public static class NetWorthCalculator
{
    private const int _maxHistoryMonths = 600;

    /// <summary>Property and pensions can't be spent at will; everything else counts as liquid.</summary>
    public static bool IsLiquid(AccountType type) => type is not (AccountType.Property or AccountType.Pension);

    public static NetWorthResult On(
        DateOnly date,
        Currency target,
        IReadOnlyList<Account> accounts,
        IReadOnlyList<AccountValue> values,
        IReadOnlyDictionary<string, int> minorUnits,
        CurrencyConverter converter,
        IReadOnlyList<LoanDebtInput>? loans = null)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(minorUnits);
        ArgumentNullException.ThrowIfNull(converter);

        var latest = values
            .Where(value => value.Date <= date)
            .GroupBy(value => value.AccountId)
            .ToDictionary(group => group.Key, group => group.MaxBy(value => value.Date)!);

        var lines = new List<NetWorthLine>();
        var missing = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var account in accounts.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            if ((account.ArchivedOn is { } archived && archived <= date) || !latest.TryGetValue(account.Id, out var balance))
            {
                continue;
            }

            var own = new Currency(account.CurrencyCode, minorUnits[account.CurrencyCode]);
            var conversion = converter.Convert(new Money(balance.Amount, own), target, date);
            switch (conversion)
            {
                case Conversion.Converted converted:
                    lines.Add(new NetWorthLine(
                        account.Id, account.Name, account.Type, account.CurrencyCode, balance.Amount, balance.Date,
                        converted.Amount.Round().Amount, converted.RateDate, IsLiquid(account.Type)));
                    break;
                case Conversion.RateMissing rateMissing:
                    foreach (var code in rateMissing.CurrencyCodes)
                    {
                        missing.Add(code);
                    }

                    lines.Add(new NetWorthLine(
                        account.Id, account.Name, account.Type, account.CurrencyCode, balance.Amount, balance.Date,
                        Converted: null, RateDate: null, IsLiquid(account.Type)));
                    break;
            }
        }

        var debts = new List<NetWorthDebt>();
        var uncounted = new List<string>();
        foreach (var loan in (loans ?? []).Where(l => l.PaidOut <= date).OrderBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var balance = loan.Balances.Where(b => b.Date <= date).MaxBy(b => b.Date);
            if (balance is null)
            {
                uncounted.Add(loan.Name);
                continue;
            }

            var own = new Currency(loan.CurrencyCode, minorUnits[loan.CurrencyCode]);
            switch (converter.Convert(new Money(balance.Amount, own), target, date))
            {
                case Conversion.Converted converted:
                    debts.Add(new NetWorthDebt(loan.LoanId, loan.Name, loan.CurrencyCode, balance.Amount, balance.Date, converted.Amount.Round().Amount, converted.RateDate, loan.SecuredOnProperty));
                    break;
                case Conversion.RateMissing rateMissing:
                    foreach (var code in rateMissing.CurrencyCodes)
                    {
                        missing.Add(code);
                    }

                    debts.Add(new NetWorthDebt(loan.LoanId, loan.Name, loan.CurrencyCode, balance.Amount, balance.Date, Converted: null, RateDate: null, loan.SecuredOnProperty));
                    break;
            }
        }

        return new NetWorthResult(
            date,
            target.Code,
            lines.Sum(line => line.Converted ?? 0m) - debts.Sum(debt => debt.Converted ?? 0m),
            lines.Where(line => line.Liquid).Sum(line => line.Converted ?? 0m) - debts.Where(debt => !debt.SecuredOnProperty).Sum(debt => debt.Converted ?? 0m),
            [.. missing],
            lines,
            debts,
            uncounted);
    }

    /// <summary>
    /// Net worth at the end of each month from the first balance to <paramref name="to"/>; the last point
    /// is <paramref name="to"/> itself, even mid-month.
    /// </summary>
    public static IReadOnlyList<NetWorthPoint> Monthly(
        DateOnly to,
        Currency target,
        IReadOnlyList<Account> accounts,
        IReadOnlyList<AccountValue> values,
        IReadOnlyDictionary<string, int> minorUnits,
        CurrencyConverter converter,
        IReadOnlyList<LoanDebtInput>? loans = null)
    {
        ArgumentNullException.ThrowIfNull(values);
        var firstDates = values.Select(value => value.Date)
            .Concat((loans ?? []).SelectMany(loan => loan.Balances.Select(balance => balance.Date)))
            .ToArray();
        if (firstDates.Length == 0)
        {
            return [];
        }

        var first = firstDates.Min();
        var points = new List<NetWorthPoint>();
        for (var month = new DateOnly(first.Year, first.Month, 1); month <= to && points.Count < _maxHistoryMonths; month = month.AddMonths(1))
        {
            var end = month.AddMonths(1).AddDays(-1);
            var day = end < to ? end : to;
            var result = On(day, target, accounts, values, minorUnits, converter, loans);
            points.Add(new NetWorthPoint(day, result.Total, result.Liquid, result.MissingRates, result.Lines.Select(l => l.RateDate).Concat(result.Debts.Select(d => d.RateDate)).Min()));
        }

        return points;
    }
}
