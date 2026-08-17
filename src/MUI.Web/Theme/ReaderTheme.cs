namespace MUI.Web.Theme;

/// <summary>What a reader has said about which theme they want.</summary>
/// <remarks>
/// <b>Three members and not two.</b> "Follow my system" is an answer, and the only one that is still
/// right at both ends of the day for a reader whose desktop switches at dusk. A two-state flip
/// cannot express it, and once a reader has been moved off it there is no way back.
/// </remarks>
public enum ThemeChoice
{
    /// <summary>No choice made: <c>prefers-color-scheme</c> decides, in the stylesheet.</summary>
    System,

    Light,

    Dark,
}

/// <summary>
/// The reader's theme — the cookie it lives in, the attribute it writes, and the words the control
/// is built out of.
/// </summary>
/// <remarks>
/// <para>
/// <b>Server-side, because this site runs no script.</b> <c>App.razor</c> deliberately omits
/// <c>blazor.web.js</c> and the facet panel and the plain surface both depend on that; a preference
/// kept in <c>localStorage</c> and applied by an inline script would have been the first blocking
/// script on every page and dead for exactly the readers the no-script guarantee is for. A cookie
/// read while the document is being rendered also cannot flash: there is no moment at which the
/// wrong theme is painted.
/// </para>
/// <para>
/// <b>The stylesheet was already ready for this.</b> <c>app.css</c> has carried
/// <c>:root[data-theme="light"]</c> and <c>[data-theme="dark"]</c> since the tokens were written,
/// specifically so a control could be added without moving a single colour. Nothing here defines a
/// theme; it decides which one the document names.
/// </para>
/// <para>
/// <b>Absent means absent.</b> With no cookie the attribute is not written at all, rather than
/// written with the value we would guess. The guess would be wrong for every reader whose system
/// says light, which is the bug the attribute was left off the document to avoid in the first place.
/// </para>
/// </remarks>
public static class ReaderTheme
{
    /// <summary>Where the choice is kept.</summary>
    /// <remarks>
    /// Prefixed like the rest of this deployment's cookies. It is read by the server while the page
    /// is being composed and never by a script, so it is <c>HttpOnly</c>.
    /// </remarks>
    public const string CookieName = "mui_theme";

    /// <summary>The endpoint the control posts to.</summary>
    public const string Path = "/theme";

    /// <summary>The form field carrying the chosen theme.</summary>
    public const string Field = "theme";

    /// <summary>The form field carrying the page to come back to.</summary>
    public const string ReturnField = "return";

    /// <summary>
    /// How long a choice is remembered. Long, because it is a preference and not a session.
    /// </summary>
    private static readonly TimeSpan Remembered = TimeSpan.FromDays(365);

    /// <summary>The three, in the order the control offers them.</summary>
    public static IReadOnlyList<ThemeChoice> Choices { get; } =
        [ThemeChoice.System, ThemeChoice.Light, ThemeChoice.Dark];

    /// <summary>What this reader has chosen, or <see cref="ThemeChoice.System"/> if nothing.</summary>
    public static ThemeChoice Of(HttpContext? context) =>
        Parse(context?.Request.Cookies[CookieName]);

    /// <summary>
    /// A word off the wire, read as a choice — and anything we did not write is no choice.
    /// </summary>
    /// <remarks>
    /// One parser for the cookie and the form field, so a value that round-trips through the control
    /// cannot mean one thing on the way out and another on the way back. <c>auto</c> lands here as
    /// <see cref="ThemeChoice.System"/> along with the nonsense, which is right: both mean "we are
    /// not pinning anything".
    /// </remarks>
    public static ThemeChoice Parse(string? value) => value switch
    {
        "light" => ThemeChoice.Light,
        "dark" => ThemeChoice.Dark,
        _ => ThemeChoice.System,
    };

    /// <summary>The word for a choice, on the wire and on the button alike.</summary>
    public static string Word(ThemeChoice choice) => choice switch
    {
        ThemeChoice.Light => "light",
        ThemeChoice.Dark => "dark",
        _ => "auto",
    };

    /// <summary>
    /// The value of <c>data-theme</c> on the document, or <see langword="null"/> to leave it off.
    /// </summary>
    /// <remarks>
    /// Null rather than "system": Razor omits an attribute whose value is null, and an omitted
    /// attribute is what hands the decision back to the media query in <c>app.css</c>.
    /// </remarks>
    public static string? Attribute(ThemeChoice choice) =>
        choice is ThemeChoice.System ? null : Word(choice);

    /// <summary>
    /// What to tell the browser its form controls, scrollbars and canvas should be.
    /// </summary>
    /// <remarks>
    /// Both when the reader has not chosen, so the browser follows the same signal the stylesheet
    /// does. One when they have — otherwise a reader who pinned light still gets dark scrollbars and
    /// a dark date picker on a system set to dark, which is the same disagreement
    /// <see cref="Plate"/> exists to settle for the chrome above the page.
    /// </remarks>
    public static string ColorScheme(ThemeChoice choice) =>
        choice is ThemeChoice.System ? "dark light" : Word(choice);

    /// <summary>
    /// The page colour a phone paints its chrome with — <c>--bg</c>, for each theme.
    /// </summary>
    /// <remarks>
    /// <b>These two values also live in <c>app.css</c>, and they move together.</b> The head has to
    /// name a colour before any stylesheet has been parsed, so there is no way to read it from the
    /// tokens; the duplication is the price of <c>theme-color</c> existing at all.
    /// </remarks>
    public static string Plate(ThemeChoice choice) =>
        choice is ThemeChoice.Light ? "#f7f8f9" : "#0f1113";

    /// <summary>Remembers a choice, or forgets one.</summary>
    /// <remarks>
    /// <b>"Auto" deletes the cookie rather than writing today's system answer into it.</b> The two
    /// look identical on the afternoon they are chosen and diverge that evening — and the server
    /// could not write the right value anyway, because <c>prefers-color-scheme</c> never reaches it.
    /// The delete carries the same path and flags as the write, or a browser keeps the old one.
    /// </remarks>
    public static void Remember(HttpContext context, ThemeChoice choice)
    {
        ArgumentNullException.ThrowIfNull(context);

        var options = new CookieOptions
        {
            Path = "/",
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,

            // Essential in the sense the consent rules mean: this is the reader's own display
            // setting, held because they asked for it, and it identifies nobody.
            IsEssential = true,
            Secure = context.Request.IsHttps,
        };

        if (choice is ThemeChoice.System)
        {
            context.Response.Cookies.Delete(CookieName, options);
            return;
        }

        options.MaxAge = Remembered;
        context.Response.Cookies.Append(CookieName, Word(choice), options);
    }

    /// <summary>
    /// The page to go back to after a choice — a path on this site, or the front page.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It arrives in a form field, so it is whatever the poster typed rather than whatever we
    /// rendered. An open redirect on a preference endpoint is still an open redirect.
    /// </para>
    /// <para>
    /// <b><c>//elsewhere.example</c> is the case this exists for.</b> A protocol-relative URL is a
    /// different host wearing a path's clothes, and it walks straight through a
    /// <c>StartsWith('/')</c> check; so does <c>/\elsewhere.example</c>, which several browsers
    /// treat as the same thing. Everything outside printable ASCII goes too — a CR or LF here is a
    /// response-splitting attempt on the <c>Location</c> header, not a page anyone asked for.
    /// </para>
    /// </remarks>
    public static string Back(string? path) =>
        path is { Length: > 1 }
        && path[0] == '/'
        && path[1] is not ('/' or '\\')
        && path.All(c => c is >= ' ' and < (char)0x7f)
            ? path
            : "/";
}
