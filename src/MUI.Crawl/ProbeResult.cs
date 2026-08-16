namespace MUI.Crawl;

/// <summary>
/// Everything one telnet connection to one game yielded. The single output of a probe and the seam
/// the rest of the system is built against (spec §6.5).
/// </summary>
/// <remarks>
/// The four layers below are not a fallback chain. One session produces the handshake and the banner
/// always, <c>WHO</c> usually, and MSSP either wholly or not at all — so a game that answers none of
/// the optional layers still yields measured capability data.
/// </remarks>
public sealed record ProbeResult
{
    public required string Host { get; init; }

    public required int Port { get; init; }

    public required DateTimeOffset ObservedAt { get; init; }

    public required ProbeOutcome Outcome { get; init; }

    /// <summary>
    /// Layer 1 — the telnet options the server actually offered. Measured, not claimed: a game whose
    /// MSSP says <c>GMCP 1</c> may simply be wrong, and the handshake cannot be (spec §6.1).
    /// </summary>
    public IReadOnlySet<string> OfferedOptions { get; init; } = new HashSet<string>();

    /// <summary>
    /// Layer 1, decoded — the full option exchange, including refusals, the charsets the server
    /// offered, and any MNES/MTTS behaviour it revealed by asking us questions.
    /// </summary>
    public Negotiation Negotiation { get; init; } = new();

    /// <summary>Layer 2 — the connect screen, ANSI intact. Display asset and codebase fingerprint both.</summary>
    public string? Banner { get; init; }

    /// <summary>
    /// Whether the server emitted MXP in anything it sent us.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="OfferedOptions"/> because it is a different observation. MXP is
    /// telnet option 91 and a server that negotiates it lands in that set like any other; a great
    /// many never negotiate and simply start emitting MXP, whose line-mode sequences are ANSI-legal
    /// and pass harmlessly through a client that has never heard of them. Both facts are worth
    /// having and they are not the same fact, so this one is recorded under the source that
    /// produced it rather than folded in beside the handshake's.
    /// </remarks>
    public bool MxpObserved { get; init; }

    /// <summary>Layer 3 — what login-screen commands yielded.</summary>
    /// <remarks>
    /// Defaults to <see cref="WhoReading.NotAsked"/> rather than to an unreadable answer, so a probe
    /// that failed before it could ask does not claim to have tried.
    /// </remarks>
    public WhoReading Who { get; init; } = WhoReading.NotAsked;

    /// <summary>
    /// The shape of the <c>WHO</c> response, for §11's replay window. Never its text.
    /// </summary>
    /// <remarks>
    /// Redacted inside the probe rather than downstream, so "redacted before it touches disk"
    /// is the stronger "redacted before it leaves the socket": the raw response exists as a local
    /// in one method and is never a member of anything. See <see cref="PayloadRedaction"/> for why
    /// a shape is what a replay needs and a name-finding redactor could not have produced one.
    /// </remarks>
    public string? WhoShape { get; init; }

    /// <summary>The reply to <c>INFO</c> at the login screen, when one arrived.</summary>
    public string? Info { get; init; }

    /// <summary>The reply to <c>VERSION</c> at the login screen, when one arrived.</summary>
    public string? Version { get; init; }

    /// <summary>
    /// Layer 4 — MSSP as the server reported it over telnet option 70. Every variable, every value,
    /// in wire order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A value is a list because MSSP says it is.</b> "The same variable can be sent more than
    /// once with different values", and two of the variables this project most depends on use that:
    /// <c>REFERRAL</c>, which is the entire basis of crawl discovery and which a game may publish
    /// several of, and <c>PORT</c>, which a game listening on 4201 and 4202 publishes twice. A flat
    /// <c>string</c> map kept one of them.
    /// </para>
    /// <para>
    /// Joining them into one string was worse than dropping them, which is what this replaced: a
    /// value may legitimately contain a comma, so <c>string.Join(", ", …)</c> manufactured something
    /// that looks like a value and cannot be split back apart — a fabrication, and the rule against
    /// those does not stop at player counts.
    /// </para>
    /// <para>
    /// <b>Nothing is filtered.</b> The probe used to hand-pick seven variables and discard the rest,
    /// including <c>REFERRAL</c>, <c>WEBSITE</c>, <c>PORT</c>, <c>SSL</c>, <c>LANGUAGE</c> and every
    /// name a codebase invented. A crawler whose premise is faithful measurement does not get to
    /// decide which of a server's own answers were worth keeping; deciding what to display is the
    /// catalogue's job, and it cannot display what was thrown away at the socket.
    /// </para>
    /// </remarks>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Mssp { get; init; } = MsspReport.Empty;

