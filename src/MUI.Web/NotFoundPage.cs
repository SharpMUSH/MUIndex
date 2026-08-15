using Microsoft.AspNetCore.Diagnostics;

namespace MUI.Web;

/// <summary>
/// The site's not-found page, and the rule about which requests may be answered with it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Pages only, and that boundary is the whole of this type.</b> One process serves three surfaces
/// (spec §4): the site, the read API (§10), and the account endpoints (§8). A status-code page
/// applied to the pipeline as a whole reaches all three — and an endpoint that answered
/// <c>401</c> with no body then comes back as <c>400 text/html</c>, because the re-execution runs
/// the same method against a page that does not handle it and its own failure becomes the answer.
/// Measured, not assumed: <c>passkey.js</c> renders <c>throw new Error(await response.text())</c>
/// into the sign-in status line, so a mistyped passkey reported "Bad Request" instead of being
/// refused.
/// </para>
/// <para>
/// <b>A page is something a reader navigated to</b>, so the gate is the method: <c>GET</c> or
/// <c>HEAD</c>. A <c>POST</c> is an endpoint being asked to do something and it answers for itself —
/// the sign-in above, and §8.1's claim check, which is mapped under <c>/g/</c> and would otherwise
/// have been caught by a path rule. The one path exclusion is the API, where a consumer asked for
/// JSON and a page of markup is a parse error at the far end telling them nothing.
/// </para>
/// </remarks>
public static class NotFoundPage
{
    /// <summary>Where the page lives. The router names the component; this names the URL.</summary>
    public const string Path = "/not-found";

    /// <summary>The one surface that is never answered with a page.</summary>
    private const string ApiPrefix = "/api";

    public static IApplicationBuilder UseMuiNotFoundPage(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseStatusCodePagesWithReExecute(Path);

        // Switched off per request rather than wrapped in UseWhen, and the difference was measured:
        // inside a UseWhen branch the re-execution reaches no endpoint and every unknown page comes
        // back empty, because what it re-executes is the branch rather than the application.
        // IStatusCodePagesFeature is the framework's own opt-out and leaves the middleware where it
        // works.
        return app.Use(async (context, next) =>
        {
            if (!IsAPageRequest(context)
                && context.Features.Get<IStatusCodePagesFeature>() is { } statusCodePages)
            {
                statusCodePages.Enabled = false;
            }

            await next(context);
        });
    }

    private static bool IsAPageRequest(HttpContext context) =>
        (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
        && !context.Request.Path.StartsWithSegments(ApiPrefix, StringComparison.OrdinalIgnoreCase);
}
