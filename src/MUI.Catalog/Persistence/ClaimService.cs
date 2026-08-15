namespace MUI.Catalog.Persistence;

/// <summary>
/// Issues claim tokens and decides what a beacon read off a server means (spec §8).
/// </summary>
/// <remarks>
/// <para>
/// The rules live here rather than in the crawler or a page handler because all three of the callers
/// that matter — the dashboard minting a token, the probe loop offering one it read, and an owner
/// pressing <em>check now</em> — must reach the same conclusions. A rule implemented twice is a rule
/// that will disagree with itself.
/// </para>
/// <para>
/// <b>It takes no <c>ProbeResult</c>, and cannot.</b> <c>MUI.Catalog</c> may not reference
/// <c>MUI.Crawl</c>: the writers that consume a probe must not know a socket exists. The crawler reads
/// the beacon (<c>ClaimTokenBeacon</c>, which knows about MSSP variables and connect screens) and
/// hands this a string and a channel.
/// </para>
/// </remarks>
public sealed class ClaimService(IClaimStore claims, IGameStore games, TimeProvider time)
{
    /// <summary>
    /// How often a claimant may ask us to look again (spec §8.1).
    /// </summary>
    /// <remarks>
    /// Short enough that an operator who has just edited <c>mush.cnf</c> is not left waiting on the
    /// scheduler, long enough that the button is not a way to make us dial a stranger repeatedly. The
    /// bound is per claim rather than per source address, because the claim is the thing that must
    /// already exist — an attacker cannot create one for a game they have not been offered.
    /// </remarks>
    public static readonly TimeSpan RecheckInterval = TimeSpan.FromMinutes(3);

    /// <summary>
    /// The token <paramref name="userId"/> should publish for <paramref name="gameId"/>, minting one
    /// if they have none outstanding.
    /// </summary>
    /// <remarks>
    /// <b>An existing pending claim is returned rather than replaced.</b> The previous token may
    /// already be printed on a connect screen, and minting a rival would invalidate what the operator
    /// has just finished publishing — the most annoying possible failure, because it looks like the
    /// site not working. A token is only replaced once it has expired.
    /// </remarks>
    public async Task<GameClaim> IssueAsync(
        Guid gameId,
        Guid userId,
        ClaimIntent intent = ClaimIntent.Join,
        CancellationToken cancellationToken = default)
    {
        var now = time.GetUtcNow();
        var existing = await claims.ForUserAsync(userId, cancellationToken);

        if (existing.FirstOrDefault(c => c.GameId == gameId && c.IsPending(now)) is { } pending)
        {
            return pending;
        }

        var claim = new GameClaim
        {
            Id = Guid.CreateVersion7(),
            GameId = gameId,
            UserId = userId,
            Token = ClaimToken.Mint(),
            Intent = intent,
            IssuedAt = now,
            ExpiresAt = now + ClaimToken.PendingLifetime,
        };

        await claims.InsertAsync(claim, cancellationToken);

        var reissue = existing.Any(c => c.GameId == gameId);
        await claims.RecordEventAsync(
            new ClaimEvent(claim.Id, now, reissue ? ClaimEventKind.Reissued : ClaimEventKind.Issued),
            cancellationToken);

        return claim;
    }

    /// <summary>
    /// Offers a token read off <paramref name="gameId"/> to the claim store.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Called on every probe that read a beacon at all, which is why <see cref="ClaimVerdict.NothingToDo"/>
    /// is the ordinary answer rather than a failure. A game publishing somebody else's token, or a
    /// token from a claim that expired, is a normal state of the world and not a problem to report.
    /// </para>
    /// <para>
    /// <b>An already-verified claim is refreshed, never re-verified</b> (§8.4). Seeing the beacon
    /// updates <c>beacon_last_seen_at</c> and nothing else; not seeing it does nothing at all, which
    /// is the point — absence must never revoke.
    /// </para>
    /// </remarks>
    public async Task<ClaimVerdict> OfferBeaconAsync(
        Guid gameId,
        string? token,
        ClaimChannel channel,
        CancellationToken cancellationToken = default)
    {
        if (!ClaimToken.LooksLikeOne(token))
        {
            return ClaimVerdict.NothingToDo;
        }

        var now = time.GetUtcNow();

        if (await claims.FindPendingByTokenAsync(gameId, token!, cancellationToken) is { } pending)
        {
            await claims.UpdateAsync(
                pending with
                {
                    ClaimedAt = now,
                    BeaconLastSeenAt = now,
                    VerifiedVia = channel,
                },
                cancellationToken);

            await claims.RecordEventAsync(
                new ClaimEvent(pending.Id, now, ClaimEventKind.Verified, channel.ToString()),
                cancellationToken);

            // The listing badge and §7.5's ceiling grace both read this, and neither has ever been
            // reachable before now.
            await games.SetClaimedAsync(gameId, true, cancellationToken);

            if (pending.Intent is not ClaimIntent.Assume)
            {
                return ClaimVerdict.Verified;
            }

            // §8.4's counter-claim, which is how a game changes hands. The displaced owners are
            // revoked here and nowhere else: this is the ONE revocation nobody typed, and it is
            // sound because the account that caused it published a token on the same server the
            // others did. It proves control now, which is the only thing ownership here ever meant.
            //
            // The game stays claimed throughout — SetClaimedAsync was called above — so a takeover
            // never flickers the listing badge off and on.
            var displaced = (await claims.ForGameAsync(gameId, cancellationToken))
                .Where(other => other.Id != pending.Id && other.IsVerified)
                .ToList();

            foreach (var other in displaced)
            {
                await claims.UpdateAsync(
                    other with
                    {
                        RevokedAt = now,
                        RevokedReason = "counter-claim: another account proved control of this game",
                    },
                    cancellationToken);

                // On the LOSING claim, because that is whose record changed. An owner reading their
                // own history has to be able to see what happened to them and when.
                await claims.RecordEventAsync(
                    new ClaimEvent(other.Id, now, ClaimEventKind.CounterClaimed),
                    cancellationToken);
            }

            return displaced.Count > 0 ? ClaimVerdict.Assumed : ClaimVerdict.Verified;
        }

        var onGame = await claims.ForGameAsync(gameId, cancellationToken);
        var match = onGame.FirstOrDefault(c => string.Equals(c.Token, token, StringComparison.Ordinal));

        if (match is null)
        {
            // A token-shaped string we never issued. Says nothing about anybody.
            return ClaimVerdict.NothingToDo;
        }

        if (!match.IsVerified)
        {
            return ClaimVerdict.Stale;
        }

        await claims.UpdateAsync(match with { BeaconLastSeenAt = now }, cancellationToken);
        await claims.RecordEventAsync(
            new ClaimEvent(match.Id, now, ClaimEventKind.BeaconSeen),
            cancellationToken);

        return ClaimVerdict.StillSeen;
    }

