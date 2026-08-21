namespace MUI.Web.Mcp;

/// <summary>What <see cref="CrawlAdminTools.CrawlSeedAddAsync"/> did.</summary>
public sealed record CrawlSeedAddResult(string Host, int Port, bool Exempt, bool WasNewlyPlanted);

/// <summary>One row of <see cref="CrawlAdminTools.CrawlDueTargetsAsync"/> or a dry-run listing.</summary>
public sealed record CrawlDueTarget(
    string Host, int Port, int Depth, int ConsecutiveFailures, DateTimeOffset NextProbeAt);

/// <summary>What <see cref="CrawlAdminTools.CrawlOptOutCheckAsync"/> read off DNS.</summary>
public sealed record CrawlOptOutCheckResult(
    string DnsName, bool Answered, IReadOnlyList<string> Records, string Verdict);

/// <summary>
/// What <see cref="CrawlAdminTools.CrawlRunCycleAsync"/> did — a dry-run listing, a real run's cycle
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

/// <summary>What <see cref="GameAdminTools.GameFieldSetAsync"/> changed.</summary>
public sealed record GameFieldSetResult(
    string GameSlug,
    string Field,
    string? PreviousValue,
    string NewValue,
    string? Warning);

/// <summary>What <see cref="GameAdminTools.GameRenameAsync"/> did. <see cref="OldSlug"/> is the same as
/// <see cref="NewSlug"/> when the new name minted the same slug as the one the game already had.</summary>
public sealed record GameRenameResult(string OldSlug, string NewSlug, string Name);

/// <summary>What <see cref="GameAdminTools.GameMergeAsync"/> did. <see cref="ResolvedReviewId"/> is null
/// when no open duplicate_review named this pair -- the merge was still recorded, as a judgement with
/// no signals, and <see cref="Score"/> reads 0 in that case.</summary>
public sealed record GameMergeResult(
    string WinnerSlug,
    string LoserSlug,
    Guid MergeId,
    Guid? ResolvedReviewId,
    double Score);
