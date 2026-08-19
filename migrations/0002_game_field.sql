-- game_field: one row per (game, field, source) — no append-only ledger. A probe either
-- confirms (bumps last_confirmed_at) or changes a value (rewrites the row and appends to
-- field_change). Keyed by source, not just field, so the capability matrix can hold
-- disagreeing values from different sources at once (§9); the winning value is derived on
-- read by FieldPrecedence, never stored.
--
-- No `confidence` column (§5.1): provenance + age carry the meaning; a numeric confidence
-- would be unset by most writers and unread by most readers.
CREATE TABLE game_field (
    game_id           uuid NOT NULL REFERENCES game (id),
    field             text NOT NULL,
    source            text NOT NULL,
    value             text NOT NULL,
    first_seen_at     timestamptz NOT NULL,
    last_confirmed_at timestamptz NOT NULL,

    PRIMARY KEY (game_id, field, source),

    -- §5.1 precedence ladder vocabulary; order matches MUI.Catalog.FieldSource.
    CONSTRAINT game_field_source_vocabulary CHECK (source IN (
        'staff', 'handshake', 'owner', 'who', 'mssp', 'banner')),

    CONSTRAINT game_field_confirmed_after_first_seen CHECK (last_confirmed_at >= first_seen_at)
);

-- Serves §9's faceted search (e.g. CODEBASE = PennMUSH).
CREATE INDEX game_field_field_value_idx ON game_field (field, value);

-- field_change: the per-game change feed (§9). First sightings are not events here;
-- old_value is NULL only where an importer or staff correction had nothing to replace.
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

-- The most-recent-N-changes query, newest first.
CREATE INDEX field_change_game_at_idx ON field_change (game_id, at DESC);
