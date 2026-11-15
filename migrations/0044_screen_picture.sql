-- screen_picture: the pictures a game's connect screen draws over its own cells, so the site can
-- draw them over the same cells of the screen it quotes.
--
-- Two kinds of row. An inline picture (sixel, a Kitty transmission, an iTerm2 file) arrived on the
-- wire as its bytes, and the probe's row carries them from the start. An MXP IMAGE named an
-- address, which is what source_url holds; the bytes arrive later, fetched by the web tier through
-- the same §7.2 gate as a game's ICON (ScreenPictureRefresher), and stay null until then.
--
-- Droppable, like game_icon and icon_attempt (migrations 0013, 0035), and for the same reason. The
-- fact is the connect screen (game_field connect_screen); these are its pictures, which the next
-- probe that reads the screen writes again. §7.5's "nothing is ever deleted" protects a game's
-- record, and a re-fetchable picture of a screen is not one — so rows are replaced wholesale each
-- time a screen is read, and a picture the screen no longer draws is deleted rather than kept.
--
-- A failed fetch writes nothing about the game (rule 5). It does write when we tried and when to try
-- again, on the row itself: icon_attempt's reason — a queue with no record of a failure cannot tell
-- a candidate it just failed from one it has never seen, and asks the same dead servers for ever.
-- Nothing in attempted_at, failures or next_attempt_at reaches a page.
CREATE TABLE screen_picture (
    -- ON DELETE CASCADE, as game_icon has: a picture of a game that no longer exists is worth nothing.
    game_id         uuid NOT NULL REFERENCES game (id) ON DELETE CASCADE,

    -- Which picture on the screen, in the order the screen draws them, from 0.
    ordinal         int  NOT NULL CHECK (ordinal BETWEEN 0 AND 7),

    -- Where it is drawn, in the stored connect_screen's own cells: its \n-separated line from 0, and
    -- the cell on that line from 0. Rows and columns, never pixels — the frame draws the picture over
    -- the cells the game laid out for it, at whatever size those cells are on the reader's screen.
    line            int  NOT NULL CHECK (line >= 0),
    col             int  NOT NULL CHECK (col >= 0),
    columns         int  NOT NULL CHECK (columns BETWEEN 1 AND 256),
    rows            int  NOT NULL CHECK (rows BETWEEN 1 AND 100),

    -- The address an MXP IMAGE named, or null for a picture that arrived inline.
    source_url      text,

    -- Determined from the bytes, never trusted from a header, and served with nosniff. Null until an
    -- address has been fetched.
    content_type    text,
    bytes           bytea CHECK (octet_length(bytes) <= 1048576),

    -- When the screen holding this picture was read.
    captured_at     timestamptz NOT NULL,

    -- The fetch bookkeeping, for an address only, and never shown. next_attempt_at is when the
    -- address is next worth asking: null for one never asked, a back-off after a failure, and the
    -- refresh interval after a success, so a picture replaced at the same address is picked up.
    attempted_at    timestamptz,
    failures        int NOT NULL DEFAULT 0 CHECK (failures >= 0),
    next_attempt_at timestamptz,

    PRIMARY KEY (game_id, ordinal),

    -- An inline picture is its bytes; a row with neither bytes nor an address has nothing to show
    -- and nothing to fetch.
    CONSTRAINT screen_picture_has_a_source CHECK (bytes IS NOT NULL OR source_url IS NOT NULL),
    CONSTRAINT screen_picture_bytes_have_a_type CHECK ((bytes IS NULL) = (content_type IS NULL))
);

-- ScreenPictureRefresher's question: which addresses are due.
CREATE INDEX screen_picture_due_idx ON screen_picture (next_attempt_at NULLS FIRST)
    WHERE source_url IS NOT NULL;
