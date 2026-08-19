using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;

using MUI.Catalog;
using MUI.Web.Components;

namespace MUI.Web.Api;

/// <summary>A read listing request: what to match, and how much of the answer to send.</summary>
public sealed record GameQuery(GameFilter Filter, FilterView Echo, int Limit, int Offset);

/// <summary>
/// Querystring to <see cref="GameFilter"/> — the one parser, with two callers.
/// </summary>
/// <remarks>
/// The facet panel is a plain GET form, so its field names are the public query language, and the
/// page reads its own URL through this same function rather than binding each parameter itself —
/// <c>/games?band=quiet</c> and <c>/api/games?band=quiet</c> must mean one thing.
/// An unrecognised <c>band</c> or <c>seen</c> is refused, not ignored: a typo should be told, not
/// handed the unfiltered catalogue to read as the answer.
/// </remarks>
public static class GameFilterBinding
{
    public const int DefaultLimit = 100;

    public const int MaxLimit = 500;

    /// <summary>Reads a request's querystring — the API's caller.</summary>
    public static bool TryRead(IQueryCollection query, out GameQuery result, out string? error)
    {
        ArgumentNullException.ThrowIfNull(query);

        return TryRead(name => query[name], out result, out error);
    }

    /// <summary>
    /// Reads a raw querystring — the page's caller, which has a URL rather than an
    /// <see cref="IQueryCollection"/> because a static-SSR component is handed neither the request
    /// nor a bound model it could share with the API.
    /// </summary>
    public static bool TryRead(string? queryString, out GameQuery result, out string? error)
    {
        var parsed = QueryHelpers.ParseQuery(queryString ?? string.Empty);

        return TryRead(
            name => parsed.TryGetValue(name, out var values) ? values : StringValues.Empty,
            out result,
            out error);
    }

    private static bool TryRead(
        Func<string, StringValues> read,
        out GameQuery result,
        out string? error)
    {
        result = null!;

        if (!TryBand(read, out var band, out error)
            || !TryLastSeen(read, out var seen, out error)
            || !TryToggle(read, FacetKeys.Uncounted, out var uncounted, out error)
            || !TryToggle(read, FacetKeys.Unreachable, out var unreachable, out error)
            || !TryTrending(read, out var trending, out error)
            || !TrySort(read, out var sort, out error))
        {
            return false;
        }

        var protocols = Protocols(read);
        var text = read(FacetKeys.Text).ToString();

        // `codebase-family` is the old name for `codebase`, read as an alias rather than dropped.
        // PRESENT, NOT NON-EMPTY: the new key wins whenever a caller names it (even blank), so
        // `?codebase=&codebase-family=PennMUSH` clears the filter rather than re-applying the alias.
        var codebase = read(FacetKeys.Codebase).Count > 0
            ? Choice(read, FacetKeys.Codebase)
            : Choice(read, FacetKeys.CodebaseFamily);

        var filter = new GameFilter
        {
            Text = string.IsNullOrWhiteSpace(text) ? null : text,
            IncludeArchived = Truthy.Is(read(FacetKeys.Archived)),

            // The listing surface's own default, written here rather than on GameFilter (which
            // defaults the other way) — callers that build filters in code, like the dump, are meant
            // to keep counting the whole catalogue.
            IncludeAdult = Truthy.Is(read(FacetKeys.Adult)),
            MeasuredProtocols = protocols,
            Tls = Truthy.Is(read(FacetKeys.Tls)),
            Band = band,
            LastSeen = seen,
            Uncounted = uncounted,
            Unreachable = unreachable,
            Charset = Choice(read, FacetKeys.Charset),
            Codebase = codebase,
            CodebaseVersion = Choice(read, FacetKeys.CodebaseVersion),
            Lineage = Choice(read, FacetKeys.Lineage),
            Family = Choice(read, FacetKeys.Family),
            Trending = trending,
            Genre = Choice(read, FacetKeys.Genre),
            Language = Choice(read, FacetKeys.Language),
            Sort = sort,
        };

        result = new GameQuery(
            filter,
            FilterView.Of(filter),
            Bounded(read("limit"), DefaultLimit, 1, MaxLimit),
            Bounded(read("offset"), 0, 0, int.MaxValue));

        return true;
    }

