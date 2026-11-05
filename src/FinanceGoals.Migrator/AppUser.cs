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

    private static string ValidName(string name)
    {
        // Entra names may contain '-' and '.'; nothing that needs more than QUOTENAME.
        if (!NamePattern().IsMatch(name))
        {
            throw new ArgumentException(
                $"'{name}' is not a valid app user name: use 1-128 letters, digits, '_', '-' or '.'.", nameof(name));
        }

        return name;
    }

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_.\-]{0,127}$")]
    private static partial Regex NamePattern();
}
