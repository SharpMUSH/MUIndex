-- A game that asked to be left alone, and then asked to be taken off the listing too (§11).
--
-- Distinct from the opt-out: opt-out (§11) stops dialling and leaves everything already
-- measured on the page. This additionally removes the listing for operators who want more than
-- that.
--
-- Distinct from `excluded` (0024): exclusion is our judgement that something isn't a game.
-- Unlisting is a game that asked not to be shown — filing it under `excluded` would put a false
-- claim in the column readers filter on.
--
-- No reason column, deliberately: `excluded_reason` exists because exclusion is our unarguable
-- claim about what a thing is; this isn't our claim. `unlisted_by` plus crawl_opt_out.detail
-- already record who asked and how, without us inventing our own prose about it.
--
-- unlisted_by is the authorization story, not audit decoration: through OwnerListing (the only
-- writer in the shipped tree) it's by construction the account holding a verified claim at the
-- time the button was pressed. A hand-written row records the operator's own account. NOT NULL
-- so nobody's wishes can be inferred or defaulted (cf. the ContactedMaintainer defect).
--
-- Reversible by a probe, unlike an exclusion: an opted-out address is refused before the dial, so
-- any probe that answers proves no opt-out stands any more, and ArchiveSweeper.RestoreAsync
-- relists on the first one. Deleting the TXT record and waiting out §7.4's probe floor is
-- sufficient to relist.
--
-- §7.5's guarantees are unchanged — page, URL, history, change feed all survive, schedule
-- continues. Only the default listing, rankings, and "active today" are affected.
--
-- No BEGIN/COMMIT: MigrationRunner opens its own transaction per script and writes the ledger
-- entry inside it.

ALTER TABLE game
    DROP CONSTRAINT game_state_vocabulary,
    ADD CONSTRAINT game_state_vocabulary CHECK (
        state IN ('active', 'quiet', 'dark', 'archived', 'excluded', 'unlisted')),

    ADD COLUMN unlisted_at timestamptz,

    -- No ON DELETE clause: refuses the delete rather than blanking the column, so an account
    -- can't be removed out from under a standing unlisting (same shape as game_claim's reference).
    ADD COLUMN unlisted_by uuid REFERENCES app_user (id),

    ADD CONSTRAINT game_unlisting_is_attributed CHECK (
        (state = 'unlisted') = (unlisted_at IS NOT NULL)
        AND (unlisted_at IS NULL) = (unlisted_by IS NULL));
