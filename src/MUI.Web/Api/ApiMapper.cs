using MUI.Catalog;
using MUI.Web.Components;

namespace MUI.Web.Api;

/// <summary>
/// View models to wire shapes. The only file that knows both vocabularies.
/// </summary>
/// <remarks>
/// It computes nothing the catalogue already computed. Reachability arithmetic, staleness and the
/// disagreement flag all arrive decided (spec §5.6) and are carried across, because a surface that
/// re-derived any of them would be free to disagree with the page about the same fact.
/// </remarks>
public static class ApiMapper
{
    /// <summary>The window the reachability figures on <see cref="GamePage"/> were computed over.</summary>
    public const int ReachableWindowDays = 90;

    public static GameSummaryView Summary(GameSummary game) => new(
        game.Id,
        game.Slug,
        game.Name,
        game.Tagline,
        game.State,
        Archived: game.State is LifecycleState.Archived,
        Claimed: game.IsClaimed,
        game.PlayersNow,
        Counted(game.PlayersNow),
        game.Codebase,
        game.MeasuredProtocols,
        ApiRoutes.Page(game.Slug),
        ApiRoutes.Game(game.Id));

    public static GameView Game(
        GamePage page,
        IReadOnlyList<AvailabilityInterval> availability,
        DateTimeOffset now)
    {
        var game = page.Summary;

        return new GameView(
            game.Id,
            game.Slug,
            game.Name,
            game.Tagline,
            page.Description,
            game.State,
            Archived: game.State is LifecycleState.Archived,
            Claimed: game.IsClaimed,
            game.PlayersNow,
            Counted(game.PlayersNow),
            game.Codebase,
            game.MeasuredProtocols,
            [.. page.Endpoints.Select(Endpoint)],
            new ConnectScreenView(page.ConnectScreenSuppressed, page.ConnectScreen),
            new ReachabilityView(
                ReachableWindowDays,
                page.ReachableFraction,
                page.ReachableFraction * 100d,
                page.LongestOutage?.TotalSeconds),
            [.. page.Capabilities.Select(c => Capability(c, now))],
            page.DisagreementCount,
            page.Declared.ToDictionary(f => f.Key, f => Provenance(f.Value, now), StringComparer.Ordinal),
            new PresenceView("dayOfWeekHour", "UTC", [.. page.Activity.Select(Presence)]),
            [.. availability.Select(i => Span(i, now))],
            [.. page.Changes.Select(c => new ChangeView(c.At, c.Summary))],
            ApiRoutes.Page(game.Slug),
            ApiRoutes.Game(game.Id));
    }

    public static ProvenanceView Provenance(ProvenanceChip chip, DateTimeOffset now)
    {
        var age = Age(chip.LastConfirmedAt, now);
        return new ProvenanceView(
            chip.Value,
            chip.Source,
            chip.IsMeasured,
            chip.LastConfirmedAt,
            age.TotalSeconds,
            Relative.Format(age),
            chip.IsStale);
    }

    public static CapabilityView Capability(CapabilityRow row, DateTimeOffset now)
    {
        var age = row.LastConfirmedAt is { } at ? Age(at, now) : (TimeSpan?)null;
        return new CapabilityView(
            row.Protocol,
            row.Measured,
            row.Declared,
            row.Disagrees,
            row.LastConfirmedAt,
            age?.TotalSeconds,
            age is { } span ? Relative.Format(span) : null);
    }

    /// <summary>
    /// The three states of an hour, kept three (spec §5.4). A cell that was probed and produced no
    /// number is <see cref="PresenceState.Unmeasurable"/> and never a zero; one we could not reach
    /// is a <see cref="PresenceState.Gap"/> and never a zero either.
    /// </summary>
    public static PresenceCellView Presence(ActivityCell cell) => new(
        cell.DayOfWeek,
        cell.Hour,
        cell switch
        {
            { IsCounted: true } => PresenceState.Counted,
            { IsUnmeasurable: true } => PresenceState.Unmeasurable,
            _ => PresenceState.Gap,
        },
        cell.IsCounted ? cell.Count : null);

    public static AvailabilitySpanView Span(AvailabilityInterval interval, DateTimeOffset now) => new(
        interval.State,
        interval.Cause,
        interval.FromAt,
        interval.ToAt,
        interval.IsOpen,
        interval.DurationAt(now).TotalSeconds);

    public static EndpointView Endpoint(GameEndpointView endpoint) =>
        new(endpoint.Host, endpoint.Port, endpoint.Kind, endpoint.TlsMeasured);

    public static FeedEntryView Feed(FeedEntry entry, Guid? id) => new(
        id,
        entry.Slug,
        entry.Name,
        entry.At,
        entry.Detail,
        ApiRoutes.Page(entry.Slug),
        id is { } known ? ApiRoutes.Game(known) : $"{ApiRoutes.Games}/{Uri.EscapeDataString(entry.Slug)}");

    /// <summary>
    /// Null is "we did not measure a count", and it is a different fact from zero (rule 4). It ships
    /// as a null <em>and</em> as a named state, because a consumer that coerces null to zero would
    /// otherwise publish a claim we never made.
    /// </summary>
    private static PlayerCountState Counted(int? players) =>
        players is null ? PlayerCountState.Unknown : PlayerCountState.Measured;

    /// <summary>
    /// Ages never go negative. A field confirmed in the same minute the response is stamped for is
    /// zero seconds old, not minus forty — see <see cref="ApiClock"/> for why the two can cross.
    /// </summary>
    private static TimeSpan Age(DateTimeOffset at, DateTimeOffset now) =>
        now > at ? now - at : TimeSpan.Zero;
}
