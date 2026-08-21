namespace MUI.Catalog;

/// <summary>
/// The name of every facet, in the one spelling the query, the read API and the site's GET form all
/// use (spec §9).
/// </summary>
/// <remarks>
/// One spelling shared by the querystring, the API and the page, so they cannot drift apart.
/// </remarks>
public static class FacetKeys
{
    public const string Text = "q";

    public const string Archived = "archived";

    /// <summary>
    /// Whether games declaring adult content are in the answer. Off unless asked for — see
    /// <c>GameFilter.IncludeAdult</c> for why the default lives in the query language rather than
    /// in the filter.
    /// </summary>
    public const string Adult = "adult";

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
    /// Splitting one codebase across patchlevels would waste panel slots
    /// (<see cref="FacetedSearch.MaxValues"/>) saying the same word. The exact string is
    /// <see cref="CodebaseVersion"/>.
    /// </remarks>
    public const string Codebase = "codebase";

    /// <summary>
    /// The codebase exactly as the game reports it, version and all.
    /// </summary>
    /// <remarks>
    /// Kept as its own facet because "which patchlevels are running" is a real question the family
    /// facet collapses away.
    /// </remarks>
    public const string CodebaseVersion = "version";

    /// <summary>
    /// The old spelling of <see cref="Codebase"/>, still accepted in a querystring.
    /// </summary>
    /// <remarks>
    /// Kept as an alias because reference pages link with this spelling; removing it would silently
    /// return the unfiltered catalogue instead of erroring.
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

    /// <summary>This week's median against last week's — ours, not a claim from the game (spec §5).</summary>
    public const string Trending = "trending";

    /// <summary>
    /// Games we reached and hold no readable count for — <b>never games we counted at nought</b>.
    /// </summary>
    /// <remarks>
    /// Distinct from <c>band=quiet</c>: that band also holds games measured at zero, and conflating
    /// the two would publish parser limits as an empty game (rules 2, 4). Backed by
    /// <c>PresenceSample.Unmeasurable</c> — recent rows exist but none carry a number. A game with no
    /// recent rows at all is neither counted nor uncounted (§5.4's third state) and is not offered
    /// here.
    /// </remarks>
    public const string Uncounted = "uncounted";

    /// <summary>
    /// Games we could not reach recently. Read off the availability series, never off a gap.
    /// </summary>
    /// <remarks>
    /// Orthogonal to <see cref="Uncounted"/> — "couldn't get in" vs "got in, couldn't count" are
    /// independent causes of a missing number, and <c>band</c> can't express both at once. Not the
    /// archive: archiving is editorial (<see cref="Archived"/>), this is a measurement. Never
    /// inferred from a hole in the presence series (rule 2 forbids naming a cause for that) — comes
    /// from <c>game.last_reachable_at</c>, which the availability intervals write and which can tell
    /// "could not reach" from "never probed".
    /// </remarks>
    public const string Unreachable = "unreachable";

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
/// Named for what each reads, never for a superlative — there is no "busiest", because
/// <c>/rankings</c> already owns that word for a different statistic (median over a windowed sample
/// floor). There is no "recently listed" sort either: the only date is <c>game.first_seen_at</c>,
/// when our crawler reached a game — a fact about our schedule, not the hobby (rule 5). That framing
/// lives in the <em>newly discovered</em> feed instead.
/// </remarks>
public enum GameSort
{
    /// <summary>
    /// Alphabetical — the one order that ranks nobody.
    /// </summary>
    Name,

    /// <summary>
    /// Most players counted on right now, first — and the default.
    /// </summary>
    /// <remarks>
    /// Ranks on a measurement, not our judgement: games we could not count follow as a labeled
    /// group, never as a tail of zeroes (see <see cref="GameSorting"/>).
    /// </remarks>
    Players,

    /// <summary>Most recently reached first.</summary>
    Reached,

