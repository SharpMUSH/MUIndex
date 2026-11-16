using MUI.Crawl;
using MUI.Discovery.Tests.Support;

namespace MUI.Discovery.Tests;

/// <summary>
/// Whether <c>INFO</c> and <c>VERSION</c> are still worth typing at an address, and what a session
/// teaches about it (issue #182).
/// </summary>
public class LoginCommandsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 5, 0, 0, TimeSpan.Zero);

    private static readonly LoginCommandMemory Fresh = default;

    /// <summary>What <c>fs.twkang.net:5555</c> says to both, as text.</summary>
    private static readonly ProbeResult NamePrompt = ProbeResults.Answered(
        info: "好吧﹐那麼請重新輸入您的英文名字﹕") with { InfoAsked = true };

    private static readonly ProbeResult Huh = ProbeResults.Answered(
        info: "Huh?  (Type \"help\" for help.)",
        version: "Huh?  (Type \"help\" for help.)") with { InfoAsked = true, VersionAsked = true };

    private static readonly ProbeResult InfoBlock = ProbeResults.Answered(
        info: "### Begin INFO 1\r\nName: Mortal Realms\r\nConnected: 7\r\n### End INFO\r\n")
        with { InfoAsked = true, VersionAsked = true };

    [Test]
    public async Task ThreeSessionsWithNothingReadableRunningSilenceTheAddress()
    {
        var memory = Fresh;

        for (var session = 1; session < LoginCommands.Patience; session++)
        {
            memory = LoginCommands.Learn(memory, session % 2 == 0 ? Huh : NamePrompt, Now);

            await Assert.That(memory.QuietAt).IsNull();
            await Assert.That(LoginCommands.Ask(Listed(memory))).IsTrue();
        }

        memory = LoginCommands.Learn(memory, NamePrompt, Now);

        await Assert.That(memory).IsEqualTo(new LoginCommandMemory(LoginCommands.Patience, Now));
        await Assert.That(LoginCommands.Ask(Listed(memory))).IsFalse();
    }

    [Test]
    public async Task OneAnswerClearsTheRun()
    {
        var memory = new LoginCommandMemory(LoginCommands.Patience - 1, null);

        await Assert.That(LoginCommands.Learn(memory, InfoBlock, Now)).IsEqualTo(Fresh);
    }

    /// <summary>
    /// A quiet address stays quiet: there is no retrial, however long it has been.
    /// </summary>
    [Test]
    public async Task AQuietAddressIsNeverAskedAgain()
    {
        var quiet = new LoginCommandMemory(LoginCommands.Patience, Now.AddYears(-2));

        await Assert.That(LoginCommands.Ask(Listed(quiet))).IsFalse();
    }

    /// <summary>
    /// A submission awaiting §7.8 is the one session asked while quiet. A miss there keeps the date
    /// the address went quiet; an answer clears the memory.
    /// </summary>
    [Test]
    public async Task AQuietSubmissionsMissKeepsTheDateAndItsAnswerClearsIt()
    {
        var since = Now.AddDays(-40);
        var quiet = new LoginCommandMemory(LoginCommands.Patience, since);

        await Assert.That(LoginCommands.Learn(quiet, NamePrompt, Now)).IsEqualTo(quiet);
        await Assert.That(LoginCommands.Learn(quiet, InfoBlock, Now)).IsEqualTo(Fresh);
    }

    /// <summary>
    /// Rule 5: our silence is not theirs. A session that did not type <c>INFO</c> — because we chose
    /// not to, or because the dial never got that far — leaves the memory exactly as it was, even
    /// though its <c>Info</c> is as empty as a game that answered nothing.
    /// </summary>
    [Test]
    public async Task ASessionThatNeverAskedOrNeverConnectedTeachesNothing()
    {
        var memory = new LoginCommandMemory(1, null);

        await Assert.That(LoginCommands.Learn(memory, ProbeResults.Answered(), Now)).IsEqualTo(memory);
        await Assert.That(LoginCommands.Learn(memory, ProbeResults.Failed(), Now)).IsEqualTo(memory);

        var quiet = new LoginCommandMemory(LoginCommands.Patience, Now.AddDays(-3));

        await Assert.That(LoginCommands.Learn(quiet, ProbeResults.Answered(), Now)).IsEqualTo(quiet);
    }

    /// <summary>
    /// A submission still awaiting §7.8 is asked whatever its history says: for a game that
    /// negotiates nothing, these replies may be the only evidence that it is a game at all.
    /// </summary>
    [Test]
    public async Task ASubmissionAwaitingCorroborationIsAlwaysAsked()
    {
        var quiet = new LoginCommandMemory(LoginCommands.Patience, Now);

        await Assert.That(LoginCommands.Ask(Listed(quiet) with { AwaitingCorroboration = true })).IsTrue();
    }

    [Test]
    public async Task AnAddressWithNoHistoryIsAsked()
    {
        await Assert.That(LoginCommands.Ask(Listed(Fresh))).IsTrue();
    }

    private static CrawlTarget Listed(LoginCommandMemory memory) => new()
    {
        Id = Guid.NewGuid(),
        Host = "fs.twkang.net",
        Port = 5555,
        NextProbeAt = Now,
        FirstSeenAt = Now.AddYears(-1),
        AwaitingCorroboration = false,
        LoginCommands = memory,
    };
}
