namespace FinanceGoals.Application.Accounts;

/// <summary>An account's balance on a date, in the account's currency.</summary>
public sealed record AccountValue(int AccountId, DateOnly Date, decimal Amount);

/// <summary>What happened to a batch of balances.</summary>
public enum ValueOutcome
{
    Saved,

    /// <summary>A balance is for an account that isn't in the household. Nothing was saved.</summary>
    UnknownAccount,
}

/// <summary>Account balances on dates (#36). Every method is scoped to one profile.</summary>
public interface IAccountValueStore
{
    /// <summary>The household's balances, newest first; one account's if <paramref name="accountId"/> is given.</summary>
    Task<IReadOnlyList<AccountValue>> ListAsync(int profileId, int? accountId, CancellationToken cancellationToken);

    /// <summary>Records balances on one date (a snapshot); a balance already recorded for that account and date is replaced. All or nothing.</summary>
    Task<ValueOutcome> SaveAsync(int profileId, DateOnly day, IReadOnlyList<(int AccountId, decimal Amount)> values, int byUserId, CancellationToken cancellationToken);

    /// <summary>Deletes an account's balance on a date; false if there is none.</summary>
    Task<bool> DeleteAsync(int profileId, int accountId, DateOnly day, CancellationToken cancellationToken);

    /// <summary>Deletes every balance recorded on a date (a whole snapshot); how many were deleted.</summary>
    Task<int> DeleteSnapshotAsync(int profileId, DateOnly day, CancellationToken cancellationToken);
}
