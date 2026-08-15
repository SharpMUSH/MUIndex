-- spec §11 — the opt-out. Three routes in (an MSSP field, a DNS TXT record, and a request an
-- operator records on somebody's behalf), one table, and nothing about it in any game's record.
--
-- THIS IS THE TABLE THAT MUST NOT BECOME A COLUMN ON crawl_target. §7.1's registry is monotonic: a
-- target is never deleted, never disabled, and never given a next_probe_at meaning never. An opt-out
-- is a standing decision of *ours* about whether to dial, so it is kept beside the registry rather
-- than inside it — and the day a game asks us back, it is one timestamp here and the schedule that
-- address has had all along.
--
-- IT IS ALSO NOT AN AVAILABILITY ROW, A PRESENCE ROW OR A FIELD. A refusal happens before a probe
-- exists (§7.2, and the same rule reads forward into §11): we declined to knock, we did not measure,
-- and a game's public reachability history may never carry a decision of ours. What an opted-out
-- game's page shows is exactly what it showed before, with nothing new arriving.
CREATE TABLE crawl_opt_out (
    id                uuid PRIMARY KEY,

    host              text NOT NULL,

    -- NULL is "every port on this host", which is what a TXT record with no port qualifier says: that
    -- record is the domain's own operator speaking about a machine they run. An MSSP field names the
    -- listener that published it and nothing else, because a hostname is not a game (§8.3) — two
    -- unrelated games on one hosting domain are separated only by port, and one of them must not be
    -- able to silence the other.
    port              integer,

    source            text NOT NULL,

    -- Two dates, because they are two facts (§8.4's shape): when they first asked, and when we last
    -- heard them say it. Only the DNS route can refresh the second, since it is the only one we can
    -- read without connecting to a server that has asked us not to.
    recorded_at       timestamptz NOT NULL,
    last_confirmed_at timestamptz NOT NULL,

    -- What we read, or who asked and how. An opt-out nobody can explain later is an opt-out somebody
    -- eventually undoes by guessing.
    detail            text NOT NULL,

    -- Set when the route that carried it withdraws it — a TXT record taken down. NEVER a DELETE: the
    -- rule that nothing is deleted covers our own decisions too, and "they asked us to stop and later
    -- asked us back" is a thing the record should be able to say.
    withdrawn_at      timestamptz,

    CONSTRAINT crawl_opt_out_port_is_a_port CHECK (port IS NULL OR port BETWEEN 1 AND 65535),
    CONSTRAINT crawl_opt_out_source_is_known CHECK (source IN ('mssp', 'dns_txt', 'request')),
    CONSTRAINT crawl_opt_out_detail_says_something CHECK (btrim(detail) <> ''),
    CONSTRAINT crawl_opt_out_confirmed_after_recorded CHECK (last_confirmed_at >= recorded_at),
    CONSTRAINT crawl_opt_out_withdrawn_after_recorded CHECK (
        withdrawn_at IS NULL OR withdrawn_at >= recorded_at),

    -- The same teeth crawl_target has, for the same reason: an opt-out recorded against a spelling
    -- the crawl loop never looks up is an opt-out that does not stop anything.
    CONSTRAINT crawl_opt_out_host_is_canonical CHECK (
        host = lower(host) AND host = btrim(host) AND host NOT LIKE '%.')
);

-- One row per address per route. NULLS NOT DISTINCT so a host-wide opt-out is one row rather than one
-- per confirmation: two routes may both be standing for one address, and neither withdraws the other.
CREATE UNIQUE INDEX crawl_opt_out_address_idx
    ON crawl_opt_out (host, port, source) NULLS NOT DISTINCT;

-- The crawl loop's question, asked once per dial: has anyone at this host asked us to stop?
CREATE INDEX crawl_opt_out_standing_idx ON crawl_opt_out (host) WHERE withdrawn_at IS NULL;
