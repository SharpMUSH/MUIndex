namespace MUI.Catalog;

/// <summary>
/// Turns a filter and a set of games into the listing plus every facet's counts.
/// </summary>
/// <remarks>
/// Counts are measured against what each choice would actually return. A
/// <see cref="FacetKind.Choice"/> facet replaces its own selection, so its values are counted with
/// that selection lifted but every other filter applied — the count beside <c>quiet</c> is what
/// clicking <c>quiet</c> would return. A <see cref="FacetKind.Presence"/> facet intersects, so its
/// values are counted against the current results. A value with no games is never offered — a facet
/// that could be clicked into an empty listing would be lying about the catalogue.
/// </remarks>
public static partial class FacetedSearch
{
    /// <summary>
    /// How many values an open-ended facet offers. The tail is reachable by search and by URL, and
    /// the panel says as much.
    /// </summary>
    /// <remarks>
    /// <see cref="FacetKeys.CodebaseVersion"/>, not <see cref="FacetKeys.Codebase"/>, is the facet
    /// that needs this cap now: a reader scanning for a codebase wants families; one wanting a
    /// specific patchlevel already knows its name.
    /// </remarks>
    public const int MaxValues = 20;

    public static GameListing Search(IReadOnlyList<GameFacetRow> rows, GameFilter filter)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(filter);

        // Archived games leave the default listing entirely (spec §7.5). Asking for the archived
        // band already means asking for them, so the toggle need not also be set.
        var wantsArchived = filter.IncludeArchived || filter.Band is ActivityBand.Archived;

        // Games declaring adult content leave the default listing too, same as archiving — but
        // unlike archiving, selecting genre=Adult does not lift the exclusion. band=archived is a
        // bounded facet value that's always drawn even when it returns nothing; genre is
        // open-ended, so a value nothing matches is simply never offered, and lifting on selection
        // would make every other value in the dropdown a promise the listing wouldn't keep. The
        // adult checkbox is the only way in.
        var baseRows = rows
            .Where(r => (wantsArchived || r.Band is not ActivityBand.Archived)
                && (filter.IncludeAdult || !r.IsAdult)
                && MatchesText(r, filter.Text))
            .ToList();

        var results = baseRows.Where(r => Chosen(r, filter, null) && Present(r, filter)).ToList();

        var groups = new List<FacetGroup>();

        foreach (var facet in Choices)
        {
            // This facet's own selection lifted, so a count is what choosing the value returns.
            var domain = baseRows.Where(r => Chosen(r, filter, facet.Key) && Present(r, filter)).ToList();
            var values = facet.Bounded is { } vocabulary

                // Counted over the domain, but which rows exist at all is decided by baseRows —
                // the catalogue as this reader is looking at it, before any facet selection. See
                // Bounded: that is what keeps a scale the same length whatever else is filtered.
                ? Bounded(domain, baseRows, facet, vocabulary, filter)
                : Open(domain, facet, filter);

            if (values.Count > 0)
            {
                groups.Add(new FacetGroup(
                    facet.Key, facet.Evidence, FacetKind.Choice, domain.Count, values));
            }
        }

        groups.AddRange(Presence(results, filter));

        // Ordered after counting, never before: every count is taken over a set, which has no
        // order, so sorting here can't move a number.
        return new GameListing(GameSorting.Apply(results.Select(r => r.Summary), filter.Sort), groups);
    }

    /// <summary>
    /// The last-seen band a game is in, given when it was last reachable.
    /// </summary>
    /// <remarks>
    /// Null is <see cref="LastSeenBand.Never"/>, never the oldest bucket — dating an unreached game
    /// from our own ignorance would be the same error as painting an unprobed hour as an outage.
    /// </remarks>
    public static LastSeenBand LastSeenOf(DateTimeOffset? lastReachableAt, DateTimeOffset now) =>
        lastReachableAt is not { } seen ? LastSeenBand.Never
            : now - seen <= TimeSpan.FromDays(1) ? LastSeenBand.Day
            : now - seen <= TimeSpan.FromDays(7) ? LastSeenBand.Week
            : now - seen <= TimeSpan.FromDays(30) ? LastSeenBand.Month
            : LastSeenBand.Older;

    /// <summary>
    /// How long ago a game must have answered for us to still call it reached.
    /// </summary>
    /// <remarks>
    /// One constant read by the activity band, <see cref="NotReachedRecently"/> and both
    /// <see cref="IGameQueries"/> implementations, so "still answering" isn't two different answers
    /// on one page.
    /// </remarks>
    public static readonly TimeSpan RecentlyReachable = TimeSpan.FromDays(30);

    /// <summary>
    /// Whether the availability series says we have not reached this game lately — the
    /// <see cref="FacetKeys.Unreachable"/> fact.
    /// </summary>
    /// <remarks>
    /// Never inferred from missing presence rows — a hole there covers "could not reach" and "never
    /// probed" alike and may not name a cause (rule 2). Reads <c>game.last_reachable_at</c> instead,
    /// which the intervals write. A game never reached is true here (not reached recently);
    /// <see cref="LastSeenBand.Never"/> is where that stays separately visible.
    /// </remarks>
    public static bool NotReachedRecently(DateTimeOffset? lastReachableAt, DateTimeOffset now) =>
        lastReachableAt is not { } seen || now - seen > RecentlyReachable;

    /// <summary>
    /// A game matches the text box on its name, its own one-line tagline, or its codebase.
    /// </summary>
    /// <remarks>
    /// Done here rather than in SQL so the facet counts and the listing are computed over the same
    /// set — a search term applied in one place and counted in another would answer two questions.
    /// </remarks>
    private static bool MatchesText(GameFacetRow row, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var needle = text.Trim();

        return row.Summary.Name.Contains(needle, StringComparison.OrdinalIgnoreCase)
            || (row.Summary.Tagline?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false)
            || (row.Codebase?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private static bool Chosen(GameFacetRow row, GameFilter filter, string? except)
    {
        foreach (var facet in Choices)
        {
            if (string.Equals(facet.Key, except, StringComparison.Ordinal))
            {
                continue;
            }

            // Applied once to the answer, not per token: see FacetChoice.Covers.
            if (facet.SelectionOf(filter) is { } selection
                && !selection.Admits(facet.TokensOf(row).Any(selection.Covers)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The presence facets, which intersect. Every one of them reads a measurement — a protocol the
    /// handshake offered, or an endpoint we completed a TLS connection to.
    /// </summary>
    private static bool Present(GameFacetRow row, GameFilter filter) =>
        filter.MeasuredProtocols.All(
            p => row.Summary.MeasuredProtocols.Contains(p, StringComparer.OrdinalIgnoreCase))
        && (!filter.Tls || row.TlsMeasured);
}
