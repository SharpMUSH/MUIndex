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
          --no-referrals          Do not follow MSSP REFERRAL. Makes this a status checker.
          --dry-run               Print what is due and write nothing.
          -v, --verbose           Debug logging.
          -h, --help              This.

        Seeding is idempotent: an address already in the registry keeps its own schedule and is not
        dragged forward, so re-running this does not turn a restart into a burst of traffic at
        somebody else's server.
        """;

    public string? Connection { get; init; } = Environment.GetEnvironmentVariable("MUI_CRAWL_POSTGRES");

    public IReadOnlyList<CrawlSeed> Seeds { get; init; } = [];

    public int Cycles { get; init; } = 1;

    public int Batch { get; init; } = 50;

    public int Concurrency { get; init; } = 8;

    public bool FollowReferrals { get; init; } = true;

    public bool DryRun { get; init; }

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

                case "--no-referrals":
                    parsed = parsed with { FollowReferrals = false };
                    break;

                case "--connection":
                    parsed = parsed with { Connection = Next(args, ref i, "--connection") };
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

                default:
                    throw new ArgumentException($"Unrecognised argument '{args[i]}'.{Environment.NewLine}{Usage}");
            }
        }

        return parsed with { Seeds = seeds };
    }

    private static string Next(string[] args, ref int i, string name) =>
        ++i < args.Length ? args[i] : throw new ArgumentException($"{name} needs a value.");

    private static int Number(string[] args, ref int i, string name) =>
        int.TryParse(Next(args, ref i, name), out var value) && value > 0
            ? value
            : throw new ArgumentException($"{name} needs a positive number.");
}
