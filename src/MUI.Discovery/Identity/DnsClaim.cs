using MUI.Catalog;
using MUI.Catalog.Persistence;

using Microsoft.Extensions.Logging;

namespace MUI.Discovery;

/// <summary>
/// Reads spec §8.3's third claim channel: a port-qualified TXT record at <c>_muindex.&lt;host&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing here dials the game.</b> That is the channel's point: an operator whose server is down,
/// firewalled off from us, or behind a codebase whose connect screen they cannot edit can still prove
/// they run it, and we can check without opening a socket. It is also why this is not part of
/// <c>CrawlCycle</c> — a crawl is a thing that dials, and this is a thing that asks a resolver.
/// </para>
/// <para>
/// <b>A lookup is spent only where it could matter.</b> The gate is a live claim on the game, read
/// first, before any name is resolved: sweeping the catalogue would put a standing query load on
/// strangers' nameservers in aid of a channel almost none of them use. An already-verified DNS claim
/// still gets its lookup, because that is how <c>beacon_last_seen_at</c> stays honest — and only
/// that; §8.4's rule that absence never revokes holds here exactly as it does on the wire, and a
/// deleted record does nothing at all.
/// </para>
/// <para>
/// <b>Endpoints a game has moved off are excluded.</b> A <see cref="EndpointState.Gone"/> row is kept
/// for ever and still probed at §7.4's floor, so it is the sort of record that outlives whoever holds
/// the hostname now; verifying against one would let the next tenant of an expired domain claim a
/// game that left it years ago.
/// </para>
/// </remarks>
public sealed class DnsClaimVerifier(
    IClaimStore claims,
    IEndpointStore endpoints,
    IDnsTxtResolver dns,
    ClaimService service,
    TimeProvider time,
    ILogger<DnsClaimVerifier>? logger = null)
{
    /// <summary>
    /// Looks up whatever this game's operators have published, and offers it to the claim store.
    /// </summary>
    /// <returns>
    /// The strongest thing that happened, or <see cref="ClaimVerdict.NothingToDo"/> — which is the
    /// ordinary answer, including for a game whose records are exactly as they were.
    /// </returns>
    public async Task<ClaimVerdict> CheckAsync(Guid gameId, CancellationToken cancellationToken = default)
    {
        var now = time.GetUtcNow();
        var onGame = await claims.ForGameAsync(gameId, cancellationToken);

        if (!onGame.Any(claim => claim.IsPending(now)
            || (claim.IsVerified && claim.VerifiedVia is ClaimChannel.DnsTxt)))
        {
            return ClaimVerdict.NothingToDo;
        }

        var reachable = (await endpoints.ForGameAsync(gameId, cancellationToken))
            .Where(endpoint => endpoint.State is not EndpointState.Gone)
            .ToList();

        // Distinct hosts, not distinct endpoints: a game on six ports is one machine and one zone.
        var published = new HashSet<string>(StringComparer.Ordinal);

        foreach (var host in reachable.Select(e => e.Host).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var answer = await dns.LookupAsync(ClaimTokenBeacon.DnsNameFor(host), cancellationToken);

            if (!answer.Answered || answer.Records.Count == 0)
            {
                continue;
            }

            foreach (var port in reachable
                .Where(e => string.Equals(e.Host, host, StringComparison.OrdinalIgnoreCase))
                .Select(e => e.Port)
                .Distinct())
            {
                if (ClaimTokenBeacon.ReadDns(answer.Records, port) is { } token)
                {
                    published.Add(token);
                }
            }
        }

        var strongest = ClaimVerdict.NothingToDo;

        foreach (var token in published)
        {
            var verdict = await service.OfferBeaconAsync(
                gameId, token, ClaimChannel.DnsTxt, cancellationToken);

            if (verdict is ClaimVerdict.Verified)
            {
                logger?.LogInformation(
                    "A TXT record proved a claim on {Game}; verified via DNS", gameId);
            }

            if (Rank(verdict) > Rank(strongest))
            {
                strongest = verdict;
            }
        }

        return strongest;
    }

    /// <summary>
    /// How much a verdict is worth reporting, when several tokens were published at once.
    /// </summary>
    /// <remarks>
    /// Only for choosing what to return: every verdict has already been acted on by the time this is
    /// consulted, so nothing is discarded by losing the comparison.
    /// </remarks>
    private static int Rank(ClaimVerdict verdict) => verdict switch
    {
        ClaimVerdict.Assumed => 4,
        ClaimVerdict.Verified => 3,
        ClaimVerdict.StillSeen => 2,
        ClaimVerdict.Stale => 1,
        _ => 0,
    };
}
