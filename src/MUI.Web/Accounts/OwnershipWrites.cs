using Microsoft.AspNetCore.Identity;

using MUI.Catalog;
using MUI.Catalog.Persistence;

using MUI.Web.Localization;

namespace MUI.Web.Accounts;

/// <summary>
/// The two ownership decisions a person makes with a form (spec §8.4, §8.5).
/// </summary>
/// <remarks>
/// Both delegate every decision to <see cref="ClaimService"/>, including who may make it. A claim id
/// is not a credential and travels in URLs and logs, so the account is checked against the claim
/// rather than assumed from knowledge of its id.
/// </remarks>
public static class OwnershipWrites
{
    /// <summary>
    /// The word an operator types to confirm giving up a claim.
    /// </summary>
    /// <remarks>A constant so the dashboard's printed instruction and this endpoint's comparison agree. Never translated — the comparison only accepts this exact word.</remarks>
    public const string ResignConfirmation = "resign";

    public static void MapMuiOwnership(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // The intent (join vs. assume) is chosen HERE, before the token exists, and stored on the
        // claim it belongs to — a token published as a co-owner must not be settleable as a takeover.
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
                return Back(context, $"/g/{slug}/claim");
            }

            var intent = string.Equals(form["intent"], "assume", StringComparison.Ordinal)
                ? ClaimIntent.Assume
                : ClaimIntent.Join;

            await claims.IssueAsync(page.Summary.Id, user.Id, intent);

            return Back(context, $"/g/{slug}/claim");
        }).RequireAuthorization();

        // §8.4 requires every non-counter-claim revocation to be explicit: a claim never lapses on
        // its own and absence never revokes.
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

            // A typed confirmation rather than a second page — a bare button would be one misclick
            // from a listing an operator no longer owns.
            if (!string.Equals(form["confirm"], ResignConfirmation, StringComparison.Ordinal))
            {
                return Back(context, $"{Passkeys.DashboardPath}?resign={claimId}");
            }

            return await claims.ResignAsync(claimId, user.Id)
                ? Back(context, $"{Passkeys.DashboardPath}?resigned=1")
                : Results.StatusCode(StatusCodes.Status403Forbidden);
        }).RequireAuthorization();
    }

    /// <summary>The page the operator came from, in the language they were reading it in.</summary>
    /// <remarks>The posted form carries this locale in its own action, so the request always has one.</remarks>
    private static IResult Back(HttpContext context, string path) =>
        Results.Redirect(LocaleRouting.Link(context.LocaleOf().Tag, path));
}
