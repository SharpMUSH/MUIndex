using System.Text.Json.Nodes;

using MUI.Catalog;

namespace MUI.Web.Components;

/// <summary>
/// A listing page as schema.org: a collection, and the games on it in the order they are drawn.
/// </summary>
/// <remarks>
/// The game pages said what they were; the pages that gather them said nothing, so a listing read as
/// an article that happens to contain a lot of links.
/// <para>
/// Names and URLs only, first <see cref="Limit"/> rows: the per-game facts belong on the game's own
/// page, which each item links to. <c>numberOfItems</c> counts what is in this list, not what the
/// query matched. No <c>itemListOrder</c> — the vocabulary's ordering values describe rank, which on
/// this site is a word with a specific meaning.
/// </para>
/// </remarks>
public static class ListingStructuredData
{
    private const string Vocabulary = "https://schema.org";

    /// <summary>
    /// How many rows enter the graph.
    /// </summary>
    /// <remarks>Short of the hundred a listing draws: the marginal item is several hundred bytes on
    /// every request and adds nothing a crawler cannot get by following the first one.</remarks>
    public const int Limit = 30;

    /// <summary>The graph for one listing page.</summary>
    /// <param name="games">The games, in the order the page draws them.</param>
    /// <param name="origin">This site's absolute origin — scheme, authority and path base.</param>
    /// <param name="path">This page's own path, with the query that identifies it, if any.</param>
    /// <param name="name">What this collection is called, in the locale being answered.</param>
    /// <param name="description">What this collection is, in the locale being answered.</param>
    public static string For(
        IReadOnlyList<GameSummary> games,
        Uri origin,
        string path,
        string name,
        string description)
    {
        ArgumentNullException.ThrowIfNull(games);
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(path);

        var root = origin.ToString().TrimEnd('/');
        var url = root + path;

        var items = new JsonArray();
        var position = 0;

        foreach (var game in games.Take(Limit))
        {
            items.Add(new JsonObject
            {
                ["@type"] = "ListItem",
                ["position"] = ++position,
                ["url"] = $"{root}/g/{game.Slug}",
                ["name"] = game.Name,
            });
        }

        var page = new JsonObject
        {
            ["@type"] = "CollectionPage",
            ["@id"] = url,
            ["url"] = url,
            ["name"] = name,
            ["description"] = description,
            ["isPartOf"] = new JsonObject { ["@id"] = $"{root}#website" },
            ["mainEntity"] = new JsonObject
            {
                ["@type"] = "ItemList",
                ["numberOfItems"] = position,
                ["itemListElement"] = items,
            },
        };

        var document = new JsonObject
        {
            ["@context"] = Vocabulary,
            ["@graph"] = new JsonArray(page),
        };

        return document.ToJsonString();
    }
}
