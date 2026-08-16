using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MUI.I3;

/// <summary>
/// Talks JSON-RPC 2.0 to the Intermud-3 sidecar over its newline-delimited TCP surface.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this is not.</b> It is not an I3 client. The gateway owns mudmode framing, the I3v3
/// handshake, the router password and the mudlist cache; this owns one TCP socket to localhost and
/// the shape of the messages that cross it. The protocol boundary is deliberate — see
/// <c>deploy/i3/config.yaml</c> for what we tell the network about ourselves, and note that the one
/// thing we learned the hard way lives there rather than here: a startup packet advertising no
/// services at all is discarded by <c>*i4</c> in silence.
/// </para>
/// <para>
/// <b>Remote queries are asynchronous and the immediate reply means nothing.</b> A <c>who</c> call
/// returns <c>{"status":"requested"}</c> the moment the packet leaves; the answer arrives later, out
/// of band, as a <c>who_reply</c> event carrying the mud's name. That mirrors I3 itself, where a
/// request and its reply are two unrelated packets, and it is why <see cref="WhoAsync"/> registers a
/// waiter *before* it sends rather than reading the next line off the socket.
/// </para>
/// <para>
/// <b>Do not use the gateway's synchronous <c>who</c> return value.</b> Its handler calls a method
/// that exists only on the who *service* and returns a bool, then takes the length of it. The event
/// is the supported path and the only one this client reads.
/// </para>
/// </remarks>
public sealed class GatewayClient : IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly GatewayOptions _options;
    private readonly ILogger<GatewayClient> _log;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private readonly Dictionary<int, TaskCompletionSource<JsonElement>> _pending = [];

    /// <summary>
    /// Waiters keyed by the mud whose answer they want, because a <c>who_reply</c> carries no request
    /// id — only <c>from_mud</c>. Two outstanding questions to the same mud would be one question as
    /// far as the wire is concerned, so the second waiter replaces the first rather than both hanging.
    /// </summary>
    private readonly Dictionary<string, TaskCompletionSource<I3WhoReply>> _whoWaiters =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Lock _gate = new();

    private TcpClient? _tcp;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private Task? _pump;
    private CancellationTokenSource? _pumpStop;
    private int _nextId;

    public GatewayClient(GatewayOptions options, ILogger<GatewayClient>? log = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _log = log ?? NullLogger<GatewayClient>.Instance;
    }

    /// <summary>Opens the socket, authenticates, and starts reading. Idempotent per instance.</summary>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_tcp is not null)
        {
            throw new InvalidOperationException("This client has already connected.");
        }

        _tcp = new TcpClient();
        await _tcp.ConnectAsync(_options.Host, _options.Port, cancellationToken).ConfigureAwait(false);

        var stream = _tcp.GetStream();
        _reader = new StreamReader(stream, Encoding.UTF8, false, 8192, leaveOpen: true);
        // AutoFlush rather than flushing by hand: every write here is one complete JSON object and a
        // newline, and a buffered half-message is a request the gateway will never see.
        _writer = new StreamWriter(stream, new UTF8Encoding(false), 8192, leaveOpen: true) { AutoFlush = true };

        _pumpStop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _pump = Task.Run(() => PumpAsync(_pumpStop.Token), CancellationToken.None);

        var authenticated = await CallAsync(
            "authenticate",
            new Dictionary<string, object?> { ["api_key"] = _options.ApiKey },
            cancellationToken).ConfigureAwait(false);

        if (!authenticated.TryGetProperty("status", out var status)
            || status.GetString() != "authenticated")
        {
            throw new GatewayException(
                $"The gateway did not accept our API key: {authenticated.GetRawText()}");
        }
    }

    /// <summary>Every mud the router has told the gateway about.</summary>
    /// <remarks>
    /// Unfiltered on purpose. The gateway accepts a <c>filter</c> — <c>{"status":"up",
    /// "has_service":"who"}</c> — and pushing the gate down there would hide from the caller the one
    /// thing worth recording, which is how many muds exist versus how many we may ask. Filter here,
    /// where the decision is legible.
    /// </remarks>
    public async Task<IReadOnlyList<I3Mud>> MudlistAsync(CancellationToken cancellationToken = default)
    {
        var result = await CallAsync("mudlist", new Dictionary<string, object?>(), cancellationToken)
            .ConfigureAwait(false);

        // The gateway represents its mudlist two different ways in two different places: the JSON-RPC
        // method answers `{"muds": [ … ]}` — an array, each element carrying its own `name` — while
        // the state file it writes to disk is an object keyed by mud name. Both are accepted, because
        // a beta whose own two representations disagree is not one to pin a single shape to, and
        // reading the wrong one yields an empty list rather than an error.
        var muds = result.ValueKind == JsonValueKind.Object && result.TryGetProperty("muds", out var inner)
            ? inner
            : result;

        var list = new List<I3Mud>();

        switch (muds.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var element in muds.EnumerateArray())
                {
                    if (element.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var mud = element.Deserialize<I3Mud>(Json);
                    if (mud is not null && !string.IsNullOrEmpty(mud.Name))
                    {
                        list.Add(mud);
                    }
                }

                break;

            case JsonValueKind.Object:
                foreach (var entry in muds.EnumerateObject())
                {
                    if (entry.Value.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var mud = entry.Value.Deserialize<I3Mud>(Json);
                    if (mud is null)
                    {
                        continue;
                    }

                    // The key is the canonical mud name; the body may or may not repeat it.
                    list.Add(string.IsNullOrEmpty(mud.Name) ? mud with { Name = entry.Name } : mud);
                }

                break;
        }

        return list;
    }

    /// <summary>
    /// Asks one mud to enumerate its users, and waits for the answer that arrives as an event.
    /// </summary>
    /// <returns>
    /// The reply, or <see langword="null"/> if the mud did not answer inside
    /// <see cref="GatewayOptions.WhoTimeout"/>. <b>Silence is not zero</b> — a mud that never answers
    /// has told us nothing about how many people are on it, and the caller must record that as
    /// unmeasurable rather than as an empty game (spec §5.4, rule 4).
    /// </returns>
    public async Task<I3WhoReply?> WhoAsync(string mud, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mud);

        var waiter = new TaskCompletionSource<I3WhoReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            // A second question to the same mud supersedes the first: the wire cannot tell the two
            // apart, so keeping both would leave one of them hanging until its timeout for no reason.
            if (_whoWaiters.TryGetValue(mud, out var existing))
            {
                existing.TrySetResult(new I3WhoReply { FromMud = mud });
            }

            _whoWaiters[mud] = waiter;
        }

        try
        {
            await CallAsync(
                "who",
                new Dictionary<string, object?>
                {
                    ["target_mud"] = mud,
                    // I3 routes usernames in lowercase and the field is required. It names the asker,
                    // not a player: nobody typed this.
                    ["from_user"] = _options.FromUser,
                },
                cancellationToken).ConfigureAwait(false);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.WhoTimeout);

            try
            {
                return await waiter.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _log.LogDebug("No who-reply from {Mud} within {Timeout}.", mud, _options.WhoTimeout);
                return null;
            }
        }
        finally
        {
            lock (_gate)
            {
                if (_whoWaiters.TryGetValue(mud, out var current) && ReferenceEquals(current, waiter))
                {
                    _whoWaiters.Remove(mud);
                }
            }
        }
    }

    private async Task<JsonElement> CallAsync(
        string method,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _nextId);
        var waiter = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            _pending[id] = waiter;
        }

        var request = JsonSerializer.Serialize(
            new Dictionary<string, object?>
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id,
                ["method"] = method,
                ["params"] = parameters,
            },
            Json);

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _writer!.WriteLineAsync(request.AsMemory(), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.CallTimeout);
        try
        {
            return await waiter.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new GatewayException($"The gateway did not answer '{method}' within {_options.CallTimeout}.");
        }
        finally
        {
            lock (_gate)
            {
                _pending.Remove(id);
            }
        }
    }

    private async Task PumpAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await _reader!.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                if (line.Length == 0)
                {
                    continue;
                }

                try
                {
                    Dispatch(line);
                }
                catch (JsonException e)
                {
                    // A line we cannot read is the gateway's problem, not a reason to stop reading the
                    // ones after it.
                    _log.LogWarning(e, "Unreadable line from the I3 gateway.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Disposal.
        }
        catch (IOException e)
        {
            _log.LogWarning(e, "The I3 gateway connection dropped.");
        }
        finally
        {
            FailEveryWaiter(new GatewayException("The I3 gateway connection closed."));
        }
    }

    private void Dispatch(string line)
    {
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;

        // A line that parses but is not an object — a bare string is legal JSON — would otherwise
        // reach TryGetProperty, which throws InvalidOperationException rather than JsonException and
        // so would kill the pump instead of being skipped. One malformed line must not make us deaf
        // to every line after it.
        if (root.ValueKind != JsonValueKind.Object)
        {
            _log.LogWarning("Ignoring a non-object line from the I3 gateway.");
            return;
        }

        if (root.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number)
        {
            TaskCompletionSource<JsonElement>? waiter;
            lock (_gate)
            {
                _pending.TryGetValue(id.GetInt32(), out waiter);
            }

            if (waiter is null)
            {
                return;
            }

            if (root.TryGetProperty("error", out var error))
            {
                waiter.TrySetException(new GatewayException(
                    $"The gateway rejected the call: {error.GetRawText()}"));
                return;
            }

            // Cloned because the JsonDocument backing it is disposed the moment this method returns.
            waiter.TrySetResult(root.TryGetProperty("result", out var result)
                ? result.Clone()
                : default);
            return;
        }

        if (!root.TryGetProperty("method", out var method))
        {
            return;
        }

        switch (method.GetString())
        {
            case "who_reply":
                if (root.TryGetProperty("params", out var payload))
                {
                    var reply = payload.Deserialize<I3WhoReply>(Json);
                    if (reply is not null && !string.IsNullOrEmpty(reply.FromMud))
                    {
                        lock (_gate)
                        {
                            if (_whoWaiters.TryGetValue(reply.FromMud, out var waiting))
                            {
                                waiting.TrySetResult(reply);
                            }
                        }
                    }
                }

                break;

            case "error_occurred":
                // Logged and dropped. An I3 error names a packet, not necessarily ours, and the
                // waiter's timeout is what turns "no answer" into an unmeasurable reading — letting
                // an unrelated error resolve it would be recording our confusion as their state.
                _log.LogDebug("I3 error event: {Payload}",
                    root.TryGetProperty("params", out var e) ? e.GetRawText() : "(none)");
                break;
        }
    }

    private void FailEveryWaiter(Exception reason)
    {
        List<TaskCompletionSource<JsonElement>> calls;
        List<TaskCompletionSource<I3WhoReply>> whos;
        lock (_gate)
        {
            calls = [.. _pending.Values];
            whos = [.. _whoWaiters.Values];
            _pending.Clear();
            _whoWaiters.Clear();
        }

        foreach (var call in calls)
        {
            call.TrySetException(reason);
        }

        // A dropped connection is silence, and silence is unmeasurable rather than an error — the
        // caller distinguishes "no answer" from "no count" the same way a who timeout does.
        foreach (var who in whos)
        {
            who.TrySetCanceled();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_pumpStop is not null)
        {
            await _pumpStop.CancelAsync().ConfigureAwait(false);
        }

        _tcp?.Close();

        if (_pump is not null)
        {
            try
            {
                await _pump.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected.
            }
        }

        _pumpStop?.Dispose();
        _reader?.Dispose();
        _writer?.Dispose();
        _tcp?.Dispose();
        _writeLock.Dispose();
    }
}

