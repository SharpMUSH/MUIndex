-- The icon a game publishes, fetched by us rather than hot-linked by a reader's browser.
--
-- ICON is an MSSP variable holding a URL, and it is in §8.5's overridable set — so an owner may point
-- it somewhere, and so may any game that filled the variable in. THE ICON IS THEREFORE NOT AN OWNER
-- FEATURE: every game with a declared ICON gets one, and claiming a game is how you change it rather
-- than how you get it.
--
-- WHY WE HOLD BYTES AT ALL. Rendering <img src="https://their-host/logo.png"> hands every reader's IP
-- address, user-agent and referring URL to a third-party server on every page view, for a decoration.
-- §11 is a section about not doing that to people who did not ask, and readers did not ask. So the
-- site fetches the image once, on a schedule, and serves it from its own origin.
--
-- THIS TABLE IS A CACHE AND HOLDS NO FACT, WHICH IS WHY IT IS THE ONE PLACE HERE THAT MAY BE EMPTIED.
-- "Nothing is ever deleted" (§7.5) is a rule about a game's record: its fields, its history, its URLs.
-- The fact here is the ICON field, which is a GameField row like any other and is not in this table.
-- These are bytes we fetched from the URL that field names, and dropping every one of them loses
-- nothing that cannot be fetched again. It is not a fact about the game, it carries no age anybody is
-- shown, and it never reaches the change feed.
--
-- A FAILED FETCH WRITES NOTHING AT ALL — no row, no marker, no "we tried". That we could not reach
-- somebody's web server is a fact about our afternoon and not about their game, and rule 5 forbids
-- recording one as the other. The page renders no element rather than a broken image or an apology.
CREATE TABLE game_icon (
    -- One icon per game. The URL it came from lives beside the bytes rather than being re-read from
    -- the field, because the two can disagree for one cycle — an owner changes the field, and until
    -- the next refresh these bytes are still the old picture. Serving them while saying they came
    -- from the new URL would be a small lie of exactly the kind this site exists not to tell.
    --
    -- ON DELETE CASCADE, unlike every other table here, and for the same reason the whole table is
    -- different: a cache row for a game that no longer exists is not a record worth keeping.
    game_id      uuid PRIMARY KEY REFERENCES game (id) ON DELETE CASCADE,

    -- The URL these bytes were fetched from, as the field said it at the time.
    source_url   text NOT NULL,

    -- The type WE determined from the bytes, never the one the far end claimed in a header. It is
    -- served back verbatim with X-Content-Type-Options: nosniff, so it has to be a type we checked.
    content_type text NOT NULL,

    -- Read from the image's own header rather than from a decode. Stored because the page uses them
    -- for width/height attributes, which is what stops the layout jumping as the image arrives.
    width        int  NOT NULL,
    height       int  NOT NULL,

    -- Bounded by the fetcher at 256 KB. A CHECK here as well, because the bound is a property of what
    -- this table is for and not only of the one path that currently writes it.
    bytes        bytea NOT NULL CHECK (octet_length(bytes) <= 262144),

    -- What the far end gave us, so the next fetch can ask conditionally and usually be told nothing
    -- has changed. Nullable: plenty of servers do not send one.
    etag         text,

    -- When we last fetched, so the refresher knows what is due. Not shown to anybody: an icon is a
    -- decoration and dating it on the page would give it the weight of a measurement.
    fetched_at   timestamptz NOT NULL
);

-- The refresher's one query: which games are due a fetch. Ordered by how long ago we looked.
CREATE INDEX game_icon_fetched_at_idx ON game_icon (fetched_at);
