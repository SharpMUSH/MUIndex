using Microsoft.AspNetCore.Mvc;
using MUI.Web.Localization;

namespace MUI.Web.Theme;

/// <summary>
/// <c>POST /theme</c> — the one write behind the control in the site header.
/// </summary>
/// <remarks>
/// <para>
/// <b>A post and not a link.</b> A GET that changes state is a state change any prefetcher, link
/// checker or crawler can make on a reader's behalf, and this control sits in the header of every
/// page on the site — which is precisely where a prefetcher looks. The cost is a form element; the
/// alternative is a reader whose theme flips because their browser was being helpful.
/// </para>
/// <para>
/// <b>Anti-forgery is off here, deliberately.</b> Everything a forged post can achieve is changing
/// the colours in the poster's victim's own browser, which they can already do in their own
/// settings. Buying protection against that would mean rendering an
/// <c>&lt;AntiforgeryToken /&gt;</c> in the header of every page — and the token is stored in a
/// cookie, so it would put a <c>Set-Cookie</c> on every response from a site that otherwise sets
/// none for a reader who is not signed in, and make each of those responses uncacheable by anything
/// in front of us. That is a real cost paid against no real attack.
/// </para>
/// <para>
/// Mapped whether or not a database is configured, unlike the submission and account endpoints
/// beside it: this writes nothing but a cookie, so the demo deployment can offer it too.
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

            // 303 rather than 302: the reader posted a form and what they should now be holding is
            // the page they came from, fetched with GET. A 302 gets there by convention on every
            // browser written since 1996, and this says what was meant.
            //
            // And back into the language they were reading, which the return field cannot carry: it
            // holds Request.Path, from which the middleware has already taken the prefix. A reader
            // who followed a shared /de/… link has no cookie either, so this redirect was the whole
            // of what stood between them and an English page — for choosing a light background.
            context.Response.Headers.Location =
                LocaleRouting.Link(context.LocaleOf().Tag, ReaderTheme.Back(returnTo));

            return Results.StatusCode(StatusCodes.Status303SeeOther);
        }).DisableAntiforgery();

        return endpoints;
    }
}
