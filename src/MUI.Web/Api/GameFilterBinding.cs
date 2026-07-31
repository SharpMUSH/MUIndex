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
/// <para>
/// The facet panel is a plain GET form, so its field names <em>are</em> the public query language,
/// and the page reads its own URL through this same function rather than binding each parameter for
/// itself. That is not tidiness: <c>/games?band=quiet</c> and <c>/api/games?band=quiet</c> have to
/// mean one thing, and a second binder is exactly how they stop meaning one thing. Every name comes
/// from <see cref="FacetKeys"/>, so the facet a query returns and the parameter that selects it
/// cannot be spelled differently.
/// </para>
/// <para>
/// An unrecognised <c>band</c> or <c>seen</c> is refused rather than ignored. A consumer who typoed
/// a facet should be told, not handed the unfiltered catalogue and left to read it as the answer —
/// the same rule as everywhere else here: our own silence must not be published as a fact.
/// </para>
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

        if (!TryBand(read, out var band, out error) || !TryLastSeen(read, out var seen, out error))
        {
            return false;
        }

        var protocols = Protocols(read);
        var text = read(FacetKeys.Text).ToString();

        var filter = new GameFilter
        {
            Text = string.IsNullOrWhiteSpace(text) ? null : text,
            IncludeArchived = Truthy.Is(read(FacetKeys.Archived)),
            MeasuredProtocols = protocols,
            Tls = Truthy.Is(read(FacetKeys.Tls)),
            Band = band,
            LastSeen = seen,
            Charset = Choice(read, FacetKeys.Charset),
            Codebase = Choice(read, FacetKeys.Codebase),
            Family = Choice(read, FacetKeys.Family),
            Genre = Choice(read, FacetKeys.Genre),
            Language = Choice(read, FacetKeys.Language),
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
    /// <remarks>
    /// A blank parameter is no selection at all; <c>~unknown</c> is a selection of the games that
    /// have no value. Folding the two together would make "games whose codebase we could not
    /// identify" — a measurement of our own reach, and one of the more useful filters here —
    /// unaskable, and would quietly re-point any URL that asked it.
    /// </remarks>
    private static FacetChoice? Choice(Func<string, StringValues> read, string key)
    {
        var value = read(key).ToString();

        return string.IsNullOrWhiteSpace(value) ? null : FacetChoice.Parse(value.Trim());
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

    private static int Bounded(string? value, int fallback, int min, int max)
    {
        if (!int.TryParse(value, out var parsed))
        {
            return fallback;
        }

        return parsed < min ? min : parsed > max ? max : parsed;
    }
}
