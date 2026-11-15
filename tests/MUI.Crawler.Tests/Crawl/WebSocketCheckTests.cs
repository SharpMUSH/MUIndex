using MUI.Catalog;
using MUI.Crawl;
using MUI.Crawler.Tests.Support;
using MUI.Discovery;

namespace MUI.Crawler.Tests;

/// <summary>What <see cref="WebSocketCheck"/> reads out of a report, and what a check's answer means.</summary>
public class WebSocketCheckTests
{
    [Test]
    [Arguments("wss://game.example/ws", "wss://game.example/ws")]
    [Arguments("ws://game.example:4002", "ws://game.example:4002/")]
    [Arguments("https://game.example/play, wss://game.example/ws", "wss://game.example/ws")]
    [Arguments(" wss://a.example/ws , wss://b.example/ws ", "wss://a.example/ws")]
    public async Task TheFirstWebSocketUrlInTheReportIsChecked(string declared, string expected)
    {
        var result = Probes.Answered(mssp: Probes.Mssp(("WEBSOCKET", declared)));

        await Assert.That(WebSocketCheck.Declared(result)?.ToString()).IsEqualTo(expected);
    }

    [Test]
    [Arguments("1")]
    [Arguments("game.example:4002")]
    [Arguments("https://game.example/play")]
    public async Task AValueThatNamesNoWebSocketUrlIsNotDialled(string declared)
    {
        var result = Probes.Answered(mssp: Probes.Mssp(("WEBSOCKET", declared)));

        await Assert.That(WebSocketCheck.Declared(result)).IsNull();
    }

    /// <summary>MSSP's own rule: a repeated variable's last value is the one that stands.</summary>
    [Test]
    public async Task TheLastValueStands()
    {
        var result = Probes.Answered(mssp: Probes.Mssp(
            ("WEBSOCKET", "wss://old.example/ws"), ("WEBSOCKET", "wss://new.example/ws")));

        await Assert.That(WebSocketCheck.Declared(result)?.Host).IsEqualTo("new.example");
    }

    [Test]
    public async Task AReportWithoutTheVariableNamesNothing()
    {
        await Assert.That(WebSocketCheck.Declared(Probes.Answered(mssp: Probes.Mssp(("NAME", "X"))))).IsNull();
        await Assert.That(WebSocketCheck.Declared(Probes.Answered())).IsNull();
    }

    [Test]
    public async Task AnUpgradeIntoSilenceProvesNoGame()
    {
        var silent = Probes.Answered(transport: ProbeTransport.WebSocket);

        await Assert.That(WebSocketCheck.Observation(silent)).IsNull();
    }

    [Test]
    [Arguments(DialFailureCause.Refused, "false")]
    [Arguments(DialFailureCause.Tls, "false")]
    [Arguments(DialFailureCause.Error, "false")]
    [Arguments(DialFailureCause.Timeout, null)]
    [Arguments(DialFailureCause.NoRoute, null)]
    [Arguments(DialFailureCause.Dns, null)]
    public async Task OnlyAnAnswerFromTheFarEndIsMeasuredFalse(DialFailureCause cause, string? expected)
    {
        var observation = WebSocketCheck.Observation(Probes.Failed(cause: cause));

        await Assert.That(observation?.Value).IsEqualTo(expected);

        if (observation is not null)
        {
            await Assert.That(observation.Field).IsEqualTo("capability.websocket.measured");
            await Assert.That(observation.Source).IsEqualTo(FieldSource.Handshake);
        }
    }

    [Test]
    public async Task ATargetNeverCheckedIsDueAndOneCheckedAnHourAgoIsNot()
    {
        var now = Probes.Observed;
        var target = new CrawlTarget
        {
            Id = Guid.NewGuid(),
            Host = "game.example",
            Port = 4000,
            NextProbeAt = now,
            FirstSeenAt = now,
        };

        await Assert.That(WebSocketCheck.Due(target, now, TimeSpan.FromDays(1))).IsTrue();
        await Assert.That(WebSocketCheck.Due(target with { WebSocketCheckedAt = now.AddHours(-1) }, now, TimeSpan.FromDays(1))).IsFalse();
        await Assert.That(WebSocketCheck.Due(target with { WebSocketCheckedAt = now.AddDays(-2) }, now, TimeSpan.FromDays(1))).IsTrue();
    }
}
