-- Partitions presence_rollup_hour by month, so retention on it is a DROP rather than a DELETE.
--
-- WHY NOW, and why this table only. Measured over the thirteen days on the deployment
-- (2026-08-15..08-27, 931 games): the hourly rollup takes 6,318 rows/day at 218 bytes each, which is
-- ~503 MB a year and grows with the catalogue. It is the largest growing thing here by some margin --
-- the daily rollup is 787 rows/day (~68 MB/yr) and raw presence is already partitioned and already
-- droppable. Nothing else needs this.
--
-- `presence_rollup_day` is deliberately NOT partitioned. §5.2 keeps that grain for ever, so it gets
-- no retention, so partitioning buys it no drop path -- only twelve more partitions a year, for ever,
-- for the planner to consider on every window read. Planning time is not free here: it was measured
-- at 470-700ms on this box under load, against executions of comparable length. A partition set that
-- grows without bound to serve a table that is never pruned is a cost with no matching benefit.
--
-- The conversion is cheap TODAY and expensive later, which is the whole argument for doing it before
-- retention is switched on rather than after: 82,141 rows copy in well under a second. The same
-- operation against two years of accumulated data is a maintenance window.

-- The old table keeps its rows while the new shape is built beside it. Renamed rather than copied
-- into a temporary name so the FK, indexes and constraints go with it and nothing is left pointing at
-- a table that is about to vanish.
ALTER TABLE presence_rollup_hour RENAME TO presence_rollup_hour_unpartitioned;

ALTER INDEX presence_rollup_hour_pkey RENAME TO presence_rollup_hour_unpartitioned_pkey;
ALTER INDEX presence_rollup_hour_hour_idx RENAME TO presence_rollup_hour_unpartitioned_hour_idx;

-- Same columns, same constraints, same generated column as the table it replaces -- taken from the
-- deployment's own pg_dump rather than reassembled from migrations 0011, 0014 and 0019, because
-- three migrations' worth of ALTERs is exactly the kind of thing a hand-rebuild gets subtly wrong.
--
-- The primary key must contain the partition key, which is why it stays (game_id, hour) and not
-- (hour, game_id): `hour` is already in it, so the existing key is legal unchanged and no reader's
-- access path moves.
CREATE TABLE presence_rollup_hour (
    game_id              uuid NOT NULL REFERENCES game (id),
    hour                 timestamptz NOT NULL,

    counted_samples      integer NOT NULL,
    unmeasurable_samples integer NOT NULL,

    min_count            integer,
    max_count            integer,
    sum_count            bigint,
    mean_count           numeric GENERATED ALWAYS AS (sum_count::numeric / NULLIF(counted_samples, 0)) STORED,

    count_histogram      jsonb,

    PRIMARY KEY (game_id, hour),

    CONSTRAINT presence_rollup_hour_measured_something CHECK (
        counted_samples > 0 OR unmeasurable_samples > 0),

    CONSTRAINT presence_rollup_hour_tallies_are_not_negative CHECK (
        counted_samples >= 0 AND unmeasurable_samples >= 0),

    CONSTRAINT presence_rollup_hour_counts_iff_counted CHECK (
        (counted_samples > 0) = (min_count IS NOT NULL)
        AND (counted_samples > 0) = (max_count IS NOT NULL)
        AND (counted_samples > 0) = (sum_count IS NOT NULL)),

    CONSTRAINT presence_rollup_hour_counts_are_ordered CHECK (
        min_count IS NULL OR (min_count >= 0 AND max_count >= min_count AND sum_count >= max_count)),

    CONSTRAINT presence_rollup_hour_is_on_the_hour CHECK (
        hour AT TIME ZONE 'UTC' = date_trunc('hour', hour AT TIME ZONE 'UTC')),

    CONSTRAINT presence_rollup_hour_histogram_needs_a_count CHECK (
        count_histogram IS NULL OR counted_samples > 0),

    CONSTRAINT presence_rollup_hour_histogram_totals_the_samples CHECK (
        count_histogram IS NULL OR presence_histogram_total(count_histogram) = counted_samples)
) PARTITION BY RANGE (hour);

-- Every month the old table covers, plus two ahead -- the same lead PresenceRetentionOptions.
-- MonthsOfPartitionsAhead keeps for raw, and for the same reason: there is no DEFAULT partition, so a
-- month without one is an insert error, and a calendar rollover must never be the first thing to
-- discover a maintenance pass that could not reach the database.
--
-- Named presence_rollup_hour_YYYYMM, matching presence_sample_YYYYMM, because the drop path reads a
-- partition's month back out of its name; anything named otherwise is never dropped by us.
-- Months are derived and bounded in UTC, never in the session's zone. `month` is deliberately a
-- zone-less `timestamp` holding a UTC wall clock, converted back with AT TIME ZONE 'UTC' at the point
-- it becomes a bound -- the same discipline every bucket boundary in this codebase follows, and here
-- it is load-bearing rather than tidy. PresencePartitions.CreateDdl emits explicit UTC bounds, so a
-- migration run from a session in, say, America/Chicago would otherwise cut its months at 05:00 UTC
-- and the next partition the application created would overlap one of these and fail outright.
-- Measured: under America/Chicago the two spellings do not name the same instant, under UTC they do.
DO $$
DECLARE
    month timestamp;
    last  timestamp;
BEGIN
    month := date_trunc('month', COALESCE(
        (SELECT min(hour) FROM presence_rollup_hour_unpartitioned), now()) AT TIME ZONE 'UTC');

    last := date_trunc('month', now() AT TIME ZONE 'UTC') + interval '2 months';

    WHILE month <= last LOOP
        EXECUTE format(
            'CREATE TABLE IF NOT EXISTS %I PARTITION OF presence_rollup_hour '
            || 'FOR VALUES FROM (%L) TO (%L)',
            'presence_rollup_hour_' || to_char(month, 'YYYYMM'),
            month AT TIME ZONE 'UTC',
            (month + interval '1 month') AT TIME ZONE 'UTC');

        month := month + interval '1 month';
    END LOOP;
END $$;

-- mean_count is omitted on purpose: it is GENERATED, so naming it here would be an error, and the
-- new table recomputes it from sum_count and counted_samples as each row lands.
INSERT INTO presence_rollup_hour
    (game_id, hour, counted_samples, unmeasurable_samples,
     min_count, max_count, sum_count, count_histogram)
SELECT game_id, hour, counted_samples, unmeasurable_samples,
       min_count, max_count, sum_count, count_histogram
  FROM presence_rollup_hour_unpartitioned;

-- Refuses the migration rather than silently losing rows. A copy that dropped some to a constraint
-- the old table did not have would otherwise be discovered as a hole in somebody's heatmap weeks
-- later, with the evidence already dropped below.
DO $$
DECLARE
    before bigint;
    after  bigint;
BEGIN
    SELECT count(*) INTO before FROM presence_rollup_hour_unpartitioned;
    SELECT count(*) INTO after  FROM presence_rollup_hour;

    IF before <> after THEN
        RAISE EXCEPTION
            'presence_rollup_hour partition copy lost rows: % before, % after', before, after;
    END IF;
END $$;

DROP TABLE presence_rollup_hour_unpartitioned;

-- The hour index is recreated on the parent, which cascades to every partition and to every partition
-- made later. Kept rather than dropped in favour of pruning alone: NpgsqlGameQueries.GamePage reads
-- `hour >= @from` across all games for one game's heatmap, and pruning narrows that to the months in
-- the window while this still orders within them.
CREATE INDEX presence_rollup_hour_hour_idx ON presence_rollup_hour (hour);
