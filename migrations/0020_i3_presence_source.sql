-- Adds `i3` to the vocabulary a presence sample may name as its source, and `i3_no_reply` to the
-- reasons it may give for having no count (spec §5.2, §5.4).
--
-- Same rule as 0019: `presence_sample.source` is a text column under a CHECK constraint rather than a
-- Postgres enum type, so this alters constraints instead of adding enum values. The C# spelling in
-- SqlEnums and the SQL vocabulary here are two halves of one decision, and a member added on one side
-- and forgotten on the other has to fail at the database rather than become a value every reader
-- downstream copes with differently.
--
-- What produces the new source. Intermud-3 is a network of LP-family games that predates MSSP and
-- never adopted it, and whose login prompts take a character name rather than commands — so `WHO` at
-- the connect screen is consumed as a name and the telnet probe has nothing to count. Of 428 games
-- sampled over three days, 240 were unmeasurable, and reading a sample of the stored payloads showed
-- roughly 210 of those are games where `WHO` is not a connect-screen command at all. I3 answers the
-- question a different way: `who-req` returns an array of users, one entry per person, and the count
-- is the length of that array. Nightfall — dark to the telnet probe, because its login prompt reads
-- `WHO` as a character name — answered with five users and their idle times.
--
-- Measured, not declared. `FieldSources.IsMeasured` admits it, beside `who` and `banner` and unlike
-- `mssp` and `info`. The distinction this codebase draws is whether a value was read or reported: an
-- MSSP `PLAYERS` and an INFO `Connected:` are figures a codebase generated about itself and may have
-- cached, while an I3 who-reply is a list of people built when we asked, which we count ourselves.
-- Nobody stated a number.
--
-- The caveat is real and is written down rather than smoothed over: the reply crosses a router we do
-- not run, and it lists whoever the remote mud's own visibility rules chose to list. A telnet `WHO`
-- has the second property too, which is why it is not grounds to rank this lower — but it does mean
-- an I3 count and a telnet count for the same game may legitimately differ, and neither is wrong.
--
-- `i3_no_reply` is the third state doing its job. A mud that is up, advertises `who`, is asked, and
-- says nothing has told us nothing about how many people are on it. That is a hatched cell with a
-- cause, never a zero (rule 4, and §5.4's middle case). An empty `users` array is the opposite: the
-- mud answered and the answer was nobody, which is a measured zero and a filled cell — `The Zone`
-- returned exactly that.
--
-- There is deliberately no reason here for "the mud does not advertise `who`". We never ask those,
-- so no row is written at all, and a reason that no writer can produce is a vocabulary entry that
-- only invites somebody to reach for it. Not asking is our decision about our manners, and §5.5's
-- rule is that a decision of ours is never recorded as a measurement of theirs.
--
-- `game_field`'s vocabulary is not widened. Nothing writes an `i3` field row: the mudlist's driver
-- and mudlib are the network's claims relayed by a router, not something we observed, and they are
-- not the kind of thing this site records as a measurement.
--
-- No data moves. Every existing row keeps the source it was written with.
--
-- No BEGIN/COMMIT here: MigrationRunner opens a transaction around each script and writes the ledger
-- entry inside it, so a script opening its own commits half the work and then fails the ledger
-- insert with "transaction is already completed".

ALTER TABLE presence_sample
    DROP CONSTRAINT presence_sample_source_vocabulary,
    ADD CONSTRAINT presence_sample_source_vocabulary CHECK (source IN (
        'who', 'i3', 'mssp', 'info', 'banner')),

    DROP CONSTRAINT presence_sample_reason_vocabulary,
    ADD CONSTRAINT presence_sample_reason_vocabulary CHECK (unmeasurable_reason IS NULL OR unmeasurable_reason IN (
        'who_unparseable', 'who_not_offered', 'players_not_numeric', 'i3_no_reply'));
