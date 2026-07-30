using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TelnetNegotiationCore.Builders;
using TelnetNegotiationCore.Interpreters;
using TelnetNegotiationCore.Models;
using TelnetNegotiationCore.Protocols;

namespace MUI.Crawl;

/// <summary>
/// One telnet session against one game, yielding a <see cref="ProbeResult"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>This client never authenticates.</b> Everything it reads is what a server hands an anonymous
/// connection: the banner it sends unprompted, the options it offers in the handshake, the MSSP
/// report it publishes for crawlers, and the pre-login <c>WHO</c> the TinyMUD family answers before
/// login. <see cref="PermittedCommands"/> is the complete list of what may go on the wire, and
/// <c>connect</c> and <c>create</c> are not on it — enforced by test, not by good intentions.
/// </para>
/// <para>
/// Temporary: this is the real-server path used while there is no scripted fake MU* server. It is
/// deliberately conservative — one connection, one short quiet period, one <c>WHO</c>.
/// </para>
/// </remarks>
public sealed class TelnetProbe(ProbeOptions? options = null, ILogger? logger = null) : IProbe
{
    /// <summary>
    /// Every command this probe is allowed to send. Anything that logs in, creates a character, or
    /// changes server state is absent by construction.
    /// </summary>
    public static readonly IReadOnlyList<string> PermittedCommands = ["WHO"];

    private readonly ProbeOptions _options = options ?? new ProbeOptions();
    private readonly ILogger _logger = logger ?? NullLogger.Instance;

    public async Task<ProbeResult> ProbeAsync(ProbeTarget target, CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.GetTimestamp();
        var observedAt = DateTimeOffset.UtcNow;

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(_options.Timeout);

        var banner = new StringBuilder();
        var lines = new List<string>();
        MSSPConfig? mssp = null;
        var msspOutcome = MsspOutcome.NotOffered;
        int? rejectedBytes = null;
        var offered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var client = new TcpClient();

        try
        {
            await client.ConnectAsync(target.Host, target.Port, budget.Token);

            var built = await new TelnetInterpreterBuilder()
                .UseMode(TelnetInterpreter.TelnetMode.Client)
                .UseLogger(_logger)
                .OnSubmit((bytes, encoding, _) =>
                {
                    var text = (encoding ?? Encoding.UTF8).GetString(bytes);
                    lock (lines)
                    {
                        lines.Add(text);
                        banner.AppendLine(text);
                    }

                    return ValueTask.CompletedTask;
                })
                .AddDefaultMUDProtocols()
                .AddPlugin<MSSPProtocol>()
                    .OnMSSP(config =>
                    {
                        mssp = config;
                        msspOutcome = MsspOutcome.Received;
                        offered.Add("MSSP");
                        return ValueTask.CompletedTask;
                    })
                    // 2.7.0 drops an oversized report whole rather than truncating it. Recorded as
                    // its own outcome: "we asked, they answered, we declined to hold it" is not
                    // "no MSSP", and rendering it as one would publish our limit as their fact.
                    .OnMSSPMessageTooLarge(sizes =>
                    {
                        msspOutcome = MsspOutcome.RejectedTooLarge;
                        rejectedBytes = (int)Math.Min(sizes.Item1, int.MaxValue);
                        offered.Add("MSSP");
                        return ValueTask.CompletedTask;
                    })
                    .WithMaxMessageSize(_options.MaxSubnegotiationBytes)
                .BuildAndStartAsync(client, budget.Token);

            var telnet = built.Item1;

            // Let the connect screen arrive, then ask the one question we are allowed to ask.
            await Task.Delay(_options.BannerQuietPeriod, budget.Token);
            await telnet.SendAsync(Encoding.ASCII.GetBytes($"{PermittedCommands[0]}\r\n"));
            await Task.Delay(_options.BannerQuietPeriod, budget.Token);

            if (telnet.CurrentEncoding is not null)
            {
                offered.Add($"CHARSET:{telnet.CurrentEncoding.WebName}");
            }

            return new ProbeResult
            {
                Host = target.Host,
                Port = target.Port,
                ObservedAt = observedAt,
                Outcome = ProbeOutcome.Answered,
                OfferedOptions = offered,
                Banner = banner.ToString(),
                Who = WhoReading.Unread,
                Mssp = Flatten(mssp),
                MsspOutcome = msspOutcome,
                MsspBytesRejected = rejectedBytes,
                MsspTransport = msspOutcome is MsspOutcome.Received
                    ? MsspTransport.TelnetOption70
                    : MsspTransport.None,
                Elapsed = Stopwatch.GetElapsedTime(started),
            };
        }
        catch (Exception error)
        {
            return new ProbeResult
            {
                Host = target.Host,
                Port = target.Port,
                ObservedAt = observedAt,
                Outcome = ProbeOutcome.Failed,
                Banner = banner.Length == 0 ? null : banner.ToString(),
                Failure = Classify(error),
                Elapsed = Stopwatch.GetElapsedTime(started),
            };
        }
    }

    /// <summary>
    /// Failure causes, kept apart because only a change of cause writes an availability transition
    /// (spec §5.3) — a hundred consecutive timeouts are one interval, not a hundred.
    /// </summary>
    private static FailureDetail Classify(Exception error) => error switch
    {
        SocketException { SocketErrorCode: SocketError.HostNotFound } => new("dns", error.Message),
        SocketException { SocketErrorCode: SocketError.ConnectionRefused } => new("refused", error.Message),
        SocketException { SocketErrorCode: SocketError.TimedOut } => new("timeout", error.Message),
        OperationCanceledException => new("timeout", "probe budget exhausted"),
        AuthenticationExceptionMarker => new("tls", error.Message),
        _ => new("error", error.Message),
    };

    /// <summary>Matches TLS failures without taking a dependency on the auth namespace here.</summary>
    private abstract class AuthenticationExceptionMarker : Exception;

    private static IReadOnlyDictionary<string, string> Flatten(MSSPConfig? config)
    {
        if (config is null)
        {
            return new Dictionary<string, string>();
        }

        var flat = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void Put(string name, object? value)
        {
            if (value is null)
            {
                return;
            }

            var text = value switch
            {
                IEnumerable<string> many => string.Join(", ", many),
                _ => value.ToString(),
            };

            if (!string.IsNullOrWhiteSpace(text))
            {
                flat[name] = text!;
            }
        }

        Put("NAME", config.Name);
        Put("PLAYERS", config.Players);
        Put("UPTIME", config.Uptime);
        Put("CODEBASE", config.Codebase);
        Put("CONTACT", config.Contact);
        Put("CRAWL DELAY", config.Crawl_Delay);
        Put("CHARSET", config.Charset);

        return flat;
    }
}
