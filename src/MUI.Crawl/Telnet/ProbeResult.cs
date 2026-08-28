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

    /// <summary>
    /// The encoding every piece of text below was actually read with, or null when the probe
    /// produced no text at all.
    /// </summary>
    /// <remarks>
    /// Not the same fact as <see cref="Negotiation.Charset"/>: that's what the session declared it
    /// settled on, this is what a strict UTF-8 decoder proved about the actual bytes (or an operator
    /// override). Where the two differ, the disagreement is the interesting fact (rule 1). See
    /// <see cref="WireEncoding"/>.
    /// </remarks>
    public string? ReadAs { get; init; }

    /// <summary>
    /// How much is known about <see cref="ReadAs"/> — proven from the bytes, chosen by an operator,
    /// or undetermined.
    /// </summary>
    /// <remarks>
    /// A writer must consult this before storing <see cref="ReadAs"/> anywhere:
    /// <see cref="WireCharset.Undetermined"/> means the Latin-1 fallback is a way of keeping the
    /// bytes, not a reading of them, and storing it as the latter records our own fallback as a fact
    /// about the game.
    /// </remarks>
    public WireCharset CharsetSource { get; init; } = WireCharset.Proven;

    /// <summary>Layer 2 — the connect screen, ANSI intact. Display asset and codebase fingerprint both.</summary>
    public string? Banner { get; init; }

    /// <summary>
    /// Whether the server emitted MXP in anything it sent us.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="OfferedOptions"/> because many servers emit MXP without ever
    /// negotiating the option — its line-mode sequences are ANSI-legal and pass harmlessly through a
    /// client that never heard of them. Both are real, distinct observations.
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
    /// Redacted inside the probe, not downstream — the raw response exists only as a local in one
    /// method and is never a member of anything. See <see cref="PayloadRedaction"/>.
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
    /// A value is a list because MSSP allows the same variable to be sent more than once with
    /// different values — <c>REFERRAL</c> (crawl discovery's whole basis) and <c>PORT</c> (a game on
    /// two ports) both do this, and a flat map or a comma-joined string would lose or fabricate data
    /// (a value may itself contain a comma). Nothing is filtered: deciding what's worth displaying is
    /// the catalogue's job, and it can't display what the probe already threw away.
    /// </remarks>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Mssp { get; init; } = MsspReport.Empty;

    /// <summary>
    /// The single value of an MSSP variable, or null when the server did not report it.
    /// </summary>
    /// <remarks>
    /// Returns the <em>last</em> value where several were sent, per MSSP's own rule for reducing to
    /// a scalar. Anything that cares about the others (e.g. reading <c>REFERRAL</c>) must read
    /// <see cref="Mssp"/> instead.
    /// </remarks>
    public string? MsspField(string variable) => MsspReport.Last(Mssp, variable);

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
    /// The weakest of the three count sources — pattern-matching a stranger's ASCII art — so it's
    /// read only when MSSP and a pre-login <c>WHO</c> have both failed.
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
/// TelnetNegotiationCore bounds the MSSP payload and, at the ceiling, drops the report rather than
/// truncating it — a truncated report would parse cleanly and lie. This outcome must be carried
/// through rather than recorded as "no MSSP", or our own size limit gets published as a fact about
/// the game.
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
    /// Nothing produces this yet, deliberately — the plaintext form belongs in
    /// TelnetNegotiationCore (first-party), and implementing it here would duplicate then have to be
    /// deleted. The member stays because spec §6.4 describes both routes.
    /// </remarks>
    PlaintextRequest,
}

public enum ProbeOutcome
{
    Answered,
    Failed,
}

/// <summary>Why a probe failed, in the vocabulary the availability writer stores (spec §5.3).</summary>
public sealed record FailureDetail(DialFailureCause Cause, string? Detail = null);

/// <summary>
/// The closed set of causes <see cref="DialFailure.Classify"/> produces.
/// </summary>
/// <remarks>
/// A type here rather than a string, so a cause added to <see cref="DialFailure.Classify"/> without
/// updating every consumer's switch is a compiler error rather than a silent misclassification —
/// <c>MUI.Crawler</c>'s <c>ProbeIngestor.CauseOf</c> is the one place that maps this to
/// <c>MUI.Catalog</c>'s own <c>FailureCause</c>. Deliberately not that type, nor a reference to it:
/// <c>MUI.Crawl</c> does not take a dependency inward toward the catalogue (see the "MUIndex owns its
/// crawler" note), so this repeats the small piece of vocabulary it needs instead.
/// </remarks>
public enum DialFailureCause
{
    Dns,
    Refused,
    Timeout,
    NoRoute,
    Error,
}

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
    /// The state a probe carries when it never got as far as asking. Distinct from
    /// <see cref="Unreadable"/> by value — the whole reason it exists.
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
    /// A <c>WHO</c> was sent, and what came back was the server's login prompt reacting to it.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="Unreadable"/> because only one of the two problems is ours: Unreadable
    /// means our parser met a dialect it couldn't read, a defect with a fix. LoginPrompt means the
    /// server never had a <c>WHO</c> to answer — its login prompt consumed the word as a character
    /// name, so what came back was <c>Illegal name, try again.</c>. Filing the second as the first is
    /// rule 5 in its quietest form: our own limit written into a game's record as a fact about the
    /// game. Still <see cref="Attempted"/> with no count, so §5.4's hatched cell is unchanged — only
    /// the reason recorded beside it does.
    /// </remarks>
    public static readonly WhoReading LoginPrompt = new(WhoConfidence.LoginPrompt);

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
    /// A probe that asked and couldn't read the answer has measured something (the hatched, *probed
    /// but uncountable* cell); one that never asked has measured nothing. When both states shared
    /// <c>new(WhoConfidence.Unknown)</c> they were equal by value, so no writer downstream could tell
    /// them apart however carefully it was written.
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

    /// <summary>
    /// Asked, and the server answered as though the word were a character name. See
    /// <see cref="WhoReading.LoginPrompt"/>.
    /// </summary>
    LoginPrompt,

    /// <summary>The number of connected players is readable.</summary>
    Count,

    /// <summary>
    /// The name column is positionally identifiable, so anonymised aggregates can be computed.
    /// Names are hashed with a rotating salt and never persisted (spec §11).
    /// </summary>
    PerPlayer,
}