    /// <summary>Whether <paramref name="claim"/> may ask for an on-demand probe yet.</summary>
    public bool MayRecheck(GameClaim claim)
    {
        ArgumentNullException.ThrowIfNull(claim);

        return claim.LastCheckedAt is not { } last || time.GetUtcNow() - last >= RecheckInterval;
    }

    /// <summary>Records that a check was asked for. The dialling itself belongs to the crawler.</summary>
    public async Task<bool> RequestCheckAsync(Guid claimId, CancellationToken cancellationToken = default)
    {
        if (await claims.FindAsync(claimId, cancellationToken) is not { } claim || !MayRecheck(claim))
        {
            return false;
        }

        var now = time.GetUtcNow();

        await claims.UpdateAsync(claim with { LastCheckedAt = now }, cancellationToken);
        await claims.RecordEventAsync(
            new ClaimEvent(claim.Id, now, ClaimEventKind.CheckRequested),
            cancellationToken);

        return true;
    }

    /// <summary>
    /// Withdraws a claim, explicitly.
    /// </summary>
    /// <remarks>
    /// The only way a verified claim ends, along with a counter-claim. <b>Never called because a
    /// beacon went missing</b> — see <see cref="OfferBeaconAsync"/> and §8.4. The game stops being
    /// claimed only when no verified claim is left, because §8.5 allows several owners and one
    /// walking away does not unclaim the game for the others.
    /// </remarks>
    /// <summary>
    /// An owner giving up a game they hold.
    /// </summary>
    /// <remarks>
    /// Scoped to the account, because <see cref="RevokeAsync"/> takes a claim id and a claim id is
    /// not a credential — anybody who learns one could otherwise unclaim somebody else's game. The
    /// caller is the account, so the check belongs here rather than in whichever page happens to
    /// call it.
    /// </remarks>
    public async Task<bool> ResignAsync(
        Guid claimId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (await claims.FindAsync(claimId, cancellationToken) is not { } claim
            || claim.UserId != userId
            || !claim.IsVerified)
        {
            return false;
        }

        await RevokeAsync(claimId, "the owner gave up this claim", cancellationToken);

        return true;
    }

    /// <summary>Every account that has proved control of a game, newest first (spec §8.5).</summary>
    public async Task<IReadOnlyList<GameClaim>> OwnersAsync(
        Guid gameId,
        CancellationToken cancellationToken = default) =>
        [.. (await claims.ForGameAsync(gameId, cancellationToken)).Where(claim => claim.IsVerified)];

    /// <summary>One claim's audit log, oldest first (spec §8.5).</summary>
    public Task<IReadOnlyList<ClaimEvent>> HistoryAsync(
        Guid claimId,
        CancellationToken cancellationToken = default) =>
        claims.EventsAsync(claimId, cancellationToken);

    public async Task RevokeAsync(Guid claimId, string reason, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (await claims.FindAsync(claimId, cancellationToken) is not { } claim)
        {
            return;
        }

        var now = time.GetUtcNow();

        await claims.UpdateAsync(
            claim with { RevokedAt = now, RevokedReason = reason },
            cancellationToken);
        await claims.RecordEventAsync(
            new ClaimEvent(claim.Id, now, ClaimEventKind.Revoked, reason),
            cancellationToken);

        var remaining = await claims.ForGameAsync(claim.GameId, cancellationToken);

        if (!remaining.Any(c => c.Id != claim.Id && c.IsVerified))
        {
            await games.SetClaimedAsync(claim.GameId, false, cancellationToken);
        }
    }
}
