namespace FinanceGoals.Application.Loans;

/// <summary>A one-off extra payment of a loan, in the loan's currency.</summary>
public sealed record StoredExtra(DateOnly Date, decimal Amount);

/// <summary>An extra payment made with every regular payment from a day to an optional last day.</summary>
public sealed record StoredMonthlyExtra(int Id, decimal Amount, DateOnly From, DateOnly? Until);

/// <summary>The balance the bank reported on a day, in the loan's currency.</summary>
public sealed record StoredBalance(DateOnly Date, decimal Amount);

/// <summary>A loan's extra payments and recorded balances.</summary>
public sealed record LoanExtras(IReadOnlyList<StoredExtra> OneOff, IReadOnlyList<StoredMonthlyExtra> Monthly, IReadOnlyList<StoredBalance> Balances);

/// <summary>What happened to a save of an extra payment or balance.</summary>
public enum LoanExtraOutcome
{
    Saved,

    /// <summary>The loan, or the extra or balance asked for, isn't there (or belongs to another household).</summary>
    NotFound,

    /// <summary>A one-off extra payment already exists on that day.</summary>
    Duplicate,
}

/// <summary>A loan's extra payments and actual balances (#137). Every method is scoped to one profile and checks the loan belongs to it.</summary>
public interface ILoanExtraStore
{
    /// <summary>The loan's extras and balances, oldest first; null if the profile has no such loan.</summary>
    Task<LoanExtras?> GetAsync(int profileId, int loanId, CancellationToken cancellationToken);

    Task<LoanExtraOutcome> AddOneOffAsync(int profileId, int loanId, DateOnly day, decimal amount, int byUserId, CancellationToken cancellationToken);

    Task<LoanExtraOutcome> DeleteOneOffAsync(int profileId, int loanId, DateOnly day, CancellationToken cancellationToken);

    Task<(LoanExtraOutcome Outcome, int Id)> AddMonthlyAsync(int profileId, int loanId, decimal amount, DateOnly from, DateOnly? until, int byUserId, CancellationToken cancellationToken);

    Task<LoanExtraOutcome> DeleteMonthlyAsync(int profileId, int loanId, int id, CancellationToken cancellationToken);

    /// <summary>Records the balance on a day, replacing one already recorded for that day.</summary>
    Task<LoanExtraOutcome> SaveBalanceAsync(int profileId, int loanId, DateOnly day, decimal amount, int byUserId, CancellationToken cancellationToken);

    Task<LoanExtraOutcome> DeleteBalanceAsync(int profileId, int loanId, DateOnly day, CancellationToken cancellationToken);
}
