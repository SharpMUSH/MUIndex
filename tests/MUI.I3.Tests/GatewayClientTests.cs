using System.Text.Json;

namespace MUI.I3.Tests;

public class GatewayClientTests
{
    private static GatewayOptions OptionsFor(FakeGateway gateway) => new()
    {
        Host = "127.0.0.1",
        Port = gateway.Port,
        ApiKey = "test-key",
        CallTimeout = TimeSpan.FromSeconds(5),
        WhoTimeout = TimeSpan.FromSeconds(2),
    };

    private static Task Authenticate(string line, FakeGateway gateway) =>
        gateway.ReplyAsync(
            FakeGateway.IdOf(line),
            new Dictionary<string, object?> { ["status"] = "authenticated", ["mud_name"] = "MUIndex" });

    [Test]
    public async Task AuthenticatesBeforeAnythingElse()
    {
        await using var gateway = FakeGateway.Start(Authenticate);
        await using var client = new GatewayClient(OptionsFor(gateway));

        await client.ConnectAsync(CancellationToken.None);

        await Assert.That(gateway.Received).Count().IsEqualTo(1);
        await Assert.That(FakeGateway.MethodOf(gateway.Received[0])).IsEqualTo("authenticate");
    }

    [Test]
    public async Task RefusesToProceedWhenTheKeyIsRejected()
    {
        await using var gateway = FakeGateway.Start((line, g) =>
            g.ReplyAsync(FakeGateway.IdOf(line), new Dictionary<string, object?> { ["status"] = "denied" }));
        await using var client = new GatewayClient(OptionsFor(gateway));

        await Assert.That(async () => await client.ConnectAsync(CancellationToken.None))
            .Throws<GatewayException>();
    }

    /// <summary>
    /// The count is the length of the users array, and an empty array is a measured zero rather than
    /// an absence — see <c>I3PresenceChoice</c>, which is where that distinction is acted on.
    /// </summary>
    [Test]
    public async Task AWhoReplyArrivingAsAnEventAnswersTheCall()
    {
        await using var gateway = FakeGateway.Start(async (line, g) =>
        {
            if (FakeGateway.MethodOf(line) == "authenticate")
            {
                await Authenticate(line, g);
                return;
            }

            // Exactly what the gateway does: acknowledge the send, then deliver the answer later as
            // an unsolicited event carrying only the mud's name.
            await g.ReplyAsync(
                FakeGateway.IdOf(line),
                new Dictionary<string, object?> { ["status"] = "requested", ["mud_name"] = "Nightfall" });

            await g.SendAsync(new Dictionary<string, object?>
            {
                ["jsonrpc"] = "2.0",
                ["method"] = "who_reply",
                ["params"] = new Dictionary<string, object?>
                {
                    ["from_mud"] = "Nightfall",
                    ["users"] = new[]
                    {
                        new Dictionary<string, object?> { ["name"] = "Eriol", ["idle"] = 0 },
                        new Dictionary<string, object?> { ["name"] = "Thror", ["idle"] = 12 },
                    },
                },
            });
        });

        await using var client = new GatewayClient(OptionsFor(gateway));
        await client.ConnectAsync(CancellationToken.None);

        var reply = await client.WhoAsync("Nightfall", CancellationToken.None);

        await Assert.That(reply).IsNotNull();
        await Assert.That(reply!.FromMud).IsEqualTo("Nightfall");
        await Assert.That(reply.Users).Count().IsEqualTo(2);
    }

    /// <summary>
    /// A reply for somebody else must not resolve our question. The wire carries no request id, only
    /// <c>from_mud</c>, so mis-keying this would silently attribute one game's players to another.
    /// </summary>
    [Test]
    public async Task AReplyFromADifferentMudDoesNotAnswerOurs()
    {
        await using var gateway = FakeGateway.Start(async (line, g) =>
        {
            if (FakeGateway.MethodOf(line) == "authenticate")
            {
                await Authenticate(line, g);
                return;
            }

            await g.ReplyAsync(FakeGateway.IdOf(line),
                new Dictionary<string, object?> { ["status"] = "requested" });

            await g.SendAsync(new Dictionary<string, object?>
            {
                ["jsonrpc"] = "2.0",
                ["method"] = "who_reply",
                ["params"] = new Dictionary<string, object?>
                {
                    ["from_mud"] = "SomeOtherMud",
                    ["users"] = new[] { new Dictionary<string, object?> { ["name"] = "Nobody" } },
                },
            });
        });

        await using var client = new GatewayClient(OptionsFor(gateway));
        await client.ConnectAsync(CancellationToken.None);

        var reply = await client.WhoAsync("Nightfall", CancellationToken.None);

        await Assert.That(reply).IsNull();
    }

