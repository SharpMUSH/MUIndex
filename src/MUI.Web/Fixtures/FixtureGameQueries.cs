using System.Globalization;

using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Web.Data;

namespace MUI.Web.Fixtures;

/// <summary>
/// Answers <see cref="IGameQueries"/> and <see cref="IAvailabilityHistory"/> from data observed on
/// real servers, so the site can be built and reviewed before the database exists.
/// </summary>
/// <remarks>
/// <para>
/// Every hard state here came off a real game: M*U*S*H disagrees with itself (its MSSP says
/// <c>1.8.8p0</c> and its banner says <c>1.8.7</c>), Eldertale sits at a measured zero, Aardwolf
/// publishes its count only in the connect screen, and Midnight Sun II answers with nothing we can
/// count at all. A fixture of well-behaved games renders a site that never has to show its hard
/// states, and those states are the product.
/// </para>
/// <para>
/// The archived and suppressed entries are the design handoff's own exemplars rather than real
/// games, deliberately: asserting that a named game is dead, or that its owner asked us to stop
/// republishing it, is exactly the kind of claim this site may not make without a measurement.
/// </para>
/// <para>
/// Replaced by a Postgres implementation behind the same interfaces. No page changes when it is.
/// </para>
/// </remarks>
public sealed class FixtureGameQueries : IGameQueries, IAvailabilityHistory
{
    /// <summary>Fixed, so a rendered page is the same page tomorrow and a test can assert on it.</summary>
    public static readonly DateTimeOffset Now = new(2026, 7, 30, 20, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The same week and the same sample floor <c>NpgsqlGameQueries</c> ranks on, so the demo page
    /// and the measured one describe their table the same way.
    /// </summary>
    private static readonly TimeSpan RankingWindow = TimeSpan.FromDays(7);

    private const int MinimumSamples = 24;

    private static readonly GameSummary Mush = new(
        Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"), "m-u-s-h", "M*U*S*H",
        "The PennMUSH development server.", LifecycleState.Active, IsClaimed: false,
        PlayersNow: 15, Codebase: "PennMUSH 1.8.8p0", MeasuredProtocols: ["MSSP", "CHARSET"],
        LastReachableAt: Now.AddMinutes(-4));

    private static readonly GameSummary Eldertale = new(
        Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002"), "eldertale", "Eldertale Online",
        null, LifecycleState.Active, IsClaimed: false,
        PlayersNow: 0, Codebase: "PennMUSH 1.8.8p0", MeasuredProtocols: ["MSSP", "CHARSET"],
        LastReachableAt: Now.AddHours(-3));

    // No MSSP PLAYERS, no pre-login WHO. Its count exists only because the connect screen states it.
    private static readonly GameSummary Aardwolf = new(
        Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003"), "aardwolf", "Aardwolf MUD",
        "Counted from the connect screen, which is the only place this game publishes a number.",
        LifecycleState.Active, IsClaimed: false,
        PlayersNow: 219, Codebase: null, MeasuredProtocols: ["MSSP", "GMCP", "MCCP2", "MSDP"],
        LastReachableAt: Now.AddMinutes(-40));

    // Answers, but nothing we can count. Renders "count unknown" — never a zero.
    private static readonly GameSummary MidnightSun = new(
        Guid.Parse("aaaaaaaa-0000-0000-0000-000000000004"), "midnight-sun", "Midnight Sun II",
        null, LifecycleState.Active, IsClaimed: false,
        PlayersNow: null, Codebase: "Midnight Sun", MeasuredProtocols: [],
        LastReachableAt: Now.AddHours(-1));

    private static readonly GameSummary Enormous = new(
        Guid.Parse("aaaaaaaa-0000-0000-0000-000000000006"), "batmud", "BatMUD",
        "An intro screen long enough that the frame has to explain why it stopped.",
        LifecycleState.Active, IsClaimed: false,
        PlayersNow: 71, Codebase: null, MeasuredProtocols: ["MSSP", "MCCP2"],
        LastReachableAt: Now.AddDays(-2));

    // Claimed, and the owner turned republication off. Stated without editorial (spec §8).
    private static readonly GameSummary Ashen = new(
        Guid.Parse("aaaaaaaa-0000-0000-0000-000000000007"), "ashen-court", "Ashen Court",
        "Courtly intrigue, low fantasy. Application required.", LifecycleState.Active,
        IsClaimed: true, PlayersNow: 9, Codebase: "Evennia", MeasuredProtocols: ["MSSP", "GMCP", "TLS"],
        LastReachableAt: Now.AddMinutes(-9));

    private static readonly GameSummary Gaslight = new(
        Guid.Parse("aaaaaaaa-0000-0000-0000-000000000005"), "gaslight-row", "Gaslight Row",
        "Ceased answering in March 2023. We still try the door every week.",
        LifecycleState.Archived, IsClaimed: false,
        PlayersNow: null, Codebase: "PennMUSH 1.8.5", MeasuredProtocols: [],
        LastReachableAt: Now.AddDays(-1237));

    private static readonly GameSummary Verdigris = new(
        Guid.Parse("aaaaaaaa-0000-0000-0000-000000000008"), "verdigris", "Verdigris",
        "Stopped answering in 2024; the host still refuses the port every week.",
        LifecycleState.Archived, IsClaimed: false,
        PlayersNow: null, Codebase: "TinyMUX 2.12", MeasuredProtocols: [],
        LastReachableAt: Now.AddDays(-700));

    private static readonly GameSummary[] All =
    [
        .. new[] { Mush, Eldertale, Aardwolf, MidnightSun, Enormous, Ashen, Gaslight, Verdigris }
            .Select(Labelled),
    ];

    /// <summary>
    /// The two bare values on a listing row, given the label the rest of the site puts on everything.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both are dated from the probe that produced them — a game's last reachable moment — because a
    /// fixture that stamped every fact with the current instant would render a listing on which
    /// nothing is ever old, and the state worth showing here is exactly the old one. Gaslight Row
    /// last answered in 2023, so its codebase is three years unconfirmed and says so.
    /// </para>
    /// <para>
    /// The sources are not decoration either, and all three a count can have are here. M*U*S*H
    /// answers a pre-login <c>WHO</c>. Aardwolf's number exists only because its connect screen
    /// states it, which we parse off the screen ourselves — <em>measured</em>, like the <c>WHO</c>,
    /// and still its own source. Ashen Court publishes <c>PLAYERS</c> in MSSP and answers no
    /// pre-login <c>WHO</c>, which is the commonest way a directory ends up quoting a game's own
    /// report as a measurement — <em>declared</em>, and the row that makes the labelling visible.
    /// </para>
    /// </remarks>
    private static GameSummary Labelled(GameSummary g)
    {
        var at = g.LastReachableAt ?? Now;

        return g with
        {
            PlayersNowProvenance = g.PlayersNow is { } count
                ? Chip("PLAYERS", count.ToString(CultureInfo.InvariantCulture), CountSource(g), at)
                : null,
            CodebaseProvenance = g.Codebase is { } codebase
                ? Chip("CODEBASE", codebase, FieldSource.Mssp, at)
                : null,
        };
    }

    private static FieldSource CountSource(GameSummary g) => g.Slug switch
    {
        "aardwolf" => FieldSource.Banner,
        "ashen-court" => FieldSource.Mssp,
        _ => FieldSource.Who,
    };

    /// <summary>
    /// A labelled fact, aged against the field's own expected-refresh window (spec §5.6) rather than
    /// against a judgement made here — the registry is the one place that says how old is old.
    /// </summary>
    private static ProvenanceChip Chip(
        string field, string value, FieldSource source, DateTimeOffset at) =>
        new(value, source, at, FieldRegistry.Instance.IsStale(field, at, Now));

    /// <summary>
    /// The listing and its facets, through the same <see cref="FacetedSearch"/> the database uses.
    /// </summary>
    /// <remarks>
    /// The filtering is emphatically not reimplemented here. A fixture with its own idea of what a
    /// filter means can pass a test the real query fails, and this one already did: it read
    /// <c>band=archived</c> as lifting the archive exclusion and Postgres did not, so one filter had
    /// two answers and only the fixture's was ever exercised. All this owes the shared search now is
    /// one row per game.
    /// </remarks>
    public Task<GameListing> SearchAsync(
        GameFilter filter, CancellationToken cancellationToken = default) =>
        Task.FromResult(FacetedSearch.Search([.. All.Select(FacetRow)], filter));

    /// <summary>A listing with no panel — the same search, projected.</summary>
    public async Task<IReadOnlyList<GameSummary>> ListAsync(
        GameFilter filter,
        CancellationToken cancellationToken = default) =>
        (await SearchAsync(filter, cancellationToken)).Games;

    /// <summary>
    /// One game's facet values.
    /// </summary>
    /// <remarks>
    /// The bands are assigned rather than derived, because there is no presence series here to
    /// derive them from — which is the fixture's whole nature, and why every page it renders carries
    /// the demo banner. Midnight Sun is <see cref="ActivityBand.Quiet"/> and never
    /// <see cref="ActivityBand.Dark"/> on purpose: every one of its counts is unmeasurable, and
    /// being uncountable is not being absent (spec §5.2).
    /// </remarks>
    private static GameFacetRow FacetRow(GameSummary game) => new(
        game,
        Band(game),
        FacetedSearch.LastSeenOf(game.LastReachableAt, Now),

        // An endpoint we opened, never an SSL line in a self-description — the same distinction the
        // real query draws, so the demo cannot show a facet the database could not.
        TlsMeasured: Endpoints(game).Any(e => e.TlsMeasured),
        Charset: Charset(game),
        Language: game.Slug is "midnight-sun" ? "Swedish" : "English",
        Codebase: game.Codebase,
        Family: Family(game),
        Genre: Genre(game));

    private static ActivityBand Band(GameSummary g) => g.Slug switch
    {
        "gaslight-row" or "verdigris" => ActivityBand.Archived,
        "eldertale" => ActivityBand.ActiveThisWeek,
        "midnight-sun" => ActivityBand.Quiet,
        _ => ActivityBand.PlayersNow,
    };

    /// <summary>
    /// The <em>negotiated</em> encoding — which most servers never negotiate, so most of these are
    /// null and land in the facet's "not measured" bucket rather than being filled in from MSSP.
    /// </summary>
    private static string? Charset(GameSummary g) =>
        g.MeasuredProtocols.Contains("CHARSET") ? "UTF-8" : null;

    /// <summary>
    /// MSSP <c>FAMILY</c>, which is the coarse taxonomy <c>CODEBASE</c>'s version strings are too
    /// fine to serve as. BatMUD and Aardwolf declare neither, which is the common case and why the
    /// facet has an unknown bucket at all.
    /// </summary>
    private static string? Family(GameSummary g) => g.Codebase switch
    {
        var c when c is not null && c.StartsWith("PennMUSH", StringComparison.Ordinal) => "PennMUSH",
        var c when c is not null && c.StartsWith("TinyMUX", StringComparison.Ordinal) => "TinyMUX",
        "Evennia" => "Evennia",
        _ => null,
    };

    /// <summary>
    /// MSSP <c>GENRE</c>. Midnight Sun declares none, which is the commonest state of a hand-typed
    /// field and the one a demo of well-behaved games would never show — the facet's unknown bucket
    /// exists for it, and a fixture with nothing in that bucket does not exercise it.
    /// </summary>
    private static string? Genre(GameSummary g) => g.Slug switch
    {
        "ashen-court" => "Historical",
        "aardwolf" or "batmud" => "Fantasy",
        "m-u-s-h" or "eldertale" => "Development",
        _ => null,
    };

    /// <summary>
    /// The demo has no accounts, so nothing here ever asks — but the interface is the interface.
    /// </summary>
    public Task<GameSummary?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(All.FirstOrDefault(g => g.Id == id));

    /// <summary>The same page by the identifier that does not move (spec §5.7).</summary>
    public Task<GamePage?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
        All.FirstOrDefault(g => g.Id == id) is { } game
            ? FindAsync(game.Slug, cancellationToken)
            : Task.FromResult<GamePage?>(null);

    public Task<GamePage?> FindAsync(string slug, CancellationToken cancellationToken = default)
    {
        var summary = All.FirstOrDefault(g => g.Slug == slug);
        if (summary is null)
        {
            return Task.FromResult<GamePage?>(null);
        }

        var intervals = Intervals(summary);

        return Task.FromResult<GamePage?>(new GamePage(
            summary,
            Description: Description(summary),
            Endpoints: Endpoints(summary),
            ConnectScreen: Screen(summary),
            ConnectScreenSuppressed: summary.Slug is "ashen-court",
            ReachableFraction: Reachability.FractionReachable(intervals, TimeSpan.FromDays(90), Now),
            LongestOutage: Reachability.LongestOutage(intervals, TimeSpan.FromDays(90), Now),
            Capabilities: Capabilities(summary),
            Activity: Activity(summary),
            Declared: Declared(summary),
            Changes: Changes(summary)));
    }

    public Task<IReadOnlyList<AvailabilityInterval>> ForGameAsync(
        Guid gameId, CancellationToken cancellationToken = default)
    {
        var summary = All.FirstOrDefault(g => g.Id == gameId);
        return Task.FromResult<IReadOnlyList<AvailabilityInterval>>(
            summary is null ? [] : Intervals(summary));
    }

    private static string? Description(GameSummary g) => g.Slug switch
    {
        "m-u-s-h" => "The development server for PennMUSH. Open to visitors, and the place a change "
            + "to the codebase is tried before anybody else runs it.",
        "aardwolf" => "A large, long-running combat MUD. It states its player count on the connect "
            + "screen and nowhere a machine can ask for it.",
        "ashen-court" => "Courtly intrigue in a low-fantasy setting. Applications are read weekly.",
        _ => null,
    };

    private static GameEndpointView[] Endpoints(GameSummary g) => g.Slug switch
    {
        "m-u-s-h" =>
        [
            new GameEndpointView("mush.pennmush.org", 4201, "telnet", TlsMeasured: false),
            new GameEndpointView("mush.pennmush.org", 4202, "tls", TlsMeasured: true),
        ],
        "aardwolf" => [new GameEndpointView("aardmud.org", 4000, "telnet", TlsMeasured: false)],
        "midnight-sun" => [new GameEndpointView("midnightsun2.org", 3000, "telnet", TlsMeasured: false)],
        "batmud" => [new GameEndpointView("bat.org", 23, "telnet", TlsMeasured: false)],
        "ashen-court" => [new GameEndpointView("ashen.example", 4000, "tls", TlsMeasured: true)],
        "gaslight-row" => [new GameEndpointView("gaslight.example", 4201, "telnet", TlsMeasured: false)],
        "verdigris" => [new GameEndpointView("verdigris.example", 6250, "telnet", TlsMeasured: false)],
        _ => [new GameEndpointView("eldertale.example", 4000, "telnet", TlsMeasured: false)],
    };

    private static string? Screen(GameSummary g) => g.Slug switch
    {
        "m-u-s-h" => FixtureScreens.Mush,
        "aardwolf" => FixtureScreens.Aardwolf,
        "midnight-sun" => FixtureScreens.MidnightSun,
        "batmud" => FixtureScreens.Enormous,

        // An archived game's last screen is preserved and labelled, not withdrawn.
        "gaslight-row" => FixtureScreens.Mush,
        _ => null,
    };

    /// <summary>
    /// The disagreement is real and observed: M*U*S*H's MSSP says 1.8.8p0 while its banner says
    /// 1.8.7, and every one of these games declares GMCP in a hand-typed field it has never offered.
    /// That row is the one a reader should not be able to scroll past.
    /// </summary>
    private static CapabilityRow[] Capabilities(GameSummary g)
    {
        bool Measured(string p) => g.MeasuredProtocols.Contains(p);

        CapabilityState State(string p) => Measured(p) ? CapabilityState.Present : CapabilityState.Absent;

        return
        [
            new("MSSP", State("MSSP"), CapabilityState.Unknown, Now.AddMinutes(-4)),
            new("CHARSET", State("CHARSET"), CapabilityState.Unknown, Now.AddMinutes(-4)),
            new("MCCP2", State("MCCP2"),
                Measured("MCCP2") ? CapabilityState.Present : CapabilityState.Unknown, Now.AddMinutes(-4)),
            new("TLS", State("TLS"), CapabilityState.Unknown, Now.AddMinutes(-4)),
            new("GMCP", State("GMCP"), CapabilityState.Present, Now.AddYears(-6)),
            new("MXP", CapabilityState.Unknown, CapabilityState.Unknown, null),
        ];
    }

    /// <summary>
    /// All three cell states, because a grid that only ever shows two is not a test of anything.
    /// Wednesday's daytime gap is a real outage; the scattered hatched hours are probes that
    /// answered and produced no number.
    /// </summary>
    private static ActivityCell[] Activity(GameSummary g)
    {
        // A week with a shape: night owls after midnight, nobody at all between five and noon, and
        // an evening peak that is highest on Tuesday and Friday. A flat grid would let a renderer
        // that never computes anything look right.
        double[] shape =
        [
            0.40, 0.24, 0.16, 0.10, 0.06, 0, 0, 0, 0, 0, 0, 0,
            0.14, 0.18, 0.22, 0.26, 0.34, 0.45, 0.62, 0.82, 1.00, 0.94, 0.72, 0.50,
        ];
        double[] byDay = [0.72, 1.00, 0.78, 0.84, 0.92, 0.88, 0.66];

        // Scaled to the game's own count rather than to one number for every game. A fixture where
        // Aardwolf reports 219 players and draws the same week as a nine-player game is internally
        // inconsistent in the way that matters here: the demo ranking reads off this series, and a
        // league table whose every row ties is a table that cannot show whether it ranks anything.
        var peak = g.PlayersNow ?? 17;

        var cells = new List<ActivityCell>(168);

        // A game we cannot count is hatched in every hour it answered. It is not dark, and it is
        // emphatically not a week of measured zeros.
        var uncountable = g.PlayersNow is null;

        // An archived game was not reachable in any of these hours, so every cell is empty. Hatching
        // them would say we got in and could not count, which is a claim about a game that has not
        // answered the door since 2023.
        var dark = g.State is LifecycleState.Archived or LifecycleState.Dark;

        for (var day = 0; day < 7; day++)
        {
            for (var hour = 0; hour < 24; hour++)
            {
                if (dark)
                {
                    cells.Add(new ActivityCell(day, hour, null, Probed: false));
                    continue;
                }

                cells.Add((day, hour, uncountable) switch
                {
                    // Wednesday's daytime: we could not reach the game at all. Empty cells.
                    (2, >= 2 and <= 13, _) => new ActivityCell(day, hour, null, Probed: false),

                    (_, _, true) => new ActivityCell(day, hour, null, Probed: true),

                    // One hour that answered and produced no number. Hatched, and not a zero.
                    (4, 15, _) => new ActivityCell(day, hour, null, Probed: true),

                    _ => new ActivityCell(
                        day, hour, (int)Math.Round(shape[hour] * byDay[day] * peak), Probed: true),
                });
            }
        }

        return [.. cells];
    }

    /// <summary>
    /// What the game says about itself, as the page shows it.
    /// </summary>
    /// <remarks>
    /// The codebase entry is the summary's own chip rather than a second one written here. A page
    /// and a listing that disagreed about when the same value was last confirmed would be two
    /// answers to one question, and this file had exactly that: every game's codebase was dated four
    /// minutes ago, including one that has not answered the door since 2023.
    /// </remarks>
    private static Dictionary<string, ProvenanceChip> Declared(GameSummary g) => new()
    {
        ["codebase"] = g.CodebaseProvenance
            ?? Chip("CODEBASE", "not identified", FieldSource.Banner, g.LastReachableAt ?? Now),
        ["genre"] = new ProvenanceChip("Modern Supernatural", FieldSource.Mssp, Now.AddDays(-14), IsStale: false),
        ["created"] = new ProvenanceChip("2009", FieldSource.Mssp, Now.AddYears(-6), IsStale: true),
        ["language"] = new ProvenanceChip("English", FieldSource.Mssp, Now.AddYears(-6), IsStale: true),
    };

    private static ChangeEntry[] Changes(GameSummary g) =>
    [
        new(Now.AddDays(-16), g.Codebase is { } codebase
            ? $"MSSP CODEBASE changed to {codebase}"
            : "banner fingerprint changed; codebase still not identified"),
        new(Now.AddDays(-50), "unreachable for 2d · connection refused"),
    ];

    /// <summary>
    /// Availability as intervals (spec §5.3): a run of reachable time is one row, and only a change
    /// of state or cause opens another. A hundred consecutive timeouts are one interval.
    /// </summary>
    private static AvailabilityInterval[] Intervals(GameSummary g)
    {
        AvailabilityInterval Span(double fromDaysAgo, double? toDaysAgo, AvailabilityState state, FailureCause cause) =>
            new()
            {
                GameId = g.Id,
                State = state,
                FromAt = Now.AddDays(-fromDaysAgo),
                ToAt = toDaysAgo is { } to ? Now.AddDays(-to) : null,
                Cause = cause,
            };

        return g.Slug switch
        {
            // Dark since March 2023 and still knocked on weekly. The strip is one long hatched run.
            "gaslight-row" =>
            [
                Span(5100, 1237, AvailabilityState.Reachable, FailureCause.None),
                Span(1237, null, AvailabilityState.Unreachable, FailureCause.Dns),
            ],
            "verdigris" =>
            [
                Span(3600, 700, AvailabilityState.Reachable, FailureCause.None),
                Span(700, null, AvailabilityState.Unreachable, FailureCause.Refused),
            ],

            // Found six weeks ago: the first half of the 90-day window predates anything we
            // measured, and painting it as unreachable would record our own ignorance as a fact
            // about the game.
            "eldertale" =>
            [
                Span(44, 12, AvailabilityState.Reachable, FailureCause.None),
                Span(12, 11, AvailabilityState.Degraded, FailureCause.HandshakeStalled),
                Span(11, null, AvailabilityState.Reachable, FailureCause.None),
            ],
            "midnight-sun" =>
            [
                Span(2000, 60, AvailabilityState.Reachable, FailureCause.None),
                Span(60, 58, AvailabilityState.Unreachable, FailureCause.Timeout),
                Span(58, null, AvailabilityState.Reachable, FailureCause.None),
            ],
            _ =>
            [
                Span(4000, 51, AvailabilityState.Reachable, FailureCause.None),
                Span(51, 49, AvailabilityState.Unreachable, FailureCause.Refused),
                Span(49, 31, AvailabilityState.Reachable, FailureCause.None),
                Span(31, 30, AvailabilityState.Degraded, FailureCause.HandshakeStalled),
                Span(30, null, AvailabilityState.Reachable, FailureCause.None),
            ],
        };
    }

    /// <summary>
    /// The dashboard, derived from this fixture's own games rather than from a table of percentages.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A hand-written set of shares would be the one thing this file may not contain: a page whose
    /// whole subject is that a ratio has a denominator, showing ratios that never had one. So every
    /// figure here is counted off the eight games above by the same rules the Postgres reader uses —
    /// archived games are out, the protocol denominator is the games with a reachable interval, and a
    /// protocol no fixture game offers is <em>unmeasured</em> rather than nought per cent.
    /// </para>
    /// <para>
    /// <c>MSSP</c> is the one protocol whose absence is written down as an absence, here as in the
    /// crawler: we ask for it by name on every probe, so a game that answered and never engaged it has
    /// declined a question that was put. Every other silence stays a silence.
    /// </para>
    /// </remarks>
    public Task<EcosystemDashboard> EcosystemAsync(CancellationToken cancellationToken = default)
    {
        var listed = All.Where(g => g.State is not LifecycleState.Archived).ToList();
        var handshaked = listed
            .Where(g => Intervals(g).Any(i => i.State is AvailabilityState.Reachable))
            .ToList();

        var codebases = listed.Select(g => g.Codebase).OfType<string>().ToList();
        var families = codebases
            .Select(CodebaseFamily.Of)
            .GroupBy(family => family, StringComparer.OrdinalIgnoreCase)
            .Select(group => new MeasuredShare(group.First(), group.Count(), codebases.Count))
            .OrderByDescending(share => share.Count)
            .ThenBy(share => share.Label, StringComparer.Ordinal)
            .ToList();

        var offeredAnywhere = handshaked
            .SelectMany(g => g.MeasuredProtocols)
            .ToHashSet(StringComparer.Ordinal);

        // Games whose MSSP report we hold — not every listed game, because Midnight Sun answers and
        // offers no MSSP at all. That is the second denominator, and it is a different set.
        var reporting = listed
            .Where(g => g.MeasuredProtocols.Contains("MSSP", StringComparer.Ordinal))
            .ToList();

        var protocols = EcosystemProtocols.Headline
            .Concat(offeredAnywhere.Order(StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Select(protocol => new ProtocolAdoption(
                protocol,
                offeredAnywhere.Contains(protocol)
                    ? handshaked.Count(g => g.MeasuredProtocols.Contains(protocol, StringComparer.Ordinal))
                    : null,

                // The one honest negative: asked for on every probe, so silence is an answer.
                protocol is "MSSP"
                    ? handshaked.Count(g => !g.MeasuredProtocols.Contains("MSSP", StringComparer.Ordinal))
                    : 0,
                handshaked.Count,
                reporting.Count(g => Capabilities(g)
                    .Any(c => c.Protocol == protocol && c.Declared is CapabilityState.Present)),
                reporting.Count))
            .ToList();

        return Task.FromResult(new EcosystemDashboard(
            Now,
            listed.Count,
            handshaked.Count,
            reporting.Count,
            Now.AddMinutes(-4),

            // Nothing in this fixture's change feed is a capability transition, which is the state a
            // young crawl is really in: the page has to say the curve is not drawable yet, and a
            // fixture that faked one would be teaching the page to lie.
            CapabilityTransitions: 0,
            new CodebaseUsage(families, codebases.Count, listed.Count - codebases.Count),
            protocols));
    }

    /// <summary>
    /// The rankings, computed off the same sampled week the heatmap renders.
    /// </summary>
    /// <remarks>
    /// The activity grid <em>is</em> a series of measured samples, so the median and the peak here are
    /// real arithmetic over it rather than numbers typed beside a name. A game whose every sample is
    /// unmeasurable produces no median and drops out of the table — which is the behaviour that has to
    /// be visible, because reading those as zeros is the failure §5.4 exists to prevent.
    /// </remarks>
    public Task<Rankings> RankingsAsync(CancellationToken cancellationToken = default)
    {
        var listed = All.Where(g => g.State is not LifecycleState.Archived).ToList();

        var busiest = listed
            .Select(g => (Game: g, Counts: Activity(g)
                .Where(c => c.IsCounted)
                .Select(c => c.Count!.Value)
                .Order()
                .ToList()))
            .Where(entry => entry.Counts.Count >= MinimumSamples)
            .Select(entry => new BusiestGame(
                entry.Game.Slug,
                entry.Game.Name,
                entry.Counts[entry.Counts.Count / 2],
                entry.Counts[^1],
                entry.Counts.Count))
            .OrderByDescending(row => row.Median)
            .ThenByDescending(row => row.Peak)
            .ThenBy(row => row.Name, StringComparer.Ordinal)
            .ToList();

        var spells = listed
            .Select(g => (Game: g, Open: Intervals(g)
                .FirstOrDefault(i => i.IsOpen && i.State is AvailabilityState.Reachable)))
            .Where(entry => entry.Open is not null)
            .Select(entry => new ReachableSpell(entry.Game.Slug, entry.Game.Name, entry.Open!.FromAt))
            .OrderBy(spell => spell.Since)
            .ToList();

        return Task.FromResult(new Rankings(
            Now, RankingWindow, MinimumSamples, listed.Count, busiest.Count, busiest, spells));
    }

    public Task<LivenessFeeds> FeedsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new LivenessFeeds(
            NewlyDiscovered:
            [
                Event("eldertale", Now.AddHours(-2),
                    "found via REFERRAL from M*U*S*H · answered MSSP on first probe · PennMUSH 1.8.8p0"),
                Event("batmud", Now.AddDays(-1),
                    "found in the TinTin mudlist · answered MSSP · 214-row intro screen"),
            ],
            WentDark:
            [
                Event("verdigris", Now.AddDays(-6),
                    "unreachable since 24 July · connection refused · page stays, we keep knocking weekly"),
            ],
            CameBack:
            [
                Event("aardwolf", Now.AddMinutes(-40),
                    "Answered again after 26 months dark. Two hundred and nineteen players on within the hour."),
            ]));

    /// <summary>
    /// One feed event, taking its id and name from the game it is about.
    /// </summary>
    /// <remarks>
    /// Named from the catalogue rather than retyped, because a feed entry whose name or id drifted
    /// from the game's would send a reader to a page about something else — and the id is the whole
    /// point of the entry carrying one (spec §5.7).
    /// </remarks>
    private static FeedEntry Event(string slug, DateTimeOffset at, string detail)
    {
        var game = All.Single(g => g.Slug == slug);
        return new FeedEntry(game.Id, game.Slug, game.Name, at, detail);
    }
}
