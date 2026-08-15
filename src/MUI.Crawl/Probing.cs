namespace MUI.Crawl;

/// <summary>What the crawler was asked to dial.</summary>
/// <param name="WhoHeader">
/// The line this game's verified owner told us begins their <c>WHO</c> table (spec §8.5), or null.
/// A hint about where to start counting rows and never a count: what it can change is whether we
/// measure at all, not what we measure.
/// </param>
public sealed record ProbeTarget(string Host, int Port, string? WhoHeader = null)
{
    public override string ToString() => Host.Contains(':') ? $"[{Host}]:{Port}" : $"{Host}:{Port}";
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
