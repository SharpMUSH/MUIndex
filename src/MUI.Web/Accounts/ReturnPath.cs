using MUI.Web.Localization;

namespace MUI.Web.Accounts;

/// <summary>
/// The page an operator was on when they were asked to sign in, so sign-in can hand them back to it.
/// </summary>
/// <remarks>
/// Sign-in used to land on the dashboard whatever brought somebody to it, which left the operator to
/// find their way back to the claim page themselves — and the way back a browser offers is the Back
/// button, which is exactly the path that served them the signed-out render again (see
/// <see cref="PageCacheHeaders"/>). One of those two is the bug and the other is the trap that made
/// it a loop; both are worth closing.
/// <b>Local paths only.</b> A return address arrives in a querystring, which means anybody can put
/// one in a link — so it is a path on this site or it is nothing at all, and "nothing at all" is the
/// dashboard rather than an error. An open redirect on a sign-in page is worth more to an attacker
/// than the account it would be attached to.
/// </remarks>
public static class ReturnPath
{
    /// <summary>
    /// The querystring parameter, shared with the cookie handler's own challenge redirect.
    /// </summary>
    /// <remarks>Set as <c>ReturnUrlParameter</c> in <see cref="Passkeys.AddMuiAccounts"/> so the two spell it the same way; the default is <c>ReturnUrl</c>, and two names for one idea is one of them eventually going unread.</remarks>
    public const string Parameter = "return";

    /// <summary>
    /// A ceiling on what will be echoed back into a page, well above any address this site mints.
    /// </summary>
    /// <remarks>The longest real one is a listing with every facet set, around 200 characters.</remarks>
    private const int MaxLength = 512;

    /// <summary>
    /// The address of the sign-in page, carrying wherever the reader is now.
    /// </summary>
    /// <remarks>
    /// Localized, so signing in is not also a language change, and the return value inside it is
    /// <em>not</em> — the locale lives in <c>PathBase</c> by the time a page reads its own path, and
    /// gets put back by <see cref="LocaleRouting.Link(HttpContext, string)"/> on the way out.
    /// </remarks>
    public static string SignInFrom(HttpContext? context)
    {
        var signIn = context.Link(Passkeys.SignInPath);
        var here = Here(context);

        return here is null
            ? signIn
            : $"{signIn}?{Parameter}={Uri.EscapeDataString(here)}";
    }

    /// <summary>
    /// What the sign-in page should send an operator to, or <c>null</c> for the dashboard.
    /// </summary>
    /// <remarks>
    /// Refuses everything that is not a path on this site: an absolute URL, a protocol-relative
    /// <c>//host</c>, the <c>/\host</c> that several browsers also read as protocol-relative, and
    /// anything carrying a control character (a newline here is a response-splitting attempt).
    /// </remarks>
    public static string? Of(string? candidate)
    {
        if (string.IsNullOrEmpty(candidate)
            || candidate.Length > MaxLength
            || candidate[0] != '/')
        {
            return null;
        }

        if (candidate.Length > 1 && candidate[1] is '/' or '\\')
        {
            return null;
        }

        return candidate.Any(char.IsControl) ? null : candidate;
    }

    /// <summary>
    /// The address of the page being read, when there is any point coming back to it.
    /// </summary>
    /// <remarks>The two account pages return nothing: coming back to the sign-in page having signed in is a loop, and the dashboard is already where a returnless sign-in goes.</remarks>
    private static string? Here(HttpContext? context)
    {
        if (context is null)
        {
            return null;
        }

        var path = context.Request.Path.Value;

        if (string.IsNullOrEmpty(path)
            || path.StartsWith(Passkeys.DashboardPath, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Of(path + context.Request.QueryString.Value);
    }
}
