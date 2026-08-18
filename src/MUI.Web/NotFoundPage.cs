using Microsoft.AspNetCore.Diagnostics;

namespace MUI.Web;

/// <summary>
/// The site's not-found page, and the rule about which requests may be answered with it.
/// </summary>
/// <remarks>
/// <b>Pages only.</b> One process serves three surfaces (spec §4): the site, the read API (§10), and
/// account endpoints (§8). A status-code page applied to the whole pipeline reaches all three — an
/// endpoint that answered <c>401</c> with no body came back as <c>400 text/html</c>, breaking
/// <c>passkey.js</c>'s sign-in error handling, which reads the response body as the error message.
/// <b>A page is something a reader navigated to</b>, so the gate is the method (<c>GET</c>/<c>HEAD</c>);
/// a <c>POST</c> answers for itself. The API is excluded by path since a JSON consumer can't parse markup.
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

        // Switched off per request via IStatusCodePagesFeature rather than wrapped in UseWhen: inside
        // a UseWhen branch, re-execution reaches no endpoint and every unknown page comes back empty.
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
