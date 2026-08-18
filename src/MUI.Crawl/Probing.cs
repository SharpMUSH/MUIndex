using System.Net;

namespace MUI.Crawl;

/// <summary>What the crawler was asked to dial.</summary>
public sealed record ProbeTarget(string Host, int Port)
{
    /// <summary>
    /// The addresses the scope guard already resolved and vetted, when there are any.
    /// </summary>
    /// <remarks>
    /// Empty means "resolve the name yourself", which is what the on-demand and CLI paths want.
    /// <para>
    /// The crawl loop fills it, and that is not an optimisation. <c>HostScopeGuard</c> checks that
    /// every address a name resolves to is globally routable, and a dial that resolves the name a
    /// second time is free to reach an address the guard never ruled on — so the guard was checking
    /// one answer and the socket was using another. Carrying the vetted list forward closes that,
    /// and it also halves the lookups: a probe used to resolve twice, and a transient failure in
    /// either one is published as the game going dark.
    /// </para>
    /// </remarks>
    public IReadOnlyList<IPAddress> Addresses { get; init; } = [];

    /// <summary>
    /// The encoding an operator has said this game's bytes are in, overriding what it declares.
    /// </summary>
    /// <remarks>
    /// Null on every target that has not needed one, which is nearly all of them. It rides on the
    /// target rather than on <see cref="ProbeOptions"/> because it is a fact about one game and the
    /// options are one instance shared by the whole crawl; and it is a name rather than an
    /// <c>Encoding</c> so that this seam stays a value a fixture can carry. See
    /// <see cref="WireEncoding"/> for why the crawler cannot work this out for itself.
    /// </remarks>
    public string? Charset { get; init; }

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
