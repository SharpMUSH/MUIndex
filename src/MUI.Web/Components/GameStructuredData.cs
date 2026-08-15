using System.Text.Json;
using System.Text.Json.Nodes;

using MUI.Catalog;

namespace MUI.Web.Components;

/// <summary>
/// A game page as schema.org, for the readers that are programs.
/// </summary>
/// <remarks>
/// <para>
/// <b>The vocabulary invites exactly the mistake this project exists to prevent.</b>
/// <c>userInteractionCount</c> is a bare integer, and there is nowhere in the obvious spelling of it
/// to put "and we read that four minutes ago". A number published without its age is the unlabelled
/// fact spec §10.1 called the one place a surface could contradict the whole project — so the rule
/// enforced here is narrow and absolute: <b>nothing enters this graph unless the thing beside it
/// carries when it was taken</b>. <c>InteractionCounter.endTime</c> is where the timestamp goes.
/// </para>
/// <para>
/// <b>Only measurements go in.</b> A count a game declared about itself in MSSP stays on the page,
/// where it sits under a chip reading <em>declared</em> and a reader can weigh it. There is no
/// schema.org property that means "they said so and we could not check", and the nearest ones all
/// read as observations — so publishing a declared count here would be our inability to verify it,
/// republished in a machine-readable format as our having verified it. That is rule 5 exactly.
/// </para>
/// <para>
/// <b>And none of it is emitted over the demo fixture.</b> That decision is the caller's, in
/// <c>SitePreview</c>: a consumer of this graph never sees the banner, and there is no field in
/// which to write "these facts are invented".
/// </para>
/// <para>
/// <b>A game's name is a stranger's bytes.</b> It arrives from MSSP or off a connect screen on a
/// host nobody here controls, and it lands inside a <c>&lt;script&gt;</c> element — the one place in
/// an HTML document where the usual escaping does not apply, because the parser is looking for
/// <c>&lt;/script&gt;</c> and not for entities. <see cref="JavaScriptEncoder"/>'s default behaviour
/// escapes <c>&lt;</c> and <c>&gt;</c>, which is what closes that hole, so this serializer is
/// deliberately left on its defaults and must never be given
/// <c>JavaScriptEncoder.UnsafeRelaxedJsonEscaping</c> to make the output prettier.
/// </para>
/// </remarks>
public static class GameStructuredData
{
    private const string Vocabulary = "https://schema.org";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
    };

    /// <summary>The graph for one game page.</summary>
    /// <param name="page">The game, as the page renders it.</param>
    /// <param name="origin">
    /// This site's absolute origin, from the request being answered — scheme, authority and path
    /// base. Appended to rather than resolved against, for the reason <see cref="SiteUrls.Absolute"/>
    /// gives: <see cref="Uri"/>'s relative-reference rules discard the path base, so
    /// <c>new Uri(new Uri("https://h/mui"), "/g/x")</c> names a page that exists only where the site
    /// happens to be mounted at the root.
    /// </param>
    /// <remarks>
    /// There is no <c>now</c> parameter, deliberately. Every instant in this graph is one we
    /// recorded when we took the measurement; a value stamped with the moment the page happened to
    /// be rendered would be a freshness claim nobody made.
    /// </remarks>
    public static string For(GamePage page, Uri origin)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(origin);

        var root = origin.ToString().TrimEnd('/');
        var summary = page.Summary;
        var url = $"{root}/g/{summary.Slug}";

        var game = new JsonObject
        {
            ["@type"] = "VideoGame",
            ["@id"] = url,
            ["name"] = summary.Name,
            ["url"] = url,
            ["playMode"] = "MultiPlayer",
            ["gamePlatform"] = "MU* server, over telnet",
        };

        if ((summary.Tagline ?? page.Description) is { } description)
        {
            game["description"] = description;
        }

        // When the facts on this page were last confirmed — not when it was rendered. A page
        // regenerated hourly from a measurement three years old is three years old.
        if (summary.LastReachableAt is { } confirmed)
        {
            game["dateModified"] = confirmed.ToUniversalTime().ToString("o");
        }

        // The codebase is what a game runs, which is the closest schema.org gets to it — and it is
        // named only when we have a chip for it, because an unlabelled value is the one thing this
        // graph may not contain.
        if (summary.CodebaseProvenance is not null && summary.Codebase is { } codebase)
        {
            game["gameServer"] = codebase;
        }

        if (Counter(summary) is { } counter)
        {
            game["interactionStatistic"] = counter;
        }

        var graph = new JsonArray(game, Breadcrumbs(root, summary, url));

        var document = new JsonObject
        {
            ["@context"] = Vocabulary,
            ["@graph"] = graph,
        };

        return document.ToJsonString(Options);
    }

    /// <summary>
    /// The count, with the instant it was taken — or nothing at all.
    /// </summary>
    /// <remarks>
    /// Three cases and they are not two. An unknown count is omitted (rule 4: an unreadable
    /// <c>WHO</c> yields unknown, never zero). A measured zero is published, because we got in and
    /// nobody was there and that is a fact about the game. A declared count is omitted, because
    /// there is no honest way to say "declared" here.
    /// </remarks>
    private static JsonObject? Counter(GameSummary summary)
    {
        if (summary.PlayersNow is not { } players ||
            summary.PlayersNowProvenance is not { IsMeasured: true } chip)
        {
            return null;
        }

        return new JsonObject
        {
            ["@type"] = "InteractionCounter",
            ["interactionType"] = $"{Vocabulary}/PlayAction",
            ["userInteractionCount"] = players,

            // The whole reason this block is safe to publish. Without it the integer above is a
            // number with no age, which is the thing the site does not have anywhere else.
            ["endTime"] = chip.LastConfirmedAt.ToUniversalTime().ToString("o"),
        };
    }

    private static JsonObject Breadcrumbs(string root, GameSummary summary, string url) => new()
    {
        ["@type"] = "BreadcrumbList",
        ["itemListElement"] = new JsonArray(
            Crumb(1, "Games", $"{root}/games"),
            Crumb(2, summary.Name, url)),
    };

    private static JsonObject Crumb(int position, string name, string item) => new()
    {
        ["@type"] = "ListItem",
        ["position"] = position,
        ["name"] = name,
        ["item"] = item,
    };
}
