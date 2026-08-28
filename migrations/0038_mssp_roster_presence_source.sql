-- Adds `mssp_roster` to presence_sample's source vocabulary (§5.2). Text column under a CHECK
-- rather than a Postgres enum, so this alters the constraint instead of adding an enum value; the
-- C# spelling (SqlEnums) and this vocabulary must stay in sync or fail loudly. Applied to the
-- partitioned parent, which propagates to every partition — the same shape 0019 and 0020 used.
--
-- Source: three codebase families publish who is online inside their own MSSP report, under names
-- MSSP does not define. Surveyed 2026-08-28 across the 934 catalogued games holding an MSSP report:
--
--   PLAYERNAMES   1 game   one value, comma-separated          Circle/Nukefire
--   WHO           9 games  repeated, one occurrence per player Dead Souls / LPMud
--   PLAYER INFO   2 games  one value, `name:role` entries      Rise of Praxis, LPMud
--
-- Its own rung rather than more `mssp`, and this is the whole reason for the migration: both
-- arrive in the same report, but `PLAYERS` is the game answering the question while a roster is us
-- counting a list published for another purpose, and the two do not agree. Measured, not assumed —
-- tdome.nukefire.org:4000 states PLAYERS = 70 and names sixty-nine, identically across three probes
-- minutes apart. A roster leaves out whoever the game does not show. Publishing that under `mssp`
-- would relabel a floor as a total and leave no way for a reader to tell which they had.
--
-- Ranked below `info` and above `banner`, on the rule 0019 set: a new rung may only fill rows that
-- would otherwise be NULL, never relabel a count already published. In today's catalogue that makes
-- it empty — every game publishing a roster also states PLAYERS or is counted over I3 — and that is
-- the point. It exists so the probe can stop typing WHO at a game whose report already answered,
-- with somewhere honest to put the answer if the stated count ever goes missing.
--
-- Declared, not measured (FieldSources.IsMeasured does not admit it), which is where it parts
-- company with `i3`: an I3 who-reply is a list a mud built because we asked, over a socket, now. An
-- MSSP roster came unsolicited inside the game's own self-description and is as old as whatever
-- generated the report.
--
-- game_field's vocabulary is not widened: nothing writes a roster as a descriptive field.
--
-- No BEGIN/COMMIT: MigrationRunner opens its own transaction per script and writes the ledger
-- entry inside it.

ALTER TABLE presence_sample
    DROP CONSTRAINT presence_sample_source_vocabulary,
    ADD CONSTRAINT presence_sample_source_vocabulary CHECK (source IN (
        'who', 'i3', 'mssp', 'info', 'mssp_roster', 'banner'));
