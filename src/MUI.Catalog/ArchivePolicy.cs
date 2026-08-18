namespace MUI.Catalog;

/// <summary>
/// How long a game may stay unreachable before it leaves the default listing for the archive.
/// </summary>
/// <remarks>
/// <para>
/// Tiered rather than constant — a fortnight-old game and a decade-old institution do not deserve
/// the same benefit of the doubt (spec §7.5):
/// </para>
/// <code>
/// grace = clamp(credited_reachable_time / 4, 60 days, 365 days)
/// </code>
/// <para>
/// The input is <em>cumulative</em> reachable time summed from availability intervals, not the span
/// between first and last sighting — a history of flapping accrues nothing for the gaps.
/// </para>
/// </remarks>
public static class ArchivePolicy
{
    /// <summary>No game is archived sooner than this, however briefly we have known it.</summary>
    public static readonly TimeSpan Floor = TimeSpan.FromDays(60);

    /// <summary>No game waits longer than this, however venerable.</summary>
    public static readonly TimeSpan Ceiling = TimeSpan.FromDays(365);

    /// <summary>Grace is a quarter of credited reachable time before clamping.</summary>
    public const double ReachableDivisor = 4.0;

    /// <summary>
    /// The grace period a game has earned.
    /// </summary>
    /// <remarks>
    /// Only first-party measurements earn grace: the backfill contributes addresses only, never
    /// history (spec §7.6), so there is no imported reachable time to weight.
    /// </remarks>
    /// <param name="firstPartyReachable">Cumulative time this site measured the game as reachable.</param>
    /// <param name="isClaimed">
    /// Whether an owner has proved control of the game (spec §8). A claimed game receives the ceiling
    /// outright — someone with server access has demonstrably staked a claim, and that is worth a year
    /// regardless of how long we happen to have been watching.
    /// </param>
    public static TimeSpan GraceFor(TimeSpan firstPartyReachable, bool isClaimed = false)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(firstPartyReachable, TimeSpan.Zero);

        if (isClaimed)
        {
            return Ceiling;
        }

        var grace = firstPartyReachable / ReachableDivisor;

        return grace < Floor ? Floor
            : grace > Ceiling ? Ceiling
            : grace;
    }

    /// <summary>
    /// Whether a game that has been dark for <paramref name="darkFor"/> has exhausted its grace.
    /// </summary>
    public static bool ShouldArchive(
        TimeSpan darkFor,
        TimeSpan firstPartyReachable,
        bool isClaimed = false) =>
        darkFor >= GraceFor(firstPartyReachable, isClaimed);
}
