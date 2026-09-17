using System.Net.Sockets;
using System.Security.Authentication;

namespace MUI.Crawl;

/// <summary>
/// Reads why a dial failed, as a cause the catalogue has a word for.
/// </summary>
/// <remarks>
/// Extracted from <see cref="TelnetProbe"/> so the mapping can be asserted directly, including the
/// errors that cannot be provoked on demand from a socket test.
/// </remarks>
public static class DialFailure
{
    /// <summary>The cause and the message, for one exception from a dial.</summary>
    /// <remarks>
    /// Causes are kept apart because only a change of cause writes an availability transition (spec
    /// §5.3) — a hundred consecutive timeouts are one interval, not a hundred. The message beside it
    /// is evidence and takes no part in that comparison.
    /// </remarks>
    public static FailureDetail Classify(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return error switch
        {
            // TryAgain (EAI_AGAIN) is the transient DNS failure; without it here, a resolver retry
            // falls to the catch-all and our own lookup giving up gets published as the game timing out.
            SocketException
            {
                SocketErrorCode: SocketError.HostNotFound
                    or SocketError.TryAgain
                    or SocketError.NoData
                    or SocketError.NoRecovery,
            } => new(DialFailureCause.Dns, error.Message),
            SocketException { SocketErrorCode: SocketError.ConnectionRefused } =>
                new(DialFailureCause.Refused, error.Message),
            SocketException { SocketErrorCode: SocketError.TimedOut } => new(DialFailureCause.Timeout, error.Message),

            // One word for all three: reachability is measured from one vantage point, so "no route
            // from here" is the honest sentence regardless of which errno produced it.
            SocketException
            {
                SocketErrorCode: SocketError.NetworkUnreachable
                    or SocketError.HostUnreachable
                    or SocketError.NetworkDown,
            } => new(DialFailureCause.NoRoute, error.Message),
            // Only reachable on a dial that asked for TLS: nothing else here builds an SslStream.
            // "It answered and would not do TLS" is a fact about the far end, so it gets the word
            // the catalogue's own cause vocabulary has carried since migration 0028.
            AuthenticationException => new(DialFailureCause.Tls, error.Message),
            OperationCanceledException => new(DialFailureCause.Timeout, "probe budget exhausted"),
            _ => new(DialFailureCause.Error, error.Message),
        };
    }
}
