using System.Text.Json.Serialization;

namespace FinanceGoals.Application.Accounts;

/// <summary>What kind of account it is. No individual holdings are tracked, only the account's value.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AccountType>))]
public enum AccountType
{
    Brokerage,
    Savings,
    Deposit,
    Current,
    Pension,
    Property,
}

/// <summary>An account of a household, in one currency.</summary>
public sealed record Account(int Id, string Name, AccountType Type, string CurrencyCode, bool Archived);

/// <summary>Changes to an account; a null one stays as it is.</summary>
public sealed record AccountChanges(string? Name, AccountType? Type, string? CurrencyCode, bool? Archived);

/// <summary>What happened to a create or an update.</summary>
public enum AccountOutcome
{
    Saved,
    NotFound,

    /// <summary>Another account of the household already has the name.</summary>
    NameTaken,

    /// <summary>The account has balances in its currency, so the currency can no longer change.</summary>
    CurrencyLocked,
}

/// <summary>The household's accounts (#35). Every method is scoped to one profile.</summary>
public interface IAccountStore
{
    Task<IReadOnlyList<Account>> ListAsync(int profileId, bool includeArchived, CancellationToken cancellationToken);

    Task<(AccountOutcome Outcome, Account? Account)> CreateAsync(
        int profileId, string name, AccountType type, string currencyCode, int byUserId, CancellationToken cancellationToken);

    /// <summary>Changes an account of the profile; an account of another profile is NotFound.</summary>
    Task<(AccountOutcome Outcome, Account? Account)> UpdateAsync(
        int profileId, int accountId, AccountChanges changes, int byUserId, CancellationToken cancellationToken);
}
