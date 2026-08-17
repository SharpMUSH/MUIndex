using System.Globalization;

using MUI.Catalog;
using MUI.Web.Localization;

namespace MUI.Web.Components;

/// <summary>
/// The words the facets are shown in — on the rendered panel and in plain text alike.
/// </summary>
/// <remarks>
/// <para>
/// Wording lives here rather than beside the query because <c>MUI.Catalog</c> is UI-agnostic and a
/// facet's <em>name</em> is not its label: <c>seen</c> is a querystring parameter and "last seen" is
/// a phrase in English. It lives in one place rather than two because the graphical panel and the
/// plain surface are the same facts with different renderers, and a value called one thing in a
/// <c>&lt;select&gt;</c> and another in an 80-column list is two vocabularies again.
/// </para>
/// <para>
/// <see cref="Unknown"/> is the load-bearing one. Every facet spells its own absence, and none of
/// them spells it as a <em>no</em> — "not identified" is a fact about our reach, "not declared" is a
/// fact about what a game published, and neither is a fact about the game lacking the thing.
/// </para>
/// </remarks>
public static class FacetWords
{
    /// <summary>What a facet is called on the page.</summary>
    public static string Group(string tag, string key) => Messages.For(tag, key switch
    {
        FacetKeys.Band => "facet.group.band",
        FacetKeys.LastSeen => "facet.group.seen",
        FacetKeys.Protocol => "facet.group.protocol",
        FacetKeys.Tls => "facet.group.tls",

        // "encoding", not "encoding negotiated": the evidence chip beside it already says measured,
        // and the two words together wrapped the label and knocked its control out of line with the
        // rest of the row. What negotiation has to do with it is in the values — "nothing negotiated"
        // is what this facet calls a game it has no answer for.
        FacetKeys.Charset => "facet.group.charset",
        FacetKeys.Codebase => "facet.group.codebase",

        // "version" alone, because it sits directly under the codebase it is a version of and the
        // pair reads as one column. "codebase version" repeated the word the row above it already
        // said and wrapped the label onto two lines for the sake of it.
        FacetKeys.CodebaseVersion => "facet.group.version",
        FacetKeys.Lineage => "facet.group.lineage",
        FacetKeys.Family => "facet.group.family",
        FacetKeys.Genre => "facet.group.genre",
        FacetKeys.Language => "facet.group.language",

        // The three the panel has no group for. They are things the query asks that no facet
        // answers back — a typed name, and the two widenings — and they reach a reader only as
        // chips. Named here rather than in the chip builder so there is one place a facet is put
        // into words, and so the fallback below stays what it is for: a key nobody has named yet.
        FacetKeys.Text => "facet.group.search",
        FacetKeys.Archived => "facet.group.archived",
        FacetKeys.Adult => "facet.group.adult",
        _ => key,
    });

    /// <summary>
    /// What kind of statement a facet is, in one word.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A word and never only a symbol. The difference between something we watched happen and
    /// something a game typed into <c>mush.cnf</c> in 2017 is the product, and a glyph a reader has
    /// to learn is a difference they will not read.
    /// </para>
    /// <para>
    /// One word rather than the sentence it used to be. "codebase — the game says so" on every row
    /// spends a line of prose per facet saying the same two things over and over, and a panel that
    /// explains itself seven times is a panel nobody reads once. The sentence still exists — see
    /// <see cref="EvidenceMeaning"/> — and is said once, beside the two words, where it reads as a
    /// key rather than as commentary.
    /// </para>
    /// </remarks>
    public static string Evidence(string tag, FacetEvidence evidence) => Messages.For(tag, evidence switch
    {
        FacetEvidence.Measured => "kicker.measured",
        FacetEvidence.Derived => "kicker.derived",
        _ => "kicker.declared",
    });

    /// <summary>What each of those words means, said once per surface rather than once per facet.</summary>
    /// <remarks>
    /// <see cref="FacetEvidence.Derived"/> names us out loud — "we grouped", not "is grouped". The
    /// other two sentences have somebody in them (we watched; the game says) and a passive third
    /// would be the only fact on the site whose author had gone missing, which is precisely the one
    /// where it matters.
    /// </remarks>
    public static string EvidenceMeaning(string tag, FacetEvidence evidence) => Messages.For(tag, evidence switch
    {
        FacetEvidence.Measured => "evidence.measured.meaning",
        FacetEvidence.Derived => "evidence.derived.meaning",
        _ => "evidence.declared.meaning",
    });

