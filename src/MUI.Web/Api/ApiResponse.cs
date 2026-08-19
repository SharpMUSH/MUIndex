using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace MUI.Web.Api;

/// <summary>
/// The one place an API payload becomes bytes: serialise, hash, honour <c>If-None-Match</c>, write.
/// </summary>
/// <remarks>Every route goes through this rather than returning <c>Results.Ok</c> directly, so ETag/conditional caching is a property of the whole surface, not of endpoints that remembered to add it.</remarks>
public static class ApiResponse
{
    /// <summary>A minute — the window within which the bytes are provably identical (see <see cref="ApiClock"/>).</summary>
    public const string CacheControl = "public, max-age=60";

    /// <param name="cacheControl">
    /// A lifetime other than the default minute. Must be passed in, not set by the caller afterwards
    /// — <see cref="WriteAsync"/> starts the response, and a header set after that is discarded or throws.
    /// </param>
    public static Task WriteJsonAsync<T>(HttpContext http, T payload, string? cacheControl = null)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(payload, ApiJson.Options);
        return WriteAsync(http, body, "application/json; charset=utf-8", ETag.Of(body), cacheControl);
    }

    public static Task WriteTextAsync(
        HttpContext http, string text, string contentType, string? cacheControl = null)
    {
        var body = Encoding.UTF8.GetBytes(text);
        return WriteAsync(http, body, contentType, ETag.Of(body), cacheControl);
    }

    public static async Task WriteAsync(
        HttpContext http, byte[] body, string contentType, string etag, string? cacheControl = null)
    {
        Prepare(http, contentType, etag, cacheControl);

        if (NotModified(http, etag))
        {
            return;
        }

        http.Response.ContentLength = body.Length;
        await http.Response.Body.WriteAsync(body, http.RequestAborted);
    }

    /// <summary>Sets the headers every API response carries, before anything is written.</summary>
    public static void Prepare(
        HttpContext http, string contentType, string etag, string? cacheControl = null)
    {
        var headers = http.Response.Headers;
        headers[HeaderNames.ETag] = etag;
        headers[HeaderNames.CacheControl] = cacheControl ?? CacheControl;

        // Relaxed-escaped JSON must not be sniffed as a document by a browser. See ApiJson.
        headers[HeaderNames.XContentTypeOptions] = "nosniff";

        // Open CORS: a read-only public dataset, browser-side code elsewhere may read it (§10).
        headers[HeaderNames.AccessControlAllowOrigin] = "*";

        // Licence travels with every response, not just the bulk dump, so no route ships unlabelled.
        if (http.RequestServices.GetService<IOptions<DatasetLicenceOptions>>()?.Value is { } licence)
        {
            headers["X-MUIndex-Licence"] = licence.LicenceId;
            if (licence.LicenceUrl is { Length: > 0 } url)
            {
                headers[HeaderNames.Link] = $"<{url}>; rel=\"license\"";
            }
        }

        http.Response.ContentType = contentType;
    }

    /// <summary>
    /// Turns the response into a 304 when the caller already holds these bytes. Call after
    /// <see cref="Prepare"/> — a 304 must repeat the validator it matched.
    /// </summary>
    public static bool NotModified(HttpContext http, string etag)
    {
        if (!ETag.Matches(http.Request.Headers[HeaderNames.IfNoneMatch], etag))
        {
            return false;
        }

        http.Response.StatusCode = StatusCodes.Status304NotModified;

        // RFC 9110: a 304 carries no body and must not have a Content-Length.
        http.Response.Headers.Remove(HeaderNames.ContentType);
        http.Response.ContentLength = null;
        return true;
    }

    /// <summary>A refusal in the same shape as every other error, so a consumer parses one type.</summary>
    public static async Task ProblemAsync(HttpContext http, int status, string title, string detail)
    {
        http.Response.StatusCode = status;
        http.Response.ContentType = "application/problem+json; charset=utf-8";
        http.Response.Headers[HeaderNames.CacheControl] = "no-store";
        await http.Response.WriteAsync(
            JsonSerializer.Serialize(new ApiProblem(status, title, detail), ApiJson.Options),
            http.RequestAborted);
    }
}

/// <summary>RFC 9457's shape, minus the fields nothing here can fill honestly.</summary>
public sealed record ApiProblem(int Status, string Title, string Detail);
