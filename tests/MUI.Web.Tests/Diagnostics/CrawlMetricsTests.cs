using MUI.Crawler;
using MUI.Web.Diagnostics;

namespace MUI.Web.Tests.Diagnostics;

/// <summary>
/// What the crawl loop has done, as counters.
/// </summary>
/// <remarks>
/// Recorded from the <see cref="CycleReport"/> the loop already returns rather than from new
/// instrumentation threaded through it: the report is the cycle's own account of itself, and a
/// second set of numbers counted somewhere else could disagree with it.
/// </remarks>
public class CrawlMetricsTests
{
    /// <summary>A report with everything at zero, so each case names only the figures it is about.</summary>
    private static CycleReport Cycle(
        int considered = 0,
        int probed = 0,
        int answered = 0,
        int failed = 0,
        int refused = 0,
        int optedOut = 0,
        int errored = 0,
        int counted = 0,
        int unmeasurable = 0) =>
        new(considered, probed, answered, failed, refused, optedOut, errored,
            Listed: 0, ReviewsOpened: 0, counted, unmeasurable, Transitions: 0, ReferralsAdded: 0);

    private static string Scrape(CrawlMetrics metrics)
    {
        var text = new PrometheusText();
        metrics.WriteTo(text);
        return text.ToString();
    }

    /// <summary>
    /// Zero before anything has run — and present rather than absent. A counter that appears only
    /// once it is non-zero gives <c>rate()</c> nothing to work from over the window where it started,
    /// which is the window an incident is usually in.
    /// </summary>
    [Test]
    public async Task TheCountersExistBeforeTheFirstCycle()
    {
        var scrape = Scrape(new CrawlMetrics(TimeProvider.System));

        await Assert.That(RuntimeMetricsTests.Read(scrape, "mui_crawl_cycles_total")).IsEqualTo(0);
        await Assert.That(RuntimeMetricsTests.Read(scrape, "mui_crawl_targets_total")).IsEqualTo(0);
    }

    [Test]
    public async Task ACycleIsCountedWithTheTargetsItConsidered()
    {
        var metrics = new CrawlMetrics(TimeProvider.System);

        metrics.Record(Cycle(considered: 12, probed: 12, answered: 9, failed: 2, errored: 1));
        metrics.Record(Cycle(considered: 8, probed: 8, answered: 8));

        var scrape = Scrape(metrics);

        await Assert.That(RuntimeMetricsTests.Read(scrape, "mui_crawl_cycles_total")).IsEqualTo(2);
        await Assert.That(RuntimeMetricsTests.Read(scrape, "mui_crawl_targets_total")).IsEqualTo(20);
    }

    /// <summary>
    /// Outcomes are labelled rather than being three differently-named counters, so a graph can sum
    /// them and a fourth outcome would not need a fourth metric name.
    /// </summary>
    [Test]
    public async Task OutcomesAreCountedUnderTheirOwnLabel()
    {
        var metrics = new CrawlMetrics(TimeProvider.System);

        metrics.Record(Cycle(considered: 12, probed: 12, answered: 9, failed: 2, errored: 1));

        var scrape = Scrape(metrics);

        await Assert.That(RuntimeMetricsTests.Read(scrape, "mui_crawl_outcomes_total", ("outcome", "answered")))
            .IsEqualTo(9);
        await Assert.That(RuntimeMetricsTests.Read(scrape, "mui_crawl_outcomes_total", ("outcome", "failed")))
            .IsEqualTo(2);
        await Assert.That(RuntimeMetricsTests.Read(scrape, "mui_crawl_outcomes_total", ("outcome", "errored")))
            .IsEqualTo(1);
    }

    /// <summary>
    /// A refusal is ours, not theirs, and is counted apart from every measured outcome (rule 5). A
    /// dashboard that folded it into "failed" would be reading our own policy as the far end's
    /// downtime — the same mistake in a graph that the catalogue refuses to make in the database.
    /// The two reasons stay apart for the reason <see cref="CycleReport"/> keeps them apart.
    /// </summary>
    [Test]
    public async Task ARefusalIsCountedApartFromAMeasuredFailure()
    {
        var metrics = new CrawlMetrics(TimeProvider.System);

        metrics.Record(Cycle(considered: 5, probed: 3, answered: 3, refused: 1, optedOut: 1));

        var scrape = Scrape(metrics);

        await Assert.That(RuntimeMetricsTests.Read(scrape, "mui_crawl_refusals_total", ("reason", "out_of_scope")))
            .IsEqualTo(1);
        await Assert.That(RuntimeMetricsTests.Read(scrape, "mui_crawl_refusals_total", ("reason", "opted_out")))
            .IsEqualTo(1);
        await Assert.That(RuntimeMetricsTests.Read(scrape, "mui_crawl_outcomes_total", ("outcome", "failed")))
            .IsEqualTo(0);
    }

