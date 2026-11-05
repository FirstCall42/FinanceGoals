using FinanceGoals.Api;
using FinanceGoals.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSerilog((_, logger) => ApiLogging.Configure(logger, builder.Configuration, Console.Out));
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks().AddDatabaseHealthCheck();

var app = builder.Build();

app.UseSerilogRequestLogging(options => options.GetLevel = (context, _, error) => ApiLogging.RequestLevel(context, error));

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

app.Run();
