using MUI.Crawl;
using MUI.Crawler;

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

          --connection <string>   PostgreSQL connection string. Defaults to $MUI_CRAWL_POSTGRES, and
                                  then to $MUI_POSTGRES, which is what the deployable reads — so
                                  this runs inside the deployment with no connection string typed.
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
          --ares                  Run one AresCentral pass instead of a crawl cycle: read the
                                  hub's games list, seed new addresses, record what it says.
          --ares-client-id <s>    The client id AresCentral issued. Defaults to $MUI_ARES_CLIENT_ID.
          --ares-key <string>     The key that goes with it. Defaults to $MUI_ARES_API_KEY.
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
          --mint-now              Consider every game for a re-mint with §5.7's grace set to zero,
                                  and exit. Waives the wait and nothing else: a name a source has
                                  never declared, a codebase default and an owner's override are
                                  refused here exactly as they are on a normal cycle. Needs
                                  --because. Every slug it retires still redirects for ever.
          --merge <winner> <loser>
                                  Resolve a suspected-duplicate pair by hand: absorb <loser> into
                                  <winner> and exit. Each may be a slug or a game id. If an open
                                  duplicate_review row names this pair its score and signals carry
                                  onto the merge log and the row is closed; otherwise the merge is
                                  recorded as a judgement with no signals (spec §7.3, migration
                                  0018). Reversible later by hand against merge_log — nothing here
                                  moves an endpoint, a field or any history, only the redirect.
                                  Needs --because.
          --rename <slug> <newName>
                                  Rename a game and mint it a new, unique slug at once, and exit.
                                  <slug> may be a slug or a game id. Writes NAME as staff first (so
                                  the value has provenance and reaches the change feed), then takes
                                  the same immediate, no-grace mint-and-rename path a verified
                                  owner's own rename does (spec §5.7) — not the fourteen-day grace a
                                  measured rename waits for. The old slug redirects to the new page
                                  for ever. Needs --because.
          -v, --verbose           Debug logging.
          -h, --help              This.

        Seeding is idempotent: an address already in the registry keeps its own schedule and is not
        dragged forward, so re-running this does not turn a restart into a burst of traffic at
        somebody else's server.
        """;

    /// <summary>
    /// Where the catalogue is.
    /// </summary>
    /// <remarks>
    /// <c>MUI_CRAWL_POSTGRES</c> first, so pointing this at a scratch database doesn't require unsetting
    /// the deployment's variable; then <c>MUI_POSTGRES</c> (what <c>MUI.Web</c> reads), for running
    /// this inside the deployment without pasting a secret onto a command line.
    /// </remarks>
    public string? Connection { get; init; } =
        Environment.GetEnvironmentVariable("MUI_CRAWL_POSTGRES")
        ?? Environment.GetEnvironmentVariable("MUI_POSTGRES");

    public IReadOnlyList<CrawlSeed> Seeds { get; init; } = [];

    /// <summary>
    /// What this crawl tells the servers it dials about who is dialling them (spec §11).
    /// </summary>
    /// <remarks>
    /// Read from the same variable the deployable reads, since <c>mui-crawl</c> opens sockets to other
    /// people's machines exactly as the site does. A placeholder when nothing is set — honest for a
    /// dry run, rude against a real crawl (<c>Program</c> warns on the way past).
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

    /// <summary>Run one AresCentral pass instead of a crawl cycle.</summary>
    public bool Ares { get; init; }

    /// <summary>
    /// The client id and key AresCentral issued, defaulting to the environment.
    /// </summary>
    /// <remarks>
    /// From the environment by default so an operator inside the deployment runs a pass without
    /// pasting a credential into their shell history — the container already holds both.
    /// </remarks>
    public string? AresClientId { get; init; } =
        Environment.GetEnvironmentVariable("MUI_ARES_CLIENT_ID");

    /// <inheritdoc cref="AresClientId"/>
    public string? AresKey { get; init; } = Environment.GetEnvironmentVariable("MUI_ARES_API_KEY");

    /// <summary>An address somebody has asked us to stop crawling (spec §11).</summary>
    public CrawlAddress? OptOut { get; init; }

    /// <summary>
    /// Who asked, and how.
    /// </summary>
    /// <remarks>
    /// Required with <see cref="OptOut"/> and deliberately not defaulted: recording a request is this
    /// operator making a claim about somebody else's wishes, and a gate like that is satisfied by a
    /// caller who can make the claim, never by a default.
    /// </remarks>
    public string? Because { get; init; }

    /// <summary>Print the submissions awaiting a decision (spec §7.8) and exit.</summary>
    public bool Submissions { get; init; }

    /// <summary>The slug of a submission to publish by hand (spec §7.8), or null.</summary>
    public string? Release { get; init; }

    /// <summary>
    /// Consider every game for a re-mint with §5.7's grace waived, and exit.
    /// </summary>
    /// <remarks>
    /// The grace answers "is this name settled or flapping"; a person reading the change feed can
    /// answer that for the whole catalogue in one sitting. That's the only judgement this switch
    /// carries — <see cref="SlugMinter"/> does the rest, so a codebase default or an owner's override
    /// is refused here exactly as on a normal cycle.
    /// </remarks>
    public bool MintNow { get; init; }

    /// <summary>
    /// The pair to resolve by hand, winner named first (spec §7.3). Null when nothing was asked.
    /// </summary>
    public MergeRequest? Merge { get; init; }

    /// <summary>The game to rename and the name to give it (spec §5.7). Null when nothing was asked.</summary>
    public RenameRequest? Rename { get; init; }

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

                case "--ares":
                    parsed = parsed with { Ares = true };
                    break;

                case "--ares-client-id":
                    parsed = parsed with { AresClientId = Next(args, ref i, "--ares-client-id") };
                    break;

                case "--ares-key":
                    parsed = parsed with { AresKey = Next(args, ref i, "--ares-key") };
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
                    parsed = parsed with { OptOut = CrawlAddress.Parse(Next(args, ref i, "--opt-out")) };
                    break;

                case "--submissions":
                    parsed = parsed with { Submissions = true };
                    break;

                case "--release":
                    parsed = parsed with { Release = Next(args, ref i, "--release") };
                    break;

                case "--mint-now":
                    parsed = parsed with { MintNow = true };
                    break;

                case "--merge":
                    var winner = Next(args, ref i, "--merge");
                    var loser = Next(args, ref i, "--merge");
                    parsed = parsed with { Merge = new MergeRequest(winner, loser) };
                    break;

                case "--rename":
                    var renameSlug = Next(args, ref i, "--rename");
                    var renameName = Next(args, ref i, "--rename");
                    parsed = parsed with { Rename = new RenameRequest(renameSlug, renameName) };
                    break;

                case "--because":
                    parsed = parsed with { Because = Next(args, ref i, "--because") };
                    break;

                case "--opt-out-check":
                    parsed = parsed with { OptOutCheck = CrawlAddress.Parse(Next(args, ref i, "--opt-out-check")) };
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

        // Same rule as --opt-out's: a release publishes on our judgement, not a measurement, and an
        // unrecorded judgement can't be reviewed later.
        if (parsed.Release is not null && string.IsNullOrWhiteSpace(parsed.Because))
        {
            throw new ArgumentException(
                $"--release needs --because: say what convinced you.{Environment.NewLine}{Usage}");
        }

        // Same rule again: waiving the grace mints URLs the catalogue keeps forever, on somebody's
        // reading of the change feed rather than the passage of time.
        if (parsed.MintNow && string.IsNullOrWhiteSpace(parsed.Because))
        {
            throw new ArgumentException(
                $"--mint-now needs --because: say what settled these names.{Environment.NewLine}{Usage}");
        }

        // Same rule again: a merge folds a live listing into another, and the reason belongs on the
        // row beside the score and signals.
        if (parsed.Merge is not null && string.IsNullOrWhiteSpace(parsed.Because))
        {
            throw new ArgumentException(
                $"--merge needs --because: say what convinced you these are one game.{Environment.NewLine}{Usage}");
        }

        // The fifth. A rename mints a URL the catalogue keeps for ever, on somebody's judgement that
        // the name has settled rather than on §5.7's usual fourteen-day wait; the reason belongs
        // beside the row it lands on, same as the other three consequential writes above.
        if (parsed.Rename is not null && string.IsNullOrWhiteSpace(parsed.Because))
        {
            throw new ArgumentException(
                $"--rename needs --because: say why this name is worth a new URL.{Environment.NewLine}{Usage}");
        }

        // Before a socket rather than after one: an address nobody can open is worth catching while
        // the person who typed it is still looking at the terminal.
        new ProbeOptions { InfoUrl = parsed.InfoUrl }.Validate();

        return parsed with { Seeds = seeds };
    }

    private static string Next(string[] args, ref int i, string name) =>
        ++i < args.Length ? args[i] : throw new ArgumentException($"{name} needs a value.");

    private static int Number(string[] args, ref int i, string name) =>
        int.TryParse(Next(args, ref i, name), out var value) && value > 0
            ? value
            : throw new ArgumentException($"{name} needs a positive number.");
}

/// <summary>
/// A pair to resolve by hand, exactly as typed — a slug or a game id, either identifier read the same
/// way <see cref="Program"/> already reads one for any other operator surface.
/// </summary>
public sealed record MergeRequest(string Winner, string Loser);

/// <summary>A game to rename and the name to give it, exactly as typed (spec §5.7).</summary>
public sealed record RenameRequest(string Slug, string NewName);