    /// <summary>Silence times out and yields null, which the caller records as unmeasurable.</summary>
    [Test]
    public async Task SilenceFromTheMudIsNotAnEmptyGame()
    {
        await using var gateway = FakeGateway.Start(async (line, g) =>
        {
            if (FakeGateway.MethodOf(line) == "authenticate")
            {
                await Authenticate(line, g);
                return;
            }

            await g.ReplyAsync(FakeGateway.IdOf(line),
                new Dictionary<string, object?> { ["status"] = "requested" });
        });

        await using var client = new GatewayClient(OptionsFor(gateway));
        await client.ConnectAsync(CancellationToken.None);

        var reply = await client.WhoAsync("Silent", CancellationToken.None);

        await Assert.That(reply).IsNull();
    }

    /// <summary>An empty array is an answer: the mud said nobody is on.</summary>
    [Test]
    public async Task AnEmptyUserListIsStillAnAnswer()
    {
        await using var gateway = FakeGateway.Start(async (line, g) =>
        {
            if (FakeGateway.MethodOf(line) == "authenticate")
            {
                await Authenticate(line, g);
                return;
            }

            await g.ReplyAsync(FakeGateway.IdOf(line),
                new Dictionary<string, object?> { ["status"] = "requested" });

            await g.SendAsync(new Dictionary<string, object?>
            {
                ["jsonrpc"] = "2.0",
                ["method"] = "who_reply",
                ["params"] = new Dictionary<string, object?>
                {
                    ["from_mud"] = "The Zone",
                    ["users"] = Array.Empty<object>(),
                },
            });
        });

        await using var client = new GatewayClient(OptionsFor(gateway));
        await client.ConnectAsync(CancellationToken.None);

        var reply = await client.WhoAsync("The Zone", CancellationToken.None);

        await Assert.That(reply).IsNotNull();
        await Assert.That(reply!.Users).IsEmpty();
    }

    /// <summary>
    /// The gateway has spelled the mudlist both ways across releases — a bare object, and an envelope
    /// with the muds under a key. Both parse, because pinning a beta API's shape is how a working
    /// integration breaks on somebody else's refactor.
    /// </summary>
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task TheMudlistParsesWithOrWithoutItsEnvelope(bool wrapped)
    {
        var muds = new Dictionary<string, object?>
        {
            ["Nightfall"] = new Dictionary<string, object?>
            {
                ["address"] = "82.153.225.173",
                ["player_port"] = 4242,
                ["status"] = "up",
                ["services"] = new Dictionary<string, object?> { ["who"] = 1, ["tell"] = 1 },
            },
            ["Epitaph"] = new Dictionary<string, object?>
            {
                ["address"] = "135.181.197.219",
                ["player_port"] = 0,
                ["status"] = "up",
                ["services"] = new Dictionary<string, object?> { ["who"] = 1 },
            },
        };

        await using var gateway = FakeGateway.Start(async (line, g) =>
        {
            if (FakeGateway.MethodOf(line) == "authenticate")
            {
                await Authenticate(line, g);
                return;
            }

            await g.ReplyAsync(
                FakeGateway.IdOf(line),
                wrapped ? new Dictionary<string, object?> { ["muds"] = muds } : muds);
        });

        await using var client = new GatewayClient(OptionsFor(gateway));
        await client.ConnectAsync(CancellationToken.None);

        var list = await client.MudlistAsync(CancellationToken.None);

        await Assert.That(list).Count().IsEqualTo(2);

        var nightfall = list.Single(m => m.Name == "Nightfall");
        await Assert.That(nightfall.Address).IsEqualTo("82.153.225.173");
        await Assert.That(nightfall.PlayerPort).IsEqualTo(4242);
        await Assert.That(nightfall.IsUp).IsTrue();
        await Assert.That(nightfall.Answers("who")).IsTrue();
        await Assert.That(nightfall.Answers("locate")).IsFalse();

        // Zero is a published fact, not a missing value: the spec permits it for a mud that is not
        // connectable, and MUIndex publishes it about itself.
        await Assert.That(list.Single(m => m.Name == "Epitaph").PlayerPort).IsEqualTo(0);
    }

