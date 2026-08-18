-- Gives "no path from here to there" a word of its own (§5.3).
--
-- `ENETUNREACH`, `EHOSTUNREACH` and `ENETDOWN` fell through `DialFailure.Classify`'s catch-all to
-- cause `error`, and `FailureReading.CauseOf` maps anything it does not recognise to `timeout`. So a
-- route that did not exist was published as a game that did not answer in time — two different facts
-- under one word, and the wrong one. 0027 made the difference visible by keeping the message; this
-- makes it sayable.
--
-- **Why this is legitimately a game's record and not our own limitation.** Rule 5 forbids recording a
-- decision of ours as a measurement of theirs, and a routing failure looks at first like ours. It is
-- not, because of what the word already means here: reachable is measured from one vantage point at
-- intervals, which is why nothing in this schema is called uptime and why "a game with a routing
-- problem to our host is unreachable and perfectly alive" is a sentence the vocabulary was built to
-- be able to say. `no_route` is that sentence with a cause attached, and the site renders it as "no
-- route from here" — naming the vantage point, in the copy, where a reader sees it.
--
-- One word for all three errnos rather than three. What separates them is a question for whoever
-- reads a particular interval, not for a six-word vocabulary the whole site reasons about, and
-- `availability_interval.detail` carries the errno for exactly that reader.
--
-- No backfill, and here it would be actively wrong: the rows already written as `timeout` recorded
-- what the crawler believed at the time, and nothing in them distinguishes a real timeout from a
-- route that did not exist — the message that would have told them apart was not kept until 0027.
-- Rows re-sort themselves as games are next probed.
--
-- No BEGIN/COMMIT: MigrationRunner opens a transaction around each script and writes the ledger entry
-- inside it, so a script opening its own commits half the work and then fails the ledger insert.

ALTER TABLE availability_interval
    DROP CONSTRAINT availability_interval_cause_vocabulary,
    ADD CONSTRAINT availability_interval_cause_vocabulary CHECK (cause IN (
        'none', 'dns', 'refused', 'tls', 'timeout', 'handshake_stalled', 'no_route'));
