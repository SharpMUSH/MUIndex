-- presence_sample: the only table that grows linearly with games × time, so it is the only
-- partitioned one — RANGE on `at`, monthly, so retention/rollups (§5.2, migration 0011) work on
-- whole partitions rather than row-by-row deletes.
--
-- No DEFAULT partition: a row landing there would block creating the partition that should
-- have held it. NpgsqlPresenceStore creates the month's partition before every append instead.
CREATE TABLE presence_sample (
    game_id             uuid NOT NULL REFERENCES game (id),
    at                  timestamptz NOT NULL,

    -- §5.4: NULL is "probed, could not count" (hatched); 0 is "probed, counted zero" (filled).
    count               integer,
    source              text NOT NULL,
    unmeasurable_reason text,

    -- §5.2/§11: idle-time histogram + unique-player estimate from salted rotating hashes.
    -- Populated only when the WHO parser reaches per-player confidence (§6.3). Never names.
    aggregates          jsonb,

    -- Partitioned table's PK must contain the partition key.
    PRIMARY KEY (game_id, at),

    -- Player count is not a GameField and doesn't use §5.1's ladder: `who` outranks `mssp`
    -- here. `banner` is included because some games publish counts only on the connect screen.
    CONSTRAINT presence_sample_source_vocabulary CHECK (source IN (
        'who', 'mssp', 'banner')),

    CONSTRAINT presence_sample_reason_vocabulary CHECK (unmeasurable_reason IS NULL OR unmeasurable_reason IN (
        'who_unparseable', 'who_not_offered', 'players_not_numeric')),

    -- The load-bearing constraint (§5.4): a NULL count must carry a reason, a counted cell must
    -- not. The third state — probe failed entirely — is the absence of a row, not a value here.
    CONSTRAINT presence_sample_null_count_has_a_reason CHECK (
        (count IS NULL) = (unmeasurable_reason IS NOT NULL)),

    CONSTRAINT presence_sample_count_is_not_negative CHECK (count IS NULL OR count >= 0)
) PARTITION BY RANGE (at);

-- Serves the ecosystem dashboard, which reads every game over a date window (vs. one game,
-- served by the PK plus partition pruning).
CREATE INDEX presence_sample_at_idx ON presence_sample (at);
