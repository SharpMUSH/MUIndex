using MUI.Catalog;

using MUI.Web.Localization;

namespace MUI.Web.Components;

/// <summary>
/// How we know a value, in one word — and what carried it to us, in one name.
/// </summary>
/// <remarks>
/// Three words, not two: "declared" alone would put what an operator typed into our form and what
/// their own config file emits under one word, and those are different claims by different people.
/// Centralized here since every call site (listing, game page, archive, plain mode, preview
/// metadata) must apply the same rule.
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
    /// A display name is not an enum member — never interpolate <see cref="FieldSource"/> directly
    /// into a sentence, since C#'s naming convention mis-cases acronyms and the result can't be
    /// translated. <c>MSSP</c>, <c>WHO</c>, <c>INFO</c> and <c>I3</c> are protocol names and stay as
    /// machine voice in every locale; the rest are ours to say and go through the message bundle.
    /// The switch throws rather than falling back to <c>ToString</c>, so a missing case fails loudly
    /// instead of leaking an enum name onto a page.
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
            FieldSource.AresCentral => "source.aresCentral",
            FieldSource.I3Mudlist => "source.i3Mudlist",
            FieldSource.Banner => "source.banner",
            _ => throw new ArgumentOutOfRangeException(
                nameof(source),
                source,
                "No display name for this field source. Add one rather than letting ToString answer."),
        });
    }
}
