-- spec §7.6 — "every imported value carries the originating site and the import date in its
-- provenance chip, and the about page names every source we ingested".
--
-- Nothing already stored can say that. A game_field row carries a FieldSource, a presence_sample a
-- FieldSource, an availability_interval a tier-valued `origin` — and none of the three names a site
-- or a date. This is the sidecar that does, and it exists for the chip and the attribution list.
--
-- THE WRITER DOES NOT LIVE IN THIS REPOSITORY, AND THE TABLE STILL DOES. The backfill importer is a
-- one-time tool run once against the one deployment and is parked on a branch outside main (spec
-- §7.6). This migration stays because the rows it defines outlive the tool that wrote them: a game
-- whose GENRE came from MudStats says so on its page for as long as that value stands, and dropping
-- the table would turn a provenance chip into an unattributed fact. A migration is a statement about
-- the shape of data that exists, not about which code happens to be checked out.
--
-- IT IS NOT ON THE GRACE PATH. §7.5's half weight is computed from availability_interval.origin by
-- ArchivePolicy.GraceFor, in ArchiveSweeper, and nowhere else. A second calculator reading these rows
-- would count the same history twice.
CREATE TABLE import_provenance (
    id           bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    game_id      uuid NOT NULL REFERENCES game (id),

    -- What the imported value was about: a field, an endpoint, a presence reading, a reachable span.
    subject_kind text NOT NULL,

    -- The field name for a field, 'host:port' for an endpoint, null for a dated history row, whose
    -- identity is subject_at instead.
    subject_key  text,

    -- The instant the third party's measurement is about. Null for a field or an endpoint.
    subject_at   timestamptz,

    -- The originating site, and its own identifier for the game, so a value can be traced back to the
    -- page it was read from.
    source_name  text NOT NULL,
    source_key   text NOT NULL,
    source_uri   text,

    -- Which of §7.6's two tiers the site is. Stored beside the site's name rather than derived from
    -- it, because the tier is what a re-import must not silently change.
    tier         text NOT NULL,

    -- When WE read it. Half of §7.6's requirement, and the half nothing else records.
    imported_at  timestamptz NOT NULL,

    CONSTRAINT import_provenance_subject_vocabulary CHECK (subject_kind IN (
        'field', 'endpoint', 'presence', 'availability')),

    CONSTRAINT import_provenance_tier_vocabulary CHECK (tier IN (
        'imported_measured', 'imported_asserted')),

    -- A history row is identified by the instant it is about and a field by its name. Requiring
    -- exactly one of the two is what makes the idempotence lookup below total: without it a row could
    -- carry neither and match nothing, so a re-import would write it again for ever.
    CONSTRAINT import_provenance_has_one_subject CHECK (
        (subject_key IS NOT NULL AND subject_at IS NULL)
     OR (subject_key IS NULL AND subject_at IS NOT NULL)),

    -- Only a measured source may ever have produced a history row. The tier rule of §7.6 is enforced
    -- in code by AssertedHistorySink holding nothing it could write with; this is the same rule
    -- written where a mistaken direct INSERT would also hit it.
    CONSTRAINT import_provenance_asserted_sources_have_no_history CHECK (
        tier = 'imported_measured' OR subject_kind IN ('field', 'endpoint'))
);

-- The idempotence question, asked once per candidate row by every re-run: has this site already given
-- us this subject for this game? Re-running the backfill must change nothing (spec §7.6's import is
-- one-off but re-runnable), and this index is what keeps that from being a table scan per value.
CREATE UNIQUE INDEX import_provenance_subject_idx
    ON import_provenance (game_id, source_name, subject_kind, COALESCE(subject_key, ''), COALESCE(subject_at, 'epoch'));

-- The about page's attribution list: which sites did we actually ingest, and how much of each.
CREATE INDEX import_provenance_source_idx ON import_provenance (source_name, imported_at);
