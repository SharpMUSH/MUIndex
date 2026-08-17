using System.Globalization;

namespace MUI.Web.Components;

/// <summary>
/// One absolute date format for the whole site, and the machine spelling beside it.
/// </summary>
/// <remarks>
/// <para>
/// The site had three: <c>2026-08-17</c> in a game's change list, <c>31 July 2026</c> in the
/// rankings, <c>Aug 2026</c> beside an address. Three spellings of one kind of fact is three things
/// to learn on a site whose whole argument is that its facts are comparable, and the ISO one reads
/// as a log line rather than as a date somebody would say out loud.
/// </para>
/// <para>
/// <b>Invariant, always.</b> These are rendered per request under whatever culture the host happens
/// to have, and a date that reads <c>17 août 2026</c> on one deployment and <c>17 Aug 2026</c> on
/// another is a fact that changed shape on the way to the page. The site declares
/// <c>lang="en"</c>; the dates match it.
/// </para>
/// </remarks>
public static class Dates
{
    /// <summary>The one absolute format: <c>17 Aug 2026</c>.</summary>
    public static string Absolute(DateTimeOffset at) =>
        at.UtcDateTime.ToString("d MMM yyyy", CultureInfo.InvariantCulture);

    /// <summary>The same date with the time on it: <c>17 Aug 2026 14:02 UTC</c>.</summary>
    /// <remarks>
    /// UTC is named rather than implied. Every time on this site is UTC — a crawler's clock is the
    /// only one it has — and a reader in another zone who is not told cannot tell whether "14:02"
    /// is theirs.
    /// </remarks>
    public static string Stamp(DateTimeOffset at) =>
        at.UtcDateTime.ToString("d MMM yyyy HH:mm", CultureInfo.InvariantCulture) + " UTC";

    /// <summary>What goes in a <c>datetime</c> attribute: ISO-8601, UTC, to the second.</summary>
    public static string Machine(DateTimeOffset at) =>
        at.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
