using System.Security.Claims;
using FinanceGoals.Application.Profiles;
using Microsoft.AspNetCore.Http.HttpResults;

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
                await profiles.NameAsync(http.Access().ProfileId, cancellationToken) is { } name
                    ? TypedResults.Ok(new ProfileSummary(http.Access().ProfileId, name, http.Access().Role))
                    : TypedResults.NotFound())
            .WithName("GetProfile")
            .WithSummary("The profile, with the caller's role in it.");

        profile.MapPatch("", async Task<Results<NoContent, ValidationProblem>> (RenameProfile request, HttpContext http, IProfileStore profiles, CancellationToken cancellationToken) =>
            {
                var name = request.Name?.Trim() ?? string.Empty;
                if (name.Length is 0 or > 100)
                {
                    return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["The name must be 1 to 100 characters."] });
                }

                await profiles.RenameAsync(http.Access().ProfileId, name, http.Access().UserId, cancellationToken);
                return TypedResults.NoContent();
            })
            .WithMetadata(new OwnerOnly())
            .WithName("RenameProfile")
            .WithSummary("Renames the profile. Owners only.");

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

        return profile;
    }
}

/// <summary>A new name for a profile.</summary>
internal sealed record RenameProfile(string? Name);

/// <summary>Whom to invite: the email address of their Google account.</summary>
internal sealed record InviteMember(string? Email);

/// <summary>The profile an accepted invite joined.</summary>
internal sealed record AcceptedInvite(int ProfileId);
