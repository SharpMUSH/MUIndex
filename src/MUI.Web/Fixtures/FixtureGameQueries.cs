using MUI.Catalog;

namespace MUI.Web.Fixtures;

/// <summary>
/// Answers <see cref="IGameQueries"/> from data observed on real servers, so the site can be built
/// and reviewed before the database exists.
/// </summary>
/// <remarks>
/// <para>
/// Every value here was measured, not invented — M*U*S*H's renamed <c>DOING</c> header, Eldertale's
/// name left at the codebase default, Aardwolf's count published only in its connect screen. A
/// fixture of tidy invented games would render a site that never has to show its hard states, and
/// those states are the product.
/// </para>
/// <para>
/// Replaced by a Postgres implementation behind the same interface. No page changes when it is.
/// </para>
/// </remarks>
public sealed class FixtureGameQueries : IGameQueries
{
    private static readonly DateTimeOffset Now = new(2026, 7, 30, 20, 0, 0, TimeSpan.Zero);

    private static readonly GameSummary Mush = new(
        Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"), "m-u-s-h", "M*U*S*H",
        "The PennMUSH development server.", LifecycleState.Active, IsClaimed: false,
        PlayersNow: 15, Codebase: "PennMUSH 1.8.8p0", MeasuredProtocols: ["MSSP", "CHARSET"]);

    private static readonly GameSummary Eldertale = new(
        Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002"), "eldertale", "Eldertale Online",
        null, LifecycleState.Active, IsClaimed: false,
        PlayersNow: 0, Codebase: "PennMUSH 1.8.8p0", MeasuredProtocols: ["MSSP", "CHARSET"]);

    // No MSSP, no pre-login WHO. Its count exists only because the connect screen states it.
    private static readonly GameSummary Aardwolf = new(
        Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003"), "aardwolf", "Aardwolf MUD",
        null, LifecycleState.Active, IsClaimed: false,
        PlayersNow: 219, Codebase: null, MeasuredProtocols: []);

    // Answers, but nothing we can count. Renders "count unknown" — never a zero.
    private static readonly GameSummary MidnightSun = new(
        Guid.Parse("aaaaaaaa-0000-0000-0000-000000000004"), "midnight-sun", "Midnight Sun II",
        null, LifecycleState.Active, IsClaimed: false,
        PlayersNow: null, Codebase: "Midnight Sun", MeasuredProtocols: []);

    private static readonly GameSummary Gaslight = new(
        Guid.Parse("aaaaaaaa-0000-0000-0000-000000000005"), "gaslight-row", "Gaslight Row",
        "Ceased answering in March 2023. We still try the door every week.",
        LifecycleState.Archived, IsClaimed: false,
        PlayersNow: null, Codebase: "PennMUSH 1.8.5", MeasuredProtocols: []);

    private static readonly GameSummary[] All = [Mush, Eldertale, Aardwolf, MidnightSun, Gaslight];

    public Task<IReadOnlyList<GameSummary>> ListAsync(
        GameFilter filter, CancellationToken cancellationToken = default)
    {
        var games = All.AsEnumerable();

        // Archived games leave the default listing and nothing else (spec §7.5).
        if (!filter.IncludeArchived)
        {
            games = games.Where(g => g.State is not LifecycleState.Archived);
        }

        if (!string.IsNullOrWhiteSpace(filter.Text))
        {
            games = games.Where(g => g.Name.Contains(filter.Text, StringComparison.OrdinalIgnoreCase));
        }

        return Task.FromResult<IReadOnlyList<GameSummary>>(games.ToList());
    }

