-- spec §5.7 — every slug a game has ever had, beside the games.
--
-- "Every slug a game has ever had redirects to it, forever. Nothing is ever deleted here either: a URL
-- that once worked keeps working, which is the same promise the archive makes about pages." A slug is
-- minted from a name and games rename themselves, so the promise needs somewhere to live that is not
-- an operator's configuration file.
--
-- THE ROW NAMES A GAME AND NOT ANOTHER SLUG, AND THAT IS THE WHOLE DESIGN. A game renamed twice has
-- two rows, both pointing at the game, so its oldest URL resolves to its current one in a single join
-- rather than by walking a chain — and a cycle is not expressible, because there is no edge between
-- two slugs for a cycle to be made of. The alternative (former -> current, as text) has to be walked,
-- has to be depth-limited, and goes wrong the first time a game takes back a name it used to have.
CREATE TABLE game_slug_history (
    -- The former slug, and the primary key: one URL can only ever have belonged to one game. Whatever
    -- mints a slug asks this table as well as game.slug, so a URL somebody is still holding is never
    -- handed to a different game.
    slug       text PRIMARY KEY,

    -- No ON DELETE clause, deliberately. Nothing is ever deleted (§7.5) — an archived game keeps its
    -- page, its history and its URLs — so a cascade would only ever fire for a bug, and the failing
    -- foreign key is the better outcome.
    game_id    uuid NOT NULL REFERENCES game (id),

    -- When the slug stopped being current, which is the moment the redirect began. Informational: the
    -- redirect itself has no expiry and never gets one.
    retired_at timestamptz NOT NULL,

    CONSTRAINT game_slug_history_slug_is_not_blank CHECK (btrim(slug) <> '')
);

-- "Which URLs has this game worn" — the game page's own question, and the sweep a rename performs
-- before it re-mints.
CREATE INDEX game_slug_history_game_idx ON game_slug_history (game_id, retired_at DESC);
