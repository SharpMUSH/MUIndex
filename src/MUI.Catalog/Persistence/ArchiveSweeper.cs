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
            // An exclusion is a judgement a person made and only a person undoes. Without this, the
            // first time an excluded dev instance went dark the sweeper would move it to `archived`
            // and the next probe that answered would restore it to `active` — the decision discarded
            // by two automatic steps, neither of which knew it existed.
            //
            // An unlisting is somebody else's decision and is skipped for the same reason. It is
            // rarely reached — a game that opted out writes no availability transition at all, so it
            // has no open unreachable interval to be archived on — but a game that had already gone
            // dark before its owner asked does, and archiving that one would replace "they asked" with
            // "it stopped answering" in the one column the listing reads.
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

        // An unlisted game is relisted by a probe too, and that is safe by construction rather than
        // by a check here: an opted-out address is refused before the dial (§11), so a probe that
        // answered is proof that no opt-out stands on it any more. It is what makes the exit an
        // operator can work alone — delete the TXT record, be dialled again within a week on §7.4's
        // floor — bring the listing back with the crawl, rather than leaving them to ask us twice.
        //
        // `Excluded` is deliberately not in this list. That one is our judgement about what the
        // thing is, and a socket answering was never evidence against it.
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
