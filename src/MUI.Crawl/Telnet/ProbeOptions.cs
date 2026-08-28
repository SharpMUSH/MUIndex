namespace MUI.Crawl;

/// <summary>Tuning for a single probe. Every bound here exists because a stranger chose the input.</summary>
public sealed record ProbeOptions
{
    /// <summary>How long the whole session may take before it is abandoned.</summary>
    /// <remarks>
    /// A hard bound, not a courtesy. The crawler runs in-process with the web tier, so a probe that
    /// wedges against a black-holed host would otherwise starve request threads (spec §12).
    /// </remarks>
    /// <remarks>
    /// Raised from 20s when <see cref="WhoGrace"/> was introduced: the worst-case run of graces is now
    /// about 16.5s (banner 2.5 + patience 2.5 + flush 0.5 + WHO 6 + INFO 2.5 + VERSION 2.5), and a
    /// budget that expires mid-session is not a soft landing — it leaves the <c>try</c> as an
    /// <see cref="OperationCanceledException"/>, which is deliberately not one of the
    /// <c>HungUp</c> shapes, so the whole probe is recorded <c>Failed</c> and a connect screen already
    /// in hand is thrown away. The headroom is what keeps a slow-but-answering server from being
    /// published as unreachable.
    /// </remarks>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(25);

