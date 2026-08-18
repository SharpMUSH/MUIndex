using MUI.Catalog;
using MUI.Web.Components;
using MUI.Web.Localization;

namespace MUI.Web.Api;

/// <summary>
/// View models to wire shapes. The only file that knows both vocabularies.
/// </summary>
/// <remarks>
/// Computes nothing the catalogue already computed — reachability, staleness and the disagreement
/// flag arrive decided (spec §5.6) and are carried across so this can't drift from the page.
/// <b>Every string it emits is English</b> (<see cref="Locales.SourceTag"/>), never localized by the
/// caller's <c>Accept-Language</c>: this is a contract with a program, not a browser.
/// </remarks>
public static class ApiMapper
{
    /// <summary>The window the reachability figures on <see cref="GamePage"/> were computed over.</summary>
    public const int ReachableWindowDays = 90;

    /// <summary>
    /// A listing row, with the label the catalogue put on each of its two bare values (spec §10.1).
    /// </summary>
    /// <remarks>
    /// <paramref name="now"/> is passed in rather than read off a clock so every row in a response
    /// ages from the same instant as its <c>generatedAt</c>.
    /// </remarks>
    public static GameSummaryView Summary(GameSummary game, DateTimeOffset now) => new(
        game.Id,
        game.Slug,
        game.Name,
        game.Tagline,
        game.State,
        Archived: game.State is LifecycleState.Archived,
        Claimed: game.IsClaimed,
        game.PlayersNow,
        Counted(game.PlayersNowProvenance),
        Label(game.PlayersNowProvenance, now),
        game.Codebase,
        Label(game.CodebaseProvenance, now),
        game.MeasuredProtocols,
        game.LastReachableAt,
        ApiRoutes.Page(game.Slug),
        ApiRoutes.Game(game.Id),
        Window(game.PlayersOverWindow));

    /// <summary>
    /// A window's figures, carried across with the tally they were taken over.
    /// </summary>
    /// <remarks>
    /// Null propagates: absent means either the listing wasn't sorted on a window, or the game had
    /// nothing countable in it — the response's echoed <c>sort</c> tells the two apart.
    /// </remarks>
    private static PresenceWindowView? Window(PresenceWindow? window) => window is null
        ? null
        : new PresenceWindowView(
            (int)window.Window.TotalDays, window.Median, window.Peak, window.Samples);

    /// <summary>
    /// One facet, carried across exactly as the catalogue counted it.
    /// </summary>
    /// <remarks>
    /// Nothing is recomputed, re-ordered or trimmed — a count is trustworthy only because it came
    /// from the same pass as the listing beside it.
    /// </remarks>
    public static FacetGroupView Facet(FacetGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);

        return new FacetGroupView(
            group.Key,
            group.Evidence,
            group.Kind,
            group.Total,
            [.. group.Values.Select(v => new FacetValueView(v.Token, v.Count, v.IsSelected, v.IsUnknown))]);
    }

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
            Counted(game.PlayersNowProvenance),
            Label(game.PlayersNowProvenance, now),
            game.Codebase,
            Label(game.CodebaseProvenance, now),
            game.MeasuredProtocols,
            [.. page.Endpoints.Select(Endpoint)],
            // Suppression withholds the screen here exactly as it does on the page (§11); it once
            // shipped publishing the full text beside the suppressed flag, leaking the exact text an
            // owner asked not to be republished.
            new ConnectScreenView(
                page.ConnectScreenSuppressed,
                page.ConnectScreenSuppressed ? null : page.ConnectScreen),
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
            [.. page.Referrals.Select(n => new NeighbourView(
                n.Slug,
                n.Name,
                n.Host,
                n.Port,
                n.Direction is ReferralDirection.Lists ? "lists" : "listed-by",
                n.FirstSeenAt,
                n.LastSeenAt,
                n.Present))],
            ApiRoutes.Page(game.Slug),
            ApiRoutes.Game(game.Id));
    }

    /// <summary>
    /// The label for a value we hold, or nothing where there is no value to label.
    /// </summary>
    /// <remarks>The null is a fact — we did not measure this — not an empty object a consumer has to inspect to discover the same thing.</remarks>
    public static ProvenanceView? Label(ProvenanceChip? chip, DateTimeOffset now) =>
        chip is null ? null : Provenance(chip, now);

    public static ProvenanceView Provenance(ProvenanceChip chip, DateTimeOffset now)
    {
        var age = Age(chip.LastConfirmedAt, now);
        return new ProvenanceView(
            chip.Value,
            chip.Source,
            chip.IsMeasured,
            chip.LastConfirmedAt,
            age.TotalSeconds,
            Relative.Format(Locales.SourceTag, age),
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
            age is { } span ? Relative.Format(Locales.SourceTag, span) : null);
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

    public static EndpointView Endpoint(GameEndpointView endpoint) => new(
        endpoint.Host,
        endpoint.Port,
        endpoint.Kind,
        endpoint.TlsMeasured,
        endpoint.FirstSeenAt,
        endpoint.LastSeenAt,
        endpoint.State);

    public static FeedEntryView Feed(FeedEntry entry) => new(
        entry.Id,
        entry.Slug,
        entry.Name,
        entry.At,
        entry.Detail,
        ApiRoutes.Page(entry.Slug),
        ApiRoutes.Game(entry.Id));

    /// <summary>
    /// The named state of a count, read off the same label the count carries.
    /// </summary>
    /// <remarks>
    /// Null is "we did not measure a count", distinct from zero (rule 4), and ships as both a null
    /// and a named state so a consumer can't coerce null to zero. Derived from the chip rather than
    /// the count's nullness so the two can't disagree.
    /// </remarks>
    private static PlayerCountState Counted(ProvenanceChip? count) => count switch
    {
        { IsMeasured: true } => PlayerCountState.Measured,
        not null => PlayerCountState.Declared,
        null => PlayerCountState.Unknown,
    };

    /// <summary>
    /// Ages never go negative. A field confirmed in the same minute the response is stamped for is
    /// zero seconds old, not minus forty — see <see cref="ApiClock"/> for why the two can cross.
    /// </summary>
    private static TimeSpan Age(DateTimeOffset at, DateTimeOffset now) =>
        now > at ? now - at : TimeSpan.Zero;
}
