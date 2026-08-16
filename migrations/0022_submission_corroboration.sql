-- Spec §7.8. A submitted address that the probe shows to be a game is published on sight, exactly as
-- an address the crawler found for itself is (§4.4). Before this, the form was the one discovery path
-- whose games waited for a claim — and on 16 August 2026 that was one game of 432, hidden for a
-- fortnight while answering every probe with sixty-seven players and its engine's name.
--
-- corroborated_by is what was measured at the moment it published, never a live view: the audit
-- question is "why was this published", and a column rewritten every six hours cannot answer it.
ALTER TABLE game
    ADD COLUMN corroborated_at timestamptz,
    ADD COLUMN corroborated_by text[];

-- The pair is one fact, in the idiom game_archived_games_have_a_date already uses.
ALTER TABLE game ADD CONSTRAINT game_corroboration_is_whole
    CHECK ((corroborated_at IS NULL) = (corroborated_by IS NULL)
           AND (corroborated_by IS NULL OR array_length(corroborated_by, 1) >= 1));

-- The signal names MuLikeness emits, plus 'staff' for a release made by hand from mui-crawl. A
-- vocabulary CHECK rather than a comment, so a signal renamed in C# and forgotten here fails loudly
-- on the first write instead of quietly widening what the column can mean.
ALTER TABLE game ADD CONSTRAINT game_corroboration_vocabulary
    CHECK (corroborated_by IS NULL OR corroborated_by <@ ARRAY[
        'mssp', 'gmcp', 'msdp', 'mxp', 'msp', 'mccp', 'atcp', 'zmp', 'pueblo',
        'who', 'codebase', 'vocabulary', 'staff']::text[]);

-- The queue's index: submitted, alive, and not published. Partial because the rows it excludes are
-- the whole catalogue and the rows it keeps are the handful an operator has to look at by hand.
CREATE INDEX game_awaiting_corroboration_idx ON game (submitted_at)
    WHERE submitted_at IS NOT NULL AND corroborated_at IS NULL AND state <> 'archived';