/// <summary>Where the gateway is and who we say we are to it.</summary>
public sealed record GatewayOptions
{
    public string Host { get; init; } = "i3";

    /// <summary>The gateway's newline-delimited JSON-RPC port. Its WebSocket surface is on 8080.</summary>
    public int Port { get; init; } = 8081;

    public string ApiKey { get; init; } = "";

    /// <summary>
    /// The <c>from_user</c> every request carries. I3 requires the field and routes it lowercase; it
    /// names the asker, and here the asker is a crawler rather than a person.
    /// </summary>
    public string FromUser { get; init; } = "crawler";

    /// <summary>How long the gateway itself may take to acknowledge a call.</summary>
    public TimeSpan CallTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How long a remote mud has to answer a <c>who</c>. Generous, because the packet crosses a
    /// router to a third party and back: the measured replies from Nightfall and Dead Souls Dev came
    /// in well under a second, and a mud that has not answered in this long is one we record as
    /// unmeasurable rather than as empty.
    /// </summary>
    public TimeSpan WhoTimeout { get; init; } = TimeSpan.FromSeconds(20);
}

public sealed class GatewayException : Exception
{
    public GatewayException(string message) : base(message)
    {
    }

    public GatewayException(string message, Exception inner) : base(message, inner)
    {
    }
}
