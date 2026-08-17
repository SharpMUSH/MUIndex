namespace MUI.Catalog;

/// <summary>
/// The name of every facet, in the one spelling the query, the read API and the site's GET form all
/// use (spec §9).
/// </summary>
/// <remarks>
/// These are querystring parameter names as much as they are facet identifiers, and that is the
/// point: <c>q</c> and <c>archived</c> were public on <c>/games</c> before there was a panel, and an
/// API that invented <c>search</c> beside them would have given one question two spellings and let
/// them drift. Naming them once, here, is what makes "the page and the API agree" a fact about the
/// code rather than a convention somebody has to remember.
/// </remarks>
public static class FacetKeys
{
    public const string Text = "q";

    public const string Archived = "archived";

    public const string Band = "band";

    public const string LastSeen = "seen";

    public const string Protocol = "protocol";

    public const string Tls = "tls";

    public const string Charset = "charset";

    public const string Language = "language";

    /// <summary>
    /// The codebase family — <c>PennMUSH</c>, never <c>PennMUSH 1.8.8p0</c>.
    /// </summary>
    /// <remarks>
    /// This counted the raw <c>CODEBASE</c> string until the panel was seen against a real crawl,
    /// where it offered <c>PennMUSH 1.8.8p0 (9)</c>, <c>PennMUSH 1.8.7p0 (4)</c> and
    /// <c>PennMUSH 1.8.6p1 (2)</c> as three unrelated choices. Nobody asks a version-shaped
    /// question, and splitting one codebase across its patchlevels also spent three of the twelve
    /// values the panel has room for (<see cref="FacetedSearch.MaxValues"/>) saying the same word.
    /// The exact string is still filterable — it is <see cref="CodebaseVersion"/>.
    /// </remarks>
    public const string Codebase = "codebase";

    /// <summary>
    /// The codebase exactly as the game reports it, version and all.
    /// </summary>
    /// <remarks>
    /// Worth a facet of its own rather than being folded away entirely: "which patchlevels of
    /// PennMUSH are actually running" is a real question, and it is the one a codebase reference page
    /// leaves a reader holding. It sits under <see cref="Codebase"/> in the panel because that is the
    /// order the questions come in.
    /// </remarks>
    public const string CodebaseVersion = "version";

    /// <summary>
    /// The old spelling of <see cref="Codebase"/>, still accepted in a querystring.
    /// </summary>
    /// <remarks>
    /// It named a filter that narrowed the listing while <c>codebase</c> counted raw strings; now
    /// that <c>codebase</c> <em>is</em> the family, the two are one question and one key. This
    /// survives as an alias rather than being deleted because every codebase reference page has been
    /// linking here with it, and a link that used to work and now silently returns the unfiltered
    /// catalogue is worse than one that errors.
    /// </remarks>
    public const string CodebaseFamily = "codebase-family";

    /// <summary>
    /// The lineage we place a codebase in — <c>MUSH</c>, <c>DikuMUD</c> (see
    /// <see cref="CodebaseLineage"/>). Ours, and labelled as ours.
    /// </summary>
    public const string Lineage = "lineage";

    /// <summary>MSSP's own <c>FAMILY</c> variable, as the game published it. See <see cref="Lineage"/>.</summary>
    public const string Family = "family";

    public const string Genre = "genre";

    /// <summary>
    /// What order the listing comes back in. A filter parameter by spelling and by plumbing, so the
    /// panel, the page and the read API cannot grow two words for one question.
    /// </summary>
    public const string Sort = "sort";
}

/// <summary>
/// The orders the catalogue can be read in.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every one of these is named for the measurement it reads, never for a superlative.</b> There is
/// no "busiest" here and the word is deliberately not used: <c>/rankings</c> means something specific
/// by it — a median over a window with a sample floor under it — and a sort answering to the same
/// name on a different arithmetic would be two measurements wearing one word on one site.
/// <see cref="Players"/> is "players on now", which is exactly what it orders by and exactly as much
/// as it claims; the window sorts name their statistic and their span, because an average over seven
/// days and a peak over ninety are different questions and a reader has to be able to see which one
/// they asked.
/// </para>
/// <para>
/// <b>There is no "recently listed".</b> The only date we have for that is <c>game.first_seen_at</c>,
/// which is when <em>our crawler</em> first reached a game — a picture of where the frontier has got
/// to, not of anything happening in the hobby (the same reasoning that keeps it off the adoption
/// curves, see <c>EcosystemDashboard</c>). Sorted to the top of the catalogue it would read as "new
/// games", which is a claim we would be making out of our own schedule. The <em>newly discovered</em>
/// feed publishes the same dates with the framing that makes them honest, and that is where it stays.
/// </para>
/// </remarks>
public enum GameSort
{
    /// <summary>
    /// Alphabetical — the one order that ranks nobody.
    /// </summary>
    /// <remarks>
    /// This was the default, on the argument that a listing arriving pre-ranked makes an editorial
    /// claim the reader never asked for. The argument holds against a <em>ranking</em> and does not
    /// hold against this: the alphabet is not neutral, it is an order too, and the one it produces
    /// puts <c>3Kingdoms</c> above every game in the hobby for no reason anybody chose. Neither
    /// order is an opinion of ours — <see cref="Players"/> reads a measurement and this reads a
    /// spelling — so the question is which one answers what a reader came for, and they came to find
    /// a game with people in it. It remains one click away and every URL that names it still means
    /// what it meant.
    /// </remarks>
    Name,

