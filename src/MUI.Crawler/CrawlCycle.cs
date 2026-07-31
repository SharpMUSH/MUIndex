using MUI.Catalog;
using MUI.Crawl;
using MUI.Discovery;

using Microsoft.Extensions.Logging;

using SchedulerBand = MUI.Discovery.ActivityBand;

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
/// <b>The order of operations per target is itself the design.</b> Scope first, because the gate is
/// what stops a stranger's <c>REFERRAL</c> pointing a socket inside our own network; then the rate
/// limit; then the dial; then attribution; then storage; then referrals; then the schedule. Storage
/// happens before referrals so that a game exists to hang an edge on, and the schedule happens last
/// so that it can be computed from what the probe actually found.
/// </para>
/// </remarks>
public sealed class CrawlCycle(
    ICrawlTargetRepository targets,
    IProbe probe,
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
        }
        finally
        {
            slots.Release();
        }
    }

    private async Task ProbeOneAsync(CrawlTarget target, Tally tally, CancellationToken cancellationToken)
    {
        var decision = await scope.RuleOnAsync(target, cancellationToken);

        switch (decision.Ruling)
        {
            case HostScopeRuling.RefusedNonGlobal:
                await RefuseAsync(target, decision, tally, cancellationToken);
                return;

            case HostScopeRuling.Unresolvable:
                await UnresolvableAsync(target, decision, tally, cancellationToken);
                return;
        }

        await limiter.WaitForTurnAsync(target.Host, cancellationToken);

        // The loop's own bound, on top of ProbeOptions.Timeout. Linked, so a stopping host cancels a
        // probe in flight rather than waiting out its budget.
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(options.ProbeTimeout);

        var result = await probe.ProbeAsync(new ProbeTarget(target.Host, target.Port), budget.Token);

        await StoreAsync(target, result, tally, cancellationToken);
    }

    /// <summary>
    /// A dial we declined to make (spec §7.2).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Nothing is written to the game's record.</b> No availability sample, no presence row, no
    /// field. We declined to dial; we did not measure. Recording it as downtime would put our own
    /// security policy into a game's public reachability history, which is the same class of lie as
    /// recording an unparseable <c>WHO</c> as zero players. It is counted on the cycle instead, which
    /// is where a decision of ours belongs.
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
        HostScopeDecision decision,
        Tally tally,
        CancellationToken cancellationToken)
    {
        tally.Refused();

        logger?.LogWarning(
            "Refusing {Host}:{Port} — {Detail}", target.Host, target.Port, decision.Detail);

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
    private async Task UnresolvableAsync(
        CrawlTarget target,
        HostScopeDecision decision,
        Tally tally,
        CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();

        var result = new ProbeResult
        {
            Host = target.Host,
            Port = target.Port,
            ObservedAt = now,
            Outcome = ProbeOutcome.Failed,
            Failure = new FailureDetail("dns", decision.Detail),
        };

        await StoreAsync(target, result, tally, cancellationToken);
    }

    private async Task StoreAsync(
        CrawlTarget target,
        ProbeResult result,
        Tally tally,
        CancellationToken cancellationToken)
    {
        var answered = result.Outcome is ProbeOutcome.Answered;
        var binding = await binder.BindAsync(target, result, cancellationToken);

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
            }
        }

        tally.Probed(answered, result);

        if (!answered)
        {
            logger?.LogInformation(
                "{Host}:{Port} did not answer — {Cause}",
                result.Host, result.Port, result.Failure?.Cause ?? "unknown");
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
    private sealed class Tally
    {
        private readonly Lock _gate = new();

        private int _probed;
        private int _answered;
        private int _failed;
        private int _refused;
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

        public void Refused()
        {
            lock (_gate)
            {
                _refused++;
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
                    considered, _probed, _answered, _failed, _refused, _errored,
                    _listed, _reviews, _counted, _unmeasurable, _transitions, _referralsAdded);
            }
        }
    }
}

/// <summary>
/// What one pass did. Enough for an operator to tell a quiet night from a broken crawler.
/// </summary>
/// <remarks>
/// <see cref="Refused"/> is on this report and in no game's record, which is §7.2's rule expressed as
/// a place: a decision of ours is counted where decisions of ours are counted.
/// </remarks>
public sealed record CycleReport(
    int Considered,
    int Probed,
    int Answered,
    int Failed,
    int Refused,
    int Errored,
    int Listed,
    int ReviewsOpened,
    int Counted,
    int Unmeasurable,
    int Transitions,
    int ReferralsAdded)
{
    public static readonly CycleReport Empty = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    public override string ToString() =>
        $"{Considered} due · {Answered} answered · {Failed} failed · {Refused} refused · "
        + $"{Errored} errored · {Listed} newly listed · {ReviewsOpened} reviews · "
        + $"{Counted} counted · {Unmeasurable} uncountable · {Transitions} transitions · "
        + $"{ReferralsAdded} referrals added";
}
