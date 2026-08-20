using System.Globalization;

using MUI.Catalog;
using MUI.Web.Localization;

namespace MUI.Web.Components;

/// <summary>
/// The words the facets are shown in — on the rendered panel and in plain text alike.
/// </summary>
/// <remarks>
/// Wording lives here rather than beside the query because <c>MUI.Catalog</c> is UI-agnostic; the
/// graphical panel and the plain-text surface render the same facts through one vocabulary.
/// <see cref="Unknown"/> matters most: every facet spells its own absence, and none of them spells
/// it as a <em>no</em> — an absence is a fact about our reach or about what a game published, never
/// a fact about the game lacking the thing.
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

        // Group names say what we did ("could not count"), never a claim about the game itself
        // ("empty") — see rule 5.
        FacetKeys.Uncounted => "facet.group.uncounted",
        FacetKeys.Unreachable => "facet.group.unreachable",
        FacetKeys.Charset => "facet.group.charset",
        FacetKeys.Codebase => "facet.group.codebase",

        FacetKeys.CodebaseVersion => "facet.group.version",
        FacetKeys.Lineage => "facet.group.lineage",
        FacetKeys.Family => "facet.group.family",
        FacetKeys.Trending => "facet.group.trending",
        FacetKeys.Genre => "facet.group.genre",
        FacetKeys.Language => "facet.group.language",

        // Named here, not in the chip builder, so a facet is put into words in one place.
        FacetKeys.Text => "facet.group.search",
        FacetKeys.Archived => "facet.group.archived",
        FacetKeys.Adult => "facet.group.adult",
        _ => key,
    });

    /// <summary>
    /// What kind of statement a facet is, in one word.
    /// </summary>
    /// <remarks>
    /// A word rather than only a symbol — the difference between something we watched and something
    /// a game declared is the product, not decoration. The full sentence exists once, in
    /// <see cref="EvidenceMeaning"/>, rather than repeated per facet.
    /// </remarks>
    public static string Evidence(string tag, FacetEvidence evidence) => Messages.For(tag, evidence switch
    {
        FacetEvidence.Measured => "kicker.measured",
        FacetEvidence.Derived => "kicker.derived",
        _ => "kicker.declared",
    });

    /// <summary>What each of those words means, said once per surface rather than once per facet.</summary>
    /// <remarks>
    /// <see cref="FacetEvidence.Derived"/> names us out loud — "we grouped", not "is grouped" — so
    /// all three sentences keep an explicit actor.
    /// </remarks>
    public static string EvidenceMeaning(string tag, FacetEvidence evidence) => Messages.For(tag, evidence switch
    {
        FacetEvidence.Measured => "evidence.measured.meaning",
        FacetEvidence.Derived => "evidence.derived.meaning",
        _ => "evidence.declared.meaning",
    });

    /// <summary>What each sort order is called on the control.</summary>
    /// <remarks>
    /// Named for the fact each one reads, never for a superlative — "busiest" is <c>/rankings</c>'s
    /// word for a different statistic (a median over a windowed sample), and lending it to an
    /// instantaneous count would put two measurements under one name. The window sorts name their
    /// span too, since "typically on" alone would cover three different orders.
    /// </remarks>
    public static string Sort(string tag, GameSort sort) => Messages.For(tag, sort switch
    {
        GameSort.Players => "sort.players",
        GameSort.Reached => "sort.reached",
        GameSort.Discovered => "sort.discovered",
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
    /// Grouped by what each order reads — a fact on the row, or a statistic over a span — since nine
    /// options in one flat list is a list nobody reads to the bottom of.
    /// </remarks>
    public static string SortGroup(string tag, GameSort sort) => Messages.For(tag,
        SortWindows.Of(sort) is null ? "sort.group.row"
        : SortWindows.IsMedian(sort) ? "sort.group.typical" : "sort.group.peak");

    /// <summary>
    /// How a window figure is spelled where it is shown beside the row it ranked.
    /// </summary>
    /// <remarks>
    /// <b>The sample count is part of the sentence, not an optional extra</b> — this site never
    /// publishes a figure whose basis it has hidden (§15.7), and it is also the only thing on the
    /// row distinguishing a game measured three hundred times from one probed thirty.
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
                // A number, not a string: {days} selects a plural branch, and a plural argument has
                // to be a number to have operands at all.
                ["days"] = (int)window.Window.TotalDays,
                ["count"] = window.Samples,
            });
    }

    /// <summary>
    /// What a sort did with the games it had nothing to rank.
    /// </summary>
    /// <remarks>
    /// Keeps an unknown from reading as a zero: a game we reached but could not count sorts
    /// <em>after</em> every counted game rather than among the measured zeroes, and the surface has
    /// to say what that break is — otherwise a reader sees 54, 11, 2, 0, then an unlabelled tail that
    /// looks exactly like more zeroes.
    /// </remarks>
    public static string Unranked(string tag, GameSort sort) => sort switch
    {
        GameSort.Players => Messages.For(tag, "sort.unranked.players"),
        GameSort.Reached => Messages.For(tag, "sort.unranked.reached"),
        GameSort.Discovered => Messages.For(tag, "sort.unranked.discovered"),

        // Neither reason ("nothing countable in the window" / "too few counts for a median") is
        // "nobody plays here", and a tail of noughts would say exactly that.
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
            FacetKeys.Trending => Trending(tag, value.Token),
            FacetKeys.Tls => Messages.For(tag, "facet.tls.yes"),

            // Reuses the glossary's own reviewed strings rather than minting a second translation of
            // the same claim.
            FacetKeys.Uncounted => Messages.For(tag, "state.uncounted"),
            FacetKeys.Unreachable => Messages.For(tag, "state.unreachable"),

            // Everything else IS the value — a codebase name, version string, protocol acronym — and
            // translating it would destroy the evidence rather than localize anything.
            _ => value.Token,
        };
    }

    /// <summary>
    /// The same value, negated — what the panel's <em>anything but</em> group offers.
    /// </summary>
    /// <remarks>
    /// A plain "not " + value makes nonsense of an absence — <em>not not identified</em> — so
    /// absences get their own positive sentence instead of a negated one.
    /// </remarks>
    public static string Excluded(string tag, string key, FacetValue value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.IsUnknown)
        {
            return Known(tag, key);
        }

        return key switch
        {
            // "not uncounted" reads as a claim about the games rather than the listing (rule 5); these
            // say what the reader actually did — hid some rows.
            FacetKeys.Uncounted => Messages.For(tag, "facet.excluded.uncounted"),
            FacetKeys.Unreachable => Messages.For(tag, "facet.excluded.unreachable"),

            // A message rather than string concatenation: negation word order varies by language, and
            // concatenating here would fix English's order for every translator.
            _ => Messages.For(tag, "facet.excluded", new Dictionary<string, object?>
            {
                ["value"] = Value(tag, key, value),
            }),
        };
    }

    /// <summary>The opposite of <see cref="Unknown"/> — the games this facet has any value for.</summary>
    private static string Known(string tag, string key) => Messages.For(tag, key switch
    {
        FacetKeys.Charset => "facet.known.charset",
        FacetKeys.Codebase => "facet.known.codebase",
        FacetKeys.Trending => "facet.known.trending",
        _ => "facet.known.other",
    });

    /// <summary>
    /// What "we have no value for this game" is called, per facet.
    /// </summary>
    /// <remarks>
    /// Three sentences for three different facts — a codebase we could not identify, a genre nobody
    /// declared, an encoding nothing negotiated — rather than one generic "unknown" that would throw
    /// away which limit produced the absence.
    /// </remarks>
    public static string Unknown(string tag, string key) => Messages.For(tag, key switch
    {
        FacetKeys.Charset => "facet.unknown.charset",
        FacetKeys.Codebase => "facet.unknown.codebase",
        FacetKeys.Trending => "facet.unknown.trending",
        _ => "facet.unknown.other",
    });

    /// <summary>
    /// Whether a facet's values are points on one ordered scale rather than alternatives.
    /// </summary>
    /// <remarks>
    /// Activity and last-seen are <em>nested thresholds</em>, not alternatives — a game reached an
    /// hour ago is also within the last seven days, so ticking two is meaningless and excluding one
    /// is nonsense. They render as a radio group with no exclude affordance. Every other facet holds
    /// genuine alternatives (PennMUSH vs. Evennia), so those rows stay tri-state.
    /// </remarks>
    public static bool IsSingleChoice(string key) =>
        key is FacetKeys.Band or FacetKeys.LastSeen;

    /// <summary>
    /// What an activity band is called, from its token — the same word the listing's own row uses.
    /// </summary>
    /// <remarks>
    /// Public so a second surface reads the same vocabulary rather than spelling it again. The
    /// <c>quiet</c> band holds a game measured at zero every hour beside a game whose every count was
    /// unreadable — two different facts — so it must not borrow <see cref="FacetKeys.Uncounted"/>'s
    /// wording; the band names its threshold, not the reason.
    /// </remarks>
    public static string BandWord(string tag, string token) => Band(tag, token);

    /// <summary>
    /// What a growth direction is called, from its token — shared by the panel's row and the
    /// listing's own arrow, so the two surfaces can never name the same computed direction two ways.
    /// </summary>
    public static string TrendingWord(string tag, GrowthDirection direction) =>
        Trending(tag, FacetTokens.Of(direction));

    private static string Trending(string tag, string token) => Messages.For(tag, token switch
    {
        "up" => "facet.trending.up",
        "down" => "facet.trending.down",
        _ => "facet.trending.steady",
    });

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

        // Deliberately not the oldest bucket: a game never once answered has no last-seen date, and
        // dating it from our own ignorance would read as its outage.
        _ => "facet.seen.never",
    });
}
