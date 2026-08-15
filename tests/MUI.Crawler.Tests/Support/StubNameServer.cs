using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace MUI.Crawler.Tests.Support;

/// <summary>
/// A nameserver, in about a hundred lines, so the TXT reader can be tested over real UDP.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a stub rather than a public name.</b> A resolver tested against somebody's real zone asserts
/// what their zone file says today and goes red for reasons that have nothing to do with this code —
/// the objection <c>FakeHostResolver</c> already makes about live lookups. But a resolver tested only
/// against a fake never exercises the wire format at all, and the wire format is where the surprises
/// are: TXT records are chopped into 255-byte strings, the answer's name is usually a compression
/// pointer, and "no such name" arrives as an error code rather than as an empty list. So the zone is
/// ours and everything between the query and the answer is real.
/// </para>
/// <para>
/// It speaks exactly enough DNS to answer one TXT question over UDP: header, one echoed question, and
/// an answer per published string. Anything else — TCP, EDNS, compression in the question, more than
/// one question — is not implemented, because nothing here asks for it.
/// </para>
/// </remarks>
public sealed class StubNameServer : IDisposable
{
    private const int HeaderLength = 12;

    private readonly UdpClient _socket = new(new IPEndPoint(IPAddress.Loopback, 0));
    private readonly CancellationTokenSource _stopping = new();
    private readonly Dictionary<string, List<string>> _zone = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();
    private readonly Task _serving;

    public StubNameServer() => _serving = ServeAsync(_stopping.Token);

    /// <summary>The ephemeral port it is listening on.</summary>
    public int Port => ((IPEndPoint)_socket.Client.LocalEndPoint!).Port;

    /// <summary>
    /// Every name it was asked about, so "we asked for the documented name" is assertable. Concurrent
    /// because it is written from the serving loop and read from the test's thread.
    /// </summary>
    public ConcurrentQueue<string> Asked { get; } = [];

    /// <summary>
    /// Receives and never answers, which is what a sick resolver does and is not the same as saying
    /// no. Nothing a client can do about it but stop waiting.
    /// </summary>
    public bool Silent { get; init; }

    /// <summary>Publishes one TXT record. Called before the lookup; a zone edit mid-flight is not a thing here.</summary>
    public void Publish(string name, params string[] records)
    {
        lock (_gate)
        {
            _zone[name.TrimEnd('.')] = [.. records];
        }
    }

    public void Dispose()
    {
        _stopping.Cancel();
        _socket.Dispose();

        try
        {
            _serving.GetAwaiter().GetResult();
        }
        catch (Exception error) when (error is OperationCanceledException or ObjectDisposedException or SocketException)
        {
            // Stopping a socket out from under a pending receive is how this ends.
        }

        _stopping.Dispose();
    }

    private async Task ServeAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            UdpReceiveResult query;

            try
            {
                query = await _socket.ReceiveAsync(cancellationToken);
            }
            catch (Exception error) when (error is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }

            // Parsed even when silent, so that "it was asked and said nothing" is distinguishable
            // from "it never heard the question".
            var response = Answer(query.Buffer);

            if (!Silent && response.Length > 0)
            {
                await _socket.SendAsync(response, response.Length, query.RemoteEndPoint);
            }
        }
    }

    private byte[] Answer(byte[] query)
    {
        if (query.Length < HeaderLength + 5)
        {
            return [];
        }

        var (name, questionEnd) = ReadQuestion(query);

        if (name is null)
        {
            return [];
        }

        Asked.Enqueue(name);

        List<string> records;

        lock (_gate)
        {
            records = _zone.TryGetValue(name, out var published) ? published : [];
        }

        // NXDOMAIN when the name is unknown, which is what a real zone says and what the reader has to
        // treat as an answer rather than as a failure.
        var known = records.Count > 0;
        var response = new List<byte>(query.Length + 64);

        response.AddRange(query[..2]);
        response.Add((byte)(0x84 | (query[2] & 0x01)));
        response.Add((byte)(known ? 0x80 : 0x83));
        response.AddRange([0x00, 0x01]);
        response.AddRange([(byte)(records.Count >> 8), (byte)records.Count]);
        response.AddRange([0x00, 0x00, 0x00, 0x00]);
        response.AddRange(query[HeaderLength..questionEnd]);

        foreach (var record in records)
        {
            // The owner name as a pointer to the question's, which is what every real server sends and
            // what the client therefore has to be able to follow.
            response.AddRange([0xC0, 0x0C]);
            response.AddRange([0x00, 0x10]);
            response.AddRange([0x00, 0x01]);
            response.AddRange([0x00, 0x00, 0x00, 0x3C]);

            var data = Chunked(record);

            response.AddRange([(byte)(data.Count >> 8), (byte)data.Count]);
            response.AddRange(data);
        }

        return [.. response];
    }

    /// <summary>The question's name, and where the question section ends.</summary>
    private static (string? Name, int End) ReadQuestion(byte[] query)
    {
        var labels = new List<string>();
        var at = HeaderLength;

        while (at < query.Length)
        {
            var length = query[at];

            if (length == 0)
            {
                at++;
                break;
            }

            // A pointer in a question is not a thing any client sends, and following one here would be
            // implementing a compression parser for nobody.
            if (length >= 0xC0 || at + 1 + length > query.Length)
            {
                return (null, 0);
            }

            labels.Add(Encoding.ASCII.GetString(query, at + 1, length));
            at += 1 + length;
        }

        return at + 4 <= query.Length ? (string.Join('.', labels), at + 4) : (null, 0);
    }

    /// <summary>
    /// One TXT record as the wire carries it: a sequence of length-prefixed strings, none longer than
    /// 255 bytes.
    /// </summary>
    private static List<byte> Chunked(string record)
    {
        var bytes = Encoding.UTF8.GetBytes(record);
        var data = new List<byte>(bytes.Length + (bytes.Length / 255) + 1);

        for (var offset = 0; offset < bytes.Length; offset += 255)
        {
            var length = Math.Min(255, bytes.Length - offset);

            data.Add((byte)length);
            data.AddRange(bytes.AsSpan(offset, length));
        }

        return data;
    }
}
