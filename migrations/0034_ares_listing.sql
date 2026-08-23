-- What AresCentral currently says, kept beside what we measured so the two can be told apart.
--
-- Keyed on (hostname, port) rather than on the hub's name for a game: the address is the only thing
-- here that also means something to the crawler, and a game that renames itself on the hub is the
-- same listing rather than a new one.
--
-- delisted_at rather than a delete. Nothing is ever deleted here (§7.5), and a game leaving the hub
-- is a fact worth the date — it does not end our listing, and the crawler keeps probing the address
-- forever either way (§7.4). Cleared on relisting: this is the hub's current opinion, not a
-- tombstone.
--
-- last_ping is the hub's own reachability check, stored as the string it arrives as. It reaches
-- nothing — not availability, not archive grace, not the probe schedule. §7.6 forbids importing
-- another prober's history, and a game we cannot reach must not look reachable because somebody else
-- could. It is here so an operator reading the table can see what the hub thought.
--
-- game_id is nullable and stays null until the ordinary crawl promotes the address. This table never
-- mints a game: a game exists only once a host answers for itself (§7.1).
--
-- No BEGIN/COMMIT: MigrationRunner opens its own transaction per script and writes the ledger
-- entry inside it.

CREATE TABLE ares_listing (
    hostname       text        NOT NULL,
    port           integer     NOT NULL,
    name           text,
    description    text,
    genre          text,
    website        text,
    status         text,
    last_ping      text,
    game_id        uuid        REFERENCES game (id) ON DELETE SET NULL,
    first_seen_at  timestamptz NOT NULL,
    last_listed_at timestamptz NOT NULL,
    delisted_at    timestamptz,
    PRIMARY KEY (hostname, port)
);

CREATE INDEX ares_listing_game_idx ON ares_listing (game_id) WHERE game_id IS NOT NULL;
CREATE INDEX ares_listing_live_idx ON ares_listing (last_listed_at) WHERE delisted_at IS NULL;
