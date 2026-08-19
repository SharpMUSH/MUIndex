-- ecosystem_snapshot: §9's protocol adoption curves, which the live dashboard has no past for
-- (it computes everything point-in-time over the current catalogue).
--
-- Stores the raw counts, not the ratio: a share's denominator/definition can change later, and
-- storing only "GMCP: 41%" would make old snapshots uncheckable and irreproducible. The share on
-- a curve is computed by the same code that computes the dashboard's share, so they can't disagree.
--
-- Counts of games, not players (§15.7 withholds the absolute population figure).
CREATE TABLE ecosystem_snapshot (
    -- Truncated to the day by the writer; a maintenance pass that runs twice must not create two
    -- points on one day.
    at           date NOT NULL,

    -- One column rather than one table per metric, since future curves share this shape.
    metric       text NOT NULL,

    key          text NOT NULL,

    -- NULL means never measured either way — distinct from zero games offering it (§3.1).
    offered      integer,

    declined     integer NOT NULL,
    handshakes   integer NOT NULL,
    declared     integer NOT NULL,
    mssp_reports integer NOT NULL,

    PRIMARY KEY (at, metric, key),

    CONSTRAINT ecosystem_snapshot_metric_vocabulary CHECK (metric IN ('protocol')),

    CONSTRAINT ecosystem_snapshot_counts_are_not_negative CHECK (
        (offered IS NULL OR offered >= 0)
        AND declined >= 0 AND handshakes >= 0 AND declared >= 0 AND mssp_reports >= 0),

    -- A numerator exceeding its denominator would draw a curve above 100%.
    CONSTRAINT ecosystem_snapshot_shares_are_shares CHECK (
        (offered IS NULL OR offered <= handshakes) AND declared <= mssp_reports)
);

CREATE INDEX ecosystem_snapshot_key_idx ON ecosystem_snapshot (metric, key, at);
