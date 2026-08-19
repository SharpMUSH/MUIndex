using Dapper;

using MUI.Catalog;

using Npgsql;

namespace MUI.Crawler.Persistence;

/// <summary>
/// §8.1's on-demand check, against the crawl registry: bring this game's next probe forward.
/// </summary>
/// <remarks>
/// <c>LEAST</c> rather than an assignment, so an ask can only ever make a probe sooner — a target
/// already overdue stays put, and pressing the button twice can't walk it backwards. Every endpoint
/// of the game moves, since a game is claimed but a target is one address. This schedules and does
/// not dial: the crawl loop still applies <c>CRAWL DELAY</c>, the concurrency cap, and §7.2's
/// resolved-address gate on its next pass.
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
