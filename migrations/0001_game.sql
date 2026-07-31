-- spec §5, §5.7 — the game itself. Everything else in this schema hangs off it.
--
-- Two identifiers, deliberately (§5.7): `id` is an immutable GUID that every foreign key points at
-- and that survives a merge, and `slug` is the mutable URL segment. Nothing is ever deleted here,
-- so a slug that once worked keeps working — which is the same promise §7.5 makes about pages.
CREATE TABLE game (
    id                uuid PRIMARY KEY,
    slug              text NOT NULL,
    name              text NOT NULL,
    tagline           text,
    state             text NOT NULL,
    is_claimed        boolean NOT NULL DEFAULT false,
    first_seen_at     timestamptz NOT NULL,

    -- §7.5's grace is measured from here. NULL means we have never once reached it, which is a
    -- different fact from "reachable a long time ago" and is not the same as first_seen_at.
    last_reachable_at timestamptz,
    archived_at       timestamptz,

    -- §7.4's lifecycle states, derived from availability history and never set by hand. The
    -- vocabulary lives here as well as in LifecycleState so a bad write fails at the database rather
    -- than becoming a value every reader downstream has to cope with.
    CONSTRAINT game_state_vocabulary CHECK (state IN ('active', 'quiet', 'dark', 'archived')),

    -- §7.5: archiving is a presentation change, so an archived game carries a date and nothing else
    -- about it is erased. A game that is not archived has no archive date.
    CONSTRAINT game_archived_games_have_a_date CHECK ((state = 'archived') = (archived_at IS NOT NULL))
);

-- §7.5: an archived game keeps its page, its history and its URL — and the URL is the slug, so it is
-- unique for ever. It is also the lookup the game page performs on every request.
CREATE UNIQUE INDEX game_slug_key ON game (slug);

-- §9's default listing excludes archived games, which is most reads of this table. A partial index
-- keeps that query off the archive entirely rather than filtering it out afterwards.
CREATE INDEX game_state_idx ON game (state) WHERE state <> 'archived';

-- §7.5's sweep asks "which games have been dark longer than the grace they earned", which is an
-- ordered scan over how long ago each was last reachable.
CREATE INDEX game_last_reachable_at_idx ON game (last_reachable_at);

-- §9's "newly discovered" feed is this column, newest first.
CREATE INDEX game_first_seen_at_idx ON game (first_seen_at DESC);
