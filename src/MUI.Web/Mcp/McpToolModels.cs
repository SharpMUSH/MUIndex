namespace MUI.Web.Mcp;

/// <summary>What <see cref="CrawlAdminTools.CrawlSeedAddAsync"/> did.</summary>
public sealed record CrawlSeedAddResult(string Host, int Port, bool Exempt, bool WasNewlyPlanted);

/// <summary>One row of <see cref="CrawlAdminTools.CrawlDueTargetsAsync"/> or a dry-run listing.</summary>
public sealed record CrawlDueTarget(
    string Host, int Port, int Depth, int ConsecutiveFailures, DateTimeOffset NextProbeAt);

/// <summary>
/// One address <see cref="CrawlAdminTools.CrawlRefusalsAsync"/> reports we are not dialling.
/// </summary>
/// <remarks>
/// <see cref="Reason"/> is the stored word — <c>out_of_scope</c> or <c>opted_out</c> — rather than
/// the .NET enum name, so the wire says what the table says and a reader of either sees one
/// vocabulary. The same reason <see cref="CrawlDueTarget"/> exists rather than handing out a
/// <c>CrawlTarget</c>: what this layer publishes is its own shape, not the registry's.
/// </remarks>
public sealed record CrawlRefusalRow(
    string Host,
    int Port,
    string Reason,
    string Detail,
    DateTimeOffset FirstRefusedAt,
    DateTimeOffset LastRefusedAt,
    int Times);

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

/// <summary>
/// What <see cref="GameAdminTools.GameUnlistAsync"/> or <see cref="GameAdminTools.GameRelistAsync"/>
/// left the game in.
/// </summary>
/// <param name="Slug">The game, which keeps its slug and its page either way (§7.5).</param>
/// <param name="State">The lifecycle state it now holds, as the catalogue spells it.</param>
/// <param name="Because">Why staff did it, or null for a relisting, which undoes rather than asserts.</param>
public sealed record GameUnlistResult(string Slug, string State, string? Because);

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
/// <param name="MootReviewsResolved">
/// How many further open reviews this merge closed by leaving both of their sides pointing at the
/// winner. Usually 0.
/// </param>
public sealed record GameMergeResult(
    string WinnerSlug,
    string LoserSlug,
    Guid MergeId,
    Guid? ResolvedReviewId,
    double Score,
    int MootReviewsResolved);

/// <summary>What <see cref="GameAdminTools.GameKeepDistinctAsync"/> closed. Nothing about either game
/// moved: the only write is the <c>duplicate_review</c> row's own resolution.</summary>
public sealed record GameKeepDistinctResult(
    string SlugA,
    string SlugB,
    Guid ResolvedReviewId,
    double Score);
