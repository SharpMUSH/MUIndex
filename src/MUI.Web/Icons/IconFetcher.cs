using System.Net;

using MUI.Catalog.Persistence;
using MUI.Crawl;
using MUI.Discovery;

namespace MUI.Web.Icons;

/// <summary>
/// What one fetch produced, which is three things and not two.
/// </summary>
/// <remarks>
/// <see cref="Unchanged"/> is the answer to a conditional request the far end honoured: we hold the
/// right bytes and it says so. Folding it in with <see cref="Nothing"/> would file a server doing
/// exactly what we asked as a failure, back it off as though it were unreachable, and — because a
/// 304 writes no new row — leave its icon permanently stale and permanently re-asked.
/// </remarks>
public enum IconFetchOutcome
{
    /// <summary>Refused, unreachable, too big, or not an image we serve.</summary>
    Nothing,

    /// <summary>The bytes we already hold are current, on the far end's own word.</summary>
    Unchanged,

    /// <summary>An image, read and within our ceilings.</summary>
    Fetched,
}

/// <summary>One fetch: what happened, and the image where there is one.</summary>
public readonly record struct IconFetchResult(IconFetchOutcome Outcome, GameIcon? Icon)
{
    /// <summary>Nothing came back, and nothing is written about the game.</summary>
    public static readonly IconFetchResult Nothing = new(IconFetchOutcome.Nothing, null);

    /// <summary>The far end honoured our ETag.</summary>
    public static readonly IconFetchResult Unchanged = new(IconFetchOutcome.Unchanged, null);
}

/// <summary>
/// Fetches the image a game's <c>ICON</c> field names, so this site can serve it from its own origin.
/// </summary>
/// <remarks>
/// We fetch it; we do not hot-link it (§11) — the client is typed through <c>IHttpClientFactory</c>,
/// never <c>new HttpClient()</c>, since an unbounded handler pins DNS for the process lifetime on a
/// component that fetches attacker-chosen URLs. The URL is owner-controlled, so it goes through the
/// same §7.2 host-scope gate as every dial (resolve first, refuse unless every address is globally
/// routable) — do not restate this as airtight, the same TOCTOU gap applies here. A redirect is a
/// second address the gate never ruled on, so the handler follows none: exactly one hop is followed
/// <em>here</em>, with the gate run again on the target, which is the difference between an address
/// nobody checked and an address checked the same way the first one was. Every failure returns
/// <see cref="IconFetchOutcome.Nothing"/> and writes nothing about the game (rule 5).
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
    /// What happened, and the image where there is one. "Refused", "unreachable", "too big" and "not
    /// an image we serve" are one answer between them, because none of them is a fact about the game;
    /// "unchanged" is its own, because it is the far end telling us we are already right.
    /// </returns>
    public async Task<IconFetchResult> FetchAsync(
        Guid gameId,
        string url,
        string? etag = null,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
        {
            // Not a URL we could dial at all, e.g. a `javascript:` or `file:` ICON.
            return IconFetchResult.Nothing;
        }

        try
        {
            for (var hop = 0; ; hop++)
            {
                // §7.2, before any socket exists — the gate is on the resolved address, not the
                // name, and it runs again for each hop rather than once for the first.
                var ruling = await gate.InspectAsync(uri.Host, cancellationToken);

                if (!ruling.IsAllowed)
                {
                    logger?.LogDebug(
                        "Not fetching the icon at {Url}: {Ruling}. Nothing is written about the game",
                        uri, ruling.Ruling);

                    return IconFetchResult.Nothing;
                }

                using var request = new HttpRequestMessage(HttpMethod.Get, uri);

                if (etag is { Length: > 0 })
                {
                    request.Headers.TryAddWithoutValidation("If-None-Match", etag);
                }

                using var response = await client.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                // The far end honouring our ETag, which is the cheapest good answer there is.
                if (response.StatusCode is HttpStatusCode.NotModified)
                {
                    return IconFetchResult.Unchanged;
                }

                if (Redirect(response, uri) is { } moved)
                {
                    // One hop, and the handler still follows none by itself — the loop is what makes
                    // the second address a thing the gate rules on rather than a thing it never saw.
                    // One rather than a chain because each further hop is another address to clear
                    // for a decoration, and a redirect chain longer than one is somebody's tracker or
                    // somebody's loop far more often than it is a moved logo.
                    if (hop > 0)
                    {
                        logger?.LogDebug(
                            "The icon at {Url} redirects again, to {Moved}. Following one hop only", uri, moved);

                        return IconFetchResult.Nothing;
                    }

                    uri = moved;
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    return IconFetchResult.Nothing;
                }

                if (await ReadBoundedAsync(response, cancellationToken) is not { } bytes)
                {
                    return IconFetchResult.Nothing;
                }

                if (ImageHeader.Read(bytes) is not { } header)
                {
                    logger?.LogDebug(
                        "The icon at {Url} is not an image format we read. Not serving it", uri);

                    return IconFetchResult.Nothing;
                }

                if (header.Width > MaxDimension || header.Height > MaxDimension)
                {
                    return IconFetchResult.Nothing;
                }

                return new IconFetchResult(
                    IconFetchOutcome.Fetched,
                    new GameIcon(
                        gameId,
                        // The URL the ICON field named, not the one a redirect landed on: this is
                        // stored to be compared against the field next pass, and storing the target
                        // would make an unmoved field look moved every time.
                        url,
                        header.ContentType,
                        header.Width,
                        header.Height,
                        bytes,
                        response.Headers.ETag?.ToString(),
                        time.GetUtcNow()));
            }
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

            return IconFetchResult.Nothing;
        }
    }

    /// <summary>
    /// Where a response says the image has moved to, or null where it is not a redirect we would
    /// follow.
    /// </summary>
    /// <remarks>
    /// Absolute-ised against the address that answered, since <c>Location</c> is allowed to be
    /// relative; and http/https only, so a <c>Location</c> naming some other scheme is declined here
    /// rather than by whatever would have tried to dial it.
    /// </remarks>
    private static Uri? Redirect(HttpResponseMessage response, Uri from) =>
        response.StatusCode is HttpStatusCode.MovedPermanently
            or HttpStatusCode.Found
            or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect
        && response.Headers.Location is { } location
        && Uri.TryCreate(from, location, out var target)
        && target.Scheme is "http" or "https"
            ? target
            : null;

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
