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

    /// <summary>Layer 2 — the connect screen, ANSI intact. Display asset and codebase fingerprint both.</summary>
    public string? Banner { get; init; }

    /// <summary>Layer 3 — what <c>WHO</c> or <c>DOING</c> yielded at the login screen.</summary>
    public WhoReading Who { get; init; } = WhoReading.Unread;

    /// <summary>Layer 4 — MSSP, whether by telnet option 70 or the plaintext <c>MSSP-REQUEST</c> fallback.</summary>
    public IReadOnlyDictionary<string, string> Mssp { get; init; } = new Dictionary<string, string>();

    public FailureDetail? Failure { get; init; }

    public TimeSpan Elapsed { get; init; }
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
    public static readonly WhoReading Unread = new(WhoConfidence.Unknown);

    /// <summary>
    /// The count is trustworthy. Never synthesised: an unreadable WHO reports
    /// <see cref="WhoConfidence.Unknown"/> and the site falls back to MSSP <c>PLAYERS</c>, labelled
    /// as such. A parser that guessed zero would be indistinguishable from an empty game.
    /// </summary>
    public bool HasCount => Confidence is not WhoConfidence.Unknown && Count is not null;
}

public enum WhoConfidence
{
    /// <summary>Nothing usable. Writes no presence sample at all.</summary>
    Unknown,

    /// <summary>The number of connected players is readable.</summary>
    Count,

    /// <summary>
    /// The name column is positionally identifiable, so anonymised aggregates can be computed.
    /// Names are hashed with a rotating salt and never persisted (spec §11).
    /// </summary>
    PerPlayer,
}