    /// <summary>
    /// Most players counted on right now, first — and the default.
    /// </summary>
    /// <remarks>
    /// The most useful fact on the page leads it. This ranks on a measurement rather than on our
    /// judgement, which is the property that matters: the games above the fold are the ones somebody
    /// was counted in, not the ones we chose to promote. The break the listing draws is what keeps it
    /// honest — the games we could not count follow as a group that says so, and never as a tail of
    /// zeroes (see <see cref="GameSorting"/>).
    /// </remarks>
    Players,

    /// <summary>Most recently reached first.</summary>
    Reached,

    /// <summary>Highest median count over the last 7 days, first.</summary>
    MedianWeek,

    /// <summary>Highest median count over the last 30 days, first.</summary>
    MedianMonth,

    /// <summary>Highest median count over the last 90 days, first.</summary>
    MedianQuarter,

    /// <summary>Largest single count observed in the last 7 days, first.</summary>
    PeakWeek,

    /// <summary>Largest single count observed in the last 30 days, first.</summary>
    PeakMonth,

    /// <summary>Largest single count observed in the last 90 days, first.</summary>
    PeakQuarter,
}

/// <summary>
/// The windows the listing can be sorted over, and what each of the two statistics needs before it
/// may rank a game (spec §9).
/// </summary>
/// <remarks>
/// <para>
/// <b>Three spans and not a free parameter.</b> A window a caller can dial to any width is a window
/// somebody can tune until a game they like comes top, and every one of these figures would then be
/// answering a slightly different question from the one beside it. Seven, thirty and ninety days are
/// the three this site already measures things over — <c>RankingWindow</c>, "reachable recently" and
/// the availability fraction on a game's page — so a reader who has read one of those pages already
/// knows what these mean.
/// </para>
/// <para>
/// <b>A median carries a sample floor and a peak does not</b>, because they fail differently. A
/// median over four probes is not a median of anything and would put a game found on Friday above
/// one measured three hundred times, which is ranking our own crawl schedule; the floor is the same
/// <c>NpgsqlGameQueries.MinimumRankingSamples</c> that <c>/rankings</c> uses, so the two surfaces
/// cannot come to hold two opinions about how much evidence is enough. A peak is one observation and
/// is true however few of them there were — we counted that many people on at once, and a floor
/// under it would suppress a measurement we actually took.
/// </para>
/// <para>
/// <b>A median and never a mean</b>, which is the same choice <c>/rankings</c> made and migration
/// 0019 exists to make cheap. A mean is pulled around by the one evening a game was linked from
/// somewhere; the typical count is what a reader asking "how busy is this normally" wants, and it is
/// a number a server actually reported rather than an arithmetic artefact between two of them.
/// </para>
/// </remarks>
public static class SortWindows
{
    /// <summary>
    /// How many counted samples an average needs before it may rank a game.
    /// </summary>
    /// <remarks>
    /// A day's worth of hourly probes, and the same floor <c>/rankings</c> puts under its median —
    /// <c>NpgsqlGameQueries.MinimumRankingSamples</c> is this constant, so the listing and the league
    /// table cannot come to hold two opinions about how much evidence is enough. It lives here rather
    /// than there because the sort is what enforces it and the sort is model-layer.
    /// </remarks>
    public const int MinimumSamples = 24;

    public static readonly TimeSpan Week = TimeSpan.FromDays(7);

    public static readonly TimeSpan Month = TimeSpan.FromDays(30);

    public static readonly TimeSpan Quarter = TimeSpan.FromDays(90);

    /// <summary>The span a sort reads over, or null where it reads no window at all.</summary>
    public static TimeSpan? Of(GameSort sort) => sort switch
    {
        GameSort.MedianWeek or GameSort.PeakWeek => Week,
        GameSort.MedianMonth or GameSort.PeakMonth => Month,
        GameSort.MedianQuarter or GameSort.PeakQuarter => Quarter,
        _ => null,
    };

    /// <summary>Whether a sort ranks on the typical count rather than on the largest reading.</summary>
    public static bool IsMedian(GameSort sort) =>
        sort is GameSort.MedianWeek or GameSort.MedianMonth or GameSort.MedianQuarter;

    /// <summary>
    /// Whether a window's figures are enough to rank this game on this sort.
    /// </summary>
    /// <remarks>
    /// A window with no counted sample at all ranks nothing either way: there is no median of
    /// nothing and no largest of no readings, and the query does not return a row for such a game.
    /// </remarks>
    public static bool CanRank(PresenceWindow window, GameSort sort)
    {
        ArgumentNullException.ThrowIfNull(window);

        return window.Samples > 0 && (!IsMedian(sort) || window.Samples >= MinimumSamples);
    }
}

