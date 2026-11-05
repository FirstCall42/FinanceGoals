using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceGoals.Api.Tests;

/// <summary>
/// The API in memory, with the given environment, app connection string and extra settings. Its logs go to
/// <see cref="Log"/> instead of standard output.
/// </summary>
internal sealed class ApiFactory : WebApplicationFactory<Program>
{
    /// <summary>Nothing listens on port 1, so connecting fails at once.</summary>
    public const string UnreachableDatabase = "Server=127.0.0.1,1;Database=none;User ID=none;Password=none;Connect Timeout=2;Encrypt=False";

    private readonly string _environment;
    private readonly string? _connectionString;
    private readonly IReadOnlyDictionary<string, string> _settings;

    public ApiFactory(string? connectionString, string environment = "Production", IReadOnlyDictionary<string, string>? settings = null)
    {
        _connectionString = connectionString;
        _environment = environment;
        _settings = settings ?? new Dictionary<string, string>();
    }

    /// <summary>Everything the API logged. The sink serializes writes to it.</summary>
    public StringWriter Log { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);
        builder.UseSetting("ConnectionStrings:App", _connectionString ?? string.Empty);
        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services => services.AddSingleton(new ApiLogging.Output(Log)));
    }
}
