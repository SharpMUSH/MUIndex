namespace MUI.Discovery;

/// <summary>One weighted signal and whether it fired, kept whether it fired or not.</summary>
/// <remarks>
/// The losing signals are carried deliberately: a review is a judgement a person makes, and "which of
/// the six were considered and how did each land" is the whole content of that judgement.
/// </remarks>
public sealed record IdentitySignal(string Name, double Weight, bool Matched);

/// <summary>How well one probe matched one candidate game.</summary>
public sealed record IdentityScore(
    Guid? CandidateGameId,
    double Score,
    IReadOnlyList<IdentitySignal> Signals);

/// <summary>What to do about it (spec §7.3).</summary>
public abstract record IdentityVerdict
{
    private IdentityVerdict()
    {
    }

    /// <summary>Above threshold: this probe is that game. The endpoint change is recorded as a FieldChange.</summary>
    public sealed record Merge(Guid GameId, IdentityScore Score) : IdentityVerdict;

    /// <summary>
    /// Middling: open a suspected-duplicate pair. Both pages stay live and link to each other
    /// reciprocally, because a wrongly hidden game is worse than a visible duplicate.
    /// </summary>
    public sealed record Review(Guid GameId, IdentityScore Score) : IdentityVerdict;

    /// <summary>Below threshold: a new game. <paramref name="Best"/> is null when there was no candidate at all.</summary>
    public sealed record Fresh(IdentityScore? Best) : IdentityVerdict;
}
