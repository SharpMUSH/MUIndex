using System.Diagnostics.CodeAnalysis;

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
/// <b>Silence over a guess.</b> The map is what the hobby's own history plainly supports — TinyMUD's
/// three surviving branches, the two big MUD lineages, AberMUD — plus the codebases whose own
/// operators place them, which is a citation rather than a resemblance. Anything else renders as
/// unclassified. An invented parent would be indistinguishable, on the page, from a measured one.
/// </para>
/// <para>
/// <b>Where the less famous entries come from.</b> The 19 codebases in
/// <c>docs/codebase-survey-2026-07-30.md</c> that this map did not place were re-probed on
/// 2026-08-15, and seven of them answer the question themselves — see the survey's
/// <em>What a codebase says about its own ancestry</em> section for the transcript. <c>EmlenMud</c>,
/// <c>NarutoMUD Engine</c> and <c>LastOutpost</c> publish <c>FAMILY DikuMUD</c> (Last Outpost's
/// connect screen says "Last Outpost DikuMUD" as well); <c>Midnight Sun</c>, <c>CD</c> and
/// <c>Epiphany</c> publish <c>FAMILY LPMud</c> (Epiphany's screen adds "LPmud version : FluffOS
/// v2.26"). Six more publish <c>FAMILY Custom</c> — Evennia, Riftforge, Enrym, EternityMUD, IME and
/// LoFP — which is the same answer this class was already giving them, arrived at independently, and
/// CoffeeMUD answers <c>FAMILY CoffeeMUD</c>. Six offer no MSSP at all and stay unplaced: Ew, GWM,
/// Nightmare III, NakedMud, Dark City, Mindcloud3.
/// </para>
/// <para>
/// <b>The mudlibs, and the rest of Diku.</b> A second pass on 2026-08-16 read the <c>FAMILY</c> we
/// already hold for every codebase this map still did not place — the same citation as above, and
/// it needed no new probe. Eleven publish <c>FAMILY LPMud</c>: TMI-2, Dead Souls, Discworld lib,
/// UNIlib, 3Scapes mudlib, TD-MUDLIB, MorgenGrauen, Aldebaran, RoleMUD, Moral Decay and PD/NM III,
/// which are mostly mudlibs rather than drivers — and a mudlib and the driver beneath it are
/// different software in one lineage, which is the question this facet asks. Five publish
/// <c>FAMILY DikuMUD</c>: PizzaMUD, Galaxy Engine, EmpireMUD, JediMUD and MUME.
/// </para>
/// <para>
/// <b>What that pass deliberately left alone.</b> Legends of the Jedi, Materia Magica, Alter Aeon,
/// Rapture, EternityMUD, IME and TeenyMUSH all publish <c>FAMILY Custom</c>, and an ancestry anyone
/// could name from the outside does not outrank a game's own answer about itself; placing them
/// would be resemblance overruling a declaration, which is the one move this map does not make.
/// TinyBit and Haven publish no MSSP at all. Northern Crossroads publishes <c>FAMILY DikuMUD</c>
/// and stays unplaced anyway, because its <c>CODEBASE</c> is <c>NC-7.0.278.89f11404</c> and the
/// only key that reaches it is <c>NC</c> — two letters, short enough to turn up inside somebody
/// else's name later. That is a limit of the key, not a doubt about the game.
/// </para>
/// <para>
/// <b>That evidence is read once, by a person, and never at runtime.</b> A declaration is a fine
/// reason to write a row here and an unacceptable way to fill the facet: <see cref="Of"/> reading
/// MSSP <c>FAMILY</c> live would put a game wherever its own <c>mush.cnf</c> said, mix our
/// classification with their assertion in one column, and leave no way to tell which a given row
/// was. The map is compiled in, so every entry has been looked at.
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

            // Placed by their own MSSP FAMILY, re-probed 2026-08-15. See the class remarks.
            ["EmlenMud"] = Diku,
            ["NarutoMUD"] = Diku,
            ["LastOutpost"] = Diku,

            // Placed by their own MSSP FAMILY, read 2026-08-16. See "The mudlibs and the rest of
            // Diku" in the class remarks.
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

            // Likewise placed by their own declaration rather than by resemblance.
            ["Midnight Sun"] = Lp,
            ["CD"] = Lp,
            ["Epiphany"] = Lp,

            // The mudlibs, each publishing FAMILY LPMud. A driver and the mudlib on top of it are
            // different software and the same lineage, which is the question this facet asks.
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
    /// <para>
    /// The fold only removes a trailing version token, so a codebase that spells itself any other way
    /// keeps a version in its key and misses the map. Live, that is not hypothetical: <c>CD.06.06</c>
    /// puts its version behind a dot, and <c>Epiphany v1.2.15 [development]</c> puts a bracketed
    /// qualifier after it — two of the nineteen codebases surveyed, both of which publish an ancestry
    /// this map knows.
    /// </para>
    /// <para>
    /// So a miss falls back to reading the words the game wrote. <c>Diku Merc Rom RoT AoD</c> is the
    /// case that makes the rule obviously right rather than merely convenient: the string is a
    /// recital of its own descent, and every name in it is one this map already places.
    /// </para>
    /// <para>
    /// <b>Whole words, and unanimity, or nothing.</b> Whole words because <c>ROM</c> must not be found
    /// inside <c>ROMulus</c> — the same boundary the fold is careful about. Unanimity because a string
    /// naming two lineages is a game telling us something we cannot act on: picking either would be
    /// choosing on the reader's behalf and recording the choice as a fact about their game.
    /// </para>
    /// <para>
    /// <b>A word may end in its own version.</b> <c>ROM2.4/Haven</c> splits to <c>ROM2</c>, which no
    /// key matches, so a game whose codebase string plainly recites <c>ROM</c> was going unplaced on
    /// a space its author did not type. Trailing digits therefore come off before the lookup, which
    /// is the boundary <c>LoginCommandReading.NamesFamily</c> already applies one rung up and for a
    /// higher-stakes decision: <c>ROM24</c> and <c>CircleMUD3</c> are how these are written in the
    /// wild, and a letter after the marker still disqualifies it. The cost is real and small — a
    /// token like <c>ROT13</c> would be read as <c>ROT</c> — and it is bounded by everything else
    /// here: only the family string is searched, the match has to be a whole token, and a second
    /// lineage anywhere in the string discards both.
    /// </para>
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
