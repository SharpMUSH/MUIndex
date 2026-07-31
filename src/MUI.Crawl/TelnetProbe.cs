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
/// connection: the banner it sends unprompted, the options it negotiates, the MSSP report it
/// publishes for crawlers, and the pre-login <c>WHO</c> the TinyMUD family answers before login.
/// <see cref="PermittedCommands"/> is the complete list of what may go on the wire, and
/// <c>connect</c> and <c>create</c> are not on it — enforced by test, not by good intentions.
/// </para>
/// <para>
/// <b>Layer 1 comes from the library's own callbacks, not from parsing bytes.</b> Each plugin below
/// fires only when the server actually negotiated that option, so being told by the thing that did
/// the negotiating is both simpler and more truthful than decoding the same exchange a second time.
/// </para>
/// </remarks>
public sealed class TelnetProbe(ProbeOptions? options = null, ILogger? logger = null) : IProbe
{
    /// <summary>
    /// Every command this probe is allowed to send. Anything that logs in, creates a character, or
    /// changes server state is absent by construction.
    /// </summary>
    public static readonly IReadOnlyList<string> PermittedCommands = ["WHO"];

    private const byte Iac = 255;
    private const byte Do = 253;

    private readonly ProbeOptions _options = options ?? new ProbeOptions();
    private readonly ILogger _logger = logger ?? NullLogger.Instance;

    public async Task<ProbeResult> ProbeAsync(ProbeTarget target, CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.GetTimestamp();
        var observedAt = DateTimeOffset.UtcNow;

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(_options.Timeout);

        var lines = new List<string>();
        var seen = new Observations();

        using var client = new TcpClient();

        try
        {
            await client.ConnectAsync(target.Host, target.Port, budget.Token);

            var built = await Build(seen, lines).BuildAndStartAsync(client, budget.Token);
            var telnet = built.Item1;

            // Ask for the options a server may support without volunteering. Negotiation, not
            // traffic — written straight to the network so it is not escaped as data would be.
            foreach (var option in _options.RequestOptions)
            {
                await telnet.WriteToNetworkAsync(new byte[] { Iac, Do, option }, budget.Token);
            }

            // Let the connect screen arrive, then ask the one question we are allowed to ask.
            // Banner and answer are kept apart because they are different evidence: one is a display
            // asset and codebase fingerprint, the other is a measurement.
            await Task.Delay(_options.BannerQuietPeriod, budget.Token);

            int bannerLines;
            lock (lines)
            {
                bannerLines = lines.Count;
            }

            await telnet.SendAsync(Encoding.ASCII.GetBytes($"{PermittedCommands[0]}\r\n"));
            await Task.Delay(_options.BannerQuietPeriod, budget.Token);

            string banner, whoText;
            lock (lines)
            {
                banner = string.Join("\n", lines.Take(bannerLines));
                whoText = string.Join("\n", lines.Skip(bannerLines));
            }

            if (telnet.CurrentEncoding is not null)
            {
                seen.Charset ??= telnet.CurrentEncoding.WebName;
            }

            return new ProbeResult
            {
                Host = target.Host,
                Port = target.Port,
                ObservedAt = observedAt,
                Outcome = ProbeOutcome.Answered,
                OfferedOptions = seen.Supported,
                Negotiation = seen.ToNegotiation(),
                Banner = banner,
                Who = new WhoParser().Parse(whoText),
                BannerPlayerCount = BannerCount.Find(banner),
                Mssp = Flatten(seen.Mssp),
                MsspOutcome = seen.MsspOutcome,
                MsspBytesRejected = seen.MsspRejectedBytes,
                MsspTransport = seen.MsspOutcome is MsspOutcome.Received
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
                OfferedOptions = seen.Supported,
                Negotiation = seen.ToNegotiation(),
                Failure = Classify(error),
                Elapsed = Stopwatch.GetElapsedTime(started),
            };
        }
    }