    /// <summary>
    /// How long the server must say nothing before a phase is treated as finished.
    /// </summary>
    /// <remarks>
    /// Settling on a gap between <em>lines</em> rather than a flat wait means most probes finish well
    /// under a second instead of paying a fixed cost regardless of how fast the game answers. This is
    /// sound because a server's last line lands in the same breath as its second-to-last; what it
    /// cannot see is a trailing line the server never terminated, which is why every phase ends by
    /// flushing one (see <see cref="TelnetProbe"/>).
    /// </remarks>
    public TimeSpan QuietPeriod { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// How long a server must stay silent on an unterminated line before it is taken as a prompt.
    /// </summary>
    /// <remarks>
    /// Handed to TelnetNegotiationCore's <c>PacketPatchProtocol</c>, which does the holding on its
    /// own byte-processing loop. 500 ms is the library's default and the convention the hobby's
    /// clients settled on — TinTin++'s packet patch and Mudlet's posting timer both use it. Shorter
    /// would split a line at any server that pauses mid-output, so a phase waits this out rather
    /// than racing it, and only when the library says it is holding a line.
    /// </remarks>
    public TimeSpan PromptHold { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// How long to wait for a phase to produce anything at all before concluding it never will.
    /// </summary>
    /// <remarks>
    /// Longer than <see cref="QuietPeriod"/> and for a different reason: a gap between lines means
    /// the server has finished, while silence from the start means it has not begun. A server with
    /// no connect screen at all pays this once.
    /// </remarks>
    public TimeSpan SilenceGrace { get; init; } = TimeSpan.FromMilliseconds(2500);

    /// <summary>
    /// How long to wait for a <c>WHO</c> answer specifically, before concluding the server has none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Longer than <see cref="SilenceGrace"/> because some codebases throttle login-screen commands on
    /// purpose. <c>twyst.org:3333</c> and <c>rupert.twyst.org:6666</c> — two EW-too talkers — both
    /// answer <c>WHO</c> after <b>5.05 seconds</b>, identical to two decimal places, which is a
    /// deliberate delay rather than network weather. Under one grace for every phase the probe gave up
    /// at 2.5s and sent <c>INFO</c>, and the roster arrived inside the <c>INFO</c> window: the count
    /// was lost, and a <c>WHO</c> table was recorded as the game's <c>INFO</c> block — a fact about our
    /// timing published as a fact about their server (rule 5).
    /// </para>
    /// <para>
    /// This is spent only where it buys something. A phase that produces even one line settles on
    /// <see cref="QuietPeriod"/> instead, so a game that answers promptly pays nothing; the cost falls
    /// on games with no <c>WHO</c> at all, and it is why <c>WHO</c> has this and <c>INFO</c>/
    /// <c>VERSION</c> do not — <c>WHO</c> is the top rung of the count ladder (spec §5.2) and the other
    /// two are read only when it and MSSP have already failed.
    /// </para>
    /// </remarks>
    public TimeSpan WhoGrace { get; init; } = TimeSpan.FromMilliseconds(6000);

    /// <summary>
    /// The ceiling on any one phase, so a server that talks forever cannot outlast
    /// <see cref="QuietPeriod"/> indefinitely.
    /// </summary>
    /// <remarks>
    /// A phase that keeps producing lines never goes quiet, so quiet-period settling on its own has
    /// no upper bound. <see cref="Timeout"/> still bounds the whole session; this bounds one part of
    /// it, so a chatty banner cannot eat the budget the <c>WHO</c> reply needs.
    /// </remarks>
    public TimeSpan MaxPhase { get; init; } = TimeSpan.FromSeconds(8);

    /// <summary>
    /// How often the settle loop looks for new output. Purely a resolution knob.
    /// </summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// A connect screen this slight, once the phase has gone quiet, is treated as unfinished rather
    /// than as the screen.
    /// </summary>
    /// <remarks>
    /// Measured in flattened characters — escape sequences stripped, whitespace collapsed — because
    /// a screen can be a kilobyte of colour and one word of text.
    /// </remarks>
    public int SlightBannerLength { get; init; } = 120;

    /// <summary>
    /// Extra time the connect-screen phase may spend while what it has is no longer than
    /// <see cref="SlightBannerLength"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Some servers announce they are not ready and then go quiet for longer than one line-gap —
    /// <c>tbamud.com:4000</c> sends <c>Attempting to Detect Client, Please Wait...</c>, negotiates,
    /// and paints its real screen about 1.5s later. Under a 500ms quiet period that placeholder line
    /// became the stored banner, and its hash became the game's identity: two unrelated tbaMUDs
    /// fingerprinted identically, and the second was held as a duplicate rather than listed.
    /// </para>
    /// <para>
    /// The patience is conditional on the screen being nearly empty, so it costs nothing once a
    /// server has already painted a real screen. <see cref="MaxPhase"/> still bounds the phase
    /// either way, and a server with no connect screen at all pays this once, like
    /// <see cref="SilenceGrace"/>.
    /// </para>
    /// </remarks>
    public TimeSpan BannerPatience { get; init; } = TimeSpan.FromMilliseconds(2500);

    /// <summary>
    /// A bounded backstop: how long the residue flush waits for a <c>WILL MSSP</c> delayed by the
    /// network before deciding nothing was negotiated.
    /// </summary>
    /// <remarks>
    /// The real fix is <c>Watched.Mssp.OnNegotiationChangedAsync</c> noting <c>MSSP</c> the instant
    /// TelnetNegotiationCore's own state machine confirms the real <c>WILL MSSP</c> — long before this
    /// line runs in the ordinary case, since that happens during initial connect negotiation and the
    /// flush decision comes after the banner has settled, any pre-login prompts are answered, and any
    /// who's-online menu is tried. This grace only matters if the network itself delays that exchange
    /// past all of that — the flush decision used to read <c>seen.Supported</c> at whatever instant it
    /// happened to reach it, with no allowance for a delayed <c>WILL</c> at all. A server slow enough
    /// for the gap to matter is exactly the kind that hangs up on the flush's blank line (see
    /// <see cref="TelnetProbe.AskFollowUpsAsync"/>), so losing the race did not mean "no measurement
    /// this cycle" — it meant the flush ending the session before negotiation the game was already
    /// completing could be observed, and <c>FieldObservations.Measured</c> then wrote the
    /// honest-negative <c>false</c> for a game that does offer MSSP. Measured in production as
    /// <c>capability.mssp.measured</c> flapping true/false across ordinary crawl cycles for DIKU-family
    /// games (God Wars Legends, GodWars: Apocalypse) that answer MSSP cleanly every time when probed
    /// directly. Paid only by the narrow population already about to be flushed blind — every game that
    /// has negotiated anything else by this point already skips the wait, the same way it already
    /// skips the flush.
    /// </remarks>
    public TimeSpan MsspSettleGrace { get; init; } = TimeSpan.FromMilliseconds(1500);

    /// <summary>
    /// How many pre-login prompts (colour, charset menu, press-enter, age-gate) one probe will answer
    /// in a row before treating whatever it has as the connect screen.
    /// </summary>
    /// <remarks>
    /// A misclassified screen must not be able to spin the probe through its whole <see
    /// cref="MaxPhase"/> answering itself — bounded well above anything measured (New Haven stacks a
    /// colour question and a press-enter gate; nothing surveyed stacks more than two), so an honest
    /// multi-step gate still resolves and a runaway false positive still stops quickly.
    /// </remarks>
    public int MaxPromptRounds { get; init; } = 4;

    // Deliberately no option here for the plaintext MSSP-REQUEST form — it belongs in
    // TelnetNegotiationCore, and a compensating implementation here would duplicate a first-party
    // dependency and then have to be deleted once that lands. Every surveyed game that answered the
    // plaintext form also answered telnet option 70, so it reached nothing extra; several others read
    // the bare request as a character name and burned a login attempt on it.

    /// <summary>
    /// Ceiling on a single subnegotiation payload, handed to TelnetNegotiationCore's
    /// <c>WithMaxMessageSize</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately well below the library's 1 MiB default: a real MSSP report is a few kilobytes
    /// (the spec's whole vocabulary is 45 variables), so this is roughly two orders of magnitude of
    /// headroom for anything legitimate while bounding what a hostile or broken server can make us
    /// allocate.
    /// </para>
    /// <para>
    /// At the ceiling the report is <b>dropped, not truncated</b>, and surfaces as
    /// <see cref="MsspOutcome.RejectedTooLarge"/> — never recorded as an absent report.
    /// </para>
    /// </remarks>
    public int MaxSubnegotiationBytes { get; init; } = 128 * 1024;

    /// <summary>
    /// What the crawler calls itself over TTYPE/MTTS and MNES <c>CLIENT_NAME</c> (spec §11).
    /// </summary>
    /// <remarks>
    /// <para>
    /// An admin reading their logs must be able to find out who we are and how to opt out — a
    /// politeness obligation, not a cosmetic string.
    /// </para>
    /// <para>
    /// Passed verbatim to <c>TerminalTypeProtocol.WithTerminalTypes</c>. Per MTTS convention the
    /// first entry is the client name, the second the terminal type, the third the MTTS bitvector.
    /// The first entry also feeds <c>WithClientIdentity</c>, which answers the MNES
    /// <c>CLIENT_NAME</c> variable.
    /// </para>
    /// </remarks>
    public IReadOnlyList<string> TerminalTypes { get; init; } =
        ["MUINDEX-CRAWLER", "MUINDEX", "MTTS 9"];

    /// <summary>Where an admin can read what we do and ask us to stop.</summary>
    /// <remarks>
    /// A placeholder, and it stays one: compiling this deployment's address in would make every fork
    /// and local run announce our contact page to servers it dials — a claim about somebody else's
    /// crawl, in the same shape as the <c>ContactedMaintainer</c> defect. The real address is a thing
    /// a deployment says (<c>MUI_CRAWL_INFO_URL</c>), never a default it inherits. <c>/about</c>
    /// compares against this default and says so on the page rather than letting it pass silently.
    /// </remarks>
    public string InfoUrl { get; init; } = "https://muindex.example/crawler";

    /// <summary>Throws on a setting that could only have come from a typo or a hand-edited file.</summary>
    /// <remarks>
    /// <see cref="InfoUrl"/> is the one setting here that <em>somebody else</em> reads — an admin who
    /// has just been dialled and wants to know by whom. A malformed one is not a degraded crawl but a
    /// crawl that cannot be complained to, so it is refused while a person is still watching the
    /// terminal. The scheme is pinned since we hand this address to a reader with no way to check
    /// what they're opening.
    /// </remarks>
    public void Validate()
    {
        if (!Uri.TryCreate(InfoUrl, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException(
                $"The crawler's contact address is '{InfoUrl}', which is not an absolute https URL. "
                + "It is announced to every server this crawler dials, so it has to be an address "
                + "somebody reading their logs can open.");
        }
    }
}
