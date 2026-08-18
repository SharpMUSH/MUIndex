using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace MUI.I3.Tests;

/// <summary>
/// A gateway that answers on a real loopback socket, scripted per test.
/// </summary>
/// <remarks>
/// A real socket rather than a stream double: a hand-fed stream can't prove framing/ordering bugs
/// in <see cref="GatewayClient"/> because the test author controls the interleaving.
/// </remarks>
internal sealed class FakeGateway : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly Func<string, FakeGateway, Task> _onRequest;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _accepting;

    private StreamWriter? _writer;

    private FakeGateway(TcpListener listener, Func<string, FakeGateway, Task> onRequest)
    {
        _listener = listener;
        _onRequest = onRequest;
        _accepting = Task.Run(AcceptAsync);
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>Every line the client sent, in order.</summary>
    public List<string> Received { get; } = [];

    public static FakeGateway Start(Func<string, FakeGateway, Task> onRequest)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return new FakeGateway(listener, onRequest);
    }

    /// <summary>Pushes an unsolicited line — an event — at the client.</summary>
    public async Task SendAsync(object message)
    {
        var json = JsonSerializer.Serialize(message);
        await _writer!.WriteLineAsync(json).ConfigureAwait(false);
    }

    public Task ReplyAsync(int id, object result) =>
        SendAsync(new Dictionary<string, object?>
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["result"] = result,
        });

    private async Task AcceptAsync()
    {
        try
        {
            using var client = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
            var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, false, 8192, leaveOpen: true);
            _writer = new StreamWriter(stream, new UTF8Encoding(false), 8192, leaveOpen: true)
            {
                AutoFlush = true,
            };

            while (!_stop.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(_stop.Token).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                lock (Received)
                {
                    Received.Add(line);
                }

                await _onRequest(line, this).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown.
        }
        catch (IOException)
        {
            // The client hung up first.
        }
    }

    /// <summary>The <c>id</c> of a request line, so a handler can answer the right call.</summary>
    public static int IdOf(string line) =>
        JsonDocument.Parse(line).RootElement.GetProperty("id").GetInt32();

    public static string MethodOf(string line) =>
        JsonDocument.Parse(line).RootElement.GetProperty("method").GetString() ?? "";

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        _listener.Stop();
        try
        {
            await _accepting.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected.
        }

        _stop.Dispose();
        _writer?.Dispose();
    }
}
