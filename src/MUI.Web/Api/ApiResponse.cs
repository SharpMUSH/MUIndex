using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace MUI.Web.Api;

/// <summary>
/// The one place an API payload becomes bytes: serialise, hash, honour <c>If-None-Match</c>, write.
/// </summary>
/// <remarks>
/// Every route goes through this rather than returning <c>Results.Ok</c>, because "ETagged and
/// conditionally cacheable" is a property of the whole surface and not of the endpoints somebody
/// remembered. A route that forgot would be indistinguishable from one that could not be cached.
/// </remarks>
public static class ApiResponse
{
    /// <summary>
    /// A minute, which is the interval within which the bytes are provably identical
    /// (see <see cref="ApiClock"/>). Beyond it the ages really have moved, so a revalidation is a
    /// real question rather than a wasted round trip.
    /// </summary>
    public const string CacheControl = "public, max-age=60";

    public static Task WriteJsonAsync<T>(HttpContext http, T payload)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(payload, ApiJson.Options);
        return WriteAsync(http, body, "application/json; charset=utf-8", ETag.Of(body));
    }

    public static Task WriteTextAsync(HttpContext http, string text, string contentType)
    {
        var body = Encoding.UTF8.GetBytes(text);
        return WriteAsync(http, body, contentType, ETag.Of(body));
    }

    public static async Task WriteAsync(
        HttpContext http, byte[] body, string contentType, string etag)
    {
        Prepare(http, contentType, etag);

        if (NotModified(http, etag))
        {
            return;
        }

        http.Response.ContentLength = body.Length;
        await http.Response.Body.WriteAsync(body, http.RequestAborted);
    }

    /// <summary>Sets the headers every API response carries, before anything is written.</summary>
    public static void Prepare(HttpContext http, string contentType, string etag)
    {
        var headers = http.Response.Headers;
        headers[HeaderNames.ETag] = etag;
        headers[HeaderNames.CacheControl] = CacheControl;

        // The JSON is served relaxed-escaped, so a browser must not be allowed to guess it is a
        // document. See ApiJson.
        headers[HeaderNames.XContentTypeOptions] = "nosniff";

        // A read-only public dataset. Republishing rather than siloing means a rival directory's
        // browser-side code can read this, which is the whole point of §10.
        headers[HeaderNames.AccessControlAllowOrigin] = "*";

        // The terms travel with every response and not only with the bulk dump. Somebody
        // republishing three fields off the listing is under the same licence as somebody taking the
        // whole catalogue, and a consumer should not have to fetch a different route to learn it.
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
    /// Turns the response into a 304 when the caller already holds these bytes, and says whether it
    /// did so. Call after <see cref="Prepare"/> — a 304 must repeat the validator it matched.
    /// </summary>
    public static bool NotModified(HttpContext http, string etag)
    {
        if (!ETag.Matches(http.Request.Headers[HeaderNames.IfNoneMatch], etag))
        {
            return false;
        }

        http.Response.StatusCode = StatusCodes.Status304NotModified;

        // A 304 carries no body, and RFC 9110 forbids it a Content-Length describing one.
        http.Response.Headers.Remove(HeaderNames.ContentType);
        http.Response.ContentLength = null;
        return true;
    }

    /// <summary>
    /// A refusal in the same shape as everything else, so a consumer parses one error type.
    /// </summary>
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
