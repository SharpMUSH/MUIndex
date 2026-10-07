using System.Net;

namespace MUI.Crawl;

/// <summary>What the crawler was asked to dial.</summary>
public sealed record ProbeTarget(string Host, int Port)
{
    /// <summary>
    /// The addresses the scope guard already resolved and vetted, when there are any.
    /// </summary>
    /// <remarks>
    /// Empty means "resolve the name yourself", for the on-demand and CLI paths. Filled by the crawl
    /// loop so the dial cannot re-resolve the name and reach an address the guard never ruled on —
    /// otherwise the guard checks one answer and the socket uses another.
    /// </remarks>
    public IReadOnlyList<IPAddress> Addresses { get; init; } = [];

    /// <summary>
    /// The encoding an operator has said this game's bytes are in, overriding what it declares.
    /// </summary>
    /// <remarks>
    /// Null on every target that has not needed one. Rides on the target rather than
    /// <see cref="ProbeOptions"/> because it is a fact about one game, not the whole crawl. See
    /// <see cref="WireEncoding"/> for why the crawler cannot work this out for itself.
    /// </remarks>
    public string? Charset { get; init; }

    /// <summary>
    /// The encoding an operator has said this game's MSSP report is in, when that is not the one its
    /// screen is in.
    /// </summary>
    /// <remarks>
    /// Null on all but the few that have needed one — world text is legacy when the game is, while a
    /// report is generally a config file somebody wrote in a modern editor, and nothing makes the two
    /// agree. Measured at <c>doom.twmuds.com:4000</c>: Big5 screen, UTF-8 report, one session. See
    /// <see cref="WireEncoding.Read"/>, including why a declared report stops voting on the screen.
    /// </remarks>
    public string? MsspCharset { get; init; }

    /// <summary>
    /// Whether this address still has to prove it is a game at all — a submission §7.8 has not yet
    /// corroborated.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one thing <c>WHO</c> is worth typing at a server that negotiates nothing and publishes no
    /// MSSP: for such a host a parseable <c>WHO</c> is its only protocol-tier <see cref="MuLikeness"/>
    /// signal, and without one a submitted game is never listed. See
    /// <c>TelnetProbe.PublishedCountAsync</c>, which is the only reader.
    /// </para>
    /// <para>
    /// Defaults to <c>true</c>, which is the cautious value: a caller that does not know keeps the
    /// probe asking. Only the crawl loop knows the answer, because only it has the catalogue — and
    /// the answer is narrow, because <see cref="MuLikeness"/> has exactly one consumer
    /// (<c>CatalogueBinder.CorroborateAsync</c>) and that one returns early for every game that is
    /// not an uncorroborated submission. For all the rest, typing <c>WHO</c> at a login prompt every
    /// thirty minutes re-proves a fact nothing reads.
    /// </para>
    /// <para>
    /// Rides on the target rather than <see cref="ProbeOptions"/> for the same reason
    /// <see cref="Charset"/> does: it is a fact about one game, not about the crawl.
    /// </para>
    /// </remarks>
    public bool AwaitingCorroboration { get; init; } = true;

    /// <summary>Whether to open this dial with a TLS handshake rather than in the clear.</summary>
    /// <remarks>
    /// <para>
    /// TLS is a transport <em>underneath</em> telnet, not a protocol beside it: the IAC bytes are
    /// still on the wire once the handshake completes, so MSSP, GMCP, MCCP and CHARSET all survive
    /// the wrapping and nothing above the socket knows the difference. Measured at
    /// <c>chatmud.com:7443</c> and <c>mud.drifters.world:3000</c> — a full connect screen with
    /// telnet negotiation inside the tunnel.
    /// </para>
    /// <para>
    /// False on a target nobody has established is TLS, which is every target until a probe finds
    /// out. A TLS listener says <em>nothing</em> until it is sent a ClientHello, so there is no
    /// banner to recognise it by and no passive detection is possible — see
    /// <c>TelnetProbe.ProbeAsync</c>, which retries a silent plaintext dial once over TLS, and
    /// <see cref="ProbeResult.Transport"/>, which is how the answer gets written down.
    /// </para>
    /// </remarks>
    public bool UseTls { get; init; }

    /// <summary>
    /// Whether, and how, to send the plaintext <c>MSSP-REQUEST</c> line this time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Defaults to <see cref="PlaintextMsspAsk.Never"/></b>, the opposite direction from
    /// <see cref="AwaitingCorroboration"/> and for the opposite reason: <c>IAC DO 70</c> is ignored by
    /// a server without MSSP, but this is a line of text at somebody's login prompt, and eight of the
    /// twenty games surveyed read it as a character name (<c>docs/codebase-survey-2026-07-30.md</c>).
    /// A caller that cannot say what this address has done before does not type it.
    /// </para>
    /// <para>
    /// Only the crawl loop answers it, from what the address did before (<c>crawl_target.mssp_route</c>,
    /// see <c>MsspRoutes</c>): an address that has ever reported over option 70, or failed to answer
    /// this line, is never asked; one that answered it is asked every session; one that has done
    /// neither gets one <see cref="PlaintextMsspAsk.Trial"/>.
    /// </para>
    /// </remarks>
    public PlaintextMsspAsk PlaintextMssp { get; init; } = PlaintextMsspAsk.Never;

