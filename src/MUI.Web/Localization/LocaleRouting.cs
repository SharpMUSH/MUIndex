using Microsoft.AspNetCore.Http.Extensions;

namespace MUI.Web.Localization;

/// <summary>Which locale this request is being answered in, and how it was decided.</summary>
/// <param name="Locale">The locale in force.</param>
/// <param name="FromPath">
/// Whether a path segment named it — which is what makes a locale linkable, cacheable and
/// indexable rather than a property of whoever's cookie jar the request arrived with.
/// </param>
public sealed record LocaleContext(Locale Locale, bool FromPath)
{
    /// <summary>The tag, which is what every message lookup is keyed on.</summary>
    public string Tag => Locale.Tag;

    /// <summary>The source locale, for a request nothing has decided yet.</summary>
    public static LocaleContext Default { get; } = new(Locales.Source, FromPath: false);
}

/// <summary>
/// The locale in the path, and the two ways a reader gets one.
/// </summary>
/// <remarks>
/// <para>
/// <b>In the path and not in a header.</b> A locale that lives only in a cookie or in
/// <c>Accept-Language</c> gives one URL two bodies: a shared link opens in the sender's language for
/// them and the recipient's for everybody else, a cache in front of the site serves whichever
/// arrived first to whoever asks next, and a search engine indexes one of them arbitrarily.
/// <c>/de/games?plain=1</c> is one address for one document, which is the same argument the
/// querystring already wins for the filters.
/// </para>
/// <para>
/// <b>The source locale has no prefix.</b> <c>/games</c> is the canonical English address and
/// <c>/en/games</c> redirects to it, because two URLs for one document is the thing this is
/// avoiding — and every link written across this site, every bookmark and every inbound link
/// already spells the unprefixed one.
/// </para>
/// <para>
/// <b><c>Accept-Language</c> decides the first visit and nothing after it.</b> A header is a
/// standing preference about content in general and not a choice about this site, so it is worth one
/// redirect and no more: once a reader has chosen, the cookie is what answers, and a reader who
/// deliberately opened the English page must not be bounced out of it by their browser's settings on
/// every request.
/// </para>
/// </remarks>
public static class LocaleRouting
{
    /// <summary>Where the middleware leaves its answer for the rest of the request.</summary>
    public const string ItemKey = "mui.locale";

    /// <summary>The locale this request is being answered in.</summary>
    public static LocaleContext LocaleOf(this HttpContext? context) =>
        context?.Items.TryGetValue(ItemKey, out var found) is true && found is LocaleContext ctx
            ? ctx
            : LocaleContext.Default;

    /// <summary>Reads the locale out of the path, or decides one for a request that carries none.</summary>
    public static IApplicationBuilder UseMuiLocale(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use(async (context, next) =>
        {
            var path = context.Request.Path.Value ?? "/";
            var segment = FirstSegment(path);

            if (Locales.Find(segment) is { } named)
            {
                // The canonical English address carries no prefix, so /en/... is a second URL for a
                // document that already has one. Permanent, because it always was one.
                if (named.Tag == Locales.SourceTag)
                {
                    context.Response.Redirect(Rest(path) + context.Request.QueryString, permanent: true);
                    return;
                }

                context.Items[ItemKey] = new LocaleContext(named, FromPath: true);

                // The rest of the pipeline routes, links and renders as though the prefix were not
                // there — which is what lets every @page directive stay written once.
                context.Request.PathBase = context.Request.PathBase.Add("/" + segment);
                context.Request.Path = Rest(path);

                await next(context);
                return;
            }

            // No prefix. A reader who has chosen is sent to their choice; a reader who has not is
            // offered one exactly once, off the header their browser sends.
            var remembered = Locales.Find(context.Request.Cookies[Locales.CookieName]);

            if (remembered is { IsOffered: true } && remembered.Tag != Locales.SourceTag)
            {
                context.Response.Redirect("/" + remembered.Tag + path + context.Request.QueryString);
                return;
            }

            if (remembered is null && Preferred(context.Request.Headers.AcceptLanguage) is { } guessed)
            {
                context.Response.Redirect("/" + guessed.Tag + path + context.Request.QueryString);
                return;
            }

            context.Items[ItemKey] = LocaleContext.Default;

            await next(context);
        });
    }

    /// <summary>The switcher's endpoint: remember a choice, and go back to the same page in it.</summary>
    public static IEndpointRouteBuilder MapMuiLocale(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapPost(Locales.Path, async context =>
        {
            var form = await context.Request.ReadFormAsync();
            var chosen = Locales.Find(form[Locales.Field]) ?? Locales.Source;
            var back = Back(form[Locales.ReturnField]);

            var options = new CookieOptions
            {
                Path = "/",
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,

                // Essential in the sense the consent rules mean: it is which language a reader
                // asked to read in, held because they asked, and it identifies nobody.
                IsEssential = true,
                Secure = context.Request.IsHttps,
                MaxAge = TimeSpan.FromDays(365),
            };

            if (chosen.Tag == Locales.SourceTag)
            {
                // The source language is the absence of a choice rather than a choice of its own,
                // which is what stops a reader who picks English being pinned out of a future
                // Accept-Language answer. Same shape as the theme control's "auto".
                context.Response.Cookies.Delete(Locales.CookieName, options);
                context.Response.Redirect(back);
                return;
            }

            context.Response.Cookies.Append(Locales.CookieName, chosen.Tag, options);
            context.Response.Redirect("/" + chosen.Tag + back);
        });

        return endpoints;
    }

