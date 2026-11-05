using FinanceGoals.Migrator;
using Microsoft.Extensions.Configuration;

// Configuration, from environment variables or --key=value arguments:
//   ConnectionStrings__SchemaOwner   connection string of the schema owner (required)
//   Migrator__AppUser                app user to create and add to app_data (optional)
//   Migrator__AppUserPassword        its password; leave unset for an Entra ID user
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

var appUserName = configuration["Migrator:AppUser"];
var appUserPassword = configuration["Migrator:AppUserPassword"];

try
{
    AppUser? appUser = string.IsNullOrWhiteSpace(appUserName) ? null
        : string.IsNullOrEmpty(appUserPassword) ? AppUser.FromExternalProvider(appUserName)
        : AppUser.WithPassword(appUserName, appUserPassword);

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