    /// <summary>Most recently discovered first — when we first saw this address, not when it answered.</summary>
    Discovered,

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
/// Three fixed spans, not a free parameter — a caller-tunable window could be dialed until a
/// favored game ranks top. Seven/thirty/ninety days match what the rest of the site already
/// measures over.
/// <para>
/// A median requires a sample floor (shared with <c>NpgsqlGameQueries.MinimumRankingSamples</c>)
/// because a median of four probes isn't meaningful; a peak needs no floor since one observation is
/// true however few there were. Median, never mean, for the same reason <c>/rankings</c> uses
/// median (migration 0019) — a mean is skewed by a single busy evening.
/// </para>
/// </remarks>
public static class SortWindows
{
    /// <summary>
    /// How many counted samples an average needs before it may rank a game.
    /// </summary>
    /// <remarks>
    /// Same floor as <c>NpgsqlGameQueries.MinimumRankingSamples</c>, so the listing and the rankings
    /// page cannot disagree about how much evidence is enough. Lives here because the sort enforces
    /// it.
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
    /// A window with no samples ranks nothing either way — no median of nothing, no largest of no
    /// readings — and the query omits the row.
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
/// An unknown never sorts as zero: most of the catalogue has nothing countable (<c>WHO</c> past the
/// parser, no <c>PLAYERS</c> reported), and null-as-zero would pile those at the bottom of "players
/// on now" indistinguishable from games measured empty — the site's central claim, broken. Ranked
/// games sort first, in order; unranked follow as one labeled group. <see cref="IsUnranked"/> is the
/// single source both the ordering and the label read, so they cannot disagree.
/// </remarks>
public static class GameSorting
{
    /// <summary>Whether this sort has nothing to rank a game by — never "whether it is zero".</summary>
    /// <remarks>
    /// Unranked covers both an absent window and one too thin to average — both mean "cannot place
    /// this game in order", so the break says so in one sentence rather than dropping the game or
    /// floating it as a nought.
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
            GameSort.Discovered => game.FirstSeenAt is null,
            _ => false,
        };
    }

    public static IReadOnlyList<GameSummary> Apply(IEnumerable<GameSummary> games, GameSort sort)
    {
        ArgumentNullException.ThrowIfNull(games);

        // Ranked before unranked always, then the sort's own key, then name — a total order so
        // identical requests don't reshuffle. Each key reads zero (or MinValue) for sorts it doesn't
        // belong to; that's safe only because unranked games are already pushed below ranked ones by
        // the first clause — zero here means "this sort ignores this column," never "measured
        // nothing." Reached and Discovered share one date-typed clause since exactly one is active at
        // a time.
        var ordered = games
            .OrderBy(g => IsUnranked(g, sort) ? 1 : 0)
            .ThenByDescending(g => sort is GameSort.Players ? g.PlayersNow ?? 0 : 0)
            .ThenByDescending(g => sort switch
            {
                GameSort.Reached => g.LastReachableAt ?? DateTimeOffset.MinValue,
                GameSort.Discovered => g.FirstSeenAt ?? DateTimeOffset.MinValue,
                _ => DateTimeOffset.MinValue,
            })
            .ThenByDescending(g => Ranked(g, sort))
            .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase);

        return [.. ordered];
    }

    /// <summary>
    /// What a window sort ranks on, as one number — the typical count, or the largest reading.
    /// </summary>
    /// <remarks>
    /// Zero for sorts that read no window and for games this sort can't rank — never a claim that
    /// nobody was there; those games sort below the break instead.
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
/// The distinction is the product: "we watched this happen" vs "the game typed this into
/// <c>mush.cnf</c>, maybe in 2017" are different claims, and presenting them identically would be
/// the exact thing this site exists to avoid.
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
    /// A third word rather than reusing the other two: <c>codebase = MUSH</c> is neither measured
    /// (nothing on the wire says it) nor declared (the game never said it) — it's our own grouping,
    /// and it's the one kind of fact that must not go unlabelled.
    /// </remarks>
    Derived,
}

/// <summary>
/// Which spelling names a group of values that differ only in case.
/// </summary>
/// <remarks>
/// Groups case-insensitively (<c>russian</c>/<c>Russian</c> are one language). The label is the
/// commonest spelling, ordinal-tiebroken, so it's a function of the set rather than of read order —
/// otherwise the same catalogue could label a facet differently across renders.
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
    /// No way to ask for the complement — "games without GMCP" isn't answerable: a capability is
    /// written <c>true</c> when observed and otherwise not written at all (see
    /// <c>FieldObservations.Measured</c>), since this client only requests some options and an
    /// unasked server hasn't declined. An unchecked box meaning "no" would publish our own
    /// instrumentation gaps as fact.
    /// </remarks>
    Presence,
}

