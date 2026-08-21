using System.Diagnostics.CodeAnalysis;

namespace MUI.Catalog;

/// <summary>
/// The server lineage a codebase family descends from — <c>MUSH</c> for PennMUSH, TinyMUX and
/// RhostMUSH alike (spec §9).
/// </summary>
/// <remarks>
/// This is editorial, not measured — a classification we apply to a codebase name, so it is always
/// labelled <see cref="FacetEvidence.Derived"/>. It is not MSSP's <c>FAMILY</c>: that vocabulary has
/// no <c>MUSH</c> in it, and most MUSH-family codebases publish no MSSP at all, so <c>FAMILY</c>
/// cannot answer this question. Lineage is derived only from the identified codebase, never from a
/// game's declared <c>family</c> facet — mixing the two would make this one column part our
/// classification and part their assertion with no way to tell which. An unplaced codebase renders as
/// unclassified rather than guessed; see <c>docs/codebase-survey-2026-07-30.md</c> for how entries
/// were placed. The map is compiled in and read once, by a person — <see cref="Of"/> never reads MSSP
/// <c>FAMILY</c> live, which would let one game's config line dictate its own lineage.
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
    /// never needs a row here: <c>PennMUSH 1.8.9</c> ships and it is already classified. A codebase
    /// that spells its version some way the fold does not remove keeps it in the key and misses this
    /// map entirely, which is what <see cref="NamedIn"/> is for.
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

            // Placed by each codebase's own declared FAMILY, not by resemblance.
            ["EmlenMud"] = Diku,
            ["NarutoMUD"] = Diku,
            ["LastOutpost"] = Diku,

            ["PizzaMUD"] = Diku,
            ["Galaxy Engine"] = Diku,
            ["EmpireMUD"] = Diku,
            ["JediMUD"] = Diku,
            ["MUME"] = Diku,

            ["LPMud"] = Lp,
            ["MudOS"] = Lp,
            ["FluffOS"] = Lp,
            ["LDMud"] = Lp,
            ["DGD"] = Lp,

            ["Midnight Sun"] = Lp,
            ["CD"] = Lp,
            ["Epiphany"] = Lp,

            // The mudlibs; a driver and the mudlib on top of it are different software, same lineage.
            ["TMI-2"] = Lp,
            ["Dead Souls"] = Lp,
            ["Discworld lib"] = Lp,
            ["UNIlib"] = Lp,
            ["3Scapes mudlib"] = Lp,
            ["TD-MUDLIB"] = Lp,
            ["MorgenGrauen"] = Lp,
            ["Aldebaran"] = Lp,
            ["RoleMUD"] = Lp,
            ["Moral Decay"] = Lp,
            ["PD/NM III"] = Lp,

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
    public static string? Of(string? codebase)
    {
        if (CodebaseFamily.For(codebase) is not { } family)
        {
            return null;
        }

        return Lineages.TryGetValue(family, out var exact) ? exact : NamedIn(family);
    }

    /// <summary>
    /// The lineage a codebase string <em>names</em>, when folding it did not produce a key.
    /// </summary>
    /// <remarks>
    /// Falls back to reading words out of the codebase string when the fold-based key misses (e.g. a
    /// version tucked behind a dot or in brackets, as in <c>CD.06.06</c> or
    /// <c>Epiphany v1.2.15 [development]</c>). Matches whole words only — <c>ROM</c> must not match
    /// inside <c>ROMulus</c> — with trailing digits stripped first so <c>ROM24</c> or
    /// <c>CircleMUD3</c> still resolve. A string naming two different lineages returns null rather
    /// than picking one arbitrarily.
    /// </remarks>
    private static string? NamedIn(string codebase)
    {
        string? found = null;

        foreach (var word in codebase.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Named(word, out var lineage))
            {
                continue;
            }

            if (found is not null && !string.Equals(found, lineage, StringComparison.Ordinal))
            {
                return null;
            }

            found = lineage;
        }

        return found;
    }

    /// <summary>The lineage a single word names, with a version fused to its end allowed for.</summary>
    private static bool Named(string word, [NotNullWhen(true)] out string? lineage)
    {
        if (Lineages.TryGetValue(word, out lineage))
        {
            return true;
        }

        var name = word.TrimEnd(Digits);

        return name.Length > 0 && Lineages.TryGetValue(name, out lineage);
    }

    /// <summary>
    /// What counts as a word boundary in a codebase string. Everything that is not a letter or a
    /// digit, because <c>CD.06.06</c>, <c>ROM-2.4</c> and <c>Epiphany v1.2.15 [development]</c> are
    /// all one game writing its name next to its version with whatever punctuation came to hand.
    /// </summary>
    private static readonly char[] Separators =
        [.. Enumerable.Range(0, 128).Select(c => (char)c).Where(c => !char.IsAsciiLetterOrDigit(c))];

    private static readonly char[] Digits = [.. Enumerable.Range('0', 10).Select(c => (char)c)];

    /// <summary>Every lineage this classifies into, in the order the panel offers them.</summary>
    /// <remarks>
    /// A fixed vocabulary rather than one read off the data, so a lineage nothing in the catalogue
    /// matches yet still has a stable place in the list rather than appearing and disappearing as the
    /// crawl reaches new games.
    /// </remarks>
    public static IReadOnlyList<string> All { get; } = [Mush, Muck, Moo, Diku, Lp, Aber];
}
