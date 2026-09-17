using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

using Microsoft.Extensions.Logging.Abstractions;

using TelnetNegotiationCore.Builders;
using TelnetNegotiationCore.Interpreters;

namespace MUI.Crawl.Tests;

/// <summary>
/// The probe against a game that answers only behind a TLS handshake.
/// </summary>
/// <remarks>
/// Measured before it was written: <c>chatmud.com:7443</c>, <c>mud.drifters.world:3000</c> and two
/// others accept a connection, send nothing, and serve a whole connect screen — telnet negotiation
/// and all — to anyone who opens with a ClientHello. Unlike SSH, TLS is a transport underneath
/// telnet rather than a session protocol beside it: the IAC bytes are still there, so MSSP, GMCP and
/// everything else above survive the wrapping and none of it has to be special-cased.
/// </remarks>
public class TlsTransportTests
{
    /// <summary>Short, so the suite settles in a moment rather than in the live defaults.</summary>
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

    /// <summary>
    /// A target told to dial TLS reads the screen behind the handshake.
    /// </summary>
    /// <remarks>
    /// The fixture's certificate is self-signed and names <c>CN=localhost</c> while the probe dials
    /// <c>127.0.0.1</c>, so this also pins the accept-anything policy: a probe reads a public login
    /// screen, and refusing a certificate chain would drop games without protecting anything.
    /// </remarks>
    [Test]
    public async Task AGameBehindTlsIsReadThroughTheHandshake()
    {
        await using var game = new TlsGame
        {
            Banner = "Welcome to Nowhere\r\nA quiet little place.\r\n",
        };

        var result = await new TelnetProbe(Fast()).ProbeAsync(game.Target with { UseTls = true });

        await Assert.That(game.Fault?.ToString() ?? "none").IsEqualTo("none");
        await Assert.That(result.Outcome).IsEqualTo(ProbeOutcome.Answered);
        await Assert.That(result.Banner).Contains("A quiet little place.");
        await Assert.That(result.Transport).IsEqualTo(ProbeTransport.Tls);
    }

    /// <summary>
    /// A silent plaintext dial is asked the one further question there is to ask.
    /// </summary>
    /// <remarks>
    /// The retry <em>is</em> the detection, and there is no other: a TLS listener sends nothing at
    /// all until it is sent a ClientHello, so it is byte-for-byte indistinguishable from a socket
    /// that accepts and sits there. This is how <c>chatmud.com:7443</c> and
    /// <c>mud.drifters.world:3000</c> spent months in the registry reading as mute.
    /// </remarks>
    [Test]
    public async Task ASilentDialIsRetriedOnceOverTls()
    {
        await using var game = new TlsGame
        {
            Banner = "Welcome to Nowhere\r\nA quiet little place.\r\n",
        };

        // No UseTls: nothing has told this probe the address is anything but an ordinary port.
        var result = await new TelnetProbe(Fast()).ProbeAsync(game.Target);

        await Assert.That(game.Fault?.ToString() ?? "none").IsEqualTo("none");
        await Assert.That(result.Outcome).IsEqualTo(ProbeOutcome.Answered);
        await Assert.That(result.Banner).Contains("A quiet little place.");
        await Assert.That(result.Transport).IsEqualTo(ProbeTransport.Tls);
        await Assert.That(game.Connections).IsEqualTo(2);
    }

    /// <summary>
    /// An ordinary port that answers is dialled once, and stays what it is.
    /// </summary>
    /// <remarks>
    /// The retry's bound, and the reason it is affordable: it is spent only where the first dial
    /// heard nothing. A server that said anything has already answered the question the handshake
    /// would ask, and dialling it twice every cycle would double the load on the whole catalogue to
    /// re-learn what the first connection already proved.
    /// </remarks>
    [Test]
    public async Task APortThatAnswersInTheClearIsNotDialledTwice()
    {
        await using var game = new TlsGame
        {
            Tls = false,
            Banner = "Welcome to Nowhere\r\nA quiet little place.\r\n",
        };

        var result = await new TelnetProbe(Fast()).ProbeAsync(game.Target);

        await Assert.That(game.Fault?.ToString() ?? "none").IsEqualTo("none");
        await Assert.That(result.Outcome).IsEqualTo(ProbeOutcome.Answered);
        await Assert.That(result.Banner).Contains("A quiet little place.");
        await Assert.That(result.Transport).IsEqualTo(ProbeTransport.Telnet);
        await Assert.That(game.Connections).IsEqualTo(1);
    }

