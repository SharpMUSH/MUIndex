using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Primitives;

using MUI.Catalog.Persistence;

using MUI.Web.Localization;

namespace MUI.Web.Accounts;

/// <summary>
/// The three things a verified owner may change, as three form posts (spec §8.5, §11).
/// </summary>
/// <remarks>
/// Plain <c>POST</c> and a redirect, with no scripting — §8.2 draws the JavaScript boundary around
/// the WebAuthn ceremony and nowhere else. The outcome comes back in the querystring, what survives a
/// redirect, and the dashboard says it out loud.
/// Neither endpoint decides anything; both hand the request to <see cref="OwnerEnrichment"/>, which
/// owns the authorisation and the writable set, so the rule that an owner may never edit a
/// measurement has one spelling.
/// </remarks>
public static class OwnerWrites
{
    /// <summary>
    /// The prefix a form control carries to name the field it edits — <c>field:FANDOM</c>.
    /// </summary>
    /// <remarks>Names travel with values so the gate can be on the name. Everything else a form posts is ignored rather than guessed at.</remarks>
    public const string FieldPrefix = "field:";

    /// <summary>Where both endpoints send an operator back to.</summary>
    private const string Dashboard = Passkeys.DashboardPath;

    /// <summary>
    /// Which of the three things just happened, so the dashboard can say the right one.
    /// </summary>
    /// <remarks>Distinct outcomes so the dashboard's message always names the action that actually happened, not a generic "saved".</remarks>
    private static class Saved
    {
        public const string Fields = "fields";

        public const string ScreenHidden = "screen-hidden";

        public const string ScreenShown = "screen-shown";

        public const string Stopped = "crawl-stopped";

        public const string Resumed = "crawl-resumed";

        public const string Unlisted = "unlisted";

        public const string Relisted = "relisted";
    }

    /// <summary>
    /// The edits a posted form is asking for, in the order it asked for them.
    /// </summary>
    /// <remarks>Deliberately not filtered here: a key naming a measurement passes through to be refused downstream, since dropping it silently would defeat §8.5's out-loud refusal.</remarks>
    public static IReadOnlyList<OwnerEdit> EditsIn(IEnumerable<KeyValuePair<string, StringValues>> form)
    {
        ArgumentNullException.ThrowIfNull(form);

        return
        [
            .. form
                .Where(entry => entry.Key.StartsWith(FieldPrefix, StringComparison.Ordinal))
                .Select(entry => new OwnerEdit(
                    entry.Key[FieldPrefix.Length..],
                    entry.Value.Count > 0 ? entry.Value[^1] ?? string.Empty : string.Empty)),
        ];
    }

    public static void MapMuiOwnerWrites(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var games = app.MapGroup("/account/games/{gameId:guid}").RequireAuthorization();

        // §8.5's enrichment: the fields MSSP has no room for, stored as FieldSource.Owner beside
        // whatever the crawler measured, never instead of it.
        games.MapPost("/enrichment", async (
            HttpContext context,
            UserManager<MuiUser> users,
            OwnerEnrichment enrichment,
            Guid gameId,
            IFormCollection form) =>
        {
            if (await users.GetUserAsync(context.User) is not { } user)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            return Landing(
                context, await enrichment.ApplyAsync(gameId, user.Id, EditsIn(form)), gameId, Saved.Fields);
        });

        // §11: suppressed on owner request, no questions asked, no reason field.
        games.MapPost("/connect-screen", async (
            HttpContext context,
            UserManager<MuiUser> users,
            OwnerEnrichment enrichment,
            Guid gameId,
            IFormCollection form) =>
        {
            if (await users.GetUserAsync(context.User) is not { } user)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            var suppress = string.Equals(form["suppress"], "true", StringComparison.Ordinal);

            return Landing(
                context,
                await enrichment.SetConnectScreenSuppressedAsync(gameId, user.Id, suppress),
                gameId,
                suppress ? Saved.ScreenHidden : Saved.ScreenShown);
        });

        // §11's third route, for the one person who does not have to be taken on trust; MSSP and DNS
        // are answered by the game itself.
        games.MapPost("/crawl", async (
            HttpContext context,
            UserManager<MuiUser> users,
            OwnerOptOut optOut,
            Guid gameId,
            IFormCollection form) =>
        {
            if (await users.GetUserAsync(context.User) is not { } user)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            var stop = string.Equals(form["stop"], "true", StringComparison.Ordinal);

            // Who asked, named from the account rather than the form — §11's Request route requires a
            // claim somebody can stand behind, and one typed into a posted field would not be one.
            var outcome = await optOut.SetAsync(
                gameId,
                user.Id,
                stop,
                await users.GetUserNameAsync(user) ?? user.Id.ToString(),
                context.RequestAborted);

            return outcome.Verdict switch
            {
                OwnerOptOutVerdict.Applied => Back(context,
                    $"{Dashboard}?saved={gameId}&did={(stop ? Saved.Stopped : Saved.Resumed)}"),
                OwnerOptOutVerdict.NotAnOwner => Results.StatusCode(StatusCodes.Status403Forbidden),

                // Out loud: the failure mode of saying nothing is an owner who believes we stopped
                // crawling them when there was never an address to stop.
                _ => Back(context,
                    $"{Dashboard}?refused=crawl&because={OwnerOptOutVerdict.NoAddresses}"),
            };
        });

        // The second half of §11's opt-out (migration 0025): out of the listing as well as the crawl.
        // Offered only where a standing opt-out already covers every address.
        games.MapPost("/listing", async (
            HttpContext context,
            UserManager<MuiUser> users,
            OwnerListing listing,
            Guid gameId,
            IFormCollection form) =>
        {
            if (await users.GetUserAsync(context.User) is not { } user)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            var unlist = string.Equals(form["unlist"], "true", StringComparison.Ordinal);

            var outcome = await listing.SetAsync(gameId, user.Id, unlist, context.RequestAborted);

            return outcome.Verdict switch
            {
                OwnerListingVerdict.Applied => Back(context,
                    $"{Dashboard}?saved={gameId}&did={(unlist ? Saved.Unlisted : Saved.Relisted)}"),
                OwnerListingVerdict.NotAnOwner => Results.StatusCode(StatusCodes.Status403Forbidden),
                _ => Back(context,
                    $"{Dashboard}?refused=listing&because={OwnerListingVerdict.NotOptedOut}"),
            };
        });
    }

    /// <summary>
    /// Where an outcome sends the operator, and what it says when they get there.
    /// </summary>
    /// <remarks>A 403, not a message, for a caller with no verified claim — not a mistake an owner can correct. Everything an owner can correct names the field, per §8.5's out-loud refusal.</remarks>
    private static IResult Landing(
        HttpContext context, EnrichmentOutcome outcome, Guid gameId, string what) => outcome.Verdict switch
    {
        EnrichmentVerdict.Applied => Back(context, $"{Dashboard}?saved={gameId}&did={what}"),
        EnrichmentVerdict.NotAnOwner => Results.StatusCode(StatusCodes.Status403Forbidden),
        _ => Back(context,
            $"{Dashboard}?refused={Uri.EscapeDataString(outcome.Field ?? string.Empty)}"
            + $"&because={Uri.EscapeDataString(outcome.Verdict.ToString())}"),
    };

    /// <summary>
    /// The dashboard the operator came from, in the language they were reading it in.
    /// </summary>
    /// <remarks>The posted form carries this locale in its own action, so a redirect can't answer a non-English operator's saved edit with the wrong-language page.</remarks>
    private static IResult Back(HttpContext context, string path) =>
        Results.Redirect(LocaleRouting.Link(context.LocaleOf().Tag, path));
}
