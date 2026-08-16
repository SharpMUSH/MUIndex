using Dapper;

using Npgsql;

namespace MUI.Crawler.Cli;

/// <summary>
/// What the Intermud-3 pass left behind, read back out of the database.
/// </summary>
/// <remarks>
/// <b>Read back rather than reported from memory.</b> The cycle's own result says what it believes it
/// did; this says what is in the tables afterwards, and the only interesting runs are the ones where
/// those two disagree.
/// </remarks>
public static class I3Summary
{
    public static async Task PrintAsync(NpgsqlDataSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        await using var connection = await source.OpenConnectionAsync();

        var muds = await connection.QuerySingleAsync<(int Total, int Bound, int Answering, int Asked)>(
            """
            SELECT count(*)::int AS "Total",
                   count(*) FILTER (WHERE game_id IS NOT NULL)::int AS "Bound",
                   count(*) FILTER (WHERE answers_who)::int AS "Answering",
                   count(*) FILTER (WHERE last_asked_at IS NOT NULL)::int AS "Asked"
            FROM i3_mud
            """);

        Console.WriteLine($"i3 muds       {muds.Total} known, {muds.Bound} bound, "
            + $"{muds.Answering} answer who, {muds.Asked} asked");

        var targets = await connection.ExecuteScalarAsync<int>(
            """
            SELECT count(*)::int FROM crawl_target t
            WHERE EXISTS (SELECT 1 FROM i3_mud m WHERE m.host = t.host AND m.port = t.port)
            """);

        Console.WriteLine($"i3 targets    {targets} addresses in the registry came from the mudlist");

        var samples = await connection.QueryAsync<(string? Reason, int Rows, int? Total)>(
            """
            SELECT coalesce(unmeasurable_reason, 'counted') AS "Reason",
                   count(*)::int AS "Rows",
                   sum(count)::int AS "Total"
            FROM presence_sample WHERE source = 'i3'
            GROUP BY 1 ORDER BY 2 DESC
            """);

        var rows = samples.ToList();

        Console.WriteLine(rows.Count == 0
            ? "i3 presence   no rows — nothing was bound and answering yet"
            : "i3 presence");

        foreach (var row in rows)
        {
            Console.WriteLine(row.Reason == "counted"
                ? $"  counted     {row.Rows} rows, {row.Total} players in total"
                : $"  {row.Reason,-11} {row.Rows} rows");
        }
    }
}