    /// <summary>What each sort order is called on the control.</summary>
    /// <remarks>
    /// Named for the fact each one reads, never for a superlative. "Players on now" is what the
    /// column says and what the sort does; "busiest" is <c>/rankings</c>'s word for a median over a
    /// window with a sample floor under it, and lending it to one instantaneous count would be two
    /// different measurements answering to one name on the same site. The window sorts name their
    /// statistic <em>and</em> their span for the same reason — "typically on" alone would be three
    /// different orders wearing one label.
    /// <para>
    /// "Typically on" rather than "median players on". The statistic is a median and is called one
    /// everywhere it is documented, but the control is read by people looking for a game to play and
    /// the word for what they want is <em>typical</em>. The row beside it prints the number under the
    /// word "median", so nothing is hidden by the plainer label.
    /// </para>
    /// </remarks>
    public static string Sort(string tag, GameSort sort) => Messages.For(tag, sort switch
    {
        GameSort.Players => "sort.players",
        GameSort.Reached => "sort.reached",
        GameSort.MedianWeek => "sort.medianWeek",
        GameSort.MedianMonth => "sort.medianMonth",
        GameSort.MedianQuarter => "sort.medianQuarter",
        GameSort.PeakWeek => "sort.peakWeek",
        GameSort.PeakMonth => "sort.peakMonth",
        GameSort.PeakQuarter => "sort.peakQuarter",
        _ => "sort.name",
    });

    /// <summary>
    /// Which group of the sort control an order belongs in.
    /// </summary>
    /// <remarks>
    /// Nine options in one flat list is a list nobody reads to the bottom of. The grouping is by what
    /// each order reads — a fact on the row, or a statistic over a span — which is also the
    /// difference a reader most needs to see before choosing one.
    /// </remarks>
    public static string SortGroup(string tag, GameSort sort) => Messages.For(tag,
        SortWindows.Of(sort) is null ? "sort.group.row"

        // Two words, because each option under them already names its own window: "typically on ·
        // 7 days" under a heading reading "typical over a window" said the window twice and the
        // word once.
        : SortWindows.IsMedian(sort) ? "sort.group.typical" : "sort.group.peak");

    /// <summary>
    /// How a window figure is spelled where it is shown beside the row it ranked.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The sample count is part of the sentence and not an optional extra.</b> A median is a
    /// median of something, and this site does not publish a figure whose basis it has hidden
    /// (§15.7). It is also the only thing on the row that distinguishes a game measured three hundred
    /// times from one found on Friday and probed thirty.
    /// </para>
    /// <para>
    /// The word here is "median" even though the control says "typically on". The control is a
    /// question a reader is choosing between and the row is the answer's basis — a number labelled
    /// with the statistic it is, so anybody who wants to know what "typical" was computed as can read
    /// it off the row rather than the documentation.
    /// </para>
    /// </remarks>
    public static string Window(string tag, PresenceWindow window, GameSort sort)
    {
        ArgumentNullException.ThrowIfNull(window);

        return Messages.For(
            tag,
            SortWindows.IsMedian(sort) ? "sort.window.median" : "sort.window.peak",
            new Dictionary<string, object?>
            {
                ["value"] = SortWindows.IsMedian(sort)
                    ? window.Median.ToString(CultureInfo.InvariantCulture)
                    : window.Peak.ToString(CultureInfo.InvariantCulture),
                ["days"] = ((int)window.Window.TotalDays).ToString(CultureInfo.InvariantCulture),
                ["count"] = window.Samples,
            });
    }

    /// <summary>
    /// What a sort did with the games it had nothing to rank.
    /// </summary>
    /// <remarks>
    /// The sentence that keeps an unknown from reading as a zero. A game we reached and could not
    /// count sorts <em>after</em> every counted game rather than among the measured zeroes, and the
    /// surface that draws that break has to say what the break is — otherwise the reader sees a list
    /// that runs 54, 11, 2, 0, and then a long tail of games showing no number, which is a list that
    /// looks exactly like the lie.
    /// </remarks>
    public static string Unranked(string tag, GameSort sort) => sort switch
    {
        GameSort.Players => Messages.For(tag, "sort.unranked.players"),
        GameSort.Reached => Messages.For(tag, "sort.unranked.reached"),

        // Two reasons in one group, and the sentence names both: nothing countable in the window, or
        // too few counts to take a median of. Neither is "nobody plays here", and a tail rendered as
        // a run of noughts would say exactly that.
        _ when SortWindows.IsMedian(sort) => Messages.For(tag, "sort.unranked.median",
            new Dictionary<string, object?> { ["minimum"] = SortWindows.MinimumSamples }),
        _ when SortWindows.Of(sort) is not null => Messages.For(tag, "sort.unranked.window"),
        _ => string.Empty,
    };

    /// <summary>One value's label. Open-ended facets are their own labels; the derived ones are not.</summary>
    public static string Value(string tag, string key, FacetValue value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.IsUnknown)
        {
            return Unknown(tag, key);
        }

