using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;

namespace MUI.Crawl;

/// <summary>What came of one WebSocket handshake.</summary>
/// <param name="Answered">True only when the server completed the upgrade.</param>
/// <param name="Detail">Why not, for a log line. Never written to a game's record.</param>
/// <param name="SubProtocol">The subprotocol the server chose, or null when it chose none.</param>
public sealed record WebSocketHandshake(bool Answered, string? Detail = null, string? SubProtocol = null)
{
    public static readonly WebSocketHandshake Upgraded = new(true);

    public static WebSocketHandshake UpgradedWith(string? subProtocol) =>
        string.IsNullOrEmpty(subProtocol) ? Upgraded : new(true, SubProtocol: subProtocol);
}

/// <summary>
/// Completes a WebSocket handshake at an address, and says whether it did.
/// </summary>
/// <remarks>
/// A seam so the crawler's writer is testable with no socket; <see cref="WebSocketProbe"/> is the one
/// that dials.
/// </remarks>
public interface IWebSocketProbe
{
    /// <summary>
    /// Dials <paramref name="address"/> at exactly <paramref name="addresses"/>, which the scope gate
    /// has already ruled on, and asks for the upgrade.
    /// </summary>
    Task<WebSocketHandshake> HandshakeAsync(
        WebSocketAddress address,
        IReadOnlyList<IPAddress> addresses,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The handshake a MU* client's WebSocket would make, and nothing after it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The mudstandards.org names are offered first, then none.</b> Which one the server chooses is
/// the fact a client needs: Evennia 6.1 chooses <c>terminal.</c>, <c>gmcp.</c> or
/// <c>json.mudstandards.org</c> and never <c>telnet.</c>, and with none chosen it talks its own JSON,
/// so a socket that upgraded says nothing about what a client must speak until the choice is known.
/// But the plain bridges are strict: websockify answers 400 to a name it does not know, and MUME's
/// Apache answers 502 to <c>telnet.mudstandards.org</c>, while both accept a handshake that names
/// none. So an HTTP refusal of the offer is retried once with no offer, and an upgrade there is an
/// upgrade with no subprotocol — offering a name the bridge refuses must not become the game's
/// failure (rule 5).
/// </para>
/// <para>
/// <b>The socket goes to the addresses the scope gate ruled on</b>, never to a second lookup of the
/// name — <see cref="TelnetProbe"/>'s rule, and §7.2's. TLS still names the host, so SNI and the
/// certificate check are the host's. The certificate is checked: a reader's client would refuse one
/// that does not verify, so an address that needs it ignored is not an address to show them.
/// </para>
/// <para>
/// No proxy, no redirect followed (a WebSocket upgrade does not follow one), and the connection is
/// closed as soon as the upgrade is in: nothing is read and nothing is sent to the game.
/// </para>
/// </remarks>
public sealed class WebSocketProbe(TimeSpan? timeout = null) : IWebSocketProbe
{
    /// <summary>
    /// The subprotocols offered, in the order a client of this site's would prefer them: telnet first,
    /// since that is what a MU* client already speaks, then the three the spec adds beside it.
    /// </summary>
    public static readonly IReadOnlyList<string> Offered =
    [
        "telnet.mudstandards.org",
        "gmcp.mudstandards.org",
        "terminal.mudstandards.org",
        "json.mudstandards.org",
    ];

    /// <summary>The whole handshake's budget, connect and TLS included.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    private readonly TimeSpan _timeout = timeout ?? DefaultTimeout;

    public async Task<WebSocketHandshake> HandshakeAsync(
        WebSocketAddress address,
        IReadOnlyList<IPAddress> addresses,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(addresses);

        if (addresses.Count == 0)
        {
            return new WebSocketHandshake(false, "no address to dial");
        }

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(_timeout);

        var offered = await AttemptAsync(address, addresses, Offered, budget.Token, cancellationToken);

        // A status means the server read the request and turned down the offer; anything else (no
        // answer, a reset, the budget) would fail the same way without it, and is not retried.
        if (offered.Answered || offered.Status is null)
        {
            return offered.Result;
        }

        return (await AttemptAsync(address, addresses, [], budget.Token, cancellationToken)).Result;
    }

    private sealed record Attempt(WebSocketHandshake Result, int? Status)
    {
        public bool Answered => Result.Answered;
    }

    private async Task<Attempt> AttemptAsync(
        WebSocketAddress address,
        IReadOnlyList<IPAddress> addresses,
        IReadOnlyList<string> subProtocols,
        CancellationToken budget,
        CancellationToken cancellationToken)
    {
        using var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            ConnectTimeout = _timeout,
            ConnectCallback = async (context, token) =>
            {
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };

                try
                {
                    await socket.ConnectAsync([.. addresses], address.Port, token).ConfigureAwait(false);

                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            },
        };

        using var invoker = new HttpMessageInvoker(handler);
        using var socket = new ClientWebSocket();
        socket.Options.CollectHttpResponseDetails = true;

        foreach (var subProtocol in subProtocols)
        {
            socket.Options.AddSubProtocol(subProtocol);
        }

        try
        {
            await socket.ConnectAsync(address.Uri, invoker, budget).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new Attempt(new WebSocketHandshake(false, $"no upgrade within {_timeout.TotalSeconds:0} s"), null);
        }
        catch (Exception error) when (error is WebSocketException or HttpRequestException or IOException or SocketException)
        {
            var status = socket.HttpStatusCode != 0 ? (int?)socket.HttpStatusCode : null;

            return new Attempt(
                new WebSocketHandshake(false, status is { } code ? $"HTTP {code}" : error.Message),
                status);
        }

        // ClientWebSocket refuses a choice that was not offered, so what is here is one of ours.
        var chosen = socket.SubProtocol;

        try
        {
            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, budget).ConfigureAwait(false);
        }
        catch (Exception error) when (error is WebSocketException or OperationCanceledException or IOException)
        {
            // The upgrade is the measurement; how the close went is not.
            socket.Abort();
        }

        return new Attempt(WebSocketHandshake.UpgradedWith(chosen), null);
    }
}
