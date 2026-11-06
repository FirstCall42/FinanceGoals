using System.Buffers.Text;
using System.Security.Cryptography;

namespace FinanceGoals.Application.Profiles;

/// <summary>An invite that hasn't been accepted yet.</summary>
public sealed record PendingInvite(int Id, string Email, DateTime ExpiresAt);

/// <summary>A new invite; <see cref="Token"/> goes in the link and is never stored or shown again.</summary>
public sealed record CreatedInvite(int Id, string Email, DateTime ExpiresAt, string Token);

/// <summary>What happened when someone used an invite link.</summary>
public enum InviteOutcome
{
    /// <summary>Unknown, used or expired: the link doesn't work (no hint which).</summary>
    NotFound,

    /// <summary>The person is now a member of the profile.</summary>
    Accepted,

    /// <summary>The link is for another email address than the signed-in Google account's.</summary>
    WrongAccount,
}

/// <summary>Invite tokens: 32 random bytes in the link, and only their SHA-256 hash in the database.</summary>
public static class InviteTokens
{
    public static TimeSpan Lifetime { get; } = TimeSpan.FromDays(7);

    public static (string Token, byte[] Hash) New()
    {
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        return (token, Hash(token));
    }

    public static byte[] Hash(string token) => SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));
}

/// <summary>Invitations to a household profile (#26): single use, for one email address, valid for 7 days.</summary>
public interface IProfileInviteStore
{
    Task<CreatedInvite> CreateAsync(int profileId, string email, int byUserId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PendingInvite>> PendingAsync(int profileId, CancellationToken cancellationToken);

    /// <summary>Withdraws a pending invite; false if the profile has no such pending invite.</summary>
    Task<bool> RevokeAsync(int profileId, int inviteId, CancellationToken cancellationToken);

    /// <summary>
    /// Uses an invite link: if it is pending, unexpired and for the signed-in account's email, the
    /// account becomes a Member of the profile and the invite is used up.
    /// </summary>
    Task<(InviteOutcome Outcome, int? ProfileId)> AcceptAsync(string token, string googleSubject, string email, CancellationToken cancellationToken);
}