    /// <summary>
    /// The single value of an MSSP variable, or null when the server did not report it.
    /// </summary>
    /// <remarks>
    /// The convenience half of the pair, so a caller after <c>NAME</c> is no worse off than it was
    /// under a flat map. Where a variable carries several values this is the <em>last</em>, which is
    /// the specification's own rule for reducing one to a scalar ("the last reported value should be
    /// used as the default value"). Anything that cares about the others — discovery, reading
    /// <c>REFERRAL</c> — must read <see cref="Mssp"/> rather than this.
    /// </remarks>
    public string? MsspField(string variable) =>
        Mssp.TryGetValue(variable, out var values) && values.Count > 0 ? values[^1] : null;

    /// <summary>Every value of an MSSP variable, in wire order, or empty when it was not reported.</summary>
    public IReadOnlyList<string> MsspValues(string variable) =>
        Mssp.TryGetValue(variable, out var values) ? values : [];

    /// <summary>
    /// How layer 4 went. Empty <see cref="Mssp"/> is not self-explaining — see <see cref="MsspOutcome"/>.
    /// </summary>
    public MsspOutcome MsspOutcome { get; init; } = MsspOutcome.NotOffered;

    /// <summary>
    /// When <see cref="MsspOutcome"/> is <see cref="MsspOutcome.RejectedTooLarge"/>, how many bytes
    /// arrived before the report was dropped. Recorded so a limit can be tuned against real servers
    /// rather than guessed at.
    /// </summary>
    public int? MsspBytesRejected { get; init; }

    /// <summary>
    /// A player count the connect screen stated about itself, when it did.
    /// </summary>
    /// <remarks>
    /// The weakest of the three count sources and the only one that reaches a game with neither MSSP
    /// nor a pre-login <c>WHO</c> — Aardwolf being the case in point. Ranked last deliberately: it is
    /// pattern-matching a stranger's ASCII art, so it is read only when the other two have failed.
    /// </remarks>
    public int? BannerPlayerCount { get; init; }

    /// <summary>Which transport produced <see cref="Mssp"/>. Part of the value's provenance.</summary>
    public MsspTransport MsspTransport { get; init; } = MsspTransport.None;

    public FailureDetail? Failure { get; init; }

    public TimeSpan Elapsed { get; init; }
}

/// <summary>
/// What happened when MSSP was asked for. <b>Three outcomes, because an empty report has three
/// different meanings and only one of them is "this game has no MSSP".</b>
/// </summary>
/// <remarks>
/// TelnetNegotiationCore 2.7.0 bounds the MSSP payload and, at the ceiling, <b>drops the report
/// rather than truncating it</b> — a truncated report would be worse, since half a report parses
/// cleanly and lies. The drop is surfaced through <c>OnMSSPMessageTooLarge</c>, and the crawler must
/// carry it as its own outcome: recording a dropped report as an absent one would publish "this game
/// does not support MSSP" on the strength of our own size limit, which is a decision of ours
/// masquerading as a measurement of theirs.
/// </remarks>
public enum MsspOutcome
{
    /// <summary>The server never offered MSSP and did not answer the plaintext request.</summary>
    NotOffered,

    /// <summary>A report arrived and was parsed. It may still be empty, which is the server's answer.</summary>
    Received,

    /// <summary>
    /// A report arrived and exceeded the configured ceiling, so it was dropped whole. We asked, the
    /// server answered, and we chose not to hold it — <b>never render this as "no MSSP"</b>.
    /// </summary>
    RejectedTooLarge,
}

/// <summary>
/// Which route produced an MSSP report, because the two do not always agree byte for byte and the
/// route is therefore part of the value's provenance.
/// </summary>
public enum MsspTransport
{
    None,

