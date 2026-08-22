using System.Net;

using MUI.Catalog;
using MUI.Crawl;

namespace MUI.Discovery;

/// <summary>
/// Scores a probe against the games it might already be (spec §7.3).
/// </summary>
/// <remarks>
/// <para>
/// Candidates are gathered by reverse lookup, not by scanning: the endpoint, then every game sharing
/// this probe's claim token, name, banner hash, website or contact. Each candidate is then scored
/// over all seven signals, so a candidate found by one signal is still credited for the others.
/// </para>
/// <para>
/// <b>Every signal is filtered through <see cref="MsspDefaults.IsPlaceholder"/> on both sides.</b> A
/// codebase default is the absence of a signal, not a weak one — every unedited PennMUSH publishes
/// <c>NAME "PennMUSH"</c>, and scored naively they'd all match on the strongest textual signal in the
/// table. Two absences must never score as an agreement, including the empty banner, whose
/// fingerprint is a stable hash of nothing.
/// </para>
/// <para>
/// <b><c>CODEBASE</c> is scored but never gathered on.</b> Nearly every MUSH in the catalogue reports
/// the same string, so gathering on it would make every probe's candidate set the whole catalogue. At
/// 0.15 it can only corroborate a candidate found some other way.
/// </para>
/// </remarks>
public sealed class IdentityMatcher(
    IGameDirectory games,
    IEndpointDirectory endpoints,
    IGameFieldStore fields,
    IGameFieldIndex index,
    DiscoveryOptions options,
    // Null on every caller that hasn't wired one; IdentityWeights.ResolvedEndpoint simply never
    // fires then. Production passes IHostScopeGuard's own SystemHostResolver, so no test performs a
    // live lookup by accident.
    IHostResolver? resolver = null,
    // Also optional, and only ever consulted to collapse already-merged listings into one before
    // counting how many publish a connect screen. Null means every game row counts for itself, which
    // over-counts a game whose twin was absorbed and so can only suppress a banner, never trust one.
    IMergeLog? merges = null)
{
    public async Task<IdentityVerdict> ResolveAsync(ProbeResult result, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.Outcome is not ProbeOutcome.Answered)
        {
            // No evidence: a refused connection carries no MSSP or banner. Guessing from the address
            // alone is how duplicates and mis-merges happen.
            return new IdentityVerdict.Fresh(null);
        }

        var endpoint = await endpoints.ByAddressAsync(result.Host, result.Port, ct);

        var (candidates, observed) = await CandidatesAsync(Observation.Of(result), endpoint, ct);

        var best = await BestAsync(candidates, observed, endpoint, null, ct);

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
    /// <b><see cref="ResolveAsync"/> cannot answer this, and that's the whole reason this exists.</b>
    /// A probe of an address we've already attributed scores its own game at 1.00 on the endpoint
    /// signal alone, so the bound game always wins and a twin standing right behind it is never
    /// returned.
    /// </para>
    /// <para>
    /// <b>A rival is never a merge, however high it scores.</b> The same score that would create
    /// nothing on first sighting means, against an already-bound address, "these two listings may be
    /// one" — and folding a live listing into another without a person looking isn't undoable. So the
    /// ceiling here is a review, with the score carried onto the pair.
    /// </para>
    /// </remarks>
    public async Task<IdentityScore?> RivalAsync(ProbeResult result, Guid bound, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.Outcome is not ProbeOutcome.Answered)
        {
            return null;
        }

        var (candidates, observed) = await CandidatesAsync(Observation.Of(result), endpoint: null, ct);
        candidates.Remove(bound);

        if (candidates.Count == 0)
        {
            // Common case: reverse lookups found nobody else, so nothing further is scored or read.
            return null;
        }

        var best = await BestAsync(candidates, observed, endpoint: null, bound, ct);

        return best is not null && best.Score >= options.ReviewThreshold ? best : null;
    }

    /// <summary>
    /// Every game this probe could be, and the observation the rest of the scoring should use — which
    /// is the one that came in, minus a connect screen that turns out not to identify a game.
    /// </summary>
    private async Task<(HashSet<Guid> Candidates, Observation Observed)> CandidatesAsync(
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

        if (observed.BannerHash is { } hash)
        {
            var holders = await index.GamesWithFieldAsync(IdentityFields.BannerHash, hash, ct);

            if (await IdentifiesOneGameAsync(holders, ct))
            {
                foreach (var id in holders)
                {
                    candidates.Add(id);
                }
            }
            else
            {
                observed = observed with { BannerHash = null };
            }
        }

        await GatherAsync(candidates, IdentityFields.Website, observed.Website, ct);
        await GatherAsync(candidates, IdentityFields.Contact, observed.Contact, ct);

        return (candidates, observed);
    }

    /// <summary>
    /// Whether a connect screen is this game's or its codebase's — measured from how many separate
    /// listings publish it, not asserted from a list of engines.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The length floor was not enough.</b> <see cref="BannerFingerprint.MinimumIdentifyingLength"/>
    /// catches a bare <c>login:</c>, and stock screens are nothing like that short — an unedited
    /// RhostMUSH sends 983 characters of "Welcome to RhostMUSH" and a connect-command legend, and six
    /// unrelated games behind one host published that byte for byte. Scored, it is
    /// <see cref="IdentityWeights.BannerHash"/> — over
    /// <see cref="IdentityWeights.ReviewThreshold"/> on its own — so every pair of them opened a
    /// review that no evidence could ever settle. Same for TinyMUX and TinyMUSH.
    /// </para>
    /// <para>
    /// <b>Already-merged listings collapse first.</b> One game reachable at three addresses is three
    /// game rows publishing one screen, and counting those as three would suppress the very signal
    /// that found them. They count as the one listing they redirect to, so this only ever fires on
    /// screens shared by games nobody has judged the same.
    /// </para>
    /// <para>
    /// <b>It contributes nothing rather than a little</b>, exactly as
    /// <see cref="MsspDefaults.IsPlaceholder"/> treats <c>NAME "PennMUSH"</c> — dropped from the
    /// candidate gather as well as from the score, since a non-answer must not be able to nominate a
    /// game either.
    /// </para>
    /// </remarks>
    private async Task<bool> IdentifiesOneGameAsync(IReadOnlyList<Guid> holders, CancellationToken ct)
    {
        if (holders.Count < options.SharedBannerListings)
        {
            return true;
        }

        var listings = new HashSet<Guid>();

        foreach (var holder in holders)
        {
            listings.Add(merges is null ? holder : await merges.ListingOfAsync(holder, ct));

            // The count only ever goes up, so the answer is settled the moment it reaches the floor.
            // This runs on the crawl hot path for every probe carrying a banner, and each lookup is
            // its own round trip — a stock screen shared by two hundred listings would otherwise cost
            // two hundred of them to learn what the third one already told us.
            if (listings.Count >= options.SharedBannerListings)
            {
                return false;
            }
        }

        return true;
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
    /// <b>An owner's answer is the one to show and the wrong one to match on.</b> <c>Owner</c>
    /// outranks <c>Mssp</c> in <see cref="FieldPrecedence"/> for display — §8.5 lets a verified owner
    /// override what MSSP says about their own game — but scored here that would let them type
    /// <c>NAME</c>/<c>CREATED</c> to match an unrelated game and trigger an unreviewable merge.
    /// <see cref="FieldSource.Staff"/> stays in: a staff row is the curator's correction, and there's
    /// no surface through which anybody else can write one.
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
    /// Bounded on every side: no resolver wired never fires; the probed host being a hostname (not a
    /// literal) never fires — that's <see cref="IdentityWeights.Endpoint"/>'s job; a candidate
    /// endpoint that's itself a literal is skipped, same reason; a mismatched port is skipped, since a
    /// different port is a different listener even on one machine.
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
                // A resolver failure is not evidence either way (rule 5: our failure isn't a
                // measurement of anybody) — this candidate just doesn't corroborate, and we move on.
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
        /// The banner's fingerprint, or null when the connect screen is too short to identify the
        /// game rather than its engine (see <see cref="BannerFingerprint.MinimumIdentifyingLength"/>).
        /// </summary>
        private static string? FingerprintOf(string? banner) =>
            banner is { Length: > 0 }
            && BannerFingerprint.Flatten(banner).Length >= BannerFingerprint.MinimumIdentifyingLength
                ? BannerFingerprint.Of(banner)
                : null;
    }
}
