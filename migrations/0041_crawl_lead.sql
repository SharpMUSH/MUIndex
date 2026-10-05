-- crawl_lead: addresses staff's lead routine read out of public announcements, and what became of
-- each one.
--
-- The routine is a model reading forum posts (r/MUD and the like), search results, and the game
-- websites those link to. What it hands in through `crawl_lead_add` is an address and the page it was
-- read from — nothing about the game. The target it writes carries submitted_at, so the address has to
-- identify itself before CatalogueBinder mints a game and has to pass §7.8 before the listing shows
-- one, exactly as a stranger's form submission does. A model that misreads a web server's port costs
-- one probe and puts nothing on a page.
--
-- THIS IS OUR NOTE ABOUT OUR OWN CRAWL, on the crawl_refusal and game_submission precedent. The
-- evidence URL says which page we happened to read first, and that is not a fact about the game: a
-- game announced on one forum is announced on three, which is §7.6's whole objection to an origin
-- field. So nothing here reaches a game page, the API or the change feed, and discovered_via says only
-- 'announcement', never which site. crawl_target_id exists so an operator can join, never so a
-- surface can render.
--
-- A log, not standing state: unlike crawl_refusal, a row is never removed, because the routine reads
-- it back to skip pages it has already handed in, and the bound counts it.
--
-- No BEGIN/COMMIT: MigrationRunner opens its own transaction per script and writes the ledger entry
-- inside it.

ALTER TABLE crawl_target DROP CONSTRAINT crawl_target_discovered_via_vocabulary;
ALTER TABLE crawl_target ADD CONSTRAINT crawl_target_discovered_via_vocabulary CHECK (
    discovered_via IS NULL OR discovered_via IN (
        'operator_seed', 'submission', 'referral', 'i3_mudlist', 'ares_central', 'announcement',
        'backfill'));

ALTER TABLE game DROP CONSTRAINT game_discovered_via_vocabulary;
ALTER TABLE game ADD CONSTRAINT game_discovered_via_vocabulary CHECK (
    discovered_via IS NULL OR discovered_via IN (
        'operator_seed', 'submission', 'referral', 'i3_mudlist', 'ares_central', 'announcement',
        'backfill'));

CREATE TABLE crawl_lead (
    id              uuid PRIMARY KEY,

    -- NULL while the row is only the reservation, and for a lead whose address could not be read.
    host            text,
    port            integer,

    -- The page the address was read from, word for word.
    evidence_url    text NOT NULL,

    -- The announcement, when the address was found one link further on, on the game's own site.
    post_url        text,

    -- Where the routine was reading: 'reddit', 'gemini', … A label for an operator, never compared.
    channel         text NOT NULL,

    found_at        timestamptz NOT NULL,

    -- 'pending' reserves the slot before the outcome is known, under an advisory lock, so a burst
    -- can't pass a stale count. The rest is game_submission's vocabulary: the two doors share one
    -- sequence of checks (AddressIntake), so they share its answers.
    outcome         text NOT NULL,

    crawl_target_id uuid REFERENCES crawl_target (id),

    CONSTRAINT crawl_lead_address_is_whole CHECK ((host IS NULL) = (port IS NULL)),
    CONSTRAINT crawl_lead_port_is_a_port CHECK (port IS NULL OR port BETWEEN 1 AND 65535),
    CONSTRAINT crawl_lead_host_is_bounded CHECK (host IS NULL OR length(host) <= 253),
    CONSTRAINT crawl_lead_evidence_is_a_web_page CHECK (
        evidence_url ~ '^https?://' AND length(evidence_url) <= 2048),
    CONSTRAINT crawl_lead_post_is_a_web_page CHECK (
        post_url IS NULL OR (post_url ~ '^https?://' AND length(post_url) <= 2048)),
    CONSTRAINT crawl_lead_channel_is_a_label CHECK (channel ~ '^[a-z0-9_-]{1,32}$'),
    CONSTRAINT crawl_lead_outcome_vocabulary CHECK (outcome IN (
        'pending',
        'accepted', 'already_listed', 'already_queued',
        'malformed', 'refused_not_routable', 'unresolvable', 'refused_opt_out')),
    CONSTRAINT crawl_lead_accepted_names_its_target CHECK (
        (outcome = 'accepted') = (crawl_target_id IS NOT NULL))
);

-- The bound's count and the routine's "what have I already handed in", both newest first.
CREATE INDEX crawl_lead_found_at_idx ON crawl_lead (found_at DESC);
