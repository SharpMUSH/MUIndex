-- spec §5.1 — one row per (game, field, source), and no append-only ledger. Every probe does exactly
-- one of two things to each field: confirm (bump last_confirmed_at and write nothing else) or change
-- (rewrite this row AND append one row to field_change). A game whose GENRE never moves therefore
-- costs one row per source for ever, not one per hour.
--
-- Keyed by SOURCE as well as by field, which is the whole reason the capability matrix can exist: one
-- row per (game, field) cannot hold both "the handshake offered GMCP" and "MSSP claims GMCP", and
-- §9 requires both, each with its own age, at the same time. The winner is derived on read by
-- FieldPrecedence and is never stored, so it cannot go stale against the rows it summarises.
--
-- There is deliberately no `confidence` column (§5.1). Provenance and age carry the meaning between
-- them, and an unspecified numeric confidence would be a field nothing sets consistently and nothing
-- renders.
CREATE TABLE game_field (
    game_id           uuid NOT NULL REFERENCES game (id),
    field             text NOT NULL,
    source            text NOT NULL,
    value             text NOT NULL,
    first_seen_at     timestamptz NOT NULL,
    last_confirmed_at timestamptz NOT NULL,

    PRIMARY KEY (game_id, field, source),

    -- The §5.1 precedence ladder's vocabulary. The declared order of MUI.Catalog.FieldSource is the
    -- ladder itself; this list is only the spelling.
    CONSTRAINT game_field_source_vocabulary CHECK (source IN (
        'staff', 'handshake', 'owner', 'who', 'mssp', 'banner')),

    -- A value cannot have been confirmed before it was first seen.
    CONSTRAINT game_field_confirmed_after_first_seen CHECK (last_confirmed_at >= first_seen_at)
);

-- The game page renders every field of one game at once (§9), which the primary key's leading column
-- already serves. This index serves the other direction: §9's faceted search asks which games have
-- CODEBASE = PennMUSH, or capability.gmcp.measured = true.
CREATE INDEX game_field_field_value_idx ON game_field (field, value);

-- spec §5.1 — the per-game change feed, which is a table of events that actually happened, and which
-- is also what one wants to render. A first sighting is deliberately not one of them: that is the
-- "newly discovered" feed's business (§9), and old_value is NULL only where an importer or a staff
-- correction genuinely had nothing to replace.
CREATE TABLE field_change (
    id        bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    game_id   uuid NOT NULL REFERENCES game (id),
    field     text NOT NULL,
    source    text NOT NULL,
    old_value text,
    new_value text NOT NULL,
    at        timestamptz NOT NULL,

    CONSTRAINT field_change_source_vocabulary CHECK (source IN (
        'staff', 'handshake', 'owner', 'who', 'mssp', 'banner'))
);

-- §9's change feed is "the most recent N changes for this game", newest first, which is exactly this.
CREATE INDEX field_change_game_at_idx ON field_change (game_id, at DESC);
