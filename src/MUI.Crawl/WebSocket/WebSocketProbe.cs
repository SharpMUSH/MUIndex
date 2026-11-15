using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;

namespace MUI.Crawl;

/// <summary>What came of one WebSocket handshake.</summary>
/// <param name="Answered">True only when the server completed the upgrade.</param>
/// <param name="Detail">Why not, for a log line. Never written to a game's record.</param>
public sealed record WebSocketHandshake(bool Answered, string? Detail = null)
{
    public static readonly WebSocketHandshake Upgraded = new(true);
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
/// <b>No subprotocol is offered.</b> The real bridges are strict about it: websockify answers 400 to a
/// name it does not know, and MUME's Apache answers 502 to <c>telnet.mudstandards.org</c>, while both
/// accept a handshake that names none. The question here is whether a client can reach the game at
/// this address, and offering a name the bridge refuses would record our choice of offer as the
/// game's failure (rule 5).
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

        try
        {
            await socket.ConnectAsync(address.Uri, invoker, budget.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new WebSocketHandshake(false, $"no upgrade within {_timeout.TotalSeconds:0} s");
        }
        catch (Exception error) when (error is WebSocketException or HttpRequestException or IOException or SocketException)
        {
            return new WebSocketHandshake(
                false,
                socket.HttpStatusCode != 0 ? $"HTTP {(int)socket.HttpStatusCode}" : error.Message);
        }

        try
        {
            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, budget.Token).ConfigureAwait(false);
        }
        catch (Exception error) when (error is WebSocketException or OperationCanceledException or IOException)
        {
            // The upgrade is the measurement; how the close went is not.
            socket.Abort();
        }

        return WebSocketHandshake.Upgraded;
    }
}
