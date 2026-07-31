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

    public const string Codebase = "codebase";

    /// <summary>
    /// The codebase with its version taken off. Its own key, because <see cref="Codebase"/> is the
    /// counted facet over raw values and the two answer different questions (see
    /// <c>GameFilter.CodebaseFamily</c>).
    /// </summary>
    public const string CodebaseFamily = "codebase-family";

    public const string Family = "family";

    public const string Genre = "genre";
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
    Measured,

    Declared,
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
public sealed record FacetChoice(string? Value)
{
    /// <summary>
    /// The querystring spelling of the absence. Tilde-prefixed so it cannot collide with a real
    /// value: a game may legitimately be called <c>none</c> and none may legitimately be a genre.
    /// </summary>
    public const string UnknownToken = "~unknown";

    /// <summary>Games for which this facet has no value at all.</summary>
    public static readonly FacetChoice Unknown = new((string?)null);

    public static FacetChoice Of(string value) => new(value);

    public bool IsUnknown => Value is null;

    /// <summary>What this selection is called in a URL.</summary>
    public string Token => Value ?? UnknownToken;

    public static FacetChoice Parse(string token) =>
        string.Equals(token, UnknownToken, StringComparison.Ordinal) ? Unknown : Of(token);

    /// <summary>Whether a game whose value for this facet is <paramref name="actual"/> matches.</summary>
    public bool Matches(string? actual) =>
        IsUnknown ? actual is null : string.Equals(actual, Value, StringComparison.OrdinalIgnoreCase);
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
public sealed record FacetValue(string Token, int Count, bool IsSelected, bool IsUnknown);

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
    /// How many values an open-ended facet offers. Codebases are versioned strings and there are
    /// hundreds of them; the tail is reachable by search and by URL, and the panel says as much.
    /// </summary>
    public const int MaxValues = 12;

    public static GameListing Search(IReadOnlyList<GameFacetRow> rows, GameFilter filter)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(filter);

        // Archived games leave the default listing and nothing else (spec §7.5). Asking for the
        // archived band *is* asking for them, so the toggle does not also have to be set — the
        // database and the demo fixture disagreed about that until this became one function.
        var wantsArchived = filter.IncludeArchived || filter.Band is ActivityBand.Archived;

        // The codebase family narrows the base set rather than being offered as a counted facet,
        // which is deliberate: a reference page links here to say "the games running PennMUSH", and
        // the facet counts on the page it lands on should be counts *within* that codebase. It sits
        // beside the text search for the same reason — both are the question, not an answer to it.
        var baseRows = rows
            .Where(r => (wantsArchived || r.Band is not ActivityBand.Archived)
                && MatchesText(r, filter.Text)
                && CodebaseFamily.Matches(r.Codebase, filter.CodebaseFamily))
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

        return new GameListing([.. results.Select(r => r.Summary)], groups);
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

            if (facet.SelectionOf(filter) is { } selection
                && !facet.TokensOf(row).Any(selection.Matches))
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
                    counts.GetValueOrDefault(token),
                    selection?.Matches(token) ?? false,
                    IsUnknown: false))
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
                c.Key, c.Value, selection?.Matches(c.Key) ?? false, IsUnknown: false))
            .OrderByDescending(v => v.IsSelected)
            .ThenByDescending(v => v.Count)
            .ThenBy(v => v.Token, StringComparer.Ordinal)
            .Take(MaxValues)
            .OrderByDescending(v => v.Count)
            .ThenBy(v => v.Token, StringComparer.Ordinal)
            .ToList();

        var unknown = counts.GetValueOrDefault(FacetChoice.UnknownToken);
        var unknownSelected = selection?.IsUnknown ?? false;

        if (unknown > 0 || unknownSelected)
        {
            named.Add(new FacetValue(FacetChoice.UnknownToken, unknown, unknownSelected, IsUnknown: true));
        }

        return named;
    }

    private static Dictionary<string, int> Counts(IReadOnlyList<GameFacetRow> domain, ChoiceFacet facet)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in domain)
        {
            foreach (var token in facet.TokensOf(row))
            {
                var key = token ?? FacetChoice.UnknownToken;
                counts[key] = counts.GetValueOrDefault(key) + 1;
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
    /// Every choice facet, in the order the panel shows them: what we measured first, then what the
    /// game says about itself. The order is editorial and the labelling is not — a reader has to be
    /// able to see which half of the panel is evidence.
    /// </summary>
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
        new(FacetKeys.Codebase, FacetEvidence.Declared, r => [r.Codebase], f => f.Codebase),
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

    public static bool TryBand(string? text, out ActivityBand band) => TryRead(text, out band);

    public static bool TryLastSeen(string? text, out LastSeenBand band) => TryRead(text, out band);

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
