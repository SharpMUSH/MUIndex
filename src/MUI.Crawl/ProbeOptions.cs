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
    /// <b>It does not reach the wire yet, and neither does <see cref="InfoUrl"/>.</b>
    /// TelnetNegotiationCore's <c>TerminalTypeProtocol</c> hardcodes a client's terminal types to
    /// <c>TNC</c>, <c>XTERM</c>, <c>MTTS 3853</c> in a private field with no setter, and its
    /// <c>NewEnvironProtocol</c> answers a server's NEW-ENVIRON request with the crawler host's own
    /// <c>USER</c> and a fixed <c>LANG</c> — so what an admin actually sees is the library's default
    /// and a local account name, not us. The library is first-party: the fix is a PR there making
    /// both settable, never a reflection hack or a hand-rolled plugin here.
    /// </para>
    /// <para>
    /// Until then nothing may claim otherwise. <c>/about</c> reads this field and says plainly that
    /// the crawler is <em>configured</em> to call itself this and does not manage to, because
    /// "the crawler identifies itself" is a claim about our own behaviour and that one would be
    /// false in exactly the way <c>ContactedMaintainer</c>'s default was.
    /// </para>
    /// </remarks>
    public IReadOnlyList<string> TerminalTypes { get; init; } =
        ["MUINDEX-CRAWLER", "MUINDEX", "MTTS 9"];

    /// <summary>
    /// Telnet options to request outright with <c>IAC DO</c> rather than waiting to be offered.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Waiting is not enough.</b> The MSSP specification says a server "should send
    /// <c>IAC WILL MSSP</c>" on connect, so a listen-only crawler assumes it will hear one. Many
    /// servers that support MSSP never volunteer anything and answer only when asked, which is why
    /// the protocol's own reference client (TinTin++'s <c>#session mssp</c>) asks rather than listens.
    /// </para>
    /// <para>
    /// Asking also makes a negative answer meaningful. Aardwolf, measured directly, offers MCCP1/2,
    /// ATCP, GMCP and its own option 102, and requests TTYPE and NAWS — but does not answer
    /// <c>IAC DO MSSP</c> at all. Because we asked, "no MSSP" there is a measurement rather than an
    /// assumption we never tested.
    /// </para>
    /// <para>
    /// This is negotiation, not traffic: <c>IAC DO</c> is the client half of the option handshake and
    /// is the only category of byte this probe puts on the wire beyond the pre-login <c>WHO</c>. Only
    /// MSSP is requested, because MSSP is the one option that exists specifically to be asked for by
    /// crawlers — the rest are observed if a server offers them and left alone if it does not.
    /// </para>
    /// </remarks>
    public IReadOnlyList<byte> RequestOptions { get; init; } = [MsspOption];

    /// <summary>The MSSP telnet option, 70.</summary>
    public const byte MsspOption = 70;

    /// <summary>Where an admin can read what we do and ask us to stop.</summary>
    /// <remarks>
    /// A placeholder domain, because the domain is an open question (spec §15.1) and inventing one
    /// here would settle it by accident. It is also not yet sent to anybody — see
    /// <see cref="TerminalTypes"/> — so a deployment that leaves this alone is publishing an address
    /// that answers nobody. <c>/about</c> compares against this default and says so when it matches.
    /// </remarks>
    public string InfoUrl { get; init; } = "https://muindex.example/crawler";
}
