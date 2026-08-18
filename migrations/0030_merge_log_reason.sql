-- Gives a merge somewhere to carry the operator's stated reason, not just a score (§7.3).
-- `--merge` requires `--because` at the CLI, but merge_log had no column for it: the
-- review-backed path wrote the reason onto duplicate_review.resolution, while a hand-judgement
-- merge (no open duplicate_review row) had nowhere to record it at all.
--
-- Nullable: the rows already in this table predate the CLI and its reason column, so leaving
-- them NULL says truthfully that nobody recorded why at the time, rather than backfilling an
-- invented explanation. Every merge from the CLI onward always has one, since the CLI refuses
-- `--merge` without `--because`.
--
-- No BEGIN/COMMIT: MigrationRunner opens its own transaction per script and writes the ledger
-- entry inside it.

ALTER TABLE merge_log ADD COLUMN reason text;
