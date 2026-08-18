using Dapper;

using Npgsql;

namespace MUI.Crawler;

/// <summary>One row of <see cref="CrawlSummaryData.Totals"/> — a label and the count behind it.</summary>
public sealed record CrawlSummaryTotal(string Label, long Count);

/// <summary>One game's line in <see cref="CrawlSummaryData.Games"/>.</summary>
/// <remarks>
/// Carries the raw columns rather than only <see cref="PresenceSummary"/>'s formatted line, so a
/// caller that wants the number and one that wants the sentence both read this without either
/// re-deriving the other from a string.
/// </remarks>
public sealed record CrawlSummaryGame(
    string Slug,
    string State,
    long Fields,
    long Samples,
    int? Latest,
    string? Reason,
    string? Reach,
    string? Cause)
{
    /// <summary>
    /// The most recent presence row, in §5.4's three states. A measured zero prints as a count,
    /// because it is one — we got in and nobody was there.
    /// </summary>
    public string PresenceSummary => (Samples, Latest, Reason) switch
    {
        (0, _, _) => "no presence row",
        (_, { } n, _) => $"{n} players",
        (_, null, { } why) => $"uncountable · {why}",
        _ => "uncountable",
    };
}

/// <summary>What <see cref="CrawlSummary.CollectAsync"/> read back, before anybody renders it.</summary>
public sealed record CrawlSummaryData(
    IReadOnlyList<CrawlSummaryTotal> Totals,
    IReadOnlyList<CrawlSummaryGame> Games);

/// <summary>
/// What actually landed, read back out of the database.
/// </summary>
/// <remarks>
/// Read back rather than accumulated in memory, since the claim being checked is "the site shows
/// what's in these tables" — a summary tallied from what the cycle thought it wrote would prove
/// nothing. The three presence states of §5.4 are broken out separately rather than one "samples"
/// figure, since which of the three a cycle produced is the whole point.
///
/// Split into <see cref="CollectAsync"/> and <see cref="PrintAsync"/> because <c>mui-crawl</c> is not
/// the only caller: the MCP <c>crawl_summary</c> tool wants the same query without a terminal on the
/// other end, and a second copy of the SQL is a second place for the two to drift apart.
/// </remarks>
public static class CrawlSummary
{
    private static readonly (string Label, string Sql)[] TotalQueries =
    [
        ("games", "SELECT count(*) FROM game"),
        ("crawl targets", "SELECT count(*) FROM crawl_target"),
        ("endpoints", "SELECT count(*) FROM game_endpoint"),
        ("fields", "SELECT count(*) FROM game_field"),
        ("field changes", "SELECT count(*) FROM field_change"),
        ("presence · counted", "SELECT count(*) FROM presence_sample WHERE count IS NOT NULL"),
        ("presence · uncountable", "SELECT count(*) FROM presence_sample WHERE count IS NULL"),
        ("availability intervals", "SELECT count(*) FROM availability_interval"),
        ("referral edges", "SELECT count(*) FROM referral_edge"),
        // §11's "and recorded", where an operator will actually look for it: how many addresses we
        // are declining to dial, and how many asks we hold in total including the withdrawn ones.
        ("opt-outs standing", "SELECT count(*) FROM crawl_opt_out WHERE withdrawn_at IS NULL"),
        ("opt-outs recorded", "SELECT count(*) FROM crawl_opt_out"),
        ("duplicate reviews open", "SELECT count(*) FROM duplicate_review WHERE resolved_at IS NULL"),
    ];

    public static async Task<CrawlSummaryData> CollectAsync(
        NpgsqlDataSource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        var totals = new List<CrawlSummaryTotal>(TotalQueries.Length);

        foreach (var (label, sql) in TotalQueries)
        {
            totals.Add(new CrawlSummaryTotal(
                label,
                await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                    sql, cancellationToken: cancellationToken))));
        }

        var rows = await connection.QueryAsync<GameRow>(new CommandDefinition(
            """
            SELECT g.slug AS Slug,
                   g.state AS State,
                   (SELECT count(*) FROM game_field f WHERE f.game_id = g.id) AS Fields,
                   (SELECT count(*) FROM presence_sample p WHERE p.game_id = g.id) AS Samples,
                   (SELECT p.count FROM presence_sample p
                     WHERE p.game_id = g.id ORDER BY p.at DESC LIMIT 1) AS Latest,
                   (SELECT p.unmeasurable_reason FROM presence_sample p
                     WHERE p.game_id = g.id ORDER BY p.at DESC LIMIT 1) AS Reason,
                   (SELECT a.state FROM availability_interval a
                     WHERE a.game_id = g.id AND a.to_at IS NULL) AS Reach,
                   (SELECT a.cause FROM availability_interval a
                     WHERE a.game_id = g.id AND a.to_at IS NULL) AS Cause
              FROM game g
             ORDER BY g.slug
            """,
            cancellationToken: cancellationToken));

        return new CrawlSummaryData(
            totals,
            [.. rows.Select(row => row.ToRecord())]);
    }

    public static async Task PrintAsync(NpgsqlDataSource source)
    {
        var data = await CollectAsync(source);

        Console.WriteLine();
        Console.WriteLine("in the database now");

        foreach (var total in data.Totals)
        {
            Console.WriteLine($"  {total.Label,-24} {total.Count}");
        }

        Console.WriteLine();
        Console.WriteLine("games");

        foreach (var game in data.Games)
        {
            Console.WriteLine(
                $"  {game.Slug,-34} {game.State,-8} {game.Fields,3} fields {game.Samples,3} samples  "
                + $"{game.PresenceSummary,-22} {game.Reach ?? "never probed"}"
                + (game.Cause is null or "none" ? string.Empty : $" ({game.Cause})"));
        }
    }

    private sealed class GameRow
    {
        public string Slug { get; init; } = string.Empty;

        public string State { get; init; } = string.Empty;

        public long Fields { get; init; }

        public long Samples { get; init; }

        public int? Latest { get; init; }

        public string? Reason { get; init; }

        public string? Reach { get; init; }

        public string? Cause { get; init; }

        public CrawlSummaryGame ToRecord() =>
            new(Slug, State, Fields, Samples, Latest, Reason, Reach, Cause);
    }
}
