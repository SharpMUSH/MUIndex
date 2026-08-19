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
/// In the path, not a header or cookie alone: a locale that lives only in <c>Accept-Language</c> or
/// a cookie gives one URL two bodies, which breaks sharing, caching and indexing. The source locale
/// has no prefix — <c>/games</c> is canonical and <c>/en/games</c> redirects to it, since every
/// existing link already spells the unprefixed form. <c>Accept-Language</c> decides only the first
/// visit; once a reader has chosen, the cookie answers, so a deliberately-opened English page is
/// never bounced by browser settings on a later request.
/// </remarks>
public static class LocaleRouting
{
    /// <summary>Where the middleware leaves its answer for the rest of the request.</summary>
    public const string ItemKey = "mui.locale";

    /// <summary>
    /// Whether this request is being answered by a deployment somebody is reviewing.
    /// </summary>
    /// <remarks>
    /// Asked of the request's own services rather than injecting <c>IWebHostEnvironment</c> directly
    /// (breaks headless component tests, which render with no web host) or a <c>static bool</c> set
    /// from composition (wrong when a test process starts several hosts in different environments).
    /// A request with no <c>HttpContext</c> at all is simply not a review build.
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
            // Request.Path.Value is decoded, so a %2F/%23/%3F in a segment would turn into a literal
            // slash/fragment/query and redirect the reader somewhere they did not ask for.
            var escaped = context.Request.Path.ToUriComponent();

