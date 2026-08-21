namespace MUI.Discovery;

/// <summary>
/// Two games that might be one, held open for a person to judge (spec §7.3's middle band).
/// </summary>
/// <remarks>
/// <b>Both pages stay live and link to each other reciprocally.</b> This record changes no
/// presentational state at all — it does not archive, hide, redirect or reorder either game. A wrongly
/// hidden game is worse than a visible duplicate, and hiding one side would make this an unreviewed
/// merge wearing a review's name.
/// </remarks>
public sealed record DuplicateReview(
    Guid Id,
    Guid LeftGameId,
    Guid RightGameId,
    double Score,
    string SignalsJson,
    DateTimeOffset OpenedAt,
    DateTimeOffset? ResolvedAt,
    string? Resolution)
{
    public bool IsOpen => ResolvedAt is null;

    /// <summary>The counterpart, from whichever side is being rendered — the reciprocal link.</summary>
    public Guid OtherThan(Guid gameId) =>
        gameId == LeftGameId ? RightGameId
        : gameId == RightGameId ? LeftGameId
        : throw new ArgumentException($"Game {gameId} is not part of review {Id}.", nameof(gameId));
}

/// <summary>Suspected-duplicate pairs. The pair is unordered; the storage orders it.</summary>
/// <remarks>
/// Ordering the pair on write is what makes "have we already opened this?" one lookup rather than two
/// that can race each other into two rows for one pair.
/// </remarks>
public interface IDuplicateReviewRepository
{
    /// <summary>
    /// Opens the pair, or returns the id of the one already open. The order of the arguments is
    /// irrelevant, and re-opening an open pair must not accumulate a row per probe.
    /// </summary>
    Task<Guid> OpenAsync(Guid a, Guid b, IdentityScore score, DateTimeOffset at, CancellationToken ct);

    Task<IReadOnlyList<DuplicateReview>> OpenPairsForAsync(Guid gameId, CancellationToken ct);

    /// <summary>Closes the pair. The row is kept: a judgement is part of the record.</summary>
    /// <param name="unitOfWork">
    /// When given, this write joins it rather than committing on its own -- see
    /// <see cref="ReviewMergeService.MergeAsync"/>, which needs this write and the preceding
    /// <c>merge_log</c> insert to commit or roll back together.
    /// </param>
    Task ResolveAsync(
        Guid id, string resolution, DateTimeOffset at, CancellationToken ct, IUnitOfWork? unitOfWork = null);
}
