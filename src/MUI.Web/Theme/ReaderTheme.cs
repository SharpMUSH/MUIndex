namespace MUI.Web.Theme;

/// <summary>What a reader has said about which theme they want.</summary>
/// <remarks>
/// Three members, not two: "follow my system" is a real answer, and the only one still right at
/// both ends of the day for a reader whose desktop switches at dusk.
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
/// Server-side, because this site runs no script (<c>App.razor</c> omits <c>blazor.web.js</c>): a
/// preference in <c>localStorage</c> applied by an inline script would be the first blocking script
/// on every page. Reading the cookie while the document is rendered also means it never flashes the
/// wrong theme. With no cookie, <c>data-theme</c> is omitted rather than written with a guessed
/// value — the guess would be wrong for every reader whose system prefers light.
/// </remarks>
public static class ReaderTheme
{
    /// <summary>Where the choice is kept.</summary>
    /// <remarks>Read by the server while the page is composed and never by a script, so it is <c>HttpOnly</c>.</remarks>
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
    /// Shared by the cookie and the form field, so a value round-trips consistently. Unrecognized
    /// input (including <c>auto</c>) lands as <see cref="ThemeChoice.System"/>.
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
    /// Null rather than "system": Razor omits an attribute whose value is null, handing the decision
    /// back to the media query in <c>app.css</c>.
    /// </remarks>
    public static string? Attribute(ThemeChoice choice) =>
        choice is ThemeChoice.System ? null : Word(choice);

    /// <summary>
    /// What to tell the browser its form controls, scrollbars and canvas should be.
    /// </summary>
    /// <remarks>
    /// Both when unset, so the browser follows the same signal the stylesheet does; one when pinned,
    /// or a reader who chose light still gets dark scrollbars on a system set to dark.
    /// </remarks>
    public static string ColorScheme(ThemeChoice choice) =>
        choice is ThemeChoice.System ? "dark light" : Word(choice);

    /// <summary>
    /// The page colour a phone paints its chrome with — <c>--bg</c>, for each theme.
    /// </summary>
    /// <remarks>
    /// Duplicated from <c>app.css</c>'s tokens and must move with them: the head names a colour
    /// before any stylesheet is parsed, so there's no way to read it from there.
    /// </remarks>
    public static string Plate(ThemeChoice choice) =>
        choice is ThemeChoice.Light ? "#f7f8f9" : "#0f1113";

    /// <summary>Remembers a choice, or forgets one.</summary>
    /// <remarks>
    /// "Auto" deletes the cookie rather than writing today's system answer into it — the server
    /// never sees <c>prefers-color-scheme</c>, so it cannot write the right value anyway. The delete
    /// must carry the same path and flags as the write, or a browser keeps the old cookie.
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
    /// Arrives in a form field, so an open redirect here is a real open redirect. Must reject
    /// protocol-relative URLs like <c>//elsewhere.example</c> and <c>/\elsewhere.example</c> (both
    /// walk through a naive <c>StartsWith('/')</c> check) and any non-printable-ASCII byte, since a
    /// CR/LF here is a response-splitting attempt on the <c>Location</c> header.
    /// </remarks>
    public static string Back(string? path) =>
        path is { Length: > 1 }
        && path[0] == '/'
        && path[1] is not ('/' or '\\')
        && path.All(c => c is >= ' ' and < (char)0x7f)
            ? path
            : "/";
}