    /// <summary>
    /// Registers every protocol worth observing and hooks the callback that proves it engaged.
    /// </summary>
    /// <remarks>
    /// Each plugin is here to be <em>watched</em>, not used. This client renders nothing, so MXP and
    /// MCCP buy it no features — what they buy is the knowledge that this server offers them, which
    /// is what the capability matrix is made of.
    /// <para>
    /// A protocol is recorded from inside its own callback, because that is the event that proves it
    /// ran. The <c>OnEnabledAsync</c> overrides in <see cref="Watched"/> are a second, cheaper signal
    /// for the same thing — but they are not sufficient on their own: measured against live servers
    /// they did not fire, while <c>OnMSSP</c> and <c>OnCharsetChange</c> did. Keeping both means a
    /// protocol counts as supported if either the library says it enabled or it demonstrably did
    /// something, and neither route can silently become the only one.
    /// </para>
    /// </remarks>
    private TelnetInterpreterBuilder Build(Observations seen, List<string> lines)
    {
        void Note(string protocol) => seen.Note(protocol);

        var mssp = new Watched.Mssp(Note);
        mssp.WithMaxMessageSize(_options.MaxSubnegotiationBytes);
        mssp.OnMSSP(config =>
        {
            seen.Mssp = config;
            seen.MsspOutcome = MsspOutcome.Received;
            Note("MSSP");
            return ValueTask.CompletedTask;
        });
        // 2.7.0 drops an oversized report whole rather than truncating it. Kept as its own outcome:
        // we asked, they answered, we declined to hold it — which is not "no MSSP".
        mssp.OnMSSPMessageTooLarge(sizes =>
        {
            seen.MsspOutcome = MsspOutcome.RejectedTooLarge;
            seen.MsspRejectedBytes = (int)Math.Min(sizes.Item1, int.MaxValue);
            Note("MSSP");
            return ValueTask.CompletedTask;
        });

        var gmcp = new Watched.Gmcp(Note);
        gmcp.WithMaxMessageSize(_options.MaxSubnegotiationBytes);
        gmcp.OnGMCPMessage(message =>
        {
            seen.Gmcp(message.Item1);
            Note("GMCP");
            return ValueTask.CompletedTask;
        });

        var msdp = new Watched.Msdp(Note);
        msdp.WithMaxMessageSize(_options.MaxSubnegotiationBytes);

        var newEnviron = new Watched.NewEnviron(Note);
        newEnviron.OnEnvironmentVariables((requested, _) =>
        {
            seen.Environment(requested.Keys);
            Note("NEW-ENVIRON");
            return ValueTask.CompletedTask;
        });

        var eor = new Watched.Eor(Note);
        eor.OnPrompt(() =>
        {
            seen.Prompts = true;
            Note("EOR");
            return ValueTask.CompletedTask;
        });

        // CharsetProtocol takes its order as a property rather than a builder call, and exposes
        // OnCharsetChange on the instance — so the settled encoding is captured here rather than
        // read off the interpreter afterwards, where a default is indistinguishable from a result.
        var charset = new Watched.Charset(Note) { CharsetOrder = [Encoding.UTF8, Encoding.Latin1] };
        charset.OnCharsetChange(encoding =>
        {
            seen.Charset = encoding.WebName;
            seen.CharsetNegotiated = true;
            Note("CHARSET");
            return ValueTask.CompletedTask;
        });

        return new TelnetInterpreterBuilder()
            .UseMode(TelnetInterpreter.TelnetMode.Client)
            .UseLogger(_logger)
            .OnSubmit((bytes, encoding, _) =>
            {
                var text = (encoding ?? Encoding.UTF8).GetString(bytes);
                lock (lines)
                {
                    lines.Add(text);
                }

                return ValueTask.CompletedTask;
            })
            .AddPlugin(mssp)
            .AddPlugin(charset)
            .AddPlugin(newEnviron)
            .AddPlugin(gmcp)
            .AddPlugin(msdp)
            .AddPlugin(eor)
            // MCCP is deliberately NOT registered. Two reasons, and the first stands on its own:
            // a crawler reads a few kilobytes per probe, so compression buys it nothing while
            // costing it the ability to read what arrives. The second is that accepting it today
            // loses the data outright — TelnetNegotiationCore negotiates MCCP2 and never inflates
            // the stream (upstream issue #62), so the connect screen and the whole WHO reply arrive
            // as raw zlib and are decoded as text. Declining returns the identical banner in
            // plaintext. Measured on 13 of the 38 codebases surveyed.
            //
            // The cost is honest and worth stating: we no longer observe that a server *offers*
            // MCCP, because the library only reports it on acceptance. When #62 is fixed, accept it
            // again and get both.
            .AddPlugin(new Watched.Mxp(Note))
            .AddPlugin(new Watched.SuppressGoAhead(Note))
            .AddPlugin(new Watched.Naws(Note))
            .AddPlugin(new Watched.TerminalType(Note))
            .AddPlugin(new Watched.Echo(Note));
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
        _ => new("error", error.Message),
    };

    private static IReadOnlyDictionary<string, string> Flatten(MSSPConfig? config)
    {
        if (config is null)
        {
            return new Dictionary<string, string>();
        }

        var flat = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void Put(string name, object? value)
        {
            var text = value switch
            {
                null => null,
                IEnumerable<string> many => string.Join(", ", many),
                _ => value.ToString(),
            };

            if (!string.IsNullOrWhiteSpace(text))
            {
                flat[name] = text;
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

    /// <summary>Mutable scratch for one probe. Callbacks arrive on the read loop, so it locks.</summary>
    private sealed class Observations
    {
        private readonly HashSet<string> _supported = new(StringComparer.Ordinal);
        private readonly List<string> _environment = [];
        private readonly List<string> _gmcp = [];

        public MSSPConfig? Mssp;
        public MsspOutcome MsspOutcome = MsspOutcome.NotOffered;
        public int? MsspRejectedBytes;
        public string? Charset;
        public bool Prompts;
        public bool CharsetNegotiated;

        public IReadOnlySet<string> Supported
        {
            get
            {
                lock (_supported)
                {
                    return _supported.ToHashSet(StringComparer.Ordinal);
                }
            }
        }

        public void Note(string protocol)
        {
            lock (_supported)
            {
                _supported.Add(protocol);
            }
        }

        public void Environment(IEnumerable<string> names)
        {
            lock (_environment)
            {
                _environment.AddRange(names);
            }
        }

        public void Gmcp(string package)
        {
            lock (_gmcp)
            {
                _gmcp.Add(package);
            }
        }

        public Negotiation ToNegotiation()
        {
            lock (_environment)
            {
                lock (_gmcp)
                {
                    return new Negotiation
                    {
                        Supported = Supported,
                        Charset = Charset,
                        CharsetNegotiated = CharsetNegotiated,
                        EnvironmentRequested = _environment.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                        GmcpPackages = _gmcp.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                        SendsPromptMarkers = Prompts,
                    };
                }
            }
        }
    }
}
