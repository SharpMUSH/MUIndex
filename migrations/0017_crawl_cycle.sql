-- What the crawl loop did, per cycle, so the instrument this site's whole claim rests on can be
-- seen running rather than assumed to be.
--
-- CrawlerService has always built this record and always thrown it at the log. That was enough
-- until the log was the only place it lived: an unrelated background service crash-looped every
-- ninety seconds for four hours, and nothing on the site said so — not the home page, not the API,
-- not the game pages, which went on rendering measurements that had stopped being taken. It was
-- found by opening an SSH session, and by then the container's own log had rotated past most of it.
--
-- THIS IS A FACT ABOUT OUR CRAWLER, NOT ABOUT ANY GAME, which is what makes it storable and what
-- bounds what it may say. It is the same category as game_icon: §7.5's "nothing is ever deleted"
-- guards measurements of somebody else's game, and no row here is one. A reader may not learn from
-- this table that a *game* was down; they may only learn that we were or were not looking.
--
-- Retention is therefore a TTL and not a debate — thirty days, swept by the same maintenance pass
-- that sweeps §11's probe shapes. Two cycles a minute is about a million rows a year, which is
-- nothing to Postgres and still not a thing to accumulate for ever on one small VM for a strip on
-- the front page. What outlives the window is the measurements the cycles took, which is the point.
CREATE TABLE crawl_cycle (
    id            bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,

    started_at    timestamptz NOT NULL,
    finished_at   timestamptz NOT NULL,

    -- CycleReport, one column per counter. Flattened rather than stored as JSON because the strip
    -- reads three of them by name and a JSON blob would make the schema the place nobody looks.
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

    -- A cycle cannot finish before it started, and every counter is a tally of things that happened.
    -- Cheap, and the kind of constraint that catches a mapping written from the wrong record.
    CONSTRAINT crawl_cycle_ends_after_it_starts CHECK (finished_at >= started_at),
    CONSTRAINT crawl_cycle_counts_are_tallies CHECK (
        considered >= 0 AND probed >= 0 AND answered >= 0 AND failed >= 0 AND refused >= 0
        AND opted_out >= 0 AND errored >= 0 AND listed >= 0 AND reviews >= 0 AND counted >= 0
        AND unmeasurable >= 0 AND transitions >= 0 AND referrals >= 0)
);

-- Both readers ask the same question from opposite ends: the strip wants the newest row, the sweep
-- wants everything before a cutoff. One index on the clock answers both.
CREATE INDEX crawl_cycle_finished_idx ON crawl_cycle (finished_at DESC);
