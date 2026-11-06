using System.Security.Claims;
using FinanceGoals.Application.Accounts;
using FinanceGoals.Application.Currencies;
using FinanceGoals.Application.Profiles;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace FinanceGoals.Api;

/// <summary>An endpoint that only a profile's Owners may use; the rest need any member.</summary>
internal sealed class OwnerOnly;

/// <summary>On every endpoint of the profile group, so tests can tell it passes the membership filter.</summary>
internal sealed class ProfileScoped;

/// <summary>
/// Everything under /api/profiles/{profileId} (#25). The group checks the caller's role first: someone
/// who isn't a member gets 404, as if the profile didn't exist (#27), and a Member calling an
/// Owner-only endpoint gets 403. Endpoints for a profile's data belong in this group.
/// </summary>
internal static class ProfileEndpoints
{
    private const string _accessKey = "ProfileAccess";

    /// <summary>The caller's access, checked by the group's filter.</summary>
    public static ProfileAccess Access(this HttpContext context) =>
        context.Items[_accessKey] as ProfileAccess ?? throw new InvalidOperationException("The profile filter did not run.");

    public static RouteGroupBuilder MapProfiles(this IEndpointRouteBuilder app)
    {
        var profile = app.MapGroup("/api/profiles/{profileId:int}");
        profile.WithMetadata(new ProfileScoped());
        profile.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            var profileId = Convert.ToInt32(http.Request.RouteValues["profileId"], System.Globalization.CultureInfo.InvariantCulture);
            var subject = http.User.FindFirstValue("sub");
            var access = subject is null
                ? null
                : await http.RequestServices.GetRequiredService<IProfileStore>().AccessAsync(subject, profileId, http.RequestAborted);
            if (access is null)
            {
                return TypedResults.NotFound();
            }

            if (access.Role != ProfileRole.Owner && http.GetEndpoint()?.Metadata.GetMetadata<OwnerOnly>() is not null)
            {
                return TypedResults.Forbid();
            }

            http.Items[_accessKey] = access;
            return await next(context);
        });

        profile.MapGet("", async Task<Results<Ok<ProfileSummary>, NotFound>> (HttpContext http, IProfileStore profiles, CancellationToken cancellationToken) =>
                await profiles.GetAsync(http.Access().ProfileId, http.Access().Role, cancellationToken) is { } summary
                    ? TypedResults.Ok(summary)
                    : TypedResults.NotFound())
            .WithName("GetProfile")
            .WithSummary("The profile, with the caller's role in it.");

        profile.MapPatch("", async Task<Results<NoContent, ValidationProblem>> (UpdateProfile request, HttpContext http, IProfileStore profiles, ICurrencyCatalog currencies, CancellationToken cancellationToken) =>
            {
                var errors = new Dictionary<string, string[]>();
                string? name = null;
                if (request.Name is not null)
                {
                    name = request.Name.Trim();
                    if (name.Length is 0 or > 100)
                    {
                        errors["name"] = ["The name must be 1 to 100 characters."];
                    }
                }

                if (request.MainCurrency is not null
                    && !(await currencies.ListAsync(cancellationToken)).Any(c => c.Code == request.MainCurrency))
                {
                    errors["mainCurrency"] = ["Pick one of the supported currencies."];
                }

                if (request.Name is null && request.MainCurrency is null)
                {
                    errors["profile"] = ["Send a name or a main currency to change."];
                }

                if (errors.Count > 0)
                {
                    return TypedResults.ValidationProblem(errors);
                }

                await profiles.UpdateAsync(http.Access().ProfileId, name, request.MainCurrency, http.Access().UserId, cancellationToken);
                return TypedResults.NoContent();
            })
            .WithMetadata(new OwnerOnly())
            .WithName("UpdateProfile")
            .WithSummary("Changes the profile's name and/or main currency. Owners only.");

        profile.MapGet("/members", async (HttpContext http, IProfileStore profiles, CancellationToken cancellationToken) =>
                TypedResults.Ok(await profiles.MembersAsync(http.Access().ProfileId, cancellationToken)))
            .WithName("ListProfileMembers")
            .WithSummary("The profile's members and their roles.");

        profile.MapPost("/invites", async Task<Results<Created<CreatedInvite>, ValidationProblem>> (InviteMember request, HttpContext http, IProfileInviteStore invites, CancellationToken cancellationToken) =>
            {
                var email = request.Email?.Trim() ?? string.Empty;
                if (email.Length is < 3 or > 320 || email.IndexOf('@', StringComparison.Ordinal) is < 1 || email.EndsWith('@'))
                {
                    return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["email"] = ["Enter the email address of their Google account."] });
                }

                var access = http.Access();
                var invite = await invites.CreateAsync(access.ProfileId, email, access.UserId, cancellationToken);
                return TypedResults.Created($"/api/profiles/{access.ProfileId}/invites/{invite.Id}", invite);
            })
            .WithMetadata(new OwnerOnly())
            .WithName("InviteMember")
            .WithSummary("Invites someone by the email of their Google account. Returns the single-use token for the link, once. Owners only.");

        profile.MapGet("/invites", async (HttpContext http, IProfileInviteStore invites, CancellationToken cancellationToken) =>
                TypedResults.Ok(await invites.PendingAsync(http.Access().ProfileId, cancellationToken)))
            .WithMetadata(new OwnerOnly())
            .WithName("ListInvites")
            .WithSummary("Invites that are not used or expired yet. Owners only.");

        profile.MapDelete("/invites/{inviteId:int}", async Task<Results<NoContent, NotFound>> (int inviteId, HttpContext http, IProfileInviteStore invites, CancellationToken cancellationToken) =>
                await invites.RevokeAsync(http.Access().ProfileId, inviteId, cancellationToken) ? TypedResults.NoContent() : TypedResults.NotFound())
            .WithMetadata(new OwnerOnly())
            .WithName("RevokeInvite")
            .WithSummary("Withdraws a pending invite. Owners only.");


        profile.MapGet("/accounts", async (bool? includeArchived, HttpContext http, IAccountStore accounts, CancellationToken cancellationToken) =>
                TypedResults.Ok(await accounts.ListAsync(http.Access().ProfileId, includeArchived ?? false, cancellationToken)))
            .WithName("ListAccounts")
            .WithSummary("The household's accounts, by name. Archived ones only with includeArchived=true.");

        profile.MapPost("/accounts", async Task<Results<Created<Account>, ValidationProblem, Conflict<ProblemDetails>>> (CreateAccount request, HttpContext http, IAccountStore accounts, ICurrencyCatalog currencies, CancellationToken cancellationToken) =>
            {
                var name = request.Name?.Trim() ?? string.Empty;
                var errors = new Dictionary<string, string[]>();
                if (name.Length is 0 or > 100)
                {
                    errors["name"] = ["The name must be 1 to 100 characters."];
                }

                if (request.Type is null)
                {
                    errors["type"] = ["Pick the type of account."];
                }

                if (request.CurrencyCode is null || !(await currencies.ListAsync(cancellationToken)).Any(c => c.Code == request.CurrencyCode))
                {
                    errors["currencyCode"] = ["Pick one of the supported currencies."];
                }

                if (errors.Count > 0)
                {
                    return TypedResults.ValidationProblem(errors);
                }

                var access = http.Access();
                var (outcome, account) = await accounts.CreateAsync(access.ProfileId, name, request.Type!.Value, request.CurrencyCode!, access.UserId, cancellationToken);
                return outcome == AccountOutcome.NameTaken
                    ? TypedResults.Conflict(new ProblemDetails { Title = "An account with that name already exists.", Status = StatusCodes.Status409Conflict })
                    : TypedResults.Created($"/api/profiles/{access.ProfileId}/accounts/{account!.Id}", account);
            })
            .WithName("CreateAccount")
            .WithSummary("Adds an account to the household.");

        profile.MapPatch("/accounts/{accountId:int}", async Task<Results<Ok<Account>, NotFound, ValidationProblem, Conflict<ProblemDetails>>> (int accountId, UpdateAccount request, HttpContext http, IAccountStore accounts, ICurrencyCatalog currencies, CancellationToken cancellationToken) =>
            {
                var errors = new Dictionary<string, string[]>();
                string? name = null;
                if (request.Name is not null)
                {
                    name = request.Name.Trim();
                    if (name.Length is 0 or > 100)
                    {
                        errors["name"] = ["The name must be 1 to 100 characters."];
                    }
                }

                if (request.CurrencyCode is not null && !(await currencies.ListAsync(cancellationToken)).Any(c => c.Code == request.CurrencyCode))
                {
                    errors["currencyCode"] = ["Pick one of the supported currencies."];
                }

                if (request.Name is null && request.Type is null && request.CurrencyCode is null && request.Archived is null)
                {
                    errors["account"] = ["Send something to change."];
                }

                if (errors.Count > 0)
                {
                    return TypedResults.ValidationProblem(errors);
                }

                var access = http.Access();
                var (outcome, account) = await accounts.UpdateAsync(
                    access.ProfileId, accountId, new AccountChanges(name, request.Type, request.CurrencyCode, request.Archived), access.UserId, cancellationToken);
                return outcome switch
                {
                    AccountOutcome.NotFound => TypedResults.NotFound(),
                    AccountOutcome.NameTaken => TypedResults.Conflict(new ProblemDetails { Title = "An account with that name already exists.", Status = StatusCodes.Status409Conflict }),
                    _ => TypedResults.Ok(account!),
                };
            })
            .WithName("UpdateAccount")
            .WithSummary("Changes an account: name, type, currency, or archive and restore it.");

        return profile;
    }
}

/// <summary>New settings for a profile; a null one stays as it is.</summary>
internal sealed record UpdateProfile(string? Name = null, string? MainCurrency = null);

/// <summary>Whom to invite: the email address of their Google account.</summary>
internal sealed record InviteMember(string? Email = null);

/// <summary>The profile an accepted invite joined.</summary>
internal sealed record AcceptedInvite(int ProfileId);

/// <summary>The token from an invite link.</summary>
internal sealed record AcceptInvite(string? Token = null);

/// <summary>A new account.</summary>
internal sealed record CreateAccount(string? Name = null, AccountType? Type = null, string? CurrencyCode = null);

/// <summary>Changes to an account; a missing field stays as it is. Archived true archives it, false restores it.</summary>
internal sealed record UpdateAccount(string? Name = null, AccountType? Type = null, string? CurrencyCode = null, bool? Archived = null);
