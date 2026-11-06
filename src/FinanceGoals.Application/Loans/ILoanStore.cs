using FinanceGoals.Domain.Loans;

namespace FinanceGoals.Application.Loans;

/// <summary>A saved loan with its id.</summary>
public sealed record StoredLoan(int Id, Loan Loan);

/// <summary>What happened to a save.</summary>
public enum LoanOutcome
{
    Saved,
    NotFound,

    /// <summary>Another loan of the household already has the name.</summary>
    NameTaken,

    /// <summary>The loan already has a rate change on that day (a concurrent save won).</summary>
    RateDateTaken,
}

/// <summary>The household's loans (#107). Every method is scoped to one profile.</summary>
public interface ILoanStore
{
    Task<IReadOnlyList<StoredLoan>> ListAsync(int profileId, CancellationToken cancellationToken);

    /// <summary>A loan of the profile; a loan of another profile is null.</summary>
    Task<StoredLoan?> GetAsync(int profileId, int loanId, CancellationToken cancellationToken);

    Task<(LoanOutcome Outcome, StoredLoan? Loan)> CreateAsync(int profileId, Loan loan, int byUserId, CancellationToken cancellationToken);

    /// <summary>Stores a new rate change in the period it falls in; the caller has checked it with <see cref="Loan.WithRateChange"/>.</summary>
    Task<LoanOutcome> AddRateChangeAsync(int profileId, int loanId, DateOnly periodStart, RateChange change, int byUserId, CancellationToken cancellationToken);
}
