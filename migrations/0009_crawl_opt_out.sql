-- crawl_opt_out: the opt-out (§11). Three routes in (MSSP field, DNS TXT record, or an
-- operator-recorded request), one table, kept separate from crawl_target and outside a game's
-- own record.
--
-- Not a column on crawl_target: the registry is monotonic (§7.1, never disabled), while an
-- opt-out is a standing decision of ours about whether to dial. Not an availability/presence
-- row or field either — a refusal happens before a probe exists, so it must never appear in a
-- game's reachability history (§7.2, §11).
CREATE TABLE crawl_opt_out (
    id                uuid PRIMARY KEY,

    host              text NOT NULL,

    -- NULL means every port on this host (what an unqualified TXT record says). An MSSP field
    -- names only the listener that published it, since one host can carry unrelated games on
    -- different ports.
    port              integer,

    source            text NOT NULL,

    -- Two facts: when first asked, and when we last confirmed it still stands. Only the DNS
    -- route can refresh the second without connecting to a server that asked us not to.
    recorded_at       timestamptz NOT NULL,
    last_confirmed_at timestamptz NOT NULL,

    detail            text NOT NULL,

    -- Set when the route withdraws it (e.g. TXT record removed). Never a DELETE — an opt-out
    -- later rescinded is still part of the record.
    withdrawn_at      timestamptz,

    CONSTRAINT crawl_opt_out_port_is_a_port CHECK (port IS NULL OR port BETWEEN 1 AND 65535),
    CONSTRAINT crawl_opt_out_source_is_known CHECK (source IN ('mssp', 'dns_txt', 'request')),
    CONSTRAINT crawl_opt_out_detail_says_something CHECK (btrim(detail) <> ''),
    CONSTRAINT crawl_opt_out_confirmed_after_recorded CHECK (last_confirmed_at >= recorded_at),
    CONSTRAINT crawl_opt_out_withdrawn_after_recorded CHECK (
        withdrawn_at IS NULL OR withdrawn_at >= recorded_at),

    CONSTRAINT crawl_opt_out_host_is_canonical CHECK (
        host = lower(host) AND host = btrim(host) AND host NOT LIKE '%.')
);

-- One row per address per route. NULLS NOT DISTINCT so a host-wide (NULL port) opt-out is one
-- row, and two routes standing for the same address don't collide or overwrite each other.
CREATE UNIQUE INDEX crawl_opt_out_address_idx
    ON crawl_opt_out (host, port, source) NULLS NOT DISTINCT;

-- The crawl loop's per-dial question: has anyone at this host asked us to stop?
CREATE INDEX crawl_opt_out_standing_idx ON crawl_opt_out (host) WHERE withdrawn_at IS NULL;
