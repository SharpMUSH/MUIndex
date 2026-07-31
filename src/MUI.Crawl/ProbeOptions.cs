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
    /// <para>
    /// The probe used to spend a flat three seconds waiting for the connect screen and another three
    /// for the <c>WHO</c> reply, which is fine for one server and wrong for a fleet: it made every
    /// probe cost six seconds whether the game answered in eighty milliseconds or not at all. Most do
    /// answer in well under a second, so settling on a gap rather than a stopwatch is most of the
    /// crawl budget back.
    /// </para>
    /// <para>
    /// The gap is measured between <em>lines</em>, which is the only arrival signal a line-oriented
    /// callback gives. That is sound because a server's last line lands in the same breath as its
    /// second-to-last; what it cannot see is a trailing line the server never terminated, which is
    /// why every phase ends by flushing one (see <see cref="TelnetProbe"/>).
    /// </para>
    /// </remarks>
    public TimeSpan QuietPeriod { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// How long to wait for a phase to produce anything at all before concluding it never will.
    /// </summary>
    /// <remarks>
    /// Longer than <see cref="QuietPeriod"/> and for a different reason: a gap between lines means
    /// the server has finished, while silence from the start means it has not begun, and a game
    /// behind a slow link has not said no just because it has not yet said anything. A server with
    /// no connect screen at all — measured on <c>bigdamn.com:7777</c> — pays this once.
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
    /// <b>Some servers announce that they are not ready, and are then quiet for longer than a gap
    /// between lines.</b> <c>tbamud.com:4000</c> sends <c>Attempting to Detect Client, Please
    /// Wait...</c>, negotiates, and paints its real screen about a second and a half later. Under a
    /// 500 ms quiet period that one placeholder line <em>was</em> the connect screen: it became the
    /// game's stored banner and its hash became the game's identity, so two unrelated tbaMUDs
    /// fingerprinted identically and the second was held in a duplicate review rather than listed.
    /// A referral crawl reaching the second one is what surfaced this.
    /// </para>
    /// <para>
    /// The patience is conditional on the screen being nearly empty, so it costs nothing on the
    /// common case — a server that has already painted a real screen settles at the quiet period as
    /// before. <see cref="MaxPhase"/> still bounds the phase either way, and a server with no
    /// connect screen at all pays this once, like <see cref="SilenceGrace"/>.
    /// </para>
    /// </remarks>
    public TimeSpan BannerPatience { get; init; } = TimeSpan.FromMilliseconds(2500);

    // There is deliberately no option here for the plaintext MSSP-REQUEST form. It belongs in
    // TelnetNegotiationCore, where it is filed as issue #61, and a compensating implementation here
    // would duplicate a first-party dependency and then have to be deleted when that lands.
    //
    // The measurements are in docs/codebase-survey-2026-07-30.md and they say the same thing: of
    // twenty games asked directly, the three that answered — CoffeeMUD, NarutoMUD and Riftforge —
    // all answer telnet option 70 as well, so the plaintext form reached nothing the option did not.
    // Eight others read the request as a character name and spent one of a stranger's login attempts
    // on it.

    /// <summary>
    /// Ceiling on a single subnegotiation payload, handed to TelnetNegotiationCore's
    /// <c>WithMaxMessageSize</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately well below the library's 1 MiB default. A real MSSP report is a few kilobytes —
    /// the specification's whole official vocabulary is 45 variables — so 128 KiB is roughly two
    /// orders of magnitude of headroom for anything legitimate, while bounding what a hostile or
    /// broken server can make us allocate. A crawler connects to servers it does not trust by
    /// definition, which is precisely the case the library's own release notes call out.
    /// </para>
    /// <para>
    /// At the ceiling the report is <b>dropped, not truncated</b>, and surfaces as
    /// <see cref="MsspOutcome.RejectedTooLarge"/>. That must never be recorded as an absent report.
    /// </para>
    /// </remarks>
    public int MaxSubnegotiationBytes { get; init; } = 128 * 1024;

    /// <summary>
    /// What the crawler calls itself over TTYPE/MTTS and MNES <c>CLIENT_NAME</c> (spec §11).
    /// </summary>
    /// <remarks>
    /// <para>
    /// An admin reading their logs must be able to find out who we are and how to opt out, so this
    /// is a politeness obligation rather than a cosmetic string. It carries a URL for the same
    /// reason.
    /// </para>
    /// <para>
    /// The list is passed verbatim to <c>TerminalTypeProtocol.WithTerminalTypes</c>, which controls
    /// the sequence of TTYPE responses. Per MTTS convention the first entry is the client name, the
    /// second is the terminal type, and the third is the MTTS bitvector. The first entry is also
    /// passed to <c>WithClientIdentity</c>, which feeds the MNES <c>CLIENT_NAME</c> variable that
    /// <c>NewEnvironProtocol</c> answers when a server asks.
    /// </para>
    /// </remarks>
    public IReadOnlyList<string> TerminalTypes { get; init; } =
        ["MUINDEX-CRAWLER", "MUINDEX", "MTTS 9"];

    /// <summary>Where an admin can read what we do and ask us to stop.</summary>
    /// <remarks>
    /// A placeholder domain, because the domain is an open question (spec §15.1) and inventing one
    /// here would settle it by accident. A deployment that leaves this alone is publishing an address
    /// that answers nobody. <c>/about</c> compares against this default and says so when it matches.
    /// </remarks>
    public string InfoUrl { get; init; } = "https://muindex.example/crawler";
}