/// <summary>
/// The listing's order, and what it does with the games a sort cannot rank.
/// </summary>
/// <remarks>
/// <para>
/// <b>An unknown is never a zero and never sorts as one.</b> Most of this catalogue answers with
/// nothing we can count — we got in and the <c>WHO</c> was past our parser, or the game published no
/// <c>PLAYERS</c> — and <c>null</c> ordered as <c>0</c> would pile every one of those at the bottom
/// of "players on now" indistinguishably from the games we measured and found empty. That is the
/// central claim of this project made backwards, on the page it is most likely to be read off.
/// </para>
/// <para>
/// So the games a sort can rank come first, in order, and the ones it cannot follow as a group in
/// the default order. <see cref="IsUnranked"/> is the same question the surfaces ask to know where to
/// draw the line and what to call the group, so the ordering and the label cannot disagree about
/// which games are in it.
/// </para>
/// </remarks>
public static class GameSorting
{
    /// <summary>Whether this sort has nothing to rank a game by — never "whether it is zero".</summary>
    /// <remarks>
    /// A window sort is unranked where the window is absent — the game had nothing countable in the
    /// span — <em>and</em> where it is present and too thin to average over. Both are "we cannot put
    /// this game in this order", and the break the surfaces draw says so in one sentence rather than
    /// dropping the game or floating it to the bottom as a nought.
    /// </remarks>
    public static bool IsUnranked(GameSummary game, GameSort sort)
    {
        ArgumentNullException.ThrowIfNull(game);

        if (SortWindows.Of(sort) is not null)
        {
            return game.PlayersOverWindow is not { } window || !SortWindows.CanRank(window, sort);
        }

        return sort switch
        {
            GameSort.Players => game.PlayersNow is null,
            GameSort.Reached => game.LastReachableAt is null,
            _ => false,
        };
    }

    public static IReadOnlyList<GameSummary> Apply(IEnumerable<GameSummary> games, GameSort sort)
    {
        ArgumentNullException.ThrowIfNull(games);

        // Ranked before unranked, always — then the sort's own key, then the name, so the order is
        // total and a listing does not shuffle between two identical requests. Each key reads as
        // zero for the sorts it does not belong to, which is safe only because the unranked games
        // have already been pushed below every ranked one by the first clause: a key of zero here
        // is "this sort does not use this column", never "this game measured nothing".
        var ordered = games
            .OrderBy(g => IsUnranked(g, sort) ? 1 : 0)
            .ThenByDescending(g => sort is GameSort.Players ? g.PlayersNow ?? 0 : 0)
            .ThenByDescending(g => sort is GameSort.Reached
                ? g.LastReachableAt ?? DateTimeOffset.MinValue
                : DateTimeOffset.MinValue)
            .ThenByDescending(g => Ranked(g, sort))
            .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase);

        return [.. ordered];
    }

    /// <summary>
    /// What a window sort ranks on, as one number — the typical count, or the largest reading.
    /// </summary>
    /// <remarks>
    /// Zero for every sort that reads no window, and for every game this sort cannot rank. Neither
    /// is a claim that nobody was there: the games in the second group sort below the break, where
    /// the listing says in words what they have in common.
    /// </remarks>
    private static int Ranked(GameSummary game, GameSort sort) =>
        SortWindows.Of(sort) is null || game.PlayersOverWindow is not { } window
            ? 0
            : SortWindows.IsMedian(sort) ? window.Median : window.Peak;
}

/// <summary>
/// Which side of §3.1 a facet reads. Rendered beside every group, never inferred by the reader.
/// </summary>
/// <remarks>
/// The distinction is the product. A measured facet answers "we watched this happen"; a declared one
/// answers "the game typed this into <c>mush.cnf</c>, possibly in 2017". Both are worth filtering on
/// and they are not the same question, so a panel that presented them identically would be making
/// the exact claim this site exists to stop making.
/// </remarks>
public enum FacetEvidence
{
    /// <summary>We watched it happen.</summary>
    Measured,

    /// <summary>The game published it and we did not check.</summary>
    Declared,

    /// <summary>
    /// Neither: our own classification of something a game published.
    /// </summary>
    /// <remarks>
    /// A third word rather than borrowing one of the other two, because both would be false in a way
    /// a reader cannot see through. <c>codebase = MUSH</c> is not a measurement — nothing on the wire
    /// says it — and it is not a declaration either, because the game never said it; we grouped
    /// PennMUSH, TinyMUX and RhostMUSH under one heading and that grouping is an editorial act. This
    /// site's whole claim is that a reader can tell where a fact came from, and the one kind of fact
    /// that comes from <em>us</em> is the one it would be least excusable to leave unlabelled.
    /// </remarks>
    Derived,
}

/// <summary>
/// Which spelling names a group of values that differ only in case.
/// </summary>
/// <remarks>
/// <para>
/// Every open-ended facet groups case-insensitively, because <c>russian</c> and <c>Russian</c> are
/// one language and a panel offering both is splitting a count on somebody's shift key. That leaves
/// the question of what the group is <em>called</em>, and "whichever row was read first" is an answer
/// that changes with the sort order — the same catalogue could label a facet differently on two
/// renders, which is a wobble a reader would read as data changing.
/// </para>
/// <para>
/// The commonest spelling wins, ordinal breaking the tie. One game's stray capitalisation therefore
/// cannot name a codebase family on a public page, and the label is a function of the set rather than
/// of the order it arrived in.
/// </para>
/// </remarks>
public static class Spellings
{
    public static string Commonest(IEnumerable<string> spellings)
    {
        ArgumentNullException.ThrowIfNull(spellings);

        return spellings
            .GroupBy(spelling => spelling, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .First().Key;
    }
}

/// <summary>How a facet combines with itself.</summary>
public enum FacetKind
{
    /// <summary>One value at a time; choosing another replaces it.</summary>
    Choice,

