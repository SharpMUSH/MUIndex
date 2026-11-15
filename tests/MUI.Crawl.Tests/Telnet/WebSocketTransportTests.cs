using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.Logging.Abstractions;

using TelnetNegotiationCore.Builders;
using TelnetNegotiationCore.Interpreters;
using TelnetNegotiationCore.Models;
using TelnetNegotiationCore.Protocols;
using TelnetNegotiationCore.WebSockets;

namespace MUI.Crawl.Tests;

/// <summary>
/// The probe against a game reached through a WebSocket speaking telnet (<c>telnet.mudstandards.org</c>).
/// </summary>
/// <remarks>
/// The whole telnet stream rides in BINARY frames, so everything above the transport — negotiation,
/// MSSP, the connect screen — is the session a socket carries. What is under test here is the
/// transport and the dial: the subprotocol offer, the scope guard's addresses, and what a refused
/// upgrade becomes.
/// </remarks>
public class WebSocketTransportTests
{
    private static ProbeOptions Fast() => new()
    {
        QuietPeriod = TimeSpan.FromMilliseconds(120),
        SilenceGrace = TimeSpan.FromMilliseconds(300),
        MaxPhase = TimeSpan.FromSeconds(3),
        BannerPatience = TimeSpan.FromMilliseconds(300),
        WhoGrace = TimeSpan.FromMilliseconds(700),
        PollInterval = TimeSpan.FromMilliseconds(15),
        Timeout = TimeSpan.FromSeconds(20),
        MsspSettleGrace = TimeSpan.FromMilliseconds(400),
        PromptHold = TimeSpan.FromMilliseconds(120),
    };

    [Test]
    public async Task AGameBehindAWebSocketIsReadOverTelnet()
    {
        await using var game = new WebSocketGame { Banner = "Welcome to Nowhere\r\nA quiet little place.\r\n" };

        var result = await new TelnetProbe(Fast()).ProbeAsync(game.Target);

        await Assert.That(game.Fault?.ToString() ?? "none").IsEqualTo("none");
        await Assert.That(result.Outcome).IsEqualTo(ProbeOutcome.Answered);
        await Assert.That(result.Transport).IsEqualTo(ProbeTransport.WebSocket);
        await Assert.That(result.Banner).Contains("A quiet little place.");
        await Assert.That(game.RequestedPath).IsEqualTo("/ws");
        await Assert.That(game.Offered).IsEquivalentTo(
            [MudWebSocketSubprotocols.Telnet, MudWebSocketSubprotocols.TelnetAlias]);
    }

    /// <summary>
    /// Telnet negotiation survives the framing: the report arrives over option 70, inside the socket.
    /// </summary>
    [Test]
    public async Task MsspIsNegotiatedInsideTheWebSocket()
    {
        await using var game = new WebSocketGame { Banner = "Hello.\r\n", Mssp = true };

        var result = await new TelnetProbe(Fast()).ProbeAsync(game.Target);

        await Assert.That(game.Fault?.ToString() ?? "none").IsEqualTo("none");
        await Assert.That(result.MsspOutcome).IsEqualTo(MsspOutcome.Received);
        await Assert.That(result.MsspTransport).IsEqualTo(MsspTransport.TelnetOption70);
        await Assert.That(result.MsspField("NAME")).IsEqualTo("Socket Realm");
        await Assert.That(result.OfferedOptions).Contains("MSSP");
    }

    /// <summary>
    /// A bridge that accepts the upgrade without naming a subprotocol is taken as telnet.
    /// </summary>
    /// <remarks>
    /// Most wss-to-telnet bridges in the field name none, which is why TelnetNegotiationCore reads
    /// a bare socket as telnet. Refusing them would drop exactly the games this transport is for.
    /// </remarks>
    [Test]
    public async Task ABridgeThatNamesNoSubprotocolIsTakenAsTelnet()
    {
        await using var game = new WebSocketGame { Banner = "Bridged.\r\n", Chosen = null };

        var result = await new TelnetProbe(Fast()).ProbeAsync(game.Target);

        await Assert.That(game.Fault?.ToString() ?? "none").IsEqualTo("none");
        await Assert.That(result.Outcome).IsEqualTo(ProbeOutcome.Answered);
        await Assert.That(result.Banner).Contains("Bridged.");
    }

