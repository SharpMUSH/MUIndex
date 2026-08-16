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
