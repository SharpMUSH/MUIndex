using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography.X509Certificates;
using TelnetNegotiationCore.Connections;
using TelnetNegotiationCore.WebSockets;

namespace MUI.Crawl;

/// <summary>
/// The socket under one probe session, whichever kind it is, and the TelnetNegotiationCore transport
/// that reads it.
/// </summary>
/// <remarks>
/// <para>
/// Everything above <see cref="Transport"/> — negotiation, MSSP, the <c>WHO</c> parser, the encoding
/// decision — runs identically on every kind, which is why a TLS or WebSocket game keeps every
/// capability a cleartext one has. This type is the only place the three differ.
/// </para>
/// <para>
/// Disposed <em>after</em> the <see cref="TelnetConnection"/> that reads it, never under it: the
/// interpreter still does work during its own teardown. The tunnel goes before the TCP client it
/// wraps.
/// </para>
/// </remarks>
internal sealed class ProbeLine : IAsyncDisposable
{
    private TcpClient? _client;
    private SslStream? _tunnel;
    private ClientWebSocket? _socket;
    private HttpMessageInvoker? _invoker;

    /// <summary>What the interpreter reads and writes. Set once the line is open.</summary>
    public ITelnetTransport Transport { get; private set; } = null!;

    /// <summary>
    /// Whether the dial got far enough to start a TLS handshake that did not finish. Only
    /// <see cref="ProbeTransport.Tls"/> can set it. See <c>TelnetProbe.Session.HandshakeIncomplete</c>.
    /// </summary>
    public bool HandshakeIncomplete { get; private set; }

    /// <summary>
    /// Opens a line to <paramref name="target"/> over <paramref name="transport"/>.
    /// </summary>
    /// <remarks>
    /// The line is still the caller's to dispose when this throws part-way: a TCP connect that
    /// succeeded under a handshake that failed still holds a socket.
    /// </remarks>
    public async Task OpenAsync(ProbeTarget target, ProbeTransport transport, CancellationToken cancellationToken)
    {
        if (transport is ProbeTransport.WebSocket)
        {
            await OpenWebSocketAsync(target, cancellationToken);
            return;
        }

        var client = _client = new TcpClient();

        // The vetted addresses when the caller has them, the name otherwise. See
        // ProbeTarget.Addresses for why re-resolving a name the scope guard already ruled on is both
        // a hole in the guard and a second chance to fail on a transient lookup.
        HandshakeIncomplete = transport is ProbeTransport.Tls;
        await (target.Addresses.Count > 0
            ? client.ConnectAsync([.. target.Addresses], target.Port, cancellationToken)
            : client.ConnectAsync(target.Host, target.Port, cancellationToken));

        if (transport is ProbeTransport.Tls)
        {
            _tunnel = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
            await _tunnel.AuthenticateAsClientAsync(TlsOptions(target.Host), cancellationToken);
            HandshakeIncomplete = false;
            Transport = TelnetTransport.Stream(_tunnel);
            return;
        }

        Transport = TelnetTransport.Tcp(client);
    }

    /// <summary>
    /// Connects a WebSocket offering the telnet subprotocols and the bridged ones, through the vetted
    /// addresses.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The subprotocols are the ones <c>TelnetWebSocketClient.ConnectAsync</c> offers:
    /// <c>telnet.mudstandards.org</c> and <c>telnet</c>, then <c>gmcp.mudstandards.org</c> and
    /// <c>terminal.mudstandards.org</c> (<see cref="WebSocketClientBridges.Default"/>). A server that
    /// names none is taken as telnet, because most wss-to-telnet bridges name none. One that picks a
    /// bridged name (Evennia picks <c>gmcp</c>; it has no telnet layer on its WebSocket) is read
    /// through the bridge, which turns its frames into a telnet stream and opens GMCP as though the
    /// server had offered it, so the session above reads the same either way.
    /// </para>
    /// <para>
    /// The helper is handed an invoker of ours, because the scope guard's ruling is on addresses
    /// (spec §7.2): the <see cref="SocketsHttpHandler.ConnectCallback"/> here dials exactly the
    /// addresses the guard vetted, the same as the TCP path, and the helper's second dial (with no
    /// subprotocol, after a server answers the first with an HTTP error) goes through it too.
    /// </para>
    /// <para>
    /// The handler lives for one dial and is disposed with the line, so none of the DNS-pinning
    /// concern that puts <c>IconFetcher</c> behind <c>IHttpClientFactory</c> applies — there is no
    /// pool to outlive an answer, and the answer is pinned on purpose. No proxy, no cookies, no
    /// redirects: the upgrade request goes to the address that was ruled on, and nowhere else.
    /// </para>
    /// </remarks>
    private async Task OpenWebSocketAsync(ProbeTarget target, CancellationToken cancellationToken)
    {
        var uri = target.WebSocket
            ?? throw new InvalidOperationException($"{target} has no WebSocket address to dial.");

        var addresses = target.Addresses;

        _invoker = new HttpMessageInvoker(
            new SocketsHttpHandler
            {
                UseProxy = false,
                UseCookies = false,
                AllowAutoRedirect = false,
                SslOptions = TlsOptions(uri.IdnHost),
                ConnectCallback = async (context, token) =>
                {
                    var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };

                    try
                    {
                        await (addresses.Count > 0
                            ? socket.ConnectAsync([.. addresses], context.DnsEndPoint.Port, token)
                            : socket.ConnectAsync(context.DnsEndPoint, token));

                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch
                    {
                        socket.Dispose();
                        throw;
                    }
                },
            },
            disposeHandler: true);

        _socket = await TelnetWebSocketClient.ConnectAsync(uri, _invoker, null, Bridges, cancellationToken);

        // Throws ArgumentException for a server that picked a subprotocol that is neither telnet nor
        // bridged. It cannot, having been offered only those, but if it does that is not a game this
        // probe can read.
        Transport = TelnetTransport.WebSocketClient(_socket, Bridges);
    }

    /// <summary>The subprotocols offered after the telnet ones, and read through a bridge.</summary>
    private static readonly WebSocketClientBridges Bridges = WebSocketClientBridges.Default;

    /// <summary>
    /// The TLS settings for every handshake this probe makes, raw TLS and <c>wss://</c> alike.
    /// </summary>
    /// <remarks>
    /// <b>Every certificate is accepted</b> — self-signed, expired, wrong name, any of it. The probe
    /// reads a login screen a server shows to strangers, so there is nothing here for a chain to
    /// protect, and MU* TLS ports routinely serve a certificate no public root signed. What this
    /// costs is precision of language downstream, not safety: <see cref="ProbeTransport.Tls"/> means
    /// a handshake completed and must never be rendered as though somebody had verified anything.
    /// Revocation checking is off for the same reason — it is a network round trip in service of a
    /// question this probe is not asking.
    /// </remarks>
    private static SslClientAuthenticationOptions TlsOptions(string host) => new()
    {
        // SNI: a host serving several games from one address picks by this, and sending the dialled
        // name is the only way to reach the right one.
        TargetHost = host,
        RemoteCertificateValidationCallback = static (_, _, _, _) => true,
        CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
    };

    public async ValueTask DisposeAsync()
    {
        if (_socket is not null)
        {
            // Abort rather than a close handshake: by the time the line goes, the connection above it
            // has already stopped reading, so nothing would read the peer's answer to a close frame.
            _socket.Abort();
            _socket.Dispose();
        }

        _invoker?.Dispose();

        if (_tunnel is not null)
        {
            await _tunnel.DisposeAsync();
        }

        _client?.Dispose();
    }
}
