using MUI.Catalog;
using MUI.Web.Localization;

namespace MUI.Web.Components;

/// <summary>
/// What a faceted listing calls itself once it is a page of its own.
/// </summary>
/// <remarks>
/// <para>
/// One indexable listing per dimension value (see <see cref="IndexableFacet"/>), each needing three
/// sentences nobody had written: a heading, a document title, and a description. Before this, all of
/// them said "Games" and carried <c>/games</c>'s description — the same two strings across every
/// category, which is the shape a search engine treats as one page duplicated rather than as a
/// catalogue.
/// </para>
/// <para>
/// <b>The evidence word is not decoration here either.</b> A protocol facet reads the handshake we
/// watched; a genre or language facet reads what the game said about itself; a lineage is a
/// classification of ours. Each description says which, because the same sentence over all five
/// would state a measurement for three facets that never made one (rule 1, rule 5).
/// </para>
/// <para>
/// The value itself is machine voice — a codebase name, a protocol acronym, a genre a game typed
/// into its own <c>mush.cnf</c> — and is passed as an argument rather than translated.
/// </para>
/// </remarks>
public static class CategoryCopy
{
    /// <summary>The document's first line.</summary>
    public static string Heading(string tag, IndexableFacet.Category category) =>
        Say(tag, "games.heading.", category);

    /// <summary>The browser-tab noun phrase, which a title bar and a search result both show.</summary>
    /// <remarks>
    /// A separate id from <see cref="Heading"/> even where the English is the same words — the
    /// reason <c>PreviewCopy.Titles</c> gives applies here unchanged.
    /// </remarks>
    public static string Title(string tag, IndexableFacet.Category category) =>
        Say(tag, "preview.title.category.", category);

    /// <summary>What this listing is, for a search result and for a link somebody pasted.</summary>
    public static string Description(string tag, IndexableFacet.Category category) =>
        Say(tag, "preview.desc.category.", category);

    private static string Say(string tag, string prefix, IndexableFacet.Category category)
    {
        ArgumentNullException.ThrowIfNull(category);

        return Messages.Say(tag, prefix + Suffix(category.Key), ("value", category.Value));
    }

    /// <summary>
    /// The id fragment for a dimension.
    /// </summary>
    /// <remarks>
    /// Throws rather than falling back, so a dimension added to
    /// <see cref="IndexableFacet.Dimensions"/> without copy fails a test rather than shipping a page
    /// whose heading is a raw message id.
    /// </remarks>
    private static string Suffix(string key) => key switch
    {
        FacetKeys.Codebase => "codebase",
        FacetKeys.Lineage => "lineage",
        FacetKeys.Genre => "genre",
        FacetKeys.Language => "language",
        FacetKeys.Protocol => "protocol",
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "No category copy for this facet."),
    };
}
