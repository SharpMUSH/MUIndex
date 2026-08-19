using MUI.Catalog;
using MUI.Web.Localization;

namespace MUI.Web.Reference;

/// <summary>
/// The rows every client page carries, in the order it carries them.
/// </summary>
/// <remarks>
/// The vocabulary is fixed here rather than per file so every client page is comparable. Screen
/// reader support is always the first row (spec §9): an author who found nothing must produce a
/// visible <em>unknown</em>, not a silently missing row. Protocol names match
/// <see cref="MUI.Catalog.Persistence.CapabilityFields"/> so a claimed and a measured capability use
/// the same word, even though the two are never mixed in one table.
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
    /// The word for a state, in the reader's language. <em>Unknown</em> is spelled out rather than
    /// left blank, since a blank cell reads as a no.
    /// </summary>
    /// <remarks>
    /// Uses this table's own message ids rather than the game pages' capability words: those answer
    /// "was it offered on a wire", these answer "does the documentation say so" — collapsing the two
    /// would publish a missed documentation search as the client lacking the feature.
    /// </remarks>
    public static string Word(string tag, CapabilityState state) => Messages.For(tag, state switch
    {
        CapabilityState.Present => "reference.capability.yes",
        CapabilityState.Absent => "reference.capability.no",
        _ => "reference.capability.unknown",
    });
}
