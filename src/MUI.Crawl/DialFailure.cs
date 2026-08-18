using System.Net.Sockets;

namespace MUI.Crawl;

/// <summary>
/// Reads why a dial failed, as a cause the catalogue has a word for.
/// </summary>
/// <remarks>
/// Extracted from <see cref="TelnetProbe"/> so the mapping can be asserted directly. It cannot be
/// exercised through a socket: three of the errors below cannot be provoked on demand from a test,
/// and they are exactly the ones that were being read wrongly.
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
            // Every way getaddrinfo can fail, not just the one. EAI_AGAIN (TryAgain) is the
            // transient one and was the costly omission: it fell to the catch-all, which
            // FailureReading turns into "timeout", so our own resolver giving up was published as
            // the game not answering. Measured on the production host, a cold lookup for these
            // domains takes 1.6s to 10s and sometimes returns nothing at all.
            SocketException
            {
                SocketErrorCode: SocketError.HostNotFound
                    or SocketError.TryAgain
                    or SocketError.NoData
                    or SocketError.NoRecovery,
            } => new("dns", error.Message),
            SocketException { SocketErrorCode: SocketError.ConnectionRefused } => new("refused", error.Message),
            SocketException { SocketErrorCode: SocketError.TimedOut } => new("timeout", error.Message),
            OperationCanceledException => new("timeout", "probe budget exhausted"),
            _ => new("error", error.Message),
        };
    }
}
