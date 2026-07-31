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
        FacetKeys.Charset => "encoding negotiated",
        FacetKeys.Codebase => "codebase",
        FacetKeys.Family => "family",
        FacetKeys.Genre => "genre",
        FacetKeys.Language => "language",
        _ => key,
    };

    /// <summary>
    /// How a facet's evidence is described, in three words a reader can act on.
    /// </summary>
    /// <remarks>
    /// Never abbreviated to a symbol. The difference between something we watched happen and
    /// something a game typed into <c>mush.cnf</c> in 2017 is the product, and a legend a reader has
    /// to learn is a difference they will not read.
    /// </remarks>
    public static string Evidence(FacetEvidence evidence) => evidence switch
    {
        FacetEvidence.Measured => "we measured this",
        _ => "the game says so",
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