    /// <summary>
    /// The same page, in every locale it exists in — for <c>&lt;link rel="alternate"&gt;</c>.
    /// </summary>
    /// <remarks>
    /// <c>x-default</c> points at the unprefixed address, which is what tells a search engine that
    /// the English URL is the one to show a reader whose language nothing here matches, rather than
    /// having it pick one of the seven.
    /// </remarks>
    public static IEnumerable<(string HrefLang, string Path)> Alternates(string pathWithinLocale)
    {
        ArgumentNullException.ThrowIfNull(pathWithinLocale);

        var path = pathWithinLocale.Length == 0 ? "/" : pathWithinLocale;

        yield return ("x-default", path);

        foreach (var locale in Locales.Offered)
        {
            yield return (locale.Tag, locale.Tag == Locales.SourceTag ? path : "/" + locale.Tag + path);
        }
    }

    /// <summary>
    /// The best offered locale for a browser's own list, or null to leave the reader where they are.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Quality values are honoured because a browser sends them meaning something — <c>de;q=0.9,
    /// en;q=0.8</c> is a reader who reads both and prefers German — and a match on the language
    /// subtag alone counts, so a reader asking for <c>zh-CN</c> reaches <c>zh-Hans</c>. Anything
    /// scoring zero is a language the reader has explicitly refused.
    /// </para>
    /// <para>
    /// English never wins here, because English is where the reader already is: returning it would
    /// be a redirect to the page being served.
    /// </para>
    /// </remarks>
    public static Locale? Preferred(string? acceptLanguage)
    {
        if (string.IsNullOrWhiteSpace(acceptLanguage))
        {
            return null;
        }

        var best = default(Locale);
        var bestScore = 0d;

        foreach (var part in acceptLanguage.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var bits = part.Split(';', StringSplitOptions.TrimEntries);
            var tag = bits[0];

            var quality = 1d;

            foreach (var parameter in bits.Skip(1))
            {
                if (parameter.StartsWith("q=", StringComparison.OrdinalIgnoreCase)
                    && double.TryParse(parameter[2..], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var q))
                {
                    quality = q;
                }
            }

            if (quality <= 0 || tag == "*")
            {
                continue;
            }

            var match = Locales.Offered.FirstOrDefault(l => Matches(l.Tag, tag));

            if (match is null || match.Tag == Locales.SourceTag)
            {
                continue;
            }

            if (quality > bestScore)
            {
                best = match;
                bestScore = quality;
            }
        }

        return best;
    }

    /// <summary>Whether an offered tag answers to what a browser asked for.</summary>
    /// <remarks>
    /// Both directions, on the language subtag: <c>zh-Hans</c> answers a request for <c>zh</c> and
    /// for <c>zh-CN</c> alike, because the script is ours to choose and the region is not something
    /// this site varies on.
    /// </remarks>
    private static bool Matches(string offered, string asked) =>
        offered.Equals(asked, StringComparison.OrdinalIgnoreCase)
        || Language(offered).Equals(Language(asked), StringComparison.OrdinalIgnoreCase);

    private static string Language(string tag)
    {
        var dash = tag.IndexOf('-', StringComparison.Ordinal);

        return dash < 0 ? tag : tag[..dash];
    }

    private static string FirstSegment(string path)
    {
        var trimmed = path.AsSpan().TrimStart('/');
        var slash = trimmed.IndexOf('/');

        return (slash < 0 ? trimmed : trimmed[..slash]).ToString();
    }

    private static string Rest(string path)
    {
        var trimmed = path.AsSpan().TrimStart('/');
        var slash = trimmed.IndexOf('/');

        return slash < 0 ? "/" : trimmed[slash..].ToString();
    }

    /// <summary>
    /// The page to return to, as a path on this site.
    /// </summary>
    /// <remarks>
    /// It arrives in a form field, so it is whatever the poster typed rather than whatever we
    /// rendered — and it is written into a <c>Location</c> header. Same guard as the theme
    /// endpoint's, and for the same reasons: <c>//elsewhere.example</c> is a different host wearing
    /// a path's clothes and walks straight through a <c>StartsWith('/')</c> check, several browsers
    /// read <c>/\elsewhere.example</c> as the same thing, and a CR or LF here is a response-splitting
    /// attempt rather than a page anybody asked for.
    /// </remarks>
    public static string Back(string? path) =>
        path is { Length: > 1 }
        && path[0] == '/'
        && path[1] is not ('/' or '\\')
        && path.All(c => c is >= ' ' and < (char)0x7f)

            // Any locale prefix already on it is stripped: the field carries where the reader is,
            // and the endpoint decides which language that page is served in.
            ? StripLocale(path)
            : "/";

    private static string StripLocale(string path) =>
        Locales.IsLocaleSegment(FirstSegment(path)) ? Rest(path) : path;

    /// <summary>This request's path with its locale prefix removed, for the alternates.</summary>
    public static string PathWithinLocale(this HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Request.Path.Value is { Length: > 0 } path ? path : "/";
    }

    /// <summary>The absolute address of this request, for a canonical or an alternate.</summary>
    public static string Absolute(this HttpContext context, string path)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new Uri(new Uri(UriHelper.BuildAbsolute(
            context.Request.Scheme, context.Request.Host)), path).ToString();
    }
}
