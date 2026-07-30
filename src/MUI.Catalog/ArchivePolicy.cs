namespace MUI.Catalog;

/// <summary>
/// How long a game may stay unreachable before it leaves the default listing for the archive.
/// </summary>
/// <remarks>
/// <para>
/// The threshold is tiered rather than constant, because a fortnight-old game and a decade-old
/// institution do not deserve the same benefit of the doubt (spec §7.5):
/// </para>
/// <code>
/// grace = clamp(credited_reachable_time / 4, 60 days, 365 days)
/// </code>
/// <para>
/// The input is <em>cumulative</em> reachable time summed from availability intervals, not the span
/// between first and last sighting — a game reachable for two years out of five is credited with two, and
/// a history of flapping accrues nothing for the gaps.
/// </para>
/// <para>
/// Archiving is a presentation change and never a deletion: an archived game keeps its page, its URL
/// and its history, keeps being probed at the weekly floor forever, and is restored by a single
/// successful probe.
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
    /// Third-party measured history counts for half. Not because it is suspect, but because we cannot
    /// audit their probe, their parser or their failure handling (spec §7.6).
    /// </summary>
    public const double ImportedMeasuredWeight = 0.5;

    /// <summary>
    /// The grace period a game has earned.
    /// </summary>
    /// <param name="firstPartyReachable">Cumulative time this site measured the game as reachable.</param>
    /// <param name="importedMeasuredReachable">
    /// Cumulative reachable time imported from a directory that ran its own probe. Time imported from a
    /// hand-maintained list is not a parameter here, deliberately: it earns nothing.
    /// </param>
    /// <param name="isClaimed">
    /// Whether an owner has proved control of the game (spec §8). A claimed game receives the ceiling
    /// outright — someone with server access has demonstrably staked a claim, and that is worth a year
    /// regardless of how long we happen to have been watching.
    /// </param>
    public static TimeSpan GraceFor(
        TimeSpan firstPartyReachable,
        TimeSpan importedMeasuredReachable = default,
        bool isClaimed = false)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(firstPartyReachable, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(importedMeasuredReachable, TimeSpan.Zero);

        if (isClaimed)
        {
            return Ceiling;
        }

        var credited = firstPartyReachable + importedMeasuredReachable * ImportedMeasuredWeight;
        var grace = credited / ReachableDivisor;

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
        TimeSpan importedMeasuredReachable = default,
        bool isClaimed = false) =>
        darkFor >= GraceFor(firstPartyReachable, importedMeasuredReachable, isClaimed);
}
