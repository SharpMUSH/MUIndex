using System.Globalization;

using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Web.Data;

using Microsoft.Net.Http.Headers;

namespace MUI.Web.Api;

/// <summary>
/// §10's time series: presence over time, and reachability over time.
/// </summary>
/// <remarks>
/// <para>
/// Presence is served from the hourly and daily rollups rather than from raw samples, because §5.2
/// lets retention drop the raw partitions once they have been aggregated — a series read off the raw
/// table would quietly shorten as a deployment aged. The rollup is the copy that outlives them.
/// </para>
/// <para>
/// Both routes resolve the game through <see cref="IGameQueries"/> rather than reading a store by id
/// directly, which is not indirection for its own sake: <c>FindAsync</c> carries the rule that keeps
/// an unclaimed submission off every public surface, and a series route that went to the store itself
/// would be a way to read presence for a game the listing refuses to show.
/// </para>
/// </remarks>
public static class SeriesEndpoints
{
    /// <summary>
    /// The widest window each grain will answer in one response.
    /// </summary>
    /// <remarks>
    /// A bound on the response and not on the history: the data behind an hourly series runs to
    /// §5.2's two years and the daily series is kept for ever. Ninety days of hours is 2,160 buckets
    /// and five years of days is 1,826 — both a few hundred kilobytes, and a consumer wanting more
    /// pages the window rather than being handed a body that times out mid-write.
    /// </remarks>
    public static readonly IReadOnlyDictionary<PresenceGrain, TimeSpan> WidestWindow =
        new Dictionary<PresenceGrain, TimeSpan>
        {
            [PresenceGrain.Hour] = TimeSpan.FromDays(90),
            [PresenceGrain.Day] = TimeSpan.FromDays(1826),
        };

    /// <summary>What a caller gets without asking, per grain.</summary>
    private static readonly IReadOnlyDictionary<PresenceGrain, TimeSpan> DefaultWindow =
        new Dictionary<PresenceGrain, TimeSpan>
        {
            [PresenceGrain.Hour] = TimeSpan.FromDays(7),
            [PresenceGrain.Day] = TimeSpan.FromDays(90),
        };

    private const string PresenceNotice =
        "Buckets nobody measured are absent rather than zero. countedSamples and uncountableSamples "
        + "count probes, not players; min, max and mean are over counted probes only and are null "
        + "when there were none. A gap means we did not measure, not that the game was empty.";

