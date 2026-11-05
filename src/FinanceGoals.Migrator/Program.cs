using FinanceGoals.Migrator;
using Microsoft.Extensions.Configuration;

// Configuration, from environment variables or --key=value arguments:
//   ConnectionStrings__SchemaOwner   connection string of the schema owner (required)
//   Migrator__AppUser                app user to create and add to app_data (optional)
//   Migrator__AppUserKind            "password" (contained user) or "entra" (Entra ID user);
//                                    required with Migrator__AppUser
//   Migrator__AppUserPassword        its password, for kind "password" only
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
        configuration["Migrator:AppUserPassword"]);

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
