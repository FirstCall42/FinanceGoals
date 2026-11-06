namespace FinanceGoals.Application.Accounts;

/// <summary>What the household expects of an account, for the balance forecast. Both parts are optional guesses.</summary>
public sealed record AccountAssumption(int AccountId, decimal? ExpectedYearlyReturn, decimal? MonthlyContribution);

/// <summary>The household's expectations per account (#119). Every method is scoped to one profile.</summary>
public interface IAccountAssumptionStore
{
    Task<IReadOnlyList<AccountAssumption>> ListAsync(int profileId, CancellationToken cancellationToken);

    /// <summary>Saves the expectations of an account of the profile; false when the profile has no such account.</summary>
    Task<bool> SaveAsync(int profileId, AccountAssumption assumption, int byUserId, CancellationToken cancellationToken);

    /// <summary>Removes them; false when there were none (or no such account in the profile).</summary>
    Task<bool> DeleteAsync(int profileId, int accountId, CancellationToken cancellationToken);
}
