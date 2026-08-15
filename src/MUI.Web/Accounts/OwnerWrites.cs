using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Primitives;

using MUI.Catalog.Persistence;

namespace MUI.Web.Accounts;

/// <summary>
/// The four things a verified owner may change, as four form posts (spec §8.5, §11).
/// </summary>
/// <remarks>
/// <para>
/// Plain <c>POST</c> and a redirect, with no scripting anywhere: §8.2 draws the JavaScript boundary
/// around the WebAuthn ceremony and nowhere else, and an owner who has signed in should not need a
/// second reason to keep it on. The outcome comes back in the querystring because that is what
/// survives a redirect, and the dashboard says it out loud.
/// </para>
/// <para>
/// Neither endpoint decides anything. Both hand the request to <see cref="OwnerEnrichment"/>, which
/// owns the authorisation and the writable set, so the rule that an owner may never edit a
/// measurement has one spelling and a second caller cannot acquire a more generous one.
/// </para>
/// </remarks>
public static class OwnerWrites
{
    /// <summary>
    /// The prefix a form control carries to name the field it edits — <c>field:FANDOM</c>.
    /// </summary>
    /// <remarks>
    /// Names travel with values so the gate can be on the name. Everything else a form posts — the
    /// anti-forgery token, a button — is not an edit and is ignored rather than guessed at.
    /// </remarks>
    public const string FieldPrefix = "field:";

    /// <summary>Where both endpoints send an operator back to.</summary>
    private const string Dashboard = Passkeys.DashboardPath;

    /// <summary>
    /// Which of the three things just happened, so the dashboard can say the right one.
    /// </summary>
    /// <remarks>
    /// Both endpoints reported <c>saved</c> and nothing else to begin with, so hiding a connect
    /// screen came back as "your page now shows it as owner-declared" — a sentence about a different
    /// action, on the surface whose whole job is to say what actually happened.
    /// </remarks>
    private static class Saved
    {
        public const string Fields = "fields";

        public const string ScreenHidden = "screen-hidden";

        public const string ScreenShown = "screen-shown";

        public const string Stopped = "crawl-stopped";

        public const string Resumed = "crawl-resumed";

        public const string WhoHeader = "who-header";
    }

    /// <summary>
    /// The edits a posted form is asking for, in the order it asked for them.
    /// </summary>
    /// <remarks>
    /// Deliberately not filtered here. A form key naming a measurement is passed straight through to
    /// be <em>refused</em>, because a parser that quietly dropped it would turn §8.5's out-loud
    /// refusal back into the silent no-op it exists to prevent.
    /// </remarks>
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

        // §8.5's enrichment: the fields MSSP has no room for. What comes back is stored as
        // FieldSource.Owner, beside whatever the crawler measured and never instead of it.
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

            return Landing(await enrichment.ApplyAsync(gameId, user.Id, EditsIn(form)), gameId, Saved.Fields);
        });

        // §11: suppressed on owner request, no questions asked. One button, no reason field, and no
        // second step — asking why would be the interface arguing with a rule the site does not have.
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
                await enrichment.SetConnectScreenSuppressedAsync(gameId, user.Id, suppress),
                gameId,
                suppress ? Saved.ScreenHidden : Saved.ScreenShown);
        });

        // §8.5's WHO-format override. A separate post from the enrichment form because it is a
        // different kind of thing: everything in that form is a fact we publish about the game, and
        // this is an instruction our parser acts on.
        games.MapPost("/who-header", async (
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
                await enrichment.SetWhoHeaderAsync(gameId, user.Id, form["header"], context.RequestAborted),
                gameId,
                Saved.WhoHeader);
        });

        // §11's third route, for the one person who does not have to be taken on trust. MSSP and DNS
        // are answered by the game itself; a recorded request was reachable only from the crawler
        // CLI, so the operator who had already proved they run the game was the one person who could
        // not ask through the interface built for them.
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

            // Who asked, named from the account rather than from the form: the form is the request
            // and the identity is ours to state. §11's Request route requires a claim somebody can
            // stand behind, and one typed into a posted field would not be one.
            var outcome = await optOut.SetAsync(
                gameId,
                user.Id,
                stop,
                await users.GetUserNameAsync(user) ?? user.Id.ToString(),
                context.RequestAborted);

            return outcome.Verdict switch
            {
                OwnerOptOutVerdict.Applied => Results.Redirect(
                    $"{Dashboard}?saved={gameId}&did={(stop ? Saved.Stopped : Saved.Resumed)}"),
                OwnerOptOutVerdict.NotAnOwner => Results.StatusCode(StatusCodes.Status403Forbidden),

                // Out loud, because the failure mode of saying nothing here is an owner who believes
                // we have stopped crawling them and has not been told we never had an address to stop.
                _ => Results.Redirect(
                    $"{Dashboard}?refused=crawl&because={OwnerOptOutVerdict.NoAddresses}"),
            };
        });
    }

    /// <summary>
    /// Where an outcome sends the operator, and what it says when they get there.
    /// </summary>
    /// <remarks>
    /// A request from somebody with no verified claim on the game is a 403 rather than a message,
    /// because it is not a mistake an owner can correct on the page. Everything an owner <em>can</em>
    /// correct arrives back on the dashboard naming the field, which is §8.5's out-loud refusal: a
    /// silent no-op teaches an owner that the site is broken.
    /// </remarks>
    private static IResult Landing(EnrichmentOutcome outcome, Guid gameId, string what) => outcome.Verdict switch
    {
        EnrichmentVerdict.Applied => Results.Redirect($"{Dashboard}?saved={gameId}&did={what}"),
        EnrichmentVerdict.NotAnOwner => Results.StatusCode(StatusCodes.Status403Forbidden),
        _ => Results.Redirect(
            $"{Dashboard}?refused={Uri.EscapeDataString(outcome.Field ?? string.Empty)}"
            + $"&because={Uri.EscapeDataString(outcome.Verdict.ToString())}"),
    };
}
