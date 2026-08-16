-- The distribution behind a rolled-up bucket, so a median survives the raw rows being dropped.
--
-- Migration 0011 kept a tally and a min/max/sum, which answers "how many probes" and "how busy at
-- the extremes" but cannot answer "what is the typical count" — a mean is pulled around by one
-- evening a game was linked from somewhere, and the rankings have always argued for a median over a
-- peak for exactly that reason. The median was therefore computed from `presence_sample` directly,
-- which chained the whole rankings surface to raw retention: §5.2 authorises dropping raw partitions
-- once they have been aggregated, and the day the first one goes a seven-day median still works and
-- a ninety-day one silently shortens without saying so.
--
-- So the distribution moves into the rollup, which is the copy that outlives raw.
--
-- **It is exact, not approximate.** A bucket holds one entry per distinct count some probe actually
-- read, so its size is bounded by the number of counted samples in it — at the crawler's cadence
-- that is a couple of entries for an hour and a dozen for a day. There is no bucketing, no sketch
-- and no error term to explain on the page: summing these maps over a window and walking to the
-- half-way position returns the same number `percentile_disc(0.5)` returns over the same rows, which
-- is what the Postgres tests assert.
--
-- Shape: the key is the count a probe read, as text because a jsonb object key is text; the value is
-- how many probes read it. `{"0": 3, "2": 8, "14": 1}` is twelve probes, three of which found an
-- empty game — and a measured zero is a measurement, so it is a key here like any other.
--
-- §5.4's three states are unchanged and are still carried by the tally: a bucket that was probed and
-- never counted has no distribution, exactly as it has no min, max or sum.

ALTER TABLE presence_rollup_hour ADD COLUMN count_histogram jsonb;
ALTER TABLE presence_rollup_day  ADD COLUMN count_histogram jsonb;

-- How many samples a distribution accounts for. A function rather than an inlined expression because
-- a CHECK cannot contain a subquery, and this invariant is worth having the database enforce: a
-- histogram whose values do not add up to the tally is a median computed over a different number of
-- probes than the one the page prints beside it.
--
-- IMMUTABLE because it is a pure function of its argument, which is what a CHECK requires. NULL in,
-- NULL out — the constraints handle the null case themselves and this must not turn it into a 0.
CREATE FUNCTION presence_histogram_total(histogram jsonb) RETURNS bigint
    LANGUAGE sql IMMUTABLE PARALLEL SAFE AS $$
    SELECT CASE WHEN histogram IS NULL THEN NULL
                ELSE (SELECT coalesce(sum(value::bigint), 0) FROM jsonb_each_text(histogram)) END
$$;

-- Backfill from raw, and **only where raw is complete for that bucket**.
--
-- The guard is the last clause: a reconstructed distribution is written only if it adds up to the
-- tally the rollup already recorded. Where retention has taken some of a bucket's raw rows the two
-- disagree, and the bucket is left without a histogram rather than given a distribution over the
-- rows that happen to have survived — which would be a median of whatever retention left behind,
-- presented as a median of what we measured.
--
-- Both grains are rebuilt from raw independently, exactly as `NpgsqlPresenceRollupStore` rolls them:
-- the day is not derived from the hour, so that the permanent grain never depends on the temporary
-- one having survived.
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

-- Two invariants, and deliberately not a third.
--
-- A distribution may only exist where something was counted, and it must add up to the tally beside
-- it. What is NOT asserted is the converse — that every counted bucket has one — because this column
-- was added to a table with history, and a deployment that had already dropped raw partitions has
-- buckets that can never be given one. A migration that failed there, or that manufactured a
-- distribution from the surviving rows, would both be worse than a bucket that says it does not
-- know.
--
-- The converse is a property of the writer instead, asserted in the Postgres tests: every bucket
-- `RollUpAsync` produces from a counted sample carries its distribution. Readers must therefore
-- treat a missing histogram as a bucket outside their reach — never as an empty one — and the
-- rankings query does, by computing its stated sample count over the same buckets it took the
-- median from.
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
