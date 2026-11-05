using FinanceGoals.Migrator;
using Microsoft.Extensions.Configuration;

// Configuration, from environment variables or --key=value arguments:
//   ConnectionStrings__SchemaOwner   connection string of the schema owner (required)
//   Migrator__AppUser                app user to create and add to app_data (optional)
//   Migrator__AppUserKind            "password" (contained user) or "entra" (Entra ID user);
//                                    required with Migrator__AppUser
//   Migrator__AppUserPassword        its password, for kind "password" only
//   Migrator__AppUserClientId        its client (application) ID, for kind "entra" only
var configuration = new ConfigurationBuilder()
    .AddEnvironmentVariables()
    .AddCommandLine(args)
    .Build();

var connectionString = configuration.GetConnectionString("SchemaOwner");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("ConnectionStrings__SchemaOwner is not set.");
    return 2;
}

try
{
    var appUser = AppUser.FromSettings(
        configuration["Migrator:AppUser"],
        configuration["Migrator:AppUserKind"],
        configuration["Migrator:AppUserPassword"],
        configuration["Migrator:AppUserClientId"]);
    if (appUser is null)
    {
        var orphaned = configuration["Migrator:AppUserKind"] is not null
            || configuration["Migrator:AppUserPassword"] is not null
            || configuration["Migrator:AppUserClientId"] is not null;
        Console.WriteLine(orphaned
            ? "WARNING: Migrator__AppUserKind, Migrator__AppUserPassword or Migrator__AppUserClientId is set but Migrator__AppUser is not; no app user step."
            : "No Migrator__AppUser set; skipping the app user step.");
    }

    var result = new DatabaseMigrator(connectionString, Console.Out).Run(appUser);
    Console.WriteLine(
        $"Done: {result.Migrations.Count} migration(s), {result.Repeatable.Count} repeatable, {result.Seeds.Count} seed(s).");
    return 0;
}
catch (Exception ex) when (ex is MigrationException or ArgumentException or Microsoft.Data.SqlClient.SqlException)
{
    Console.Error.WriteLine($"Migration failed: {ex.Message}");
    return 1;
}
