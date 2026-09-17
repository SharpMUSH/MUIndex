-- Lets staff honour "please unlist us" from somebody who has no account (issue #187).
--
-- Migration 0025 required every unlisting to name an app_user, which reads as the right rule and is
-- only the right rule for the dashboard. Measured on production: Convergence MUSH's admin asked to
-- be unlisted in a chat message on 2026-08-16. The opt-out was recorded, honoured at the dial, and
-- the game stayed in the listing for a month — because the person who asked held no verified claim,
-- so `unlisted_by` could not be filled and the state could not be reached at all. The request was
-- granted and its point was not.
--
-- Attribution becomes A PERSON OR AN EXPLANATION, never neither. That is the shape this codebase
-- already uses everywhere the actor is staff rather than an account: excluded_reason,
-- crawl_opt_out.detail, and --because on merge and distinct. An unlisting nobody is accountable for
-- is one nobody can review, which is why the check stays in the schema rather than moving into the
-- tool where raw SQL could walk around it.
--
-- Nothing is backfilled: every existing unlisting came through the dashboard and already names the
-- account that asked, which is a better record than a reason anybody could write now.
--
-- No BEGIN/COMMIT: MigrationRunner opens its own transaction per script and writes the ledger entry
-- inside it.

ALTER TABLE game
    ADD COLUMN unlisted_reason text,

    DROP CONSTRAINT game_unlisting_is_attributed,

    ADD CONSTRAINT game_unlisting_is_attributed CHECK (
        (state = 'unlisted') = (unlisted_at IS NOT NULL)
        AND (unlisted_at IS NULL) = (unlisted_by IS NULL AND unlisted_reason IS NULL)
        AND NOT (unlisted_by IS NOT NULL AND unlisted_reason IS NOT NULL)),

    ADD CONSTRAINT game_unlisting_reason_is_not_blank CHECK (
        unlisted_reason IS NULL OR length(btrim(unlisted_reason)) > 0);

COMMENT ON COLUMN game.unlisted_reason IS
    'Why staff unlisted this game, when the ask came from somebody with no account. Exactly one of '
    'this and unlisted_by is set: an unlisting is attributable to a person or explained in words, '
    'never both and never neither.';
