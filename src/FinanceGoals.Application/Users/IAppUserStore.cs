namespace FinanceGoals.Application.Users;

/// <summary>A person who has signed in, as the app knows them.</summary>
public sealed record User(int Id, string Email, string? Name);

/// <summary>The people who have signed in with Google.</summary>
public interface IAppUserStore
{
    /// <summary>
    /// Records a sign-in: creates the user for a Google account the first time, and refreshes the
    /// email and name after that.
    /// </summary>
    Task<User> SignInAsync(string googleSubject, string email, string? name, CancellationToken cancellationToken);
}
