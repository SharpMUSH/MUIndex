using System.Net.Http.Headers;
using System.Text.Json;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MUI.Ares;

/// <summary>
/// Reads the AresCentral games list.
/// </summary>
/// <remarks>
/// The <see cref="HttpClient"/> arrives from <c>IHttpClientFactory</c> and is never constructed here
/// — the factory is what bounds the handler's lifetime, which is the same rule <c>IconFetcher</c>
/// follows and for the same reason. Redirects are off at the registration: a redirect is a second
/// address nobody ruled on.
/// </remarks>
public sealed class AresGamesClient(
    HttpClient http,
    AresOptions options,
    ILogger<AresGamesClient>? log = null) : IAresGames
{
    /// <summary>
    /// The name the <c>IHttpClientFactory</c> registration and every caller agree on.
    /// </summary>
    /// <remarks>
    /// A named client rather than a typed one, deliberately. A typed client is registered transient,
    /// and the only thing that consumes this is a singleton hosted service — which would resolve one
    /// and hold it, and its handler, for the life of the process. Named means the caller asks the
    /// factory for a client per pass and the pool rotates handlers underneath as intended.
    /// </remarks>
    public const string HttpClientName = "arescentral";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ILogger<AresGamesClient> _log = log ?? NullLogger<AresGamesClient>.Instance;

    /// <summary>
    /// The bearer credential AresCentral documents: the client id and the key, joined by a colon.
    /// </summary>
    internal static string AuthorizationFor(AresOptions options) =>
        $"{options.ClientId}:{options.ApiKey}";

    public async Task<IReadOnlyList<AresListedGame>> ListAsync(CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, options.GamesPath);
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", AuthorizationFor(options));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await http.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, ct);

        // Throws on any non-success, and that is the point: nothing downstream may mistake a refusal
        // for a hub that lists no games.
        response.EnsureSuccessStatusCode();

        await using var body = await response.Content.ReadAsStreamAsync(ct);
        await using var bounded = new BoundedStream(body, options.MaxResponseBytes);

        var games = await JsonSerializer.DeserializeAsync<List<AresListedGame>>(bounded, Json, ct)
            ?? throw new JsonException(
                "AresCentral answered with a JSON null rather than a list of games.");

        _log.LogDebug("AresCentral listed {Count} games", games.Count);

        return games;
    }

    /// <summary>A read that stops at a ceiling rather than trusting the far end to be reasonable.</summary>
    private sealed class BoundedStream(Stream inner, long ceiling) : Stream
    {
        private long _read;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => _read;
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await inner.ReadAsync(buffer, cancellationToken);
            _read += read;

            return _read > ceiling
                ? throw new InvalidOperationException(
                    $"AresCentral's answer passed {ceiling} bytes, which is more than a games list "
                    + "should be.")
                : read;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override void Flush()
        {
            // Nothing is buffered on the way out; this stream is read-only.
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
