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
/// Archiving is a presentation change, never a deletion: an archived game keeps its page, its URL,
/// its history and its change feed, keeps being probed at the weekly floor, and one successful probe
/// restores it with no human on either side.
/// The threshold is <see cref="ArchivePolicy"/>'s and is never recomputed here — this class only
/// decides how long a game has been dark and how much reachable time it has earned, then asks.
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
            // An exclusion is a judgement a person made and only a person undoes — otherwise the
            // sweeper archives it and the next answering probe restores it, discarding the decision
            // without either automatic step knowing it existed. An unlisting is skipped for the same
            // reason: a game that had already gone dark before its owner asked would otherwise get
            // archived, replacing "they asked" with "it stopped answering" in the column the listing
            // reads.
            if (game.State is LifecycleState.Excluded or LifecycleState.Unlisted)
            {
                continue;
            }

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

        // An unlisted game is relisted by a probe too — safe by construction, not by a check here: an
        // opted-out address is refused before the dial (§11), so an answering probe proves no opt-out
        // stands. This lets an operator delete the TXT record and be relisted by the crawl alone,
        // rather than having to ask us twice.
        //
        // `Excluded` is deliberately not in this list — that's our judgement, and a socket answering
        // was never evidence against it.
        if (game is null || game.State is not (LifecycleState.Archived or LifecycleState.Unlisted))
        {
            return false;
        }

        if (game.State is LifecycleState.Unlisted)
        {
            await games.RelistAsync(gameId, at, cancellationToken);
        }
        else
        {
            await games.SetStateAsync(gameId, LifecycleState.Active, at, cancellationToken);
        }

        return true;
    }

    private async Task<bool> ShouldArchiveAsync(
        GameRecord game,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var open = await availability.OpenIntervalAsync(game.Id, cancellationToken);

        // A game never probed, or currently reachable or degraded (socket answered, session didn't
        // finish), has no darkness to measure — archiving it would record our own probe timeout as
        // the game's absence (rule 5).
        if (open is not { State: AvailabilityState.Unreachable })
        {
            return false;
        }

        var darkFor = now - open.FromAt;

        var firstParty = await history.CumulativeReachableAsync(game.Id, now, cancellationToken);

        return ArchivePolicy.ShouldArchive(darkFor, firstParty, game.IsClaimed);
    }
}
