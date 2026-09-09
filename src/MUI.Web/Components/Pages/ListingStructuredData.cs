using System.Text.Json.Nodes;

using MUI.Catalog;

namespace MUI.Web.Components;

/// <summary>
/// A listing page as schema.org: a collection, and the games on it in the order they are drawn.
/// </summary>
/// <remarks>
/// <para>
/// The game pages said what they were; the pages that gather them said nothing, so a listing was
/// indistinguishable from an article that happens to contain a lot of links. <c>CollectionPage</c>
/// with an <c>ItemList</c> as its <c>mainEntity</c> is the vocabulary for "this page's purpose is
/// the list on it", and it is what separates a catalogue from a blog to anything reading the markup.
/// </para>
/// <para>
/// <b>Names and URLs only, and only the first <see cref="Limit"/> rows.</b> A listing draws up to a
/// hundred games and this graph is bytes on a page that already loads slowly; the per-game facts
/// belong on the game's own page, which each item links to and which carries them with their
/// sources and dates. <c>numberOfItems</c> counts what is in this list rather than what the query
/// matched, so it cannot be read as a total the list does not contain.
/// </para>
/// <para>
/// <b>No <c>itemListOrder</c>.</b> The default order is by measured players, but a reader can sort
/// by six things and an archive listing is alphabetical — and the vocabulary's ordering values
/// describe rank, which on this site is a word with a specific meaning. The list is in page order;
/// nothing here claims that order means quality.
/// </para>
/// <para>
/// Suppressed over the demo fixture by <c>SitePreview</c>'s own gate, the same as every other
/// catalogue graph: these are game names, and over the fixture they are invented.
/// </para>
/// </remarks>
public static class ListingStructuredData
{
    private const string Vocabulary = "https://schema.org";

    /// <summary>
    /// How many rows enter the graph.
    /// </summary>
    /// <remarks>
    /// Enough to establish what the page is and to name its most prominent entries; short of the
    /// hundred a full listing draws, because the marginal item is several hundred bytes on every
    /// request and adds nothing a crawler cannot get by following the first one.
    /// </remarks>
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
