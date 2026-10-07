using MUI.Crawl;
using MUI.Discovery.Tests.Support;

namespace MUI.Discovery.Tests;

/// <summary>
/// What a probe teaches about whether an address answers a pre-login <c>WHO</c>.
/// </summary>
public class WhoAnswersTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 20, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Earlier = Now.AddDays(-3);

    [Test]
    public async Task ACountedWhoIsRememberedAndDatedOnce()
    {
        var counted = ProbeResults.Answered(who: new WhoReading(WhoConfidence.Count, 412));

        await Assert.That(WhoAnswers.Learn(null, counted, Now)).IsEqualTo(Now);

        // Already known: the row already says it, so nothing is re-dated.
        await Assert.That(WhoAnswers.Learn(Earlier, counted, Now)).IsEqualTo(Earlier);
    }

    [Test]
    public async Task TheLoginPromptTakingWhoForANameForgetsIt()
    {
        var ate = ProbeResults.Answered(who: WhoReading.LoginPrompt);

        await Assert.That(WhoAnswers.Learn(Earlier, ate, Now)).IsNull();
    }

    [Test]
    public async Task AnUnreadableWhoIsOurParserAndChangesNothing()
    {
        var unreadable = ProbeResults.Answered(who: WhoReading.Unreadable);

        await Assert.That(WhoAnswers.Learn(Earlier, unreadable, Now)).IsEqualTo(Earlier);
        await Assert.That(WhoAnswers.Learn(null, unreadable, Now)).IsNull();
    }

    [Test]
    public async Task ASessionThatNeverAskedOrNeverConnectedTeachesNothing()
    {
        await Assert.That(WhoAnswers.Learn(Earlier, ProbeResults.Answered(), Now)).IsEqualTo(Earlier);
        await Assert.That(WhoAnswers.Learn(Earlier, ProbeResults.Failed(), Now)).IsEqualTo(Earlier);
        await Assert.That(WhoAnswers.Learn(null, ProbeResults.Failed(), Now)).IsNull();
    }
}
