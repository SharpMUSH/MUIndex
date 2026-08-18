-- Spec §7.8. A submitted address the probe shows to be a game publishes on sight, exactly as a
-- crawler-discovered one does (§4.4), instead of waiting on a claim.
--
-- corroborated_by is a point-in-time snapshot, never a live view — the audit question is "why
-- was this published", which a column rewritten on every probe couldn't answer.
ALTER TABLE game
    ADD COLUMN corroborated_at timestamptz,
    ADD COLUMN corroborated_by text[];

-- The pair is one fact, matching game_archived_games_have_a_date's idiom.
ALTER TABLE game ADD CONSTRAINT game_corroboration_is_whole
    CHECK ((corroborated_at IS NULL) = (corroborated_by IS NULL)
           AND (corroborated_by IS NULL OR array_length(corroborated_by, 1) >= 1));

-- The signals MuLikeness emits, plus 'staff' for a manual mui-crawl release. A vocabulary CHECK
-- rather than a comment, so a signal renamed in C# and forgotten here fails at write time.
ALTER TABLE game ADD CONSTRAINT game_corroboration_vocabulary
    CHECK (corroborated_by IS NULL OR corroborated_by <@ ARRAY[
        'mssp', 'gmcp', 'msdp', 'mxp', 'msp', 'mccp', 'atcp', 'zmp', 'pueblo',
        'who', 'codebase', 'vocabulary', 'staff']::text[]);

-- The review queue: submitted, alive, and not yet published.
CREATE INDEX game_awaiting_corroboration_idx ON game (submitted_at)
    WHERE submitted_at IS NOT NULL AND corroborated_at IS NULL AND state <> 'archived';
