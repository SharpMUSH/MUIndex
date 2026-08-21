using Dapper;

using Npgsql;

namespace MUI.Catalog.Persistence;

public sealed partial class NpgsqlGameQueries
{
    /// <summary>
    /// The §5.1 ladder as a SQL parameter, generated from the enum so it cannot drift from it.
    /// </summary>
    private static readonly string[] SourceLadder = Enum.GetValues<FieldSource>()
        .OrderBy(FieldPrecedence.RankOf)
        .Select(SqlEnums.ToDb)
        .ToArray();

    /// <summary>
    /// Codebase share and protocol adoption over the listed games (spec §9).
    /// </summary>
    /// <remarks>
    /// Every figure is a count of <em>games</em>; this method has no access to presence at all, so a
    /// player total can never be computed here (§15.7 forbids publishing one). The protocol
    /// denominator is games with a completed session (<c>reachable</c> interval), read off
    /// availability rather than off capability rows — counting games that merely have capability rows
    /// would let a handshake that measured nothing quietly raise every share. Archived games are
    /// excluded (§7.5): a handshake from 2019 isn't evidence about what the hobby runs now.
    /// </remarks>
    public async Task<EcosystemDashboard> EcosystemAsync(CancellationToken cancellationToken = default)
    {
        var now = _time.GetUtcNow();

        await using var connection = await source.OpenConnectionAsync(cancellationToken);

        var totals = await connection.QuerySingleAsync<EcosystemTotalsRow>(new CommandDefinition(
            $"""
            SELECT
              (SELECT count(*)::int FROM game WHERE state NOT IN {ListedStates} AND {Public}) AS Listed,

              -- A completed session, which is what a measured capability is a capability of.
              (SELECT count(DISTINCT a.game_id)::int
                 FROM availability_interval a
                 JOIN game g ON g.id = a.game_id
                WHERE {PublicG} AND g.state NOT IN {ListedStates} AND a.state = 'reachable') AS Handshakes,

              -- Games whose MSSP report we hold — a different set, hence its own denominator.
              (SELECT count(DISTINCT f.game_id)::int
                 FROM game_field f
                 JOIN game g ON g.id = f.game_id
                WHERE {PublicG} AND g.state NOT IN {ListedStates} AND f.source = 'mssp') AS MsspReports,

              -- How stale the stalest handshake is, so the page can say how old the picture is.
              (SELECT min(f.last_confirmed_at)
                 FROM game_field f
                 JOIN game g ON g.id = f.game_id
                WHERE {PublicG} AND g.state NOT IN {ListedStates} AND f.source = 'handshake'
                  AND f.field LIKE 'capability.%.measured') AS OldestHandshake,

              -- The raw material of the curve this page cannot yet draw (§5.1's change ledger).
              (SELECT count(*)::int
                 FROM field_change c
                 JOIN game g ON g.id = c.game_id
                WHERE {PublicG} AND g.state NOT IN {ListedStates}
                  AND c.field LIKE 'capability.%.measured') AS CapabilityTransitions
            """,
            cancellationToken: cancellationToken));

        var codebases = (await connection.QueryAsync<string>(new CommandDefinition(
            $"""
            SELECT DISTINCT ON (f.game_id) f.value
              FROM game_field f
              JOIN game g ON g.id = f.game_id
             WHERE {PublicG} AND g.state NOT IN {ListedStates} AND f.field = 'CODEBASE' AND f.value <> ''
             ORDER BY f.game_id, array_position(@ladder::text[], f.source), f.last_confirmed_at DESC
            """,
            new { ladder = SourceLadder },
            cancellationToken: cancellationToken))).ToList();

        var capabilities = (await connection.QueryAsync<CapabilityTallyRow>(new CommandDefinition(
            $"""
            SELECT winner.field AS Field, winner.value AS Value, count(*)::int AS Games
              FROM (SELECT DISTINCT ON (f.game_id, f.field) f.field, f.value
                      FROM game_field f
                      JOIN game g ON g.id = f.game_id
                     WHERE {PublicG} AND g.state NOT IN {ListedStates} AND f.field LIKE 'capability.%'
                     ORDER BY f.game_id, f.field,
                              array_position(@ladder::text[], f.source), f.last_confirmed_at DESC) winner
             GROUP BY winner.field, winner.value
            """,
            new { ladder = SourceLadder },
            cancellationToken: cancellationToken))).ToList();

        return new EcosystemDashboard(
            now,
            totals.Listed,
            totals.Handshakes,
            totals.MsspReports,
            totals.OldestHandshake,
            totals.CapabilityTransitions,
            CodebaseUsage.Of(codebases, totals.Listed),
            ProtocolsOf(capabilities, totals.Handshakes, totals.MsspReports));
    }

    private sealed class EcosystemTotalsRow
    {
        public int Listed { get; init; }

        public int Handshakes { get; init; }

        public int MsspReports { get; init; }

        public DateTimeOffset? OldestHandshake { get; init; }

        public int CapabilityTransitions { get; init; }
    }

    private sealed class CapabilityTallyRow
    {
        public string Field { get; init; } = string.Empty;

        public string Value { get; init; } = string.Empty;

        public int Games { get; init; }
    }

    /// <summary>
    /// One row per capability worth reporting, measured beside declared.
    /// </summary>
    /// <remarks>
    /// A capability nothing offered is reported as <b>unmeasured</b>, never nought per cent — derived
    /// from the tally, not compiled in, so a column starts reporting a share the day the first
    /// measurement lands (TLS is the standing example: the probe dials plaintext). §9's headline four
    /// are always listed, even unmeasured, since "not measured yet" is only visible if the row exists.
    /// </remarks>
    private static IReadOnlyList<ProtocolAdoption> ProtocolsOf(
        IReadOnlyList<CapabilityTallyRow> tallies,
        int handshakes,
        int msspReports)
    {
        var offered = new Dictionary<string, int>(StringComparer.Ordinal);
        var declined = new Dictionary<string, int>(StringComparer.Ordinal);
        var declared = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var tally in tallies)
        {
            if (CapabilityFields.NameOf(tally.Field) is not { } name)
            {
                continue;
            }

            var bucket = (CapabilityFields.IsMeasured(tally.Field), tally.Value) switch
            {
                (true, "true") => offered,
                (true, "false") => declined,
                (false, "true") => declared,
                _ => null,
            };

            if (bucket is null)
            {
                continue;
            }

            bucket[name] = bucket.GetValueOrDefault(name) + tally.Games;
        }

        // A protocol observed at all (either direction) gets a measurable column; one never observed
        // gets none — "0%" would be our own reach reported as the hobby's.
        var measurable = offered.Keys.Concat(declined.Keys).ToHashSet(StringComparer.Ordinal);

        var names = EcosystemProtocols.Headline
            .Concat(measurable.Concat(declared.Keys).Order(StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return names
            .Select(name => new ProtocolAdoption(
                name,
                measurable.Contains(name) ? offered.GetValueOrDefault(name) : null,
                declined.GetValueOrDefault(name),
                handshakes,
                declared.GetValueOrDefault(name),
                msspReports))
            .ToList();
    }
}
