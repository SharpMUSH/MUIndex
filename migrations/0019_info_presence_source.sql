-- Adds `info` to presence_sample's source vocabulary (§5.2). Text column under a CHECK rather
-- than a Postgres enum, so this alters the constraint instead of adding an enum value; the C#
-- spelling (SqlEnums) and this vocabulary must stay in sync or fail loudly.
--
-- Source: the pre-login INFO block several codebases emit (PennMUSH's dump_info, Evennia's
-- equivalent) includes a player count alongside name/codebase, which the crawler was already
-- reading for name/codebase but discarding the count.
--
-- Ranked below `mssp` rather than above: in PennMUSH both figures come from the same
-- count_players() call, so there's no accuracy difference to justify ranking above it; placing
-- it second only fills rows that would otherwise be NULL, without changing any existing value.
--
-- Declared, not measured (FieldSources.IsMeasured does not admit it) — the value is a labelled
-- line a codebase generated about itself, same class as an MSSP variable.
--
-- game_field's vocabulary is not widened: name/codebase parsed from the same block still go in
-- under `banner`, since they're parsed from free text rather than lifted off a generated line.
--
-- No BEGIN/COMMIT: MigrationRunner opens its own transaction per script and writes the ledger
-- entry inside it.

ALTER TABLE presence_sample
    DROP CONSTRAINT presence_sample_source_vocabulary,
    ADD CONSTRAINT presence_sample_source_vocabulary CHECK (source IN (
        'who', 'mssp', 'info', 'banner'));
