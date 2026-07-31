-- spec §5.2 — the only table in this schema that grows linearly with games × time, which is why it is
-- the only partitioned one. RANGE on `at`, monthly, so §5.2's retention and rollups can later work on
-- whole partitions rather than row-by-row deletes over hundreds of millions of rows.
--
-- There is no DEFAULT partition, deliberately: rows landing in one would block the later creation of
-- the partition that should have held them. NpgsqlPresenceStore creates the month's partition before
-- every append instead, because a crawler is not entitled to lose a measurement to a calendar
-- rollover.
CREATE TABLE presence_sample (
    game_id             uuid NOT NULL REFERENCES game (id),
    at                  timestamptz NOT NULL,

    -- §5.4, and the load-bearing nullability in this schema. NULL is "we got in and could not count",
    -- which is a hatched cell; 0 is "we got in and nobody was there", which is a filled one.
    count               integer,
    source              text NOT NULL,
    unmeasurable_reason text,

    -- §5.2/§11: idle-time histogram buckets and a unique-player estimate derived from salted rotating
    -- hashes. Populated only when the WHO parser reached per-player confidence (§6.3). Never names.
    aggregates          jsonb,

    -- A partitioned table's primary key must contain the partition key.
    PRIMARY KEY (game_id, at),

    -- Player count is not a GameField and does not use §5.1's ladder: it lives here, where `who`
    -- outranks `mssp`. `banner` is in this list because several games publish their count only on the
    -- connect screen, and that is still a measurement of ours.
    CONSTRAINT presence_sample_source_vocabulary CHECK (source IN (
        'who', 'mssp', 'banner', 'imported_measured')),

    CONSTRAINT presence_sample_reason_vocabulary CHECK (unmeasurable_reason IS NULL OR unmeasurable_reason IN (
        'who_unparseable', 'who_not_offered', 'players_not_numeric')),

    -- The most important constraint in this schema (§5.4). A NULL count is a probed-but-unmeasurable
    -- cell and must say why; a counted cell must not carry a reason. Those are two different facts
    -- with two different renderings, and the schema keeps them apart so that no writer can quietly
    -- produce a third thing. The third state — the probe failed — is the ABSENCE of a row here, and
    -- reaches the availability series instead.
    CONSTRAINT presence_sample_null_count_has_a_reason CHECK (
        (count IS NULL) = (unmeasurable_reason IS NOT NULL)),

    -- Parsers never fabricate, and they never go negative either.
    CONSTRAINT presence_sample_count_is_not_negative CHECK (count IS NULL OR count >= 0)
) PARTITION BY RANGE (at);

-- §9's heatmap and trend lines read one game over a date window, which the primary key plus partition
-- pruning already serve. This one is for the ecosystem dashboard, which reads every game over a
-- window and would otherwise scan each partition whole.
CREATE INDEX presence_sample_at_idx ON presence_sample (at);
