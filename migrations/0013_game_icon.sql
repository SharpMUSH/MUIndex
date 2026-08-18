-- game_icon: the icon a game publishes (MSSP ICON), fetched and re-served from our own origin
-- rather than hot-linked — hot-linking would leak every reader's IP/UA/referrer to a
-- third-party server for a decoration (§11). Available to every game with a declared ICON, not
-- an owner-only feature; claiming a game only lets you change it.
--
-- The one table here that may be emptied. "Nothing is ever deleted" (§7.5) protects a game's
-- record — fields, history, URLs. This table holds no fact of its own, only bytes fetched from
-- a URL the ICON field already names; dropping them loses nothing that can't be re-fetched.
--
-- A failed fetch writes nothing at all — no row, no marker. Our inability to reach a web server
-- is not a fact about the game (rule 5); the page just renders no icon.
CREATE TABLE game_icon (
    -- ON DELETE CASCADE, unlike every other table here — a cache row for a game that no longer
    -- exists isn't worth keeping.
    game_id      uuid PRIMARY KEY REFERENCES game (id) ON DELETE CASCADE,

    -- Stored beside the bytes rather than re-read from the field: the two can disagree for one
    -- cycle if the field changes before the next refresh, and serving old bytes while claiming
    -- the new URL would misattribute them.
    source_url   text NOT NULL,

    -- Determined by us from the bytes, never trusted from a response header — served back with
    -- X-Content-Type-Options: nosniff, so it has to be a type we actually checked.
    content_type text NOT NULL,

    -- Read from the image's own header; used for width/height attributes so layout doesn't jump.
    width        int  NOT NULL,
    height       int  NOT NULL,

    -- Bounded by the fetcher at 256 KB; enforced here too since the bound belongs to the table,
    -- not just to the one path that currently writes it.
    bytes        bytea NOT NULL CHECK (octet_length(bytes) <= 262144),

    -- Enables conditional refetching; nullable since many servers don't send one.
    etag         text,

    -- Drives the refresher's due-list. Not shown on the page — dating a decoration would give
    -- it the weight of a measurement.
    fetched_at   timestamptz NOT NULL
);

-- The refresher's query: which games are due a fetch, oldest first.
CREATE INDEX game_icon_fetched_at_idx ON game_icon (fetched_at);
