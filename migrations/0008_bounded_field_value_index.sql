-- Bounds two indexes on game_field so a long connect screen can't overflow them.
--
-- PostgreSQL's btree cannot index a row wider than ~2704 bytes, and a connect screen routinely
-- runs to thousands of characters; an oversized row makes the INSERT fail outright, which loses
-- that game's ingestion permanently. Neither index needs the full value: game_field_field_value_idx
-- serves §9's faceted search (short values only, e.g. CODEBASE = PennMUSH); the fingerprint used
-- for connect-screen matching lives in its own column. The stored value itself is untouched —
-- only the indexes are bounded.
DROP INDEX IF EXISTS game_field_field_value_idx;

CREATE INDEX game_field_field_value_idx ON game_field (field, left(value, 256));

-- Same flaw in §7.3's folded identity lookup. Partial (WHERE length <= 256) rather than
-- prefixed, because the lookup is an equality match and a prefix would turn it into a
-- starts-with. Every identity signal §7.3 names (name, year, hostname, hash, token) is short.
--
-- CatalogueDirectories must repeat this predicate verbatim, or the planner can't use the index.
DROP INDEX IF EXISTS game_field_folded_value_idx;

CREATE INDEX game_field_folded_value_idx
    ON game_field (lower(btrim(field)), lower(btrim(value)))
 WHERE length(value) <= 256;