    /// <summary>Telnet option 70 subnegotiation.</summary>
    TelnetOption70,

    /// <summary>The plaintext <c>MSSP-REQUEST</c> reply, delimited by START/END markers.</summary>
    /// <remarks>
    /// <b>Nothing produces this yet, deliberately.</b> The plaintext form belongs in
    /// TelnetNegotiationCore, where it is filed as issue #61; implementing it here would duplicate a
    /// first-party dependency and then have to be deleted. The member stays because the transport is
    /// part of a value's provenance the moment there are two routes, and spec §6.4 describes both —
    /// see <c>docs/codebase-survey-2026-07-30.md</c> for what the form actually reached when it was
    /// measured against twenty live games.
    /// </remarks>
    PlaintextRequest,
}

public enum ProbeOutcome
{
    Answered,
    Failed,
}

/// <summary>Why a probe failed, in the vocabulary the availability writer stores (spec §5.3).</summary>
public sealed record FailureDetail(string Cause, string? Detail = null);

/// <summary>
/// How much of a <c>WHO</c> response the structural parser could make sense of.
/// </summary>
/// <remarks>
/// Parsing is structural rather than per-dialect (spec §6.3): find the trailing
/// "<c>N Players logged in</c>" summary, else count rows between the header rule and the footer.
/// Penn, MUX and Rhost all let operators rewrite the DOING header in softcode, so a per-codebase
/// parser would be a maintenance treadmill that still lost to any game that customised it.
/// </remarks>
public sealed record WhoReading(WhoConfidence Confidence, int? Count = null, int? IdentifiablePlayers = null)
{
    /// <summary>
    /// No <c>WHO</c> was ever sent, so there is nothing to have failed to read.
    /// </summary>
    /// <remarks>
    /// The state a probe carries when it never got as far as asking — a dial that failed, a session
    /// abandoned against the budget. <b>Distinct from <see cref="Unreadable"/> by value</b>, which is
    /// the whole reason it exists.
    /// </remarks>
    public static readonly WhoReading NotAsked = new(WhoConfidence.NotAsked);

    /// <summary>
    /// A <c>WHO</c> was sent and the answer could not be made sense of.
    /// </summary>
    /// <remarks>
    /// This is a measurement of the parser meeting a dialect it cannot read, and it is the state
    /// spec §5.4's hatched cell is made of: probed, and uncountable. It is emphatically not zero.
    /// </remarks>
    public static readonly WhoReading Unreadable = new(WhoConfidence.Unknown);

    /// <summary>
    /// The count is trustworthy. Never synthesised: an unreadable WHO reports
    /// <see cref="WhoConfidence.Unknown"/> and the site falls back to MSSP <c>PLAYERS</c>, labelled
    /// as such. A parser that guessed zero would be indistinguishable from an empty game.
    /// </summary>
    public bool HasCount => Count is not null
        && Confidence is WhoConfidence.Count or WhoConfidence.PerPlayer;

    /// <summary>
    /// Whether the question was put to the server at all.
    /// </summary>
    /// <remarks>
    /// The distinction §5.4 turns on one level down. A probe that asked and could not read the answer
    /// has measured something about the game — it renders as the hatched, *probed but uncountable*
    /// cell. A probe that never asked has measured nothing and must render as neither that nor zero.
    /// While both states were <c>new(WhoConfidence.Unknown)</c> they were equal by value, so no
    /// writer downstream could tell them apart however carefully it was written.
    /// </remarks>
    public bool Attempted => Confidence is not WhoConfidence.NotAsked;
}

public enum WhoConfidence
{
    /// <summary>
    /// The question was never asked. The default, deliberately: a <see cref="WhoReading"/> nobody
    /// filled in has measured nothing, and that is the honest thing for it to say.
    /// </summary>
    NotAsked,

    /// <summary>Asked, and nothing usable came back. Writes no presence sample at all.</summary>
    Unknown,

    /// <summary>The number of connected players is readable.</summary>
    Count,

    /// <summary>
    /// The name column is positionally identifiable, so anonymised aggregates can be computed.
    /// Names are hashed with a rotating salt and never persisted (spec §11).
    /// </summary>
    PerPlayer,
}
