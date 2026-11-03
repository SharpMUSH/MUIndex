using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using MUI.Crawl;

namespace MUI.Crawl.Tests;

/// <summary>
/// A server's MXP <c>&lt;SUPPORT&gt;</c> question is answered, and the screen it was holding back is
/// the one stored.
/// </summary>
/// <remarks>
/// CoffeeMud asks on connect and holds its connect screen up to three seconds for the reply
/// (<c>DefaultSession.HandshakeCallback</c>, <c>HANDSHAKE_MXPPAUSE</c>). Unanswered, the probe settled
/// on the questions, stored <c>&lt;SUPPORT IMAGE IMAGE.URL&gt;</c> as the game's connect screen
/// (<c>24.138.50.103:5555</c>, 2026-11-02), and the real screen arrived in the phases after it.
/// </remarks>
public class MxpSupportTests
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
    public async Task ASupportQuestionIsAnsweredAndTheScreenBehindItIsTheBanner()
    {
        await using var game = new HoldingGame();

        var result = await new TelnetProbe(Fast()).ProbeAsync(game.Target);

        await Assert.That(result.Outcome).IsEqualTo(ProbeOutcome.Answered);
        await Assert.That(game.Answer).IsNotNull().Because("the server asked, and was waiting");
        await Assert.That(game.Answer!.ToUpperInvariant()).Contains("-IMAGE.URL")
            .Because("this client renders nothing, so it refuses what it was asked about");
        await Assert.That(game.Answer!).DoesNotContain("+")
            .Because("nothing is claimed that the probe does not do");
        await Assert.That(result.Banner).Contains("Welcome to the Holding Pattern");
        await Assert.That(result.Banner).DoesNotContain("<SUPPORT");
        await Assert.That(result.MxpObserved).IsTrue();
    }

    /// <summary>
    /// The same, as CoffeeMud actually does it: the option is agreed, no start marker follows, and the
    /// question arrives on a lock-secure line.
    /// </summary>
    [Test]
    public async Task ASupportQuestionWithNoStartMarkerIsStillAnswered()
    {
        await using var game = new HoldingGame { SendsStartMarker = false };

        var result = await new TelnetProbe(Fast()).ProbeAsync(game.Target);

        await Assert.That(result.Outcome).IsEqualTo(ProbeOutcome.Answered);
        await Assert.That(game.Answer).IsNotNull().Because("the server asked, and was waiting");
        await Assert.That(game.Answer!.ToUpperInvariant()).Contains("-IMAGE.URL");
        await Assert.That(result.Banner).Contains("Welcome to the Holding Pattern");
        await Assert.That(result.Banner).DoesNotContain("<SUPPORT");
        await Assert.That(result.MxpObserved).IsTrue();
    }

    /// <summary>
    /// Once MXP mode starts, the server escapes its quotes, and the screen is stored with them
    /// decoded. CoffeeMud Hardcore's ASCII art arrived as <c>.-&amp;quot;&amp;quot;-.</c>.
    /// </summary>
    [Test]
    public async Task EntitiesInTheScreenAreDecoded()
    {
        await using var game = new HoldingGame
        {
            SendsStartMarker = false,
            Screen = "  .-&quot;&quot;-.  Welcome to the Holding Pattern &amp; friends\r\nAccount name: ",
        };

        var result = await new TelnetProbe(Fast()).ProbeAsync(game.Target);

        await Assert.That(result.Outcome).IsEqualTo(ProbeOutcome.Answered);
        await Assert.That(result.Banner).Contains(".-\"\"-.  Welcome to the Holding Pattern & friends");
        await Assert.That(result.Banner).DoesNotContain("&quot;");
    }

    /// <summary>
    /// Speaks just enough telnet to agree MXP, start it, and ask; then holds its real screen until it
    /// is answered or the hold runs out, as CoffeeMud does.
    /// </summary>
    private sealed class HoldingGame : IAsyncDisposable
    {
        private const byte Iac = 255, Sb = 250, Se = 240, Will = 251, Do = 253, Mxp = 91;

        private static readonly TimeSpan Hold = TimeSpan.FromSeconds(3);

        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _stopping = new();
        private readonly Task _serving;
        private volatile string? _answer;

        public HoldingGame()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            _serving = ServeAsync();
        }

        /// <summary>
        /// Whether MXP mode is started with <c>IAC SB MXP IAC SE</c>. CoffeeMud never sends it and
        /// goes straight to its line-mode sequences.
        /// </summary>
        public bool SendsStartMarker { get; init; } = true;

        /// <summary>The screen sent once the hold ends.</summary>
        public string Screen { get; init; } = "Welcome to the Holding Pattern\r\nAccount name: ";

        public ProbeTarget Target => new(
            IPAddress.Loopback.ToString(),
            ((IPEndPoint)_listener.LocalEndpoint).Port);

        /// <summary>The client's <c>&lt;SUPPORTS&gt;</c> line, if it sent one before the hold ran out.</summary>
        public string? Answer => _answer;

        private async Task ServeAsync()
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync(_stopping.Token);
                await using var stream = client.GetStream();
                var heard = new List<byte>();
                var listening = ListenAsync(stream, heard);

                await stream.WriteAsync(new byte[] { Iac, Will, Mxp }, _stopping.Token);
                await SendAsync(stream, "\r\nConnecting to the Holding Pattern...\r\n");

                var agreed = Stopwatch.StartNew();
                while (!Contains(heard, [Iac, Do, Mxp]) && agreed.Elapsed < Hold)
                {
                    await Task.Delay(20, _stopping.Token);
                }

                if (SendsStartMarker)
                {
                    await stream.WriteAsync(new byte[] { Iac, Sb, Mxp, Iac, Se }, _stopping.Token);
                }

                await SendAsync(stream, "\n\u001b[6z\n\u001b[6z<SUPPORT IMAGE IMAGE.URL>\n");
                await SendAsync(stream, "\n\u001b[6z\n\u001b[6z<SUPPORT>\n");

                var holding = Stopwatch.StartNew();
                while (holding.Elapsed < Hold && _answer is null)
                {
                    _answer = FindAnswer(heard);
                    await Task.Delay(20, _stopping.Token);
                }

                await SendAsync(stream, Screen);
                await listening;
            }
            catch (OperationCanceledException)
            {
            }
            catch (IOException)
            {
            }
            catch (SocketException)
            {
            }
        }

        /// <summary>Everything the client sends, until it closes or the game is disposed.</summary>
        private async Task ListenAsync(NetworkStream stream, List<byte> heard)
        {
            var buffer = new byte[4096];

            try
            {
                while (true)
                {
                    var read = await stream.ReadAsync(buffer, _stopping.Token);
                    if (read == 0)
                    {
                        return;
                    }

                    lock (heard)
                    {
                        heard.AddRange(buffer.AsSpan(0, read).ToArray());
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (IOException)
            {
            }
        }

        private static string? FindAnswer(List<byte> heard)
        {
            string text;
            lock (heard)
            {
                text = Encoding.Latin1.GetString(heard.ToArray());
            }

            var at = text.IndexOf("<SUPPORTS", StringComparison.OrdinalIgnoreCase);
            var end = at < 0 ? -1 : text.IndexOf('>', at);
            return end > 0 ? text[at..(end + 1)] : null;
        }

        private static bool Contains(List<byte> heard, byte[] sequence)
        {
            lock (heard)
            {
                return IndexOf(heard, sequence) >= 0;
            }
        }

        private static int IndexOf(List<byte> heard, byte[] sequence)
        {
            for (var i = 0; i + sequence.Length <= heard.Count; i++)
            {
                var match = true;
                for (var j = 0; j < sequence.Length && match; j++)
                {
                    match = heard[i + j] == sequence[j];
                }

                if (match)
                {
                    return i;
                }
            }

            return -1;
        }

        private Task SendAsync(NetworkStream stream, string text) =>
            stream.WriteAsync(Encoding.Latin1.GetBytes(text), _stopping.Token).AsTask();

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
    }
}
