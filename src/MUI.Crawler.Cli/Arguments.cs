using MUI.Crawl;

namespace MUI.Crawler.Cli;

/// <summary>What <c>mui-crawl</c> was asked to do.</summary>
/// <remarks>
/// Hand-parsed, because a dependency for eight switches is a dependency the whole solution then
/// carries. Anything unrecognised is an error rather than a shrug: a mistyped switch that silently
/// did nothing would be a crawl that quietly ignored the seed list a person came here to test.
/// </remarks>
public sealed record Arguments
{
    public const string Usage = """
        mui-crawl — run crawl cycles against a real database and print what landed.

          --connection <string>   PostgreSQL connection string. Defaults to $MUI_CRAWL_POSTGRES.
          --seed <host:port>      An address to add to the registry. Repeatable. An IPv6 literal
                                  is bracketed — [2001:db8::1]:4201 — because a bare one does not
                                  say which colon is the port.
          --seed-exempt <h:p>     The same, exempt from the resolved-address gate (§7.2). Say this
                                  only about an address you chose on purpose — it is what lets the
                                  crawler dial a private address, and nothing else may.
          --cycles <n>            How many passes to run. Default 1.
          --batch <n>             How many due targets one pass claims. Default 50.
          --concurrency <n>       How many probes may be in flight at once. Default 8.
          --info-url <https url>  The contact address announced to every server dialled (§11).
                                  Defaults to $MUI_CRAWL_INFO_URL, and to a placeholder that
                                  answers nobody if that is unset too.
          --no-referrals          Do not follow MSSP REFERRAL. Makes this a status checker.
          --dry-run               Print what is due and write nothing.
          --i3                    Run one Intermud-3 pass instead of a crawl cycle: read the
                                  sidecar's mudlist, seed addresses we do not have, bind muds whose
                                  address has been promoted, and ask the bound ones for a count.
          --i3-gateway <host:port> Where the sidecar's JSON-RPC surface is. Default 127.0.0.1:8081.
          --i3-key <string>       The key the sidecar expects. Defaults to $MUI_I3_API_KEY.
          --opt-out <host[:port]> Record that somebody asked us to stop crawling them (§11), and
                                  exit. Needs --because. A bare host covers every port on it.
          --because <text>        Who asked and how. Required with --opt-out, and required because
                                  this is a claim about somebody else's wishes.
          --opt-out-check <h[:p]> Ask DNS whether that address has published an opt-out record, print
                                  what we read, and exit. Touches no database and no game server.
          --submissions           List the submitted games §7.8's rubric did not publish, and exit.
                                  These answered and are listed; no probe of ours could show they
                                  are games. Run mui-probe against one to see what it says now.
          --release <slug>        Publish one of them by hand, recorded as staff rather than as a
                                  measurement, and exit. Needs --because.
          -v, --verbose           Debug logging.
          -h, --help              This.

        Seeding is idempotent: an address already in the registry keeps its own schedule and is not
        dragged forward, so re-running this does not turn a restart into a burst of traffic at
        somebody else's server.
        """;

    public string? Connection { get; init; } = Environment.GetEnvironmentVariable("MUI_CRAWL_POSTGRES");

    public IReadOnlyList<CrawlSeed> Seeds { get; init; } = [];

    /// <summary>
    /// What this crawl tells the servers it dials about who is dialling them (spec §11).
    /// </summary>
    /// <remarks>
    /// Read from the same variable the deployable reads, because <c>mui-crawl</c> opens sockets to
    /// other people's machines exactly as the site does and an admin cannot tell the two apart from
    /// their logs. It is a placeholder when nothing is set, which is honest for a dry run on a
    /// laptop and would be rude against a real crawl — <c>Program</c> says so on the way past.
    /// </remarks>
    public string InfoUrl { get; init; } =
        Environment.GetEnvironmentVariable("MUI_CRAWL_INFO_URL") is { Length: > 0 } url
            ? url
            : new ProbeOptions().InfoUrl;

    public int Cycles { get; init; } = 1;

    public int Batch { get; init; } = 50;

    public int Concurrency { get; init; } = 8;

    public bool FollowReferrals { get; init; } = true;

    public bool DryRun { get; init; }

    /// <summary>Run one Intermud-3 pass rather than a crawl cycle.</summary>
    public bool I3 { get; init; }

    public string I3Gateway { get; init; } = "127.0.0.1:8081";

    public string? I3Key { get; init; } = Environment.GetEnvironmentVariable("MUI_I3_API_KEY");

    /// <summary>An address somebody has asked us to stop crawling (spec §11).</summary>
    public CrawlAddress? OptOut { get; init; }

    /// <summary>
    /// Who asked, and how.
    /// </summary>
    /// <remarks>
    /// Required with <see cref="OptOut"/> and deliberately not defaulted. Recording a request is this
    /// deployment's operator making a claim about somebody else's wishes, which is the exact shape of
    /// the <c>ContactedMaintainer</c> defect: a gate like that is satisfied by a caller who can make
    /// the claim, never by a default.
    /// </remarks>
    public string? Because { get; init; }

    /// <summary>Print the submissions awaiting a decision (spec §7.8) and exit.</summary>
    public bool Submissions { get; init; }

    /// <summary>The slug of a submission to publish by hand (spec §7.8), or null.</summary>
    public string? Release { get; init; }

    /// <summary>An address to ask DNS about, without touching a database or a game server.</summary>
    public CrawlAddress? OptOutCheck { get; init; }