    /// <summary>
    /// A set of things we observed, intersected. Checking two asks for games that offered both.
    /// </summary>
    /// <remarks>
    /// There is deliberately no way to ask for the complement. "Games that do not offer GMCP" is a
    /// question the data cannot answer: a capability is written <c>true</c> when it was observed and
    /// is otherwise not written at all (see <c>FieldObservations.Measured</c>), because this client
    /// requests only some options and a server that was never asked has not declined. A checkbox
    /// whose unchecked state meant "no" would publish our own instrumentation as a fact about
    /// somebody's game.
    /// </remarks>
    Presence,
}

/// <summary>
/// One choice-facet selection: a value the data carries, or the absence of one.
/// </summary>
/// <remarks>
/// The absence is a first-class member rather than an empty string, because the whole site turns on
/// unknown and <em>no</em> being different facts. "Games whose codebase we could not identify" is a
/// real and useful question; it is not "games with no codebase", and neither is it a games-with-any
/// filter left blank. Modelling it as a value would let one be typed where the other was meant.
/// </remarks>
public sealed record FacetChoice(string? Value, bool Exclude = false)
{
    /// <summary>
    /// The querystring spelling of the absence. Tilde-prefixed so it cannot collide with a real
    /// value: a game may legitimately be called <c>none</c> and none may legitimately be a genre.
    /// </summary>
    public const string UnknownToken = "~unknown";

    /// <summary>
    /// The prefix that turns a selection inside out: <c>?codebase=!Evennia</c> is every game whose
    /// codebase is not Evennia.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A facet has three states, not two</b>, and the third one is what makes the panel a filter
    /// rather than a set of shortcuts. Absent means the facet is not being asked about; a value
    /// means <em>only these</em>; an excluded value means <em>anything but these</em>. Without the
    /// third, "show me the games that are not Evennia" is a question the catalogue can answer and
    /// the interface cannot ask.
    /// </para>
    /// <para>
    /// <c>!</c> rather than <c>-</c> because a codebase, genre or language may legitimately begin
    /// with a hyphen and none of the values observed in the wild begin with a bang. A literal
    /// leading <c>!</c> is written <c>!!</c>, so a value is never unreachable — see
    /// <see cref="Parse"/>.
    /// </para>
    /// </remarks>
    public const string ExcludeToken = "!";

    /// <summary>Games for which this facet has no value at all.</summary>
    public static readonly FacetChoice Unknown = new((string?)null);

    public static FacetChoice Of(string value) => new(value);

    /// <summary>The same selection, inside out.</summary>
    public static FacetChoice Not(string value) => new(value, Exclude: true);

    public bool IsUnknown => Value is null;

    /// <summary>What this selection is called in a URL, polarity included.</summary>
    public string Token =>
        (Exclude ? ExcludeToken : string.Empty) + Escaped(Value ?? UnknownToken);

    /// <summary>The same facet with its polarity flipped, which is what a panel's toggle emits.</summary>
    /// <remarks>
    /// <b>A method and not a property, deliberately.</b> A record's generated <c>ToString</c> prints
    /// every public property, so a property returning another <see cref="FacetChoice"/> makes
    /// printing one recurse until the stack runs out — which is exactly what happened, and it
    /// surfaced as an unrelated test dying inside an assertion message rather than as anything to do
    /// with facets.
    /// </remarks>
    public FacetChoice Invert() => this with { Exclude = !Exclude };

    public static FacetChoice Parse(string token)
    {
        ArgumentNullException.ThrowIfNull(token);

        var exclude = token.StartsWith(ExcludeToken, StringComparison.Ordinal)
            && !token.StartsWith(ExcludeToken + ExcludeToken, StringComparison.Ordinal);

        var body = exclude
            ? token[ExcludeToken.Length..]
            // A doubled bang is a literal one: a value that genuinely starts with "!" stays
            // reachable rather than being silently reinterpreted as its own negation.
            : token.StartsWith(ExcludeToken + ExcludeToken, StringComparison.Ordinal)
                ? token[ExcludeToken.Length..]
                : token;

        return string.Equals(body, UnknownToken, StringComparison.Ordinal)
            ? Unknown with { Exclude = exclude }
            : new FacetChoice(body, exclude);
    }

    /// <summary>
    /// Whether <paramref name="actual"/> is the value this selection names — <b>polarity ignored</b>.
    /// </summary>
    /// <remarks>
    /// Deliberately separate from <see cref="Admits"/>. A facet can hand a row several tokens (a
    /// game reached an hour ago is in the last day, week and month), and inverting the comparison
    /// per token would make an excluded selection mean "some token differs" — which every row with
    /// more than one token satisfies. The polarity is applied once, to the answer.
    /// </remarks>
    public bool Covers(string? actual) =>
        IsUnknown ? actual is null : string.Equals(actual, Value, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether a row that did or did not match this selection survives it.</summary>
    public bool Admits(bool covered) => covered != Exclude;

    /// <summary>A value that begins with the exclusion marker is doubled so it round-trips.</summary>
    private static string Escaped(string value) =>
        value.StartsWith(ExcludeToken, StringComparison.Ordinal) ? ExcludeToken + value : value;
}

/// <summary>
/// The last-seen facet (spec §9), measured from <c>game.last_reachable_at</c>.
/// </summary>
/// <remarks>
/// The first three nest — a game seen in the last hour is in all of them — because "seen within a
/// week" is the question people actually have, and cutting it into exclusive rings would make the
/// common case two clicks that cannot both be made. The counts stay honest under nesting: each says
/// exactly how many games choosing it returns.
/// <para>
/// <see cref="Never"/> is a value rather than an unknown, and that is the whole reason it exists
/// separately from <see cref="Older"/>. A game we have listed and never once reached is a different
/// fact from one we reached in 2023, and rendering the two the same way would let our own crawl
/// history read as somebody's outage.
/// </para>
/// </remarks>
public enum LastSeenBand
{
    Day,

    Week,

    Month,

    Older,

