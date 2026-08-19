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
/// Every hard state here came off a real game: M*U*S*H disagrees with itself on version, Eldertale
/// sits at a measured zero, Aardwolf publishes its count only in the connect screen, and Midnight Sun
/// II answers with nothing countable. Eldertale, Midnight Sun II and Hollow Bell each show a listing
/// row with no number, for three different reasons, which the <c>uncounted</c>/<c>unreachable</c>
/// switches keep apart. Archived and suppressed entries are the design handoff's own exemplars, not
/// real games. Replaced by a Postgres implementation behind the same interfaces; no page changes when
/// it is.
/// </remarks>
public sealed class FixtureGameQueries : IGameQueries, IAvailabilityHistory
{
    /// <summary>Fixed, so a rendered page is the same page tomorrow and a test can assert on it.</summary>
    public static readonly DateTimeOffset Now = new(2026, 7, 30, 20, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The same sample floor <c>NpgsqlGameQueries</c> ranks on, so the demo page and the measured
    /// one describe their table the same way. The window comes from the span asked for.
    /// </summary>
    private const int MinimumSamples = 24;

    private static readonly GameSummary Mush = new(
        Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"), "m-u-s-h", "M*U*S*H",
        "The PennMUSH development server.", LifecycleState.Active, IsClaimed: false,
        PlayersNow: 15, Codebase: "PennMUSH 1.8.8p0", MeasuredProtocols: ["MSSP", "CHARSET"],
        LastReachableAt: Now.AddMinutes(-4), Growth: GrowthDirection.Up);

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
        PlayersNow: 219, Codebase: null, MeasuredProtocols: ["MSSP", "GMCP", "MCCP", "MSDP"],
        LastReachableAt: Now.AddMinutes(-40), Growth: GrowthDirection.Down);

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
        PlayersNow: 71, Codebase: null, MeasuredProtocols: ["MSSP", "MCCP"],
        LastReachableAt: Now.AddDays(-2));

    // Claimed, and the owner turned republication off. Stated without editorial (spec §8).
    private static readonly GameSummary Ashen = new(
        Guid.Parse("aaaaaaaa-0000-0000-0000-000000000007"), "ashen-court", "Ashen Court",
        "Courtly intrigue, low fantasy. Application required.", LifecycleState.Active,
        IsClaimed: true, PlayersNow: 9, Codebase: "Evennia", MeasuredProtocols: ["MSSP", "GMCP", "TLS"],
        LastReachableAt: Now.AddMinutes(-9), Growth: GrowthDirection.Steady);

    /// <summary>
    /// Stopped answering six weeks ago and has not been archived: unreachable, and not uncounted.
    /// </summary>
    /// <remarks>
    /// The counter-example the <c>uncounted</c>/<c>unreachable</c> pair needs: Midnight Sun answers
    /// and cannot be counted, this one cannot be reached at all — its grid is empty rather than
    /// hatched, since we never got in to fail to count.
    /// </remarks>
    private static readonly GameSummary HollowBell = new(
        Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a"), "hollow-bell", "Hollow Bell",
        "Has not answered since June. Still probed, and one answer puts it back.",
        LifecycleState.Dark, IsClaimed: false,
        PlayersNow: null, Codebase: "PennMUSH 1.8.5", MeasuredProtocols: ["MSSP"],
        LastReachableAt: Now.AddDays(-44));

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

    // Declares GENRE Adult, so the default listing leaves it out and the bar's checkbox brings it back.
    private static readonly GameSummary Cinder = new(
        Guid.Parse("aaaaaaaa-0000-0000-0000-000000000009"), "cinder", "Cinder",
        "Declares adult content, so it is off the default listing until the box is ticked.",
        LifecycleState.Active, IsClaimed: false,
        PlayersNow: 4, Codebase: "Evennia", MeasuredProtocols: ["MSSP"],
        LastReachableAt: Now.AddMinutes(-25));

    private static readonly GameSummary[] All =
    [
        .. new[]
            {
                Mush, Eldertale, Aardwolf, MidnightSun, Enormous, Ashen, HollowBell, Gaslight,
                Verdigris, Cinder,
            }
            .Select(Labelled),
    ];

    /// <summary>
    /// The two bare values on a listing row, given the label the rest of the site puts on everything.
    /// </summary>
    /// <remarks>
    /// Both are dated from the probe that produced them, not the current instant, so a stale value
    /// (like Gaslight Row's three-year-old codebase) says so. All three count sources are exercised:
    /// M*U*S*H's <c>WHO</c>, Aardwolf's parsed connect screen (still measured), and Ashen Court's
    /// MSSP <c>PLAYERS</c> (declared, not measured).
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
    /// The filtering is not reimplemented here — a fixture with its own idea of what a filter means
    /// can pass a test the real query fails. This owes the shared search only one row per game.
    /// </remarks>
    public Task<GameListing> SearchAsync(
        GameFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var games = SortWindows.Of(filter.Sort) is { } span
            ? All.Select(g => g with { PlayersOverWindow = OverWindow(g, span) })
            : All.AsEnumerable();

        return Task.FromResult(FacetedSearch.Search([.. games.Select(FacetRow)], filter));
    }

    /// <summary>
    /// What a fixture game's counts add up to over a window, read off the same invented week the
    /// heatmap draws.
    /// </summary>
    /// <remarks>
    /// Derived rather than tabulated, so a listing sort can't contradict the grid on the game page.
    /// Null where the grid has no counted hour. The median is the lower of the two middle values on
    /// an even count, matching <c>percentile_disc(0.5)</c> rather than averaging the pair.
    /// </remarks>
    private static PresenceWindow? OverWindow(GameSummary game, TimeSpan window)
    {
        var counted = Activity(game).Where(c => c.IsCounted).Select(c => c.Count!.Value).Order().ToList();

        if (counted.Count == 0)
        {
            return null;
        }

        var samples = (int)Math.Round(counted.Count * (window.TotalDays / 7));

        return new PresenceWindow(
            window,
            counted[(int)Math.Ceiling(counted.Count / 2.0) - 1],
            counted[^1],
            samples);
    }

    /// <summary>A listing with no panel — the same search, projected.</summary>
    public async Task<IReadOnlyList<GameSummary>> ListAsync(
        GameFilter filter,
        CancellationToken cancellationToken = default) =>
        (await SearchAsync(filter, cancellationToken)).Games;

    /// <summary>
    /// One game's facet values.
    /// </summary>
    /// <remarks>
    /// Bands are assigned rather than derived, since there's no real presence series to derive them
    /// from. Midnight Sun is <see cref="ActivityBand.Quiet"/>, never <see cref="ActivityBand.Dark"/>:
    /// being uncountable is not being absent (spec §5.2).
    /// </remarks>
    private static GameFacetRow FacetRow(GameSummary game) => new(
        game,
        Band(game),
        FacetedSearch.LastSeenOf(game.LastReachableAt, Now),

        // An endpoint we opened, never an SSL line in a self-description.
        TlsMeasured: Endpoints(game).Any(e => e.TlsMeasured),
        Charset: Charset(game),
        Language: game.Slug is "midnight-sun" ? "Swedish" : "English",
        Codebase: game.Codebase,
        Family: Family(game),
        Genre: Genre(game),

        // Through the same predicate the database reads, not a slug test of its own.
        IsAdult: AdultContent.Declared(Genre(game), AdultMaterial(game)),
        Uncounted: Uncounted(game),
        Unreachable: FacetedSearch.NotReachedRecently(game.LastReachableAt, Now),
        Growth: game.Growth);

    /// <summary>
    /// Whether every hour this game answered in produced no number.
    /// </summary>
    /// <remarks>
    /// Read off the fixture's own grid, not a slug switch, so the panel can't promise something the
    /// heatmap contradicts. Both clauses matter: Eldertale (measured zero every hour) is not this,
    /// Hollow Bell (never answered) is not this either — only Midnight Sun (answered every hour,
    /// produced no number) is uncounted.
    /// </remarks>
    private static bool Uncounted(GameSummary game)
    {
        var week = Activity(game);

        return week.Any(c => c.IsUnmeasurable) && !week.Any(c => c.IsCounted);
    }

    private static ActivityBand Band(GameSummary g) => g.Slug switch
    {
        "gaslight-row" or "verdigris" => ActivityBand.Archived,
        "eldertale" => ActivityBand.ActiveThisWeek,
        "midnight-sun" => ActivityBand.Quiet,

        "hollow-bell" => ActivityBand.Dark,
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
        "cinder" => AdultContent.Genre,
        _ => null,
    };

    /// <summary>
    /// MSSP <c>ADULT MATERIAL</c>, which most games never send at all — and a game that did not
    /// send it has not answered the question either way.
    /// </summary>
    /// <remarks>
    /// Cinder House declares its content through <c>GENRE</c> instead, which is the way three of the
    /// four games this catches in production do it. The flag is the other way, and the reason the
    /// rule reads both.
    /// </remarks>
    private static string? AdultMaterial(GameSummary g) => g.Slug switch
    {
        "ashen-court" => "0",
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
            Changes: Changes(summary),
            Reachable: Reach(summary)));
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

    /// <summary>
    /// The addresses, with the history the real table keeps.
    /// </summary>
    /// <remarks>
    /// m-u-s-h carries a <c>gone</c> row on purpose, per §7.5 (nothing is deleted): the departed
    /// address renders as departed, beside the one that replaced it.
    /// </remarks>
    private static GameEndpointView[] Endpoints(GameSummary g) => g.Slug switch
    {
        "m-u-s-h" =>
        [
            Endpoint("mush.pennmush.org", 4201, "telnet", tls: false, sinceDays: 2200),
            Endpoint("mush.pennmush.org", 4202, "tls", tls: true, sinceDays: 900),
            Endpoint("mush.example.net", 4201, "telnet", tls: false, sinceDays: 2900, goneDays: 2200),
        ],
        "aardwolf" => [Endpoint("aardmud.org", 4000, "telnet", tls: false, sinceDays: 4000)],
        "midnight-sun" => [Endpoint("midnightsun2.org", 3000, "telnet", tls: false, sinceDays: 1500)],
        "batmud" => [Endpoint("bat.org", 23, "telnet", tls: false, sinceDays: 5000)],
        "ashen-court" => [Endpoint("ashen.example", 4000, "tls", tls: true, sinceDays: 600)],
        "hollow-bell" => [Endpoint("hollowbell.example", 4201, "telnet", tls: false, sinceDays: 2400)],
        "gaslight-row" => [Endpoint("gaslight.example", 4201, "telnet", tls: false, sinceDays: 3000)],
        "verdigris" => [Endpoint("verdigris.example", 6250, "telnet", tls: false, sinceDays: 800)],
        _ => [Endpoint("eldertale.example", 4000, "telnet", tls: false, sinceDays: 1200)],
    };

    /// <param name="goneDays">How long ago we last reached it, for an address the game has left.</param>
    private static GameEndpointView Endpoint(
        string host, int port, string kind, bool tls, int sinceDays, int? goneDays = null) => new(
        host,
        port,
        kind,
        TlsMeasured: tls,
        Now.AddDays(-sinceDays),
        goneDays is { } left ? Now.AddDays(-left) : Now,
        goneDays is null ? "active" : "gone");

    private static string? Screen(GameSummary g) => g.Slug switch
    {
        "m-u-s-h" => FixtureScreens.Mush,
        "aardwolf" => FixtureScreens.Aardwolf,
        "midnight-sun" => FixtureScreens.MidnightSun,
        "batmud" => FixtureScreens.Enormous,

        // Ashen Court's owner asked us not to republish theirs; we hold it all the same and the
        // crawler still reads it as an identity signal (§7.3) — held-and-withheld, not absent.
        "ashen-court" => FixtureScreens.Mush,

        // An archived game's last screen is preserved and labelled, not withdrawn.
        "gaslight-row" => FixtureScreens.Mush,
        _ => null,
    };

    /// <summary>
    /// The disagreement is real and observed: M*U*S*H's MSSP says 1.8.8p0 while its banner says
    /// 1.8.7, and each game declares GMCP in a hand-typed field it has never offered.
    /// </summary>
    private static CapabilityRow[] Capabilities(GameSummary g)
    {
        bool Measured(string p) => g.MeasuredProtocols.Contains(p);

        CapabilityState State(string p) => Measured(p) ? CapabilityState.Present : CapabilityState.Absent;

        return
        [
            new("MSSP", State("MSSP"), CapabilityState.Unknown, Now.AddMinutes(-4)),
            new("CHARSET", State("CHARSET"), CapabilityState.Unknown, Now.AddMinutes(-4)),
            new("MCCP", State("MCCP"),
                Measured("MCCP") ? CapabilityState.Present : CapabilityState.Unknown, Now.AddMinutes(-4)),
            new("TLS", State("TLS"), CapabilityState.Unknown, Now.AddMinutes(-4)),
            new("GMCP", State("GMCP"), CapabilityState.Present, Now.AddYears(-6)),
            new("MXP", CapabilityState.Unknown, CapabilityState.Unknown, null),
        ];
    }

    /// <summary>
    /// All three cell states. Wednesday's daytime gap is a real outage; the scattered hatched hours
    /// are probes that answered and produced no number.
    /// </summary>
    private static ActivityCell[] Activity(GameSummary g)
    {
        double[] shape =
        [
            0.40, 0.24, 0.16, 0.10, 0.06, 0, 0, 0, 0, 0, 0, 0,
            0.14, 0.18, 0.22, 0.26, 0.34, 0.45, 0.62, 0.82, 1.00, 0.94, 0.72, 0.50,
        ];
        double[] byDay = [0.72, 1.00, 0.78, 0.84, 0.92, 0.88, 0.66];

        // Scaled to the game's own count, not one number for every game — the demo ranking reads off
        // this series, and a league table whose every row ties can't show whether it ranks anything.
        var peak = g.PlayersNow ?? 17;

        var cells = new List<ActivityCell>(168);

        // A game we cannot count is hatched in every hour it answered, not dark and not zero.
        var uncountable = g.PlayersNow is null;

        // An archived game was not reachable in any of these hours, so every cell is empty rather
        // than hatched — hatching would claim we got in and could not count.
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
    /// The codebase entry reuses the summary's own chip rather than a second one written here, so a
    /// page and its listing can't disagree about when the value was last confirmed.
    /// </remarks>
    private static Dictionary<string, ProvenanceChip> Declared(GameSummary g)
    {
        var declared = new Dictionary<string, ProvenanceChip>(StringComparer.Ordinal)
        {
            ["codebase"] = g.CodebaseProvenance
                ?? Chip("CODEBASE", "not identified", FieldSource.Banner, g.LastReachableAt ?? Now),
            ["genre"] = new ProvenanceChip("Modern Supernatural", FieldSource.Mssp, Now.AddDays(-14), IsStale: false),
            ["created"] = new ProvenanceChip("2009", FieldSource.Mssp, Now.AddYears(-6), IsStale: true),
            ["language"] = new ProvenanceChip("English", FieldSource.Mssp, Now.AddYears(-6), IsStale: true),
        };

        // Every link is also a declared field, since on the real page it is one.
        foreach (var link in Reach(g))
        {
            declared[link.Field.ToLowerInvariant()] =
                new ProvenanceChip(link.Shown, link.Source, link.LastConfirmedAt, link.IsStale);
        }

        return declared;
    }

    /// <summary>
    /// The ways to reach each game's people, where the fixture gives them any.
    /// </summary>
    /// <remarks>
    /// m-u-s-h carries what MSSP can express; ashen-court carries the half only an owner can — the
    /// distinction the dashboard panel exists to explain.
    /// </remarks>
    private static QuickLink[] Reach(GameSummary g) => g.Slug switch
    {
        "m-u-s-h" =>
        [
            Reachable(LinkKind.Website, "WEBSITE", "https://www.pennmush.org/", FieldSource.Mssp, 30),
            Reachable(LinkKind.Email, "CONTACT", "mailto:nobody@example.org", FieldSource.Mssp, 30, "nobody@example.org"),
        ],
        "ashen-court" =>
        [
            Reachable(LinkKind.Website, "WEBSITE", "https://ashen.example/", FieldSource.Owner, 5),
            Reachable(LinkKind.Wiki, "WIKI", "https://wiki.ashen.example/", FieldSource.Owner, 5),
            Reachable(LinkKind.Forum, "FORUM", "https://forum.ashen.example/", FieldSource.Owner, 5),
            Reachable(LinkKind.Discord, "DISCORD", "https://discord.gg/example", FieldSource.Mssp, 40),
            Reachable(LinkKind.Mastodon, "MASTODON", "https://mastodon.example/@ashen", FieldSource.Owner, 5),
        ],
        _ => [],
    };

    private static QuickLink Reachable(
        LinkKind kind,
        string field,
        string href,
        FieldSource source,
        int ageDays,
        string? shown = null) =>
        new(kind, field, href, shown ?? href, source, Now.AddDays(-ageDays), IsStale: ageDays > 90);

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

            // Found six weeks ago: painting the unmeasured half of the window as unreachable would
            // record our own ignorance as a fact about the game.
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

            // Answered for years then stopped, run still open — an interval can say we tried and got
            // nothing, where an empty heatmap cell cannot.
            "hollow-bell" =>
            [
                Span(2400, 44, AvailabilityState.Reachable, FailureCause.None),
                Span(44, null, AvailabilityState.Unreachable, FailureCause.Timeout),
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
    /// Every figure is counted off the fixture games by the same rules the Postgres reader uses:
    /// archived games are out, the protocol denominator is games with a reachable interval, and a
    /// protocol no fixture game offers is unmeasured rather than nought per cent. <c>MSSP</c> is the
    /// one protocol whose absence is written down as an absence, since we ask for it on every probe.
    /// </remarks>
    public Task<EcosystemDashboard> EcosystemAsync(CancellationToken cancellationToken = default)
    {
        var listed = All.Where(g => g.State is not LifecycleState.Archived).ToList();
        var handshaked = listed
            .Where(g => Intervals(g).Any(i => i.State is AvailabilityState.Reachable))
            .ToList();

        var codebases = listed.Select(g => g.Codebase).OfType<string>().ToList();

        var offeredAnywhere = handshaked
            .SelectMany(g => g.MeasuredProtocols)
            .ToHashSet(StringComparer.Ordinal);

        // Games whose MSSP report we hold — not every listed game (Midnight Sun offers no MSSP).
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

                // The one honest negative: asked on every probe, so silence is an answer.
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

            // Nothing here is a capability transition — the page must say the curve isn't drawable yet.
            CapabilityTransitions: 0,
            CodebaseUsage.Of(codebases, listed.Count),
            protocols));
    }

    /// <summary>
    /// The rankings, computed off the same sampled week the heatmap renders.
    /// </summary>
    /// <remarks>
    /// The activity grid is a series of measured samples, so median and peak are real arithmetic over
    /// it. A game whose every sample is unmeasurable produces no median and drops out — reading those
    /// as zeros is the failure §5.4 exists to prevent.
    /// </remarks>
    public Task<Rankings> RankingsAsync(
        RankingSpan span = RankingSpan.Week,
        CancellationToken cancellationToken = default)
    {
        var listed = All.Where(g => g.State is not LifecycleState.Archived).ToList();

        // The grid is one week of hours, so a wider span is the same shape measured for longer:
        // median and peak are unchanged and only the basis grows.
        var weeks = span.Days() / 7d;

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
                (int)Math.Round(entry.Counts.Count * weeks),
                span.Days()))
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

        // Only the game the row arrow already marks Up — matching NpgsqlGameQueries.TrendingThisWeekAsync,
        // which reads the same classification the arrow does rather than a bare sort on the numbers.
        var trending = listed
            .Where(g => g.Growth == GrowthDirection.Up)
            .Select(TrendingRow)
            .OrderByDescending(row => row.Change)
            .ToList();

        return Task.FromResult(new Rankings(
            Now, span.Window(), MinimumSamples, listed.Count, busiest.Count, busiest, spells, trending)
        {
            Span = span,
        });
    }

    private static TrendingGame TrendingRow(GameSummary g) =>
        new(g.Slug, g.Name, Median: 15, PriorMedian: 8, Samples: 40, PriorSamples: 38);

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
                Event("verdigris", Now.AddDays(-6), "connection refused"),
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
    /// Named from the catalogue rather than retyped, so a drifted name or id can't send a reader to a
    /// page about something else (spec §5.7).
    /// </remarks>
    private static FeedEntry Event(string slug, DateTimeOffset at, string detail)
    {
        var game = All.Single(g => g.Slug == slug);
        return new FeedEntry(game.Id, game.Slug, game.Name, at, detail);
    }

    public Task<IReadOnlyList<RecentGameChange>> RecentFieldChangesAsync(
        int limit, int perGameLimit = 3, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<RecentGameChange> changes =
        [
            Change("m-u-s-h", "CODEBASE", FieldSource.Mssp, "PennMUSH 1.8.7p0", "PennMUSH 1.8.8p0", Now.AddMinutes(-4)),
            Change("ashen-court", "PLAYERS", FieldSource.Mssp, "6", "9", Now.AddMinutes(-9)),
            Change("aardwolf", "PLAYERS", FieldSource.Banner, null, "219", Now.AddMinutes(-40)),
            Change("cinder", "GENRE", FieldSource.Mssp, null, "Adult", Now.AddMinutes(-25)),
        ];

        return Task.FromResult<IReadOnlyList<RecentGameChange>>([.. changes.Take(limit)]);
    }

    private static RecentGameChange Change(
        string slug, string field, FieldSource source, string? oldValue, string newValue, DateTimeOffset at)
    {
        var game = All.Single(g => g.Slug == slug);
        return new RecentGameChange(game.Id, game.Slug, game.Name, field, source, oldValue, newValue, at);
    }
}