/// <summary>
/// One choice-facet selection: a value the data carries, or the absence of one.
/// </summary>
/// <remarks>
/// Absence is first-class rather than an empty string: "codebase unknown" and "no codebase" are
/// different, real questions, and modelling absence as a value would let one be typed for the other.
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
    /// A facet has three states: absent (not asked), a value (only these), an excluded value
    /// (anything but these) — without the third, "not Evennia" is unaskable. <c>!</c> rather than
    /// <c>-</c> because values may start with a hyphen; a literal leading <c>!</c> is escaped as
    /// <c>!!</c> so no value is unreachable (see <see cref="Parse"/>).
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
    /// A method, not a property: a record's generated <c>ToString</c> prints every public property,
    /// and a property returning another <see cref="FacetChoice"/> would make printing one recurse
    /// until the stack overflows — which happened, surfacing as an unrelated test failing inside an
    /// assertion message.
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
    /// Separate from <see cref="Admits"/> deliberately: a facet can hand a row several tokens
    /// (reached an hour ago is in "last day", "last week" and "last month"), and inverting per-token
    /// would make an excluded selection mean "some token differs" — true for nearly every
    /// multi-token row. Polarity is applied once, to the answer.
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
/// The first three bands nest — reached an hour ago is in all of them — because "seen within a
/// week" is the common question and exclusive rings would split it across clicks that can't both be
/// made.
/// <para>
/// <see cref="Never"/> is a value, not an unknown, because it exists to be told apart from
/// <see cref="Older"/>: a game never once reached is a different fact from one reached in 2023, and
/// rendering them the same would let our own crawl history read as somebody's outage.
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
/// <see cref="Count"/> is computed from the same pass that produces the listing, never estimated, so
/// a facet cannot promise results it won't deliver. A value nothing matches is never offered — except
/// a currently selected one, kept visible at zero so it can be undone.
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
    /// A panel that only checked <see cref="IsSelected"/> would draw an included and an excluded
    /// value identically — the one thing a tri-state filter must not do.
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
/// <see cref="Total"/> is what dropping this facet's selection would return — carried rather than
/// summed from <see cref="Values"/>, because an open-ended facet only offers its commonest values and
/// the sum would fall short of the true total.
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
/// Returned together rather than fetched separately, so the panel's counts and the listing can never
/// disagree.
/// </remarks>
public sealed record GameListing(IReadOnlyList<GameSummary> Games, IReadOnlyList<FacetGroup> Facets)
{
    public static readonly GameListing Empty = new([], []);
}

/// <summary>
/// One game reduced to the values every facet reads, so the facets are computed once from one shape.
/// </summary>
/// <remarks>
/// Assembled once per <see cref="IGameQueries"/> implementation (Postgres from <c>game_field</c>
/// rows, the demo fixture from constants); filtering and counting happens once in
/// <see cref="FacetedSearch"/>, so the fixture and the database cannot silently answer a filter
/// differently, as they once did for <c>band=archived</c>.
/// <see cref="Charset"/> is the negotiated charset, never the game's MSSP claim; <see cref="TlsMeasured"/>
/// is a completed TLS connection, never an <c>SSL</c> self-description line — both named for what
/// they are so a later reader can't reach for the declared column by accident.
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
    string? Genre,

    /// <summary>
    /// Whether the game declared adult content, by <see cref="AdultContent"/>'s one rule.
    /// </summary>
    /// <remarks>
    /// Carried on the row rather than re-derived from <see cref="Genre"/>: MSSP's
    /// <c>ADULT MATERIAL</c> flag is independent of genre and has no facet of its own.
    /// </remarks>
    bool IsAdult,

    /// <summary>
    /// We hold recent presence rows for this game and not one of them carries a number
    /// (<see cref="FacetKeys.Uncounted"/>).
    /// </summary>
    /// <remarks>
    /// Distinct from a measured zero (<see cref="ActivityBand.Quiet"/> holds both, so
    /// <see cref="Band"/> can't tell them apart) and from "not measured at all" (those are told apart
    /// by <see cref="Unreachable"/> and the last-seen facet rather than by inventing a cause here,
    /// rule 2). See <see cref="FacetKeys.Uncounted"/> for why the distinction matters.
    /// </remarks>
    bool Uncounted,

    /// <summary>
    /// We have not reached this game recently (<see cref="FacetKeys.Unreachable"/>).
    /// </summary>
    /// <remarks>
    /// Read from <c>game.last_reachable_at</c>, never inferred from a hole in the presence series
    /// (see <see cref="FacetKeys.Unreachable"/>). A game never reached is included here; the
    /// last-seen facet is where that stays separately visible as <see cref="LastSeenBand.Never"/>.
    /// </remarks>
    bool Unreachable,

    /// <summary>This week's median against last week's (<see cref="FacetKeys.Trending"/>).</summary>
    GrowthDirection? Growth = null);

