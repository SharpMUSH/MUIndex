namespace MUI.Discovery;

/// <summary>
/// One atomic unit of work spanning more than one repository write.
/// </summary>
/// <remarks>
/// <para>
/// <b>The gap this closes.</b> <see cref="ReviewMergeService.MergeAsync"/> performs two writes -- the
/// <c>merge_log</c> insert (<see cref="MergeApplier.MergeGamesAsync"/>) and, when a review named the
/// pair, the matching <c>duplicate_review</c> resolve -- with nothing tying them together. If the
/// second write never lands (the process dies, the connection drops, an operator's request is
/// cancelled), the merge is in force but the review sits open forever: <c>merge_log_absorbed_once_idx</c>
/// refuses a second merge for the same loser, so the ordinary retry that would "finish the job" fails
/// outright, and the pair can only be closed by hand against <c>duplicate_review</c> directly.
/// </para>
/// <para>
/// <b>Deliberately opaque outside the persistence layer.</b> This interface carries no connection, no
/// transaction, nothing but the one operation a caller needs — MUI.Discovery must not know Npgsql
/// exists, the same layering CLAUDE.md states for MUI.Catalog and MUI.Crawl. The concrete
/// implementation, and the repository changes needed to actually share one connection across two
/// writes, live in MUI.Crawler.Persistence.
/// </para>
/// </remarks>
public interface IUnitOfWork : IAsyncDisposable
{
    /// <summary>
    /// Commits every write made against this unit of work. Disposing without calling this rolls all of
    /// them back — the caller never has to remember to do that separately on the failure path.
    /// </summary>
    Task CommitAsync(CancellationToken ct);
}

/// <summary>Begins a new <see cref="IUnitOfWork"/>.</summary>
public interface IUnitOfWorkFactory
{
    Task<IUnitOfWork> BeginAsync(CancellationToken ct);
}
