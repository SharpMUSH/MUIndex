using Microsoft.AspNetCore.Identity;

using MUI.Catalog;
using MUI.Catalog.Persistence;

namespace MUI.Web.Accounts;

/// <summary>
/// The two ownership decisions a person makes with a form (spec §8.4, §8.5).
/// </summary>
/// <remarks>
/// Both are plain posts and redirects, like the rest of the account surface, and both delegate every
/// decision to <see cref="ClaimService"/> — including who is allowed to make it. A claim id is not a
/// credential and travels in URLs and logs, so the account has to be checked against the claim
/// rather than assumed from the fact that somebody knew its id.
/// </remarks>
public static class OwnershipWrites
{
    public static void MapMuiOwnership(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Asking for a token on a game somebody already owns, having said which of the two things
        // §8.5 allows you are doing. The choice is made HERE, before the token exists, because it
        // is stored on the claim the token belongs to — a token published as a co-owner must not be
        // settleable as a takeover.
        app.MapPost("/g/{slug}/claim/start", async (
            HttpContext context,
            UserManager<MuiUser> users,
            IGameQueries queries,
            ClaimService claims,
            string slug,
            IFormCollection form) =>
        {
            if (await users.GetUserAsync(context.User) is not { } user
                || await queries.FindAsync(slug) is not { } page)
            {
                return Results.Redirect($"/g/{slug}/claim");
            }

            var intent = string.Equals(form["intent"], "assume", StringComparison.Ordinal)
                ? ClaimIntent.Assume
                : ClaimIntent.Join;

            await claims.IssueAsync(page.Summary.Id, user.Id, intent);

            return Results.Redirect($"/g/{slug}/claim");
        }).RequireAuthorization();

        // Giving up a game. Explicit, which §8.4 requires of every revocation that is not a
        // counter-claim: a claim never lapses on its own and absence never revokes.
        app.MapPost("/account/claims/{claimId:guid}/resign", async (
            HttpContext context,
            UserManager<MuiUser> users,
            ClaimService claims,
            Guid claimId,
            IFormCollection form) =>
        {
            if (await users.GetUserAsync(context.User) is not { } user)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            // A typed confirmation rather than a second page. Resigning is irreversible in the sense
            // that getting back in means publishing a fresh token — recoverable, but not by pressing
            // undo — and a bare button beside a game's name is one misclick from a listing an
            // operator no longer owns.
            if (!string.Equals(form["confirm"], "resign", StringComparison.Ordinal))
            {
                return Results.Redirect($"{Passkeys.DashboardPath}?resign={claimId}");
            }

            return await claims.ResignAsync(claimId, user.Id)
                ? Results.Redirect($"{Passkeys.DashboardPath}?resigned=1")
                : Results.StatusCode(StatusCodes.Status403Forbidden);
        }).RequireAuthorization();
    }
}
