using Dapper;

using Npgsql;

namespace MUI.Crawler.Cli;

/// <summary>
/// What the AresCentral pass left behind, read back out of the database.
/// </summary>
/// <remarks>
/// <b>Read back rather than reported from memory.</b> The cycle's own result says what it believes it
/// did; this says what is in the tables afterwards, and the only interesting runs are the ones where
/// those two disagree.
/// </remarks>
public static class AresSummary
{
    public static async Task PrintAsync(NpgsqlDataSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        await using var connection = await source.OpenConnectionAsync();

        var listings = await connection.QuerySingleAsync<(int Total, int Bound, int Delisted)>(
            """
            SELECT count(*)::int AS "Total",
                   count(*) FILTER (WHERE game_id IS NOT NULL)::int AS "Bound",
                   count(*) FILTER (WHERE delisted_at IS NOT NULL)::int AS "Delisted"
            FROM ares_listing
            """);

        Console.WriteLine($"ares listings {listings.Total} held, {listings.Bound} bound to a game, "
            + $"{listings.Delisted} no longer listed");

        var targets = await connection.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM crawl_target WHERE discovered_via = 'ares_central'");

        Console.WriteLine($"ares targets  {targets} addresses in the registry were first seen here");

        var fields = await connection.QueryAsync<(string Field, int Rows)>(
            """
            SELECT field AS "Field", count(*)::int AS "Rows"
            FROM game_field WHERE source = 'ares_central'
            GROUP BY 1 ORDER BY 1
            """);

        var rows = fields.ToList();

        Console.WriteLine(rows.Count == 0
            ? "ares fields   no rows — nothing listed has been promoted to a game yet"
            : "ares fields");

        foreach (var row in rows)
        {
            Console.WriteLine($"              {row.Field,-12} {row.Rows}");
        }
    }
}
