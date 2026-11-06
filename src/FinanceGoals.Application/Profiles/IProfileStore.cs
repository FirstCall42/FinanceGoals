using System.Text.Json.Serialization;

namespace FinanceGoals.Application.Profiles;

/// <summary>What a user may do in a profile. Owners also manage the profile and its members.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ProfileRole>))]
public enum ProfileRole
{
    Member,
    Owner,
}

/// <summary>A profile as one of its members sees it.</summary>
public sealed record ProfileSummary(int Id, string Name, ProfileRole Role);

/// <summary>A member of a profile.</summary>
public sealed record ProfileMember(int UserId, string Email, string? Name, ProfileRole Role);

/// <summary>A signed-in user's access to one profile.</summary>
public sealed record ProfileAccess(int ProfileId, int UserId, ProfileRole Role);

/// <summary>Household profiles (#25): a profile owns all data, and users belong to it as Owner or Member.</summary>
public interface IProfileStore
{
    /// <summary>The profile name a first sign-in gets.</summary>
    public const string DefaultName = "Household";

    /// <summary>
    /// The user's profiles. A user without one (a first sign-in) gets a new profile, as its Owner.
    /// </summary>
    Task<IReadOnlyList<ProfileSummary>> EnsureForUserAsync(int userId, CancellationToken cancellationToken);

    /// <summary>The access of the Google account to a profile, or null if it is not a member.</summary>
    Task<ProfileAccess?> AccessAsync(string googleSubject, int profileId, CancellationToken cancellationToken);

    /// <summary>The profile's name, or null if it doesn't exist.</summary>
    Task<string?> NameAsync(int profileId, CancellationToken cancellationToken);

    Task RenameAsync(int profileId, string name, int byUserId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ProfileMember>> MembersAsync(int profileId, CancellationToken cancellationToken);
}
