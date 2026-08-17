using MUI.Web.Localization;

using MUI.Catalog;
using MUI.Web.Components;

namespace MUI.Web.Tests;

/// <summary>
/// The strip that says whether the instrument behind every other number is running.
/// </summary>
/// <remarks>
/// Most of these are about what it refuses to say. The strip reads one column of one table and is
/// on the most-served page here, so the temptation to turn an old timestamp into a diagnosis is
/// permanent and has to be pinned rather than remembered.
/// </remarks>
public class CrawlerStripTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);

    private static CrawlCycleRecord Cycle(int considered, int answered, int failed) =>
        new(Now.AddSeconds(-20), Now.AddSeconds(-8), considered, considered, answered, failed,
            0, 0, 0, 0, 0, answered, 0, 0, 0);

    private static CrawlerPulse Pulse(TimeSpan since, CrawlCycleRecord? cycle = null) =>
        new(Now - since, Now.AddMinutes(3), 4, 710, cycle);

    /// <summary>A recent probe is the whole of the evidence that the loop is alive.</summary>
    [Test]
    public async Task ARecentProbeReadsAsWorking()
    {
        var pulse = Pulse(TimeSpan.FromSeconds(40));

        await Assert.That(pulse.State(Now)).IsEqualTo(CrawlState.Working);
        await Assert.That(CrawlerCopy.State(Locales.SourceTag, pulse, Now))
            .IsEqualTo("crawler live · last probe " + Messages.For(Locales.SourceTag, "age.ago.now"));
    }

    /// <summary>
    /// <b>Silence is quiet and is never given a cause.</b>
    /// </summary>
    /// <remarks>
    /// The one that matters. A stale pulse is consistent with a crashed host, a lease held by a
    /// replica this process cannot see, a batch of slow servers and an empty registry — and on the
    /// day this was written three of those were live hypotheses at once. Rule 5 forbids publishing
    /// our own limits as facts about somebody else's game; pointed inward it forbids publishing a
    /// guess as a fact about our own instrument.
    /// </remarks>
    [Test]
    public async Task AStalePulseIsQuietAndNamesNoCause()
    {
        var pulse = Pulse(TimeSpan.FromHours(4));
        var copy = CrawlerCopy.State(Locales.SourceTag, pulse, Now);

        await Assert.That(pulse.State(Now)).IsEqualTo(CrawlState.Quiet);
        await Assert.That(copy).IsEqualTo(
            "crawler quiet · last probe " + Relative.Ago(Locales.SourceTag, TimeSpan.FromHours(4)));

        foreach (var diagnosis in new[] { "stopped", "down", "crashed", "stalled", "failed", "error", "offline" })
        {
            await Assert.That(copy.ToLowerInvariant()).DoesNotContain(diagnosis);
        }
    }

    /// <summary>
    /// "Uptime" is the wrong word here as everywhere else, and so is any claim about a game.
    /// </summary>
    [Test]
    public async Task TheStripNeverSaysUptimeAndNeverDescribesAGame()
    {
        var rendered = string.Join(
            "\n",
            CrawlerCopy.State(Locales.SourceTag, Pulse(TimeSpan.FromSeconds(10)), Now),
            CrawlerCopy.State(Locales.SourceTag, Pulse(TimeSpan.FromDays(2)), Now),
            CrawlerCopy.LastCycle(Pulse(TimeSpan.Zero, Cycle(8, 6, 2))) ?? string.Empty,
            CrawlerCopy.Registry(Pulse(TimeSpan.Zero)));

        foreach (var word in new[] { "uptime", "unreachable", "reachable" })
        {
            await Assert.That(rendered.ToLowerInvariant()).DoesNotContain(word);
        }
    }

    /// <summary>Before the first probe there is nothing to say, and it says nothing.</summary>
    /// <remarks>
    /// Which is also the demo path: <c>NoCrawlerPulse</c> answers <c>Unknown</c>, so a fixture
    /// deployment renders no strip rather than a fabricated heartbeat under the demo banner.
    /// </remarks>
    [Test]
    public async Task AnUnmeasuredPulseRendersNothing()
    {
        await Assert.That(CrawlerPulse.Unknown.State(Now)).IsEqualTo(CrawlState.NotYet);
        await Assert.That(CrawlerCopy.LastCycle(CrawlerPulse.Unknown)).IsNull();

        var html = await Render.PageAsync<Components.Pages.Home>([]);
        await Assert.That(html).DoesNotContain("crawler live");
        await Assert.That(html).DoesNotContain("crawler quiet");
    }

    /// <summary>
    /// An empty cycle is a real answer and reads as one.
    /// </summary>
    /// <remarks>
    /// "0 due · 0 answered" reads as a failure and means the opposite — every address in the
    /// registry is up to date. That is the healthiest a crawl gets and it must not look like the
    /// sickest.
    /// </remarks>
    [Test]
    public async Task ACycleWithNothingDueSaysSoRatherThanPrintingZeroes()
    {
        await Assert.That(CrawlerCopy.LastCycle(Pulse(TimeSpan.Zero, Cycle(0, 0, 0))))
            .IsEqualTo("nothing due this cycle");

        await Assert.That(CrawlerCopy.LastCycle(Pulse(TimeSpan.Zero, Cycle(8, 6, 2))))
            .IsEqualTo("8 due · 6 answered · 2 failed");
    }

    /// <summary>
    /// The freshness bound is generous, because a strip that cries wolf is one nobody reads.
    /// </summary>
    [Test]
    public async Task TheFreshnessBoundOutlastsSeveralPollIntervals()
    {
        // DiscoveryOptions.PollInterval is thirty seconds, and a cycle whose batch is all slow
        // servers legitimately lands no probe for a while.
        await Assert.That(CrawlerPulse.Fresh).IsGreaterThanOrEqualTo(TimeSpan.FromMinutes(2));
        await Assert.That(CrawlerPulse.Fresh).IsLessThan(TimeSpan.FromMinutes(15));
    }
}
