-- game_slug_history: every slug a game has ever had (§5.7) — old URLs redirect forever.
--
-- The row names a game, not another slug: a game renamed twice has two independent rows
-- pointing at it, so resolving an old URL is a single join rather than walking a chain (and a
-- cycle isn't expressible, since there's no slug-to-slug edge to form one).
CREATE TABLE game_slug_history (
    -- The former slug, and the PK: one URL can only ever have belonged to one game. Slug
    -- minting checks this table as well as game.slug.
    slug       text PRIMARY KEY,

    -- No ON DELETE clause: nothing is ever deleted (§7.5), so a cascade would only fire on a
    -- bug — the failing FK is the better outcome.
    game_id    uuid NOT NULL REFERENCES game (id),

    -- When the slug stopped being current; the redirect itself never expires.
    retired_at timestamptz NOT NULL,

    CONSTRAINT game_slug_history_slug_is_not_blank CHECK (btrim(slug) <> '')
);

-- The game page's "which URLs has this game worn" query, and the sweep a rename performs first.
CREATE INDEX game_slug_history_game_idx ON game_slug_history (game_id, retired_at DESC);
