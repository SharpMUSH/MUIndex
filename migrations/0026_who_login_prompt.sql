-- Adds `who_login_prompt` to the reasons a presence sample may give for having no count (§5.4).
--
-- Same rule as 0019 and 0020: `presence_sample.unmeasurable_reason` is a text column under a CHECK
-- constraint rather than a Postgres enum type, so this alters the constraint instead of adding an enum
-- value. The C# spelling in SqlEnums and the SQL vocabulary here are two halves of one decision, and a
-- member added on one side and forgotten on the other has to fail at the database rather than become a
-- value every reader downstream copes with differently.
--
-- What it separates, and why it is worth a vocabulary entry. §5.4's hatched cell means "we probed and
-- could not count", and two entirely different facts were arriving under one word for it:
--
--   * `who_unparseable` — our parser met a WHO dialect it could not read. Every one of these is a
--     defect of ours with an owner and a fix, and the count is sitting in the payload.
--   * `who_login_prompt` — the game has no pre-login WHO at all. Its login prompt takes a character
--     name, so the word WHO went in as one and `Illegal name, try again.` came back. There is nothing
--     here for a parser to get better at; the count is not in the payload and never was.
--
-- Reading the stored payloads of the 107 games recorded unmeasurable-and-unparseable on 2026-08-17,
-- **43 were the second** — the largest single population under the word, and the whole of it was being
-- read as a parser backlog. That is rule 5 in its quietest form: a limit of ours written into a game's
-- record as a fact about the game. It is also the difference between a maintainer chasing 120 parser
-- bugs and chasing 60.
--
-- No data moves, and that is deliberate rather than an omission. Every existing row keeps the reason it
-- was written with, because a row written in August recorded what the crawler believed in August and
-- rewriting it would be back-dating a conclusion this migration reached — §7.5's rule, applied to a
-- value rather than to a game. The rows re-sort themselves as each game is next probed, which is
-- within one crawl cycle for an active game, and the ones that never re-sort are games that stopped
-- answering, whose old reason is the right one to have kept.
--
-- No BEGIN/COMMIT here: MigrationRunner opens a transaction around each script and writes the ledger
-- entry inside it, so a script opening its own commits half the work and then fails the ledger
-- insert with "transaction is already completed".

ALTER TABLE presence_sample
    DROP CONSTRAINT presence_sample_reason_vocabulary,
    ADD CONSTRAINT presence_sample_reason_vocabulary CHECK (unmeasurable_reason IS NULL OR unmeasurable_reason IN (
        'who_unparseable', 'who_not_offered', 'who_login_prompt', 'players_not_numeric', 'i3_no_reply'));
