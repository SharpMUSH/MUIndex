-- spec §7.2, §7.6, §8, §9 — anybody may hand us an address, and an address is all we take.
--
-- The public form asks for a host and a port and nothing else, which is §7.6's rule for the backfill
-- applied to the one-at-a-time case: every fact about a game on this site is measured by this
-- crawler, so there is nothing for a submitter to assert and no column here for them to assert it
-- into. No name, no description, no codebase, no player count, and no record of who sent it.

-- THE MARKER IS A TIMESTAMP AND IT IS NOT A LIFECYCLE STATE. `game.state` is derived from
-- availability history and never set by hand (0001), so a submission cannot live there: it is a
-- different axis entirely — how a game reached us, rather than how it has been behaving. Two axes in
-- one column would mean a submitted game that goes dark has nowhere to say both things.
--
-- The listing rule is one sentence and reads off this column alone:
--
--     a game is public if nobody submitted it, or if it has been claimed.
--
-- Which keeps §7.1's auto-listing exactly as it was — anything the crawler found for itself is listed
-- immediately as discovered-and-unclaimed — while a stranger's assertion that some address is a game
-- waits for somebody to prove they run it.
--
-- WHY THE ASYMMETRY IS NOT ARBITRARY. Discovery is something we did: a referral we walked and a
-- resolved-address gate we applied (§7.2). A submission is somebody else pointing us at a host. If a
-- submitted game listed on sight, the form would be a way to put any address on a public page, and
-- the answer to "who says this is a game?" would be "a stranger with a browser".
--
-- THIS IS NOT THE MODERATION QUEUE §3 CONDEMNS, AND THE DIFFERENCE MUST BE KEPT. The incumbents'
-- queues waited on a human at their end, which is why listings sat unapproved for a year. This waits
-- on the game's own operator, is settled by one probe, and has nobody in the middle. If a screen is
-- ever added where staff approve submissions, that distinction is gone and so is the argument for the
-- feature.
ALTER TABLE game
    ADD COLUMN submitted_at timestamptz;

-- The listing filters on this on every read, and the overwhelming majority of rows are NULL, so it is
-- worth an index only over the rows that are not.
CREATE INDEX game_submitted_at_idx ON game (submitted_at) WHERE submitted_at IS NOT NULL;

-- The same fact one step earlier, on the address rather than on the game.
--
-- A submission creates no game: §7.1's promotion is unchanged, and a game exists when a host answers
-- for itself. So the marker has to be carried by the thing that exists in the meantime — the crawl
-- target — and copied onto the game at the moment CatalogueBinder mints one.
--
-- NOTHING EVER SETS THIS ON A TARGET THAT ALREADY EXISTS, and that is a security property rather than
-- an optimisation. `ICrawlTargetRepository.AddAsync` collapses onto the existing row, so submitting an
-- address we already crawl changes nothing at all — otherwise the form would be a way to hide any
-- listed game on the site by naming it.
ALTER TABLE crawl_target
    ADD COLUMN submitted_at timestamptz;

-- Every submission this deployment received, and what we did about it.
--
-- WHAT THIS IS FOR. An unauthenticated form needs a bound, the bound needs a counter, and a counter
-- that only counts cannot say what happened: a burst of submissions is a thing somebody will want to
-- look at, and "247" is not a thing anybody can look at. So the rate limit reads this table and the
-- table keeps the rows.
--
-- NONE OF THIS IS A MEASUREMENT OF A GAME, AND NOTHING HERE REFERENCES ONE. A submission we refused
-- under §7.2 is a decision of ours about where our own socket may go; recording it against a game
-- would be the same class of lie as recording a scope refusal as downtime. There is no game_id column
-- to write it into, which is how that is enforced rather than remembered.
CREATE TABLE game_submission (
    id             uuid PRIMARY KEY,

    -- NULL when nothing usable was submitted at all. A form somebody typed a sentence into still has
    -- to count against the bound, or the bound is bypassed by sending rubbish.
    host           text,
    port           integer,

    submitted_at   timestamptz NOT NULL,

    -- What we did. Refusals are here and in no game's record.
    outcome        text NOT NULL,

    -- The registry row this became, when it became one. NULL for every other outcome.
    crawl_target_id uuid REFERENCES crawl_target (id),

    -- §11 — WE DO NOT STORE THE SUBMITTER'S ADDRESS. This is a salted hash of it, with the salt
    -- generated per process and never written down, exactly as §11's player aggregates work: the
    -- rate limit needs to know that two submissions came from one place within the hour, and needs
    -- nothing else, ever. The salt rotating on every restart is the point — it is more often than the
    -- window is long, so these stop being comparable well before they stop being rows.
    source         text NOT NULL,

    CONSTRAINT game_submission_address_is_whole CHECK ((host IS NULL) = (port IS NULL)),
    CONSTRAINT game_submission_port_is_a_port CHECK (port IS NULL OR port BETWEEN 1 AND 65535),
    CONSTRAINT game_submission_host_is_bounded CHECK (host IS NULL OR length(host) <= 253),
    CONSTRAINT game_submission_source_is_a_digest CHECK (source ~ '^[0-9a-f]{64}$'),
    CONSTRAINT game_submission_outcome_vocabulary CHECK (outcome IN (
        'accepted', 'already_listed', 'already_queued',
        'malformed', 'refused_not_routable', 'unresolvable')),

    -- An accepted submission is the only one that has a registry row to point at, and it always has
    -- one. Stated here so a handler that forgot to record which target it created fails at the
    -- database rather than leaving a row nobody can trace.
    CONSTRAINT game_submission_accepted_names_its_target CHECK (
        (outcome = 'accepted') = (crawl_target_id IS NOT NULL))
);

-- The rate limit's one hot query: how many submissions from this source since a moment.
CREATE INDEX game_submission_source_idx ON game_submission (source, submitted_at DESC);

-- "Who has been submitting this address, and how often?" — the question a burst raises.
CREATE INDEX game_submission_address_idx ON game_submission (host, port, submitted_at DESC)
    WHERE host IS NOT NULL;