    /// <summary>
    /// A port that has stopped speaking TLS is read in the clear rather than left dark.
    /// </summary>
    /// <remarks>
    /// The retry runs in both directions on purpose, and this is the direction that matters most:
    /// once a target is marked TLS, a game that later moves back to an ordinary port would dial into
    /// a handshake nobody answers, fail every cycle, and be published as dark while running
    /// perfectly well — the same bug this whole change exists to fix, only mirrored and worse, since
    /// nothing in the registry would ever unstick it.
    /// </remarks>
    [Test]
    public async Task APortThatStoppedSpeakingTlsIsStillRead()
    {
        await using var game = new TlsGame
        {
            Tls = false,
            Banner = "Welcome to Nowhere\r\nA quiet little place.\r\n",
        };

        var result = await new TelnetProbe(Fast()).ProbeAsync(game.Target with { UseTls = true });

        await Assert.That(game.Fault?.ToString() ?? "none").IsEqualTo("none");
        await Assert.That(result.Outcome).IsEqualTo(ProbeOutcome.Answered);
        await Assert.That(result.Banner).Contains("A quiet little place.");
        await Assert.That(result.Transport).IsEqualTo(ProbeTransport.Telnet);
    }

    /// <summary>
    /// A web server behind the handshake is not adopted as a game.
    /// </summary>
    /// <remarks>
    /// Measured, and it is half of what the retry finds in the wild: of the four silent addresses in
    /// the registry that answer a ClientHello, <c>110.10.160.150:4001</c> and <c>mud.ren:8888</c> are
    /// nginx, which completes the handshake and then returns <c>400 Bad Request</c> to telnet
    /// negotiation bytes. One of the two is already listed as a game, so adopting that reply would
    /// put an HTML error page on a game page as its connect screen — a claim about a game, made out
    /// of a web server's complaint. An HTTP response is proof the port is not a MU*, so the retry
    /// declines it and the address stays exactly as silent as it was before this existed.
    /// </remarks>
    [Test]
    public async Task AWebServerBehindTheHandshakeIsNotAdoptedAsAConnectScreen()
    {
        await using var game = new TlsGame
        {
            Banner = "HTTP/1.1 400 Bad Request\r\nServer: nginx\r\nConnection: close\r\n\r\n"
                + "<html><head><title>400 Bad Request</title></head></html>\r\n",
        };

        var result = await new TelnetProbe(Fast()).ProbeAsync(game.Target);

        await Assert.That(game.Fault?.ToString() ?? "none").IsEqualTo("none");
        await Assert.That(result.Transport).IsEqualTo(ProbeTransport.Telnet);
        await Assert.That(result.Banner ?? string.Empty).DoesNotContain("400 Bad Request");
    }

    /// <summary>
    /// Nor is one adopted at an address we already believed was TLS.
    /// </summary>
    /// <remarks>
    /// The same defect by the other route, and the one that survives longest: a port that really
    /// was a TLS game has <c>use_tls</c> set on its target for ever after, so when the game dies and
    /// the host puts a web server on the port, the probe opens with a handshake that now succeeds
    /// and takes nginx's reply as that game's connect screen. Nothing would correct it — the screen
    /// would simply change one day, on a live listing, into an HTML error document.
    /// </remarks>
    [Test]
    public async Task AWebServerIsNotAdoptedAtAnAddressAlreadyBelievedToBeTls()
    {
        await using var game = new TlsGame
        {
            Banner = "HTTP/1.1 400 Bad Request\r\nServer: nginx\r\nConnection: close\r\n\r\n"
                + "<html><head><title>400 Bad Request</title></head></html>\r\n",
        };

        var result = await new TelnetProbe(Fast()).ProbeAsync(game.Target with { UseTls = true });

        await Assert.That(game.Fault?.ToString() ?? "none").IsEqualTo("none");
        await Assert.That(result.Banner ?? string.Empty).DoesNotContain("400 Bad Request");
        await Assert.That(result.Transport).IsEqualTo(ProbeTransport.Telnet);
    }

    /// <summary>
    /// And when there is nowhere to fall back to, the web server's reply is dropped rather than kept.
    /// </summary>
    /// <remarks>
    /// The residual case: the handshake answered with HTTP and the second dial never got in, so
    /// there is no other session to prefer. The failed dial must not be returned in its place —
    /// <c>ProbeIngestor</c> reads a failure as unreachable, and this address demonstrably answered a
    /// moment ago, so that would publish an outage that did not happen (rule 5). What is left is the
    /// TLS session, which is true in every respect except the one that matters: its banner is not a
    /// connect screen. So the session stands and the banner goes. <b>Dropping it is the opposite of
    /// fabricating</b> — no screen was read, and none is recorded.
    /// </remarks>
    [Test]
    public async Task AWebServerWithNowhereToFallBackToKeepsTheAnswerAndLosesTheBanner()
    {
        await using var game = new TlsGame
        {
            ClosesListenerAfterFirstConnection = true,
            Banner = "HTTP/1.1 400 Bad Request\r\nServer: nginx\r\nConnection: close\r\n\r\n"
                + "<html><head><title>400 Bad Request</title></head></html>\r\n",
        };

        var result = await new TelnetProbe(Fast()).ProbeAsync(game.Target with { UseTls = true });

        await Assert.That(game.Fault?.ToString() ?? "none").IsEqualTo("none");

        // It answered, and saying otherwise would be an outage we invented.
        await Assert.That(result.Outcome).IsEqualTo(ProbeOutcome.Answered);
        await Assert.That(result.Banner ?? string.Empty).DoesNotContain("400 Bad Request");
    }