    /// <summary>
    /// The dial goes to the addresses the scope guard ruled on, and never resolves the name.
    /// </summary>
    /// <remarks>
    /// <c>.invalid</c> cannot resolve (RFC 6761), so a probe that looked the name up would fail with
    /// a DNS error. The guard's ruling is on addresses (spec §7.2), and a second lookup at dial time
    /// is both a hole in it and a second chance to fail.
    /// </remarks>
    [Test]
    public async Task TheVettedAddressesAreDialledRatherThanTheName()
    {
        await using var game = new WebSocketGame { Banner = "Pinned.\r\n" };

        var target = ProbeTarget.ForWebSocket(new Uri($"ws://game.invalid:{game.Port}/ws")) with
        {
            Addresses = [IPAddress.Loopback],
        };

        var result = await new TelnetProbe(Fast()).ProbeAsync(target);

        await Assert.That(result.Failure?.ToString() ?? "none").IsEqualTo("none");
        await Assert.That(result.Outcome).IsEqualTo(ProbeOutcome.Answered);
        await Assert.That(result.Banner).Contains("Pinned.");
        await Assert.That(game.HostHeader).IsEqualTo($"game.invalid:{game.Port}");
    }

    /// <summary>
    /// A web server that will not upgrade is a failed dial, not a game that answered.
    /// </summary>
    /// <remarks>
    /// The HTTP response is not a connect screen, and recording it as one would publish a web
    /// server's error page as the game's (rule 5).
    /// </remarks>
    [Test]
    public async Task AnUpgradeTheServerRefusesIsAFailedDial()
    {
        await using var game = new WebSocketGame { RefuseUpgrade = true };

        var result = await new TelnetProbe(Fast()).ProbeAsync(game.Target);

        await Assert.That(result.Outcome).IsEqualTo(ProbeOutcome.Failed);
        await Assert.That(result.Banner).IsNull();
        await Assert.That(result.Transport).IsEqualTo(ProbeTransport.WebSocket);
    }

    /// <summary>
    /// A check types nothing: the measurement already asked what there was to ask.
    /// </summary>
    [Test]
    public async Task ACheckReadsTheScreenAndTypesNothing()
    {
        await using var game = new WebSocketGame { Banner = "Welcome.\r\nBy what name are you known? " };

        var result = await new TelnetProbe(Fast()).ProbeAsync(game.Target with { AsksNothing = true });

        await Assert.That(game.Fault?.ToString() ?? "none").IsEqualTo("none");
        await Assert.That(result.Outcome).IsEqualTo(ProbeOutcome.Answered);
        await Assert.That(result.Banner).Contains("Welcome.");
        await Assert.That(result.Who.Attempted).IsFalse();
        await Assert.That(game.Received).IsEmpty();
    }

    /// <summary>The control for the test above: the same screen, measured, is asked WHO.</summary>
    [Test]
    public async Task AMeasurementOfTheSameScreenAsksWho()
    {
        await using var game = new WebSocketGame { Banner = "Welcome.\r\nBy what name are you known? " };

        await new TelnetProbe(Fast()).ProbeAsync(game.Target);

        await Assert.That(game.Received).Contains(TelnetProbe.WhoCommand);
    }

