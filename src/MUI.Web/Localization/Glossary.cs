namespace MUI.Web.Localization;

/// <summary>
/// The grammatical gender and number of the noun a provenance word describes.
/// </summary>
/// <remarks>
/// Metadata for the translator, not a fact about English. "Measured" is one string in English and
/// four in Russian, chosen by the gender/number of what it describes — and this site applies the
/// same word to a count, a game, a value, a capability, and a connect screen, so a single reused
/// string is guaranteed to be wrong somewhere.
/// </remarks>
public enum Subject
{
    /// <summary>The word modifies nothing — a bare column kicker.</summary>
    Standalone,

    Masculine,
    Feminine,
    Neuter,
    Plural,
}

/// <summary>One locked string: what it must go on meaning, and what it may not become.</summary>
/// <param name="Id">The context-keyed id. Never shared between two contexts.</param>
/// <param name="English">The source text, which is also the fallback.</param>
/// <param name="Subject">What the word agrees with, where it agrees with anything.</param>
/// <param name="Rationale">
/// Why this string is locked, shipped to translators as the brief rather than withheld.
/// </param>
public sealed record LockedString(string Id, string English, Subject Subject, string Rationale);

/// <summary>
/// The strings a translation may not paraphrase.
/// </summary>
/// <remarks>
/// Every row here exists because a plausible synonym in the target language would make the site
/// claim something it did not measure — e.g. "not measured", "uncounted", and "unreachable" are
/// distinct states a translation engine may collapse into stylistic variants of "unavailable".
/// Only maintainers may change an entry; contributors translate everything else freely. Where a
/// locale has no approved translation, the English shows rather than a smoothed-over guess — and
/// these are never machine-translated, since a blind reader has only the label to go on, with no
/// graphic to correct against.
/// </remarks>
public static class Glossary
{
    /// <summary>Every locked id, with its English and the reason it is locked.</summary>
    public static IReadOnlyList<LockedString> Locked { get; } =
    [
        // ── provenance, per context ──────────────────────────────────────────────────────────
        // One id per subject, because English collapses four Russian forms into one word. Never let
        // two contexts share an id just because the source language cannot tell them apart.
        new("provenance.count.measured", "measured", Subject.Feminine,
            "We connected and read this number ourselves. The strongest claim on the site; must not "
            + "weaken to 'verified' or 'confirmed', which imply a second party checked it."),
        new("provenance.game.measured", "measured", Subject.Neuter,
            "The same claim as provenance.count.measured, agreeing with the game rather than with the "
            + "count. Separate id because English collapses the two and inflected languages do not."),
        new("provenance.capability.measured", "measured", Subject.Plural,
            "The same claim again, agreeing with a set of capabilities — plural, where the two above are "
            + "singular. A shared id would be wrong here in every gendered language."),
        new("provenance.screen.measured", "measured", Subject.Masculine,
            "The same claim once more, agreeing with the connect screen. Four subjects, four ids: this is "
            + "the entry that shows why the id space is keyed by context and not by word."),
        new("kicker.measured", "measured", Subject.Standalone,
            "A bare column header, modifying nothing — which is why it needs an id of its own rather "
            + "than borrowing one that agrees with something."),

        new("provenance.count.declared", "declared", Subject.Feminine,
            "The game said so and we did not check. Must not become a synonym of measured: the whole "
            + "distinction is that this one is hearsay."),
        new("provenance.game.declared", "declared", Subject.Neuter,
            "The same hearsay claim as provenance.count.declared, agreeing with the game rather than with "
            + "the count. Separate id for the same reason its measured counterpart has one."),
        new("provenance.capability.declared", "declared", Subject.Plural,
            "The same claim again, agreeing with a set of capabilities — plural, where the two above are "
            + "singular. A shared id would be wrong here in every gendered language."),
        new("kicker.declared", "declared", Subject.Standalone,
            "The bare column header, modifying nothing — which in an inflected language is a different "
            + "form from any of the three above, and so needs an id of its own."),

        new("provenance.derived", "derived", Subject.Neuter,
            "We computed it from things we measured. Not observed directly, and not claimed by "
            + "anybody — the third state exists so neither of the other two has to stretch."),
        new("kicker.derived", "derived", Subject.Standalone,
            "The bare column header, modifying nothing. Same argument as the two kickers above."),

        // ── the four kinds of absence, which are four different facts ────────────────────────
        new("state.notMeasured", "not measured", Subject.Standalone,
            "We have not looked yet. Distinct from every kind of failure — must not become "
            + "'unavailable', which implies we tried and something refused."),
        new("state.uncounted", "uncounted", Subject.Standalone,
            "The game answered and we could not read a count. Never zero, never empty. The string "
            + "most likely to be mistranslated into 'no players', which is defamatory to a busy game."),
        new("state.unreachable", "unreachable", Subject.Standalone,
            "We tried and got nothing back. Must not imply the game is gone: the site keeps probing "
            + "for ever and games come back."),
        new("state.notCounted", "not counted", Subject.Standalone,
            "The listing cell's own wording for an unreadable count. Never rendered as 0."),

        // ── the words the whole product rests on ─────────────────────────────────────────────
        new("term.connected", "connected", Subject.Standalone,
            "A count of open connections, not of people. Must not become 'players', 'users' or "
            + "'online members' — one person with two clients is two connections and a bot is a "
            + "connection. The grammatically safe phrasing is also the more truthful one."),
        new("term.unclaimed", "unclaimed", Subject.Standalone,
            "No owner has taken over the record. Administrative, and not a judgement about the "
            + "game's quality or activity."),
        new("term.claimedByOwner", "claimed by its owner", Subject.Standalone,
            "Names the role, never the person, and assumes no gender for somebody real. The passive "
            + "is the point rather than a shortening."),
        new("term.stillProbed", "still probed", Subject.Standalone,
            "We keep trying even though it is dark. Preserves the promise that a game which comes "
            + "back will be noticed."),
        new("term.typical", "typical", Subject.Standalone,
            "A median over a stated window. A statistical term — must not collapse into 'average' "
            + "with the one below it."),
        new("term.peak", "peak", Subject.Standalone,
            "The highest count seen in a stated window. Must not collapse into 'average'."),

        // ── the two accessibility promises ───────────────────────────────────────────────────
        new("a11y.readAsText", "read as text", Subject.Standalone,
            "The label on every graphic's text alternative. Must read as an EQUIVALENT, not a "
            + "summary or a fallback: a reader choosing it should expect nothing to be missing."),
        new("a11y.plainText", "plain text", Subject.Standalone,
            "The ?plain=1 mirror. Must not become 'simple' or 'basic', which suggest a reduced "
            + "version — it is the complete page without the graphics."),
        new("a11y.skipToContent", "skip to content", Subject.Standalone,
            "The first thing a keyboard reader meets on every page."),
        new("a11y.asciiBanner", "ASCII banner: the connect screen of {game}.", Subject.Standalone,
            "The one-line alternative for a connect screen's artwork, and a blind reader's only "
            + "route into that block. It names the drawing's kind and the game, and never describes "
            + "the art: a paragraph about somebody else's ASCII would be our reading of their work "
            + "presented as a fact about their game. Keep {game} exactly as it is."),
    ];

