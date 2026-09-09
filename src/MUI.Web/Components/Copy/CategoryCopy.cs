using MUI.Catalog;
using MUI.Web.Localization;

namespace MUI.Web.Components;

/// <summary>
/// What a faceted listing calls itself once it is a page of its own.
/// </summary>
/// <remarks>
/// A heading, a title and a description per dimension (see <see cref="IndexableFacet"/>). Each
/// description names the kind of statement its facet reads: a protocol was watched on the wire, a
/// genre and a language are the game's own claims, a lineage is a classification of ours. One
/// sentence over all five would report a measurement for the three that never made one (rules 1, 5).
/// The value is machine voice and passed as an argument rather than translated.
/// </remarks>
public static class CategoryCopy
{
    /// <summary>The document's first line.</summary>
    public static string Heading(string tag, IndexableFacet.Category category) =>
        Say(tag, "games.heading.", category);

    /// <summary>The browser-tab noun phrase.</summary>
    /// <remarks>A separate id from <see cref="Heading"/> for the reason <c>PreviewCopy.Titles</c> gives.</remarks>
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

    /// <summary>The id fragment for a dimension.</summary>
    /// <remarks>Throws rather than falling back, so a dimension added without copy fails a test
    /// rather than shipping a page whose heading is a raw message id.</remarks>
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
