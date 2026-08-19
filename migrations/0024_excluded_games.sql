-- A lifecycle state for an address that answers like a game and isn't one (§7.5).
--
-- Archiving can't be reused for this: ProbeIngestor restores an archived game on the next
-- successful probe (§7.5, correct for real games), so archiving something that answers reliably
-- (e.g. a dev instance) would flip state every cycle and never actually remove it from anything.
--
-- This is our judgement, stored as ours (rule 5): it gets its own state with a required reason,
-- rather than borrowing archived's meaning.
--
-- Immune to the crawl by construction: ArchiveSweeper.RestoreAsync only restores from `archived`,
-- so it doesn't touch `excluded`; the sweeper's archiving arm has an explicit guard so it can't
-- move an excluded game to `archived` once it goes dark.
--
-- §7.5's guarantees are unchanged: page, URL, history and change feed survive, and the game keeps
-- being probed forever. It only loses the default listing, rankings, and "active today".
--
-- No BEGIN/COMMIT: MigrationRunner opens its own transaction per script and writes the ledger
-- entry inside it.

ALTER TABLE game
    DROP CONSTRAINT game_state_vocabulary,
    ADD CONSTRAINT game_state_vocabulary CHECK (state IN ('active', 'quiet', 'dark', 'archived', 'excluded')),

    ADD COLUMN excluded_at timestamptz,
    ADD COLUMN excluded_reason text,

    ADD CONSTRAINT game_exclusion_is_explained CHECK (
        (state = 'excluded') = (excluded_at IS NOT NULL)
        AND (excluded_at IS NULL) = (excluded_reason IS NULL)),

    ADD CONSTRAINT game_exclusion_reason_is_not_blank CHECK (
        excluded_reason IS NULL OR length(btrim(excluded_reason)) > 0);
