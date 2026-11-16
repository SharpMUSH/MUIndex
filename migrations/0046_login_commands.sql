-- crawl_target.login_commands_*: whether INFO and VERSION are still worth typing at this address.
--
-- Issue #182. MsspSelfDescription stops them only where an MSSP report already answers both, so a
-- game that publishes no MSSP was asked on every crawl, for ever, whatever it answered. At
-- fs.twkang.net:5555 and its siblings it answers nothing readable: INFO comes back as the name prompt
-- again and VERSION as "create a new character called version? (y/n)". A codebase that does not
-- answer INFO today almost certainly never will, so the decision is learned from the address's own
-- history rather than re-derived from one session (LoginCommands, in MUI.Discovery).
--
--   login_commands_unanswered  consecutive sessions in which INFO was typed here and nothing in
--                              the replies was something a reader stands behind. Capped at the
--                              threshold. Back to 0 on the first session that yields anything.
--   login_commands_quiet_at    NULL: asked. Set: the run reached the threshold at this time, and
--                              INFO and VERSION are not typed here again. There is no retrial. The
--                              one exception is a submission still awaiting corroboration, which
--                              is asked regardless; an answer from it clears both columns.
--
-- A session in which we did not type INFO is no evidence either way and changes neither column, so
-- our silence is never counted as theirs (rule 5). And none of this is a fact about the game on any
-- public surface: it is our note about which questions we have stopped asking, read only by the
-- crawl loop.
--
-- No backfill: nothing persisted records what an INFO reply said, so every address starts asking
-- and earns its quiet in three sessions.
--
-- No BEGIN/COMMIT: MigrationRunner opens its own transaction per script and writes the ledger entry
-- inside it.

ALTER TABLE crawl_target
    ADD COLUMN login_commands_unanswered smallint NOT NULL DEFAULT 0
        CHECK (login_commands_unanswered >= 0),
    ADD COLUMN login_commands_quiet_at timestamptz;

COMMENT ON COLUMN crawl_target.login_commands_unanswered IS
    'Consecutive sessions in which INFO was typed here and no reply was readable. Capped.';

COMMENT ON COLUMN crawl_target.login_commands_quiet_at IS
    'When INFO and VERSION stopped being typed here for want of an answer. NULL: still asked.';
