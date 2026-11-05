using FinanceGoals.Infrastructure.Health;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceGoals.Infrastructure;

public static class InfrastructureServices
{
    /// <summary>The API's connection string: ConnectionStrings:App, signed in as the app user.</summary>
    public const string ConnectionStringName = "App";

    /// <summary>Health checks with this tag decide readiness. Liveness runs no checks at all.</summary>
    public const string ReadyTag = "ready";

    /// <summary>Adds the database readiness check. It gives up after five seconds.</summary>
    public static IHealthChecksBuilder AddDatabaseHealthCheck(this IHealthChecksBuilder builder) =>
        builder.AddCheck<DatabaseHealthCheck>("database", tags: [ReadyTag], timeout: TimeSpan.FromSeconds(5));
}
