-- spec §5.3 — intervals, not samples. A game reachable for three years is one open row, not
-- twenty-six thousand samples, and "reachable over 90 days" and "longest outage" become arithmetic
-- over a handful of rows. Each probe either extends the open interval or closes it and opens a new
-- one, and ONLY A CHANGE OF STATE OR CAUSE WRITES A TRANSITION: a hundred consecutive timeouts are
-- one interval, not a hundred.
CREATE TABLE availability_interval (
    id      bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    game_id uuid NOT NULL REFERENCES game (id),
    state   text NOT NULL,
    from_at timestamptz NOT NULL,
    to_at   timestamptz,
    cause   text NOT NULL,

    -- §7.6 — imported history counts toward archive grace at HALF weight, so it has to be summable
    -- apart from our own. Not a provenance nicety: ArchivePolicy.GraceFor takes the two as separate
    -- arguments and weights them differently, so one undifferentiated total cannot feed it.
    origin  text NOT NULL DEFAULT 'first_party',

    -- §5.8's vocabulary, in the schema so the word cannot leak. Reachable, never up. `degraded` is
    -- "we got in and could not finish" — the socket answered and the session did not complete
    -- negotiation within the probe timeout — which is neither of its neighbours.
    CONSTRAINT availability_interval_state_vocabulary CHECK (state IN (
        'reachable', 'degraded', 'unreachable')),

    -- 'none' is the cause a reachable interval carries; it is never a probe's answer.
    CONSTRAINT availability_interval_cause_vocabulary CHECK (cause IN (
        'none', 'dns', 'refused', 'tls', 'timeout', 'handshake_stalled')),

    CONSTRAINT availability_interval_origin_vocabulary CHECK (origin IN (
        'first_party', 'imported_measured')),

    CONSTRAINT availability_interval_does_not_end_before_it_starts CHECK (
        to_at IS NULL OR to_at >= from_at)
);

-- Every probe asks "what is this game's open interval", which is the one query on the hot path. As a
-- partial UNIQUE index it also enforces the invariant the whole design rests on: at most one interval
-- per game is open, so no caller can leave two running by forgetting to close the first.
CREATE UNIQUE INDEX availability_interval_open_idx ON availability_interval (game_id) WHERE to_at IS NULL;

-- The availability arithmetic — cumulative reachable time, reachable fraction over a window, longest
-- outage — reads one game's intervals in time order.
CREATE INDEX availability_interval_game_from_idx ON availability_interval (game_id, from_at);
