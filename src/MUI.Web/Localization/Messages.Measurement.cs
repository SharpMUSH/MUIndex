namespace MUI.Web.Localization;

public static partial class Messages
{
    /// <summary>
    /// WHAT WE COULD MEASURE — the two facet groups over the two reasons a listing row carries no
    /// number.
    /// </summary>
    /// <remarks>Every string here describes our reach, never the game (rule 5).</remarks>
    private static Dictionary<string, string> Measurement() => new(StringComparer.Ordinal)
    {
        // ══ WHAT WE COULD MEASURE ═════════════════════════════════════════════════════════════
        // Two switches over the two reasons a listing row carries no number. Every string here
        // describes our reach, never the game (rule 5).
        ["facet.group.measure"] = "what we could measure",

        ["facet.measure.note"] = "Hiding these takes them out of your listing; it does not mean the "
            + "game is empty.",

        // Named for what we did, never what the game is — "could not count" not "no players".
        ["facet.group.uncounted"] = "could not count",
        ["facet.group.unreachable"] = "could not reach",

        ["facet.excluded.uncounted"] = "hidden from this listing",
        ["facet.excluded.unreachable"] = "hidden from this listing",

        ["facet.plain.marks"] = "In the left column, * is a value this listing is filtered to and - "
            + "is one it is filtered against. Both are choices in the query, not facts about a game.",
    };
}
