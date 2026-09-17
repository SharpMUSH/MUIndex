-- crawl_refusal: the addresses we are declining to dial, and why (issue #185).
--
-- A refusal happens before a probe exists, so `CrawlCycle.RefuseAsync` records it through
-- `RecordAttemptAsync(succeeded: true)` — right, because the far end did not fail — and the target
-- then reads as flawless: consecutive_failures = 0, a recent last_probed_at, no availability row.
-- The only trace was a log line with about thirty minutes of retention. On 2026-09-17 two of 1,677
-- production targets were in that state (one scope refusal, one standing opt-out) and neither was
-- findable except by noticing that next_probe_at happened to be exactly seven days after
-- last_probed_at, which is an accident of the backoff rather than a record.
--
-- THIS IS OUR NOTE ABOUT OUR OWN DECISION, and that is the whole of why it may exist. Rule 5 keeps
-- it out of availability_interval, where `refused` already means an RST from a real host — a
-- measurement of them, not of us. Nothing here reaches a game page, the API, or the change feed;
-- the game_id column exists so an operator can join, never so a surface can render.
--
-- Droppable and refillable, like game_icon and icon_attempt, and for the same reason: §7.5's
-- "nothing is ever deleted" is about what a game said and when it was reachable. This is neither.
--
-- STANDING REFUSALS, NOT A LOG. A row is removed the moment we dial the address again, so the table
-- answers "what are we not dialling, right now" rather than "what has ever been refused". The
-- opposite was tried in icon_attempt's ancestor and produced a queue that could not move. The
-- history of an ask lives in crawl_opt_out, which never deletes; the history of a scope refusal is
-- the DNS answer, which is not ours to keep.
--
-- No BEGIN/COMMIT: MigrationRunner opens its own transaction per script and writes the ledger entry
-- inside it.

CREATE TABLE crawl_refusal (
    host             text NOT NULL,
    port             integer NOT NULL,

    -- NULL while the address has never been listed, which is the common case for a scope refusal:
    -- an address we will not dial rarely got far enough to become a game.
    game_id          uuid REFERENCES game (id) ON DELETE CASCADE,

    reason           text NOT NULL,

    -- What the guard or the opt-out register actually said, in its own words. Evidence for a person
    -- reading this later, never compared and never parsed.
    detail           text NOT NULL,

    first_refused_at timestamptz NOT NULL,
    last_refused_at  timestamptz NOT NULL,

    -- How many cycles have met it. A refusal standing for months and one taken this morning are
    -- different situations and the timestamps alone do not separate them from a target that is
    -- simply rarely due.
    times            integer NOT NULL DEFAULT 1,

    PRIMARY KEY (host, port),

    -- The two ways a dial is declined, matching DialRefusal. `refused` is deliberately not among
    -- them: that word belongs to availability_interval and means the far end sent an RST.
    CONSTRAINT crawl_refusal_reason_vocabulary CHECK (reason IN ('out_of_scope', 'opted_out')),
    CONSTRAINT crawl_refusal_detail_says_something CHECK (btrim(detail) <> ''),
    CONSTRAINT crawl_refusal_times_are_positive CHECK (times > 0),
    CONSTRAINT crawl_refusal_last_is_not_before_first CHECK (last_refused_at >= first_refused_at),
    CONSTRAINT crawl_refusal_port_is_a_port CHECK (port BETWEEN 1 AND 65535),
    CONSTRAINT crawl_refusal_host_is_canonical CHECK (
        host = lower(host) AND host = btrim(host) AND host NOT LIKE '%.')
);

-- The operator's question: what have we been declining, longest first.
CREATE INDEX crawl_refusal_first_refused_idx ON crawl_refusal (first_refused_at);
