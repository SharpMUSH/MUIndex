-- Stop asking a mud the router has already told us is down (spec §7.2, I3PresenceChoice.MayAsk).
--
-- `I3PresenceChoice.MayAsk` has checked `mud.IsUp && mud.Answers("who")` since the I3 pass was first
-- written, and 0021's own askable-index comment already asked "who is bound, up, consenting, and
-- due?" — but no column ever carried "up" into the database, so `AskableAsync` could only answer
-- two of those four questions. A mud the router marks down keeps its row forever (I3 never removes a
-- listing), so it kept being asked every cycle on the ordinary 30-minute floor: measured on prod
-- 2026-08-18, 8 of 87 bound muds had a 100% who-reply failure rate distinct from the rate-limit
-- flakiness 0029's sibling fix addresses, all bound for days, all still "up" per their own
-- last_seen_at cadence but answering nothing — the router's own status was never consulted.
--
-- Defaulted true rather than false: an existing row was upserted under the old code, which only ever
-- wrote rows for muds the mudlist currently lists, so "we don't know yet" and "reported up" coincide
-- until the next cycle refreshes every row with the router's real answer, same as `answers_who` does.
--
-- No BEGIN/COMMIT: MigrationRunner opens a transaction around each script and writes the ledger entry
-- inside it, so a script opening its own commits half the work and then fails the ledger insert.

ALTER TABLE i3_mud ADD COLUMN is_up boolean NOT NULL DEFAULT true;

DROP INDEX i3_mud_askable_idx;
CREATE INDEX i3_mud_askable_idx
    ON i3_mud (last_asked_at) WHERE game_id IS NOT NULL AND answers_who AND is_up;
