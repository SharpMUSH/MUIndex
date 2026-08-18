using MUI.Catalog;
using MUI.Crawl;

namespace MUI.Discovery;

/// <summary>
/// Carries out what <see cref="IdentityMatcher"/> decided.
/// </summary>
/// <remarks>
/// The matcher only judges. Attaching the endpoint, writing the change-feed entry and fingerprinting
/// the connect screen are separate acts with their own failure modes, and separating them is what lets
/// the judgement be tested without a repository in sight.
/// </remarks>
public sealed class MergeApplier(
    IEndpointDirectory endpoints,
    IGameFieldStore fields,
    IMergeLog merges,
    TimeProvider time)
{
    /// <summary>
    /// Records that this game answers at this address, and — if that is new or moved — appends the
    /// endpoint change to the change feed (spec §7.3, §5.1).
    /// </summary>
    public async Task AttachAsync(Guid gameId, ProbeResult result, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(result);

        var now = time.GetUtcNow();
        var existing = await endpoints.ByAddressAsync(result.Host, result.Port, ct);

        await endpoints.UpsertAsync(new KnownEndpoint(
            gameId,
            result.Host,
            result.Port,
            existing?.FirstSeenAt ?? now,
            now), ct);

        if (existing is null || existing.GameId != gameId)
        {
            // A change feed is a table of events that actually happened (§5.1). A second sighting of an
            // address we already attribute to this game is not an event, and writing one per probe
            // would bury the real ones.
            await fields.RecordChangeAsync(new FieldChange(
                gameId,
                IdentityFields.Endpoint,
                FieldSource.Handshake,
                existing is null ? null : $"{existing.Host} {existing.Port}",
                $"{result.Host} {result.Port}",
                now), ct);
        }

        if (result.Banner is { Length: > 0 } banner && BannerFingerprint.Flatten(banner).Length > 0)
        {
            // Nothing else writes this. Without it the banner signal could never fire: a game would
            // have to move house before we had ever fingerprinted it. A probe that saw no banner writes
            // nothing — silence is not a redesign, and the hash of an empty screen is a value every
            // silent server would share.
            await fields.UpsertAsync(new GameField(
                gameId,
                IdentityFields.BannerHash,
                FieldSource.Banner,
                BannerFingerprint.Of(banner),
                now,
                now), ct);
        }
    }

    /// <summary>
    /// Folds one game into another. A redirect, logged with every signal that was weighed — including
    /// the ones that did not fire, because a merge that has to be explained a year later is explained
    /// by what was considered.
    /// </summary>
    /// <param name="reason">
    /// An operator's own words for why these are one game, carried onto the log row beside the score
    /// (spec §7.3, migration 0030). Null for the one caller that has none: an automatic merge is its
    /// own explanation, the score and signals it crossed <c>AutoMergeThreshold</c> on.
    /// </param>
    public Task<Guid> MergeGamesAsync(
        Guid intoGameId, Guid fromGameId, IdentityScore score, CancellationToken ct, string? reason = null)
    {
        ArgumentNullException.ThrowIfNull(score);

        if (intoGameId == fromGameId)
        {
            throw new ArgumentException("A game cannot be merged into itself.", nameof(fromGameId));
        }

        return merges.RecordAsync(new MergeRecord(
            Guid.CreateVersion7(),
            intoGameId,
            fromGameId,
            score.Score,
            IdentitySignals.ToJson(score.Signals),
            time.GetUtcNow(),
            null,
            reason), ct);
    }
}
