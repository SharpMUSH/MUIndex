using System.Net;

using MUI.Catalog.Persistence;
using MUI.Crawl;
using MUI.Discovery;

namespace MUI.Web.Icons;

/// <summary>
/// Fetches the image a game's <c>ICON</c> field names, so this site can serve it from its own origin.
/// </summary>
/// <remarks>
/// We fetch it; we do not hot-link it (§11) — the client is typed through <c>IHttpClientFactory</c>,
/// never <c>new HttpClient()</c>, since an unbounded handler pins DNS for the process lifetime on a
/// component that fetches attacker-chosen URLs. The URL is owner-controlled, so it goes through the
/// same §7.2 host-scope gate as every dial (resolve first, refuse unless every address is globally
/// routable) — do not restate this as airtight, the same TOCTOU gap applies here, and redirects are
/// refused because a redirect is a second address the gate never ruled on. Every failure returns null
/// and writes nothing: none of it is a fact about the game (rule 5).
/// </remarks>
public sealed class IconFetcher(
    HttpClient client,
    IHostScopeGuard gate,
    TimeProvider time,
    ILogger<IconFetcher>? logger = null)
{
    /// <summary>
    /// The most we will hold for one icon.
    /// </summary>
    /// <remarks>
    /// A ceiling that refuses rather than truncates — half an image is not a smaller image.
    /// </remarks>
    public const int MaxBytes = 256 * 1024;

    /// <summary>
    /// The largest picture worth holding, in either direction.
    /// </summary>
    /// <remarks>
    /// Refusing is better than resizing: an image quietly rewritten on the way through is a different
    /// picture from the one its owner published.
    /// </remarks>
    public const int MaxDimension = 512;

    /// <summary>
    /// Fetches an icon, or returns null and says nothing about the game.
    /// </summary>
    /// <param name="gameId">The game the icon belongs to.</param>
    /// <param name="url">The URL its <c>ICON</c> field names, which somebody else chose.</param>
    /// <param name="etag">What the far end gave us last time, so this can ask conditionally.</param>
    /// <param name="cancellationToken">The refresher's budget.</param>
    /// <returns>
    /// The icon, or null — which covers "refused", "unreachable", "too big", "not an image we serve"
    /// and "unchanged since last time" alike, because none of them is a reason to change what we hold.
    /// </returns>
    public async Task<GameIcon?> FetchAsync(
        Guid gameId,
        string url,
        string? etag = null,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
        {
            // Not a URL we could dial at all, e.g. a `javascript:` or `file:` ICON.
            return null;
        }

        // §7.2, before any socket exists — the gate is on the resolved address, not the name.
        var ruling = await gate.InspectAsync(uri.Host, cancellationToken);

        if (!ruling.IsAllowed)
        {
            logger?.LogDebug(
                "Not fetching the icon at {Url}: {Ruling}. Nothing is written about the game",
                uri, ruling.Ruling);

            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);

        if (etag is { Length: > 0 })
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        }

        try
        {
            using var response = await client.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            // Unchanged is the ordinary answer, not a failure — nothing to write.
            if (response.StatusCode is HttpStatusCode.NotModified || !response.IsSuccessStatusCode)
            {
                return null;
            }

            // A 3xx here means a redirect we're declining — the handler doesn't follow them, since a
            // redirect is a second address the gate never ruled on.
            if (await ReadBoundedAsync(response, cancellationToken) is not { } bytes)
            {
                return null;
            }

            if (ImageHeader.Read(bytes) is not { } header)
            {
                logger?.LogDebug(
                    "The icon at {Url} is not a PNG, JPEG, GIF or WebP we can read. Not serving it", uri);

                return null;
            }

            if (header.Width > MaxDimension || header.Height > MaxDimension)
            {
                return null;
            }

            return new GameIcon(
                gameId,
                url,
                header.ContentType,
                header.Width,
                header.Height,
                bytes,
                response.Headers.ETag?.ToString(),
                time.GetUtcNow());
        }
        catch (Exception error)
            when (error is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Deliberately broad and quiet: a web server being unreachable is not a fact about the
            // game. The second clause matters — `HttpClient` reports its own Timeout elapsing as a
            // TaskCanceledException (an OperationCanceledException by type), so a filter that trusted
            // the type alone let a stalled far end masquerade as this host shutting down and
            // propagate into a real shutdown. Ask the token who actually cancelled, never the exception.
            logger?.LogDebug(error, "Could not fetch the icon at {Url}. Nothing is written", uri);

            return null;
        }
    }

    /// <summary>
    /// The body, or null if it is larger than we will hold.
    /// </summary>
    /// <remarks>
    /// Read to a ceiling rather than trusted to <c>Content-Length</c>, which the far end could declare
    /// falsely. An image of exactly <see cref="MaxBytes"/> is kept; one byte more is refused, not
    /// truncated.
    /// </remarks>
    private static async Task<byte[]?> ReadBoundedAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();

        var chunk = new byte[8 * 1024];

        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken);

            if (read == 0)
            {
                return buffer.ToArray();
            }

            buffer.Write(chunk, 0, read);

            if (buffer.Length > MaxBytes)
            {
                return null;
            }
        }
    }
}
