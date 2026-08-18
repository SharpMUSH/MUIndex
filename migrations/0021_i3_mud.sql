-- i3_mud: which Intermud-3 mud is which game (§7.2). Recorded rather than recomputed each cycle
-- because addresses can resolve differently over time — a re-derived binding would let a
-- game's I3 identity drift with nothing written down.
--
-- Binding is a lookup, not name/IP matching: the mudlist gives an address, which enters
-- crawl_target like any other (§7.6). The ordinary probe dials it and the ordinary identity path
-- decides whether it's a known game; if the target at that address has a game, that's the game.
-- Consequently a mud stays unbound until its address is probed and promoted (§7.1) — an I3 mud
-- that never answers a probe is never a game, however the router lists it.
--
-- `mud_name` is the PK (I3's canonical name is unique and case-sensitive) and not a foreign key
-- — the network can list a mud we've never bound, and that row is still worth keeping.
--
-- ON DELETE CASCADE here is not a §7.5 violation: games are never deleted; this only stops a
-- merge's redundant-row removal from leaving a dangling binding.
--
-- No BEGIN/COMMIT: MigrationRunner opens its own transaction per script and writes the ledger
-- entry inside it.

CREATE TABLE i3_mud (
    mud_name text PRIMARY KEY,

    -- NULL until the address behind this mud has answered a probe and been promoted to a game.
    game_id uuid REFERENCES game (id) ON DELETE CASCADE,

    -- What the router last said, so the next cycle can seed/gate without a round trip.
    host text NOT NULL,
    port integer NOT NULL,

    -- Whether the mud advertises `who` (I3's own opt-in mechanism) — consent, not capability.
    answers_who boolean NOT NULL DEFAULT false,

    first_seen_at timestamptz NOT NULL,
    last_seen_at timestamptz NOT NULL,

    -- When we last sent a who-req, so the pacing floor survives a restart. NULL means never.
    last_asked_at timestamptz,

    CONSTRAINT i3_mud_port_is_a_port CHECK (port >= 0 AND port <= 65535)
);

-- One-to-one binding: two muds claiming one game would double-count it.
CREATE UNIQUE INDEX i3_mud_one_per_game_idx ON i3_mud (game_id) WHERE game_id IS NOT NULL;

-- The cycle's own question: who is bound, up, consenting, and due?
CREATE INDEX i3_mud_askable_idx ON i3_mud (last_asked_at) WHERE game_id IS NOT NULL AND answers_who;
