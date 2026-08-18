-- Gives "no path from here to there" its own cause (§5.3). ENETUNREACH/EHOSTUNREACH/ENETDOWN
-- were falling through to `error`/`timeout`, so a nonexistent route was indistinguishable from a
-- slow one.
--
-- Legitimately part of a game's record, not our own limitation (rule 5): "reachable" is measured
-- from one vantage point at intervals, so "a game with a routing problem to our host is
-- unreachable and perfectly alive" is exactly the sentence this vocabulary exists to say.
--
-- One word for all three errnos — availability_interval.detail (0027) carries the specific
-- errno for anyone who needs it.
--
-- No backfill: rows already written as `timeout` don't distinguish a real timeout from a route
-- that never existed (the message that would tell them apart wasn't kept until 0027). Rows
-- re-sort as games are next probed.
--
-- No BEGIN/COMMIT: MigrationRunner opens its own transaction per script and writes the ledger
-- entry inside it.

ALTER TABLE availability_interval
    DROP CONSTRAINT availability_interval_cause_vocabulary,
    ADD CONSTRAINT availability_interval_cause_vocabulary CHECK (cause IN (
        'none', 'dns', 'refused', 'tls', 'timeout', 'handshake_stalled', 'no_route'));
