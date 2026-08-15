using MUI.Catalog;

namespace MUI.Web.Components;

/// <summary>
/// How we know a value, in one word.
/// </summary>
/// <remarks>
/// <para>
/// <b>Three words and not two.</b> "Declared" alone would put what a game's operator typed into our
/// form and what its own config file emits under one word, and those are different claims by
/// different people.
/// </para>
/// <para>
/// It lives here rather than beside any one caller because the count of callers keeps going up:
/// the listing's chips, the game page's, the archive's, plain mode's, and now the preview metadata
/// a chat client renders when somebody pastes a link. <c>PlainText</c>'s own copy carried a comment
/// observing that a rule spelled at one of four call sites is a rule the other three break — and
/// then a fifth surface arrived that could not reach the spelling at all, because it was private.
/// </para>
/// </remarks>
public static class Provenance
{
    /// <summary>Measured, owner-declared, or declared — never two of those collapsed into one.</summary>
    public static string How(ProvenanceChip chip)
    {
        ArgumentNullException.ThrowIfNull(chip);

        return chip switch
        {
            { IsMeasured: true } => "measured",
            { Source: FieldSource.Owner } => "owner-declared",
            _ => "declared",
        };
    }
}
