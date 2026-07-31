using MUI.Catalog;

namespace MUI.Web.Reference;

/// <summary>
/// The rows every client page carries, in the order it carries them.
/// </summary>
/// <remarks>
/// <para>
/// <b>The vocabulary is fixed here rather than per file</b> so that ten client pages are one table a
/// reader can read across. A matrix whose rows are whatever each author happened to establish is a
/// set of unrelated lists, and the comparison is the only reason to publish it.
/// </para>
/// <para>
/// <b>Screen-reader support is the first row, and it is present on every page whether or not anyone
/// established it.</b> Spec §9 names it specifically because no incumbent directory publishes it:
/// for a substantial part of this hobby's audience it is the question, and it is currently answered
/// by asking on a forum. Fixing it at the head of the table means a page cannot omit the row by
/// saying nothing — an author who found nothing produces a visible <em>unknown</em>, which is a
/// truthful and useful answer, and a reader can tell the difference between that and a no.
/// </para>
/// <para>
/// The protocol rows are spelled the way <see cref="MUI.Catalog.Persistence.CapabilityFields"/>
/// spells them, so a client's row and a game's measured capability are the same word. What a client
/// claims and what a server was observed doing are different kinds of fact — the first is read off
/// documentation, the second off a wire — and they are never mixed in one table; but they should at
/// least be answering the same question.
/// </para>
/// </remarks>
public static class ClientCapabilities
{
    public const string ScreenReader = "screen reader";

    public const string Scripting = "scripting";

    public static IReadOnlyList<string> Rows { get; } =
    [
        ScreenReader,
        "TLS",
        "UTF-8",
        "MCCP",
        "GMCP",
        "MSDP",
        "ATCP",
        "MXP",
        "MSP",
        Scripting,
    ];

    /// <summary>
    /// The document's claims, in <see cref="Rows"/> order, with anything it did not mention filled in
    /// as unknown — because a missing row and an unknown row are the same fact and only one of them
    /// is legible.
    /// </summary>
    public static IReadOnlyList<CapabilityClaim> For(ReferenceDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return
        [
            .. Rows.Select(row => document.Capabilities
                .FirstOrDefault(c => string.Equals(c.Name, row, StringComparison.OrdinalIgnoreCase))
                ?? new CapabilityClaim(row, CapabilityState.Unknown, null)),
        ];
    }

    /// <summary>
    /// The word for a state. Never a glyph on its own, and <em>unknown</em> is spelled out rather
    /// than left blank: a blank cell reads as a no to a human exactly as it does to a parser.
    /// </summary>
    public static string Word(CapabilityState state) => state switch
    {
        CapabilityState.Present => "yes",
        CapabilityState.Absent => "no",
        _ => "unknown",
    };
}
