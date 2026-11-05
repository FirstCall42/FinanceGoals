using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FinanceGoals.Api.Tests;

/// <summary>The API in memory, with the given environment and app connection string.</summary>
internal sealed class ApiFactory : WebApplicationFactory<Program>
{
    /// <summary>Nothing listens on port 1, so connecting fails at once.</summary>
    public const string UnreachableDatabase = "Server=127.0.0.1,1;Database=none;User ID=none;Password=none;Connect Timeout=2;Encrypt=False";

    private readonly string _environment;
    private readonly string? _connectionString;

    public ApiFactory(string? connectionString, string environment = "Production")
    {
        _connectionString = connectionString;
        _environment = environment;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);
        builder.UseSetting("ConnectionStrings:App", _connectionString ?? string.Empty);
    }
}