    /// <summary>
    /// Whether this address is known to answer a pre-login <c>WHO</c> with a count.
    /// </summary>
    /// <remarks>
    /// <para>
    /// True means <c>WHO</c> is asked even where the game has already stated a count, and its count
    /// is the one published. A stated count is the game's claim; <c>WHO</c> is the live answer, and
    /// the two are not always the same number: <c>tapestries.fur.com:2069</c> answers the plaintext
    /// <c>MSSP-REQUEST</c> with <c>PLAYERS = 26842</c>, its player objects, while its <c>WHO</c>
    /// counts a few hundred.
    /// </para>
    /// <para>
    /// False by default, so a caller that cannot say keeps the restraint
    /// <c>TelnetProbe.PublishedCountAsync</c> describes. Only the crawl loop answers it, from
    /// <c>crawl_target.who_answers_at</c> (migration 0043).
    /// </para>
    /// </remarks>
    public bool WhoAnswers { get; init; }

    public override string ToString() => Host.Contains(':') ? $"[{Host}]:{Port}" : $"{Host}:{Port}";
}

/// <summary>What a probe does about the plaintext <c>MSSP-REQUEST</c> form.</summary>
/// <remarks>
/// The plaintext form is the SMAUG family's (spec §6.4); TelnetNegotiationCore carries it as
/// <c>MSSPPlaintextProtocol</c>. It is never sent in a session that negotiated option 70, whatever
/// this says: a game already reporting the proper way has nothing to add by the older one.
/// </remarks>
public enum PlaintextMsspAsk
{
    /// <summary>Not sent.</summary>
    Never,

    /// <summary>
    /// Sent once, on a second short dial of its own, after an ordinary session that got no MSSP.
    /// </summary>
    /// <remarks>
    /// A dial of its own because the measurement session must not pay for the experiment. A game
    /// that reads the line as a character name moves on to a password prompt, and every command after
    /// it, <c>WHO</c> included, is then typed into the wrong question. That would publish our
    /// experiment as an unreadable <c>WHO</c> (rule 5). The trial dial asks for nothing else and hangs
    /// up.
    /// </remarks>
    Trial,

    /// <summary>
    /// Sent in the ordinary session, at the connect screen and before <c>WHO</c>, because this address
    /// has answered it before.
    /// </summary>
    /// <remarks>
    /// Before <c>WHO</c> on purpose: the report usually carries <c>PLAYERS</c>, which is a count
    /// <c>PublishedCountAsync</c> accepts in place of typing <c>WHO</c> at all.
    /// </remarks>
    Ask,
}

/// <summary>
/// What carried a probe session. Part of the result's provenance, and the whole of what
/// <c>EndpointKind.Tls</c> is written from.
/// </summary>
/// <remarks>
/// <b>A TLS session proves a handshake completed, never that a certificate was trustworthy.</b> The
/// probe reads a public login screen and accepts any certificate, self-signed and expired ones
/// included, because refusing a chain would drop games without protecting anything we hold. Nothing
/// downstream may render <see cref="Tls"/> as an endorsement.
/// </remarks>
public enum ProbeTransport
{
    /// <summary>A plain TCP socket, which is how the hobby's ports overwhelmingly answer.</summary>
    Telnet,

    /// <summary>The same telnet session, inside a TLS tunnel.</summary>
    Tls,
}

/// <summary>
/// One telnet session against one target, producing exactly one <see cref="ProbeResult"/>.
/// </summary>
/// <remarks>
/// This is the seam the rest of the system is built against (spec §6.5). Everything downstream
/// consumes a <see cref="ProbeResult"/> and knows nothing about sockets, which is what makes it all
/// testable against captured fixtures.
/// </remarks>
public interface IProbe
{
    Task<ProbeResult> ProbeAsync(ProbeTarget target, CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads a pre-login <c>WHO</c> or <c>DOING</c> response structurally rather than per-codebase.
/// </summary>
/// <remarks>
/// Penn, MUX, Rhost and the TinyMUD family all let operators rewrite the <c>DOING</c> header in
/// softcode, so a dialect table is a treadmill that still loses to any game that customised it.
/// Locate the trailing "<c>N players logged in</c>" summary; failing that, count rows between the
/// header rule and the footer. <b>Never fabricate</b>: an unreadable response is
/// <see cref="WhoConfidence.Unknown"/>, never zero.
/// </remarks>
public interface IWhoParser
{
    WhoReading Parse(string? response);
}

/// <summary>
/// Why a probe never got as far as a result. Distinct from <see cref="FailureDetail"/>, which
/// describes a dial that was attempted and failed.
/// </summary>
public enum DialRefusal
{
    /// <summary>Not refused.</summary>
    None,

    /// <summary>
    /// The scope guard refused (spec §7.2). <b>This must never become a
    /// <see cref="ProbeResult"/>.</b> A refusal happens before a probe exists, and
    /// <c>FailureCause.Refused</c> already means the far end sent an RST — a real measurement of a
    /// real host. Conflating them puts our security policy into a game's public reachability history
    /// and is unrecoverable downstream.
    /// </summary>
    OutOfScope,

    /// <summary>The game asked not to be crawled (spec §11).</summary>
    OptedOut,
}