    private static readonly Dictionary<string, LockedString> ById =
        Locked.ToDictionary(l => l.Id, StringComparer.Ordinal);

    /// <summary>Whether an id may only be changed by a maintainer.</summary>
    public static bool IsLocked(string id) => ById.ContainsKey(id);

    /// <summary>The locked entry for an id, or null.</summary>
    public static LockedString? Of(string id) => ById.GetValueOrDefault(id);

    /// <summary>
    /// The brief a translator is handed, rather than a spreadsheet of bare strings.
    /// </summary>
    /// <remarks>
    /// Generated from the same data the site renders, so the document a translator works from cannot
    /// drift from the strings that ship. The subject is stated because that is what an inflected
    /// language keys agreement off, and it is invisible in the English.
    /// </remarks>
    public static string Brief()
    {
        var b = new System.Text.StringBuilder();

        b.AppendLine("# The locked glossary");
        b.AppendLine();
        b.AppendLine("Every string below is locked. Translate it, and preserve what the note says it");
        b.AppendLine("means — a plausible synonym here makes the site claim something it did not");
        b.AppendLine("measure, which is the only kind of localization bug that damages the product.");
        b.AppendLine();
        b.AppendLine("Where you have no approved translation, leave it: the English will show, and a");
        b.AppendLine("reader who meets one English phrase learns something true.");
        b.AppendLine();

        foreach (var entry in Locked)
        {
            b.AppendLine($"## {entry.Id}");
            b.AppendLine();
            b.AppendLine($"    {entry.English}");
            b.AppendLine();

            if (entry.Subject is not Subject.Standalone)
            {
                b.AppendLine($"Agrees with: {entry.Subject.ToString().ToLowerInvariant()}.");
                b.AppendLine();
            }

            b.AppendLine(entry.Rationale);
            b.AppendLine();
        }

        return b.ToString();
    }
}