    /// <summary>
    /// Repeatable and comma-separated both work, because both are what people type. Every value is
    /// a <em>measured</em> protocol — a game's own claim never satisfies this facet (spec §3.1).
    /// </summary>
    private static string[] Protocols(Func<string, StringValues> read) =>
    [
        .. read(FacetKeys.Protocol)
            .SelectMany(v => (v ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries))
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase),
    ];

    /// <summary>
    /// An open-ended facet's selection, which may name the absence of a value.
    /// </summary>
    /// <remarks>A blank parameter is no selection; <c>~unknown</c> selects the games with no value — folding the two together would make "codebase we could not identify" unaskable.</remarks>
    private static FacetChoice? Choice(Func<string, StringValues> read, string key)
    {
        var value = read(key).ToString();

        return string.IsNullOrWhiteSpace(value) ? null : FacetChoice.Parse(value.Trim());
    }

    /// <summary>
    /// One of the two measurement switches: <c>yes</c>, <c>!yes</c>, or not asked.
    /// </summary>
    /// <remarks>
    /// Refused rather than ignored: its vocabulary is one word, so anything else is a typo, and a typo
    /// silently narrowing a listing to nothing would read as a measurement we never took. <c>~unknown</c>
    /// is accepted as a spelling of <c>!yes</c> — the games this facet has no value for.
    /// </remarks>
    private static bool TryToggle(
        Func<string, StringValues> read,
        string key,
        out FacetChoice? choice,
        out string? error)
    {
        choice = null;
        error = null;
        var text = read(key).ToString();

        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var parsed = FacetChoice.Parse(text.Trim());

        if (!parsed.IsUnknown && !string.Equals(parsed.Value, FacetTokens.Yes, StringComparison.OrdinalIgnoreCase))
        {
            error = $"'{text}' is not a value of '{key}'. "
                + $"Accepted: {FacetTokens.Yes}, {FacetChoice.ExcludeToken}{FacetTokens.Yes}, "
                + $"{FacetChoice.UnknownToken}.";
            return false;
        }

        choice = parsed;
        return true;
    }

    /// <summary>
    /// <c>trending</c>: <c>up</c>, <c>steady</c>, <c>down</c>, negated or <c>~unknown</c>, or not
    /// asked. Refused on anything else, same as every other bounded vocabulary here.
    /// </summary>
    private static bool TryTrending(Func<string, StringValues> read, out FacetChoice? choice, out string? error)
    {
        choice = null;
        error = null;
        var text = read(FacetKeys.Trending).ToString();

        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var parsed = FacetChoice.Parse(text.Trim());

        if (!parsed.IsUnknown && !FacetTokens.TryGrowthDirection(parsed.Value, out _))
        {
            error = $"'{text}' is not a trending direction. "
                + $"Accepted: {string.Join(", ", FacetTokens.GrowthDirections)}.";
            return false;
        }

        choice = parsed;
        return true;
    }

    private static bool TryBand(Func<string, StringValues> read, out ActivityBand? band, out string? error)
    {
        band = null;
        error = null;
        var text = read(FacetKeys.Band).ToString();

        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (!FacetTokens.TryBand(text, out var parsed))
        {
            error = $"'{text}' is not an activity band. Accepted: {string.Join(", ", FacetTokens.Bands)}.";
            return false;
        }

        band = parsed;
        return true;
    }

    private static bool TryLastSeen(Func<string, StringValues> read, out LastSeenBand? seen, out string? error)
    {
        seen = null;
        error = null;
        var text = read(FacetKeys.LastSeen).ToString();

        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (!FacetTokens.TryLastSeen(text, out var parsed))
        {
            error = $"'{text}' is not a last-seen band. "
                + $"Accepted: {string.Join(", ", FacetTokens.LastSeenBands)}.";
            return false;
        }

        seen = parsed;
        return true;
    }

    /// <summary>The listing's order. Refused rather than ignored, like every other unreadable facet.</summary>
    /// <remarks>The default is read off a default <see cref="GameFilter"/> instance rather than named again here, so the two can't drift apart.</remarks>
    private static bool TrySort(Func<string, StringValues> read, out GameSort sort, out string? error)
    {
        sort = new GameFilter().Sort;
        error = null;
        var text = read(FacetKeys.Sort).ToString();

        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (!FacetTokens.TrySort(text, out var parsed))
        {
            error = $"'{text}' is not a sort order. Accepted: {string.Join(", ", FacetTokens.Sorts)}.";
            return false;
        }

        sort = parsed;
        return true;
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
