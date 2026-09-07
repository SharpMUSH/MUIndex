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

    /// <summary>Prepare immutable derived values once per catalogue snapshot, not per filter URL.</summary>
    public static Catalogue Prepare(IReadOnlyList<GameFacetRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return new Catalogue(rows.Select(row => new IndexedRow(
            row, Choices.Select(facet => facet.TokensOf(row)).ToArray())).ToArray());
    }

    /// <summary>A bounded snapshot shared by all filter combinations through the catalogue cache.</summary>
    public sealed class Catalogue
    {
        internal Catalogue(IndexedRow[] rows) => Rows = rows;
        internal IndexedRow[] Rows { get; }
        public int Count => Rows.Length;
    }

    internal sealed record IndexedRow(GameFacetRow Row, IReadOnlyList<string?>[] Tokens);

    public static GameListing Search(IReadOnlyList<GameFacetRow> rows, GameFilter filter) =>
        Search(Prepare(rows), filter);

    public static GameListing Search(Catalogue catalogue, GameFilter filter)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(filter);

        var wantsArchived = filter.IncludeArchived || filter.Band is ActivityBand.Archived;
        var selections = Choices.Select(facet => facet.SelectionOf(filter)).ToArray();
        var baseRows = new List<IndexedRow>();
        var results = new List<GameFacetRow>();
        var domains = Choices.Select(_ => new List<IndexedRow>()).ToArray();

        foreach (var indexed in catalogue.Rows)
        {
            var row = indexed.Row;
            if ((!wantsArchived && row.Band is ActivityBand.Archived)
                || (!filter.IncludeAdult && row.IsAdult) || !MatchesText(row, filter.Text))
            {
                continue;
            }

            // Bounded vocabularies describe this reader's catalogue before facet selections,
            // including before protocol/TLS selections. Keep those rows even when Present fails.
            baseRows.Add(indexed);
            if (!Present(row, filter)) continue;

            var failed = -1;
            for (var i = 0; i < Choices.Length; i++)
            {
                if (selections[i] is not { } selection) continue;
                var covered = false;
                foreach (var token in indexed.Tokens[i])
                {
                    if (selection.Covers(token))
                    {
                        covered = true;
                        break;
                    }
                }
                if (selection.Admits(covered)) continue;
                // With two failed selections, lifting any single facet still excludes this row.
                if (failed >= 0) { failed = -2; break; }
                failed = i;
            }

            if (failed == -2) continue;
            if (failed >= 0)
            {
                domains[failed].Add(indexed);
                continue;
            }

            results.Add(row);
            foreach (var domain in domains) domain.Add(indexed);
        }

        var groups = new List<FacetGroup>();
        for (var i = 0; i < Choices.Length; i++)
        {
            var facet = Choices[i];
            var domain = domains[i];
            var values = facet.Bounded is { } vocabulary
                ? Bounded(domain, baseRows, i, vocabulary, selections[i])
                : Open(domain, i, selections[i]);
            if (values.Count > 0)
            {
                groups.Add(new FacetGroup(
                    facet.Key, facet.Evidence, FacetKind.Choice, domain.Count, values));
            }
        }
        groups.AddRange(Presence(results, filter));
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

    /// <summary>
    /// The presence facets, which intersect. Every one of them reads a measurement — a protocol the
    /// handshake offered, or an endpoint we completed a TLS connection to.
    /// </summary>
    private static bool Present(GameFacetRow row, GameFilter filter) =>
        filter.MeasuredProtocols.All(
            p => row.Summary.MeasuredProtocols.Contains(p, StringComparer.OrdinalIgnoreCase))
        && (!filter.Tls || row.TlsMeasured);
}
