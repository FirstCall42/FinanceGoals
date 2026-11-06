using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

namespace FinanceGoals.Api;

/// <summary>
/// Sign-in with Google (#24). The web app gets an ID token from Google Identity Services and sends it
/// as a bearer token; the API checks Google's signature, the issuer, the audience (our client ID) and
/// the expiry. There is no client secret: an ID token needs none. Every endpoint requires a signed-in
/// user unless it is marked AllowAnonymous.
/// </summary>
internal static class GoogleSignIn
{
    /// <summary>The OAuth client ID (Auth__Google__ClientId, set by infra/). Not a secret: the web app shows it.</summary>
    public const string ClientIdSetting = "Auth:Google:ClientId";

    public const string Authority = "https://accounts.google.com";

    public static IServiceCollection AddGoogleSignIn(this IServiceCollection services, IConfiguration configuration)
    {
        var clientId = configuration[ClientIdSetting];
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = Authority;
                options.MapInboundClaims = false;
                options.TokenValidationParameters.ValidIssuers = [Authority, "accounts.google.com"];
                options.TokenValidationParameters.ValidAudience = clientId;
                options.TokenValidationParameters.ValidateAudience = true;
                if (string.IsNullOrWhiteSpace(clientId))
                {
                    // Sign-in is not set up: ignore every token, so requests are simply unauthenticated.
                    options.Events = new JwtBearerEvents { OnMessageReceived = context => { context.NoResult(); return Task.CompletedTask; } };
                }
                options.TokenValidationParameters.NameClaimType = "email";
            });
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder()
                // A request that matched no endpoint passes, so an unknown path stays a 404.
                .RequireAssertion(context => context.Resource is HttpContext { } http && http.GetEndpoint() is null
                    || (context.User.Identity?.IsAuthenticated == true && context.User.HasClaim("email_verified", "true")))
                .Build());
        return services;
    }
}
