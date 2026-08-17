using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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

    /// <summary>
    /// Whether this request is being answered by a deployment somebody is reviewing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Asked of the request, because the alternatives were a global and an injection and both
    /// had already failed.</b> A component taking <c>IWebHostEnvironment</c> as a dependency cannot
    /// be rendered without a web host behind it, and every headless component test in this suite
    /// renders one without. A <c>static bool</c> written from composition fixed that and bought a
    /// worse problem: a test process starts many hosts, in Development and in Production, so the
    /// last one to start decided what the switcher listed on every request served by any of them.
    /// </para>
    /// <para>
    /// The request's own services answer it, and a request that has none — a component rendered
    /// with no <c>HttpContext</c> at all — is not a review build. Nothing has to be told; the
    /// question simply has an answer wherever it is asked.
    /// </para>
    /// </remarks>
    public static bool IsReviewBuild(this HttpContext? context) =>
        context?.RequestServices?.GetService<IHostEnvironment>()?.IsDevelopment() is true;

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

            // The same path, still escaped, for anything written into a Location header.
            //
            // Request.Path.Value is decoded: a segment containing %2F comes back as a bare slash and
            // becomes two segments, a %23 comes back as a `#` and truncates the target at the
            // fragment, a %3F becomes a `?` and turns the rest of the path into a query, and every
            // non-ASCII character comes back raw into a header field that may not carry one. A
            // reader following that redirect is sent somewhere they did not ask for. Splitting on
            // '/' is still right here, because in the escaped form a literal slash is the only
            // separator and an encoded one is three characters that are not it.
            var escaped = context.Request.Path.ToUriComponent();

            if (Locales.Find(segment) is { } named)
            {
                // The canonical English address carries no prefix, so /en/... is a second URL for a
                // document that already has one. Permanent, because it always was one — but only for
                // a request a redirect can carry. A 301 answering a POST is followed as a GET with
                // the body dropped, so /en/theme would lose the theme it was posted.
                if (named.Tag == Locales.SourceTag)
                {
                    if (Redirectable(context.Request))
                    {
                        context.Response.Redirect(Rest(escaped) + context.Request.QueryString, permanent: true);
                        return;
                    }

                    context.Items[ItemKey] = LocaleContext.Default;
                    context.Request.Path = Rest(path);

                    await next(context);
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

            // No prefix, and nothing here may move this request. A locale is a property of a
            // document, so only a request for a document is worth relocating.
            if (!Redirectable(context.Request))
            {
                context.Items[ItemKey] = Remembered(context) ?? LocaleContext.Default;

                await next(context);
                return;
            }

            // A reader who has chosen is sent to their choice; a reader who has not is offered one
            // exactly once, off the header their browser sends.
            var remembered = Locales.Find(context.Request.Cookies[Locales.CookieName]);

            if (remembered is { IsChoosable: true } && remembered.Tag != Locales.SourceTag)
            {
                context.Response.Redirect("/" + remembered.Tag + escaped + context.Request.QueryString);
                return;
            }

            if (remembered is null && Preferred(context.Request.Headers.AcceptLanguage) is { } guessed)
            {
                context.Response.Redirect("/" + guessed.Tag + escaped + context.Request.QueryString);
                return;
            }

            context.Items[ItemKey] = LocaleContext.Default;

            await next(context);
        });
    }

    /// <summary>
    /// Whether this request is one a locale redirect may move.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The locale was a one-way door, and the switcher out of it was the thing it shut.</b> A 302
    /// answering a POST is followed as a GET with the body discarded, and every control on this site
    /// is a form: with <c>mui_locale=de</c> set, <c>POST /theme</c> was answered with a redirect to
    /// <c>/de/theme</c>, which no endpoint serves, so a German reader could not change the theme —
    /// and <c>POST /locale</c> went the same way, so they could not change the language back either.
    /// </para>
    /// <para>
    /// The API and the crawler's own files are excluded for a different reason. They are not
    /// documents in a language: <c>/api/games</c> answers the same JSON to every reader — it is
    /// pinned to the source locale by name — and <c>robots.txt</c> and <c>sitemap.xml</c> have one
    /// canonical address each, which is where a crawler looks and where the sitemap says they are.
    /// Bouncing them through a prefix cost a round trip and published a second URL for a file that
    /// is supposed to have exactly one.
    /// </para>
    /// </remarks>
    private static bool Redirectable(HttpRequest request) =>
        (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
        && !IsUnlocalized(request.Path);

    /// <summary>The paths that are the same in every language, so a prefix says nothing about them.</summary>
    private static bool IsUnlocalized(PathString path) =>
        path.StartsWithSegments(Api.ApiRoutes.Base, StringComparison.OrdinalIgnoreCase)
        || path.Equals("/robots.txt", StringComparison.OrdinalIgnoreCase)
        || path.Equals("/sitemap.xml", StringComparison.OrdinalIgnoreCase)
        || path.Equals("/favicon.ico", StringComparison.OrdinalIgnoreCase)
        || path.Equals("/favicon.svg", StringComparison.OrdinalIgnoreCase)
        || path.Equals("/site.webmanifest", StringComparison.OrdinalIgnoreCase);

    /// <summary>The locale a reader has already chosen, where they have chosen one.</summary>
    private static LocaleContext? Remembered(HttpContext context) =>
        Locales.Find(context.Request.Cookies[Locales.CookieName]) is { IsChoosable: true } chosen
            ? new LocaleContext(chosen, FromPath: false)
            : null;

    /// <summary>
    /// The switcher's endpoint: remember a choice, and go back to the same page in it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Anti-forgery is off here, deliberately, and on the same reasoning as the theme control
    /// beside it.</b> Everything a forged post can achieve is that the victim's next page view is
    /// in a language they did not pick — visible on arrival, stated in their own language's name in
    /// the switcher, and undone by one click of the control that is already on the page. Nothing is
    /// read, nothing is written that survives the reader clearing it, and this site holds no
    /// user-specific state beyond a theme and a language.
    /// </para>
    /// <para>
    /// The price of buying protection against that is not a token: it is an
    /// <c>&lt;AntiforgeryToken /&gt;</c> in the header of <em>every page</em>, because that is where
    /// the switcher is. The token rides in a cookie, so it would put a <c>Set-Cookie</c> on every
    /// response from a site that otherwise sets none for a signed-out reader, and make every one of
    /// those responses uncacheable by anything in front of us — and then answer a reader whose
    /// cached page outlived its token with a 400 where they expected a language.
    /// </para>
    /// <para>
    /// <b><c>SameSite=Lax</c> is not what makes this safe, and it is worth being exact about why.</b>
    /// SameSite governs when a cookie is <em>sent</em>, not whether one may be <em>set</em>: a
    /// cross-site form post is a top-level navigation, the <c>Set-Cookie</c> in the answer is
    /// stored, and a later top-level GET does carry a Lax cookie. The attack works. What makes it
    /// not worth defending against is its consequence, which is that a stranger can change the
    /// language of one page view.
    /// </para>
    /// <para>
    /// The measure that would be worth having is a <c>Sec-Fetch-Site</c> check, which costs no
    /// token, no cookie, no cache entry and no script. It belongs to this endpoint and the theme
    /// endpoint together — one control guarded and its twin not is worse than neither — so it is a
    /// change of its own rather than a line here.
    /// </para>
    /// </remarks>
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
