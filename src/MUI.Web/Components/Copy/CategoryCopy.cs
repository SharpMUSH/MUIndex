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

    /// <summary>
    /// Whether this category says anything of its own beyond its heading.
    /// </summary>
    /// <remarks>
    /// An unstable facet takes the listing's plain title and description: it is not being indexed,
    /// and a title naming a set that has already changed is worse than the general one. The heading
    /// still names it, because the reader is looking at the page now.
    /// </remarks>
    private static bool Describes(IndexableFacet.Category category) => category.IsStable;

    /// <summary>The browser-tab noun phrase.</summary>
    /// <remarks>A separate id from <see cref="Heading"/> for the reason <c>PreviewCopy.Titles</c> gives.</remarks>
    public static string Title(string tag, IndexableFacet.Category category) =>
        Describes(category)
            ? Say(tag, "preview.title.category.", category)
            : PreviewCopy.Titles.Games(tag);

    /// <summary>What this listing is, for a search result and for a link somebody pasted.</summary>
    public static string Description(string tag, IndexableFacet.Category category) =>
        Describes(category)
            ? Say(tag, "preview.desc.category.", category)
            : PreviewCopy.Pages.Games(tag);

    private static string Say(string tag, string prefix, IndexableFacet.Category category)
    {
        ArgumentNullException.ThrowIfNull(category);

        // The band's five values each name a state rather than a thing, so each gets its own line
        // rather than being dropped into one frame — the panel's label for "quiet" carries a
        // definition after an em dash, which reads as a footnote in a heading.
        var id = category.Key == FacetKeys.Band
            ? prefix + "band." + category.Value
            : prefix + Suffix(category.Key);

        return Messages.Say(tag, id, ("value", Value(tag, category)));
    }

    /// <summary>
    /// The value as the sentence needs it.
    /// </summary>
    /// <remarks>
    /// Most facet values are machine voice — a codebase name, a protocol acronym, an encoding — and
    /// pass through untranslated. The two switches carry a catalogue token instead (<c>yes</c>,
    /// <c>playersNow</c>), which is not a word in any language, so each is put through the same
    /// vocabulary the facet panel draws it with.
    /// </remarks>
    private static string Value(string tag, IndexableFacet.Category category) => category.Key switch
    {
        FacetKeys.Tls => Messages.For(tag, "facet.tls.yes"),
        _ => category.Value,
    };

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
        FacetKeys.Charset => "charset",
        FacetKeys.Tls => "tls",
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "No category copy for this facet."),
    };
}
