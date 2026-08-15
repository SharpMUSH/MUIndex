-- §9's protocol adoption CURVES, which need a past the dashboard has never kept.
--
-- EcosystemDashboard is computed live over the current catalogue, so every figure on it is
-- point-in-time and there has never been a second point to draw a line between. This is the record
-- that makes the word "curve" true.
--
-- THE COMPONENTS ARE STORED AND THE RATIO IS NOT. A snapshot holding "GMCP: 41%" would be a number
-- whose denominator is gone, uncheckable afterwards and impossible to recompute if the share is ever
-- defined differently; the same four counts the live dashboard divides are kept instead, and the
-- share on a curve is computed by the same code that computes the share on the dashboard. The two
-- therefore cannot disagree about what a share means.
--
-- These are counts of GAMES, not of players. §15.7 withholds the absolute population figure and
-- nothing here approaches it: `handshakes` is how many games we completed a handshake with, which is
-- a fact about our crawl and already published as the denominator of every share on the dashboard.
CREATE TABLE ecosystem_snapshot (
    -- Truncated to the day by the writer. One row per protocol per day: a curve wants a regular
    -- spacing more than it wants precision, and a maintenance pass that ran twice must not put two
    -- points on one day.
    at           date NOT NULL,

    -- What kind of thing `key` names. One column rather than one table per metric, because the
    -- codebase-share curve this table will hold next has exactly the same shape.
    metric       text NOT NULL,

    key          text NOT NULL,

    -- Nullable, and the nullability is the point: null means we have never measured this protocol
    -- either way, which is not nought games offering it (§3.1's rule, in a table).
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

    -- A numerator larger than its denominator is an arithmetic mistake rather than a surprising
    -- measurement, and a curve drawn from one would go above 100% on a public page.
    CONSTRAINT ecosystem_snapshot_shares_are_shares CHECK (
        (offered IS NULL OR offered <= handshakes) AND declared <= mssp_reports)
);

-- "How has this protocol moved?" — the one question the table exists to answer.
CREATE INDEX ecosystem_snapshot_key_idx ON ecosystem_snapshot (metric, key, at);
