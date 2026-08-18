namespace MUI.Discovery;

/// <summary>
/// Resolving one <see cref="DuplicateReview"/> pair by hand (spec §7.3).
/// </summary>
/// <remarks>
/// <para>
/// <b>The gap this closes.</b> <see cref="IdentityMatcher"/> can judge a pair middling and
/// <see cref="MergeApplier"/> can carry out a merge once one is judged, but nothing between the two
/// ever asked a person to look and act — the queue accumulates and nothing drains it. This is that
/// asking: a person names the winner and the loser, and this does what the identity matcher's own
/// auto-merge arm does when it clears the threshold on its own.
/// </para>
/// <para>
/// <b>The evidence travels with the merge when there is any.</b> An open review row for exactly this
/// pair has its score and signals carried onto <c>merge_log</c> unchanged, so a merge made from a
/// review reads the same way a merge the matcher made on its own would. A pair with no open review —
/// an operator spotting a duplicate the matcher never flagged — is still mergeable; migration 0018
/// says as much: "a merge an operator made by hand records the score it was made on and no signals."
/// </para>
/// <para>
/// <b>This never invents a game.</b> Winner and loser must both already exist, checked through
/// <see cref="IGameDirectory"/> the same way a candidate is checked before it is scored — an id that
/// does not name a game is a repair job, not a merge.
/// </para>
/// </remarks>
public sealed class ReviewMergeService(
    IGameDirectory games,
    IDuplicateReviewRepository reviews,
    MergeApplier applier,
    TimeProvider time,
    IUnitOfWorkFactory unitOfWorkFactory)
{
    /// <summary>
    /// Absorbs <paramref name="loserId"/> into <paramref name="winnerId"/>: closes the open review
    /// between them if one exists, carrying its evidence onto the merge, and writes the merge_log entry
    /// that performs the merge (spec §7.3 — recording the row <em>is</em> performing it).
    /// </summary>
    /// <param name="because">
    /// Why a person is confident these are one game. Required and never defaulted, for the same reason
    /// <c>--opt-out</c> and <c>--release</c> require it: a judgement nobody wrote down beside the row is
    /// one nobody can review later.
    /// </param>
    public async Task<ReviewMergeResult> MergeAsync(
        Guid winnerId,
        Guid loserId,
        string because,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(because);

        if (winnerId == loserId)
        {
            throw new ArgumentException("A game cannot be merged into itself.", nameof(loserId));
        }

        if (!await games.ExistsAsync(winnerId, ct))
        {
            throw new InvalidOperationException($"{winnerId} does not name a game.");
        }

        if (!await games.ExistsAsync(loserId, ct))
        {
            throw new InvalidOperationException($"{loserId} does not name a game.");
        }

        // The pair is unordered on the review's own terms (spec: "the pair is unordered; the storage
        // orders it"), so the review naming this pair may sit under either game — asking the winner's
        // open pairs and looking for the loser on the other end finds it regardless of which side an
        // operator calls the winner.
        var openReview = (await reviews.OpenPairsForAsync(winnerId, ct))
            .FirstOrDefault(review => review.OtherThan(winnerId) == loserId);

        var score = openReview is not null
            ? new IdentityScore(loserId, openReview.Score, IdentitySignals.FromJson(openReview.SignalsJson))
            : new IdentityScore(loserId, Score: 0, Signals: []);

        // Both writes join one unit of work so a failure between them rolls back the first rather than
        // leaving a merge in force with its review still open -- see IUnitOfWork's own doc comment for
        // why that state is otherwise unrecoverable (merge_log_absorbed_once_idx refuses the retry).
        await using var unitOfWork = await unitOfWorkFactory.BeginAsync(ct);

        var mergeId = await applier.MergeGamesAsync(winnerId, loserId, score, ct, because, unitOfWork);

        if (openReview is not null)
        {
            await reviews.ResolveAsync(
                openReview.Id, $"merged into {winnerId}: {because}", time.GetUtcNow(), ct, unitOfWork);
        }

        await unitOfWork.CommitAsync(ct);

        return new ReviewMergeResult(mergeId, openReview?.Id, score.Score);
    }
}

/// <summary>What a hand-resolved merge did, for the operator who asked for it.</summary>
public sealed record ReviewMergeResult(Guid MergeId, Guid? ResolvedReviewId, double Score);
