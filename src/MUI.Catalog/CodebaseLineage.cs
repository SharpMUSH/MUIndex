namespace MUI.Catalog;

/// <summary>
/// The server lineage a codebase family descends from — <c>MUSH</c> for PennMUSH, TinyMUX and
/// RhostMUSH alike (spec §9).
/// </summary>
/// <remarks>
/// <para>
/// <b>This is ours, and it is the only fact on the site that is.</b> Every other facet is either
/// something we watched happen or something a game published; this is a classification we apply to
/// a codebase name, and it is labelled <see cref="FacetEvidence.Derived"/> on every surface for that
/// reason. A reader who cannot tell our editorial grouping from a game's own words is being misled
/// by the mechanism this project exists to replace.
/// </para>
/// <para>
/// <b>It is not MSSP's <c>FAMILY</c>, and it exists because that variable cannot answer the
/// question.</b> MSSP's own vocabulary has no <c>MUSH</c> in it — a probe of
/// <c>mush.pennmush.org:4201</c> answers <c>CODEBASE PennMUSH 1.8.8p0</c> and
/// <c>FAMILY TinyMUD</c> — and the rest of the MUSH world does not answer at all: AresMUSH, TinyMUX,
/// MUCK, RhostMUSH, CobraMUSH and TinyMUSH offer no MSSP whatsoever
/// (<c>docs/codebase-survey-2026-07-30.md</c>). So "the games in the MUSH family" was a question
/// with no declared answer for all but a handful of the games it is about, which is what a derived
/// facet is for. The declared <c>family</c> facet stays exactly as it is beside this one: when a
/// game says <c>TinyMUD</c> and we say <c>MUSH</c>, both are shown, because that is rule 1.
/// </para>
/// <para>
/// <b>Derived from the codebase and never from the declared family.</b> Reading MSSP's
/// <c>FAMILY</c> as a fallback would let one game's config line put it in a lineage its software is
/// not in, and would make this facet a mixture of our classification and their assertion with no way
/// to tell which a given row was. A game whose codebase we have not identified has no lineage.
/// </para>
/// <para>
/// <b>Silence over a guess.</b> The map is only what the hobby's own history plainly supports —
/// TinyMUD's three surviving branches, the two big MUD lineages, and AberMUD. Codebases with no
/// uncontested parent (Evennia, written from nothing in Python; CoffeeMUD, Diku-<em>inspired</em>
/// and independently written) are deliberately absent and render as unclassified. An invented parent
/// would be indistinguishable, on the page, from a measured one.
/// </para>
/// </remarks>
public static class CodebaseLineage
{
    /// <summary>TinyMUD's MUSH branch — the servers this site's audience mostly runs.</summary>
    public const string Mush = "MUSH";

    /// <summary>TinyMUD's MUCK branch.</summary>
    public const string Muck = "MUCK";

    /// <summary>TinyMUD's MOO branch.</summary>
    public const string Moo = "MOO";

    /// <summary>Diku and everything derived from it — Merc, ROM, SMAUG, Circle and their children.</summary>
    public const string Diku = "DikuMUD";

    /// <summary>LPMud and the drivers and mudlibs that carried it forward.</summary>
    public const string Lp = "LPMud";

    /// <summary>AberMUD, which fathered neither of the other two.</summary>
    public const string Aber = "AberMUD";

    /// <summary>
    /// Codebase family to lineage, keyed by the family <see cref="CodebaseFamily.Of"/> produces.
    /// </summary>
    /// <remarks>
    /// Keyed on the <em>family</em> rather than on the raw <c>CODEBASE</c> string so a new patchlevel
    /// never needs a row here: <c>PennMUSH 1.8.9</c> ships and it is already classified. The
    /// consequence is that a codebase whose version cannot be folded off keeps its version in the key
    /// and falls out unclassified, which is the safe direction to fail in.
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, string> Lineages =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["TinyMUSH"] = Mush,
            ["PennMUSH"] = Mush,
            ["TinyMUX"] = Mush,
            ["MUX"] = Mush,
            ["RhostMUSH"] = Mush,
            ["Rhost"] = Mush,
            ["CobraMUSH"] = Mush,
            ["AresMUSH"] = Mush,

            ["TinyMUCK"] = Muck,
            ["MUCK"] = Muck,
            ["Fuzzball"] = Muck,
            ["ProtoMUCK"] = Muck,
            ["GlowMUCK"] = Muck,

            ["MOO"] = Moo,
            ["LambdaMOO"] = Moo,
            ["Stunt"] = Moo,
            ["ToastStunt"] = Moo,

            ["Diku"] = Diku,
            ["DikuMUD"] = Diku,
            ["Merc"] = Diku,
            ["ROM"] = Diku,
            ["ROT"] = Diku,
            ["SMAUG"] = Diku,
            ["CircleMUD"] = Diku,
            ["tbaMUD"] = Diku,
            ["LuminariMUD"] = Diku,
            ["Envy"] = Diku,
            ["GodWars"] = Diku,
            ["Anatolia"] = Diku,

            ["LPMud"] = Lp,
            ["MudOS"] = Lp,
            ["FluffOS"] = Lp,
            ["LDMud"] = Lp,
            ["DGD"] = Lp,

            ["AberMUD"] = Aber,
        };

    /// <summary>
    /// The lineage a game's identified codebase belongs to, or null when we do not classify it.
    /// </summary>
    /// <remarks>
    /// Null covers two different situations on purpose — a codebase we could not read and a codebase
    /// we read and do not place — because neither is a fact about the game's ancestry and the facet
    /// spells both as "not classified". Splitting them would invite a reader to conclude that the
    /// second group descends from nothing.
    /// </remarks>
    public static string? Of(string? codebase) =>
        CodebaseFamily.For(codebase) is { } family && Lineages.TryGetValue(family, out var lineage)
            ? lineage
            : null;

    /// <summary>Every lineage this classifies into, in the order the panel offers them.</summary>
    /// <remarks>
    /// A fixed vocabulary rather than one read off the data, so a lineage nothing in the catalogue
    /// matches yet still has a stable place in the list rather than appearing and disappearing as the
    /// crawl reaches new games.
    /// </remarks>
    public static IReadOnlyList<string> All { get; } = [Mush, Muck, Moo, Diku, Lp, Aber];
}
