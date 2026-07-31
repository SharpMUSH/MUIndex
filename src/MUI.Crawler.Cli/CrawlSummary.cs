using Dapper;

using Npgsql;

namespace MUI.Crawler.Cli;

/// <summary>
/// What actually landed, read back out of the database.
/// </summary>
/// <remarks>
/// <b>Read back rather than accumulated in memory, deliberately.</b> The claim being checked is "the
/// site will show measured data instead of a fixture", and the site reads these tables — so a summary
/// tallied from what the cycle thought it wrote would agree with the code and prove nothing. The
/// three presence states of §5.4 are broken out for the same reason: a counted row, an uncountable
/// row and a missing row are the three things this whole design turns on, and a single "samples"
/// figure hides which of them a cycle produced.
/// </remarks>
public static class CrawlSummary
{
    private static readonly (string Label, string Sql)[] Totals =
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
        ("duplicate reviews open", "SELECT count(*) FROM duplicate_review WHERE resolved_at IS NULL"),
    ];

    public static async Task PrintAsync(NpgsqlDataSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        await using var connection = await source.OpenConnectionAsync();

        Console.WriteLine();
        Console.WriteLine("in the database now");

        foreach (var (label, sql) in Totals)
        {
            Console.WriteLine($"  {label,-24} {await connection.ExecuteScalarAsync<long>(sql)}");
        }

        var rows = await connection.QueryAsync<GameLine>(
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
            """);

        Console.WriteLine();
        Console.WriteLine("games");

        foreach (var row in rows)
        {
            Console.WriteLine(
                $"  {row.Slug,-34} {row.State,-8} {row.Fields,3} fields {row.Samples,3} samples  "
                + $"{row.Count,-22} {row.Reach ?? "never probed"}"
                + (row.Cause is null or "none" ? string.Empty : $" ({row.Cause})"));
        }
    }

    private sealed class GameLine
    {
        public string Slug { get; init; } = string.Empty;

        public string State { get; init; } = string.Empty;

        public long Fields { get; init; }

        public long Samples { get; init; }

        public int? Latest { get; init; }

        public string? Reason { get; init; }

        public string? Reach { get; init; }

        public string? Cause { get; init; }

        /// <summary>
        /// The most recent presence row, in §5.4's three states. A measured zero prints as a count,
        /// because it is one — we got in and nobody was there.
        /// </summary>
        public string Count => (Samples, Latest, Reason) switch
        {
            (0, _, _) => "no presence row",
            (_, { } n, _) => $"{n} players",
            (_, null, { } why) => $"uncountable · {why}",
            _ => "uncountable",
        };
    }
}
