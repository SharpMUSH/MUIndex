-- spec §7.1, §7.2, §7.3 — the registry the crawl loop reads, the referral graph it writes, and the
-- suspected-duplicate pairs it opens. Everything before this migration is what a probe *produced*;
-- this is what decides who gets probed and what a probe is then attributed to.

-- §7.1 — one address the crawler probes, on its own schedule, for ever.
--
-- THERE IS NO RETIREMENT COLUMN, AND ADDING ONE WOULD BE THE WORST REGRESSION THIS SCHEMA COULD TAKE.
-- Not a `retired` boolean, not a `retired_at`, not an `enabled`, and not a next_probe_at of
-- 'infinity' meaning never — the last is the folklore version of the same bug and is just as final.
-- §7.4 says a game dark for two years is still probed weekly, forever, including after archiving,
-- because a returning game re-listing itself with no human involved is the thing every incumbent
-- failed at (§3). MUI.Discovery.CrawlTarget holds the same absence in place by reflection.
CREATE TABLE crawl_target (
    id                      uuid PRIMARY KEY,

    -- NULL until the host answered for itself (§7.2). A referral is a hostname somebody claimed is a
    -- game; it becomes one by answering, not by being named.
    game_id                 uuid REFERENCES game (id),

    host                    text NOT NULL,
    port                    integer NOT NULL,
    use_tls                 boolean NOT NULL DEFAULT false,

    next_probe_at           timestamptz NOT NULL,
    consecutive_failures    integer NOT NULL DEFAULT 0,

    -- The server's own request, honoured as a floor under the interval (§7.7, §11). NULL is "the
    -- server has no preference", which is a different fact from a stated zero.
    crawl_delay             interval,

    first_seen_at           timestamptz NOT NULL,
    last_probed_at          timestamptz,

    -- §7.2 — provenance, so a hostile or careless REFERRAL list can be traced and its whole subtree
    -- pruned. Not a dependency: a target whose referring game disappears is still due on schedule.
    discovered_from_game_id uuid REFERENCES game (id),
    depth                   integer NOT NULL DEFAULT 0,

    -- §7.2 — "Operator-supplied seeds may be exempted, and nothing else may." DEFAULT false is the
    -- security-relevant half: every referral and every import is written by code that never mentions
    -- this column, and what they get by not thinking about it must be the guarded behaviour.
    is_operator_seed        boolean NOT NULL DEFAULT false,

    CONSTRAINT crawl_target_port_is_a_port CHECK (port BETWEEN 1 AND 65535),
    CONSTRAINT crawl_target_depth_is_not_negative CHECK (depth >= 0),
    CONSTRAINT crawl_target_failures_are_not_negative CHECK (consecutive_failures >= 0),
    CONSTRAINT crawl_target_crawl_delay_is_not_negative CHECK (crawl_delay IS NULL OR crawl_delay >= interval '0'),

    -- The same teeth game_endpoint has, for the same reason: the unique index below is only a
    -- guarantee that one machine is one target if one machine has one spelling. Two targets for one
    -- host is double the traffic at somebody else's server.
    CONSTRAINT crawl_target_host_is_canonical CHECK (
        host = lower(host) AND host = btrim(host) AND host NOT LIKE '%.')
);

-- One row per address. ICrawlTargetRepository.AddAsync leans on this: adding a known address returns
-- the existing id and never resets its schedule.
CREATE UNIQUE INDEX crawl_target_address_idx ON crawl_target (host, port);

-- The scheduler's one hot query: which targets are due, oldest first.
CREATE INDEX crawl_target_due_idx ON crawl_target (next_probe_at);

-- §7.2's subtree prune walks this direction.
CREATE INDEX crawl_target_discovered_from_idx ON crawl_target (discovered_from_game_id)
    WHERE discovered_from_game_id IS NOT NULL;

-- §7.1 — one game naming another in its MSSP REFERRAL field. PROVENANCE ONLY: nothing here schedules
-- anything, and an edge disappearing sets `present` to false and changes nothing else. The referred
-- game carries on being probed on its own account, for ever.
CREATE TABLE referral_edge (
    from_game_id  uuid NOT NULL REFERENCES game (id),
    to_host       text NOT NULL,
    to_port       integer NOT NULL,
    first_seen_at timestamptz NOT NULL,
    last_seen_at  timestamptz NOT NULL,

    -- False means "not in the referrer's list any more", never "deleted". Nothing here is ever
    -- deleted: the edge is what lets a poisoned source be traced after the fact.
    present       boolean NOT NULL DEFAULT true,

    PRIMARY KEY (from_game_id, to_host, to_port),

    CONSTRAINT referral_edge_port_is_a_port CHECK (to_port BETWEEN 1 AND 65535),
    CONSTRAINT referral_edge_seen_after_first_seen CHECK (last_seen_at >= first_seen_at),
    CONSTRAINT referral_edge_host_is_canonical CHECK (
        to_host = lower(to_host) AND to_host = btrim(to_host) AND to_host NOT LIKE '%.')
);

-- "Who points at this address?" — the reverse arrow §7.2's prune and §9's referral neighbours read.
CREATE INDEX referral_edge_to_idx ON referral_edge (to_host, to_port);

-- §7.3's middle band — two games that might be one, held open for a person to judge.
--
-- This table changes no presentational state at all. BOTH PAGES STAY LIVE AND LINK TO EACH OTHER
-- RECIPROCALLY: a wrongly hidden game is worse than a visible duplicate, and hiding one side would
-- make this an unreviewed merge wearing a review's name.
CREATE TABLE duplicate_review (
    id             uuid PRIMARY KEY,
    left_game_id   uuid NOT NULL REFERENCES game (id),
    right_game_id  uuid NOT NULL REFERENCES game (id),
    score          double precision NOT NULL,

    -- Every signal that was weighed, including the ones that did not fire. A review is a judgement a
    -- person makes, and "which were considered and how did each land" is the whole content of it.
    signals        jsonb NOT NULL,

    opened_at      timestamptz NOT NULL,

    -- Closing keeps the row. A judgement is part of the record.
    resolved_at    timestamptz,
    resolution     text,

    -- The pair is unordered, so the storage orders it. That is what makes "have we already opened
    -- this?" one lookup rather than two that can race each other into two rows for one pair.
    CONSTRAINT duplicate_review_pair_is_ordered CHECK (left_game_id < right_game_id),
    CONSTRAINT duplicate_review_resolution_accompanies_resolved_at CHECK (
        (resolved_at IS NULL) = (resolution IS NULL))
);

-- One open pair per couple of games. A probe that keeps scoring middling must not accumulate a row
-- per probe.
CREATE UNIQUE INDEX duplicate_review_open_pair_idx
    ON duplicate_review (left_game_id, right_game_id) WHERE resolved_at IS NULL;

CREATE INDEX duplicate_review_right_idx ON duplicate_review (right_game_id);

-- §7.3's IGameFieldIndex asks the reverse question of game_field — "which games carry this value for
-- this field" — and its contract is case-insensitive and trimmed on both sides. game_field_field_value_idx
-- indexes the raw columns, which that predicate cannot use, so the matcher would sequentially scan
-- game_field once per signal per probe.
CREATE INDEX game_field_folded_value_idx ON game_field (lower(btrim(field)), lower(btrim(value)));
