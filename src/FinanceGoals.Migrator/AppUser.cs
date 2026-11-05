using System.Text.RegularExpressions;

namespace FinanceGoals.Migrator;

/// <summary>
/// The database user the API connects as. It gets only the app_data role: read and write
/// data in the app schema, no schema changes.
/// </summary>
internal sealed partial class AppUser
{
    private AppUser(string name, string? password)
    {
        Name = name;
        Password = password;
    }

    public string Name { get; }

    /// <summary>
    /// Set for a contained user with a password (local SQL Server, Azure SQL). Null for an
    /// Entra ID user, such as the API's managed identity in Azure.
    /// </summary>
    public string? Password { get; }

    public static AppUser WithPassword(string name, string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        if (password.Length > 128)
        {
            throw new ArgumentException("The password must be 128 characters or fewer.", nameof(password));
        }

        return new AppUser(ValidName(name), password);
    }

    public static AppUser FromExternalProvider(string name) => new(ValidName(name), password: null);

    /// <summary>
    /// Reads the migrator settings. The kind is explicit so that a missing or misspelt password
    /// fails instead of silently granting app_data to an Entra principal with that name.
    /// </summary>
    public static AppUser? FromSettings(string? name, string? kind, string? password)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return kind?.Trim().ToUpperInvariant() switch
        {
            "PASSWORD" when !string.IsNullOrEmpty(password) => WithPassword(name, password),
            "PASSWORD" => throw new ArgumentException("Migrator__AppUserPassword is required for AppUserKind 'password'."),
            "ENTRA" when string.IsNullOrEmpty(password) => FromExternalProvider(name),
            "ENTRA" => throw new ArgumentException("An Entra ID app user has no password; unset Migrator__AppUserPassword."),
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