    /// <summary>
    /// A handshake that hangs is a handshake that did not happen, and still gets the second dial.
    /// </summary>
    /// <remarks>
    /// The stale-flag case again, by the one route that used to escape it. A port that answers a
    /// ClientHello with a TLS <em>alert</em> fails fast and is retried; a port that answers it with
    /// nothing at all leaves the handshake hanging until the probe budget expires, which classifies
    /// as <c>Timeout</c> — and a timeout means "the far end never got as far as a conversation", so
    /// <see cref="ProbeOutcome"/> aside there was no second question to ask and the retry was
    /// skipped. Wrong here, because the conversation the timeout describes is the handshake, which
    /// is exactly the thing the other transport does not need.
    /// </remarks>
    [Test]
    public async Task AHandshakeThatHangsIsStillRetriedInTheClear()
    {
        await using var game = new TlsGame
        {
            // Never speaks and never handshakes: the shape of a plaintext server that waits to be
            // spoken to first, sitting at an address something once measured as TLS.
            Silent = true,
        };

        // Long enough for the second session to run its phases out against a server that never
        // speaks, since that budget is what ends it; the first session spends the whole of it
        // waiting on a ServerHello that is not coming, which is the point.
        var brief = Fast() with { Timeout = TimeSpan.FromSeconds(6) };

        var result = await new TelnetProbe(brief).ProbeAsync(game.Target with { UseTls = true });

        await Assert.That(game.Fault?.ToString() ?? "none").IsEqualTo("none");
        await Assert.That(game.Connections).IsEqualTo(2);

        // And the answer is the dial that answered. A socket that accepted and said nothing is a
        // truer record than a handshake timeout, and it is the difference between a game reading as
        // reachable-and-quiet and reading as dark.
        await Assert.That(result.Outcome).IsEqualTo(ProbeOutcome.Answered);
        await Assert.That(result.Transport).IsEqualTo(ProbeTransport.Telnet);
    }

    /// <summary>
    /// A server listening for TLS, which is indistinguishable from a mute one until we try.
    /// </summary>
    /// <remarks>
    /// Accepts repeatedly rather than once, because the behaviour under test is a second dial: the
    /// first arrives as telnet negotiation bytes where a ClientHello was expected, and the handshake
    /// fails on the server exactly as it does in the field.
    /// </remarks>
    private sealed class TlsGame : IAsyncDisposable
    {
        private readonly X509Certificate2 _certificate = SelfSigned();
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _stopping = new();
        private readonly Task _serving;

        /// <summary>
        /// The sessions this fixture has started, so disposal can wait for them.
        /// </summary>
        /// <remarks>
        /// They cannot be awaited where they are started — the accept loop has to be back at
        /// <c>AcceptTcpClientAsync</c> before the probe's retry arrives, which is the behaviour every
        /// test here turns on. Untracked, disposal raced them: <c>_stopping</c> and
        /// <c>_certificate</c> could be disposed under a session still reading through them.
        /// </remarks>
        private readonly List<Task> _sessions = [];

        private int _connections;

        public TlsGame()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            _serving = ServeAsync();
        }

        public string Banner { get; init; } = string.Empty;

        /// <summary>
        /// Whether this server expects a ClientHello. False serves the same screen in the clear —
        /// an ordinary port, which is what the retry must leave alone.
        /// </summary>
        public bool Tls { get; init; } = true;

        /// <summary>
        /// Whether the listener closes as soon as it has taken one connection, so a second dial is
        /// refused while the first session carries on over the socket it already has.
        /// </summary>
        /// <remarks>
        /// Stops listening at accept rather than at the end of the session, which makes the refusal
        /// deterministic: the probe's fallback dial cannot arrive before the listener is down.
        /// </remarks>
        public bool ClosesListenerAfterFirstConnection { get; init; }

