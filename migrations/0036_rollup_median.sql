-- Caches each rolled-up day's median count in the database, next to the data it is derived from.
--
-- The point is not that the read gets faster (it does, modestly — see the note at the bottom). It is
-- that this is a derived value with NO staleness window. The median of a day is a pure function of
-- that day's histogram, so storing it beside the histogram means the two cannot disagree: PostgreSQL
-- recomputes it inside the same statement that writes the row, in the same transaction, and there is
-- no moment at which the stored median describes a histogram other than the one on the row.
--
-- That is the property the application-side cache in front of the listing cannot have. That one has
-- a duration, and it needs an explicit invalidation call after a deliberate write — a call somebody
-- has to remember to make, at every site that writes, for ever. This needs nothing: the "event" it
-- regenerates on is the write itself, and there is no code path that can skip it.
--
-- 0019 added the histogram precisely so a median could survive raw retention, and it does — but
-- every reader has been rebuilding it: expand the jsonb, sum the frequencies in ascending order,
-- take the first value whose running total reaches the half-way point. Measured on production:
-- 19,335 key/value pairs expanded from 5,728 rows, on every listing assembly.
--
-- `mean_count` on this table has been a generated column since 0011; this is the same move for the
-- figure that is actually read, since a mean is skewed by one busy evening and is not what any
-- surface shows.
--
-- ON PLACEMENT, because "why this table" is the question worth asking of any generated column: a
-- generation expression may only reference other columns OF ITS OWN ROW. Postgres rejects anything
-- else outright — "cannot use subquery in column generation expression" — so a per-game median hung
-- off `game`, recomputed whenever a game row is touched, is not a design that was rejected here but
-- one the database refuses to express. The only legal home is the row that holds the histogram, and
-- a write to that row is exactly the event "new distribution for this (game, day)". Right answer and
-- only answer, which is a good sign rather than a lucky one.
--
-- WHAT THIS DOES NOT HELP, deliberately: the rankings (NpgsqlGameQueries.Rankings) and the window
-- sorts (PlayersOverWindowAsync) pool a whole window's histograms into ONE distribution and take a
-- median of that. The median of pooled counts is not the median of per-day medians, so those keep
-- their walk and must not be "optimised" onto this column. Only a per-day median can read it —
-- DailyMediansAsync, which asks for exactly one median per day per game.
--
-- Nor does the hourly rollup get one: nothing walks presence_rollup_hour's histogram for a median.
-- A column nobody reads is a column that rots.

-- The walk of 0019's doc comment, as a pure function of one histogram: smallest count whose running
-- tally in ascending order reaches ceil(n / 2.0). Deliberately the same arithmetic and the same
-- `ceil(n / 2.0)` rather than `(n + 1) / 2` — sum() over bigint returns exact numeric, so an even
-- sample count needs the element after the true midpoint.
--
-- IMMUTABLE is not decoration: a STORED generated column refuses anything else, and this genuinely
-- is one (no clock, no locale, no GUC — jsonb object keys are text and the casts are exact).
-- Verified against production before this migration was written: identical to the walk it replaces
-- on all 5,728 rollup rows, and identical to percentile_disc(0.5) over the expanded distribution on
-- the same rows, which is the equality 0019 claimed and nothing had checked.
CREATE FUNCTION presence_histogram_median(histogram jsonb) RETURNS integer
    LANGUAGE sql IMMUTABLE PARALLEL SAFE AS $$
    SELECT CASE WHEN histogram IS NULL THEN NULL ELSE (
        SELECT min(w.value)
          FROM (SELECT e.key::int AS value,
                       sum(e.value::bigint) OVER (ORDER BY e.key::int) AS running,
                       ceil(sum(e.value::bigint) OVER () / 2.0)        AS half
                  FROM jsonb_each_text(histogram) AS e(key, value)) w
         WHERE w.running >= w.half) END
$$;

-- NULL where there is no distribution, which keeps §5.4's three states intact: a day probed but
-- never counted has no histogram, so it has no median either — not a zero. Readers must go on
-- treating a missing median as out of reach rather than as an absence of players.
--
-- No accompanying sample count: `counted_samples` is already on the row and the histogram is
-- constrained to total it (presence_rollup_day_histogram_totals_the_samples, 0019), so a second
-- column holding the same number is one more thing that can disagree with itself.
--
-- STORED is not a trigger and there is no event to choose: PostgreSQL evaluates the expression as
-- part of any INSERT or UPDATE that writes the row, within the same statement. Three things follow,
-- all checked against production rather than assumed:
--
--   * This ADD COLUMN rewrites the table under ACCESS EXCLUSIVE. Measured at 3.7s over 10,228 rows.
--     Brief, but it is a write lock on the rollup, so do not run it in the middle of a maintenance
--     pass — PresenceMaintenance will block behind it.
--   * Existing rows are filled by the rewrite itself (5,729 of 10,228 here, exactly the ones holding
--     a histogram). There is no backfill step and none should be added.
--   * NpgsqlPresenceRollupStore needs no change and must not get one. Its upsert already names its
--     columns explicitly, and naming this one would be an outright error — "cannot insert a
--     non-DEFAULT value into column median_count" — so the writer cannot drift out of step with the
--     histogram even by accident.
--
-- THE ONE TRAP: editing presence_histogram_median later does NOT recompute what is already stored.
-- PostgreSQL trusts the IMMUTABLE promise and never revisits a written row. If the definition ever
-- has to change, the migration that changes it must also force a rewrite (an ALTER TABLE that
-- rewrites, or DROP and re-ADD the column) — otherwise old days keep the old arithmetic and new days
-- get the new one, silently, which is the failure this column exists to make impossible.
ALTER TABLE presence_rollup_day
    ADD COLUMN median_count integer
    GENERATED ALWAYS AS (presence_histogram_median(count_histogram)) STORED;

-- On speed, stated honestly because it is the secondary benefit and was measured under a load that
-- moved while it was being measured: replacing the walk with a column read took DailyMediansAsync
-- from ~487-575ms to ~329-445ms over 935 games on production, roughly 1.3-1.4x. Under the heavier
-- load that made this query worth looking at, the same query measured 6.5s; the ratio holds but the
-- absolute saving is larger exactly when it matters. The equivalence is the part that was checked
-- rigorously: identical rows, 0 differing in either direction across 5,319, against the query this
-- replaces.