    /// <summary>
    /// The gateway drops zero-valued service flags rather than sending them as false, so an absent key
    /// and a disabled service are the same thing on the wire.
    /// </summary>
    [Test]
    public async Task AnAbsentServiceKeyIsNotAdvertised()
    {
        await using var gateway = FakeGateway.Start(async (line, g) =>
        {
            if (FakeGateway.MethodOf(line) == "authenticate")
            {
                await Authenticate(line, g);
                return;
            }

            await g.ReplyAsync(FakeGateway.IdOf(line), new Dictionary<string, object?>
            {
                ["Quiet"] = new Dictionary<string, object?>
                {
                    ["address"] = "203.0.113.9",
                    ["player_port"] = 4000,
                    ["status"] = "up",
                    ["services"] = new Dictionary<string, object?> { ["tell"] = 1 },
                },
            });
        });

        await using var client = new GatewayClient(OptionsFor(gateway));
        await client.ConnectAsync(CancellationToken.None);

        var mud = (await client.MudlistAsync(CancellationToken.None)).Single();

        await Assert.That(mud.Answers("who")).IsFalse();
    }

    /// <summary>A JSON-RPC error surfaces as an exception rather than an empty result.</summary>
    [Test]
    public async Task AnErrorResponseIsRaisedRatherThanReadAsEmptiness()
    {
        await using var gateway = FakeGateway.Start(async (line, g) =>
        {
            if (FakeGateway.MethodOf(line) == "authenticate")
            {
                await Authenticate(line, g);
                return;
            }

            await g.SendAsync(new Dictionary<string, object?>
            {
                ["jsonrpc"] = "2.0",
                ["id"] = FakeGateway.IdOf(line),
                ["error"] = new Dictionary<string, object?> { ["code"] = -32601, ["message"] = "no such method" },
            });
        });

        await using var client = new GatewayClient(OptionsFor(gateway));
        await client.ConnectAsync(CancellationToken.None);

        await Assert.That(async () => await client.MudlistAsync(CancellationToken.None))
            .Throws<GatewayException>();
    }

    /// <summary>
    /// A line the client cannot parse must not stop it reading the ones after it — the gateway is a
    /// beta whose event vocabulary is still moving, and one unknown shape is not a reason to go deaf.
    /// </summary>
    [Test]
    public async Task AnUnreadableLineDoesNotStopThePump()
    {
        await using var gateway = FakeGateway.Start(async (line, g) =>
        {
            if (FakeGateway.MethodOf(line) == "authenticate")
            {
                await Authenticate(line, g);
                return;
            }

            await g.SendAsync("this is not an object");
            await g.ReplyAsync(FakeGateway.IdOf(line), new Dictionary<string, object?>());
        });

        await using var client = new GatewayClient(OptionsFor(gateway));
        await client.ConnectAsync(CancellationToken.None);

        var list = await client.MudlistAsync(CancellationToken.None);

        await Assert.That(list).IsEmpty();
    }

    [Test]
    public async Task EveryRequestIsOneLine()
    {
        await using var gateway = FakeGateway.Start(async (line, g) =>
        {
            if (FakeGateway.MethodOf(line) == "authenticate")
            {
                await Authenticate(line, g);
                return;
            }

            await g.ReplyAsync(FakeGateway.IdOf(line), new Dictionary<string, object?>());
        });

        await using var client = new GatewayClient(OptionsFor(gateway));
        await client.ConnectAsync(CancellationToken.None);
        await client.MudlistAsync(CancellationToken.None);

        await Assert.That(gateway.Received).Count().IsEqualTo(2);
        foreach (var line in gateway.Received)
        {
            // Parses whole: no request was split across lines and none carried a stray newline.
            await Assert.That(() => JsonDocument.Parse(line)).ThrowsNothing();
        }
    }
}