        return key switch
        {
            FacetKeys.Band => Band(tag, value.Token),
            FacetKeys.LastSeen => LastSeen(tag, value.Token),
            FacetKeys.Tls => Messages.For(tag, "facet.tls.yes"),

            // Everything else IS the value: a codebase name, a version string, a protocol acronym.
            // Machine voice — it is what a game said about itself, and translating it would destroy
            // the evidence rather than localize anything.
            _ => value.Token,
        };
    }

    /// <summary>
    /// The same value, negated — what the panel's <em>anything but</em> group offers.
    /// </summary>
    /// <remarks>
    /// A closed <c>&lt;select&gt;</c> shows the option and never the group it came from, so each
    /// option has to read as a negation on its own. Prefixing "not" does that for a value and makes
    /// nonsense of an absence: <em>not not identified</em> is the exclusion of the games whose
    /// codebase we could not read, and nobody has ever parsed that phrase on the first try. The
    /// absences get the positive sentence they are the absence of.
    /// </remarks>
    public static string Excluded(string tag, string key, FacetValue value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return value.IsUnknown
            ? Known(tag, key)

            // A message rather than "not " + the value: the negation goes before the noun in
            // English and after it in several other languages, and a caller concatenating it here
            // has taken that word order away from every translator at once.
            : Messages.For(tag, "facet.excluded", new Dictionary<string, object?>
            {
                ["value"] = Value(tag, key, value),
            });
    }

    /// <summary>The opposite of <see cref="Unknown"/> — the games this facet has any value for.</summary>
    private static string Known(string tag, string key) => Messages.For(tag, key switch
    {
        FacetKeys.Charset => "facet.known.charset",
        FacetKeys.Codebase => "facet.known.codebase",
        _ => "facet.known.other",
    });

    /// <summary>
    /// What "we have no value for this game" is called, per facet.
    /// </summary>
    /// <remarks>
    /// Three different sentences because they are three different facts. A codebase we could not
    /// identify is a limit of our parsers; a genre nobody declared is a limit of what the game
    /// published; an encoding nothing negotiated is a limit of the handshake. Rendering all three as
    /// "unknown" would be true and would throw away the only part of the answer worth having.
    /// </remarks>
    public static string Unknown(string tag, string key) => Messages.For(tag, key switch
    {
        FacetKeys.Charset => "facet.unknown.charset",
        FacetKeys.Codebase => "facet.unknown.codebase",
        _ => "facet.unknown.other",
    });

    /// <summary>
    /// Whether a facet's values are points on one ordered scale rather than alternatives.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This decides the control's shape, and getting it wrong is the failure mode of the whole
    /// panel.</b> Activity and last-seen are <em>nested thresholds</em>: a game reached an hour ago
    /// is also in the last seven days and the last thirty, and "connected now" is a narrower window
    /// than "active this week" rather than a different kind of thing. Ticking two of them is
    /// meaningless, and excluding one is nonsense — "everything but games with somebody on" is not a
    /// question anybody has. A radio group is the honest control and it has no exclude affordance,
    /// because there is nothing to exclude.
    /// </para>
    /// <para>
    /// Every other facet holds genuine alternatives — a game runs PennMUSH or it runs Evennia — so
    /// include and exclude both mean something and the row is tri-state.
    /// </para>
    /// </remarks>
    public static bool IsSingleChoice(string key) =>
        key is FacetKeys.Band or FacetKeys.LastSeen;

    /// <summary>
    /// What an activity band is called, from its token — the same word the listing's own row uses.
    /// </summary>
    /// <remarks>
    /// Public so a second surface can read the vocabulary rather than spell it again. Find a game
    /// carried its own copy of "reachable, count unknown" with a comment saying it was the listing's
    /// words for the band, and then the listing shortened the band to "uncounted" and the two
    /// drifted — which is the whole failure the comment was written to prevent.
    /// </remarks>
    public static string BandWord(string tag, string token) => Band(tag, token);

    private static string Band(string tag, string token) => Messages.For(tag, token switch
    {
        "playersNow" => "facet.band.playersNow",
        "activeThisWeek" => "facet.band.activeThisWeek",
        "quiet" => "facet.band.quiet",
        "dark" => "facet.band.dark",
        _ => "facet.band.archived",
    });

    private static string LastSeen(string tag, string token) => Messages.For(tag, token switch
    {
        "day" => "facet.seen.day",
        "week" => "facet.seen.week",
        "month" => "facet.seen.month",
        "older" => "facet.seen.older",

        // Never reached, and deliberately not the oldest bucket: a game we have listed and never
        // once got an answer from has no last-seen date at all, and dating it from our own ignorance
        // would read as its outage.
        _ => "facet.seen.never",
    });
}
