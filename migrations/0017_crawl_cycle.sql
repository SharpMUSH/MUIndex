-- crawl_cycle: per-cycle record of what the crawl loop did, so this can be observed on the site
-- rather than only in logs (container log retention is short, on the order of ~30 minutes).
--
-- A fact about our crawler, not about any game (same category as game_icon — §7.5's "nothing is
-- ever deleted" guards measurements of someone else's game, and no row here is one). A reader
-- may learn only that we were or weren't looking, never that a game was down.
--
-- Retention is a 30-day TTL, swept by the same maintenance pass as §11's probe shapes — cheap
-- for Postgres, but no reason to keep indefinitely for a front-page strip.
CREATE TABLE crawl_cycle (
    id            bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,

    started_at    timestamptz NOT NULL,
    finished_at   timestamptz NOT NULL,

    -- CycleReport, one column per counter. Flattened rather than JSON because the strip reads
    -- three of them by name.
    considered    integer NOT NULL,
    probed        integer NOT NULL,
    answered      integer NOT NULL,
    failed        integer NOT NULL,
    refused       integer NOT NULL,
    opted_out     integer NOT NULL,
    errored       integer NOT NULL,
    listed        integer NOT NULL,
    reviews       integer NOT NULL,
    counted       integer NOT NULL,
    unmeasurable  integer NOT NULL,
    transitions   integer NOT NULL,
    referrals     integer NOT NULL,

    CONSTRAINT crawl_cycle_ends_after_it_starts CHECK (finished_at >= started_at),
    CONSTRAINT crawl_cycle_counts_are_tallies CHECK (
        considered >= 0 AND probed >= 0 AND answered >= 0 AND failed >= 0 AND refused >= 0
        AND opted_out >= 0 AND errored >= 0 AND listed >= 0 AND reviews >= 0 AND counted >= 0
        AND unmeasurable >= 0 AND transitions >= 0 AND referrals >= 0)
);

-- Serves both the strip (newest row) and the sweep (everything before a cutoff).
CREATE INDEX crawl_cycle_finished_idx ON crawl_cycle (finished_at DESC);
