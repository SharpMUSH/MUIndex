namespace MUI.Discovery;

/// <summary>What came of asking <see cref="ReviewMergeService.MergeAsync"/> to fold one game into another.</summary>
/// <remarks>
/// Three of the five outcomes are routine, anticipated shapes a hand-resolved merge takes — not bugs:
/// an operator names the same game on both sides, or one side of the pair does not exist. The other
/// two, <see cref="AlreadyAbsorbed"/> and <see cref="RedirectChain"/>, are the schema's own guards
/// (migration 0018: <c>merge_log_absorbed_once_idx</c> and the <c>merge_log_no_chains</c> trigger)
/// surfacing as a named outcome instead of a raw <c>PostgresException</c> reaching the caller. Anything
/// <see cref="ReviewMergeService.MergeAsync"/> did not anticipate still throws.
/// </remarks>
public abstract record MergeVerdict
{
    private MergeVerdict()
    {
    }

    /// <summary>The merge went through. What <see cref="ReviewMergeService.MergeAsync"/> returned before this type existed.</summary>
    public sealed record Merged(ReviewMergeResult Result) : MergeVerdict;

    /// <summary>Winner and loser name the same game.</summary>
    public sealed record SelfMerge : MergeVerdict;

    /// <summary><paramref name="Id"/> does not name a game -- checked against <see cref="IGameDirectory"/> before any write.</summary>
    public sealed record UnknownGame(Guid Id) : MergeVerdict;

    /// <summary>
    /// The loser is already absorbed by some other game -- <c>merge_log_absorbed_once_idx</c> refusing
    /// the insert. <paramref name="DatabaseMessage"/> is the schema's own message, more specific than
    /// anything worth restating here.
    /// </summary>
    public sealed record AlreadyAbsorbed(string DatabaseMessage) : MergeVerdict;

    /// <summary>
    /// This merge would leave a game absorbed but redirecting nowhere reachable -- the
    /// <c>merge_log_no_chains</c> trigger refusing the shape. <paramref name="DatabaseMessage"/> is the
    /// schema's own message.
    /// </summary>
    public sealed record RedirectChain(string DatabaseMessage) : MergeVerdict;
}
