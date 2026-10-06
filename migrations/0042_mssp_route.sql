-- crawl_target.mssp_route: which way, if any, this address hands over its MSSP report.
--
-- The plaintext form — the line `MSSP-REQUEST` typed at the connect screen, answered between
-- `MSSP-REPLY-START` and `MSSP-REPLY-END` — is the SMAUG family's, and TelnetNegotiationCore 4.0
-- carries it as MSSPPlaintextProtocol. Unlike `IAC DO 70`, which a server without MSSP ignores, it is
-- text at a stranger's login prompt: of twenty games sent it in docs/codebase-survey-2026-07-30.md,
-- three answered and eight read it as a character name. So it is asked at most once of an address
-- that has not answered it, and never of one that reports the proper way. This column is how the
-- crawl loop remembers which is which:
--
--   NULL        nothing known yet. The next session that reaches the game without an option-70
--               report is followed by one short dial that asks MSSP-REQUEST and nothing else.
--   telnet      a report has arrived over option 70. Never asked the plaintext form, ever: the user
--               rule this was built from is "only for games that have not already given us MSSP the
--               proper way".
--   plaintext   answered MSSP-REQUEST. Asked again every session, at the connect screen, because it
--               is the only route this address has.
--   none        sent MSSP-REQUEST and did not answer it. Never asked again.
--
-- A measurement of them, unlike crawl_refusal, which is our note about our own decision: whether a
-- server answers a line of the MSSP specification is a fact about the server. It is still kept off
-- every public surface. The game page's MSSP fields carry the report itself, whichever route brought
-- it; how it travelled says nothing a reader needs.
--
-- Per address, because the probe dials addresses. A game reachable on two ports is asked on each,
-- which is right: nothing makes two listeners run the same code.
--
-- No BEGIN/COMMIT: MigrationRunner opens its own transaction per script and writes the ledger entry
-- inside it.

ALTER TABLE crawl_target
    ADD COLUMN mssp_route text,
    ADD COLUMN mssp_route_at timestamptz,

    ADD CONSTRAINT crawl_target_mssp_route_vocabulary CHECK (
        mssp_route IN ('telnet', 'plaintext', 'none')),

    -- Set together: a route nobody can date is one nobody can judge the age of.
    ADD CONSTRAINT crawl_target_mssp_route_is_dated CHECK (
        (mssp_route IS NULL) = (mssp_route_at IS NULL));

COMMENT ON COLUMN crawl_target.mssp_route IS
    'How this address hands over MSSP: telnet (option 70, so the plaintext MSSP-REQUEST is never '
    'sent), plaintext (answers MSSP-REQUEST, asked every session), none (did not answer it, never '
    'asked again), or NULL (not yet known; gets one trial).';

-- Every game that has already reported over option 70 is excused the trial. Until this migration
-- nothing could put a value under these two sources except an option-70 report, so their presence
-- is exactly "has given us MSSP the proper way". Dated now rather than back-dated: this is when the
-- crawl loop learnt it, and the rows' own first_seen_at would claim a knowledge of this column that
-- nobody had.
UPDATE crawl_target t
   SET mssp_route = 'telnet',
       mssp_route_at = now()
 WHERE EXISTS (
        SELECT 1
          FROM game_field f
         WHERE f.game_id = t.game_id
           AND f.source IN ('mssp', 'mssp_roster'));