/// <summary>
/// Turns a filter and a set of games into the listing plus every facet's counts.
/// </summary>
/// <remarks>
/// Counts are measured against what each choice would actually return. A
/// <see cref="FacetKind.Choice"/> facet replaces its own selection, so its values are counted with
/// that selection lifted but every other filter applied — the count beside <c>quiet</c> is what
/// clicking <c>quiet</c> would return. A <see cref="FacetKind.Presence"/> facet intersects, so its
/// values are counted against the current results. A value with no games is never offered — a facet
/// that could be clicked into an empty listing would be lying about the catalogue.
/// </remarks>
public static class FacetedSearch
{
    /// <summary>
    /// How many values an open-ended facet offers. The tail is reachable by search and by URL, and
    /// the panel says as much.
    /// </summary>
    /// <remarks>
    /// <see cref="FacetKeys.CodebaseVersion"/>, not <see cref="FacetKeys.Codebase"/>, is the facet
    /// that needs this cap now: a reader scanning for a codebase wants families; one wanting a
    /// specific patchlevel already knows its name.
    /// </remarks>
    public const int MaxValues = 20;

    public static GameListing Search(IReadOnlyList<GameFacetRow> rows, GameFilter filter)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(filter);

        // Archived games leave the default listing entirely (spec §7.5). Asking for the archived
        // band already means asking for them, so the toggle need not also be set.
        var wantsArchived = filter.IncludeArchived || filter.Band is ActivityBand.Archived;

        // Games declaring adult content leave the default listing too, same as archiving — but
        // unlike archiving, selecting genre=Adult does not lift the exclusion. band=archived is a
        // bounded facet value that's always drawn even when it returns nothing; genre is
        // open-ended, so a value nothing matches is simply never offered, and lifting on selection
        // would make every other value in the dropdown a promise the listing wouldn't keep. The
        // adult checkbox is the only way in.
        var baseRows = rows
            .Where(r => (wantsArchived || r.Band is not ActivityBand.Archived)
                && (filter.IncludeAdult || !r.IsAdult)
                && MatchesText(r, filter.Text))
            .ToList();

        var results = baseRows.Where(r => Chosen(r, filter, null) && Present(r, filter)).ToList();

        var groups = new List<FacetGroup>();

        foreach (var facet in Choices)
        {
            // This facet's own selection lifted, so a count is what choosing the value returns.
            var domain = baseRows.Where(r => Chosen(r, filter, facet.Key) && Present(r, filter)).ToList();
            var values = facet.Bounded is { } vocabulary

                // Counted over the domain, but which rows exist at all is decided by baseRows —
                // the catalogue as this reader is looking at it, before any facet selection. See
                // Bounded: that is what keeps a scale the same length whatever else is filtered.
                ? Bounded(domain, baseRows, facet, vocabulary, filter)
                : Open(domain, facet, filter);

            if (values.Count > 0)
            {
                groups.Add(new FacetGroup(
                    facet.Key, facet.Evidence, FacetKind.Choice, domain.Count, values));
            }
        }

        groups.AddRange(Presence(results, filter));

