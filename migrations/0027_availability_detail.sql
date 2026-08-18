-- Keeps what the dial actually said, alongside the word we filed it under (§5.3).
--
-- `availability_interval.cause` is a six-word vocabulary and three of the words are wastebaskets.
-- `TelnetProbe` computed a real message for every failure — "No route to host", "Connection reset by
-- peer", "Temporary failure in name resolution" — and then `FailureReading.CauseOf` mapped anything it
-- did not recognise to `timeout` and the message was dropped on the floor. Four days of production
-- (2026-08-15 to 08-18) hold 182 dark episodes, 157 of them labelled `timeout` or `handshake_stalled`
-- with nothing else recorded anywhere: the container keeps roughly thirty minutes of logs, and a game
-- stays dark for six hours, so by the time anyone asks why, the answer is gone.
--
-- Nullable, and evidence rather than a key. A reachable interval carries no message, and — this is the
-- part that matters — a message that changed while the state and the cause did not is **not** a
-- transition. §5.3 says only a change of state or cause writes one, and a socket message carrying an
-- address or a port would otherwise split one outage into a row per probe, which is precisely the
-- sample-series-with-extra-steps this table exists to avoid. `AvailabilityWriter` compares state and
-- cause only; `AvailabilityStorePostgresTests.ADetailThatChangedDoesNotWriteATransition` pins it.
--
-- No backfill. Every existing row was written when the message was not kept, and inventing one now
-- would be back-dating a conclusion this migration reached rather than recording what was measured.
-- The rows fill in as games are next probed.
--
-- No BEGIN/COMMIT: MigrationRunner opens a transaction around each script and writes the ledger entry
-- inside it, so a script opening its own commits half the work and then fails the ledger insert.

ALTER TABLE availability_interval
    ADD COLUMN detail text;

COMMENT ON COLUMN availability_interval.detail IS
    'What the dial said, for the interval''s first probe. Evidence for a human reading a dark game; '
    'never compared, never part of the transition rule (§5.3).';
