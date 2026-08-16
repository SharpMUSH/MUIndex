-- Which Intermud-3 mud is which game (spec §7.2).
--
-- I3 identifies a participant by a canonical name and an IP literal; MUIndex identifies a game by the
-- hostname a player would type. The join has to be recorded rather than recomputed, and the reason is
-- DNS: an address resolves differently next week, and a binding re-derived every cycle would let a
-- game's I3 identity change underneath it with nothing written down about when or why. A row here is
-- a fact with a date on it.
--
-- How a binding is arrived at, and why there is no address matching in this table. The mudlist gives
-- us an address and a port, and those go into `crawl_target` like any other address we find (§7.6:
-- host and port and nothing else). The ordinary probe then dials it, and the ordinary identity path
-- decides whether it is a game we already have — which is machinery that already exists, is already
-- tested, and already knows how to tell one game on two ports from two games on one host. So binding
-- is a lookup: find the target at that address, and if it has a game, that is the game. Nothing here
-- resolves a name or compares an IP.
--
-- The consequence worth stating: a mud stays unbound until its address has been probed and promoted,
-- which is the same rule every other game obeys (§7.1 — a game exists when a host answers for
-- itself). An I3 mud that never answers a probe is never a game, however loudly the router lists it.
--
-- `mud_name` is the primary key because I3's canonical name is unique on the network and case is
-- significant. It is not a foreign key to anything: the network can list a mud we have never bound,
-- and that row is still worth keeping so that the next cycle need not rediscover it.
--
-- ON DELETE CASCADE is deliberate and is not a hole in §7.5's "nothing is ever deleted". Games are
-- never deleted; the constraint exists so that a merge which does remove a redundant row cannot leave
-- a binding pointing at nothing.
--
-- No BEGIN/COMMIT here: MigrationRunner opens a transaction around each script and writes the ledger
-- entry inside it, so a script opening its own commits half the work and then fails the ledger
-- insert with "transaction is already completed".

CREATE TABLE i3_mud (
    mud_name text PRIMARY KEY,

    -- Null until the address behind this mud has answered a probe and been promoted to a game.
    game_id uuid REFERENCES game (id) ON DELETE CASCADE,

    -- What the router last said. Kept so the next cycle can seed and gate without a second round
    -- trip, and so a mud that vanishes from the network leaves evidence of where it used to be.
    host text NOT NULL,
    port integer NOT NULL,

    -- Whether the mud advertises `who`. I3's services mapping is the network's own opt-in mechanism,
    -- so this is consent rather than capability, and it is stored beside the binding it gates.
    answers_who boolean NOT NULL DEFAULT false,

    first_seen_at timestamptz NOT NULL,
    last_seen_at timestamptz NOT NULL,

    -- When we last sent this mud a who-req, so the pacing floor survives a restart. Null means never.
    last_asked_at timestamptz,

    CONSTRAINT i3_mud_port_is_a_port CHECK (port >= 0 AND port <= 65535)
);

-- Answering "which mud is this game" for a game page, and keeping the binding one-to-one: two muds
-- claiming one game would double-count it, and the count is the whole point.
CREATE UNIQUE INDEX i3_mud_one_per_game_idx ON i3_mud (game_id) WHERE game_id IS NOT NULL;

-- The cycle's own question: who is bound, up, consenting, and due?
CREATE INDEX i3_mud_askable_idx ON i3_mud (last_asked_at) WHERE game_id IS NOT NULL AND answers_who;
