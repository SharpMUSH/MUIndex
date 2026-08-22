namespace MUI.Discovery;

/// <summary>
/// Resolving one <see cref="DuplicateReview"/> pair by hand (spec §7.3).
/// </summary>
/// <remarks>
/// <para>
/// <b>The gap this closes.</b> <see cref="IdentityMatcher"/> can judge a pair middling and
/// <see cref="MergeApplier"/> can carry out a merge once one is judged, but nothing between the two
/// asks a person to look and act. This is that asking: a person names the winner and the loser.
/// </para>
/// <para>
/// <b>Evidence travels with the merge when there is any.</b> An open review row for exactly this pair
/// has its score and signals carried onto <c>merge_log</c> unchanged. A pair with no open review — an
/// operator spotting a duplicate the matcher never flagged — is still mergeable, recording the score
/// it was made on and no signals (migration 0018).
/// </para>
/// <para>
/// <b>This never invents a game.</b> Winner and loser must both already exist, checked through
/// <see cref="IGameDirectory"/> — an id that does not name a game is a repair job, not a merge.
/// </para>
/// </remarks>
public sealed class ReviewMergeService(
    IGameDirectory games,
    IDuplicateReviewRepository reviews,
    MergeApplier applier,
    IMergeLog merges,
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
    public async Task<MergeVerdict> MergeAsync(
        Guid winnerId,
        Guid loserId,
        string because,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(because);

        if (winnerId == loserId)
        {
            return new MergeVerdict.SelfMerge();
        }

        if (!await games.ExistsAsync(winnerId, ct))
        {
            return new MergeVerdict.UnknownGame(winnerId);
        }

        if (!await games.ExistsAsync(loserId, ct))
        {
            return new MergeVerdict.UnknownGame(loserId);
        }

        // The pair is unordered (spec: "the pair is unordered; the storage orders it"), so asking the
        // winner's open pairs for the loser on the other end finds it regardless of which side an
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

        Guid mergeId;

        try
        {
            mergeId = await applier.MergeGamesAsync(winnerId, loserId, score, ct, because, unitOfWork);
        }
        catch (MergeAlreadyAbsorbedException error)
        {
            // Not deferred, so this always fires here, inside the insert itself.
            return new MergeVerdict.AlreadyAbsorbed(error.Message);
        }

        var now = time.GetUtcNow();

        if (openReview is not null)
        {
            await reviews.ResolveAsync(
                openReview.Id, $"merged into {winnerId}: {because}", now, ct, unitOfWork);
        }

        var moot = await MootReviewsAsync(winnerId, loserId, openReview?.Id, ct);

        foreach (var review in moot)
        {
            await reviews.ResolveAsync(
                review.Id,
                $"moot: both sides are now {winnerId}, which absorbed {loserId}",
                now,
                ct,
                unitOfWork);
        }

        try
        {
            await unitOfWork.CommitAsync(ct);
        }
        catch (MergeWouldChainException error)
        {
            // merge_log_no_chains is DEFERRABLE INITIALLY DEFERRED, so when the insert above shares this
            // transaction with the review resolve, the chain check itself only runs here, at commit.
            return new MergeVerdict.RedirectChain(error.Message);
        }

        return new MergeVerdict.Merged(
            new ReviewMergeResult(mergeId, openReview?.Id, score.Score, moot.Count));
    }

    /// <summary>
    /// Closes a pair as two games rather than one: the review is resolved with the reason beside it,
    /// and nothing about either game moves (spec §7.3).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The other half of the operator surface.</b> <see cref="MergeAsync"/> could act on a pair that
    /// was one game and there was nothing at all to do about a pair that was not — the row stayed open
    /// for ever, and a queue whose false positives can never be cleared stops being read. Most of them
    /// are false positives: a stock connect screen, or one operator's contact address across the
    /// several games they run.
    /// </para>
    /// <para>
    /// <b>This is a judgement, so it needs a reason, and it is not a merge, so it needs no
    /// transaction.</b> One write to one row, and the row is kept — a judgement is part of the record.
    /// </para>
    /// </remarks>
    /// <param name="because">
    /// What convinced the person these are two games. Required and never defaulted, for the same reason
    /// <see cref="MergeAsync"/>'s is.
    /// </param>
    public async Task<DistinctVerdict> KeepDistinctAsync(
        Guid a,
        Guid b,
        string because,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(because);

        if (a == b)
        {
            return new DistinctVerdict.SameGame();
        }

        if (!await games.ExistsAsync(a, ct))
        {
            return new DistinctVerdict.UnknownGame(a);
        }

        if (!await games.ExistsAsync(b, ct))
        {
            return new DistinctVerdict.UnknownGame(b);
        }

        // Refused rather than written: a merge in force already says these are one listing, and closing
        // the review as "two games" would leave the catalogue asserting both at once. Reverting the
        // merge is the operator's decision to make first, and it is deliberately not made from here.
        if (await merges.AreOneListingAsync(a, b, ct))
        {
            return new DistinctVerdict.AlreadyOneListing(await merges.ListingOfAsync(a, ct));
        }

        var openReview = (await reviews.OpenPairsForAsync(a, ct))
            .FirstOrDefault(review => review.OtherThan(a) == b);

        if (openReview is null)
        {
            return new DistinctVerdict.NoOpenReview();
        }

        await reviews.ResolveAsync(openReview.Id, $"kept distinct: {because}", time.GetUtcNow(), ct);

        return new DistinctVerdict.Kept(openReview.Id, openReview.Score);
    }

    /// <summary>
    /// The open reviews this merge empties of meaning: both of their sides now redirect to the same
    /// listing, so there is nothing left for anybody to judge.
    /// </summary>
    /// <remarks>
    /// The pair review named by the merge itself is excluded — <see cref="MergeAsync"/> resolves that
    /// one with the merge's own reason, which is the more informative record of the two. What is left
    /// is the third-party case: A absorbed B earlier, and C is now found to be A as well, which retires
    /// the open B–C pair nobody would otherwise think to look at.
    /// </remarks>
    private async Task<IReadOnlyList<DuplicateReview>> MootReviewsAsync(
        Guid winnerId, Guid loserId, Guid? alreadyResolved, CancellationToken ct)
    {
        var moot = new Dictionary<Guid, DuplicateReview>();

        foreach (var side in (Guid[])[winnerId, loserId])
        {
            foreach (var review in await reviews.OpenPairsForAsync(side, ct))
            {
                if (review.Id == alreadyResolved)
                {
                    continue;
                }

                // Read through the log as it stands, with this merge's own redirect applied by hand:
                // the row is written but not committed, so ListingOfAsync cannot see it yet.
                if (await ListingAfterThisMergeAsync(side, ct)
                    == await ListingAfterThisMergeAsync(review.OtherThan(side), ct))
                {
                    moot[review.Id] = review;
                }
            }
        }

        return [.. moot.Values];

        async Task<Guid> ListingAfterThisMergeAsync(Guid gameId, CancellationToken token) =>
            gameId == loserId ? winnerId : await merges.ListingOfAsync(gameId, token);
    }
}

/// <summary>What a hand-resolved merge did, for the operator who asked for it.</summary>
/// <param name="MootReviewsResolved">
/// Open reviews closed besides <paramref name="ResolvedReviewId"/> because this merge left both of
/// their sides pointing at the same listing — see <c>ReviewMergeService.MootReviewsAsync</c>.
/// </param>
public sealed record ReviewMergeResult(
    Guid MergeId, Guid? ResolvedReviewId, double Score, int MootReviewsResolved = 0);
