-- spec §5.5 — plural and historical. A game that moves does not become unfindable: old endpoints are
-- still probed at the §7.4 floor, and a referral or DNS record pointing at an old address re-links to
-- the existing game rather than minting a duplicate.
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

    -- Teeth for host normalisation, in the same spirit as the vocabulary constraints above. The
    -- unique index below is only an identity guarantee if one host has one spelling: 'MUD.Example.ORG'
    -- and 'mud.example.org' are different strings and would be two rows for one machine. A write path
    -- that forgets to normalise now fails here, loudly, rather than quietly minting the duplicate
    -- listing §7.3 exists to prevent.
    CONSTRAINT game_endpoint_host_is_canonical CHECK (
        host = lower(host) AND host = btrim(host) AND host NOT LIKE '%.')
);

-- §7.3 calls a previously-seen endpoint the strongest identity signal, and asks it of an address with
-- no game in hand. UNIQUE rather than merely indexed, because that is only a signal if one address
-- cannot be claimed by two games — which is exactly the duplicate-listing failure §7.3 exists to
-- stop. Plain equality on a canonical column, so the lookup uses this index; there is no lower(host)
-- functional index because the CHECK above leaves it nothing to fold.
CREATE UNIQUE INDEX game_endpoint_address_idx ON game_endpoint (host, port);
