-- Removes §11's unique-player estimate, which cannot be measured.
--
-- The promise was that salted hashes with a rotating salt make a unique-player estimate possible
-- inside an epoch while re-identification across two of them stays impossible. The first half does
-- not hold. Players rename themselves — every platform this site indexes allows it and MU* culture
-- encourages it — so one player who changes name inside a single epoch hashes to two values and is
-- counted twice. The overcount is unbounded, and it is uncorrectable in principle rather than merely
-- unimplemented: correcting it needs exactly the linkage between one person's two names that the
-- rotating salt exists to prevent. A number built that way, published as a count of players, would
-- be our parser's limitation printed as a fact about somebody's playerbase.
--
-- Nothing measured is lost here. No probe ever produced an estimate: the WHO parser counts rows and
-- never extracts a name, so PresenceAggregates was never constructed outside a test and both columns
-- have been NULL in every deployment that ever ran. This is the removal of an unkept promise, not of
-- a record.
--
-- The raw presence_sample.aggregates JSON keeps whatever keys it already holds. Dropping keys out of
-- a jsonb column would be rewriting rows to erase something the reader now ignores anyway, and
-- §7.5's habit is that we do not rewrite history to make it tidier.

ALTER TABLE presence_rollup_hour
    DROP CONSTRAINT IF EXISTS presence_rollup_hour_estimate_names_its_epoch,
    DROP COLUMN IF EXISTS peak_distinct_estimate,
    DROP COLUMN IF EXISTS salt_epoch;

ALTER TABLE presence_rollup_day
    DROP CONSTRAINT IF EXISTS presence_rollup_day_estimate_names_its_epoch,
    DROP COLUMN IF EXISTS peak_distinct_estimate,
    DROP COLUMN IF EXISTS salt_epoch;
