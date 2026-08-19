-- game_endpoint: plural and historical (§5.5). A game that moves keeps working — old endpoints
-- are still probed at the §7.4 floor, and a stale reference re-links to the existing game
-- instead of minting a duplicate.
CREATE TABLE game_endpoint (
    game_id       uuid NOT NULL REFERENCES game (id),
    host          text NOT NULL,
    port          integer NOT NULL,
    kind          text NOT NULL,
    first_seen_at timestamptz NOT NULL,
    last_seen_at  timestamptz NOT NULL,
    state         text NOT NULL,

    PRIMARY KEY (game_id, host, port),

    CONSTRAINT game_endpoint_kind_vocabulary CHECK (kind IN ('telnet', 'tls', 'websocket', 'http')),
    CONSTRAINT game_endpoint_state_vocabulary CHECK (state IN ('active', 'stale', 'gone')),
    CONSTRAINT game_endpoint_port_is_a_port CHECK (port BETWEEN 1 AND 65535),
    CONSTRAINT game_endpoint_seen_after_first_seen CHECK (last_seen_at >= first_seen_at),

    -- A write path that forgets to normalise a host fails here rather than silently minting the
    -- duplicate listing §7.3 exists to prevent.
    CONSTRAINT game_endpoint_host_is_canonical CHECK (
        host = lower(host) AND host = btrim(host) AND host NOT LIKE '%.')
);

-- §7.3's strongest identity signal: one address can belong to only one game. No functional
-- lower(host) index needed — the CHECK above already keeps the column canonical.
CREATE UNIQUE INDEX game_endpoint_address_idx ON game_endpoint (host, port);
