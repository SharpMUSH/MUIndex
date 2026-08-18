using System.Globalization;

using MUI.Web.Localization;

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
/// <b>The words come from CLDR and the order comes from a message.</b> These were rendered under
/// <see cref="CultureInfo.InvariantCulture"/>, which made a German page say <c>30 Jul 2026</c> — the
/// month name was not in any file a translator is ever sent, exactly as the heatmap's seven English
/// day names were not. The month is now <see cref="Locales.CultureOf"/>'s, and the arrangement of
/// day, month and year is <c>date.absolute</c>, because no .NET format string chosen here can say
/// <c>2026年7月30日</c> for a language it is never asked about.
/// </para>
/// <para>
/// <b>UTC, always, and in the site's own 24-hour spelling.</b> Every time here is UTC — a crawler's
/// clock is the only one it has — so the zone is named rather than implied, and the clock is one
/// shape everywhere rather than the host's or the reader's. The suffix is a zone and not a word to
/// translate.
/// </para>
/// </remarks>
public static class Dates
{
    /// <summary>The one absolute format: <c>17 Aug 2026</c>, in the reader's language.</summary>
    public static string Absolute(string tag, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(tag);

        var when = at.UtcDateTime;

        return Messages.For(tag, "date.absolute", new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            // Strings rather than numbers: a year is a label and not a quantity, and an ICU number
            // argument would render 2026 as "2.026" the moment a locale groups its thousands.
            ["day"] = when.Day.ToString(CultureInfo.InvariantCulture),
            ["month"] = MonthName(tag, when.Month),
            ["year"] = when.Year.ToString(CultureInfo.InvariantCulture),
        });
    }

    /// <summary>The same date with the time on it: <c>17 Aug 2026 14:02 UTC</c>.</summary>
    public static string Stamp(string tag, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(tag);

        return Messages.For(tag, "date.stamp", new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["date"] = Absolute(tag, at),
            ["time"] = at.UtcDateTime.ToString("HH:mm", CultureInfo.InvariantCulture),
        });
    }

    /// <summary>What goes in a <c>datetime</c> attribute: ISO-8601, UTC, to the second.</summary>
    /// <remarks>
    /// The one spelling here that takes no locale, because it is not read by a person. It is the
    /// machine-readable instant a browser, a reading-mode extractor or a scraper resolves the
    /// visible age against, and a localized one would be a bug in every language at once.
    /// </remarks>
    public static string Machine(DateTimeOffset at) =>
        at.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture);

    /// <summary>
    /// The month, abbreviated, as the locale writes it beside a day number.
    /// </summary>
    /// <remarks>
    /// <see cref="DateTimeFormatInfo.AbbreviatedMonthGenitiveNames"/> and not
    /// <see cref="DateTimeFormatInfo.GetAbbreviatedMonthName"/>: the second is the standalone form a
    /// calendar header wants, and German's is the legacy three-letter <c>Jul</c>, while the form
    /// CLDR uses in a date — the one a German reader expects after "30." — is <c>Juli</c>. English
    /// is <c>Jul</c> either way, which is why the difference is invisible until it is a German page.
    /// </remarks>
    private static string MonthName(string tag, int month) =>
        Locales.CultureOf(tag).DateTimeFormat.AbbreviatedMonthGenitiveNames[month - 1];
}
