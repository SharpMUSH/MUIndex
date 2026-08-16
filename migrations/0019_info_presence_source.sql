-- Adds `info` to the vocabulary a presence sample may name as its source (spec §5.2).
--
-- `presence_sample.source` is a text column under a CHECK constraint rather than a Postgres enum
-- type, which is why this is an ALTER of a constraint and not a CREATE TYPE / ALTER TYPE ... ADD
-- VALUE. Same rule either way: the C# spelling in SqlEnums and the SQL vocabulary here are two
-- halves of one decision, and a member added on one side and forgotten on the other has to fail at
-- the database rather than become a value every reader downstream copes with differently.
--
-- What produces the new rung. The MUSH family answers the pre-login `INFO` command with a block the
-- codebase generates — PennMUSH's `dump_info()` writes `### Begin INFO 1.1`, a run of `Label: value`
-- lines including `Connected:`, and `### End INFO`; Evennia writes the same idea with two hashes and
-- an uppercase BEGIN/END. The crawler has been sending `INFO` and reading a name and a codebase out
-- of that block for months while the one number in it went in the bin. Of 428 games sampled over
-- three days, 240 were recorded unmeasurable, and a MUSH publishing `Connected: 60` with no MSSP and
-- a softcoded DOING header our WHO parser cannot read was one of them.
--
-- Why it sits below `mssp` on §5.2's ladder rather than above it. In PennMUSH both figures come out
-- of the same `count_players()` call, so they are equal-trust and there is nothing to choose between
-- them on accuracy. Ordering the new rung second is therefore the conservative placement and not a
-- judgement: no game that measures today changes the value already recorded against it, so the
-- rung's whole effect is on rows that would otherwise have been NULL.
--
-- Declared, not measured. `FieldSources.IsMeasured` does not admit it, and the chips will say
-- "declared" beside an INFO count exactly as they do beside an MSSP one — the value is a labelled
-- line a codebase generated about itself, which is the same class of statement as an MSSP variable
-- arriving down a different pipe. `banner` stays measured and stays last, which is §5.1's two
-- ladders disagreeing on purpose.
--
-- `game_field`'s vocabulary is deliberately not widened. Nothing writes an `info` field row: the
-- name and codebase read out of the same block go in under `banner`, because those are parsed out of
-- free text rather than lifted off a generated line. A vocabulary is the list of what a table can
-- actually be given, and presence has never carried `staff` for the mirror-image reason.
--
-- No data moves. Every existing row keeps the source it was written with.
--
-- No BEGIN/COMMIT here: MigrationRunner opens a transaction around each script and writes the ledger
-- entry inside it, so a script opening its own commits half the work and then fails the ledger
-- insert with "transaction is already completed".

ALTER TABLE presence_sample
    DROP CONSTRAINT presence_sample_source_vocabulary,
    ADD CONSTRAINT presence_sample_source_vocabulary CHECK (source IN (
        'who', 'mssp', 'info', 'banner'));
