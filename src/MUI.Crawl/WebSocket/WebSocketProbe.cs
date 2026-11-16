using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using TelnetNegotiationCore.WebSockets;

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
/// <b>TelnetNegotiationCore makes the handshake</b> (<see cref="TelnetWebSocketClient"/>), so the probe offers
/// exactly what a client built on it offers, in its order, and is refused exactly where such a client is. Which
/// one the server chooses is the fact a client needs: Evennia 6.1 chooses <c>terminal.</c>, <c>gmcp.</c> or
/// <c>json.mudstandards.org</c> and never <c>telnet.</c>, and with none chosen it talks its own JSON, so a
/// socket that upgraded says nothing about what a client must speak until the choice is known.
/// </para>
/// <para>
/// <b>An HTTP refusal of the offer is retried once with no offer</b>, by the library. The plain bridges are
/// strict: websockify answers 400 to a name it does not know, and MUME's Apache answers 502 to
/// <c>telnet.mudstandards.org</c>, while both accept a handshake that names none. An upgrade there is an
/// upgrade with no subprotocol — offering a name the bridge refuses must not become the game's failure
/// (rule 5). A failure with no HTTP answer (no route, a reset, the budget) is not retried.
/// </para>
/// <para>
/// <b>The socket goes to the addresses the scope gate ruled on</b>, never to a second lookup of the
/// name — <see cref="TelnetProbe"/>'s rule, and §7.2's. Both dials, the offer and the retry, go through
/// one invoker whose <c>ConnectCallback</c> dials those addresses. TLS still names the host, so SNI and
/// the certificate check are the host's. The certificate is checked: a reader's client would refuse one
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
    /// What the library offers after the telnet names: its bridged <c>gmcp.</c> and <c>terminal.</c>, then
    /// <c>json.mudstandards.org</c>, which no client here speaks but which Evennia may choose.
    /// </summary>
    /// <remarks>The probe closes the socket at the upgrade, so no bridge is ever made.</remarks>
    private static readonly WebSocketClientBridges Bridges = WebSocketClientBridges.Default
        .Add("json.mudstandards.org", static () => throw new NotSupportedException("The probe runs no session."));

    /// <summary>
    /// The subprotocols offered, in the order a TelnetNegotiationCore client offers them: the telnet names
    /// first, since that is what a MU* client already speaks, then the names the spec adds beside them.
    /// </summary>
    public static readonly IReadOnlyList<string> Offered =
    [
        MudWebSocketSubprotocols.Telnet,
        MudWebSocketSubprotocols.TelnetAlias,
        .. Bridges.Names,
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

        var status = new LastStatus(new SocketsHttpHandler
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
        });

        using var invoker = new HttpMessageInvoker(status);
        ClientWebSocket socket;

        try
        {
            socket = await TelnetWebSocketClient.ConnectAsync(address.Uri, invoker, null, Bridges, budget.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new WebSocketHandshake(false, $"no upgrade within {_timeout.TotalSeconds:0} s");
        }
        catch (Exception error) when (error is WebSocketException or HttpRequestException or IOException or SocketException)
        {
            return new WebSocketHandshake(false, status.Code is { } code and not 101 ? $"HTTP {code}" : error.Message);
        }

        using (socket)
        {
            // ClientWebSocket refuses a choice that was not offered, so what is here is one of ours.
            var chosen = socket.SubProtocol;

            try
            {
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, budget.Token).ConfigureAwait(false);
            }
            catch (Exception error) when (error is WebSocketException or OperationCanceledException or IOException)
            {
                // The upgrade is the measurement; how the close went is not.
                socket.Abort();
            }

            return WebSocketHandshake.UpgradedWith(chosen);
        }
    }

    /// <summary>
    /// Remembers the status of the last answer to an upgrade request, for the log line. The library disposes
    /// a socket whose handshake failed, and its status with it.
    /// </summary>
    private sealed class LastStatus(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        public int? Code { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Code = null;
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            Code = (int)response.StatusCode;
            return response;
        }
    }
}
