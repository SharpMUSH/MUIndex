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
/// <para>
/// <b>Every bound spec §12 asks for is here, and none of them is hygiene.</b> The crawler shares a
/// process with the web tier, so a probe that wedges against a black-holed host is a request thread
/// that never comes back. So: a global concurrency cap (<see cref="DiscoveryOptions.MaxConcurrency"/>,
/// a semaphore, because it is a fact about connections in flight); per-host serialisation
/// (<see cref="HostGate"/>, keyed on the host alone, because a game advertising six ports is one
/// machine and one operator); a rate floor between any two connections and a longer one per host
/// (<see cref="CrawlRateLimiter"/>); and a hard timeout on every probe, linked to the cycle's own
/// token, applied by this loop <em>on top of</em> whatever the probe promises. The loop does not get
/// to trust a collaborator for the one bound that keeps the site up.
/// </para>
/// <para>
/// <b>The order of operations per target is itself the design.</b> Consent first, because a game that
/// has asked us to stop is owed nothing further and the cheapest way to honour that is not to look it
/// up at all (§11); then scope, because the gate is what stops a stranger's <c>REFERRAL</c> pointing a
/// socket inside our own network; then the rate limit; then the dial; then attribution; then storage;
/// then referrals; then the schedule. Storage happens before referrals so that a game exists to hang
/// an edge on, and the schedule happens last so that it can be computed from what the probe actually
/// found.
/// </para>
/// </remarks>
public sealed class CrawlCycle(
    ICrawlTargetRepository targets,
    IProbe probe,
    // §11's opt-out, asked before every dial. Not optional and not nullable: a gate that a caller can
    // leave out is a gate that a composition root forgets, and the thing it would forget is somebody
    // else's stated wishes.
    OptOutGate optOut,
    // The concrete guard rather than IHostScopeGuard: only the class has RuleOnAsync, which is the
    // arm that honours CrawlTarget.IsOperatorSeed — and it cannot be on the interface, because
    // MUI.Crawl (where the interface lives) does not know what a CrawlTarget is. The resolver behind
    // it is injectable, so this stays testable without live DNS.
    HostScopeGuard scope,
    ProbeIngestor ingestor,
    CatalogueBinder binder,
    ReferralGraphWriter referrals,
    CrawlRateLimiter limiter,
    HostGate gate,
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

            // And it must not go on costing a batch slot either. A target that threw is a target
            // nothing rescheduled, so DueAsync selects it again next cycle and for ever — and the
            // batch it crowds out is other games' freshness. Backed off as a failure, which is what
            // an attempt that did not complete is, and the backoff is ProbeSchedule's own.
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
    /// <para>
    /// <b>The measurement this fixes.</b> Over four days of production ending 2026-08-18, 182 dark
    /// episodes were published across 171 of 538 listed games, and 173 of them were a single failed
    /// probe followed immediately by a successful one — 86% of all downtime the site reported. The
    /// games were fine. What failed was one dial: a cold DNS lookup that took five seconds and gave
    /// up, a reset, a momentary refusal. Publishing that as the game's reachability is rule 5 —
    /// a limitation of ours recorded as a fact about them.
    /// </para>
    /// <para>
    /// <b>What is inside the retried region and why.</b> Resolution as well as the dial, because a
    /// transient lookup failure was the largest single share of the blips and the guard fails closed
    /// on one. The rate limiter too, so a confirming dial waits its turn like any other. Outside it:
    /// the opt-out check, which is a standing answer rather than a measurement, and the scope
    /// guard's <em>refusal</em>, which is our policy and does not become true by being asked twice.
    /// </para>
    /// <para>
    /// Only failures retry, so the common path pays nothing at all.
    /// </para>
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

        // Nothing measured while this host was being taken down is a fact about the far end, and the
        // writes below are fast enough against a Postgres on the same network to land before Npgsql
        // ever looks at the token. TelnetProbe already refuses to dress our cancellation as a
        // timeout, so this is the second lock on the same door rather than the only one — and it is
        // worth having, because the failure it prevents is silent, permanent (rule 3) and published.
        cancellationToken.ThrowIfCancellationRequested();

        // Read before anything is stored, so that a game which used this reply to ask us to stop is
        // never dialled again — including by the rest of this same cycle (§11's "within one cycle").
        // What this probe measured is still stored: the reply was sent to a connection already made,
        // nothing here is ever deleted, and the about page says as much.
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

        // Politeness applies to a confirming dial exactly as it does to a first one, which is why
        // this is inside the retried region: PerHostInterval is the floor under the gap between two
        // dials at one host, and being unsure about a game is not a reason to knock harder.
        await limiter.WaitForTurnAsync(target.Host, cancellationToken);

        // The loop's own bound, on top of ProbeOptions.Timeout. Linked, so a stopping host cancels a
        // probe in flight rather than waiting out its budget.
        //
        // NEITHER OF THE TWO WAYS THIS TOKEN CANCELS IS A MEASUREMENT, and they are not the same
        // non-measurement. A stopping host is nobody's business but ours and leaves the target due
        // (VisitAsync). This ceiling firing means the probe overran the twenty seconds it promised
        // by another forty, which is a fault in our probe — so it lands in VisitAsync's generic
        // handler, is counted as Errored on the cycle where failures of ours belong, and is backed
        // off. What must never happen on either path is an availability row: a host we never
        // finished dialling has not been measured, and "unreachable" would be our own limitation
        // published as their downtime (rule 5). Pinned by
        // CrawlCyclePostgresTests.TheCrawlLoopsOwnCeilingIsCountedOnTheCycleAndNotAgainstTheGame.
        //
        // The measurement ceiling is ProbeOptions.Timeout, inside the probe, and it still records
        // cause "timeout" — that one is a fact about the far end and is unchanged.
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(options.ProbeTimeout);

        var result = await probe.ProbeAsync(
            new ProbeTarget(target.Host, target.Port)
            {
                Charset = target.Charset,

                // The addresses the guard just vetted, so the dial reaches what was ruled on and the
                // name is resolved once rather than twice. See ProbeTarget.Addresses.
                Addresses = decision.Addresses,
            },
            budget.Token);

        return new Attempt(result, null);
    }

    /// <summary>
    /// What one attempt produced: a probe result, or the scope guard's reason for not dialling.
    /// </summary>
    /// <remarks>
    /// The two are kept apart rather than folded into one <c>ProbeResult</c>, and
    /// <see cref="DialRefusal"/> says why: a refusal happens before a probe exists, and dressing our
    /// own policy as a measured failure puts it into a game's public reachability history where
    /// nothing downstream can tell the two apart again.
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
    /// <para>
    /// <b>Nothing is written to the game's record.</b> No availability sample, no presence row, no
    /// field. We declined to dial; we did not measure. Recording either as downtime would put our own
    /// security policy or our own politeness into a game's public reachability history, which is the
    /// same class of lie as recording an unparseable <c>WHO</c> as zero players. Both are counted on
    /// the cycle instead, which is where decisions of ours belong — and counted <em>separately</em>,
    /// because "we would not go there" and "they asked us not to" are different facts and an operator
    /// reading one number could not tell them apart.
    /// </para>
    /// <para>
    /// <b>The schedule is the one thing that must move</b>, or the target is due for ever and re-burns
    /// a batch slot every cycle. <see cref="ICrawlTargetRepository.RecordAttemptAsync"/> offers two
    /// arms and neither means "leave the failure count alone", so this takes <c>succeeded: true</c>:
    /// a refusal is not the host failing, and lengthening the backoff would be exactly the
    /// policy-as-measurement this whole paragraph exists to prevent. The cost is that a previously
    /// failing target has its count cleared — which nothing acts on, because a refused target is never
    /// dialled. A third arm on that interface would remove the choice.
    /// </para>
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
    /// <b>This is the one place a <see cref="ProbeResult"/> is constructed outside the probe, and the
    /// distinction that makes it legitimate is §7.2's own.</b> "Could not resolve" and "resolved
    /// somewhere we won't go" are different facts, and only the second is a refusal — the first is a
    /// measurement of the world, and it is the same measurement <c>TelnetProbe</c> would have produced
    /// (its <c>Classify</c> maps <c>SocketError.HostNotFound</c> to the same <c>dns</c> cause) had the
    /// guard let the dial through. Manufacturing a result for a <em>refusal</em> would be the opposite
    /// and is forbidden; see <see cref="HostScopeGuard"/>'s remarks for why the two can never be
    /// allowed to look alike downstream.
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
    /// <para>
    /// This is the whole of the verification step, and it is deliberately this small: the crawler
    /// reads the beacon and knows nothing about what it means, and <see cref="ClaimService"/> decides
    /// and knows nothing about sockets. Every probe of a claimed game passes through here, which is
    /// also how <c>beacon_last_seen_at</c> stays current without a second schedule.
    /// </para>
    /// <para>
    /// <b>A probe that read no beacon does nothing at all</b>, rather than reporting an absence. §8.4:
    /// presence establishes, absence never revokes — and a silence here would be indistinguishable
    /// from a compression bug eating the subnegotiation that carried it.
    /// </para>
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

        // §11's shape, recorded once the game is known rather than before. The binder is what turns
        // an address into a game, so recording ahead of it wrote a null game_id for the FIRST
        // successful probe of every game — the one probe whose shape a replay most wants, filtered
        // out of the window by the very query that reads it.
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
            // The message as well as the word. The cause vocabulary is six words wide and three of
            // them are wastebaskets, so "timeout" alone has never been enough to act on.
            logger?.LogInformation(
                "{Host}:{Port} did not answer — {Cause}: {Detail}",
                result.Host,
                result.Port,
                result.Failure?.Cause ?? "unknown",
                result.Failure?.Detail ?? "no detail recorded");
        }

        // Last, because it is computed from what the probe found: max(CRAWL DELAY, backoff), with the
        // backoff clamped to a week and the server's own request applied afterwards, so politeness
        // wins (§7.7). ProbeSchedule owns that arithmetic; nothing here reimplements it.
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

    /// <summary>Mutable running total for one cycle. Written from every worker, so it locks.</summary>
    /// <summary>
    /// Records the shape of what this probe read, for §11's replay window.
    /// </summary>
    /// <remarks>
    /// Failure here is swallowed to a warning. A shape is evidence about our parser and nothing on
    /// the site is derived from one, so a write that fails must not cost the measurement the probe
    /// just took — which is stored after this and is what the crawl exists for.
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
/// <see cref="Refused"/> and <see cref="OptedOut"/> are on this report and in no game's record, which
/// is §7.2's and §11's rule expressed as a place: a decision of ours is counted where decisions of
/// ours are counted. They are two figures rather than one because they are two different decisions —
/// "we would not dial there" is a security policy and "they asked us not to" is somebody else's
/// wishes, and a single "refused" column would hide the second inside the first.
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
