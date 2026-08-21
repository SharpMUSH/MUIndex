using MUI.Catalog.Persistence;
using MUI.Catalog;
using MUI.Crawl;
using MUI.Discovery;

using Microsoft.Extensions.Logging;

using SchedulerBand = MUI.Discovery.ActivityBand;

using Polly;
using Polly.Retry;

namespace MUI.Crawler;

/// <summary>
/// One pass over the targets that are due: dial them, store what they said, follow what they pointed
/// at, and decide when each is next due.
/// </summary>
/// <remarks>
/// Every bound spec §12 asks for is here, none of it hygiene: the crawler shares a process with the
/// web tier, so a probe wedged on a black-holed host is a request thread that never comes back — a
/// global concurrency cap, per-host serialisation (<see cref="HostConcurrencyGate"/>, keyed on host alone since
/// a game on six ports is one machine), a rate floor via <see cref="CrawlRateLimiter"/>, and a hard
/// timeout linked to the cycle's own token, applied on top of whatever the probe promises. The order
/// per target is deliberate: consent (§11) before scope, since a game that asked us to stop is owed
/// nothing further; then scope, which stops a stranger's <c>REFERRAL</c> pointing a socket inside our
/// own network; then rate limit, dial, attribution, storage, referrals, schedule last — storage
/// before referrals so a game exists to hang an edge on, schedule last since it's computed from what
/// the probe found.
/// </remarks>
public sealed class CrawlCycle(
    ICrawlTargetRepository targets,
    IProbe probe,
    // §11's opt-out, asked before every dial. Not nullable: a gate a caller can leave out is a gate a
    // composition root forgets.
    OptOutGate optOut,
    // The concrete guard rather than IHostScopeGuard: only the class has RuleOnAsync, honouring
    // CrawlTarget.IsOperatorSeed — it can't be on the interface since MUI.Crawl doesn't know what a
    // CrawlTarget is.
    HostScopeGuard scope,
    ProbeIngestor ingestor,
    CatalogueBinder binder,
    ReferralGraphWriter referrals,
    CrawlRateLimiter limiter,
    HostConcurrencyGate gate,
    DiscoveryOptions options,
    TimeProvider time,
    // Optional, and null on every path that has no database behind it. A crawl that cannot settle
    // claims is a crawl doing slightly less, not a crawl that should refuse to run.
    ClaimService? claims = null,
    // §11's replay window. Optional for the same reason claims are — a crawl that cannot record a
    // shape is a crawl doing slightly less, not one that should refuse to dial.
    IProbePayloads? payloads = null,
    ILogger<CrawlCycle>? logger = null)
{
    /// <summary>Probes everything that is due, and returns what the pass did.</summary>
    public async Task<CycleReport> RunAsync(CancellationToken cancellationToken = default)
    {
        var due = await targets.DueAsync(time.GetUtcNow(), options.BatchSize, cancellationToken);

        if (due.Count == 0)
        {
            return CycleReport.Empty;
        }

        logger?.LogInformation("Crawl cycle: {Due} targets due", due.Count);

        var tally = new Tally();
        using var slots = new SemaphoreSlim(options.MaxConcurrency, options.MaxConcurrency);

        await Task.WhenAll(due.Select(target => VisitAsync(target, slots, tally, cancellationToken)));

        return tally.ToReport(due.Count);
    }

    private async Task VisitAsync(
        CrawlTarget target,
        SemaphoreSlim slots,
        Tally tally,
        CancellationToken cancellationToken)
    {
        await slots.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // One probe at a time per host, so a game advertising six ports is not hit six times at
            // once. Taken inside the concurrency slot rather than outside it, so a host with a queue
            // does not hold a slot it is not using.
            using var host = await gate.EnterAsync(target.Host, cancellationToken);

            await ProbeOneAsync(target, tally, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The host is stopping. Not this target's problem and not a failure of it: the registry is
            // monotonic and it is still due next time.
        }
        catch (Exception error)
        {
            // One target must never take the cycle down with it, because the cycle is the only thing
            // keeping every other game's data fresh.
            tally.Errored();
            logger?.LogError(error, "Probing {Host}:{Port} threw", target.Host, target.Port);

            // A target that threw is a target nothing rescheduled, so DueAsync would select it again
            // forever unless it's backed off here.
            await BackOffAsync(target, cancellationToken);
        }
        finally
        {
            slots.Release();
        }
    }

    /// <summary>
    /// Pushes a target that threw out to its ordinary failure backoff.
    /// </summary>
    /// <remarks>
    /// Its own try, because this runs in a catch block: if the registry is what threw, a second throw
    /// here would escape <see cref="VisitAsync"/> and take down the pass that is keeping every other
    /// game's data fresh — which is the thing the catch exists to prevent.
    /// </remarks>
    private async Task BackOffAsync(CrawlTarget target, CancellationToken cancellationToken)
    {
        try
        {
            var now = time.GetUtcNow();

            await targets.RecordAttemptAsync(
                target.Id,
                now,
                succeeded: false,
                crawlDelay: null,
                ProbeSchedule.NextProbeAt(
                    now, target.ConsecutiveFailures + 1, target.CrawlDelay, SchedulerBand.Unknown),
                cancellationToken);
        }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested)
        {
            logger?.LogError(
                error, "Could not reschedule {Host}:{Port} after an error", target.Host, target.Port);
        }
    }

    /// <summary>
    /// One retry around one dial, so a failure is confirmed before it is published.
    /// </summary>
    /// <remarks>
    /// A single failed dial is dominated in production by transient blips — a cold DNS lookup, a
    /// reset — where the game was fine and only the dial failed; publishing that as the game's
    /// reachability is rule 5, our own limitation recorded as a fact about them. The retried region
    /// covers resolution as well as the dial, and the rate limiter so a confirming dial waits its
    /// turn. Outside it: the opt-out check (a standing answer, not a measurement) and the scope
    /// guard's refusal (our policy, which doesn't become true by being asked twice). Only failures
    /// retry, so the common path pays nothing.
    /// </remarks>
    private ResiliencePipeline<Attempt> Confirming { get; } = options.ConfirmationAttempts == 0
        ? ResiliencePipeline<Attempt>.Empty
        : new ResiliencePipelineBuilder<Attempt>()
            .AddRetry(new RetryStrategyOptions<Attempt>
            {
                ShouldHandle = new PredicateBuilder<Attempt>().HandleResult(attempt => attempt.Failed),
                MaxRetryAttempts = options.ConfirmationAttempts,
                Delay = options.ConfirmationDelay,
                BackoffType = DelayBackoffType.Constant,
            })
            .Build();

    private async Task ProbeOneAsync(CrawlTarget target, Tally tally, CancellationToken cancellationToken)
    {
        // §11, and before the scope gate on purpose: somebody who has asked us to stop should not
        // have their name resolved either. Never retried — a "no" does not become a "maybe" because
        // we asked twice.
        if (await optOut.RuleOnAsync(target, cancellationToken) is { } asked)
        {
            await RefuseAsync(target, DialRefusal.OptedOut, asked.Wording, tally, cancellationToken);
            return;
        }

        var attempt = await Confirming.ExecuteAsync(
            async token => await AttemptAsync(target, token), cancellationToken);

        if (attempt.Refusal is { } refusal)
        {
            await RefuseAsync(target, DialRefusal.OutOfScope, refusal, tally, cancellationToken);
            return;
        }

        var result = attempt.Result!;

        // A cancellation here is our own shutdown, not a fact about the far end; the writes below are
        // fast enough to land before Npgsql looks at the token, so this guard is worth having even
        // though TelnetProbe already refuses to dress cancellation as a timeout.
        cancellationToken.ThrowIfCancellationRequested();

        // Read before anything is stored, so a game that used this reply to opt out is never dialled
        // again, including by the rest of this cycle (§11). What this probe measured is still stored.
        await optOut.HearAsync(target, result, cancellationToken);

        await StoreAsync(target, result, tally, cancellationToken);
    }

    /// <summary>One resolution and one dial — the thing a confirmation repeats.</summary>
    private async Task<Attempt> AttemptAsync(CrawlTarget target, CancellationToken cancellationToken)
    {
        var decision = await scope.RuleOnAsync(target, cancellationToken);

        switch (decision.Ruling)
        {
            case HostScopeRuling.RefusedNonGlobal:
                return new Attempt(null, decision.Detail ?? "resolved somewhere we will not dial");

            case HostScopeRuling.Unresolvable:
                return new Attempt(Unresolved(target, decision), null);
        }

        // Politeness applies to a confirming dial exactly as a first one, which is why this is inside
        // the retried region — being unsure about a game is not a reason to knock harder.
        await limiter.WaitForTurnAsync(target.Host, cancellationToken);

        // The loop's own bound on top of ProbeOptions.Timeout, linked so a stopping host cancels a
        // probe in flight. Neither way this token cancels is a measurement: a stopping host leaves
        // the target due (VisitAsync); this ceiling firing is a fault in our probe, so it's counted
        // as Errored and backed off there — never as an availability row, since a host we never
        // finished dialling has not been measured, and "unreachable" would be our own limitation
        // published as their downtime (rule 5). The measurement ceiling stays ProbeOptions.Timeout,
        // inside the probe, and still records cause "timeout" as a real fact about the far end.
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(options.ProbeTimeout);

        var result = await probe.ProbeAsync(
            new ProbeTarget(target.Host, target.Port)
            {
                Charset = target.Charset,

                // The addresses the guard just vetted, so the dial reaches what was ruled on and the
                // name is resolved once rather than twice.
                Addresses = decision.Addresses,
            },
            budget.Token);

        return new Attempt(result, null);
    }

    /// <summary>
    /// What one attempt produced: a probe result, or the scope guard's reason for not dialling.
    /// </summary>
    /// <remarks>
    /// Kept apart rather than folded into one <c>ProbeResult</c>: a refusal happens before a probe
    /// exists, and dressing our own policy as a measured failure puts it into a game's public
    /// reachability history where nothing downstream can tell the two apart again.
    /// </remarks>
    private sealed record Attempt(ProbeResult? Result, string? Refusal)
    {
        /// <summary>Whether this is a dial worth confirming before anybody believes it.</summary>
        public bool Failed => Result is { Outcome: ProbeOutcome.Failed };
    }

    /// <summary>
    /// A dial we declined to make — because of where the name resolved (spec §7.2) or because the game
    /// asked us not to (§11).
    /// </summary>
    /// <remarks>
    /// Nothing is written to the game's record — no availability sample, presence row, or field. We
    /// declined to dial; we did not measure. Recording either as downtime is rule 5, so both are
    /// counted on the cycle instead, and separately: "we would not go there" and "they asked us not
    /// to" are different facts. The schedule still must move, or the target is due forever; this
    /// takes <c>succeeded: true</c> since a refusal is not the host failing, at the cost of clearing a
    /// previously failing target's count — which nothing acts on, since a refused target is never
    /// dialled.
    /// </remarks>
    private async Task RefuseAsync(
        CrawlTarget target,
        DialRefusal reason,
        string detail,
        Tally tally,
        CancellationToken cancellationToken)
    {
        tally.Refused(reason);

        // An opt-out is not a warning. Somebody exercised a documented choice and the crawler did what
        // it was told; logging it as though something had gone wrong would eventually train an
        // operator to go looking for the fix.
        if (reason is DialRefusal.OptedOut)
        {
            logger?.LogInformation("Not dialling {Host}:{Port} — {Detail}", target.Host, target.Port, detail);
        }
        else
        {
            logger?.LogWarning("Refusing {Host}:{Port} — {Detail}", target.Host, target.Port, detail);
        }

        await targets.RecordAttemptAsync(
            target.Id,
            time.GetUtcNow(),
            succeeded: true,
            crawlDelay: null,
            time.GetUtcNow() + ProbeSchedule.LongestInterval,
            cancellationToken);
    }

    /// <summary>
    /// A name that did not resolve, which is an ordinary DNS failure and gets ordinary backoff
    /// (spec §7.2).
    /// </summary>
    /// <remarks>
    /// The one place a <see cref="ProbeResult"/> is constructed outside the probe. Legitimate only
    /// because "could not resolve" and "resolved somewhere we won't go" are different facts, and only
    /// the second is a refusal — this is a real measurement, the same one <c>TelnetProbe</c> would
    /// have produced had the guard let the dial through. Manufacturing a result for a refusal instead
    /// is forbidden; see <see cref="HostScopeGuard"/>.
    /// </remarks>
    private ProbeResult Unresolved(CrawlTarget target, HostScopeDecision decision) => new()
    {
        Host = target.Host,
        Port = target.Port,
        ObservedAt = time.GetUtcNow(),
        Outcome = ProbeOutcome.Failed,
        Failure = new FailureDetail("dns", decision.Detail),
    };

    /// <summary>
    /// Offers whatever claim beacon this probe carried to the claim store (spec §8.1).
    /// </summary>
    /// <remarks>
    /// Deliberately this small: the crawler reads the beacon and knows nothing about what it means,
    /// <see cref="ClaimService"/> decides and knows nothing about sockets. A probe that read no beacon
    /// does nothing, rather than reporting an absence — §8.4: presence establishes, absence never
    /// revokes.
    /// </remarks>
    private async Task SettleClaimsAsync(Guid gameId, ProbeResult result, CancellationToken cancellationToken)
    {
        if (claims is null || ClaimTokenBeacon.Find(result) is not { } beacon)
        {
            return;
        }

        var verdict = await claims.OfferBeaconAsync(gameId, beacon.Token, beacon.Channel, cancellationToken);

        if (verdict is ClaimVerdict.Verified)
        {
            logger?.LogInformation(
                "{Host}:{Port} published a claim token we issued; the claim is verified via {Channel}",
                result.Host, result.Port, beacon.Channel);
        }
    }

    private async Task StoreAsync(
        CrawlTarget target,
        ProbeResult result,
        Tally tally,
        CancellationToken cancellationToken)
    {
        var answered = result.Outcome is ProbeOutcome.Answered;
        var binding = await binder.BindAsync(target, result, cancellationToken);

        // §11's shape, recorded once the game is known rather than before — recording ahead of the
        // binder would write a null game_id for the first successful probe of every game.
        await RecordShapeAsync(target, binding?.GameId ?? target.GameId, result, cancellationToken);

        var activity = SchedulerBand.Unknown;

        if (binding is not null)
        {
            if (target.GameId is null)
            {
                await targets.AttachGameAsync(target.Id, binding.GameId, cancellationToken);
            }

            var ingestion = await ingestor.IngestAsync(binding.GameId, result, cancellationToken);
            activity = ingestion.Activity;

            tally.Ingested(binding, ingestion);

            if (answered)
            {
                var intake = await referrals.ApplyAsync(
                    binding.GameId, target.Depth, result, cancellationToken);
                tally.Referred(intake);

                await SettleClaimsAsync(binding.GameId, result, cancellationToken);
            }
        }

        tally.Probed(answered, result);

        if (!answered)
        {
            // The detail as well as the cause word: the cause vocabulary alone is too coarse to act on.
            logger?.LogInformation(
                "{Host}:{Port} did not answer — {Cause}: {Detail}",
                result.Host,
                result.Port,
                result.Failure?.Cause ?? "unknown",
                result.Failure?.Detail ?? "no detail recorded");
        }

        // Last, because it is computed from what the probe found. ProbeSchedule owns the arithmetic;
        // nothing here reimplements it.
        var failures = answered ? 0 : target.ConsecutiveFailures + 1;
        var now = time.GetUtcNow();

        await targets.RecordAttemptAsync(
            target.Id,
            now,
            answered,
            MsspCrawlDelay.From(result),
            ProbeSchedule.NextProbeAt(now, failures, MsspCrawlDelay.From(result) ?? target.CrawlDelay, activity),
            cancellationToken);
    }

    /// <summary>
    /// Records the shape of what this probe read, for §11's replay window.
    /// </summary>
    /// <remarks>
    /// Failure here is swallowed to a warning: a shape is evidence about our parser and nothing on
    /// the site is derived from one, so a write that fails must not cost the measurement stored after
    /// it.
    /// </remarks>
    private async Task RecordShapeAsync(
        CrawlTarget target,
        Guid? gameId,
        ProbeResult result,
        CancellationToken cancellationToken)
    {
        if (payloads is null || result.WhoShape is not { Length: > 0 } shape)
        {
            return;
        }

        try
        {
            await payloads.RecordAsync(
                [
                    new ProbePayload(
                        gameId,
                        target.Host,
                        target.Port,
                        time.GetUtcNow(),
                        ProbePayloadKind.Who,
                        shape),
                ],
                cancellationToken);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger?.LogWarning(error, "The probe shape for {Target} was not recorded", target);
        }
    }

    /// <summary>Mutable running total for one cycle. Written from every worker, so it locks.</summary>
    private sealed class Tally
    {
        private readonly Lock _gate = new();

        private int _probed;
        private int _answered;
        private int _failed;
        private int _refused;
        private int _optedOut;
        private int _errored;
        private int _listed;
        private int _reviews;
        private int _counted;
        private int _unmeasurable;
        private int _transitions;
        private int _referralsAdded;

        public void Probed(bool answered, ProbeResult result)
        {
            _ = result;

            lock (_gate)
            {
                _probed++;
                if (answered)
                {
                    _answered++;
                }
                else
                {
                    _failed++;
                }
            }
        }

        public void Refused(DialRefusal reason)
        {
            lock (_gate)
            {
                if (reason is DialRefusal.OptedOut)
                {
                    _optedOut++;
                }
                else
                {
                    _refused++;
                }
            }
        }

        public void Errored()
        {
            lock (_gate)
            {
                _errored++;
            }
        }

        public void Ingested(Binding binding, Ingestion ingestion)
        {
            lock (_gate)
            {
                if (binding.Created)
                {
                    _listed++;
                }

                if (binding.ReviewedAgainst is not null)
                {
                    _reviews++;
                }

                switch (ingestion.Presence)
                {
                    case PresenceOutcome.Counted:
                        _counted++;
                        break;
                    case PresenceOutcome.RecordedUnmeasurable:
                        _unmeasurable++;
                        break;
                }

                if (ingestion.Availability is not AvailabilityOutcome.Extended)
                {
                    _transitions++;
                }
            }
        }

        public void Referred(ReferralIntake intake)
        {
            lock (_gate)
            {
                _referralsAdded += intake.Added;
            }
        }

        public CycleReport ToReport(int considered)
        {
            lock (_gate)
            {
                return new CycleReport(
                    considered, _probed, _answered, _failed, _refused, _optedOut, _errored,
                    _listed, _reviews, _counted, _unmeasurable, _transitions, _referralsAdded);
            }
        }
    }
}

/// <summary>
/// What one pass did. Enough for an operator to tell a quiet night from a broken crawler.
/// </summary>
/// <remarks>
/// <see cref="Refused"/> and <see cref="OptedOut"/> are on this report and in no game's record — a
/// decision of ours counted where decisions of ours belong, and kept as two figures because "we
/// would not dial there" and "they asked us not to" are different decisions a single column would
/// hide.
/// </remarks>
public sealed record CycleReport(
    int Considered,
    int Probed,
    int Answered,
    int Failed,
    int Refused,
    int OptedOut,
    int Errored,
    int Listed,
    int ReviewsOpened,
    int Counted,
    int Unmeasurable,
    int Transitions,
    int ReferralsAdded)
{
    public static readonly CycleReport Empty = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    public override string ToString() =>
        $"{Considered} due · {Answered} answered · {Failed} failed · {Refused} refused · "
        + $"{OptedOut} opted out · {Errored} errored · {Listed} newly listed · {ReviewsOpened} reviews · "
        + $"{Counted} counted · {Unmeasurable} uncountable · {Transitions} transitions · "
        + $"{ReferralsAdded} referrals added";
}