    private const string AvailabilityNotice =
        "Reachable from one vantage point, measured at intervals. A game we cannot route to is "
        + "unreachable here and may be running perfectly well for everybody else, so this is not a "
        + "measurement of whether the game was running. An open span is still open now.";

    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet(ApiRoutes.Games + "/{key}" + ApiRoutes.PresenceSuffix, PresenceAsync);
        api.MapGet(ApiRoutes.Games + "/{key}" + ApiRoutes.AvailabilitySuffix, AvailabilityAsync);
    }

    /// <summary>Measured presence, bucketed (spec §10).</summary>
    public static async Task PresenceAsync(
        HttpContext http,
        string key,
        IGameQueries queries,
        IPresenceSeries series,
        ISlugHistory slugs,
        TimeProvider clock)
    {
        if (await ResolveAsync(http, key, queries, slugs, ApiRoutes.PresenceSuffix) is not { } game)
        {
            return;
        }

        if (!TryReadGrain(http, out var grain, out var grainError))
        {
            await ApiResponse.ProblemAsync(http, StatusCodes.Status400BadRequest, "Bad grain", grainError!);
            return;
        }

        var now = ApiClock.Now(clock);

        if (!TryReadWindow(http, now, DefaultWindow[grain], WidestWindow[grain], out var window, out var error))
        {
            await ApiResponse.ProblemAsync(http, StatusCodes.Status400BadRequest, "Bad window", error!);
            return;
        }

        var buckets = await series.ForGameAsync(
            game.Summary.Id, grain, window.From, window.To, http.RequestAborted);

        await ApiResponse.WriteJsonAsync(http, new PresenceSeriesView(
            ApiVersion.Current,
            now,
            game.Summary.Id,
            game.Summary.Slug,
            grain is PresenceGrain.Hour ? "hour" : "day",
            window.From,
            window.To,
            buckets.Count,
            [.. buckets.Select(b => new PresenceBucketView(
                b.Bucket,
                b.CountedSamples,
                b.UnmeasurableSamples,
                b.MinCount,
                b.MaxCount,
                b.MeanCount))],
            PresenceNotice));
    }

    /// <summary>Measured reachability, as spans (spec §5.3, §10).</summary>
    public static async Task AvailabilityAsync(
        HttpContext http,
        string key,
        IGameQueries queries,
        IAvailabilityHistory availability,
        ISlugHistory slugs,
        TimeProvider clock)
    {
        if (await ResolveAsync(http, key, queries, slugs, ApiRoutes.AvailabilitySuffix) is not { } game)
        {
            return;
        }

        var now = ApiClock.Now(clock);

        if (!TryReadWindow(http, now, TimeSpan.FromDays(90), TimeSpan.FromDays(1826), out var window, out var error))
        {
            await ApiResponse.ProblemAsync(http, StatusCodes.Status400BadRequest, "Bad window", error!);
            return;
        }

        var intervals = await availability.ForGameAsync(game.Summary.Id, http.RequestAborted);

        // Overlap rather than containment: the span a reader most wants is the one running when the
        // window opened, and it started before it. An open span has no end to compare, and is
        // therefore still overlapping anything that has begun.
        var spans = intervals
            .Where(i => i.FromAt <= window.To && (i.ToAt is null || i.ToAt >= window.From))
            .OrderBy(i => i.FromAt)
            .Select(i => ApiMapper.Span(i, now))
            .ToList();

        await ApiResponse.WriteJsonAsync(http, new AvailabilitySeriesView(
            ApiVersion.Current,
            now,
            game.Summary.Id,
            game.Summary.Slug,
            window.From,
            window.To,
            spans.Count,
            spans,
            AvailabilityNotice));
    }

    /// <summary>
    /// The game a key names, 301ing from a slug it used to wear (spec §5.7) and 404ing otherwise.
    /// </summary>
    /// <remarks>
    /// Returns null when it has already written the response, which is the shape the badge routes use
    /// for the same reason: the redirect and the problem document are both answers, and a caller that
    /// had to distinguish them would be re-deciding what this already decided.
    /// </remarks>
    private static async Task<GamePage?> ResolveAsync(
        HttpContext http,
        string key,
        IGameQueries queries,
        ISlugHistory slugs,
        string suffix)
    {
        var isId = Guid.TryParse(key, out var id);
        var page = isId
            ? await queries.FindAsync(id, http.RequestAborted)
            : await queries.FindAsync(key, http.RequestAborted);

        if (page is not null)
        {
            return page;
        }

        if (!isId
            && await SlugDestination.ForAsync(
                key,
                http.RequestServices.GetService<IMergeRedirects>(),
                slugs,
                queries,
                http.RequestAborted) is { } current)
        {
            http.Response.StatusCode = StatusCodes.Status301MovedPermanently;
            http.Response.Headers[HeaderNames.Location] =
                $"{ApiRoutes.Games}/{Uri.EscapeDataString(current)}{suffix}{http.Request.QueryString}";
            http.Response.Headers[HeaderNames.CacheControl] = ApiResponse.CacheControl;
            return null;
        }

        await ApiResponse.ProblemAsync(
            http,
            StatusCodes.Status404NotFound,
            "No such game",
            $"Nothing in the catalogue answers to '{key}'. Nothing here is ever deleted, so a game "
            + "that has gone dark still has a series — it simply stops.");

        return null;
    }

    private static bool TryReadGrain(HttpContext http, out PresenceGrain grain, out string? error)
    {
        grain = PresenceGrain.Day;
        error = null;

        if (http.Request.Query["grain"].ToString() is not { Length: > 0 } asked)
        {
            return true;
        }

        switch (asked.ToLowerInvariant())
        {
            case "hour":
                grain = PresenceGrain.Hour;
                return true;
            case "day":
                grain = PresenceGrain.Day;
                return true;
            default:
                error = $"'{asked}' is not a grain. Ask for hour or day.";
                return false;
        }
    }

    /// <summary>
    /// Reads <c>from</c> and <c>to</c>, defaulting the window and refusing one too wide to serve.
    /// </summary>
    /// <remarks>
    /// A window wider than the cap is a 400 rather than a silent clamp. Truncating it would answer a
    /// different question from the one asked and say so nowhere, and a consumer paging through
    /// history would never learn its pages were short.
    /// </remarks>
    private static bool TryReadWindow(
        HttpContext http,
        DateTimeOffset now,
        TimeSpan fallback,
        TimeSpan widest,
        out (DateTimeOffset From, DateTimeOffset To) window,
        out string? error)
    {
        window = default;
        error = null;

        if (!TryReadInstant(http, "to", now, out var to, out error)
            || !TryReadInstant(http, "from", to - fallback, out var from, out error))
        {
            return false;
        }

        if (from > to)
        {
            error = "from is after to.";
            return false;
        }

        if (to - from > widest)
        {
            error = $"That window is {(to - from).TotalDays:F0} days and the widest served in one "
                + $"response is {widest.TotalDays:F0}. Ask for a narrower window, or page through it.";
            return false;
        }

        window = (from, to);
        return true;
    }

    private static bool TryReadInstant(
        HttpContext http,
        string name,
        DateTimeOffset fallback,
        out DateTimeOffset instant,
        out string? error)
    {
        instant = fallback;
        error = null;

        if (http.Request.Query[name].ToString() is not { Length: > 0 } asked)
        {
            return true;
        }

        if (!DateTimeOffset.TryParse(
                asked,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out instant))
        {
            error = $"'{asked}' is not an instant. Use ISO 8601, as in 2026-07-30T12:00:00Z.";
            return false;
        }

        return true;
    }
}
