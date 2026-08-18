-- Adds `who_login_prompt` to presence_sample's unmeasurable_reason vocabulary (§5.4). Same rule
-- as 0019/0020: text column under a CHECK, kept in sync with SqlEnums by hand.
--
-- Splits one hatched-cell reason into two distinct facts: `who_unparseable` is a WHO dialect our
-- parser can't read (a defect of ours, the count is in the payload); `who_login_prompt` is a game
-- with no pre-login WHO at all — its login prompt reads the word WHO as a character name, so the
-- count was never in the payload to begin with. Conflating them under one reason misread a
-- "there's nothing here" population as a parser backlog.
--
-- No backfill: an existing row recorded what the crawler believed at the time it was probed, and
-- rewriting it would back-date a conclusion this migration reached (§7.5's habit, applied to a
-- value). Rows re-sort as each game is next probed.
--
-- No BEGIN/COMMIT: MigrationRunner opens its own transaction per script and writes the ledger
-- entry inside it.

ALTER TABLE presence_sample
    DROP CONSTRAINT presence_sample_reason_vocabulary,
    ADD CONSTRAINT presence_sample_reason_vocabulary CHECK (unmeasurable_reason IS NULL OR unmeasurable_reason IN (
        'who_unparseable', 'who_not_offered', 'who_login_prompt', 'players_not_numeric', 'i3_no_reply'));
