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
    /// column says and what the sort does; "busiest" is <c>/rankings</c>'s word for a median over
    /// ninety days with a sample floor under it, and lending it to one instantaneous count would be
    /// two different measurements answering to one name on the same site.
    /// </remarks>
    public static string Sort(GameSort sort) => sort switch
    {
        GameSort.Players => "players on now",
        GameSort.Reached => "last reached",
        _ => "name",
    };

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
        GameSort.Players => "answering with nothing we can count — not zero players",
        GameSort.Reached => "never once reached — not reached long ago",
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

    private static string Band(string token) => token switch
    {
        "playersNow" => "players on now",
        "activeThisWeek" => "active this week",
        "quiet" => "quiet — reachable, nobody counted",
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
