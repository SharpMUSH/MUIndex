-- icon_attempt: what happened the last time we tried to fetch a game's icon, and when it is worth
-- trying again. Bookkeeping about our own retries — never about the game.
--
-- This reverses one line of the icon design ("a failed fetch writes nothing at all — no row, no
-- marker, no attempt counter"), and the reason is worth stating rather than quietly dropping. That
-- rule was rule 5 applied to a picture: our inability to reach somebody's web server must not enter
-- their public record. It still must not, and nothing here does — no page, no API field, no change
-- feed entry, no ordering of anything a reader sees. What the rule also produced, unintentionally,
-- was a queue that could not move: DueAsync ranks candidates by what game_icon holds, a game we have
-- never fetched holds nothing, and every such game therefore ties with every other on both sort
-- keys. LIMIT 20 over a tie is the same twenty rows for ever. Production ran that way for six days —
-- the same fifteen URLs re-fetched every thirty minutes, all failing, while forty-seven games with a
-- perfectly good declared ICON were never attempted once.
--
-- So the marker exists, and it is a fact about this site: we tried, at this time, and will try again
-- after that one. It buys two things — a queue that advances, and a back-off, so a web server that
-- has been down for a week is asked once a week rather than forty-eight times a day.
--
-- Droppable, like game_icon and for the same reason (§7.5 protects a game's record; this is not one).
-- Emptying it costs one wasted pass.
CREATE TABLE icon_attempt (
    -- ON DELETE CASCADE, as game_icon has: bookkeeping for a game that no longer exists is worth
    -- nothing.
    game_id         uuid PRIMARY KEY REFERENCES game (id) ON DELETE CASCADE,

    -- The URL that was tried. A declared ICON that has since moved makes this row's failure count
    -- irrelevant — a new address is a new question, and DueAsync reads the count only where the two
    -- still agree.
    url             text NOT NULL,

    -- When we last tried. This is the sort key that makes the queue advance: a candidate just tried
    -- goes to the back, whatever it holds.
    attempted_at    timestamptz NOT NULL,

    -- How many times in a row this URL has failed us, which is what sizes the back-off. Reset by the
    -- URL changing, and by a success — a success deletes the row.
    failures        int NOT NULL CHECK (failures > 0),

    -- When it is worth asking again. Computed by IconRefresher rather than here: the schedule is
    -- policy and belongs beside the interval and the staleness window it is scaled from, not in a
    -- CHECK constraint nobody would think to look at.
    next_attempt_at timestamptz NOT NULL
);

-- DueAsync's own predicate: which attempts have come round again.
CREATE INDEX icon_attempt_next_attempt_at_idx ON icon_attempt (next_attempt_at);
