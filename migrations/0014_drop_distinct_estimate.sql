-- Removes §11's unique-player estimate: unmeasurable in principle, not just unimplemented.
-- Salted rotating hashes were meant to allow a within-epoch estimate while preventing
-- cross-epoch re-identification, but players rename themselves within an epoch, so one player
-- hashes to two values and is double-counted — an overcount that can't be corrected without the
-- cross-epoch linkage the rotating salt exists to prevent.
--
-- No measured data is lost: the WHO parser never extracts names, so these columns were NULL in
-- every deployment that ran.
--
-- presence_sample.aggregates (jsonb) is left untouched — rows are not rewritten to drop keys a
-- reader now ignores (§7.5's habit).

ALTER TABLE presence_rollup_hour
    DROP CONSTRAINT IF EXISTS presence_rollup_hour_estimate_names_its_epoch,
    DROP COLUMN IF EXISTS peak_distinct_estimate,
    DROP COLUMN IF EXISTS salt_epoch;

ALTER TABLE presence_rollup_day
    DROP CONSTRAINT IF EXISTS presence_rollup_day_estimate_names_its_epoch,
    DROP COLUMN IF EXISTS peak_distinct_estimate,
    DROP COLUMN IF EXISTS salt_epoch;