        // Ordered after counting, never before: every count is taken over a set, which has no
        // order, so sorting here can't move a number.
        return new GameListing(GameSorting.Apply(results.Select(r => r.Summary), filter.Sort), groups);
    }

    /// <summary>
    /// The last-seen band a game is in, given when it was last reachable.
    /// </summary>
    /// <remarks>
    /// Null is <see cref="LastSeenBand.Never"/>, never the oldest bucket — dating an unreached game
    /// from our own ignorance would be the same error as painting an unprobed hour as an outage.
    /// </remarks>
    public static LastSeenBand LastSeenOf(DateTimeOffset? lastReachableAt, DateTimeOffset now) =>
        lastReachableAt is not { } seen ? LastSeenBand.Never
            : now - seen <= TimeSpan.FromDays(1) ? LastSeenBand.Day
            : now - seen <= TimeSpan.FromDays(7) ? LastSeenBand.Week
            : now - seen <= TimeSpan.FromDays(30) ? LastSeenBand.Month
            : LastSeenBand.Older;

    /// <summary>
    /// How long ago a game must have answered for us to still call it reached.
    /// </summary>
    /// <remarks>
    /// One constant read by the activity band, <see cref="NotReachedRecently"/> and both
    /// <see cref="IGameQueries"/> implementations, so "still answering" isn't two different answers
    /// on one page.
    /// </remarks>
    public static readonly TimeSpan RecentlyReachable = TimeSpan.FromDays(30);

    /// <summary>
    /// Whether the availability series says we have not reached this game lately — the
    /// <see cref="FacetKeys.Unreachable"/> fact.
    /// </summary>
    /// <remarks>
    /// Never inferred from missing presence rows — a hole there covers "could not reach" and "never
    /// probed" alike and may not name a cause (rule 2). Reads <c>game.last_reachable_at</c> instead,
    /// which the intervals write. A game never reached is true here (not reached recently);
    /// <see cref="LastSeenBand.Never"/> is where that stays separately visible.
    /// </remarks>
    public static bool NotReachedRecently(DateTimeOffset? lastReachableAt, DateTimeOffset now) =>
        lastReachableAt is not { } seen || now - seen > RecentlyReachable;

    /// <summary>
    /// A game matches the text box on its name, its own one-line tagline, or its codebase.
    /// </summary>
    /// <remarks>
    /// Done here rather than in SQL so the facet counts and the listing are computed over the same
    /// set — a search term applied in one place and counted in another would answer two questions.
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
                [new FacetValue(FacetTokens.Yes, tls, filter.Tls, IsUnknown: false)]);
        }

        bool Selected(string protocol) =>
            filter.MeasuredProtocols.Contains(protocol, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A fixed vocabulary, kept in its declared order because that order is a scale — and kept
    /// whole, because a scale with a rung missing is not the same scale.
    /// </summary>
    /// <remarks>
    /// Every value stays, including ones that return nothing. Dropping empty rows (as an open-ended
    /// facet does) previously meant filtering an unrelated facet could delete rows from a bounded
    /// one, shift the panel, and hide thresholds the reader hadn't chosen — a zero row is a real
    /// answer and stays clickable.
    /// <paramref name="catalogue"/>, not <paramref name="domain"/>, decides which rungs exist: it's
    /// the listing before any facet selection but after text search and the archived/adult
    /// switches — "the catalogue I'm looking at". <paramref name="domain"/> only supplies the
    /// counts.
    /// </remarks>
    private static List<FacetValue> Bounded(
        IReadOnlyList<GameFacetRow> domain,
        IReadOnlyList<GameFacetRow> catalogue,
        ChoiceFacet facet,
        IReadOnlyList<string> vocabulary,
        GameFilter filter)
    {
        var selection = facet.SelectionOf(filter);
        var counts = Counts(domain, facet);
        var ever = Counts(catalogue, facet);

        return
        [
            .. vocabulary
                .Select(token => new FacetValue(
                    token,
                    counts.GetValueOrDefault(token)?.Count ?? 0,
                    selection?.Covers(token) ?? false,
                    IsUnknown: false,
                    IsExcluded: (selection?.Covers(token) ?? false) && selection!.Exclude))
                .Where(v => (ever.GetValueOrDefault(v.Token)?.Count ?? 0) > 0 || v.IsSelected),
        ];
    }

    /// <summary>
    /// An open-ended vocabulary — codebases, genres — ordered by how much of the catalogue each
    /// covers, capped, with the unknown bucket kept whatever it weighs.
    /// </summary>
    /// <remarks>
    /// The unknown bucket survives the popularity cap deliberately — "codebase we couldn't identify"
    /// is a useful measurement and exactly what a cap would otherwise delete.
    /// The reader's own selection also survives, at zero if necessary: since the domain has that
    /// facet's own selection lifted, a combination like
    /// <c>genre=Historical&amp;language=Swedish</c> can leave the chosen value out of the counts
    /// entirely — dropping it from the panel would remove the only control that could undo the
    /// choice responsible for an empty listing.
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

        if (selection is { Value: { } chosen }
            && !named.Any(v => string.Equals(v.Token, chosen, StringComparison.OrdinalIgnoreCase)))
        {
            named.Add(new FacetValue(
                chosen, 0, IsSelected: true, IsUnknown: false, IsExcluded: selection.Exclude));
        }

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
    /// Spellings are kept, not just counted, so <see cref="Spellings.Commonest"/> can label the
    /// group — a bare ordinal-insensitive count would let whichever row was read first name the
    /// value.
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
    /// <see cref="TokensOf"/> returns a list because values can nest — reached an hour ago is in
    /// both "last 24 hours" and "last 7 days". Every other facet returns a single token, or null.
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
    /// <see cref="FacetKeys.Lineage"/> sits ahead of the declared half rather than the end of the
    /// measured one because it's a conclusion drawn from the codebase string beneath it.
    /// <see cref="FacetKeys.Uncounted"/> and <see cref="FacetKeys.Unreachable"/> are two separate
    /// facets, not two values of one, because a game can be either, both, or neither.
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
        new(
            FacetKeys.Uncounted,
            FacetEvidence.Measured,
            r => [r.Uncounted ? FacetTokens.Yes : null],
            f => f.Uncounted,
            FacetTokens.YesOnly),
        new(
            FacetKeys.Unreachable,
            FacetEvidence.Measured,
            r => [r.Unreachable ? FacetTokens.Yes : null],
            f => f.Unreachable,
            FacetTokens.YesOnly),
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
        new(
            FacetKeys.Trending,
            FacetEvidence.Derived,
            r => [r.Growth is { } growth ? FacetTokens.Of(growth) : null],
            f => f.Trending,
            FacetTokens.GrowthDirections),
        new(FacetKeys.Genre, FacetEvidence.Declared, r => [r.Genre], f => f.Genre),
        new(FacetKeys.Language, FacetEvidence.Declared, r => [r.Language], f => f.Language),
    ];
}

