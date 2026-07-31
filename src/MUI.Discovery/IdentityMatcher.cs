using MUI.Catalog;
using MUI.Crawl;

namespace MUI.Discovery;

/// <summary>
/// Scores a probe against the games it might already be (spec §7.3).
/// </summary>
/// <remarks>
/// <para>
/// Candidates are gathered by reverse lookup rather than by scanning: the endpoint, then every game
/// sharing this probe's claim token, name, banner hash, website or contact. Each candidate is then
/// scored over all six signals, so a candidate found by one signal is still credited for the others.
/// </para>
/// <para>
/// <b>Every signal is filtered through <see cref="MsspDefaults.IsPlaceholder"/> before it is weighed,
/// on both sides of the comparison.</b> A codebase default is the absence of a signal, not a weak one:
/// every unedited PennMUSH on the internet publishes <c>NAME "PennMUSH"</c>, and scored naively they
/// all match each other on the strongest textual signal in the table. Two absences must never score as
/// an agreement — including the empty banner, whose fingerprint is a perfectly stable hash of nothing.
/// </para>
/// <para>
/// <b><c>CODEBASE</c> is scored but never gathered on, deliberately.</b> Nearly every MUSH in the
/// catalogue reports the same string, so gathering on it would make every probe's candidate set the
/// whole catalogue. At 0.15 it can corroborate a candidate found some other way and nothing else.
/// </para>
/// <para>
/// Duplicate listings are the specific failure that clutters every incumbent catalogue. This is the
/// component that prevents it, and the middle band is why: above threshold merge, middling open a
/// suspected-duplicate pair with both pages live, below threshold a new game.
/// </para>
/// </remarks>
public sealed class IdentityMatcher(
    IGameDirectory games,
    IEndpointDirectory endpoints,
    IGameFieldStore fields,
    IGameFieldIndex index,
    DiscoveryOptions options)
{
    public async Task<IdentityVerdict> ResolveAsync(ProbeResult result, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.Outcome is not ProbeOutcome.Answered)
        {
            // No evidence of any kind. A refused connection carries no MSSP, no banner and nothing
            // else; guessing from an address alone is how duplicates and mis-merges both happen.
            return new IdentityVerdict.Fresh(null);
        }

        var observed = Observation.Of(result);
        var endpoint = await endpoints.ByAddressAsync(result.Host, result.Port, ct);

        var candidates = new HashSet<Guid>();
        if (endpoint is not null)
        {
            candidates.Add(endpoint.GameId);
        }

        await GatherAsync(candidates, IdentityFields.ClaimToken, observed.ClaimToken, ct);
        await GatherAsync(candidates, IdentityFields.Name, observed.Name, ct);
        await GatherAsync(candidates, IdentityFields.BannerHash, observed.BannerHash, ct);
        await GatherAsync(candidates, IdentityFields.Website, observed.Website, ct);
        await GatherAsync(candidates, IdentityFields.Contact, observed.Contact, ct);

        IdentityScore? best = null;
        foreach (var candidate in candidates)
        {
            if (!await games.ExistsAsync(candidate, ct))
            {
                // An endpoint or field row outliving its game is a repair job, not a match.
                continue;
            }

            var score = await ScoreAsync(candidate, observed, endpoint, ct);
            if (best is null || score.Score > best.Score)
            {
                best = score;
            }
        }

        if (best?.CandidateGameId is not { } gameId)
        {
            return new IdentityVerdict.Fresh(best);
        }

        return best.Score >= options.AutoMergeThreshold ? new IdentityVerdict.Merge(gameId, best)
            : best.Score >= options.ReviewThreshold ? new IdentityVerdict.Review(gameId, best)
            : new IdentityVerdict.Fresh(best);
    }

    private async Task GatherAsync(HashSet<Guid> candidates, string field, string? value, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        foreach (var id in await index.GamesWithFieldAsync(field, value.Trim(), ct))
        {
            candidates.Add(id);
        }
    }

    private async Task<IdentityScore> ScoreAsync(
        Guid gameId,
        Observation observed,
        KnownEndpoint? endpoint,
        CancellationToken ct)
    {
        var stored = await StoredAsync(gameId, ct);

        var signals = new List<IdentitySignal>
        {
            new(nameof(IdentityWeights.Endpoint), IdentityWeights.Endpoint,
                endpoint is not null && endpoint.GameId == gameId),

            new(nameof(IdentityWeights.MsspNameAndCreated), IdentityWeights.MsspNameAndCreated,
                Same(stored, IdentityFields.Name, observed.Name)
                && Same(stored, IdentityFields.Created, observed.Created)),

            new(nameof(IdentityWeights.BannerHash), IdentityWeights.BannerHash,
                Same(stored, IdentityFields.BannerHash, observed.BannerHash)),

            new(nameof(IdentityWeights.WebsiteOrContact), IdentityWeights.WebsiteOrContact,
                Same(stored, IdentityFields.Website, observed.Website)
                || Same(stored, IdentityFields.Contact, observed.Contact)),

            new(nameof(IdentityWeights.CodebaseAndVersion), IdentityWeights.CodebaseAndVersion,
                Same(stored, IdentityFields.Codebase, observed.Codebase)),

            new(nameof(IdentityWeights.ClaimToken), IdentityWeights.ClaimToken,
                Same(stored, IdentityFields.ClaimToken, observed.ClaimToken)),
        };

        return new IdentityScore(gameId, signals.Where(s => s.Matched).Sum(s => s.Weight), signals);
    }

    /// <summary>
    /// One value per field: the source that wins under <see cref="FieldPrecedence"/>, because
    /// <c>(game, field, source)</c> means several rows can answer one field and only the winner is what
    /// the site says this game's name is.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, string>> StoredAsync(Guid gameId, CancellationToken ct)
    {
        var rows = await fields.ForGameAsync(gameId, ct);

        return rows
            .GroupBy(row => row.Field, StringComparer.OrdinalIgnoreCase)
            .Select(group => FieldPrecedence.Winner(group))
            .Where(winner => winner is not null)
            .ToDictionary(winner => winner!.Field, winner => winner!.Value, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether a stored value and an observed one agree. <b>A placeholder on either side is an absence,
    /// and two absences are not an agreement</b> — the observed side has already been filtered, and the
    /// stored side is filtered here because an import or an older write may have persisted one.
    /// </summary>
    private static bool Same(IReadOnlyDictionary<string, string> stored, string field, string? candidate) =>
        !MsspDefaults.IsPlaceholder(candidate)
        && stored.TryGetValue(field, out var value)
        && !MsspDefaults.IsPlaceholder(value)
        && string.Equals(value.Trim(), candidate!.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// What one probe says about identity, with every placeholder already reduced to null.
    /// </summary>
    /// <remarks>
    /// Extracted so the filtering happens once, at the boundary, rather than at each of six comparison
    /// sites where the seventh would eventually forget.
    /// </remarks>
    private sealed record Observation(
        string? Name,
        string? Created,
        string? Website,
        string? Contact,
        string? Codebase,
        string? BannerHash,
        string? ClaimToken)
    {
        public static Observation Of(ProbeResult result) => new(
            MsspReading.MeaningfulName(result.Mssp),
            MsspReading.Meaningful(result.Mssp, IdentityMsspVariables.Created),
            MsspReading.Meaningful(result.Mssp, IdentityMsspVariables.Website),
            MsspReading.Meaningful(result.Mssp, IdentityMsspVariables.Contact),
            MsspReading.Meaningful(result.Mssp, IdentityMsspVariables.Codebase),
            FingerprintOf(result.Banner),
            ClaimTokenBeacon.Read(result));

        /// <summary>
        /// The banner's fingerprint, or null when the connect screen carried no text at all. A server
        /// that sends nothing before the first prompt is common, and the hash of an empty string is a
        /// perfectly stable value that every such server would share — an absence scoring as an
        /// agreement, at half the merge threshold.
        /// </summary>
        private static string? FingerprintOf(string? banner) =>
            banner is { Length: > 0 } && BannerFingerprint.Flatten(banner).Length > 0
                ? BannerFingerprint.Of(banner)
                : null;
    }
}