    Never,
}

/// <summary>
/// One value of one facet, with how many games choosing it returns.
/// </summary>
/// <remarks>
/// <see cref="Count"/> is not decoration and is not an estimate: it is computed from the same pass
/// that produced the listing beside it (see <see cref="FacetedSearch"/>), so a facet cannot promise
/// results it will not deliver. A value nothing matches is never offered at all — the one exception
/// is a value that is currently selected, which stays visible at zero so it can be seen and undone.
/// </remarks>
public sealed record FacetValue(
    string Token,
    int Count,
    bool IsSelected,
    bool IsUnknown,
    bool IsExcluded = false)
{
    /// <summary>
    /// The three states a value can be in, as one question a renderer can switch on.
    /// </summary>
    /// <remarks>
    /// A panel that only knew <see cref="IsSelected"/> would draw an included and an excluded value
    /// identically, which is the one thing a tri-state filter must not do — a reader would have no
    /// way to tell "only Evennia" from "anything but Evennia" except by reading the URL.
    /// </remarks>
    public FacetState State => (IsSelected, IsExcluded) switch
    {
        (true, true) => FacetState.Excluded,
        (true, false) => FacetState.Included,
        _ => FacetState.Unselected,
    };
}

/// <summary>Whether a facet value is being filtered in, filtered out, or not asked about.</summary>
public enum FacetState
{
    Unselected,
    Included,
    Excluded,
}

/// <summary>One facet, ready to render: what it is called, what it reads, and what it offers.</summary>
/// <remarks>
/// <see cref="Total"/> is what dropping this facet's own selection returns — the number an "any"
/// option produces. It is carried rather than summed from <see cref="Values"/> because an
/// open-ended facet offers only its commonest values, so the sum is short of the truth by however
/// long the tail is, and a control labelled with a number smaller than the set it selects is the
/// same lie in the other direction.
/// </remarks>
public sealed record FacetGroup(
    string Key,
    FacetEvidence Evidence,
    FacetKind Kind,
    int Total,
    IReadOnlyList<FacetValue> Values)
{
    /// <summary>Whether anything is selected here, which is what a "clear this" affordance needs.</summary>
    public bool IsFiltered => Values.Any(v => v.IsSelected);
}

/// <summary>The listing and the facets that describe it, from one pass over one set of games.</summary>
/// <remarks>
/// They are returned together rather than fetched separately on purpose. Two queries would be two
/// answers to two slightly different questions, and the first time they disagreed the panel would be
/// advertising a count the listing could not produce.
/// </remarks>
public sealed record GameListing(IReadOnlyList<GameSummary> Games, IReadOnlyList<FacetGroup> Facets)
{
    public static readonly GameListing Empty = new([], []);
}

/// <summary>
/// One game reduced to the values every facet reads, so the facets are computed once from one shape.
/// </summary>
/// <remarks>
/// <para>
/// Assembling this is each <see cref="IGameQueries"/> implementation's job — Postgres builds it from
/// <c>game_field</c> rows and the presence digest, the demo fixture builds it from constants — and
/// filtering and counting it is <see cref="FacetedSearch"/>'s, once. That split is what stops the
/// fixture and the database from quietly answering the same filter differently, which they already
/// did for <c>band=archived</c> before this type existed.
/// </para>
/// <para>
/// <see cref="Charset"/> is the <em>negotiated</em> charset and never the game's MSSP claim about
/// one, and <see cref="TlsMeasured"/> is an endpoint we actually completed a TLS connection to
/// rather than an <c>SSL</c> line in a self-description. Both are named for what they are so that a
/// later reader wiring them up cannot reach for the declared column by accident.
/// </para>
/// </remarks>
public sealed record GameFacetRow(
    GameSummary Summary,
    ActivityBand Band,
    LastSeenBand LastSeen,
    bool TlsMeasured,
    string? Charset,
    string? Language,
    string? Codebase,
    string? Family,
    string? Genre);

/// <summary>
/// Turns a filter and a set of games into the listing plus every facet's counts.
/// </summary>
/// <remarks>
/// <para>
/// <b>Counts are measured against the set each choice would actually return.</b> A
/// <see cref="FacetKind.Choice"/> facet replaces its own selection, so its values are counted with
/// that selection lifted and every other filter still applied — the number beside <c>quiet</c> is
/// how many games you get by clicking <c>quiet</c>, not how many quiet games exist. A
/// <see cref="FacetKind.Presence"/> facet intersects, so its values are counted against the current
/// results — the number beside <c>GMCP</c> is how many of the games on screen also offered it. The
/// two denominators differ because the two gestures differ, and both answer the same question: what
/// happens if I click this.
/// </para>
/// <para>
/// A value with no games is not offered. That is not tidiness — a facet that can be clicked into an
/// empty listing is a facet lying about the catalogue, and the cheapest way to make that impossible
/// is to never draw the click.
/// </para>
/// </remarks>
public static class FacetedSearch
{
    /// <summary>
    /// How many values an open-ended facet offers. The tail is reachable by search and by URL, and
    /// the panel says as much.
    /// </summary>
    /// <remarks>
    /// The cap was written for the codebase facet, which no longer needs it much: folding the
    /// version off collapsed a catalogue of hundreds of strings to a few dozen families. It is
    /// <see cref="FacetKeys.CodebaseVersion"/> that carries the long tail now, and it is the right
    /// facet to be capped — a reader scanning for a codebase wants the families, and a reader who
    /// wants one patchlevel of one of them arrives already knowing its name.
    /// </remarks>
    public const int MaxValues = 12;

