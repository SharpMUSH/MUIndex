namespace MUI.Web.Mcp;

/// <summary>What <see cref="MuiMcpTools.CrawlSeedAddAsync"/> did.</summary>
public sealed record CrawlSeedAddResult(string Host, int Port, bool Exempt, bool WasNewlyPlanted);

/// <summary>One row of <see cref="MuiMcpTools.CrawlDueTargetsAsync"/> or a dry-run listing.</summary>
public sealed record CrawlDueTarget(
    string Host, int Port, int Depth, int ConsecutiveFailures, DateTimeOffset NextProbeAt);

/// <summary>What <see cref="MuiMcpTools.CrawlOptOutCheckAsync"/> read off DNS.</summary>
public sealed record CrawlOptOutCheckResult(
    string DnsName, bool Answered, IReadOnlyList<string> Records, string Verdict);

/// <summary>
/// What <see cref="MuiMcpTools.CrawlRunCycleAsync"/> did — a dry-run listing, a real run's cycle
/// reports, or neither when the crawl lease was held elsewhere. <see cref="Note"/> always says which.
/// </summary>
public sealed record CrawlRunCycleResult(
    int SeedsConfigured,
    int SeedsNewlyPlanted,
    bool DryRun,
    bool LeaseHeld,
    IReadOnlyList<CrawlDueTarget>? Due,
    IReadOnlyList<MUI.Crawler.CycleReport>? Cycles,
    string Note);

/// <summary>What <see cref="MuiMcpTools.GameFieldSetAsync"/> changed.</summary>
public sealed record GameFieldSetResult(
    string GameSlug,
    string Field,
    string? PreviousValue,
    string NewValue,
    string? Warning);
