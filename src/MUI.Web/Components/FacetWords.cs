using System.Globalization;

using MUI.Catalog;

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
    public static string Group(string key) => key switch
    {
        FacetKeys.Band => "activity",
        FacetKeys.LastSeen => "last seen",
        FacetKeys.Protocol => "protocols offered",
        FacetKeys.Tls => "encrypted",
        // "encoding", not "encoding negotiated": the evidence chip beside it already says measured,
        // and the two words together wrapped the label and knocked its control out of line with the
        // rest of the row. What negotiation has to do with it is in the values — "nothing negotiated"
        // is what this facet calls a game it has no answer for.
        FacetKeys.Charset => "encoding",
        FacetKeys.Codebase => "codebase",

        // "version" alone, because it sits directly under the codebase it is a version of and the
        // pair reads as one column. "codebase version" repeated the word the row above it already
        // said and wrapped the label onto two lines for the sake of it.
        FacetKeys.CodebaseVersion => "version",
        FacetKeys.Lineage => "lineage",
        FacetKeys.Family => "family",
        FacetKeys.Genre => "genre",
        FacetKeys.Language => "language",
        _ => key,
    };

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
    public static string Evidence(FacetEvidence evidence) => evidence switch
    {
        FacetEvidence.Measured => "measured",
        FacetEvidence.Derived => "derived",
        _ => "declared",
    };

    /// <summary>What each of those words means, said once per surface rather than once per facet.</summary>
    /// <remarks>
    /// <see cref="FacetEvidence.Derived"/> names us out loud — "we grouped", not "is grouped". The
    /// other two sentences have somebody in them (we watched; the game says) and a passive third
    /// would be the only fact on the site whose author had gone missing, which is precisely the one
    /// where it matters.
    /// </remarks>
    public static string EvidenceMeaning(FacetEvidence evidence) => evidence switch
    {
        FacetEvidence.Measured => "we watched this happen",
        FacetEvidence.Derived => "we grouped what the game told us",
        _ => "the game says so, and we did not check",
    };

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
    public static string Sort(GameSort sort) => sort switch
    {
        GameSort.Players => "connected now",
        GameSort.Reached => "last reached",
        GameSort.MedianWeek => "typically on · 7 days",
        GameSort.MedianMonth => "typically on · 30 days",
        GameSort.MedianQuarter => "typically on · 90 days",
        GameSort.PeakWeek => "most on at once · 7 days",
        GameSort.PeakMonth => "most on at once · 30 days",
        GameSort.PeakQuarter => "most on at once · 90 days",
        _ => "name",
    };

    /// <summary>
    /// Which group of the sort control an order belongs in.
    /// </summary>
    /// <remarks>
    /// Nine options in one flat list is a list nobody reads to the bottom of. The grouping is by what
    /// each order reads — a fact on the row, or a statistic over a span — which is also the
    /// difference a reader most needs to see before choosing one.
    /// </remarks>
    public static string SortGroup(GameSort sort) => SortWindows.Of(sort) is null
        ? "on the row now"

        // Two words, because each option under them already names its own window: "typically on ·
        // 7 days" under a heading reading "typical over a window" said the window twice and the
        // word once.
        : SortWindows.IsMedian(sort) ? "typical" : "peak";

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
    public static string Window(PresenceWindow window, GameSort sort)
    {
        ArgumentNullException.ThrowIfNull(window);

        var span = $"{window.Window.TotalDays:0}d";
        var counts = $"{window.Samples} count{(window.Samples == 1 ? string.Empty : "s")}";

        return SortWindows.IsMedian(sort)
            ? $"median {window.Median.ToString(CultureInfo.InvariantCulture)} · {span} · {counts}"
            : $"most {window.Peak} at once · {span} · {counts}";
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
    public static string Unranked(GameSort sort) => sort switch
    {
        GameSort.Players => "reachable, count unreadable — not zero",
        GameSort.Reached => "never once reached — not reached long ago",

        // Two reasons in one group, and the sentence names both: nothing countable in the window, or
        // too few counts to take a median of. Neither is "nobody plays here", and a tail rendered as
        // a run of noughts would say exactly that.
        _ when SortWindows.IsMedian(sort) =>
            $"fewer than {SortWindows.MinimumSamples} counts in the window, or none at all "
            + "— not a typical count of zero",
        _ when SortWindows.Of(sort) is not null =>
            "nothing we could count in the window — not a game nobody was on",
        _ => string.Empty,
    };

    /// <summary>One value's label. Open-ended facets are their own labels; the derived ones are not.</summary>
    public static string Value(string key, FacetValue value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.IsUnknown)
        {
            return Unknown(key);
        }

        return key switch
        {
            FacetKeys.Band => Band(value.Token),
            FacetKeys.LastSeen => LastSeen(value.Token),
            FacetKeys.Tls => "connected over TLS",
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
    public static string Excluded(string key, FacetValue value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return value.IsUnknown ? Known(key) : "not " + Value(key, value);
    }

    /// <summary>The opposite of <see cref="Unknown"/> — the games this facet has any value for.</summary>
    private static string Known(string key) => key switch
    {
        FacetKeys.Charset => "something negotiated",
        FacetKeys.Codebase => "identified at all",
        _ => "declared at all",
    };

    /// <summary>
    /// What "we have no value for this game" is called, per facet.
    /// </summary>
    /// <remarks>
    /// Three different sentences because they are three different facts. A codebase we could not
    /// identify is a limit of our parsers; a genre nobody declared is a limit of what the game
    /// published; an encoding nothing negotiated is a limit of the handshake. Rendering all three as
    /// "unknown" would be true and would throw away the only part of the answer worth having.
    /// </remarks>
    public static string Unknown(string key) => key switch
    {
        FacetKeys.Charset => "nothing negotiated",
        FacetKeys.Codebase => "not identified",
        _ => "not declared",
    };

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
    /// The one sentence a group needs at the moment somebody uses it.
    /// </summary>
    /// <remarks>
    /// Under each group's own heading rather than once at the foot of the panel, because the three
    /// shapes say three different things and a reader ticking a codebase is not the reader picking
    /// an activity threshold. It replaces a single line under the third group that applied to two of
    /// them and contradicted the first.
    /// </remarks>
    public static string Note(string key) => key switch
    {
        FacetKeys.Band => "Pick one. Each is a wider window than the last.",
        FacetKeys.LastSeen => "Pick one. Each is a wider window than the last.",
        _ => "Tick to include, − to exclude.",
    };

    /// <summary>
    /// What an activity band is called, from its token — the same word the listing's own row uses.
    /// </summary>
    /// <remarks>
    /// Public so a second surface can read the vocabulary rather than spell it again. Find a game
    /// carried its own copy of "reachable, count unknown" with a comment saying it was the
    /// listing's words for the band, and then the listing shortened the band to "uncounted" and the
    /// two drifted — which is the whole failure the comment was written to prevent.
    /// </remarks>
    public static string BandWord(string token) => Band(token);

    private static string Band(string token) => token switch
    {
        "playersNow" => "connected now",
        "activeThisWeek" => "active this week",
        "quiet" => "uncounted",
        "dark" => "dark — not reached in a month",
        _ => "archived",
    };

    private static string LastSeen(string token) => token switch
    {
        "day" => "in the last 24 hours",
        "week" => "in the last 7 days",
        "month" => "in the last 30 days",
        "older" => "longer ago",

        // Never reached, and deliberately not the oldest bucket: a game we have listed and never
        // once got an answer from has no last-seen date at all, and dating it from our own ignorance
        // would read as its outage.
        _ => "never reached",
    };
}
