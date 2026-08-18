-- Adds a count histogram to the rollups, so a median survives raw-row retention. 0011 kept a
-- tally and min/max/sum, which can't answer "what's typical" (a mean is skewed by one busy
-- evening); computing the median from presence_sample directly would chain the rankings surface
-- to raw retention, silently shortening the window once raw partitions are dropped.
--
-- Exact, not approximate: a bucket holds one entry per distinct count actually read, bounded by
-- the number of counted samples in it (a couple of entries per hour at the crawler's cadence).
-- No bucketing, no sketch, no error term — summing these maps and walking to the half-way point
-- returns exactly what percentile_disc(0.5) would return over the same rows.
--
-- Shape: key is the count read (text, since jsonb object keys are text), value is how many
-- probes read it. `{"0": 3, "2": 8, "14": 1}` — a measured zero is a key like any other.
--
-- §5.4's three states are unchanged: a bucket that was probed and never counted has no
-- distribution, same as it has no min/max/sum.

ALTER TABLE presence_rollup_hour ADD COLUMN count_histogram jsonb;
ALTER TABLE presence_rollup_day  ADD COLUMN count_histogram jsonb;

-- How many samples a distribution accounts for. A function rather than an inlined expression
-- because a CHECK can't contain a subquery. IMMUTABLE (pure function of its argument, as CHECK
-- requires); NULL in, NULL out.
CREATE FUNCTION presence_histogram_total(histogram jsonb) RETURNS bigint
    LANGUAGE sql IMMUTABLE PARALLEL SAFE AS $$
    SELECT CASE WHEN histogram IS NULL THEN NULL
                ELSE (SELECT coalesce(sum(value::bigint), 0) FROM jsonb_each_text(histogram)) END
$$;

-- Backfill from raw, only where raw is complete for that bucket: a reconstructed distribution
-- is written only if it adds up to the tally already recorded, so a bucket with partially
-- retained raw rows is left without a histogram rather than given a distribution skewed by
-- whatever survived retention. Both grains are rebuilt from raw independently (day not derived
-- from hour), matching how NpgsqlPresenceRollupStore rolls them.
UPDATE presence_rollup_hour r
   SET count_histogram = h.histogram
  FROM (SELECT game_id, bucket, jsonb_object_agg(value::text, n) AS histogram
          FROM (SELECT s.game_id,
                       date_trunc('hour', s.at AT TIME ZONE 'UTC') AT TIME ZONE 'UTC' AS bucket,
                       s.count AS value,
                       count(*) AS n
                  FROM presence_sample s
                 WHERE s.count IS NOT NULL
                 GROUP BY 1, 2, 3) f
         GROUP BY game_id, bucket) h
 WHERE r.game_id = h.game_id
   AND r.hour = h.bucket
   AND presence_histogram_total(h.histogram) = r.counted_samples;

UPDATE presence_rollup_day r
   SET count_histogram = h.histogram
  FROM (SELECT game_id, bucket, jsonb_object_agg(value::text, n) AS histogram
          FROM (SELECT s.game_id,
                       date_trunc('day', s.at AT TIME ZONE 'UTC') AT TIME ZONE 'UTC' AS bucket,
                       s.count AS value,
                       count(*) AS n
                  FROM presence_sample s
                 WHERE s.count IS NOT NULL
                 GROUP BY 1, 2, 3) f
         GROUP BY game_id, bucket) h
 WHERE r.game_id = h.game_id
   AND r.day = h.bucket
   AND presence_histogram_total(h.histogram) = r.counted_samples;

-- Two invariants, deliberately not a third: a distribution must have a count and must sum to
-- the tally, but the converse (every counted bucket has one) is NOT asserted here, since a
-- deployment that had already dropped raw partitions can have buckets that can never get one.
-- That converse is instead a property of the writer, asserted in the Postgres tests — readers
-- must treat a missing histogram as out of reach, never as empty.
ALTER TABLE presence_rollup_hour
    ADD CONSTRAINT presence_rollup_hour_histogram_needs_a_count CHECK (
        count_histogram IS NULL OR counted_samples > 0),
    ADD CONSTRAINT presence_rollup_hour_histogram_totals_the_samples CHECK (
        count_histogram IS NULL OR presence_histogram_total(count_histogram) = counted_samples);

ALTER TABLE presence_rollup_day
    ADD CONSTRAINT presence_rollup_day_histogram_needs_a_count CHECK (
        count_histogram IS NULL OR counted_samples > 0),
    ADD CONSTRAINT presence_rollup_day_histogram_totals_the_samples CHECK (
        count_histogram IS NULL OR presence_histogram_total(count_histogram) = counted_samples);
