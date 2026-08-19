-- game: the core entity. `id` is an immutable GUID for foreign keys and survives merges;
-- `slug` is the mutable URL segment (§5.7). Nothing is ever deleted, so an old slug keeps
-- resolving via game_slug_history.
CREATE TABLE game (
    id                uuid PRIMARY KEY,
    slug              text NOT NULL,
    name              text NOT NULL,
    tagline           text,
    state             text NOT NULL,
    is_claimed        boolean NOT NULL DEFAULT false,
    first_seen_at     timestamptz NOT NULL,

    -- §7.5 grace is measured from here. NULL means never reachable, distinct from "reachable
    -- long ago".
    last_reachable_at timestamptz,
    archived_at       timestamptz,

    -- §7.4 lifecycle states, derived from availability history and never set by hand.
    CONSTRAINT game_state_vocabulary CHECK (state IN ('active', 'quiet', 'dark', 'archived')),

    -- Archiving is presentation-only (§7.5): an archived game carries a date and nothing else changes.
    CONSTRAINT game_archived_games_have_a_date CHECK ((state = 'archived') = (archived_at IS NOT NULL))
);

-- The game page lookup on every request; also the permanent URL (§7.5).
CREATE UNIQUE INDEX game_slug_key ON game (slug);

-- Partial: keeps §9's default listing (excludes archived) off the archive entirely.
CREATE INDEX game_state_idx ON game (state) WHERE state <> 'archived';

-- §7.5's sweep scans "dark longer than the grace period", ordered by last_reachable_at.
CREATE INDEX game_last_reachable_at_idx ON game (last_reachable_at);

-- §9's "newly discovered" feed, newest first.
CREATE INDEX game_first_seen_at_idx ON game (first_seen_at DESC);
