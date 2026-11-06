using FinanceGoals.Application.Accounts;
using FinanceGoals.Application.Currencies;
using FinanceGoals.Application.Profiles;
using FinanceGoals.Application.Users;
using FinanceGoals.Domain.Currencies;
using FinanceGoals.Infrastructure.Accounts;
using FinanceGoals.Infrastructure.Currencies;
using FinanceGoals.Infrastructure.Health;
using FinanceGoals.Infrastructure.Profiles;
using FinanceGoals.Infrastructure.Users;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceGoals.Infrastructure;

public static class InfrastructureServices
{
    /// <summary>The API's connection string: ConnectionStrings:App, signed in as the app user.</summary>
    public const string ConnectionStringName = "App";

    /// <summary>Health checks with this tag decide readiness. Liveness runs no checks at all.</summary>
    public const string ReadyTag = "ready";

    /// <summary>The ECB feed's address; tests point it at a stub.</summary>
    public const string EcbUrlSetting = "ExchangeRates:EcbUrl";

    /// <summary>Adds the implementations of the Application interfaces.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<ICurrencyCatalog, SqlCurrencyCatalog>();
        services.AddSingleton<IAppUserStore, SqlAppUserStore>();
        services.AddSingleton<IProfileStore, SqlProfileStore>();
        services.AddSingleton<IProfileInviteStore, SqlProfileInviteStore>();
        services.AddSingleton<IAccountStore, SqlAccountStore>();
        services.AddSingleton<SqlExchangeRateStore>();
        services.AddSingleton<IExchangeRateStore>(provider => provider.GetRequiredService<SqlExchangeRateStore>());
        services.AddSingleton<IEurRates>(provider => provider.GetRequiredService<SqlExchangeRateStore>());
        services.AddHttpClient<IExchangeRateSource, EcbExchangeRateSource>((provider, client) =>
        {
            client.BaseAddress = new Uri(provider.GetRequiredService<IConfiguration>()[EcbUrlSetting] ?? EcbExchangeRateSource.DefaultUrl);
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddTransient<ExchangeRateImport>();
        return services;
    }

    /// <summary>Adds the database readiness check. It gives up after five seconds.</summary>
    public static IHealthChecksBuilder AddDatabaseHealthCheck(this IHealthChecksBuilder builder) =>
        builder.AddCheck<DatabaseHealthCheck>("database", tags: [ReadyTag], timeout: TimeSpan.FromSeconds(5));
}
