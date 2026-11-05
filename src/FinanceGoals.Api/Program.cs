using FinanceGoals.Api;
using FinanceGoals.Application.Currencies;
using FinanceGoals.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSerilog((services, logger) => ApiLogging.Configure(
    logger,
    builder.Configuration,
    services.GetService<ApiLogging.Output>()?.Writer ?? Console.Out));
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks().AddDatabaseHealthCheck();
builder.Services.AddInfrastructure();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ExchangeRateImportTrigger>();
if (builder.Configuration.GetValue(ExchangeRateImportService.EnabledSetting, defaultValue: true))
{
    builder.Services.AddHostedService<ExchangeRateImportService>();
}

// Unhandled errors become a ProblemDetails 500; the exception is logged, never sent to the client.
builder.Services.AddProblemDetails();

// The web app is served from another origin (Static Web Apps), so the API allows exactly the
// origins in Cors:AllowedOrigins (Cors__AllowedOrigins__0 and so on, set by infra/). None by default.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(allowedOrigins)
    .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE")
    .WithHeaders("Content-Type", "Authorization")));

var app = builder.Build();

app.UseExceptionHandler();
app.UseSerilogRequestLogging(options => options.GetLevel = (context, _, error) => ApiLogging.RequestLevel(context, error));
app.UseCors();

// Real API requests use the database anyway; only they may start an exchange-rate import.
app.Use(async (context, next) =>
{
    try
    {
        await next(context);
    }
    finally
    {
        // After routing, so only requests that matched an endpoint count.
        if (ExchangeRateImportTrigger.ShouldSignal(context))
        {
            context.RequestServices.GetRequiredService<ExchangeRateImportTrigger>().Signal();
        }
    }
});

if (app.Environment.IsDevelopment())
{
    // The build also writes the document to openapi/ (see the csproj); at run time it is only
    // served while developing.
    app.MapOpenApi();
}

// Liveness: the process answers. Runs no checks, so a database outage never restarts the API.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });

// Readiness: the API can serve requests, which needs the database.
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains(InfrastructureServices.ReadyTag),
});

app.MapGet("/api/currencies", async (ICurrencyCatalog catalog, CancellationToken cancellationToken) =>
        TypedResults.Ok(await catalog.ListAsync(cancellationToken)))
    .WithName("ListCurrencies")
    .WithSummary("The currencies the app supports, ordered by code.");

app.Run();
