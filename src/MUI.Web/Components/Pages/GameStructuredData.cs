using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

using MUI.Catalog;

namespace MUI.Web.Components;

/// <summary>
/// A game page as schema.org, for the readers that are programs.
/// </summary>
/// <remarks>
/// <para>
/// Every measurement here must carry when it was taken: <c>userInteractionCount</c> has nowhere to
/// say "as of", so the timestamp goes on <c>InteractionCounter.endTime</c> instead. Nothing enters
/// this graph without one.
/// </para>
/// <para>
/// Only measured counts are emitted — a declared count (MSSP) has no schema.org property meaning
/// "unverified", so publishing one here would misrepresent it as observed (rule 5).
/// </para>
/// <para>
/// Never emitted over the demo fixture (<c>SitePreview</c>): there is no field in this format for
/// "these facts are invented".
/// </para>
/// <para>
/// <b>A game's name is untrusted text landing inside a <c>&lt;script&gt;</c> element</b>, where
/// normal HTML escaping doesn't apply. Keep <see cref="JavaScriptEncoder"/> on its defaults — never
/// <c>UnsafeRelaxedJsonEscaping</c> — or <c>&lt;</c>/<c>&gt;</c> stop being escaped and this becomes
/// an XSS hole.
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
    /// This site's absolute origin — scheme, authority and path base. Appended to rather than
    /// resolved against: <see cref="Uri"/>'s relative-reference rules discard the path base, so
    /// <c>new Uri(new Uri("https://h/mui"), "/g/x")</c> names a page that only exists when the site
    /// is mounted at the root. See <see cref="SiteUrls.Absolute"/>.
    /// </param>
    /// <remarks>
    /// No <c>now</c> parameter: every timestamp here is a recorded measurement, never the render time.
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
            ["gamePlatform"] = "Text-based MU* server, over telnet",
        };

        if ((page.Description ?? summary.Tagline) is { } description)
        {
            game["description"] = description;
        }

        // Last confirmed, not last rendered — a page regenerated hourly from a three-year-old
        // measurement is three years old.
        if (summary.LastReachableAt is { } confirmed)
        {
            game["dateModified"] = confirmed.ToUniversalTime().ToString("o");
        }

        // Named only when we have a chip for it — an unlabelled value may not enter this graph.
        if (summary.CodebaseProvenance is not null && summary.Codebase is { } codebase)
        {
            game["runtimePlatform"] = codebase;
        }

        // Descriptive claims already displayed on the page; the linked API retains their sources.
        foreach (var (field, property) in new[] { ("GENRE", "genre"), ("LANGUAGE", "inLanguage") })
        {
            if (page.Declared.TryGetValue(field, out var chip)
                && !chip.IsStale && !string.IsNullOrWhiteSpace(chip.Value))
            {
                game[property] = chip.Value;
            }
        }

        if (Server(page) is { } server)
        {
            game["gameServer"] = server;
        }

        game["identifier"] = summary.Id.ToString("D");
        game["subjectOf"] = new JsonObject
        {
            ["@type"] = "DataFeed",
            ["url"] = $"{root}{Api.ApiRoutes.Game(summary.Id)}",
            ["encodingFormat"] = "application/json",
            ["name"] = "MUIndex game record with sources and observation timestamps",
        };

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
    /// Where you connect. <c>telnet://</c> is a registered URI scheme (RFC 4248).
    /// </summary>
    /// <remarks>
    /// <b>No <c>serverStatus</c> and no <c>playersOnline</c>.</b> Both are values with nowhere to say
    /// "as of when" — the same gap that puts the count on an <c>InteractionCounter</c> with an
    /// <c>endTime</c>. An undated "Offline" would also state our vantage point as a fact about
    /// somebody's game: a game we cannot route to is unreachable and perfectly alive (rule 5).
    /// <c>dateModified</c> already carries when we last got in.
    /// </remarks>
    private static JsonObject? Server(GamePage page)
    {
        var endpoint = page.Endpoints.FirstOrDefault(e => e.IsCurrent) ?? page.Endpoints.FirstOrDefault();

        if (endpoint is null)
        {
            return null;
        }

        return new JsonObject
        {
            ["@type"] = "GameServer",
            ["url"] = $"telnet://{endpoint.Host}:{endpoint.Port.ToString(CultureInfo.InvariantCulture)}",
        };
    }

    /// <summary>
    /// The count, with the instant it was taken — or nothing at all.
    /// </summary>
    /// <remarks>
    /// Three cases, not two: unknown is omitted (rule 4). A measured zero is published — we got in
    /// and nobody was there. A declared count is omitted; there's no honest way to say "declared" here.
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

            // Without this the count above is an age-less number, which the site never publishes.
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
