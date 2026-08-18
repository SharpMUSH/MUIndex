-- presence_rollup_hour/day: the rollups partitioning (0003) exists for, so retention can drop
-- whole raw partitions rather than row-by-row deletes.
--
-- Preserves §5.4's three states — a rollup that turns "uncountable" or "not measured" into a
-- zero destroys the thing this schema is careful about:
--
--   counted_samples > 0                             -> filled cell, including a measured zero
--   counted_samples = 0 AND unmeasurable_samples > 0 -> hatched cell: probed, could not count
--   no row at all                                    -> not measured
--
-- min/max/sum are over counted samples only, NULL when there were none.
CREATE TABLE presence_rollup_hour (
    game_id              uuid NOT NULL REFERENCES game (id),

    -- UTC hour start. Truncation happens in UTC on both sides so a session's TimeZone setting
    -- can't move an hour into its neighbour.
    hour                 timestamptz NOT NULL,

    counted_samples      integer NOT NULL,
    unmeasurable_samples integer NOT NULL,

    -- Mean stored as sum/count (not averaged directly) so the daily rollup can be computed
    -- exactly from the hourly one, rather than by averaging averages over uneven sample counts.
    min_count            integer,
    max_count            integer,
    sum_count            bigint,
    mean_count           numeric GENERATED ALWAYS AS (sum_count::numeric / NULLIF(counted_samples, 0)) STORED,

    -- §11: the salt rotates, so an estimate is only valid within one epoch — recorded so nothing
    -- downstream combines two epochs. NULL on both when an hour's samples spanned a rotation.
    salt_epoch             text,

    -- Largest single-sample estimate within that epoch, deliberately not a union (a union needs
    -- the hashes, which never leave the probe).
    peak_distinct_estimate integer,

    PRIMARY KEY (game_id, hour),

    -- A rollup row asserts something was measured; a row of zeroes would be "not measured"
    -- wearing a measurement's clothes.
    CONSTRAINT presence_rollup_hour_measured_something CHECK (
        counted_samples > 0 OR unmeasurable_samples > 0),

    CONSTRAINT presence_rollup_hour_tallies_are_not_negative CHECK (
        counted_samples >= 0 AND unmeasurable_samples >= 0),

    -- An uncounted hour has no count; a counted one has all three.
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

-- Serves the ecosystem dashboard/activity band, which read every game over a window.
CREATE INDEX presence_rollup_hour_hour_idx ON presence_rollup_hour (hour);

-- Same shape, one day at a time, kept forever (§5.2). Computed from the hourly rollup (not raw)
-- so it survives raw rows being dropped and stays exact.
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

-- How far each rollup job has consumed the raw table: lets the rollup resume rather than
-- re-aggregate history, and stops retention dropping a raw partition not yet rolled up.
CREATE TABLE presence_rollup_state (
    scope             text PRIMARY KEY,
    rolled_up_through timestamptz NOT NULL,
    updated_at        timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT presence_rollup_state_scope_vocabulary CHECK (scope IN ('hour', 'day'))
);
