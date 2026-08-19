-- merge_log: §7.3's "merges are reversible and logged" record. A merge is a redirect, not a
-- move — nothing is copied between games; the absorbed game keeps its endpoints, fields,
-- presence history and change feed exactly where they were (§7.5), and this row says only "when
-- asked for that game, answer with this one". Reverting is stamping one column, so there's no
-- second write to fail.
CREATE TABLE merge_log (
    id             uuid PRIMARY KEY,

    -- The surviving game: what a reader is redirected to.
    into_game_id   uuid NOT NULL REFERENCES game (id),

    -- The absorbed game. Its page redirects and the listing skips it; everything else stays.
    from_game_id   uuid NOT NULL REFERENCES game (id),

    -- The identity matcher's score and every signal behind it — kept because a merge outlives
    -- the review that prompted it. A hand-made merge records the score it was made on and no
    -- signals.
    score          double precision NOT NULL,
    signals        jsonb NOT NULL,

    at             timestamptz NOT NULL,

    -- Set once the merge stops being in force; never deleted — a reversal is still part of the
    -- record.
    reverted_at    timestamptz,

    CONSTRAINT merge_log_is_not_a_self_merge CHECK (into_game_id <> from_game_id),
    CONSTRAINT merge_log_reverted_after_merged CHECK (reverted_at IS NULL OR reverted_at >= at)
);

-- A game can be absorbed by at most one game at a time. Partial, so history stays writable: a
-- pair merged, reverted and re-merged is three rows and one in force.
CREATE UNIQUE INDEX merge_log_absorbed_once_idx ON merge_log (from_game_id) WHERE reverted_at IS NULL;

-- "Which games did this one absorb" — checked by every public read.
CREATE INDEX merge_log_into_idx ON merge_log (into_game_id) WHERE reverted_at IS NULL;

-- A chain (A -> B, B -> C) is a game with no page: A is dropped from public reads (absorbed) but
-- redirects nowhere (only one hop is followed), leaving its URL 404. Neither unique index above
-- stops this on its own, since each insert is individually legal, so it's refused here instead —
-- including against a row inserted by hand at a psql prompt. A cycle (A -> B, B -> A) would
-- additionally make chain-following non-terminating.
--
-- DEFERRABLE INITIALLY DEFERRED so a transaction may revert and re-merge in either statement
-- order and be judged on what it commits.
CREATE FUNCTION merge_log_refuses_chains() RETURNS trigger AS $$
BEGIN
    IF NEW.reverted_at IS NULL AND (
        -- The survivor is itself absorbed: readers sent here would be sent on again.
        EXISTS (SELECT 1 FROM merge_log m
                 WHERE m.reverted_at IS NULL
                   AND m.from_game_id = NEW.into_game_id
                   AND m.id <> NEW.id)
        -- The absorbed game is somebody's survivor: whatever it holds would be orphaned behind it.
        OR EXISTS (SELECT 1 FROM merge_log m
                    WHERE m.reverted_at IS NULL
                      AND m.into_game_id = NEW.from_game_id
                      AND m.id <> NEW.id))
    THEN
        RAISE EXCEPTION 'merge_log: % -> % would form a redirect chain', NEW.from_game_id, NEW.into_game_id
            USING ERRCODE = 'integrity_constraint_violation';
    END IF;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE CONSTRAINT TRIGGER merge_log_no_chains
    AFTER INSERT OR UPDATE ON merge_log
    DEFERRABLE INITIALLY DEFERRED
    FOR EACH ROW EXECUTE FUNCTION merge_log_refuses_chains();
