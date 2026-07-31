using MUI.Catalog;
using MUI.Web.Components;

namespace MUI.Web.Api;

/// <summary>A read listing request: what to match, and how much of the answer to send.</summary>
public sealed record GameQuery(GameFilter Filter, FilterView Echo, int Limit, int Offset);

/// <summary>
/// Querystring to <see cref="GameFilter"/>, in the spelling the site's own facet panel uses.
/// </summary>
/// <remarks>
/// <para>
/// The panel is a plain GET form, so its field names <em>are</em> the public query language:
/// <c>q</c> and <c>archived</c> already mean something on <c>/games</c>, and an API that invented
/// <c>search</c> and <c>include=archived</c> beside them would give one question two spellings and
/// let them drift. <c>protocol</c> and <c>band</c> are the remaining two members of
/// <see cref="GameFilter"/>, named the way the panel will name them when it grows the controls.
/// </para>
/// <para>
/// An unrecognised <c>band</c> is a 400 rather than a silent empty filter. A consumer who typoed a
/// facet should be told, not handed the unfiltered catalogue and left to wonder.
/// </para>
/// </remarks>
public static class GameFilterBinding
{
    public const int DefaultLimit = 100;

    public const int MaxLimit = 500;

    public static bool TryRead(IQueryCollection query, out GameQuery result, out string? error)
    {
        result = null!;
        error = null;

        ActivityBand? band = null;
        var bandText = query["band"].ToString();
        if (!string.IsNullOrWhiteSpace(bandText))
        {
            if (!TryBand(bandText, out var parsed))
            {
                error = $"'{bandText}' is not an activity band. "
                    + "Accepted: playersNow, activeThisWeek, quiet, dark, archived.";
                return false;
            }

            band = parsed;
        }

        var protocols = Protocols(query);
        var text = query["q"].ToString();
        var includeArchived = Truthy.Is(query["archived"]);

        var filter = new GameFilter
        {
            Text = string.IsNullOrWhiteSpace(text) ? null : text,
            IncludeArchived = includeArchived,
            MeasuredProtocols = protocols,
            Band = band,
        };

        result = new GameQuery(
            filter,
            new FilterView(filter.Text, includeArchived, protocols, band),
            Bounded(query["limit"], DefaultLimit, 1, MaxLimit),
            Bounded(query["offset"], 0, 0, int.MaxValue));

        return true;
    }

    /// <summary>
    /// Repeatable and comma-separated both work, because both are what people type. Every value is
    /// a <em>measured</em> protocol — a game's own claim never satisfies this facet (spec §3.1).
    /// </summary>
    private static string[] Protocols(IQueryCollection query) =>
    [
        .. query["protocol"]
            .SelectMany(v => (v ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries))
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase),
    ];

    private static bool TryBand(string value, out ActivityBand band)
    {
        // Hyphens and underscores are stripped so active-this-week, active_this_week and
        // activeThisWeek are one facet rather than three near misses.
        var normalised = value.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal);

        // Enum.TryParse also accepts the underlying number, which would make band=0 a synonym for
        // whichever member happens to be declared first — a facet that silently re-points itself the
        // day somebody reorders the enum. Only the names are public.
        if (normalised.Length == 0 || normalised.All(char.IsAsciiDigit))
        {
            band = default;
            return false;
        }

        return Enum.TryParse(normalised, ignoreCase: true, out band)
            && Enum.IsDefined(band);
    }

    private static int Bounded(string? value, int fallback, int min, int max)
    {
        if (!int.TryParse(value, out var parsed))
        {
            return fallback;
        }

        return parsed < min ? min : parsed > max ? max : parsed;
    }
}
