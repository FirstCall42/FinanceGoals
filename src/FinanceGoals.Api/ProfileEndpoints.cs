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

    private const int _maxSnapshotValues = 200;
    private const decimal _maxAmount = 10_000_000_000_000m;
    private static readonly DateOnly _earliestSnapshot = new(1990, 1, 1);

    /// <summary>
    /// Checks a snapshot before anything is saved: a date that isn't in the future, accounts that are
    /// the household's, no account twice, and amounts that are whole minor units (øre, cents) of the
    /// account's own currency, because an amount has no currency of its own.
    /// </summary>
    private static async Task<(Dictionary<string, string[]> Errors, Dictionary<int, string> AccountCurrency)> ValidateSnapshotAsync(
        SaveSnapshot request, int profileId, IAccountStore accounts, ICurrencyCatalog currencies, TimeProvider clock, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        var accountCurrency = new Dictionary<int, string>();
        // A day ahead of UTC, so a date that is "today" anywhere on Earth is accepted.
        var latest = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime).AddDays(1);
        if (request.Date is not { } date || date < _earliestSnapshot || date > latest)
        {
            errors["date"] = [$"The date must be between {_earliestSnapshot:yyyy-MM-dd} and today."];
        }

        if (request.Values is not { Count: > 0 and <= _maxSnapshotValues } values)
        {
            errors["values"] = [$"Record the balance of at least one account, and at most {_maxSnapshotValues}."];
            return (errors, accountCurrency);
        }

        if (values.Any(v => v.AccountId is null || v.Amount is null) || values.Select(v => v.AccountId).Distinct().Count() != values.Count)
        {
            errors["values"] = ["Each balance needs an account and an amount, and an account can appear once."];
            return (errors, accountCurrency);
        }

        accountCurrency = (await accounts.ListAsync(profileId, includeArchived: true, cancellationToken)).ToDictionary(a => a.Id, a => a.CurrencyCode);
        var minorUnits = (await currencies.ListAsync(cancellationToken)).ToDictionary(c => c.Code, c => (int)c.MinorUnits);
        var problems = new List<string>();
        foreach (var value in values)
        {
            if (!accountCurrency.TryGetValue(value.AccountId!.Value, out var code))
            {
                problems.Add("A balance is for an account that is not in this household.");
                continue;
            }

            var currency = new FinanceGoals.Domain.Currencies.Currency(code, minorUnits[code]);
            var amount = new FinanceGoals.Domain.Currencies.Money(value.Amount!.Value, currency);
            if (Math.Abs(amount.Amount) > _maxAmount || amount.Round() != amount)
            {
                problems.Add($"A balance in {code} must be a whole number of {(minorUnits[code] == 0 ? "units" : "minor units (øre, cents)")} and at most 10 000 000 000 000.");
            }
        }

        if (problems.Count > 0)
        {
            errors["values"] = [.. problems.Distinct()];
        }

        return (errors, accountCurrency);
    }

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

                if (request.Type is not { } type || !Enum.IsDefined(type))
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

                if (request.Type is { } newType && !Enum.IsDefined(newType))
                {
                    errors["type"] = ["Pick the type of account."];
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
                    AccountOutcome.CurrencyLocked => TypedResults.Conflict(new ProblemDetails { Title = "This account has balances in its currency, so the currency can no longer change. Archive it and add a new account in the other currency.", Status = StatusCodes.Status409Conflict }),
                    AccountOutcome.NameTaken => TypedResults.Conflict(new ProblemDetails { Title = "An account with that name already exists.", Status = StatusCodes.Status409Conflict }),
                    _ => TypedResults.Ok(account!),
                };
            })
            .WithName("UpdateAccount")
            .WithSummary("Changes an account: name, type, currency, or archive and restore it.");

        profile.MapGet("/values", async (int? accountId, HttpContext http, IAccountValueStore values, CancellationToken cancellationToken) =>
                TypedResults.Ok(await values.ListAsync(http.Access().ProfileId, accountId, cancellationToken)))
            .WithName("ListAccountValues")
            .WithSummary("The household's account balances, newest first; one account's with accountId.");

        profile.MapPost("/snapshots", async Task<Results<Ok<SnapshotSaved>, ValidationProblem, Conflict<ProblemDetails>>> (SaveSnapshot request, HttpContext http, IAccountStore accounts, IAccountValueStore values, ICurrencyCatalog currencies, TimeProvider clock, CancellationToken cancellationToken) =>
            {
                var access = http.Access();
                var (errors, accountCurrency) = await ValidateSnapshotAsync(request, access.ProfileId, accounts, currencies, clock, cancellationToken);
                if (errors.Count > 0)
                {
                    return TypedResults.ValidationProblem(errors);
                }

                var outcome = await values.SaveAsync(
                    access.ProfileId, request.Date!.Value, [.. request.Values!.Select(v => (v.AccountId!.Value, v.Amount!.Value, accountCurrency[v.AccountId.Value]))], access.UserId, cancellationToken);
                return outcome switch
                {
                    ValueOutcome.Saved => TypedResults.Ok(new SnapshotSaved(request.Date.Value, request.Values!.Count)),
                    ValueOutcome.CurrencyChanged => TypedResults.Conflict(new ProblemDetails { Title = "An account's currency changed while you were saving. Reload the page and enter the balances again.", Status = StatusCodes.Status409Conflict }),
                    _ => TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["values"] = ["A balance is for an account that is not in this household."] }),
                };
            })
            .WithName("SaveSnapshot")
            .WithSummary("Records the balances of several accounts on one date. A balance already recorded for that account and date is replaced; accounts left out are not recorded.");

        profile.MapDelete("/snapshots/{date}", async Task<Results<NoContent, NotFound>> (DateOnly date, HttpContext http, IAccountValueStore values, CancellationToken cancellationToken) =>
                await values.DeleteSnapshotAsync(http.Access().ProfileId, date, cancellationToken) > 0 ? TypedResults.NoContent() : TypedResults.NotFound())
            .WithName("DeleteSnapshot")
            .WithSummary("Deletes every balance recorded on a date.");

        profile.MapDelete("/accounts/{accountId:int}/values/{date}", async Task<Results<NoContent, NotFound>> (int accountId, DateOnly date, HttpContext http, IAccountValueStore values, CancellationToken cancellationToken) =>
                await values.DeleteAsync(http.Access().ProfileId, accountId, date, cancellationToken) ? TypedResults.NoContent() : TypedResults.NotFound())
            .WithName("DeleteAccountValue")
            .WithSummary("Deletes one account's balance on a date.");

        profile.MapNetWorth();
        profile.MapLoans();

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

/// <summary>One account's balance in a snapshot, in the account's own currency.</summary>
internal sealed record SnapshotValue(int? AccountId = null, decimal? Amount = null);

/// <summary>Balances of several accounts on one date.</summary>
internal sealed record SaveSnapshot(DateOnly? Date = null, IReadOnlyList<SnapshotValue>? Values = null);

/// <summary>A saved snapshot: its date and how many balances it holds.</summary>
internal sealed record SnapshotSaved(DateOnly Date, int Count);
