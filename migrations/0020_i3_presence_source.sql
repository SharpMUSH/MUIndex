-- Adds `i3` to presence_sample's source vocabulary and `i3_no_reply` to its reason vocabulary
-- (§5.2, §5.4). Same rule as 0019: text column under a CHECK, kept in sync with SqlEnums by hand.
--
-- Source: Intermud-3 is an LP-family network predating MSSP; its login prompts take a character
-- name, so a telnet `WHO` is consumed as a name and the probe gets nothing to count. I3's
-- `who-req` returns an array of users instead, counted by us.
--
-- Measured, not declared (FieldSources.IsMeasured admits it, unlike `mssp`/`info`): the reply is
-- a list of people built when we asked and counted ourselves, not a cached figure the far end
-- reported.
--
-- Caveat: the reply crosses a router we don't run and reflects the remote mud's own visibility
-- rules — same property a telnet WHO has, so not grounds to rank this lower, but it does mean an
-- I3 count and a telnet count for the same game may legitimately differ.
--
-- `i3_no_reply`: a mud that's up, advertises `who`, and says nothing — a hatched cell with a
-- cause, never a zero. An empty `users` array is different: a measured zero, a filled cell.
--
-- No reason exists here for "doesn't advertise who" — we never ask those, so no row is written,
-- and a vocabulary entry nothing can produce only invites misuse.
--
-- game_field's vocabulary is not widened: I3 mudlist driver/mudlib are the network's relayed
-- claims, not something we observed.
--
-- No BEGIN/COMMIT: MigrationRunner opens its own transaction per script and writes the ledger
-- entry inside it.

ALTER TABLE presence_sample
    DROP CONSTRAINT presence_sample_source_vocabulary,
    ADD CONSTRAINT presence_sample_source_vocabulary CHECK (source IN (
        'who', 'i3', 'mssp', 'info', 'banner')),

    DROP CONSTRAINT presence_sample_reason_vocabulary,
    ADD CONSTRAINT presence_sample_reason_vocabulary CHECK (unmeasurable_reason IS NULL OR unmeasurable_reason IN (
        'who_unparseable', 'who_not_offered', 'players_not_numeric', 'i3_no_reply'));
