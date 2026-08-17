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

    /// <summary>
    /// A listing row, with the label the catalogue put on each of its two bare values (spec §10.1).
    /// </summary>
    /// <remarks>
    /// <paramref name="now"/> is taken rather than read off a clock, because every age in one
    /// response is measured from the instant that response is stamped with — a listing whose rows
    /// each aged from their own <c>UtcNow</c> would disagree with its own <c>generatedAt</c>.
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
    /// The span is published as whole days because that is what the three sorts offer and what a
    /// consumer would otherwise have to reconstruct from a duration string. Null propagates: absent
    /// means "this listing was not sorted on a window, or this game had nothing countable in it",
    /// and the two are told apart by the <c>sort</c> the response echoes back.
    /// </remarks>
    private static PresenceWindowView? Window(PresenceWindow? window) => window is null
        ? null
        : new PresenceWindowView(
            (int)window.Window.TotalDays, window.Average, window.Peak, window.Samples);

    /// <summary>
    /// One facet, carried across exactly as the catalogue counted it.
    /// </summary>
    /// <remarks>
    /// Nothing is recomputed, re-ordered or trimmed here. A count is only trustworthy because it
    /// came from the same pass as the listing beside it, and a mapper that adjusted one would break
    /// that with no surface left to say so.
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
            // Suppression withholds the screen here exactly as it does on the page (§11). It shipped
            // the other way: the flag was published beside the full text, so the one surface most
            // likely to be re-published by somebody else was the one surface that ignored the
            // owner's request. A consumer is told there is a screen and that we do not republish it.
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
    /// <remarks>
    /// The null is a fact — we did not measure this — and it ships as one rather than as an empty
    /// object, which a consumer would have to inspect to discover said nothing.
    /// </remarks>
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
    /// Null is "we did not measure a count", and it is a different fact from zero (rule 4). It ships
    /// as a null <em>and</em> as a named state, because a consumer that coerces null to zero would
    /// otherwise publish a claim we never made. It is derived from the chip rather than from the
    /// count's nullness so the two cannot disagree — which they did, with every count that existed
    /// at all named <c>measured</c> beside a label saying the game had asserted it.
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
