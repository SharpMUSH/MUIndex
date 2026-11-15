using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace MUI.Crawl.Tests.WebSocket;

/// <summary>The handshake against a bridge on loopback that answers as the real ones do.</summary>
public class WebSocketProbeTests
{
    [Test]
    public async Task ABridgeThatRefusesUnknownSubprotocolsUpgradesTheProbe()
    {
        // websockify answers 400 to a subprotocol it does not know; the probe offers none.
        await using var bridge = Bridge.Start(refuseAnyOffer: true);

        var handshake = await new WebSocketProbe().HandshakeAsync(
            new WebSocketAddress("localhost", bridge.Port, false, "/ws/telnet/"),
            [IPAddress.Loopback]);

        await Assert.That(handshake.Answered).IsTrue();
        await Assert.That(bridge.Paths).Contains("/ws/telnet/");
        await Assert.That(bridge.Offers).Contains((string?)null);
    }

    [Test]
    public async Task ARefusedUpgradeSaysItsStatus()
    {
        await using var bridge = Bridge.Start(refuseEverything: true);

        var handshake = await new WebSocketProbe().HandshakeAsync(
            new WebSocketAddress("localhost", bridge.Port, false, "/"),
            [IPAddress.Loopback]);

        await Assert.That(handshake.Answered).IsFalse();
        await Assert.That(handshake.Detail).IsEqualTo("HTTP 400");
    }

    [Test]
    public async Task TheSocketGoesToTheRuledAddressNotToTheName()
    {
        // The name does not resolve; the probe never asks.
        await using var bridge = Bridge.Start();

        var handshake = await new WebSocketProbe().HandshakeAsync(
            new WebSocketAddress("unresolvable.invalid", bridge.Port, false, "/"),
            [IPAddress.Loopback]);

        await Assert.That(handshake.Answered).IsTrue();
    }

    [Test]
    public async Task ASilentServerRunsOutOfBudget()
    {
        using var silent = new TcpListener(IPAddress.Loopback, 0);
        silent.Start();
        var port = ((IPEndPoint)silent.LocalEndpoint).Port;

        var handshake = await new WebSocketProbe(TimeSpan.FromMilliseconds(300)).HandshakeAsync(
            new WebSocketAddress("localhost", port, false, "/"),
            [IPAddress.Loopback]);

        await Assert.That(handshake.Answered).IsFalse();
    }

    [Test]
    public async Task NoAddressIsNoDial()
    {
        var handshake = await new WebSocketProbe().HandshakeAsync(
            new WebSocketAddress("localhost", 1, false, "/"), []);

        await Assert.That(handshake.Answered).IsFalse();
    }

    private sealed class Bridge : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _loop;

        private Bridge(TcpListener listener, bool refuseAnyOffer, bool refuseEverything)
        {
            _listener = listener;
            _loop = Task.Run(() => AcceptAsync(refuseAnyOffer, refuseEverything));
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public List<string> Paths { get; } = [];

        public List<string?> Offers { get; } = [];

        public static Bridge Start(bool refuseAnyOffer = false, bool refuseEverything = false)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return new Bridge(listener, refuseAnyOffer, refuseEverything);
        }

        private async Task AcceptAsync(bool refuseAnyOffer, bool refuseEverything)
        {
            while (!_stop.IsCancellationRequested)
            {
                TcpClient client;

                try
                {
                    client = await _listener.AcceptTcpClientAsync(_stop.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                using (client)
                {
                    var stream = client.GetStream();
                    var request = await ReadHeadAsync(stream);
                    var lines = request.Split("\r\n");
                    var path = lines[0].Split(' ')[1];
                    string? Header(string name) => lines
                        .Where(l => l.StartsWith(name + ":", StringComparison.OrdinalIgnoreCase))
                        .Select(l => l[(name.Length + 1)..].Trim())
                        .FirstOrDefault();

                    var offer = Header("Sec-WebSocket-Protocol");
                    lock (Paths)
                    {
                        Paths.Add(path);
                        Offers.Add(offer);
                    }

                    string response;

                    if (refuseEverything || (refuseAnyOffer && offer is not null))
                    {
                        response = "HTTP/1.1 400 Invalid protocol selected\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
                    }
                    else
                    {
                        var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(
                            Header("Sec-WebSocket-Key") + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
                        response = "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n"
                            + $"Sec-WebSocket-Accept: {accept}\r\n\r\n";
                    }

                    await stream.WriteAsync(Encoding.ASCII.GetBytes(response));

                    // Hold the socket long enough for the client's close frame.
                    var buffer = new byte[256];
                    using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    try
                    {
                        _ = await stream.ReadAsync(buffer, wait.Token);
                    }
                    catch (Exception)
                    {
                        // The close is not under test.
                    }
                }
            }
        }

        private static async Task<string> ReadHeadAsync(NetworkStream stream)
        {
            var text = new StringBuilder();
            var buffer = new byte[1];

            while (!text.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal)
                && await stream.ReadAsync(buffer) == 1)
            {
                text.Append((char)buffer[0]);
            }

            return text.ToString();
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            _listener.Stop();
            await _loop.ConfigureAwait(false);
            _stop.Dispose();
        }
    }
}
