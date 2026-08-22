namespace MUI.Discovery;

/// <summary>
/// What came of asking <see cref="ReviewMergeService.KeepDistinctAsync"/> to close a pair as two
/// games rather than one.
/// </summary>
/// <remarks>
/// Shaped like <see cref="MergeVerdict"/> and for the same reason: an operator naming the same game
/// twice, or a game that is not there, is an anticipated shape rather than an exception. The one
/// outcome with no counterpart on the merge side is <see cref="AlreadyOneListing"/> — refusing to
/// record "these are two games" about a pair a merge has already made one, which would leave the
/// catalogue asserting both.
/// </remarks>
public abstract record DistinctVerdict
{
    private DistinctVerdict()
    {
    }

    /// <summary>The open review was closed, and will not be re-opened while the evidence stands.</summary>
    public sealed record Kept(Guid ReviewId, double Score) : DistinctVerdict;

    /// <summary>
    /// There was no open review naming this pair — nothing to close.
    /// </summary>
    /// <remarks>
    /// Not an error and not a write: unlike a merge, which is a judgement worth recording whether or
    /// not the matcher ever flagged the pair, "these two are different games" is the state the
    /// catalogue is already in. Recording it against no review would be a row nothing reads.
    /// </remarks>
    public sealed record NoOpenReview : DistinctVerdict;

    /// <summary>Both sides name the same game.</summary>
    public sealed record SameGame : DistinctVerdict;

    /// <summary><paramref name="Id"/> does not name a game.</summary>
    public sealed record UnknownGame(Guid Id) : DistinctVerdict;

    /// <summary>
    /// A merge still in force already made these one listing, so they cannot be judged distinct
    /// without reverting it first (<c>UPDATE merge_log SET reverted_at = now()</c> — see CLAUDE.md).
    /// </summary>
    public sealed record AlreadyOneListing(Guid Listing) : DistinctVerdict;
}
