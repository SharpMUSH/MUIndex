using System.Net;

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
/// scored over all seven signals, so a candidate found by one signal is still credited for the others.
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
    DiscoveryOptions options,
    // Null on every caller that has not wired one, and that degrades rather than fails:
    // IdentityWeights.ResolvedEndpoint simply never fires, exactly as ClaimToken never fires before
    // §8's verification half lands. The real one is IHostScopeGuard's own SystemHostResolver — the
    // one place live DNS is meant to be reached from — so production gets this signal by handing the
    // same resolver to both, and no test performs a lookup by accident.
    IHostResolver? resolver = null)
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

        var best = await BestAsync(await CandidatesAsync(observed, endpoint, ct), observed, endpoint, null, ct);

        if (best?.CandidateGameId is not { } gameId)
        {
            return new IdentityVerdict.Fresh(best);
        }

        return best.Score >= options.AutoMergeThreshold ? new IdentityVerdict.Merge(gameId, best)
            : best.Score >= options.ReviewThreshold ? new IdentityVerdict.Review(gameId, best)
            : new IdentityVerdict.Fresh(best);
    }

    /// <summary>
    /// The strongest game <em>other than</em> <paramref name="bound"/> that this probe could also be,
    /// at or above <see cref="DiscoveryOptions.ReviewThreshold"/> — or null when nothing else comes
    /// close (spec §7.3).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><see cref="ResolveAsync"/> cannot answer this question, and that is the whole reason this
    /// exists.</b> A probe of an address we have already attributed scores its own game at 1.00 on the
    /// endpoint signal alone, so the bound game is always the winner and a twin standing right behind
    /// it is never returned. Attribution is settled for such a probe; what is not settled is whether
    /// the catalogue is now listing one game twice.
    /// </para>
    /// <para>
    /// <b>A rival is never a merge, however high it scores.</b> Above the merge threshold on first
    /// sighting means "create nothing, this is that game"; the same score against an address already
    /// bound elsewhere means "these two listings may be one", and folding a live listing into another
    /// without a person looking is not a thing anybody undoes. So the ceiling here is a review, and
    /// the score is carried onto the pair so the operator sees what it was.
    /// </para>
    /// </remarks>
    public async Task<IdentityScore?> RivalAsync(ProbeResult result, Guid bound, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.Outcome is not ProbeOutcome.Answered)
        {
            return null;
        }

        var observed = Observation.Of(result);
        var candidates = await CandidatesAsync(observed, endpoint: null, ct);
        candidates.Remove(bound);

        if (candidates.Count == 0)
        {
            // The common case by a wide margin, and the reason this is affordable on every probe of
            // every bound target: reverse lookups on the index found nobody else, so no candidate is
            // scored and no field row — connect screens included — is read.
            return null;
        }

        var best = await BestAsync(candidates, observed, endpoint: null, bound, ct);

        return best is not null && best.Score >= options.ReviewThreshold ? best : null;
    }

    private async Task<HashSet<Guid>> CandidatesAsync(
        Observation observed,
        KnownEndpoint? endpoint,
        CancellationToken ct)
    {
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

        return candidates;
    }

    private async Task<IdentityScore?> BestAsync(
        HashSet<Guid> candidates,
        Observation observed,
        KnownEndpoint? endpoint,
        Guid? excluding,
        CancellationToken ct)
    {
        IdentityScore? best = null;
        foreach (var candidate in candidates)
        {
            if (candidate == excluding || !await games.ExistsAsync(candidate, ct))
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

        return best;
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
        var resolvedEndpointMatched = await ResolvesToKnownEndpointAsync(gameId, observed, ct);

        var signals = new List<IdentitySignal>
        {
            new(nameof(IdentityWeights.Endpoint), IdentityWeights.Endpoint,
                endpoint is not null && endpoint.GameId == gameId),

            new(nameof(IdentityWeights.ResolvedEndpoint), IdentityWeights.ResolvedEndpoint,
                resolvedEndpointMatched),

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
    /// the site says this game's name is — with <see cref="FieldSource.Owner"/> left out of the
    /// question entirely.
    /// </summary>
    /// <remarks>
    /// <b>An owner's answer is the one to show and the wrong one to match on.</b> <c>Owner</c> outranks
    /// <c>Mssp</c> in <see cref="FieldPrecedence"/>, correctly, for a page: §8.5 lets a verified owner
    /// override anything MSSP could declare about their own game. Scored here that would let them type
    /// <c>NAME</c> and <c>CREATED</c> to match a game they have nothing to do with — and §7.3 merges
    /// above threshold, which is not a thing anybody undoes. De-duplication asks which host is which
    /// game, and it answers by comparing what servers said to each other.
    ///
    /// <see cref="FieldSource.Staff"/> stays in, and the difference is not that we trust ourselves more.
    /// A staff row is the curator's correction — it exists to fix a catalogue that has merged two games
    /// or split one — so it is exactly the value identity should be steered by, and there is no surface
    /// through which anybody else can write one.
    /// </remarks>
    private async Task<IReadOnlyDictionary<string, string>> StoredAsync(Guid gameId, CancellationToken ct)
    {
        var rows = await fields.ForGameAsync(gameId, ct);

        return rows
            .Where(row => row.Source is not FieldSource.Owner)
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
    /// Whether this bare-IP probe reached the same (address, port) as one of this candidate's own known
    /// endpoints, resolved by name (spec §7.3, <see cref="IdentityWeights.ResolvedEndpoint"/>).
    /// </summary>
    /// <remarks>
    /// <b>Bounded on every side that matters.</b> No resolver wired: never fires. The probed host is
    /// itself a hostname rather than a literal: never fires — a hostname-to-hostname comparison is
    /// <see cref="IdentityWeights.Endpoint"/>'s job and this is not a second, looser way to reach the
    /// same credit. A candidate endpoint that is itself a literal: skipped — literal-to-literal is also
    /// <see cref="IdentityWeights.Endpoint"/>'s comparison, made at the top of <see cref="ResolveAsync"/>
    /// already. A port that does not match: skipped, because a different port is a different listener
    /// even on one machine.
    /// </remarks>
    private async Task<bool> ResolvesToKnownEndpointAsync(Guid gameId, Observation observed, CancellationToken ct)
    {
        if (resolver is null || !IPAddress.TryParse(observed.Host, out var probedAddress))
        {
            return false;
        }

        foreach (var candidateEndpoint in await endpoints.ForGameAsync(gameId, ct))
        {
            if (candidateEndpoint.Port != observed.Port || IPAddress.TryParse(candidateEndpoint.Host, out _))
            {
                continue;
            }

            IReadOnlyList<IPAddress> resolved;
            try
            {
                resolved = await resolver.ResolveAsync(candidateEndpoint.Host, ct);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                // HostScopeGuard's own idiom for the identical failure: a candidate's hostname not
                // resolving is not evidence either way, only a resolver having a bad minute, and it
                // must not crash a probe that happened to arrive by IP rather than by name. Rule 5's
                // shape in code — our resolver's failure is not a measurement of anybody — so this
                // candidate simply does not corroborate through this signal and the loop moves on.
                continue;
            }

            if (resolved.Any(address => address.Equals(probedAddress)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// What one probe says about identity, with every placeholder already reduced to null.
    /// </summary>
    /// <remarks>
    /// Extracted so the filtering happens once, at the boundary, rather than at each of the comparison
    /// sites where the next one would eventually forget.
    /// </remarks>
    private sealed record Observation(
        string Host,
        int Port,
        string? Name,
        string? Created,
        string? Website,
        string? Contact,
        string? Codebase,
        string? BannerHash,
        string? ClaimToken)
    {
        public static Observation Of(ProbeResult result) => new(
            result.Host,
            result.Port,
            MsspReading.MeaningfulName(result.Mssp),
            MsspReading.Meaningful(result.Mssp, IdentityMsspVariables.Created),
            MsspReading.Meaningful(result.Mssp, IdentityMsspVariables.Website),
            MsspReading.Meaningful(result.Mssp, IdentityMsspVariables.Contact),
            MsspReading.Meaningful(result.Mssp, IdentityMsspVariables.Codebase)
                ?? LoginCommandReading.MeaningfulCodebase(result.Info, result.Version),
            FingerprintOf(result.Banner),
            ClaimTokenBeacon.Read(result));

        /// <summary>
        /// The banner's fingerprint, or null when the connect screen carried too little text to be
        /// about this game rather than about its engine. A server that sends nothing before the first
        /// prompt is common, and the hash of an empty string is a perfectly stable value that every
        /// such server would share — an absence scoring as an agreement, at half the merge threshold.
        /// A stock "Do you want ANSI?" is the same absence with a few more characters in it, which is
        /// what <see cref="BannerFingerprint.MinimumIdentifyingLength"/> measures and why it is a
        /// length rather than the emptiness check this used to be.
        /// </summary>
        private static string? FingerprintOf(string? banner) =>
            banner is { Length: > 0 }
            && BannerFingerprint.Flatten(banner).Length >= BannerFingerprint.MinimumIdentifyingLength
                ? BannerFingerprint.Of(banner)
                : null;
    }
}
