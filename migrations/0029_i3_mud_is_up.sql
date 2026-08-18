-- Stop asking a mud the router has already marked down (§7.2, I3PresenceChoice.MayAsk).
-- MayAsk has always checked IsUp && Answers("who"), but no column carried "up" into the
-- database, so a mud the router marks down kept being asked every cycle regardless.
--
-- Defaulted true rather than false: existing rows were written before this column existed and
-- only for muds the mudlist currently lists, so "unknown yet" and "reported up" coincide until
-- the next cycle refreshes every row with the router's real answer (same pattern as answers_who).
--
-- No BEGIN/COMMIT: MigrationRunner opens its own transaction per script and writes the ledger
-- entry inside it.

ALTER TABLE i3_mud ADD COLUMN is_up boolean NOT NULL DEFAULT true;

DROP INDEX i3_mud_askable_idx;
CREATE INDEX i3_mud_askable_idx
    ON i3_mud (last_asked_at) WHERE game_id IS NOT NULL AND answers_who AND is_up;
