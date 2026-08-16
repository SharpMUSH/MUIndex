-- spec §7.3 — "merges are reversible and logged", given somewhere to be logged.
--
-- The interface for this shipped with no implementation: IMergeLog named RecordAsync, RevertAsync and
-- ForGameAsync, MergeApplier took one in its constructor, and nothing built one because there was no
-- table under it. So the catalogue could decide two listings were one game and had no way to act on
-- it, and the four pairs the crawler has actually found sat side by side on the site.
--
-- A MERGE IS A REDIRECT AND NOT A MOVE, which is the whole reason this is one small table rather than
-- a migration of rows between games. Nothing is carried across: the absorbed game keeps its endpoints,
-- its fields, its presence history and its change feed exactly where they are, and this row says only
-- "when you are asked for that game, answer with this one". §7.5 forbids deleting any of it, and a
-- merge that moved rows would be deleting them from where they were measured.
--
-- Reverting is therefore stamping one column. That is what makes "reversible" cost no bookkeeping and
-- makes a half-failed merge impossible: there is no second write to fail. It is also the only shape
-- consistent with the id being immutable across a merge (§5.2) — both games keep their own.

CREATE TABLE merge_log (
    id             uuid PRIMARY KEY,

    -- The surviving game: the one a reader is sent to, and the one the listing shows.
    into_game_id   uuid NOT NULL REFERENCES game (id),

    -- The absorbed game. Its page redirects and the listing skips it; everything else about it stays.
    from_game_id   uuid NOT NULL REFERENCES game (id),

    -- What the identity matcher scored the pair at, and every signal behind it — the same evidence
    -- duplicate_review carries, kept because a merge outlives the review that prompted it and
    -- "reversible" is worth little if nobody can see what the reversal is arguing with. A merge an
    -- operator made by hand records the score it was made on and no signals.
    score          double precision NOT NULL,
    signals        jsonb NOT NULL,

    at             timestamptz NOT NULL,

    -- Set once, when the merge stops being in force. The row is never deleted: a judgement that was
    -- wrong is part of the record, and the next person to look at this pair needs to know somebody
    -- already tried it.
    reverted_at    timestamptz,

    CONSTRAINT merge_log_is_not_a_self_merge CHECK (into_game_id <> from_game_id),
    CONSTRAINT merge_log_reverted_after_merged CHECK (reverted_at IS NULL OR reverted_at >= at)
);

-- A game can be absorbed by at most one game at a time. Two in force at once is not a merge that
-- needs resolving later, it is a page with two answers to "where does this redirect", and the index
-- refuses it at the moment somebody would create it rather than the moment a reader hits it.
--
-- Partial, so the history stays writable: a pair merged, reverted and merged again is three rows and
-- one of them in force.
CREATE UNIQUE INDEX merge_log_absorbed_once_idx ON merge_log (from_game_id) WHERE reverted_at IS NULL;

-- "Which games did this one absorb" — the survivor's side, asked by every public read to find out
-- whether it is a survivor at all.
CREATE INDEX merge_log_into_idx ON merge_log (into_game_id) WHERE reverted_at IS NULL;

-- A CHAIN IS A GAME WITH NO PAGE, AND NEITHER INDEX ABOVE STOPS ONE BEING MADE. A -> B and then
-- B -> C are two rows with distinct from_game_id, so merge_log_absorbed_once_idx accepts both — and A
-- is then dropped from every public read (it is absorbed) and redirected nowhere (the redirect is one
-- hop or none). Its rows are all still in the database and its URL answers 404, which is §7.5 broken
-- by two writes that were individually legal. A cycle, A -> B and B -> A, is insertable for the same
-- reason and would additionally make a chain-following read non-terminating.
--
-- So the shape is refused where it is created rather than papered over where it is read. This is the
-- first trigger in the schema and it is not reached for lightly: the alternative is a check in the
-- repository, which loses the race between two operators and is exactly the read-then-write this
-- table avoids everywhere else. It also has to hold against a row inserted by hand at a psql prompt,
-- which is how an operator merges a pair today.
--
-- DEFERRABLE INITIALLY DEFERRED so a transaction may revert and re-merge in either order and be
-- judged on the state it commits, not on the order somebody wrote the statements.
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
