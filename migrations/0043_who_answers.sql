-- crawl_target.who_answers_at: when this address last showed that WHO is a command it answers.
--
-- A game may state its own count, over MSSP or on its connect screen, and the probe then does not
-- type WHO at it (TelnetProbe.PublishedCountAsync). That restraint is right for a game whose login
-- prompt reads WHO as a character name. It is wrong for one whose stated count is not the number
-- online: tapestries.fur.com:2069 answers the plaintext MSSP-REQUEST with PLAYERS = 26842, its player
-- objects, while its pre-login WHO had counted 300 to 500 every crawl. Where WHO is known to work, WHO
-- is asked and its count outranks MSSP PLAYERS, as PresenceChoice already ranks them.
--
--   NULL   not known to answer WHO. A stated count still spares it the WHO.
--   set    a WHO typed here came back with a count. Asked every session from then on. Cleared the
--          first time WHO comes back as the login prompt taking it for a character name.
--
-- A measurement of them, like mssp_route: whether a server answers a pre-login WHO is a fact about
-- the server. Kept off every public surface all the same.
--
-- No BEGIN/COMMIT: MigrationRunner opens its own transaction per script and writes the ledger entry
-- inside it.

ALTER TABLE crawl_target
    ADD COLUMN who_answers_at timestamptz;

COMMENT ON COLUMN crawl_target.who_answers_at IS
    'When WHO typed at this address last came back with a count. Set: WHO is asked every session '
    'even where the game states its own count. NULL: not known to answer WHO.';

-- A `who` presence row exists only where a WHO came back counted and was published, so it is
-- exactly the evidence this column records. presence_sample is keyed by game, not address, so only a
-- game with one address can be credited without guessing which listener answered; a game on several
-- addresses learns it per address on the next WHO that counts. Dated by the newest such row, which is
-- when it was last seen to be true.
UPDATE crawl_target t
   SET who_answers_at = w.at
  FROM (SELECT p.game_id, max(p.at) AS at
          FROM presence_sample p
         WHERE p.source = 'who'
           AND p.count IS NOT NULL
         GROUP BY p.game_id) w
 WHERE t.game_id = w.game_id
   AND NOT EXISTS (
        SELECT 1
          FROM crawl_target other
         WHERE other.game_id = t.game_id
           AND other.id <> t.id);