    public static GameListing Search(IReadOnlyList<GameFacetRow> rows, GameFilter filter)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(filter);

        // Archived games leave the default listing and nothing else (spec §7.5). Asking for the
        // archived band *is* asking for them, so the toggle does not also have to be set — the
        // database and the demo fixture disagreed about that until this became one function.
        var wantsArchived = filter.IncludeArchived || filter.Band is ActivityBand.Archived;

        var baseRows = rows
            .Where(r => (wantsArchived || r.Band is not ActivityBand.Archived)
                && MatchesText(r, filter.Text))
            .ToList();

        var results = baseRows.Where(r => Chosen(r, filter, null) && Present(r, filter)).ToList();

        var groups = new List<FacetGroup>();

        foreach (var facet in Choices)
        {
            // This facet's own selection lifted, so a count is what choosing the value returns.
            var domain = baseRows.Where(r => Chosen(r, filter, facet.Key) && Present(r, filter)).ToList();
            var values = facet.Bounded is { } vocabulary
                ? Bounded(domain, facet, vocabulary, filter)
                : Open(domain, facet, filter);

            if (values.Count > 0)
            {
                groups.Add(new FacetGroup(
                    facet.Key, facet.Evidence, FacetKind.Choice, domain.Count, values));
            }
        }

        groups.AddRange(Presence(results, filter));

        // Ordered after the counting, never before it. Every facet count is taken over a set, and a
        // set has no order — so sorting here cannot move a number, which is what lets the panel go on
        // promising exactly what a click returns whichever way the list is arranged.
        return new GameListing(GameSorting.Apply(results.Select(r => r.Summary), filter.Sort), groups);
    }

    /// <summary>
    /// The last-seen band a game is in, given when it was last reachable.
    /// </summary>
    /// <remarks>
    /// Null is <see cref="LastSeenBand.Never"/> and never the oldest bucket: a game we have listed
    /// and never once reached has no last-seen date, and dating it from our own ignorance would be
    /// the same error as painting an unprobed hour as an outage.
    /// </remarks>
    public static LastSeenBand LastSeenOf(DateTimeOffset? lastReachableAt, DateTimeOffset now) =>
        lastReachableAt is not { } seen ? LastSeenBand.Never
            : now - seen <= TimeSpan.FromDays(1) ? LastSeenBand.Day
            : now - seen <= TimeSpan.FromDays(7) ? LastSeenBand.Week
            : now - seen <= TimeSpan.FromDays(30) ? LastSeenBand.Month
            : LastSeenBand.Older;

