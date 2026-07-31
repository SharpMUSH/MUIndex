namespace MUI.Catalog.Persistence;

/// <summary>What one sweep did.</summary>
public sealed record ArchiveSweep(int Considered, int Archived)
{
    public static readonly ArchiveSweep Nothing = new(0, 0);
}

/// <summary>
/// Moves games into the archive when they have been dark longer than the grace they earned, and out
/// of it the moment they answer again (spec §7.4, §7.5).
/// </summary>
/// <remarks>
/// <para>
/// Archiving is a presentation change and never a deletion. An archived game keeps its page, its URL,
/// its history and its change feed; it keeps being probed at the weekly floor for ever; and one
/// successful probe restores it with no human on either side of the transition. That last property is
/// the one no incumbent directory managed, so it is worth stating twice.
/// </para>
/// <para>
/// The threshold is <see cref="ArchivePolicy"/>'s and is never recomputed here. This class decides
/// <em>how long a game has been dark</em> and <em>how much reachable time it has earned</em>, and
/// then asks.
/// </para>
/// </remarks>
public sealed class ArchiveSweeper(
    IGameStore games,
    IAvailabilityStore availability,
    IReachableHistory history)
{
    /// <summary>
    /// Archives every game whose grace has run out. Returns how many were considered and how many
    /// moved.
    /// </summary>
    public async Task<ArchiveSweep> SweepAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var candidates = await games.UnarchivedAsync(cancellationToken);
        var archived = 0;

        foreach (var game in candidates)
        {
            if (await ShouldArchiveAsync(game, now, cancellationToken))
            {
                await games.SetStateAsync(game.Id, LifecycleState.Archived, now, cancellationToken);
                archived++;
            }
        }

        return new ArchiveSweep(candidates.Count, archived);
    }

    /// <summary>
    /// Restores a game that has answered. Immediate and automatic (§7.5) — a game that comes back is
    /// re-listed by the probe that found it, not by the next sweep and never by a human. Returns
    /// whether this call is the one that moved it.
    /// </summary>
    public async Task<bool> RestoreAsync(
        Guid gameId,
        DateTimeOffset at,
        CancellationToken cancellationToken = default)
    {
        var game = await games.ByIdAsync(gameId, cancellationToken);

        if (game is null || game.State is not LifecycleState.Archived)
        {
            return false;
        }

        await games.SetStateAsync(gameId, LifecycleState.Active, at, cancellationToken);

        return true;
    }

    private async Task<bool> ShouldArchiveAsync(
        GameRecord game,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var open = await availability.OpenIntervalAsync(game.Id, cancellationToken);

        // A game we have never probed has no darkness to measure. Neither has one whose current
        // interval is reachable — or degraded, which is a game whose socket answered and whose
        // session did not finish. Archiving that would record our own probe timeout as the game's
        // absence, which is exactly what rule 5 forbids.
        if (open is not { State: AvailabilityState.Unreachable })
        {
            return false;
        }

        var darkFor = now - open.FromAt;

        var firstParty = await history.CumulativeReachableAsync(game.Id, now, cancellationToken);

        return ArchivePolicy.ShouldArchive(darkFor, firstParty, game.IsClaimed);
    }
}
