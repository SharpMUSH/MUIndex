-- probe_payload: §11's short-lived probe payloads, kept as structural SHAPE (not text) so
-- parser improvements can be replayed over a recent window.
--
-- No payload text is stored: redacting player names requires finding them, which requires
-- parsing correctly — the thing that failed. What's stored is PayloadRedaction.Structural's
-- output: column positions, whitespace runs, digits and punctuation preserved, letter runs
-- masked to length. The parser is structural, so structure is all a replay needs.
--
-- Retention default is the opposite of every other table here: presence data is kept until a
-- deployment says otherwise (a measurement not taken can't be retaken), but a payload is
-- evidence, not a measurement, and decays in value within a release cycle — so the default here
-- is to drop.
CREATE TABLE probe_payload (
    -- NULL until the address is identified as a game (§7.2) — unidentified addresses are often
    -- the most interesting ones to replay.
    game_id  uuid REFERENCES game (id),

    host     text NOT NULL,
    port     integer NOT NULL,
    at       timestamptz NOT NULL,

    kind     text NOT NULL,

    -- The redaction. Never the payload.
    shape    text NOT NULL,

    PRIMARY KEY (host, port, at, kind),

    CONSTRAINT probe_payload_kind_vocabulary CHECK (kind IN ('who', 'mssp', 'banner')),
    CONSTRAINT probe_payload_port_is_a_port CHECK (port BETWEEN 1 AND 65535),
    CONSTRAINT probe_payload_host_is_canonical CHECK (
        host = lower(host) AND host = btrim(host) AND host NOT LIKE '%.')
);

-- The replay question, joined against GameField/FieldChange rows written at the same instant.
CREATE INDEX probe_payload_game_idx ON probe_payload (game_id, at);

-- The sweep's own query.
CREATE INDEX probe_payload_at_idx ON probe_payload (at);
