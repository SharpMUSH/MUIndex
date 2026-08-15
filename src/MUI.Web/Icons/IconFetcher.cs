using System.Net;

using MUI.Catalog.Persistence;
using MUI.Crawl;
using MUI.Discovery;

namespace MUI.Web.Icons;

/// <summary>
/// Fetches the image a game's <c>ICON</c> field names, so this site can serve it from its own origin.
/// </summary>
/// <remarks>
/// <para>
/// <b>We fetch it; we do not hot-link it.</b> Rendering <c>&lt;img src="https://their-host/logo.png"&gt;</c>
/// would hand every reader's IP address, user-agent and referring URL to a third-party server on every
/// page view, for a decoration. §11 is a section about not doing that to people who did not ask, and a
/// reader looking at a game page did not ask.
/// </para>
/// <para>
/// <b>This is the first HTTP client in this codebase, and that is worth noticing rather than
/// slipping in.</b> Every other network operation here is a raw socket the crawler opens. It arrives
/// as a typed client through <c>IHttpClientFactory</c> — never <c>new HttpClient()</c> and never a
/// static one — because the factory is what gives the handler a bounded lifetime. On a component
/// whose whole job is fetching URLs strangers control, a socket handler that pins DNS for the life of
/// the process is the same class of bug as §7.2's time-of-check-to-time-of-use gap, and it would
/// compound it.
/// </para>
/// <para>
/// <b>The URL is owner-controlled, which is §7.2's exact hazard in a new place.</b> It goes through
/// the same gate as every dial: resolve first, refuse unless every returned address is globally
/// routable, refuse a mixed answer whole rather than picking the good one. The guard is reused rather
/// than re-derived, because a second copy of those range checks is two sets of rules that must agree
/// for ever. <b>Do not restate this as airtight</b> — the same time-of-check-to-time-of-use gap
/// applies here as there, and redirects are refused precisely because a redirect is a second address
/// the gate never ruled on.
/// </para>
/// <para>
/// <b>Every failure is a null and writes nothing.</b> A refusal, a timeout, an oversized body, an
/// unreadable header: none of them is a fact about the game, and none reaches its record (rule 5).
/// The page renders no element rather than a broken image or an apology.
/// </para>
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
    /// A ceiling that refuses rather than truncates. Half an image is not a smaller image, and a
    /// site that quietly kept the first quarter-megabyte of somebody's logo would be serving a
    /// corrupt file under their name.
    /// </remarks>
    public const int MaxBytes = 256 * 1024;

    /// <summary>
    /// The largest picture worth holding, in either direction.
    /// </summary>
    /// <remarks>
    /// It is rendered at a few dozen pixels beside a game's title. Anything past this is a poster
    /// rather than an icon, and refusing is better than resizing: an image quietly rewritten on the
    /// way through is a different picture from the one its owner published.
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
            // Not a URL we could dial at all. A `javascript:` or `file:` ICON is somebody's config
            // being odd rather than an attack, and either way there is nothing to fetch.
            return null;
        }

        // §7.2, before any socket exists. The gate is on the resolved address and not on the name:
        // icons.example.org with an A record pointing at 169.254.169.254 passes every check that
        // reads the string.
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

            // Unchanged, which is the ordinary answer and not a failure. Nothing to write: what we
            // hold is already right, and rewriting the row would only move fetched_at.
            if (response.StatusCode is HttpStatusCode.NotModified || !response.IsSuccessStatusCode)
            {
                return null;
            }

            // A redirect is a second address the gate never ruled on, and the handler is configured
            // not to follow one. Reaching here with a 3xx means the far end answered with a redirect
            // we are declining, which is not an error worth a log line.
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
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // Deliberately broad, and deliberately quiet. Somebody's web server being unreachable is
            // a fact about our afternoon; it does not enter their game's record and it is not
            // something a reader of their page should be told.
            logger?.LogDebug(error, "Could not fetch the icon at {Url}. Nothing is written", uri);

            return null;
        }
    }

    /// <summary>
    /// The body, or null if it is larger than we will hold.
    /// </summary>
    /// <remarks>
    /// <b>Read to a ceiling rather than trusted to <c>Content-Length</c>.</b> That header is what the
    /// far end says it will send, and this is a far end somebody else chose — a server that declares
    /// two kilobytes and streams two gigabytes is the whole reason the limit lives on the read.
    /// Reading one byte past the ceiling is what tells the two cases apart, so an image of exactly
    /// <see cref="MaxBytes"/> is kept and one byte more is refused rather than truncated.
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