    public Task<GamePage?> FindAsync(string slug, CancellationToken cancellationToken = default)
    {
        var summary = All.FirstOrDefault(g => g.Slug == slug);
        if (summary is null)
        {
            return Task.FromResult<GamePage?>(null);
        }

        return Task.FromResult<GamePage?>(new GamePage(
            summary,
            Description: summary.Slug is "m-u-s-h"
                ? "The development server for PennMUSH. Open to visitors."
                : null,
            Endpoints: [new GameEndpointView("mush.pennmush.org", 4201, "telnet", TlsMeasured: false)],
            ConnectScreen: summary.Slug is "m-u-s-h"
                ? "Welcome to... M*U*S*H - mush.pennmush.org 4201 and 4202 (ssl)\n\nRunning PennMUSH 1.8.7\nContact: mush@gungnir.pennmush.org"
                : null,
            ConnectScreenSuppressed: false,
            ReachableFraction: 0.956,
            LongestOutage: TimeSpan.FromHours(52),
            Capabilities: Capabilities(summary),
            Activity: Activity(),
            Declared: Declared(summary),
            Changes: [new ChangeEntry(Now.AddDays(-16), "MSSP CODEBASE changed to PennMUSH 1.8.8p0")]));
    }

    /// <summary>
    /// The disagreement is real and observed: M*U*S*H's MSSP says 1.8.8p0 while its banner says
    /// 1.8.7. That row is the one a reader should not be able to scroll past.
    /// </summary>
    private static CapabilityRow[] Capabilities(GameSummary g) =>
    [
        new("MSSP", g.MeasuredProtocols.Contains("MSSP") ? CapabilityState.Present : CapabilityState.Absent,
            CapabilityState.Unknown, Now.AddMinutes(-4)),
        new("CHARSET", g.MeasuredProtocols.Contains("CHARSET") ? CapabilityState.Present : CapabilityState.Absent,
            CapabilityState.Unknown, Now.AddMinutes(-4)),
        new("GMCP", CapabilityState.Absent, CapabilityState.Present, Now.AddYears(-6)),
        new("MXP", CapabilityState.Unknown, CapabilityState.Unknown, null),
    ];

    /// <summary>All three cell states, because a grid that only ever shows two is not a test of anything.</summary>
    private static ActivityCell[] Activity()
    {
        var cells = new List<ActivityCell>();
        for (var day = 0; day < 7; day++)
        {
            for (var hour = 0; hour < 24; hour++)
            {
                cells.Add((day, hour) switch
                {
                    (2, >= 2 and <= 13) => new ActivityCell(day, hour, null, Probed: false),
                    (_, >= 19 and <= 23) => new ActivityCell(day, hour, 8 + ((day + hour) % 9), true),
                    (4, 15) => new ActivityCell(day, hour, null, Probed: true),
                    _ => new ActivityCell(day, hour, (day + hour) % 3, Probed: true),
                });
            }
        }

        return [.. cells];
    }

    private static Dictionary<string, ProvenanceChip> Declared(GameSummary g) => new()
    {
        ["codebase"] = new ProvenanceChip(g.Codebase ?? "unknown", FieldSource.Banner, Now.AddMinutes(-4), IsStale: false),
        ["genre"] = new ProvenanceChip("Modern Supernatural", FieldSource.Mssp, Now.AddDays(-14), IsStale: false),
        ["created"] = new ProvenanceChip("2009", FieldSource.Mssp, Now.AddYears(-6), IsStale: true),
        ["language"] = new ProvenanceChip("English", FieldSource.Mssp, Now.AddYears(-6), IsStale: true),
    };

    public Task<LivenessFeeds> FeedsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new LivenessFeeds(
            NewlyDiscovered: [new FeedEntry("eldertale", "Eldertale Online", Now.AddHours(-2),
                "found via REFERRAL · answered MSSP on first probe")],
            WentDark: [new FeedEntry("gaslight-row", "Gaslight Row", Now.AddDays(-6),
                "unreachable since 24 July · dns nxdomain · page stays, we keep knocking weekly")],
            CameBack: [new FeedEntry("aardwolf", "Aardwolf MUD", Now.AddMinutes(-40),
                "answered again after 26 months dark")]));
}
