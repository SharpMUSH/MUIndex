-- crawl_target: the registry the crawl loop reads, the referral graph it writes, and the
-- suspected-duplicate pairs it opens (§7.1-§7.3).

-- §7.1 — one address probed forever on its own schedule.
--
-- Deliberately no retirement column (no `retired`, `enabled`, or a next_probe_at of
-- 'infinity'): §7.4 requires every target to stay probed forever, including after archiving, so
-- a returning game can re-list itself with no human involved.
CREATE TABLE crawl_target (
    id                      uuid PRIMARY KEY,

    -- NULL until the host answers for itself (§7.2) — a referral becomes a game by answering,
    -- not by being named.
    game_id                 uuid REFERENCES game (id),

    host                    text NOT NULL,
    port                    integer NOT NULL,
    use_tls                 boolean NOT NULL DEFAULT false,

    next_probe_at           timestamptz NOT NULL,
    consecutive_failures    integer NOT NULL DEFAULT 0,

    -- The server's own requested floor (§7.7, §11). NULL means no stated preference, distinct
    -- from an explicit zero.
    crawl_delay             interval,

    first_seen_at           timestamptz NOT NULL,
    last_probed_at          timestamptz,

    -- §7.2 provenance, so a hostile referral subtree can be traced and pruned. Not a dependency:
    -- the target stays due on schedule even if the referring game disappears.
    discovered_from_game_id uuid REFERENCES game (id),
    depth                   integer NOT NULL DEFAULT 0,

    -- §7.2: only operator-supplied seeds may be exempted from scope checks. DEFAULT false is the
    -- security-relevant half — no write path needs to opt in to being unguarded.
    is_operator_seed        boolean NOT NULL DEFAULT false,

    CONSTRAINT crawl_target_port_is_a_port CHECK (port BETWEEN 1 AND 65535),
    CONSTRAINT crawl_target_depth_is_not_negative CHECK (depth >= 0),
    CONSTRAINT crawl_target_failures_are_not_negative CHECK (consecutive_failures >= 0),
    CONSTRAINT crawl_target_crawl_delay_is_not_negative CHECK (crawl_delay IS NULL OR crawl_delay >= interval '0'),

    CONSTRAINT crawl_target_host_is_canonical CHECK (
        host = lower(host) AND host = btrim(host) AND host NOT LIKE '%.')
);

-- One row per address; ICrawlTargetRepository.AddAsync collapses onto the existing row rather
-- than resetting its schedule.
CREATE UNIQUE INDEX crawl_target_address_idx ON crawl_target (host, port);

-- The scheduler's hot query: which targets are due, oldest first.
CREATE INDEX crawl_target_due_idx ON crawl_target (next_probe_at);

-- §7.2's subtree prune walks this direction.
CREATE INDEX crawl_target_discovered_from_idx ON crawl_target (discovered_from_game_id)
    WHERE discovered_from_game_id IS NOT NULL;

-- referral_edge: one game naming another in its MSSP REFERRAL field. Provenance only — nothing
-- here schedules anything; `present = false` means "not currently listed", never deleted.
CREATE TABLE referral_edge (
    from_game_id  uuid NOT NULL REFERENCES game (id),
    to_host       text NOT NULL,
    to_port       integer NOT NULL,
    first_seen_at timestamptz NOT NULL,
    last_seen_at  timestamptz NOT NULL,

    present       boolean NOT NULL DEFAULT true,

    PRIMARY KEY (from_game_id, to_host, to_port),

    CONSTRAINT referral_edge_port_is_a_port CHECK (to_port BETWEEN 1 AND 65535),
    CONSTRAINT referral_edge_seen_after_first_seen CHECK (last_seen_at >= first_seen_at),
    CONSTRAINT referral_edge_host_is_canonical CHECK (
        to_host = lower(to_host) AND to_host = btrim(to_host) AND to_host NOT LIKE '%.')
);

-- Reverse lookup for §7.2's prune and §9's referral neighbours.
CREATE INDEX referral_edge_to_idx ON referral_edge (to_host, to_port);

-- duplicate_review: §7.3's middle band — two games that might be one, held for a person to
-- judge. Both pages stay live and link to each other; nothing here changes presentational state.
CREATE TABLE duplicate_review (
    id             uuid PRIMARY KEY,
    left_game_id   uuid NOT NULL REFERENCES game (id),
    right_game_id  uuid NOT NULL REFERENCES game (id),
    score          double precision NOT NULL,

    -- Every signal weighed, including ones that didn't fire — the whole basis for a reviewer's
    -- judgement.
    signals        jsonb NOT NULL,

    opened_at      timestamptz NOT NULL,

    resolved_at    timestamptz,
    resolution     text,

    -- Pair is unordered; storage orders it so "already open?" is one lookup, not two that can race.
    CONSTRAINT duplicate_review_pair_is_ordered CHECK (left_game_id < right_game_id),
    CONSTRAINT duplicate_review_resolution_accompanies_resolved_at CHECK (
        (resolved_at IS NULL) = (resolution IS NULL))
);

-- One open pair per couple of games — repeated middling scores must not accumulate rows.
CREATE UNIQUE INDEX duplicate_review_open_pair_idx
    ON duplicate_review (left_game_id, right_game_id) WHERE resolved_at IS NULL;

CREATE INDEX duplicate_review_right_idx ON duplicate_review (right_game_id);

-- §7.3's IGameFieldIndex: case-insensitive, trimmed reverse lookup on game_field, which
-- game_field_field_value_idx (raw columns) cannot serve.
CREATE INDEX game_field_folded_value_idx ON game_field (lower(btrim(field)), lower(btrim(value)));
