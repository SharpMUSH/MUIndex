using MUI.Crawler;

namespace MUI.Web.Diagnostics;

/// <summary>
/// What the crawl loop has done since this process started.
/// </summary>
/// <remarks>
/// <para>
/// Fed from the <see cref="CycleReport"/> the loop already returns rather than from instrumentation
/// threaded through <c>CrawlCycle</c>: the report is the cycle's own account of itself, and a second
/// set of numbers counted somewhere else could drift from it and then have to be reconciled.
/// </para>
/// <para>
/// <b>A refusal is never folded into a failure.</b> Rule 5 — a decision of ours must not appear as a
/// measurement of theirs — is not only about what reaches a game's public record; a dashboard whose
/// failure line includes the hosts we declined to dial is the same misreading in a different
/// surface, and it is the one an operator would act on at three in the morning. The two refusal
/// reasons stay apart for the reason <see cref="CycleReport"/> keeps them apart.
/// </para>
/// </remarks>
public sealed class CrawlMetrics(TimeProvider time) : ICycleObserver
{
    private readonly Lock _gate = new();

    /// <summary>When the last cycle finished, or null before there has been one.</summary>
    private DateTimeOffset? _lastCycleAt;

    private long _cycles;
    private long _targets;
    private long _probed;
    private long _answered;
    private long _failed;
    private long _errored;
    private long _refusedOutOfScope;
    private long _refusedOptedOut;
    private long _counted;
    private long _unmeasurable;
    private long _listed;
    private long _referrals;

    /// <summary>
    /// <see cref="ICycleObserver"/>'s name for <see cref="Record"/>, which is what the crawl loop
    /// calls. Never throws: the loop calls this on the path that schedules the next cycle, and a
    /// counter that could not be incremented must not cost a crawl.
    /// </summary>
    void ICycleObserver.Observe(CycleReport report) => Record(report);

    /// <summary>Takes one cycle's report.</summary>
    public void Record(CycleReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        lock (_gate)
        {
            _lastCycleAt = time.GetUtcNow();
            _cycles++;
            _targets += report.Considered;
            _probed += report.Probed;
            _answered += report.Answered;
            _failed += report.Failed;
            _errored += report.Errored;
            _refusedOutOfScope += report.Refused;
            _refusedOptedOut += report.OptedOut;
            _counted += report.Counted;
            _unmeasurable += report.Unmeasurable;
            _listed += report.Listed;
            _referrals += report.ReferralsAdded;
        }
    }

    public void WriteTo(PrometheusText text)
    {
        ArgumentNullException.ThrowIfNull(text);

        lock (_gate)
        {
            text.Counter(
                "mui_crawl_cycles_total",
                "Crawl cycles this process has completed.",
                _cycles);

            text.Counter(
                "mui_crawl_targets_total",
                "Targets found due across every cycle, whether or not they were dialled.",
                _targets);

            text.Counter(
                "mui_crawl_probed_total",
                "Targets actually dialled, which is targets considered less those refused.",
                _probed);

            const string Outcomes =
                "Probes by what the far end did. Measurements only: a host we declined to dial is "
                + "not here, it is in mui_crawl_refusals_total.";

            text.Counter("mui_crawl_outcomes_total", Outcomes, _answered, ("outcome", "answered"));
            text.Counter("mui_crawl_outcomes_total", Outcomes, _failed, ("outcome", "failed"));
            text.Counter("mui_crawl_outcomes_total", Outcomes, _errored, ("outcome", "errored"));

            const string Refusals =
                "Dials this site decided not to make. A decision of ours, counted where decisions of "
                + "ours belong — never as the far end's downtime.";

            text.Counter("mui_crawl_refusals_total", Refusals, _refusedOutOfScope, ("reason", "out_of_scope"));
            text.Counter("mui_crawl_refusals_total", Refusals, _refusedOptedOut, ("reason", "opted_out"));

            const string Presence =
                "Answered probes by whether a player count could be read. Uncountable is a reading, "
                + "not the absence of one: a roster we cannot parse is not a game nobody was playing.";

            text.Counter("mui_crawl_presence_total", Presence, _counted, ("reading", "counted"));
            text.Counter("mui_crawl_presence_total", Presence, _unmeasurable, ("reading", "unmeasurable"));

            text.Counter(
                "mui_crawl_listed_total",
                "Games listed for the first time.",
                _listed);

            text.Counter(
                "mui_crawl_referrals_total",
                "Referral edges added.",
                _referrals);

            // When the crawl last got all the way round, as a moment rather than a flag.
            //
            // **This was `mui_crawl_lease_held`, and it was a claim we had not measured.** It was set
            // by the first cycle and never cleared, while its name and help text were both present
            // tense — and LeasedBackgroundService can find its lease connection gone, release it, and
            // go back to asking, at which point the gauge went on reporting a lease this replica had
            // given up. A fact about the past presented as a fact about the present is the shape of
            // mistake rule 5 exists to refuse, and it does not stop being one because the subject is
            // us rather than a game.
            //
            // A timestamp is what was actually observed, and it answers the operational question
            // better than the flag did: `time() - mui_crawl_last_cycle_timestamp_seconds` is "nothing
            // has crawled for twenty minutes", which is the thing worth alerting on and which a
            // lease-state boolean cannot express at all.
            //
            // -1 rather than 0 before the first cycle, because 0 is a moment — the first second of
            // 1970 — and a dashboard subtracting it would report an age of fifty-six years rather
            // than showing a gap.
            text.Gauge(
                "mui_crawl_last_cycle_timestamp_seconds",
                "Unix time when this process last completed a crawl cycle, or -1 if it never has. "
                + "Subtract from now() for the age; a replica without the crawl lease never moves it.",
                _lastCycleAt?.ToUnixTimeSeconds() ?? -1);
        }
    }
}
