-- availability_interval: intervals, not samples (§5.3) — a probe extends the open interval or
-- closes it and opens a new one. Only a change of state or cause writes a transition; repeated
-- identical failures extend the same interval.
CREATE TABLE availability_interval (
    id      bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    game_id uuid NOT NULL REFERENCES game (id),
    state   text NOT NULL,
    from_at timestamptz NOT NULL,
    to_at   timestamptz,
    cause   text NOT NULL,

    -- Single value today: the backfill (§7.6) contributes no history, so every interval here is
    -- ours. Column kept in case another party's measurements are ever ingested — an
    -- undifferentiated total could not be split back apart afterward.
    origin  text NOT NULL DEFAULT 'first_party',

    -- §5.8 vocabulary. `degraded` = socket answered but negotiation didn't complete within the
    -- probe timeout; distinct from `unreachable`.
    CONSTRAINT availability_interval_state_vocabulary CHECK (state IN (
        'reachable', 'degraded', 'unreachable')),

    -- 'none' is only valid for a reachable interval.
    CONSTRAINT availability_interval_cause_vocabulary CHECK (cause IN (
        'none', 'dns', 'refused', 'tls', 'timeout', 'handshake_stalled')),

    CONSTRAINT availability_interval_origin_vocabulary CHECK (origin IN (
        'first_party')),

    CONSTRAINT availability_interval_does_not_end_before_it_starts CHECK (
        to_at IS NULL OR to_at >= from_at)
);

-- Hot-path lookup of a game's open interval. Partial UNIQUE also enforces the core invariant:
-- at most one open interval per game.
CREATE UNIQUE INDEX availability_interval_open_idx ON availability_interval (game_id) WHERE to_at IS NULL;

-- Serves the availability arithmetic (cumulative reachable time, longest outage, etc.).
CREATE INDEX availability_interval_game_from_idx ON availability_interval (game_id, from_at);