    [Test]
    public async Task ARefusedConnectIsClassifiedThroughTheWrappers()
    {
        int port;

        using (var listener = new TcpListener(IPAddress.Loopback, 0))
        {
            listener.Start();
            port = ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        var result = await new TelnetProbe(Fast()).ProbeAsync(
            ProbeTarget.ForWebSocket(new Uri($"ws://127.0.0.1:{port}/")));

        await Assert.That(result.Outcome).IsEqualTo(ProbeOutcome.Failed);
        await Assert.That(result.Failure?.Cause).IsEqualTo(DialFailureCause.Refused);
    }

    [Test]
    [Arguments("http://game.example/ws")]
    [Arguments("telnet://game.example:4000")]
    public async Task OnlyWebSocketSchemesMakeAWebSocketTarget(string address)
    {
        await Assert.That(() => ProbeTarget.ForWebSocket(new Uri(address))).Throws<ArgumentException>();
    }

    [Test]
    public async Task AWebSocketTargetTakesItsHostAndPortFromTheUrl()
    {
        var target = ProbeTarget.ForWebSocket(new Uri("wss://game.example/play"));

        await Assert.That(target.Host).IsEqualTo("game.example");
        await Assert.That(target.Port).IsEqualTo(443);
        await Assert.That(target.ToString()).IsEqualTo("wss://game.example/play");
    }

    /// <summary>
    /// A WebSocket endpoint, upgraded by hand so the fixture needs no web server: one HTTP request
    /// in, a 101 out, then TelnetNegotiationCore's own server half on the socket.
    /// </summary>
    private sealed class WebSocketGame : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stopping = new();
        private readonly Task _serving;

        public WebSocketGame()
        {
            _listener.Start();
            _serving = ServeAsync();
        }

        public string Banner { get; init; } = string.Empty;

        /// <summary>The subprotocol the server names in its 101, or null to name none.</summary>
        public string? Chosen { get; init; } = MudWebSocketSubprotocols.Telnet;

        public bool Mssp { get; init; }

        /// <summary>Answer the upgrade with a 404, as a web server with no such endpoint would.</summary>
        public bool RefuseUpgrade { get; init; }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public ProbeTarget Target => ProbeTarget.ForWebSocket(new Uri($"ws://127.0.0.1:{Port}/ws"));

        public string? RequestedPath { get; private set; }

        public string? HostHeader { get; private set; }

        public IReadOnlyList<string> Offered { get; private set; } = [];

        public Exception? Fault { get; private set; }

        /// <summary>Every line the probe sent, in order.</summary>
        public IReadOnlyList<string> Received
        {
            get
            {
                lock (_received)
                {
                    return [.. _received];
                }
            }
        }

        private readonly List<string> _received = [];

        public async ValueTask DisposeAsync()
        {
            await _stopping.CancelAsync();
            _listener.Stop();

            try
            {
                await _serving;
            }
            catch (OperationCanceledException)
            {
            }

            _stopping.Dispose();
        }

        private async Task ServeAsync()
        {
            using var client = await _listener.AcceptTcpClientAsync(_stopping.Token);

            try
            {
                var stream = client.GetStream();
                var request = await ReadRequestAsync(stream);
                var lines = request.Split("\r\n");

                RequestedPath = lines[0].Split(' ')[1];

                var headers = lines.Skip(1)
                    .Where(line => line.Contains(':'))
                    .Select(line => line.Split(':', 2))
                    .ToDictionary(pair => pair[0].Trim(), pair => pair[1].Trim(), StringComparer.OrdinalIgnoreCase);

                HostHeader = headers.GetValueOrDefault("Host");
                Offered = headers.TryGetValue("Sec-WebSocket-Protocol", out var offered)
                    ? [.. offered.Split(',', StringSplitOptions.TrimEntries)]
                    : [];

                if (RefuseUpgrade)
                {
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(
                        "HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"));
                    return;
                }

                var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(
                    headers["Sec-WebSocket-Key"] + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));

                await stream.WriteAsync(Encoding.ASCII.GetBytes(
                    "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n"
                    + $"Sec-WebSocket-Accept: {accept}\r\n"
                    + (Chosen is null ? string.Empty : $"Sec-WebSocket-Protocol: {Chosen}\r\n")
                    + "\r\n"));

                using var socket = WebSocket.CreateFromStream(
                    stream, new WebSocketCreationOptions { IsServer = true, SubProtocol = Chosen });

                var builder = new TelnetInterpreterBuilder()
                    .UseMode(TelnetInterpreter.TelnetMode.Server)
                    .UseLogger(NullLogger.Instance)
                    .OnSubmit((bytes, encoding, _) =>
                    {
                        lock (_received)
                        {
                            _received.Add(encoding.GetString(bytes).Trim());
                        }

                        return ValueTask.CompletedTask;
                    });

                if (Mssp)
                {
                    var mssp = new MSSPProtocol();
                    mssp.SetMSSPConfig(() => new MSSPConfig { Name = "Socket Realm", Players = 3 });
                    builder = builder.AddPlugin(mssp);
                }

                await using var connection = await builder.StartAsync(socket, _stopping.Token);

                // WriteToNetworkAsync rather than SendAsync: the banner carries its own terminators.
                await connection.Interpreter.WriteToNetworkAsync(Encoding.Latin1.GetBytes(Banner));
                await connection.Completion;
            }
            catch (Exception error) when (error
                is OperationCanceledException
                or IOException
                or SocketException
                or WebSocketException
                or ObjectDisposedException)
            {
            }
            catch (Exception error)
            {
                Fault ??= error;
            }
        }

        private async Task<string> ReadRequestAsync(NetworkStream stream)
        {
            var received = new List<byte>();
            var buffer = new byte[1];

            // A byte at a time, so nothing past the blank line is consumed: the frames after it
            // belong to the WebSocket.
            while (!(received.Count >= 4 && received[^4] == '\r' && received[^3] == '\n'
                && received[^2] == '\r' && received[^1] == '\n'))
            {
                if (await stream.ReadAsync(buffer, _stopping.Token) == 0)
                {
                    throw new IOException("The client closed before finishing its request.");
                }

                received.Add(buffer[0]);
            }

            return Encoding.ASCII.GetString([.. received]);
        }
    }
}