/// <summary>
/// How the two derived facets' values are spelled in a URL.
/// </summary>
/// <remarks>
/// Lives beside the enums rather than in the querystring parser, so the panel and the filter
/// binding can never disagree about which tokens are valid.
/// </remarks>
public static class FacetTokens
{
    /// <summary>
    /// The one value a facet has when the question it asks is a yes-or-nothing one.
    /// </summary>
    /// <remarks>
    /// There is no <c>no</c>, and must not be: these facets read a fact we hold or don't —
    /// publishing an opposite value would be our own silence presented as an observation. The
    /// <c>!</c> prefix asks for the complement instead, which is a statement about the listing, not
    /// about a game.
    /// </remarks>
    public const string Yes = "yes";

    /// <summary>That one value, as the bounded vocabulary a counted facet is built from.</summary>
    public static IReadOnlyList<string> YesOnly { get; } = [Yes];

    public static IReadOnlyList<string> Bands { get; } =
        [.. Enum.GetValues<ActivityBand>().Select(Of)];

    public static IReadOnlyList<string> LastSeenBands { get; } =
        [.. Enum.GetValues<LastSeenBand>().Select(Of)];

    public static IReadOnlyList<string> Sorts { get; } =
        [.. Enum.GetValues<GameSort>().Select(Of)];

    public static IReadOnlyList<string> GrowthDirections { get; } =
        [.. Enum.GetValues<GrowthDirection>().Select(Of)];

    /// <summary>The three windows that nest, widest last.</summary>
    private static readonly string?[] Nested =
        [Of(LastSeenBand.Day), Of(LastSeenBand.Week), Of(LastSeenBand.Month)];

    /// <summary>
    /// Every last-seen value a game in <paramref name="band"/> answers to.
    /// </summary>
    /// <remarks>
    /// The first three nest (reached an hour ago is in all of them) so "seen within a week" is one
    /// click. The tails don't nest: <see cref="LastSeenBand.Never"/> is not a longer
    /// <see cref="LastSeenBand.Older"/> — a game never reached has no date, and lending it the
    /// oldest one would publish our own ignorance as its outage.
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

    public static string Of(GrowthDirection direction) => Camel(direction.ToString());

    public static bool TryBand(string? text, out ActivityBand band) => TryRead(text, out band);

    public static bool TryLastSeen(string? text, out LastSeenBand band) => TryRead(text, out band);

    public static bool TrySort(string? text, out GameSort sort) => TryRead(text, out sort);

    public static bool TryGrowthDirection(string? text, out GrowthDirection direction) =>
        TryRead(text, out direction);

    /// <summary>
    /// Reads one of the derived vocabularies, forgivingly about separators and strictly about
    /// everything else.
    /// </summary>
    /// <remarks>
    /// Hyphens/underscores are stripped so <c>active-this-week</c>, <c>active_this_week</c> and
    /// <c>activeThisWeek</c> are one facet. Digits are refused:
    /// <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)"/> also accepts the underlying
    /// number, which would make <c>band=0</c> silently repoint itself if the enum were ever
    /// reordered.
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
