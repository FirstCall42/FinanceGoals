using FinanceGoals.Domain.Currencies;

namespace FinanceGoals.Domain.Loans;

/// <summary>A one-off extra repayment (ekstra innbetaling) on a date.</summary>
public sealed record ExtraPayment
{
    public ExtraPayment(DateOnly date, Money amount)
    {
        if (amount.Amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount.Amount, "An extra payment must be positive.");
        }

        if (amount.Round() != amount)
        {
            throw new ArgumentException("An extra payment is a whole number of minor units (øre, cents).", nameof(amount));
        }

        Date = date;
        Amount = amount;
    }

    public DateOnly Date { get; }

    public Money Amount { get; }
}

/// <summary>An extra repayment made with every regular payment from <see cref="From"/> to <see cref="Until"/>.</summary>
public sealed record MonthlyExtraPayment
{
    public MonthlyExtraPayment(Money amount, DateOnly from, DateOnly? until = null)
    {
        if (amount.Amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount.Amount, "An extra payment must be positive.");
        }

        if (amount.Round() != amount)
        {
            throw new ArgumentException("An extra payment is a whole number of minor units (øre, cents).", nameof(amount));
        }

        if (until < from)
        {
            throw new ArgumentException("The monthly extra payment ends before it starts.", nameof(until));
        }

        Amount = amount;
        From = from;
        Until = until;
    }

    public Money Amount { get; }

    public DateOnly From { get; }

    /// <summary>The last day a payment can fall on; none means to the end of the loan.</summary>
    public DateOnly? Until { get; }
}

/// <summary>
/// Extra repayments on a loan. They shorten the loan and leave the regular payment as it is (the
/// usual choice at Norwegian banks); a later rate change recalculates the payment over the
/// shortened term.
/// </summary>
public sealed class ExtraPayments
{
    public static readonly ExtraPayments None = new([], []);

    public ExtraPayments(IEnumerable<ExtraPayment> oneOff, IEnumerable<MonthlyExtraPayment> monthly)
    {
        ArgumentNullException.ThrowIfNull(oneOff);
        ArgumentNullException.ThrowIfNull(monthly);
        OneOff = Array.AsReadOnly(oneOff.OrderBy(extra => extra.Date).ToArray());
        Monthly = Array.AsReadOnly(monthly.ToArray());
    }

    public IReadOnlyList<ExtraPayment> OneOff { get; }

    public IReadOnlyList<MonthlyExtraPayment> Monthly { get; }

    /// <summary>Rejects extras the schedule would otherwise skip: another currency, or outside the loan.</summary>
    internal void CheckFits(Loan loan)
    {
        var currency = loan.Principal.Currency;
        foreach (var amount in OneOff.Select(extra => extra.Amount).Concat(Monthly.Select(extra => extra.Amount)))
        {
            if (amount.Currency != currency)
            {
                throw new CurrencyMismatchException(currency, amount.Currency);
            }
        }

        if (OneOff.Any(extra => extra.Date <= loan.Start || extra.Date > loan.LastPaymentDate))
        {
            throw new ArgumentException($"A one-off extra payment falls outside the loan: after {loan.Start:yyyy-MM-dd}, up to {loan.LastPaymentDate:yyyy-MM-dd}.");
        }

        if (Monthly.Any(extra => extra.From > loan.LastPaymentDate || extra.Until < loan.FirstPaymentDate))
        {
            throw new ArgumentException($"A monthly extra payment falls outside the loan's payments, {loan.FirstPaymentDate:yyyy-MM-dd} to {loan.LastPaymentDate:yyyy-MM-dd}.");
        }
    }

    /// <summary>
    /// The extra paid with the payment on <paramref name="paymentDate"/>: the one-off payments dated
    /// after <paramref name="previousPayment"/> up to and including that day, plus the monthly extras
    /// that run on it.
    /// </summary>
    internal Money On(DateOnly previousPayment, DateOnly paymentDate, Currency currency)
    {
        var total = Money.Zero(currency);
        foreach (var extra in OneOff.Where(extra => extra.Date > previousPayment && extra.Date <= paymentDate))
        {
            total += extra.Amount;
        }

        foreach (var extra in Monthly.Where(extra => extra.From <= paymentDate && (extra.Until is null || paymentDate <= extra.Until)))
        {
            total += extra.Amount;
        }

        return total;
    }
}
