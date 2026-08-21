namespace MUI.Catalog.Persistence;

/// <summary>
/// Issues claim tokens and decides what a beacon read off a server means (spec §8).
/// </summary>
/// <remarks>
/// The rules live here so all three callers that matter — the dashboard minting a token, the probe
/// loop offering one, an owner pressing <em>check now</em> — reach the same conclusions. Takes no
/// <c>ProbeResult</c>: <c>MUI.Catalog</c> may not reference <c>MUI.Crawl</c>. The crawler reads the
/// beacon and hands this a string and a channel.
/// </remarks>
public sealed class ClaimService(
    IClaimStore claims,
    IGameStore games,
    TimeProvider time,
    IOnDemandProbes? probes = null)
{
    /// <summary>
    /// How often a claimant may ask us to look again (spec §8.1).
    /// </summary>
    /// <remarks>
    /// Short enough not to leave an operator who just edited <c>mush.cnf</c> waiting on the
    /// scheduler, long enough that the button can't be used to dial a stranger repeatedly. Bounded
    /// per claim rather than per source address, since a claim can't be created for a game nobody
    /// offered.
    /// </remarks>
    public static readonly TimeSpan RecheckInterval = TimeSpan.FromMinutes(3);

    /// <summary>
    /// The token <paramref name="userId"/> should publish for <paramref name="gameId"/>, minting one
    /// if they have none outstanding.
    /// </summary>
    /// <remarks>
    /// An existing pending claim is returned rather than replaced: the previous token may already be
    /// printed on a connect screen, and minting a rival would invalidate what the operator just
    /// published. Replaced only once expired.
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
    /// Called on every probe that read a beacon at all, and on every DNS lookup that found one, so
    /// <see cref="ClaimVerdict.NothingToDo"/> is the ordinary answer, not a failure. An
    /// already-verified claim is refreshed, never re-verified (§8.4): seeing the beacon updates
    /// <c>beacon_last_seen_at</c> and nothing else; not seeing it does nothing — absence must never
    /// revoke.
    /// <para>
    /// <b>One channel is weaker than the others and is bounded here</b>, in the one place every
    /// caller goes through: <see cref="ClaimChannel.DnsTxt"/> cannot complete a
    /// <see cref="ClaimIntent.Assume"/>. See <see cref="ClaimChannel"/> for why. The refresh path
    /// below is deliberately not bounded — a takeover already verified on the wire may still have
    /// its beacon seen in DNS, and refusing to record that would report a live record as unseen.
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

        if (await claims.FindPendingByTokenAsync(gameId, token!, now, cancellationToken) is { } pending)
        {
            // §8.3, §8.4 — DNS may add an owner and may never displace one. A TXT record is
            // published by whoever controls the domain, which on shared MU* hosting is the host's
            // operator rather than the game's; the other two channels are published by the listener
            // being claimed. Joining on the weaker evidence costs a listing an extra owner, and a
            // takeover on it costs somebody who proved control of the actual server theirs. Guarding
            // the transition rather than the issue, so the rule cannot be sidestepped by minting the
            // token as a join and publishing it as one: the intent is stored, and this reads it at
            // the moment it would take effect.
            if (channel is ClaimChannel.DnsTxt && pending.Intent is ClaimIntent.Assume)
            {
                return ClaimVerdict.NothingToDo;
            }

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

            // The listing badge and §7.5's ceiling grace both read this.
            await games.SetClaimedAsync(gameId, true, cancellationToken);

            if (pending.Intent is not ClaimIntent.Assume)
            {
                return ClaimVerdict.Verified;
            }

            // §8.4's counter-claim, how a game changes hands. Displaced owners are revoked here and
            // nowhere else: the one revocation nobody typed, sound because the triggering account
            // published a token on the same server the others did. The game stays claimed
            // throughout (SetClaimedAsync above), so a takeover never flickers the listing badge.
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

                // On the LOSING claim, because that is whose record changed.
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

    /// <summary>
    /// Brings the game's next probe forward, and records that the claimant asked.
    /// </summary>
    /// <remarks>
    /// Moves the schedule; the crawler still does the dialling. That keeps a button on a page from
    /// becoming a way to connect to a stranger's server on demand: the rate limit is per claim, and
    /// <c>CRAWL DELAY</c> and the address gate still bind when the loop gets there. Due-ness comes
    /// only from <c>crawl_target.next_probe_at</c> — this must actually move it, or the recorded ask
    /// is a no-op dressed as a real check.
    /// </remarks>
    public async Task<RequestCheckOutcome> RequestCheckAsync(
        Guid claimId,
        CancellationToken cancellationToken = default)
    {
        if (await claims.FindAsync(claimId, cancellationToken) is not { } claim)
        {
            return RequestCheckOutcome.NotFound;
        }

        if (!MayRecheck(claim))
        {
            return RequestCheckOutcome.TooSoon;
        }

        var now = time.GetUtcNow();

        // Recorded whether or not a target moved — a claim on a game with no crawl target (merged
        // away, or added by hand) is not the claimant's mistake to log.
        await claims.UpdateAsync(claim with { LastCheckedAt = now }, cancellationToken);
        await claims.RecordEventAsync(
            new ClaimEvent(claim.Id, now, ClaimEventKind.CheckRequested),
            cancellationToken);

        if (probes is not null)
        {
            await probes.BringForwardAsync(claim.GameId, now, cancellationToken);
        }

        return RequestCheckOutcome.Checked;
    }

    /// <summary>
    /// An owner giving up a game they hold.
    /// </summary>
    /// <remarks>
    /// Scoped to the account: <see cref="RevokeAsync"/> takes a claim id, and a claim id is not a
    /// credential — anybody who learns one could otherwise unclaim somebody else's game.
    /// </remarks>
    public async Task<ResignOutcome> ResignAsync(
        Guid claimId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (await claims.FindAsync(claimId, cancellationToken) is not { } claim)
        {
            return ResignOutcome.NotFound;
        }

        if (claim.UserId != userId)
        {
            return ResignOutcome.NotYours;
        }

        if (!claim.IsVerified)
        {
            return ResignOutcome.NotVerified;
        }

        await RevokeAsync(claimId, "the owner gave up this claim", cancellationToken);

        return ResignOutcome.Resigned;
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

    /// <summary>
    /// Withdraws a claim, explicitly.
    /// </summary>
    /// <remarks>
    /// The only way a verified claim ends, along with a counter-claim. Never called because a
    /// beacon went missing (see <see cref="OfferBeaconAsync"/>, §8.4). The game stops being claimed
    /// only when no verified claim is left, since §8.5 allows several owners.
    /// </remarks>
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
