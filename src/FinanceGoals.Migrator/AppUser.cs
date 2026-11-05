using System.Text.RegularExpressions;

namespace FinanceGoals.Migrator;

/// <summary>
/// The database user the API connects as. It gets only the app_data role: read and write
/// data in the app schema, no schema changes.
/// </summary>
internal sealed partial class AppUser
{
    private AppUser(string name, string? password, Guid? clientId)
    {
        Name = name;
        Password = password;
        ClientId = clientId;
    }

    public string Name { get; }

    /// <summary>Set for a contained user with a password (local SQL Server, tests). Null otherwise.</summary>
    public string? Password { get; }

    /// <summary>
    /// Set for an Entra ID user, such as the API's managed identity in Azure: its client
    /// (application) ID, which Azure SQL uses as the user's SID. Creating the user from the ID
    /// means the server never needs permission to look up names in Entra. Null otherwise.
    /// </summary>
    public Guid? ClientId { get; }

    public static AppUser WithPassword(string name, string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        if (password.Length > 128)
        {
            throw new ArgumentException("The password must be 128 characters or fewer.", nameof(password));
        }

        return new AppUser(ValidName(name), password, clientId: null);
    }

    public static AppUser FromEntra(string name, Guid clientId)
    {
        if (clientId == Guid.Empty)
        {
            throw new ArgumentException("The client ID must not be empty.", nameof(clientId));
        }

        return new AppUser(ValidName(name), password: null, clientId);
    }

    /// <summary>
    /// Reads the migrator settings. The kind is explicit so that a missing or misspelt password
    /// fails instead of silently creating an Entra user, and an Entra user needs its client ID.
    /// </summary>
    public static AppUser? FromSettings(string? name, string? kind, string? password, string? clientId)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return kind?.Trim().ToUpperInvariant() switch
        {
            "PASSWORD" when !string.IsNullOrEmpty(clientId) =>
                throw new ArgumentException("A password app user has no client ID; unset Migrator__AppUserClientId."),
            "PASSWORD" when !string.IsNullOrEmpty(password) => WithPassword(name, password),
            "PASSWORD" => throw new ArgumentException("Migrator__AppUserPassword is required for AppUserKind 'password'."),
            "ENTRA" when !string.IsNullOrEmpty(password) =>
                throw new ArgumentException("An Entra ID app user has no password; unset Migrator__AppUserPassword."),
            "ENTRA" when Guid.TryParse(clientId, out var id) => FromEntra(name, id),
            "ENTRA" => throw new ArgumentException("Migrator__AppUserClientId must be the app user's client ID (a GUID) for AppUserKind 'entra'."),
            _ => throw new ArgumentException("Migrator__AppUserKind must be 'password' or 'entra'."),
        };
    }

    private static string ValidName(string name)
    {
        // Entra names may contain '-' and '.'; nothing that needs more than QUOTENAME.
        if (!NamePattern().IsMatch(name))
        {
            throw new ArgumentException(
                $"'{name}' is not a valid app user name: start with a letter, then up to 127 letters, digits, '_', '-' or '.'.",
                nameof(name));
        }

        return name;
    }

    // \z, not $: $ also matches before a trailing newline.
    [GeneratedRegex(@"\A[A-Za-z][A-Za-z0-9_.\-]{0,127}\z")]
    private static partial Regex NamePattern();
}
