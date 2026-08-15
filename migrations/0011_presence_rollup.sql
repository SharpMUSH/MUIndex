-- spec §5.2 — the rollups the monthly partitioning in 0003 was put there for, and the retention that
-- can then work on whole partitions instead of row-by-row deletes over hundreds of millions of rows.
--
-- The rule these two tables exist to carry forward is §5.4's: an hour has THREE states, and a rollup
-- that turns "probed but uncountable" or "not measured" into a zero has destroyed the only thing this
-- schema is careful about. So the tally is kept, not the conclusion:
--
--   counted_samples > 0                             -> a filled cell, INCLUDING a measured zero
--   counted_samples = 0 AND unmeasurable_samples > 0 -> a hatched cell: probed, could not count
--   no row at all                                    -> not measured, which is the absence of a row
--                                                       here exactly as it is in presence_sample
--
-- min/max/sum are computed over counted samples only and are NULL when there were none, so a
-- reader that asks for a number gets nothing rather than a zero it could mistake for a measurement.
CREATE TABLE presence_rollup_hour (
    game_id              uuid NOT NULL REFERENCES game (id),

    -- The start of the hour, in UTC. Truncation is done in UTC on both sides of the wire so that a
    -- session's TimeZone setting can never move an hour into its neighbour.
    hour                 timestamptz NOT NULL,

    -- §5.4's tally. Both of these are counts of rows in presence_sample, never of hours.
    counted_samples      integer NOT NULL,
    unmeasurable_samples integer NOT NULL,

    -- Over the counted samples alone. The mean is stored as sum/count rather than as an average so
    -- that the daily rollup can be computed from the hourly one exactly, rather than by averaging
    -- averages over hours that saw different numbers of probes.
    min_count            integer,
    max_count            integer,
    sum_count            bigint,
    mean_count           numeric GENERATED ALWAYS AS (sum_count::numeric / NULLIF(counted_samples, 0)) STORED,

    -- §11. The salt rotates, and an estimate may only ever be read within one epoch: the epoch is
    -- recorded so that nothing downstream can combine two of them. Where an hour's samples spanned a
    -- rotation both columns are NULL — the honest answer, because the hashes that would have been
    -- needed to merge the two sets are never persisted.
    salt_epoch             text,

    -- The largest single-sample estimate inside that one epoch, and deliberately not a union: a union
    -- of distinct players needs the hashes, and the hashes never leave the probe. A lower bound that
    -- says so beats a total that cannot be justified.
    peak_distinct_estimate integer,

    PRIMARY KEY (game_id, hour),

    -- A rollup row is a statement that something was measured in that hour. A row of zeroes would be
    -- "not measured" wearing a measurement's clothes, which is §5.4's third state collapsed into the
    -- other two — so the schema refuses to hold one.
    CONSTRAINT presence_rollup_hour_measured_something CHECK (
        counted_samples > 0 OR unmeasurable_samples > 0),

    CONSTRAINT presence_rollup_hour_tallies_are_not_negative CHECK (
        counted_samples >= 0 AND unmeasurable_samples >= 0),

    -- The other half of the same rule: an hour with no counted sample has no count, and an hour with
    -- one has all three. Nothing may write an uncountable hour with a zero in it.
    CONSTRAINT presence_rollup_hour_counts_iff_counted CHECK (
        (counted_samples > 0) = (min_count IS NOT NULL)
        AND (counted_samples > 0) = (max_count IS NOT NULL)
        AND (counted_samples > 0) = (sum_count IS NOT NULL)),

    CONSTRAINT presence_rollup_hour_counts_are_ordered CHECK (
        min_count IS NULL OR (min_count >= 0 AND max_count >= min_count AND sum_count >= max_count)),

    CONSTRAINT presence_rollup_hour_estimate_names_its_epoch CHECK (
        peak_distinct_estimate IS NULL OR salt_epoch IS NOT NULL),

    CONSTRAINT presence_rollup_hour_is_on_the_hour CHECK (
        hour AT TIME ZONE 'UTC' = date_trunc('hour', hour AT TIME ZONE 'UTC'))
);

-- §9's heatmap reads one game over a window, which the primary key serves. This is for the ecosystem
-- dashboard and the activity band, which read every game over one.
CREATE INDEX presence_rollup_hour_hour_idx ON presence_rollup_hour (hour);

-- The same shape a day at a time, and the one §5.2 keeps for ever. Computed from the hourly rollup
-- rather than from the raw table, so it survives the raw rows being dropped and stays exact.
CREATE TABLE presence_rollup_day (
    game_id              uuid NOT NULL REFERENCES game (id),
    day                  timestamptz NOT NULL,

    counted_samples      integer NOT NULL,
    unmeasurable_samples integer NOT NULL,

    min_count            integer,
    max_count            integer,
    sum_count            bigint,
    mean_count           numeric GENERATED ALWAYS AS (sum_count::numeric / NULLIF(counted_samples, 0)) STORED,

    salt_epoch             text,
    peak_distinct_estimate integer,

    PRIMARY KEY (game_id, day),

    CONSTRAINT presence_rollup_day_measured_something CHECK (
        counted_samples > 0 OR unmeasurable_samples > 0),

    CONSTRAINT presence_rollup_day_tallies_are_not_negative CHECK (
        counted_samples >= 0 AND unmeasurable_samples >= 0),

    CONSTRAINT presence_rollup_day_counts_iff_counted CHECK (
        (counted_samples > 0) = (min_count IS NOT NULL)
        AND (counted_samples > 0) = (max_count IS NOT NULL)
        AND (counted_samples > 0) = (sum_count IS NOT NULL)),

    CONSTRAINT presence_rollup_day_counts_are_ordered CHECK (
        min_count IS NULL OR (min_count >= 0 AND max_count >= min_count AND sum_count >= max_count)),

    CONSTRAINT presence_rollup_day_estimate_names_its_epoch CHECK (
        peak_distinct_estimate IS NULL OR salt_epoch IS NOT NULL),

    CONSTRAINT presence_rollup_day_is_on_the_day CHECK (
        day AT TIME ZONE 'UTC' = date_trunc('day', day AT TIME ZONE 'UTC'))
);

CREATE INDEX presence_rollup_day_day_idx ON presence_rollup_day (day);

-- How far each rollup has consumed the raw table. Two jobs read this: the rollup itself, which
-- resumes from it rather than re-aggregating history on every pass, and retention, which may not drop
-- a raw partition that has not been rolled up yet — the one ordering that makes dropping raw samples
-- recoverable-in-shape rather than a loss.
CREATE TABLE presence_rollup_state (
    scope             text PRIMARY KEY,
    rolled_up_through timestamptz NOT NULL,
    updated_at        timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT presence_rollup_state_scope_vocabulary CHECK (scope IN ('hour', 'day'))
);
