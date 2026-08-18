using MUI.Catalog;

using MUI.Web.Localization;

namespace MUI.Web.Components;

/// <summary>
/// How we know a value, in one word — and what carried it to us, in one name.
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
    public static string How(string tag, ProvenanceChip chip)
    {
        ArgumentNullException.ThrowIfNull(tag);
        ArgumentNullException.ThrowIfNull(chip);

        return Messages.For(tag, chip switch
        {
            { IsMeasured: true } => "provenance.game.measured",
            { Source: FieldSource.Owner } => "provenance.game.ownerDeclared",
            _ => "provenance.game.declared",
        });
    }

    /// <summary>
    /// What carried the value to us, as a reader should see it written.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A display name is not an enum member, and the difference reached a reader.</b> The chip's
    /// tooltip interpolated <see cref="FieldSource"/> directly and said a value arrived "via Mssp":
    /// an acronym mis-cased by C#'s naming convention, in a sentence no translator could reach,
    /// because the word was never in a file they are sent. Upper-casing it at the call site would
    /// have fixed the four letters and left both problems — the next member added is mis-cased
    /// again, and the ones that are not acronyms are still English hard-coded into markup.
    /// </para>
    /// <para>
    /// Which half a member falls in is the same line the message bundle draws everywhere else.
    /// <c>MSSP</c>, <c>WHO</c>, <c>INFO</c> and <c>I3</c> are protocol and command names — machine
    /// voice, identical in every locale, and translating one destroys evidence. The handshake, the
    /// connect screen, the owner and this project's staff are ours to say, so they are said in the
    /// reader's language.
    /// </para>
    /// <para>
    /// The switch is exhaustive and throws rather than falling back to <c>ToString</c>, because a
    /// silent fallback is precisely how <c>Mssp</c> reached a page in the first place.
    /// </para>
    /// </remarks>
    public static string Via(string tag, FieldSource source)
    {
        ArgumentNullException.ThrowIfNull(tag);

        return Messages.For(tag, source switch
        {
            FieldSource.Staff => "source.staff",
            FieldSource.Handshake => "source.handshake",
            FieldSource.Owner => "source.owner",
            FieldSource.Who => "source.who",
            FieldSource.I3 => "source.i3",
            FieldSource.Mssp => "source.mssp",
            FieldSource.Info => "source.info",
            FieldSource.I3Mudlist => "source.i3Mudlist",
            FieldSource.Banner => "source.banner",
            _ => throw new ArgumentOutOfRangeException(
                nameof(source),
                source,
                "No display name for this field source. Add one rather than letting ToString answer."),
        });
    }
}
