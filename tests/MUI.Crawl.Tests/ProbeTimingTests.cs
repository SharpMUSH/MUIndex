using MUI.Crawl;

namespace MUI.Crawl.Tests;

/// <summary>
/// A probe settles on a quiet period rather than sleeping through a fixed one.
/// </summary>
/// <remarks>
/// Two flat three-second delays made every probe cost six seconds regardless of how fast the game
/// answered — fine for one server, wrong for a fleet.
/// </remarks>
public class ProbeTimingTests
{
    [Test]
    public async Task AGapBetweenLinesIsShorterThanTheWaitForSilence()
    {
        // The two are different facts. A gap means the server has finished; silence from the start
        // of a phase means it has not begun, and a game behind a slow link has not said no just
        // because it has not yet said anything.
        var options = new ProbeOptions();

        await Assert.That(options.QuietPeriod).IsGreaterThan(TimeSpan.Zero);
        await Assert.That(options.QuietPeriod).IsLessThan(options.SilenceGrace);
    }

    [Test]
    public async Task ATypicalProbeCostsFarLessThanTheOldFixedSixSeconds()
    {
        var options = new ProbeOptions();
        var promptCase = options.QuietPeriod + options.QuietPeriod + options.QuietPeriod;

        await Assert.That(promptCase).IsLessThan(TimeSpan.FromSeconds(6));
    }

    [Test]
    public async Task NoSinglePhaseCanOutlastTheWholeProbe()
    {
        // Quiet-period settling has no upper bound of its own — a server that never stops talking
        // never goes quiet — so a phase is capped, and the cap has to leave room for the phases
        // after it rather than swallowing the budget the WHO reply needs.
        var options = new ProbeOptions();

        await Assert.That(options.MaxPhase).IsLessThan(options.Timeout);
        await Assert.That(options.SilenceGrace).IsLessThan(options.MaxPhase);
    }

    [Test]
    public async Task EveryProbeIsStillHardBoundedInTime()
    {
        // The ceiling did not move: the crawler shares a process with the web tier, so an unbounded
        // probe against a black-holed host starves request threads (spec §12).
        var options = new ProbeOptions();

        await Assert.That(options.Timeout).IsGreaterThan(TimeSpan.Zero);
        await Assert.That(options.Timeout).IsLessThanOrEqualTo(TimeSpan.FromMinutes(1));
    }

    [Test]
    public async Task TheSettleLoopLooksOftenEnoughToBeWorthTheGapItWaitsFor()
    {
        var options = new ProbeOptions();

        await Assert.That(options.PollInterval).IsGreaterThan(TimeSpan.Zero);
        await Assert.That(options.PollInterval).IsLessThan(options.QuietPeriod);
    }
}
