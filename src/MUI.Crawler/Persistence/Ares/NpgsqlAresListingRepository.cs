using Dapper;

using MUI.Discovery;

using Npgsql;

namespace MUI.Crawler.Persistence;

/// <summary>
/// The <c>ares_listing</c> table (migration 0034).
/// </summary>
/// <remarks>
/// Nothing here deletes a row. A listing the hub stops mentioning is dated and kept, because a game
/// leaving somebody else's directory is a fact about that directory and changes nothing about
/// whether we go on probing the address (§7.4, §7.5).
/// </remarks>
public sealed class NpgsqlAresListingRepository(NpgsqlDataSource source) : IAresListingRepository
{
    private const string Columns = """
        hostname AS Hostname, port AS Port, name AS Name, description AS Description,
        genre AS Genre, website AS Website, status AS Status, last_ping AS LastPing,
        game_id AS GameId, first_seen_at AS FirstSeenAt, last_listed_at AS LastListedAt,
        delisted_at AS DelistedAt
        """;

    /// <remarks>
    /// <c>first_seen_at</c> is deliberately not overwritten on conflict: it means "when the hub first
    /// listed this address", and a refresh is not a first sighting. <c>delisted_at</c> clears,
    /// because a game that came back is listed, not dated. <c>game_id</c> is left alone —
    /// <see cref="BindAsync"/> owns it, and a pass that runs before the crawl has promoted an address
    /// must not unbind one it bound last time.
    /// </remarks>
    public async Task UpsertAsync(AresListing listing, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(listing);

        await using var connection = await source.OpenConnectionAsync(ct);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO ares_listing (
                hostname, port, name, description, genre, website, status, last_ping,
                first_seen_at, last_listed_at, delisted_at)
            VALUES (
                @hostname, @port, @name, @description, @genre, @website, @status, @lastPing,
                @firstSeenAt, @lastListedAt, NULL)
            ON CONFLICT (hostname, port) DO UPDATE
               SET name = EXCLUDED.name,
                   description = EXCLUDED.description,
                   genre = EXCLUDED.genre,
                   website = EXCLUDED.website,
                   status = EXCLUDED.status,
                   last_ping = EXCLUDED.last_ping,
                   last_listed_at = EXCLUDED.last_listed_at,
                   delisted_at = NULL
            """,
            new
            {
                hostname = listing.Hostname,
                port = listing.Port,
                name = listing.Name,
                description = listing.Description,
                genre = listing.Genre,
                website = listing.Website,
                status = listing.Status,
                lastPing = listing.LastPing,
                firstSeenAt = listing.FirstSeenAt.ToUniversalTime(),
                lastListedAt = listing.LastListedAt.ToUniversalTime(),
            },
            cancellationToken: ct));
    }

    public async Task BindAsync(string hostname, int port, Guid gameId, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE ares_listing SET game_id = @gameId WHERE hostname = @hostname AND port = @port",
            new { gameId, hostname, port },
            cancellationToken: ct));
    }

    /// <remarks>
    /// <c>delisted_at IS NULL</c> in the predicate is what keeps a second sweep from moving a date
    /// that is already recorded — the first one is when the hub stopped mentioning it.
    /// </remarks>
    public async Task<int> DelistMissingAsync(DateTimeOffset asOf, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);

        return await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE ares_listing
               SET delisted_at = @asOf
             WHERE delisted_at IS NULL AND last_listed_at < @asOf
            """,
            new { asOf = asOf.ToUniversalTime() },
            cancellationToken: ct));
    }

    public async Task<IReadOnlyList<AresListing>> AllAsync(CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);

        var rows = await connection.QueryAsync<AresListing>(new CommandDefinition(
            $"SELECT {Columns} FROM ares_listing ORDER BY hostname, port",
            cancellationToken: ct));

        return [.. rows];
    }
}
