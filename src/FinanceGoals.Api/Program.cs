using System.Security.Claims;
using FinanceGoals.Api;
using FinanceGoals.Application.Currencies;
using FinanceGoals.Application.Loans;
using FinanceGoals.Application.Profiles;
using FinanceGoals.Application.Users;
using FinanceGoals.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.HttpResults;
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

// Google sign-in; every endpoint needs a signed-in user unless it says AllowAnonymous.
builder.Services.AddGoogleSignIn(builder.Configuration);

var app = builder.Build();

// Explicit, where the framework would add it, so the order below is the order requests take.
app.UseRouting();

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

// Explicit, so they run here: after CORS (a refused request still gets its CORS headers).
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    // The build also writes the document to openapi/ (see the csproj); at run time it is only
    // served while developing.
    app.MapOpenApi().AllowAnonymous();
}

// Liveness: the process answers. Runs no checks, so a database outage never restarts the API.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();

// Readiness: the API can serve requests, which needs the database.
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains(InfrastructureServices.ReadyTag),
}).AllowAnonymous();

app.MapGet("/api/currencies", async (ICurrencyCatalog catalog, CancellationToken cancellationToken) =>
        TypedResults.Ok(await catalog.ListAsync(cancellationToken)))
    .WithName("ListCurrencies")
    .WithSummary("The currencies the app supports, ordered by code.")
    .AllowAnonymous();

app.MapPost("/api/loans/calculate", Results<Ok<LoanCalculationResult>, ValidationProblem> (LoanCalculationRequest request) =>
        LoanCalculator.Calculate(request, out var error) is { } result
            ? TypedResults.Ok(result)
            : TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["loan"] = [error ?? "The loan is not valid."] }))
    .WithName("CalculateLoan")
    .WithSummary("The payment schedule of a variable-rate annuity loan, without and with extra payments. Nothing is stored.")
    .AllowAnonymous();

app.MapGet("/api/auth/config", (IConfiguration configuration) =>
        TypedResults.Ok(new SignInConfig(configuration[GoogleSignIn.ClientIdSetting] is { Length: > 0 } id ? id : null)))
    .WithName("GetSignInConfig")
    .WithSummary("The Google client ID the web app signs in with; null when sign-in is not set up.")
    .AllowAnonymous();

app.MapPost("/api/me", async (ClaimsPrincipal principal, IAppUserStore users, IProfileStore profiles, CancellationToken cancellationToken) =>
    {
        var user = await users.SignInAsync(
            principal.FindFirstValue("sub") ?? throw new InvalidOperationException("The token has no subject."),
            principal.FindFirstValue("email") ?? throw new InvalidOperationException("The token has no email."),
            principal.FindFirstValue("name"),
            cancellationToken);
        return TypedResults.Ok(new SignedIn(user, await profiles.EnsureForUserAsync(user.Id, cancellationToken)));
    })
    .WithName("SignIn")
    .WithSummary("Records the sign-in of the Google account in the bearer token; returns the user and their profiles, creating one on the first sign-in.");

app.MapProfiles();

app.Run();

/// <summary>What the web app needs to show the Google sign-in button.</summary>
internal sealed record SignInConfig(string? GoogleClientId);

/// <summary>The signed-in user and the profiles they belong to.</summary>
internal sealed record SignedIn(User User, IReadOnlyList<ProfileSummary> Profiles);
