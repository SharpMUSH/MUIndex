-- Keeps what the dial actually said, alongside the cause word it's filed under (§5.3).
-- `availability_interval.cause` is a six-word vocabulary; several distinct socket errors were
-- collapsing into `timeout` or `handshake_stalled` with no detail recorded anywhere, and
-- container log retention is too short to recover it after the fact.
--
-- Nullable, and evidence rather than a key: a message changing while state and cause stay the
-- same is NOT a transition (§5.3 — only a state or cause change writes one). AvailabilityWriter
-- compares state and cause only; pinned by
-- AvailabilityStorePostgresTests.ADetailThatChangedDoesNotWriteATransition.
--
-- No backfill: existing rows were written before this column existed, and inventing a message
-- now would misrepresent what was actually measured. Rows fill in as games are next probed.
--
-- No BEGIN/COMMIT: MigrationRunner opens its own transaction per script and writes the ledger
-- entry inside it.

ALTER TABLE availability_interval
    ADD COLUMN detail text;

COMMENT ON COLUMN availability_interval.detail IS
    'What the dial said, for the interval''s first probe. Evidence for a human reading a dark game; '
    'never compared, never part of the transition rule (§5.3).';
