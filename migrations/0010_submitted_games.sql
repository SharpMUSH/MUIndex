-- spec §8 — a signed-in operator may add their own game, and it stays out of the listing until they
-- have proved they run it.
--
-- ONE NULLABLE COLUMN, AND IT IS NOT A LIFECYCLE STATE. `game.state` is derived from availability
-- history and never set by hand (0001), so a submission cannot live there: it is a different axis
-- entirely — how a game reached us, rather than how it has been behaving. Two axes in one column
-- would mean a submitted game that goes dark has nowhere to say both things.
--
-- The listing rule is one sentence and reads off this column alone:
--
--     a game is public if nobody submitted it, or if it has been claimed.
--
-- Which keeps §7.1's auto-listing exactly as it was — anything the crawler found for itself is
-- listed immediately as discovered-and-unclaimed — while a stranger's assertion that some address is
-- a game waits for that stranger to prove they run it.
--
-- WHY THE ASYMMETRY IS NOT ARBITRARY. Discovery is something we did: a referral we walked and a
-- resolved-address gate we applied (§7.2). A submission is somebody else pointing us at a host. If a
-- submitted game listed on sight, the form would be a way to put any address on a public page under
-- any description, and the answer to "who says this is a game?" would be "a stranger". Requiring the
-- claim makes the form mean *add your own game*, not *add somebody's*.
--
-- THIS IS NOT THE MODERATION QUEUE §3 CONDEMNS, AND THE DIFFERENCE MUST BE KEPT. The incumbents'
-- queues waited on a human at their end, which is why listings sat unapproved for a year. This waits
-- on the submitter, is settled by one probe, and has nobody in the middle. If a screen is ever added
-- where staff approve submissions, that distinction is gone and so is the argument for the feature.
ALTER TABLE game
    ADD COLUMN submitted_by uuid REFERENCES app_user (id);

-- The listing filters on this on every read, and the overwhelming majority of rows are NULL, so it
-- is worth an index only over the rows that are not.
CREATE INDEX game_submitted_by_idx ON game (submitted_by) WHERE submitted_by IS NOT NULL;

-- How many submissions one account may make in a day (§8). Recorded as a table rather than a
-- column so the bound is auditable: a burst of submissions from one account is a thing somebody
-- will want to look at, and a counter that only counts would not say what happened.
CREATE TABLE game_submission (
    id           uuid PRIMARY KEY,
    user_id      uuid NOT NULL REFERENCES app_user (id),
    game_id      uuid NOT NULL REFERENCES game (id),
    host         text NOT NULL,
    port         integer NOT NULL,
    submitted_at timestamptz NOT NULL,

    CONSTRAINT game_submission_port_is_a_port CHECK (port >= 1 AND port <= 65535)
);

CREATE INDEX game_submission_user_idx ON game_submission (user_id, submitted_at DESC);
