-- A game that asked to be left alone, and then asked to be taken out of the listing too (spec §11).
--
-- **Why this is not the opt-out, which is the distinction the whole feature turns on.** §11's
-- opt-out is a decision about *dialling*: we stop connecting, and the page keeps everything measured
-- before the ask. That is what the about page and the README have always promised, and it stays
-- true for everyone who only wanted the crawler to stop. Some operators want more than that, and
-- until now the answer was that there was nothing more to give them.
--
-- **Why it is not `excluded` (migration 0024), which is the nearer neighbour.** Exclusion says "this
-- answers like a game and is not one" — our judgement about what a thing *is*, stored as ours,
-- carrying an argument a reader can disagree with. A game that asks to be unlisted is a game. Filing
-- it under `excluded` would put a false statement about it in the one column readers filter on, and
-- the word is visible: it reaches the facets, the plain-text surface and the read API.
--
-- **There is no reason column here, and its absence is deliberate.** `excluded_reason` exists
-- because an exclusion is our claim and an unarguable claim is the thing 0024 was written to
-- prevent. This is not our claim. The reason is that they asked, `unlisted_by` records the account
-- that pressed the button, and `crawl_opt_out` already holds who asked and how — writing our own
-- prose about somebody's request would be inventing a third version of it.
--
-- **`unlisted_by` is not audit decoration.** It is the whole authorisation story in one column: the
-- account that held a verified claim on this game at the moment the button was pressed. This
-- repository has already shipped one claim about a third party's wishes that was compiled in by
-- whoever typed it (`ContactedMaintainer`, defaulted to true). A NOT NULL reference to the account
-- that asked is what that defect did not have.
--
-- **Reversible by a probe, unlike an exclusion.** `ArchiveSweeper.RestoreAsync` relists this state
-- on the first answered probe, and that is safe by construction rather than by a check: an
-- opted-out address is refused before the dial, so a probe that answers is proof that no opt-out
-- stands any more. It means the documented exit an operator can work alone — delete the TXT record,
-- be dialled again within a week on §7.4's permanent floor — brings the listing back with it. An
-- unlisting with no exit but ours would be the trap the about page warns about.
--
-- **Nothing is deleted and nothing stops.** §7.5's guarantees are unchanged: the page, the URL, the
-- history and the change feed all survive, and the address keeps its schedule for ever. What it
-- loses is the default listing, the rankings and the "active today" figure.
--
-- No BEGIN/COMMIT here: MigrationRunner opens a transaction around each script and writes the ledger
-- entry inside it, so a script opening its own commits half the work and then fails the ledger
-- insert with "transaction is already completed".

ALTER TABLE game
    DROP CONSTRAINT game_state_vocabulary,
    ADD CONSTRAINT game_state_vocabulary CHECK (
        state IN ('active', 'quiet', 'dark', 'archived', 'excluded', 'unlisted')),

    ADD COLUMN unlisted_at timestamptz,

    -- No ON DELETE clause, so the default refuses the delete rather than blanking the column: an
    -- account cannot be removed out from under a standing unlisting and leave the row saying nobody
    -- asked for it. That is the shape game_claim already uses for the same reference.
    ADD COLUMN unlisted_by uuid REFERENCES app_user (id),

    -- Both together or neither, and only in the state they describe — 0024's shape, for 0024's
    -- reason. An unlisting with no date, or a date with no state, is a row nobody can read back.
    ADD CONSTRAINT game_unlisting_is_attributed CHECK (
        (state = 'unlisted') = (unlisted_at IS NOT NULL)
        AND (unlisted_at IS NULL) = (unlisted_by IS NULL));
