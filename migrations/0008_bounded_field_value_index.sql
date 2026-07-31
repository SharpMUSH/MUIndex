-- The (field, value) index could not hold a connect screen, and a game whose screen was long enough
-- failed to ingest at all.
--
-- Observed on the first crawl large enough to find it: three of four hundred games died with
-- "54000: index row size 3048 exceeds btree version 4 maximum 2704 for index
-- game_field_field_value_idx". PostgreSQL's btree cannot index a row wider than about 2704 bytes,
-- and a connect screen is routinely thousands of characters — the longest in this catalogue is 9,376.
-- The failure is not partial: the INSERT is refused, so the whole probe's ingestion is lost for that
-- game, and it is lost again on every future probe, for ever. A game with a generous piece of ASCII
-- art was permanently unlistable.
--
-- The index's stated purpose (0002) is §9's faceted search: which games have CODEBASE = PennMUSH, or
-- capability.gmcp.measured = true. Every value that purpose looks up is short. NOTHING HAS EVER
-- SEARCHED BY CONNECT SCREEN and nothing ever will — it is a display asset and a fingerprint, and
-- the fingerprint has its own column. So the index covers a bounded prefix, which serves the lookups
-- it was built for and cannot overflow: 256 characters is under the limit even at four bytes each.
--
-- The stored value is untouched. Truncating what a game said in order to fit our own index would be
-- exactly the kind of quiet lossiness this schema refuses everywhere else; it is the *index* that is
-- bounded, not the fact.
DROP INDEX IF EXISTS game_field_field_value_idx;

CREATE INDEX game_field_field_value_idx ON game_field (field, left(value, 256));

-- The same flaw, one index over: §7.3's identity lookup folds case and whitespace on both columns,
-- and folding does not shorten a connect screen. This one is partial rather than prefixed, because
-- its reader asks an equality question and a prefix would silently turn that into a
-- starts-with — over-matching where the raw index merely refused. Every identity signal §7.3 names
-- is short: a name, a year, a hostname, a hash, a token. A value longer than this is not one of
-- them, so excluding it from the lookup changes no correct answer.
--
-- CatalogueDirectories carries the same predicate, or the planner cannot use a partial index.
DROP INDEX IF EXISTS game_field_folded_value_idx;

CREATE INDEX game_field_folded_value_idx
    ON game_field (lower(btrim(field)), lower(btrim(value)))
 WHERE length(value) <= 256;