    /// <summary>
    /// Counted and uncountable are two readings, not one reading and its absence — the same
    /// distinction the heatmap's three states exist for. A single "counted" figure would make a game
    /// whose roster we cannot parse indistinguishable from one nobody was playing.
    /// </summary>
    [Test]
    public async Task AnUncountableReadingIsCountedApartFromACount()
    {
        var metrics = new CrawlMetrics(TimeProvider.System);

        metrics.Record(Cycle(considered: 10, probed: 10, answered: 10, counted: 6, unmeasurable: 4));

        var scrape = Scrape(metrics);

        await Assert.That(RuntimeMetricsTests.Read(scrape, "mui_crawl_presence_total", ("reading", "counted")))
            .IsEqualTo(6);
        await Assert.That(RuntimeMetricsTests.Read(scrape, "mui_crawl_presence_total", ("reading", "unmeasurable")))
            .IsEqualTo(4);
    }

    /// <summary>
    /// When this replica last finished a cycle, as a timestamp rather than a flag.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This was <c>mui_crawl_lease_held</c>, set to 1 by the first cycle and never cleared. The name
    /// and the help text were present tense and the value was not: <c>LeasedBackgroundService</c> can
    /// find its lease connection gone, release it, and go back to asking, and the gauge would have
    /// gone on reporting that this replica held a lease it had given up — a claim about the present
    /// read off a fact about the past, which is the shape of mistake this codebase exists to refuse.
    /// </para>
    /// <para>
    /// A timestamp instead of tracking the lease through the service, because it is honest about
    /// exactly what was observed — a cycle finished, at this moment — and because it answers the
    /// operational question better: <c>time() - mui_crawl_last_cycle_timestamp_seconds</c> is
    /// "nothing has crawled for twenty minutes", which is what an alert should fire on, and a
    /// lease-state flag cannot say that at all.
    /// </para>
    /// </remarks>
    [Test]
    public async Task WhenThisReplicaLastCompletedACycleIsReported()
    {
        var clock = new FixedClock(DateTimeOffset.Parse("2026-09-03T18:00:00Z"));
        var metrics = new CrawlMetrics(clock);

        // Nothing has crawled, so there is no moment to report — and 0 would be a moment, in 1970.
        await Assert.That(RuntimeMetricsTests.Read(Scrape(metrics), "mui_crawl_last_cycle_timestamp_seconds"))
            .IsEqualTo(-1);

        metrics.Record(Cycle(considered: 1, probed: 1, answered: 1));

        await Assert.That(RuntimeMetricsTests.Read(Scrape(metrics), "mui_crawl_last_cycle_timestamp_seconds"))
            .IsEqualTo(DateTimeOffset.Parse("2026-09-03T18:00:00Z").ToUnixTimeSeconds());
    }

    /// <summary>
    /// And it moves with each cycle, so "when did this last crawl" cannot silently freeze at the
    /// first one.
    /// </summary>
    [Test]
    public async Task TheTimestampFollowsTheLatestCycle()
    {
        var clock = new FixedClock(DateTimeOffset.Parse("2026-09-03T18:00:00Z"));
        var metrics = new CrawlMetrics(clock);

        metrics.Record(Cycle(considered: 1, probed: 1, answered: 1));
        clock.Advance(TimeSpan.FromMinutes(30));
        metrics.Record(Cycle(considered: 2, probed: 2, answered: 2));

        await Assert.That(RuntimeMetricsTests.Read(Scrape(metrics), "mui_crawl_last_cycle_timestamp_seconds"))
            .IsEqualTo(DateTimeOffset.Parse("2026-09-03T18:30:00Z").ToUnixTimeSeconds());
    }
}