            if (Locales.Find(segment) is { } named)
            {
                // Permanent, since it always was one — but only for a request a redirect can carry.
                // A 301 answering a POST is followed as a GET with the body dropped, so /en/theme
                // would lose the theme it was posted.
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
    /// POST is excluded because a 302 answering a POST is followed as a GET with the body discarded —
    /// every control on this site is a form, so redirecting <c>POST /theme</c> to <c>/de/theme</c>
    /// (which no endpoint serves) locked a German reader out of changing the theme, or the language
    /// back. The API and crawler files are excluded because they are not documents in a language:
    /// <c>/api/games</c> is pinned to the source locale by name, and <c>robots.txt</c>/
    /// <c>sitemap.xml</c> each have one canonical address a crawler expects.
    /// </remarks>
    private static bool Redirectable(HttpRequest request) =>
        (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
        && !IsUnlocalized(request.Path);

    /// <summary>The paths that are the same in every language, so a prefix says nothing about them.</summary>
    public static bool IsUnlocalized(PathString path) =>
        path.StartsWithSegments(Api.ApiRoutes.Base, StringComparison.OrdinalIgnoreCase)
        || IsFile(path);

    /// <summary>
    /// The extensions this site serves as files rather than as documents.
    /// </summary>
    /// <remarks>
    /// Named by extension because the stylesheet's address carries a content fingerprint
    /// (<c>/app.gt0hup1p9v.css</c>) that changes with every build, so no fixed path list can hold it —
    /// without this, a reader with a locale cookie paid a redirect for the stylesheet on every page
    /// load. An allowlist rather than "the last segment has a dot", since <c>{Slug}</c> is a route
    /// parameter and a game could be named <c>foo.css</c>.
    /// </remarks>
    private static readonly string[] FileExtensions =
    [
        ".css", ".js", ".map", ".json", ".xml", ".txt", ".webmanifest",
        ".png", ".svg", ".ico", ".jpg", ".jpeg", ".gif", ".webp", ".avif",
        ".woff", ".woff2", ".ttf",
    ];

    /// <summary>Whether this path names a file, which is the same file in every language.</summary>
    private static bool IsFile(PathString path)
    {
        var value = path.Value;

        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        var lastSegment = value.AsSpan()[(value.LastIndexOf('/') + 1)..];
        var dot = lastSegment.LastIndexOf('.');

        if (dot < 0)
        {
            return false;
        }

        var extension = lastSegment[dot..];

        foreach (var known in FileExtensions)
        {
            if (extension.Equals(known, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The locale a reader has already chosen, where they have chosen one.</summary>
    private static LocaleContext? Remembered(HttpContext context) =>
        Locales.Find(context.Request.Cookies[Locales.CookieName]) is { IsChoosable: true } chosen
            ? new LocaleContext(chosen, FromPath: false)
            : null;

    /// <summary>
    /// The switcher's endpoint: remember a choice, and go back to the same page in it.
    /// </summary>
    /// <remarks>
    /// Anti-forgery is deliberately off here, same reasoning as the theme control: the worst a forged
    /// post can do is flip the victim's next page view to an unwanted language, visible on arrival
    /// and undone by one click. An <c>&lt;AntiforgeryToken /&gt;</c> would need to be in the header of
    /// every page (where the switcher lives), which means a <c>Set-Cookie</c> and cache-busting on
    /// every response from an otherwise-cacheable, cookie-free site. <c>SameSite=Lax</c> does not
    /// close this — it governs when a cookie is sent, not whether it may be set, so a cross-site POST
    /// still stores one — but the consequence (a stranger can change one page view's language) is not
    /// worth defending against. A <c>Sec-Fetch-Site</c> check would be worth having and costs nothing,
    /// but belongs to this endpoint and the theme endpoint together, so it's a change of its own.
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

                // Essential per the consent rules: it holds which language the reader asked for,
                // nothing that identifies them.
                IsEssential = true,
                Secure = context.Request.IsHttps,
                MaxAge = TimeSpan.FromDays(365),
            };

            // English is pinned exactly like every other locale: a reader who explicitly picked it
            // stays in it, rather than being left for a future Accept-Language guess to move again.
            context.Response.Cookies.Append(Locales.CookieName, chosen.Tag, options);
            context.Response.Redirect(Link(chosen.Tag, back));
        });

        return endpoints;
    }

    /// <summary>
    /// An address on this site, in the locale the page carrying it is being read in.
    /// </summary>
    /// <remarks>
    /// Every internal link used to be an absolute path emitted verbatim, so a German page's links —
    /// <c>href="/games"</c>, <c>href="/about"</c> — all pointed back into English. This is the one
    /// place every address passes through before becoming an attribute, so every producer
    /// (<c>ListingLinks</c>, <c>FindScreen</c>, the facet panel, the reference library) gets it free
    /// rather than each having to remember. Left untouched: anything not starting with <c>/</c>
    /// (query-only, fragment, absolute URL, <c>mailto:</c>) and anything starting <c>//</c> or
    /// <c>/\</c> (another host); <see cref="IsUnlocalized"/> covers the rest (API, robots.txt,
    /// sitemap.xml, icons, manifest). Uses the locale rather than <c>Request.PathBase</c> because the
    /// two disagree on a POST — nothing may redirect it, so it is answered in the cookie's locale with
    /// an empty <c>PathBase</c>, and reading the path base back would link to English while rendering
    /// German.
    /// </remarks>
    public static string Link(string tag, string? path)
    {
        ArgumentNullException.ThrowIfNull(tag);

        if (string.IsNullOrEmpty(path) || tag == Locales.SourceTag || path[0] != '/')
        {
            return path ?? string.Empty;
        }

        if (path.Length > 1 && path[1] is '/' or '\\')
        {
            return path;
        }

        // The path alone decides, so /games?plain=1 is localized and /sitemap.xml?x=1 is not: the
        // list is about which document an address names, and a querystring does not change that.
        var cut = path.AsSpan().IndexOfAny('?', '#');

        return IsUnlocalized(cut < 0 ? path : path[..cut]) ? path : "/" + tag + path;
    }

    /// <summary>The same, for the request a component is being rendered inside.</summary>
    /// <remarks>
    /// A component rendered with no request at all — how this suite renders most of them — is
    /// treated as the source locale.
    /// </remarks>
    public static string Link(this HttpContext? context, string? path) =>
        Link(context.LocaleOf().Tag, path);

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
            // The same rule the links on the page follow, from the same function: an alternate that
            // spelled the prefix itself would be a second copy of it to keep in step.
            yield return (locale.Tag, Link(locale.Tag, path));
        }
    }

    /// <summary>
    /// The best offered locale for a browser's own list, or null to leave the reader where they are.
    /// </summary>
    /// <remarks>
    /// Quality values are honoured (<c>de;q=0.9, en;q=0.8</c> prefers German), a match on the
    /// language subtag alone counts (<c>zh-CN</c> reaches <c>zh-Hans</c>), and anything scoring zero
    /// is treated as explicitly refused. English is scored exactly like every other candidate — a
    /// header naming a second language at a low <c>q</c> is not asking to be moved there ahead of a
    /// higher-scoring English — and only excluded once it has already won, since returning it would
    /// be a redirect to the page already being served.
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

            if (match is null)
            {
                continue;
            }

            if (quality > bestScore)
            {
                best = match;
                bestScore = quality;
            }
        }

        return best is null || best.Tag == Locales.SourceTag ? null : best;
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
    /// Arrives in a form field — whatever the poster typed — and is written into a <c>Location</c>
    /// header, so it needs the same guard as the theme endpoint: <c>//elsewhere.example</c> passes a
    /// bare <c>StartsWith('/')</c> check, several browsers treat <c>/\elsewhere.example</c> the same
    /// way, and a CR/LF is a response-splitting attempt rather than a real page.
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
