using Dapper;

using MUI.Catalog;

using Npgsql;

namespace MUI.Crawler.Persistence;

/// <summary>
/// §8.1's on-demand check, against the crawl registry: bring this game's next probe forward.
/// </summary>
/// <remarks>
/// <para>
/// <c>LEAST</c> rather than an assignment, so an ask can only ever make a probe sooner. A target
/// already overdue stays where it is in the queue rather than being pushed back to now, and a
/// claimant pressing the button twice cannot walk their own game backwards.
/// </para>
/// <para>
/// Every endpoint of the game moves, because a game is claimed and a target is an address: an
/// operator who has just edited <c>mush.cnf</c> does not know or care which of their listeners we
/// happen to have a row for.
/// </para>
/// <para>
/// It schedules and does not dial. The crawl loop picks the target up on its next pass, where
/// <c>CRAWL DELAY</c>, the concurrency cap and §7.2's resolved-address gate all still apply — none
/// of which a page may bypass.
/// </para>
/// </remarks>
public sealed class NpgsqlOnDemandProbes(NpgsqlDataSource source) : IOnDemandProbes
{
    public async Task<bool> BringForwardAsync(
        Guid gameId,
        DateTimeOffset at,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        var moved = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE crawl_target
               SET next_probe_at = LEAST(next_probe_at, @at)
             WHERE game_id = @gameId
            """,
            new { gameId, at = at.ToUniversalTime() },
            cancellationToken: cancellationToken));

        return moved > 0;
    }
}
