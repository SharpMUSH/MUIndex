namespace MUI.Crawl;

/// <summary>Tuning for a single probe. Every bound here exists because a stranger chose the input.</summary>
public sealed record ProbeOptions
{
    /// <summary>How long the whole session may take before it is abandoned.</summary>
    /// <remarks>
    /// A hard bound, not a courtesy. The crawler runs in-process with the web tier, so a probe that
    /// wedges against a black-holed host would otherwise starve request threads (spec §12).
    /// </remarks>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(20);

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
    /// How long to wait for a phase to produce anything at all before concluding it never will.
    /// </summary>
    /// <remarks>
    /// Longer than <see cref="QuietPeriod"/> and for a different reason: a gap between lines means
    /// the server has finished, while silence from the start means it has not begun. A server with
    /// no connect screen at all pays this once.
    /// </remarks>
    public TimeSpan SilenceGrace { get; init; } = TimeSpan.FromMilliseconds(2500);

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
