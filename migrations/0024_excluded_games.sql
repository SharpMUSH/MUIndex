-- A lifecycle state for an address that answers like a game and is not one (spec §7.5).
--
-- **Why archiving could not be used, which is the whole reason this exists.** Archiving means "this
-- stopped answering", and `ProbeIngestor` restores an archived game on the *next successful probe*,
-- immediately and without a human — §7.5, and correct. So archiving a live dev instance produces a
-- state flip every cycle and a change-feed entry for each one, and never removes it from anything.
-- The I3 seeding put nine of these in the catalogue: `Your MUD Name`, `test`, `lpcdb`,
-- `Unnamed_CM_225ea2cf`, four `*-Dev` instances, and a stock `DeadSouls-FluffOS2019`. They answer
-- perfectly well. What they are not is games somebody could go and play.
--
-- **This is our judgement and it is stored as ours.** Rule 5 forbids recording a decision of ours as
-- a measurement of theirs, and "not a game for players" is not something a socket told us — it is an
-- editor looking at `Your MUD Name` and deciding. So it gets its own state rather than borrowing
-- archived's, it carries a reason in the row, and the page says so. A reader who disagrees can see
-- exactly what was decided and by what argument.
--
-- **Immune to the crawl by construction, not by a new check.** `ArchiveSweeper.RestoreAsync` already
-- returns false for anything whose state is not `archived`, so an excluded game is untouched by the
-- path that would otherwise undo this. The sweeper's archiving arm gets an explicit guard, because
-- that one would happily move an excluded game to `archived` the moment it went dark and quietly
-- discard the judgement.
--
-- **Nothing is deleted and nothing stops.** §7.5's guarantees are unchanged: the game keeps its page,
-- its URL, its history and its change feed, and it keeps being probed for ever. What it loses is the
-- default listing, the rankings and the "active today" figure — exactly what archiving withholds,
-- for a different and stated reason.
--
-- The reason is NOT NULL when the state is set and NULL otherwise, enforced here rather than in a
-- writer: an exclusion whose argument nobody wrote down is the thing this table exists to prevent.
--
-- No BEGIN/COMMIT here: MigrationRunner opens a transaction around each script and writes the ledger
-- entry inside it, so a script opening its own commits half the work and then fails the ledger
-- insert with "transaction is already completed".

ALTER TABLE game
    DROP CONSTRAINT game_state_vocabulary,
    ADD CONSTRAINT game_state_vocabulary CHECK (state IN ('active', 'quiet', 'dark', 'archived', 'excluded')),

    ADD COLUMN excluded_at timestamptz,
    ADD COLUMN excluded_reason text,

    -- Both together or neither, and only in the state they describe.
    ADD CONSTRAINT game_exclusion_is_explained CHECK (
        (state = 'excluded') = (excluded_at IS NOT NULL)
        AND (excluded_at IS NULL) = (excluded_reason IS NULL)),

    -- An empty reason is not a reason.
    ADD CONSTRAINT game_exclusion_reason_is_not_blank CHECK (
        excluded_reason IS NULL OR length(btrim(excluded_reason)) > 0);