        /// <summary>
        /// Whether this server accepts a connection and then does nothing whatever with it — no
        /// handshake, no banner, no reply.
        /// </summary>
        /// <remarks>
        /// A real shape, and the one that hangs a handshake rather than refusing it: a server that
        /// waits to be spoken to first never answers a ClientHello, so <c>SslStream</c> sits there
        /// until the probe's own budget ends it.
        /// </remarks>
        public bool Silent { get; init; }

        public ProbeTarget Target => new(
            IPAddress.Loopback.ToString(),
            ((IPEndPoint)_listener.LocalEndpoint).Port);

        /// <summary>How many times anything has connected — a retry is a second one.</summary>
        public int Connections => Volatile.Read(ref _connections);

        /// <summary>An exception this fixture did not expect, held rather than swallowed.</summary>
        public Exception? Fault { get; private set; }

        public async ValueTask DisposeAsync()
        {
            await _stopping.CancelAsync();
            _listener.Stop();

            try
            {
                await _serving;

                // Before the two disposals below, never after: a session still in flight is reading
                // through this certificate and watching this token.
                Task[] sessions;

                lock (_sessions)
                {
                    sessions = [.. _sessions];
                }

                await Task.WhenAll(sessions);
            }
            catch (OperationCanceledException)
            {
            }

            _stopping.Dispose();
            _certificate.Dispose();
        }

        /// <summary>
        /// A certificate nobody trusts, which is the point: real MU* TLS ports routinely serve one.
        /// </summary>
        /// <remarks>
        /// Round-tripped through PKCS#12 because a certificate built from an ephemeral key cannot be
        /// handed to <see cref="SslStream"/> as a server certificate on every platform — the private
        /// key has to be one the certificate itself carries.
        /// </remarks>
        private static X509Certificate2 SelfSigned()
        {
            using var key = RSA.Create(2048);

            var request = new CertificateRequest(
                "CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            using var certificate = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

            return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pfx), null);
        }

        private async Task ServeAsync()
        {
            while (!_stopping.IsCancellationRequested)
            {
                TcpClient client;

                try
                {
                    client = await _listener.AcceptTcpClientAsync(_stopping.Token);
                }
                catch (Exception error) when (error is OperationCanceledException or SocketException)
                {
                    return;
                }

                Interlocked.Increment(ref _connections);

                var session = SessionAsync(client);

                lock (_sessions)
                {
                    _sessions.Add(session);
                }

                if (ClosesListenerAfterFirstConnection)
                {
                    // Down at accept rather than at the end of the session, so the probe's fallback
                    // dial is refused deterministically while this one carries on over the socket it
                    // already holds. The loop ends with it — there is nothing left to accept on.
                    _listener.Stop();
                    return;
                }
            }
        }

        private async Task SessionAsync(TcpClient client)
        {
            try
            {
                using (client)
                {
                    if (Silent)
                    {
                        // Held open rather than closed: a closed socket is a different measurement
                        // (the far end hung up) and would be retried for a different reason.
                        await Task.Delay(Timeout.Infinite, _stopping.Token);
                        return;
                    }

                    Stream transport = client.GetStream();

                    if (Tls)
                    {
                        var tls = new SslStream(transport, leaveInnerStreamOpen: false);
                        await tls.AuthenticateAsServerAsync(
                            new SslServerAuthenticationOptions { ServerCertificate = _certificate },
                            _stopping.Token);
                        transport = tls;
                    }

                    await using var _ = transport;

                    var built = await new TelnetInterpreterBuilder()
                        .UseMode(TelnetInterpreter.TelnetMode.Server)
                        .UseLogger(NullLogger.Instance)
                        // Required by the builder, and deliberately empty: this fixture answers no
                        // command. What is under test is the transport, not the conversation.
                        .OnSubmit((_, _, _) => ValueTask.CompletedTask)
                        .BuildAndStartAsync(transport, _stopping.Token);

                    await using var telnet = built.Interpreter;

                    // WriteToNetworkAsync rather than SendAsync: the banner is a complete block with
                    // its own terminators, and SendAsync would append another CR LF.
                    await telnet.WriteToNetworkAsync(Encoding.Latin1.GetBytes(Banner));

                    await built.ReadTask;
                }
            }
            catch (Exception error) when (error
                is OperationCanceledException
                or IOException
                or SocketException
                or AuthenticationException
                or ObjectDisposedException)
            {
                // A dial that opened with telnet negotiation rather than a ClientHello lands here,
                // which is the shape this fixture exists to present.
            }
            catch (Exception error)
            {
                // Anything else is a defect in the fixture rather than a shape under test. Held for
                // the test to assert on: this session is fire-and-forget, so an exception nobody
                // recorded would be an unobserved task and the test would fail somewhere unrelated.
                Fault ??= error;
            }
        }
    }
}
