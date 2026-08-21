using System.Text.Json;
using MUI.Catalog;
using MUI.Crawler;
using MUI.I3;

namespace MUI.Crawler.Tests;

public class I3PresenceChoiceTests
{
    private static I3Mud Mud(string status, params string[] services) => new()
    {
        Name = "Somewhere",
        Status = status,
        Services = services.ToDictionary(
            s => s,
            _ => JsonDocument.Parse("1").RootElement.Clone()),
    };

    /// <summary>
    /// The whole point of the type. Both of these arrive down one pipe and look alike; one is a game
    /// with nobody on it and the other is a game that told us nothing (spec §5.4).
    /// </summary>
    [Test]
    public async Task AnEmptyUserListIsAMeasuredZeroAndSilenceIsNot()
    {
        var answered = I3PresenceChoice.From(new I3WhoReply { FromMud = "The Zone", Users = [] });
        var silent = I3PresenceChoice.From(null);

        await Assert.That(answered.Count).IsEqualTo(0);
        await Assert.That(answered.Reason).IsNull();
        await Assert.That(answered.Source).IsEqualTo(FieldSource.I3);

        await Assert.That(silent.Count).IsNull();
        await Assert.That(silent.Reason).IsEqualTo(UnmeasurableReason.I3NoReply);
    }

    /// <summary>
    /// An unmeasurable I3 reading must not be filed under <c>who</c>. That would say a game failed to
    /// answer a telnet command nobody sent it — our confusion in their record (§5.5).
    /// </summary>
    [Test]
    public async Task SilenceIsAttributedToThePipeItFailedOn()
    {
        await Assert.That(I3PresenceChoice.From(null).Source).IsEqualTo(FieldSource.I3);
    }

    [Test]
    public async Task TheCountIsTheLengthOfTheUserList()
    {
        var reply = new I3WhoReply
        {
            FromMud = "Nightfall",
            Users =
            [
                new I3User { Name = "Eriol" },
                new I3User { Name = "Jameson", Idle = 300 },
                new I3User { Name = "Karolos", Idle = 900 },
                new I3User { Name = "Lemoey" },
                new I3User { Name = "Thror" },
            ],
        };

        var reading = I3PresenceChoice.From(reply);

        await Assert.That(reading.Count).IsEqualTo(5);
        await Assert.That(reading.Source).IsEqualTo(FieldSource.I3);
    }

    /// <summary>An idle user is still logged in. Idle time is not a filter (spec §5.2).</summary>
    [Test]
    public async Task IdleUsersAreCounted()
    {
        var reply = new I3WhoReply
        {
            FromMud = "Dead Souls Dev",
            Users =
            [
                new I3User { Name = "Cratylus", Idle = 445370 },
                new I3User { Name = "Ninja", Idle = 73823 },
                new I3User { Name = "Joshua", Idle = 424041 },
            ],
        };

        await Assert.That(I3PresenceChoice.From(reply).Count).IsEqualTo(3);
    }

    [Test]
    public async Task AnI3CountIsMeasuredRatherThanDeclared()
    {
        await Assert.That(FieldSources.IsMeasured(FieldSource.I3)).IsTrue();
    }

    [Test]
    [Arguments("up", true)]
    [Arguments("down", false)]
    public async Task ADownMudIsNotAsked(string status, bool expected)
    {
        await Assert.That(I3PresenceChoice.MayAsk(Mud(status, "who"))).IsEqualTo(expected);
    }

    /// <summary>
    /// The services mapping is I3's own opt-in mechanism. A mud that does not list <c>who</c> has
    /// said it will not answer one, and asking anyway is the I3 equivalent of dialling a host that
    /// asked us not to (§11).
    /// </summary>
    [Test]
    public async Task AMudThatDoesNotAdvertiseWhoIsNotAsked()
    {
        await Assert.That(I3PresenceChoice.MayAsk(Mud("up", "tell", "channel"))).IsFalse();
        await Assert.That(I3PresenceChoice.MayAsk(Mud("up"))).IsFalse();
        await Assert.That(I3PresenceChoice.MayAsk(Mud("up", "who"))).IsTrue();
    }
}