    /// <summary>
    /// A game matches the text box on its name, its own one-line tagline, or its codebase.
    /// </summary>
    /// <remarks>
    /// Here rather than in SQL because the facet counts are computed over the same set the listing
    /// is, and a search term applied in one place and counted in another is two answers to one
    /// question. The cost is a pass over the catalogue per request, which is what every count in the
    /// panel already costs; the point at which that stops being affordable is a <c>GROUP BY</c> per
    /// facet in the database, and the counts would then need pinning against the listing rather than
    /// being the same arithmetic by construction.
    /// </remarks>
    private static bool MatchesText(GameFacetRow row, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var needle = text.Trim();

        return row.Summary.Name.Contains(needle, StringComparison.OrdinalIgnoreCase)
            || (row.Summary.Tagline?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false)
            || (row.Codebase?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private static bool Chosen(GameFacetRow row, GameFilter filter, string? except)
    {
        foreach (var facet in Choices)
        {
            if (string.Equals(facet.Key, except, StringComparison.Ordinal))
            {
                continue;
            }

            // Applied once to the answer, not per token: see FacetChoice.Covers.
            if (facet.SelectionOf(filter) is { } selection
                && !selection.Admits(facet.TokensOf(row).Any(selection.Covers)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The presence facets, which intersect. Every one of them reads a measurement — a protocol the
    /// handshake offered, or an endpoint we completed a TLS connection to.
    /// </summary>
    private static bool Present(GameFacetRow row, GameFilter filter) =>
        filter.MeasuredProtocols.All(
            p => row.Summary.MeasuredProtocols.Contains(p, StringComparer.OrdinalIgnoreCase))
        && (!filter.Tls || row.TlsMeasured);

    private static IEnumerable<FacetGroup> Presence(IReadOnlyList<GameFacetRow> results, GameFilter filter)
    {
        var protocols = results
            .SelectMany(r => r.Summary.MeasuredProtocols)
            .GroupBy(p => p, StringComparer.OrdinalIgnoreCase)
            .Select(g => new { Name = g.Key, Count = g.Count() })
            .ToList();

        // A selected protocol has already narrowed the results, so every remaining game has it: its
        // count is the listing's own size, which is what unchecking it would leave in place.
        var values = protocols
            .Select(p => new FacetValue(
                p.Name,
                Selected(p.Name) ? results.Count : p.Count,
                Selected(p.Name),
                IsUnknown: false))
            .Concat(filter.MeasuredProtocols
                .Where(p => !protocols.Any(known => string.Equals(known.Name, p, StringComparison.OrdinalIgnoreCase)))
                .Select(p => new FacetValue(p, 0, IsSelected: true, IsUnknown: false)))
            .OrderByDescending(v => v.Count)
            .ThenBy(v => v.Token, StringComparer.Ordinal)
            .ToList();

        if (values.Count > 0)
        {
            yield return new FacetGroup(
                FacetKeys.Protocol, FacetEvidence.Measured, FacetKind.Presence, results.Count, values);
        }

        var tls = filter.Tls ? results.Count : results.Count(r => r.TlsMeasured);

        // Rendered only when something was measured. Nothing writes a TLS endpoint today — the
        // crawler dials plaintext — so this group is normally absent, which is the honest rendering
        // of a measurement nobody has taken. It must never be filled in from MSSP's SSL line.
        if (tls > 0 || filter.Tls)
        {
            yield return new FacetGroup(
                FacetKeys.Tls,
                FacetEvidence.Measured,
                FacetKind.Presence,
                results.Count,
                [new FacetValue("yes", tls, filter.Tls, IsUnknown: false)]);
        }

        bool Selected(string protocol) =>
            filter.MeasuredProtocols.Contains(protocol, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>A fixed vocabulary, kept in its declared order because that order is a scale.</summary>
    private static List<FacetValue> Bounded(
        IReadOnlyList<GameFacetRow> domain,
        ChoiceFacet facet,
        IReadOnlyList<string> vocabulary,
        GameFilter filter)
    {
        var selection = facet.SelectionOf(filter);
        var counts = Counts(domain, facet);

        return
        [
            .. vocabulary
                .Select(token => new FacetValue(
                    token,
                    counts.GetValueOrDefault(token)?.Count ?? 0,
                    selection?.Covers(token) ?? false,
                    IsUnknown: false,
                    IsExcluded: (selection?.Covers(token) ?? false) && selection!.Exclude))
                .Where(v => v.Count > 0 || v.IsSelected),
        ];
    }

    /// <summary>
    /// An open-ended vocabulary — codebases, genres — ordered by how much of the catalogue each
    /// covers, capped, with the unknown bucket kept whatever it weighs.
    /// </summary>
    /// <remarks>
    /// The unknown bucket survives the cap deliberately. "Games whose codebase we could not
    /// identify" is a measurement of our own reach and one of the more useful things in the panel,
    /// and it is exactly the value a popularity cap would delete on a well-covered catalogue.
    /// </remarks>
    private static List<FacetValue> Open(
        IReadOnlyList<GameFacetRow> domain,
        ChoiceFacet facet,
        GameFilter filter)
    {
        var selection = facet.SelectionOf(filter);
        var counts = Counts(domain, facet);

        var named = counts
            .Where(c => !string.Equals(c.Key, FacetChoice.UnknownToken, StringComparison.Ordinal))
            .Select(c => new FacetValue(
                Spellings.Commonest(c.Value),
                c.Value.Count,
                selection?.Covers(c.Key) ?? false,
                IsUnknown: false,
                IsExcluded: (selection?.Covers(c.Key) ?? false) && selection!.Exclude))
            .OrderByDescending(v => v.IsSelected)
            .ThenByDescending(v => v.Count)
            .ThenBy(v => v.Token, StringComparer.Ordinal)
            .Take(MaxValues)
            .OrderByDescending(v => v.Count)
            .ThenBy(v => v.Token, StringComparer.Ordinal)
            .ToList();

        var unknown = counts.GetValueOrDefault(FacetChoice.UnknownToken)?.Count ?? 0;
        var unknownSelected = selection?.IsUnknown ?? false;

        if (unknown > 0 || unknownSelected)
        {
            named.Add(new FacetValue(
                FacetChoice.UnknownToken,
                unknown,
                unknownSelected,
                IsUnknown: true,
                IsExcluded: unknownSelected && selection!.Exclude));
        }

        return named;
    }

    /// <summary>
    /// How many games each value covers, and every spelling they used for it.
    /// </summary>
    /// <remarks>
    /// The spellings are kept rather than the first one being taken as the name, so
    /// <see cref="Spellings.Commonest"/> can label the group. A bare count keyed
    /// <see cref="StringComparer.OrdinalIgnoreCase"/> silently promotes whichever row was read first
    /// to naming the value, which put <c>russian</c> beside <c>English</c> on the live panel.
    /// </remarks>
    private static Dictionary<string, List<string>> Counts(
        IReadOnlyList<GameFacetRow> domain,
        ChoiceFacet facet)
    {
        var counts = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in domain)
        {
            foreach (var token in facet.TokensOf(row))
            {
                var key = token ?? FacetChoice.UnknownToken;

                if (!counts.TryGetValue(key, out var spellings))
                {
                    counts[key] = spellings = [];
                }

                spellings.Add(key);
            }
        }

        return counts;
    }

    /// <summary>
    /// One choice facet: which of its values a game is in, and what the filter says about it.
    /// </summary>
    /// <remarks>
    /// <see cref="TokensOf"/> returns a <em>list</em> because one facet's values nest: a game
    /// reached an hour ago is in "the last 24 hours" and in "the last 7 days" both, and the question
    /// people have is the second. Every other facet returns one token, or one null for a game the
    /// facet has no value for.
    /// </remarks>
    private sealed record ChoiceFacet(
        string Key,
        FacetEvidence Evidence,
        Func<GameFacetRow, IReadOnlyList<string?>> TokensOf,
        Func<GameFilter, FacetChoice?> SelectionOf,
        IReadOnlyList<string>? Bounded = null);

    /// <summary>
    /// Every choice facet, in the order the panel shows them: what we measured, then the one thing we
    /// concluded, then what the game says about itself. The order is editorial and the labelling is
    /// not — a reader has to be able to see which part of the panel is evidence.
    /// </summary>
    /// <remarks>
    /// <see cref="FacetKeys.Lineage"/> sits at the head of the declared half rather than the foot of
    /// the measured one because it is a conclusion drawn from a declaration, and the whole of its
    /// evidence is the codebase string immediately below it. <see cref="FacetKeys.CodebaseVersion"/>
    /// follows <see cref="FacetKeys.Codebase"/> for the same reason: the three read as one column,
    /// widening to narrowing, and every step of it is labelled with where it came from.
    /// </remarks>
    private static readonly ChoiceFacet[] Choices =
    [
        new(
            FacetKeys.Band,
            FacetEvidence.Measured,
            r => [FacetTokens.Of(r.Band)],
            f => f.Band is { } band ? FacetChoice.Of(FacetTokens.Of(band)) : null,
            FacetTokens.Bands),
        new(
            FacetKeys.LastSeen,
            FacetEvidence.Measured,
            r => FacetTokens.Reaching(r.LastSeen),
            f => f.LastSeen is { } seen ? FacetChoice.Of(FacetTokens.Of(seen)) : null,
            FacetTokens.LastSeenBands),
        new(FacetKeys.Charset, FacetEvidence.Measured, r => [r.Charset], f => f.Charset),
        new(
            FacetKeys.Lineage,
            FacetEvidence.Derived,
            r => [CodebaseLineage.Of(r.Codebase)],
            f => f.Lineage,
            CodebaseLineage.All),
        new(
            FacetKeys.Codebase,
            FacetEvidence.Declared,
            r => [CodebaseFamily.For(r.Codebase)],
            f => f.Codebase),
        new(
            FacetKeys.CodebaseVersion,
            FacetEvidence.Declared,
            r => [r.Codebase],
            f => f.CodebaseVersion),
        new(FacetKeys.Family, FacetEvidence.Declared, r => [r.Family], f => f.Family),
        new(FacetKeys.Genre, FacetEvidence.Declared, r => [r.Genre], f => f.Genre),
        new(FacetKeys.Language, FacetEvidence.Declared, r => [r.Language], f => f.Language),
    ];
}

/// <summary>
/// How the two derived facets' values are spelled in a URL.
/// </summary>
/// <remarks>
/// The spelling lives beside the enums rather than in whichever surface parses a querystring,
/// because the facet panel emits these tokens and the filter binding reads them: if the two had
/// separate tables, the panel could offer a value its own parser would reject.
/// </remarks>
public static class FacetTokens
{
    public static IReadOnlyList<string> Bands { get; } =
        [.. Enum.GetValues<ActivityBand>().Select(Of)];

    public static IReadOnlyList<string> LastSeenBands { get; } =
        [.. Enum.GetValues<LastSeenBand>().Select(Of)];

    public static IReadOnlyList<string> Sorts { get; } =
        [.. Enum.GetValues<GameSort>().Select(Of)];

    /// <summary>The three windows that nest, widest last.</summary>
    private static readonly string?[] Nested =
        [Of(LastSeenBand.Day), Of(LastSeenBand.Week), Of(LastSeenBand.Month)];

    /// <summary>
    /// Every last-seen value a game in <paramref name="band"/> answers to.
    /// </summary>
    /// <remarks>
    /// The first three nest: a game reached an hour ago is in the last 24 hours, the last 7 days and
    /// the last 30. Cutting them into exclusive rings would make "seen within a week" — the question
    /// people actually have — two clicks that cannot both be made. The tails do not nest, because
    /// <see cref="LastSeenBand.Never"/> is not a longer version of <see cref="LastSeenBand.Older"/>:
    /// a game we have never once reached has no date, and lending it the oldest one would publish
    /// our own ignorance as its outage.
    /// </remarks>
    public static IReadOnlyList<string?> Reaching(LastSeenBand band) => band switch
    {
        LastSeenBand.Day => Nested[..3],
        LastSeenBand.Week => Nested[1..3],
        LastSeenBand.Month => Nested[2..3],
        LastSeenBand.Older => [Of(LastSeenBand.Older)],
        _ => [Of(LastSeenBand.Never)],
    };

    public static string Of(ActivityBand band) => Camel(band.ToString());

    public static string Of(LastSeenBand band) => Camel(band.ToString());

    public static string Of(GameSort sort) => Camel(sort.ToString());

    public static bool TryBand(string? text, out ActivityBand band) => TryRead(text, out band);

    public static bool TryLastSeen(string? text, out LastSeenBand band) => TryRead(text, out band);

    public static bool TrySort(string? text, out GameSort sort) => TryRead(text, out sort);

    /// <summary>
    /// Reads one of the derived vocabularies, forgivingly about separators and strictly about
    /// everything else.
    /// </summary>
    /// <remarks>
    /// Hyphens and underscores are stripped so <c>active-this-week</c>, <c>active_this_week</c> and
    /// <c>activeThisWeek</c> are one facet rather than three near misses. Digits are refused
    /// outright: <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)"/> also accepts the
    /// underlying number, which would make <c>band=0</c> a synonym for whichever member happens to
    /// be declared first — a facet that silently re-points itself the day somebody reorders the
    /// enum. Only the names are public.
    /// </remarks>
    private static bool TryRead<TEnum>(string? text, out TEnum value)
        where TEnum : struct, Enum
    {
        value = default;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var normalised = text.Trim()
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal);

        if (normalised.Length == 0 || normalised.All(char.IsAsciiDigit))
        {
            return false;
        }

        return Enum.TryParse(normalised, ignoreCase: true, out value) && Enum.IsDefined(value);
    }

    private static string Camel(string name) => char.ToLowerInvariant(name[0]) + name[1..];
}
