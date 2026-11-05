using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FinanceGoals.Infrastructure.Health;

/// <summary>
/// Ready when the API can open a connection with its own connection string and run a query.
/// The connection string is read on every check, so the API starts even before it is set.
/// </summary>
internal sealed class DatabaseHealthCheck : IHealthCheck
{
    private readonly IConfiguration _configuration;

    public DatabaseHealthCheck(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var connectionString = _configuration.GetConnectionString(InfrastructureServices.ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return HealthCheckResult.Unhealthy($"ConnectionStrings:{InfrastructureServices.ConnectionStringName} is not set.");
        }

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new SqlCommand("SELECT 1;", connection);
            await command.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException or ArgumentException)
        {
            // ArgumentException: a malformed connection string. The health endpoint only reports
            // the status; the exception goes to the log.
            return HealthCheckResult.Unhealthy("The database is not reachable.", ex);
        }
    }
}
