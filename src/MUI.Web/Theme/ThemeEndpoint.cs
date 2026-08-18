using Microsoft.AspNetCore.Mvc;
using MUI.Web.Localization;

namespace MUI.Web.Theme;

/// <summary>
/// <c>POST /theme</c> — the one write behind the control in the site header.
/// </summary>
/// <remarks>
/// <para>
/// <b>A post and not a link.</b> A GET that changes state is a state change any prefetcher or
/// crawler can trigger on a reader's behalf, and this control sits in the header of every page.
/// </para>
/// <para>
/// <b>Anti-forgery is off here, deliberately.</b> A forged post can only change colours in the
/// victim's own browser — something they can already do themselves — while rendering an
/// <c>&lt;AntiforgeryToken /&gt;</c> in every page header would force a <c>Set-Cookie</c> on every
/// response and make them all uncacheable. Real cost, no real attack.
/// </para>
/// <para>
/// Mapped whether or not a database is configured: this writes nothing but a cookie, so the demo
/// deployment can offer it too.
/// </para>
/// </remarks>
public static class ThemeEndpoint
{
    public static IEndpointRouteBuilder MapMuiTheme(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapPost(ReaderTheme.Path, (
            HttpContext context,
            [FromForm(Name = ReaderTheme.Field)] string? theme,
            [FromForm(Name = ReaderTheme.ReturnField)] string? returnTo) =>
        {
            ReaderTheme.Remember(context, ReaderTheme.Parse(theme));

            // 303 rather than 302: the reader posted a form and should now hold the page they came
            // from, fetched with GET.
            //
            // And back into the language they were reading: the return field holds Request.Path
            // with the locale prefix already stripped by the middleware, so this redirect restores it.
            context.Response.Headers.Location =
                LocaleRouting.Link(context.LocaleOf().Tag, ReaderTheme.Back(returnTo));

            return Results.StatusCode(StatusCodes.Status303SeeOther);
        }).DisableAntiforgery();

        return endpoints;
    }
}