    public bool Verbose { get; init; }

    public bool Help { get; init; }

    public static Arguments Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var parsed = new Arguments();
        var seeds = new List<CrawlSeed>();

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-h" or "--help":
                    return parsed with { Help = true };

                case "-v" or "--verbose":
                    parsed = parsed with { Verbose = true };
                    break;

                case "--dry-run":
                    parsed = parsed with { DryRun = true };
                    break;

                case "--i3":
                    parsed = parsed with { I3 = true };
                    break;

                case "--i3-gateway":
                    parsed = parsed with { I3Gateway = Next(args, ref i, "--i3-gateway") };
                    break;

                case "--i3-key":
                    parsed = parsed with { I3Key = Next(args, ref i, "--i3-key") };
                    break;

                case "--no-referrals":
                    parsed = parsed with { FollowReferrals = false };
                    break;

                case "--connection":
                    parsed = parsed with { Connection = Next(args, ref i, "--connection") };
                    break;

                case "--info-url":
                    parsed = parsed with { InfoUrl = Next(args, ref i, "--info-url") };
                    break;

                case "--seed":
                    seeds.Add(CrawlSeed.Parse(Next(args, ref i, "--seed"), isOperatorSeed: false));
                    break;

                case "--seed-exempt":
                    seeds.Add(CrawlSeed.Parse(Next(args, ref i, "--seed-exempt"), isOperatorSeed: true));
                    break;

                case "--cycles":
                    parsed = parsed with { Cycles = Number(args, ref i, "--cycles") };
                    break;

                case "--batch":
                    parsed = parsed with { Batch = Number(args, ref i, "--batch") };
                    break;

                case "--concurrency":
                    parsed = parsed with { Concurrency = Number(args, ref i, "--concurrency") };
                    break;

                case "--opt-out":
                    parsed = parsed with { OptOut = ParseAddress(Next(args, ref i, "--opt-out")) };
                    break;

                case "--submissions":
                    parsed = parsed with { Submissions = true };
                    break;

                case "--release":
                    parsed = parsed with { Release = Next(args, ref i, "--release") };
                    break;

                case "--because":
                    parsed = parsed with { Because = Next(args, ref i, "--because") };
                    break;

                case "--opt-out-check":
                    parsed = parsed with { OptOutCheck = ParseAddress(Next(args, ref i, "--opt-out-check")) };
                    break;

                default:
                    throw new ArgumentException($"Unrecognised argument '{args[i]}'.{Environment.NewLine}{Usage}");
            }
        }

        if (parsed.OptOut is not null && string.IsNullOrWhiteSpace(parsed.Because))
        {
            throw new ArgumentException(
                $"--opt-out needs --because: say who asked and how.{Environment.NewLine}{Usage}");
        }

        // The same rule as --opt-out's, for the same reason. A release publishes somebody's game on
        // our judgement rather than on a measurement, and a judgement nobody wrote down beside the
        // row is one nobody can review later.
        if (parsed.Release is not null && string.IsNullOrWhiteSpace(parsed.Because))
        {
            throw new ArgumentException(
                $"--release needs --because: say what convinced you.{Environment.NewLine}{Usage}");
        }

        // Before a socket rather than after one: an address nobody can open is worth catching while
        // the person who typed it is still looking at the terminal.
        new ProbeOptions { InfoUrl = parsed.InfoUrl }.Validate();

        return parsed with { Seeds = seeds };
    }

    /// <summary>
    /// <c>host</c> or <c>host:port</c>, where leaving the port off means every port on that host.
    /// </summary>
    /// <remarks>
    /// A bare IPv6 literal is read as a host rather than as an address with a port, because
    /// <c>2001:db8::1</c> ends in something that parses as a number and guessing wrong here would file
    /// an opt-out under an address nobody dials.
    /// </remarks>
    private static CrawlAddress ParseAddress(string value)
    {
        var text = value.Trim();

        if (text.Length == 0)
        {
            throw new ArgumentException("An address is needed.");
        }

        if (text.StartsWith('[') && text.Contains("]:", StringComparison.Ordinal))
        {
            var bracket = text.IndexOf("]:", StringComparison.Ordinal);
            return new CrawlAddress(text[1..bracket], Port(text[(bracket + 2)..]));
        }

        if (System.Net.IPAddress.TryParse(text.Trim('[', ']'), out var literal))
        {
            return new CrawlAddress(literal.ToString(), null);
        }

        var colon = text.LastIndexOf(':');

        return colon > 0
            ? new CrawlAddress(text[..colon], Port(text[(colon + 1)..]))
            : new CrawlAddress(text, null);

        static int Port(string text) =>
            int.TryParse(text, out var port) && port is >= 1 and <= 65535
                ? port
                : throw new ArgumentException($"'{text}' is not a port.");
    }

    private static string Next(string[] args, ref int i, string name) =>
        ++i < args.Length ? args[i] : throw new ArgumentException($"{name} needs a value.");

    private static int Number(string[] args, ref int i, string name) =>
        int.TryParse(Next(args, ref i, name), out var value) && value > 0
            ? value
            : throw new ArgumentException($"{name} needs a positive number.");
}

/// <summary>One address, with the port optional — "every port on this host" is a thing to be able to say.</summary>
public sealed record CrawlAddress(string Host, int? Port)
{
    public override string ToString() => Port is { } port ? $"{Host}:{port}" : $"{Host} (every port)";
}
