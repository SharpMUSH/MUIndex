-- spec §7.2, §7.6, §8, §9 — public submission of an address; that's all it takes in (§7.6:
-- no name, description, codebase, or player count — everything is measured by the crawler).

-- A timestamp, not a lifecycle state: `game.state` is derived from availability and never set
-- by hand (0001). Submission is a different axis — how a game reached us, not how it has
-- behaved.
--
-- Listing rule (read off this column alone): a game is public if nobody submitted it, or if it
-- has been claimed. Crawler-discovered games list immediately as before; a stranger's assertion
-- that some address is a game waits for the operator to prove it by claiming.
ALTER TABLE game
    ADD COLUMN submitted_at timestamptz;

-- Filtered on every listing read; most rows are NULL, so index only the rest.
CREATE INDEX game_submitted_at_idx ON game (submitted_at) WHERE submitted_at IS NOT NULL;

-- Same fact, one step earlier: submission creates no game (§7.1's promotion is unchanged), so
-- the marker lives on the crawl target until CatalogueBinder mints a game and copies it over.
-- Never set on a target that already exists — AddAsync collapses onto the existing row, so
-- re-submitting a listed game changes nothing.
ALTER TABLE crawl_target
    ADD COLUMN submitted_at timestamptz;

-- Every submission this deployment received, and its outcome — backs the rate limit and gives
-- it something to audit.
CREATE TABLE game_submission (
    id             uuid PRIMARY KEY,

    -- NULL when nothing usable was submitted; still has to count against the rate limit.
    host           text,
    port           integer,

    submitted_at   timestamptz NOT NULL,

    -- 'pending' reserves the rate-limit slot before the outcome is known — under an advisory
    -- lock on the source — so a burst of concurrent requests can't all pass a stale count
    -- (check-then-act). A row left pending by a dead process still counts against the bound.
    outcome        text NOT NULL,

    -- The registry row this became, when accepted. NULL for every other outcome.
    crawl_target_id uuid REFERENCES crawl_target (id),

    -- §11 — never the submitter's address: a salted digest, using the shared rotating salt
    -- below rather than a per-process one.
    source         text NOT NULL,

    CONSTRAINT game_submission_address_is_whole CHECK ((host IS NULL) = (port IS NULL)),
    CONSTRAINT game_submission_port_is_a_port CHECK (port IS NULL OR port BETWEEN 1 AND 65535),
    CONSTRAINT game_submission_host_is_bounded CHECK (host IS NULL OR length(host) <= 253),
    CONSTRAINT game_submission_source_is_a_digest CHECK (source ~ '^[0-9a-f]{64}$'),
    CONSTRAINT game_submission_outcome_vocabulary CHECK (outcome IN (
        'pending',
        'accepted', 'already_listed', 'already_queued',
        'malformed', 'refused_not_routable', 'unresolvable', 'refused_opt_out')),

    CONSTRAINT game_submission_accepted_names_its_target CHECK (
        (outcome = 'accepted') = (crawl_target_id IS NOT NULL))
);

-- The rate limit's hot query.
CREATE INDEX game_submission_source_idx ON game_submission (source, submitted_at DESC);

-- §11's rotating salt, shared across replicas rather than generated per-process — a per-process
-- salt would let each web replica compute a different digest for one address, silently
-- multiplying the rate limit by replica count and resetting it on every restart. Rows written
-- under a retired salt (epoch) can never be compared with rows under a newer one, by anyone.
CREATE TABLE submission_salt (
    -- Weeks since Unix epoch, so every replica derives the same current epoch from the clock.
    epoch      bigint PRIMARY KEY,
    salt       bytea NOT NULL,
    created_at timestamptz NOT NULL,

    CONSTRAINT submission_salt_is_long_enough CHECK (length(salt) >= 32)
);

-- "Who has been submitting this address, and how often?"
CREATE INDEX game_submission_address_idx ON game_submission (host, port, submitted_at DESC)
    WHERE host IS NOT NULL;
