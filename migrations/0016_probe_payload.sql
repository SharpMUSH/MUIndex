-- §11's short-lived probe payloads, kept so parser improvements can be replayed over a recent
-- window — and kept as SHAPE rather than as text.
--
-- §11 asks for raw payloads redacted of names. Those two cannot both be had: the payload worth
-- replaying is the WHO, because that is where the parser fails, and it is also the only place a
-- player name appears. Redacting names out of it means locating them, which means parsing it
-- correctly, which is what failed. The payloads most worth keeping are exactly the ones a
-- name-finding redactor cannot clean.
--
-- So no payload text is stored. What is stored is PayloadRedaction.Structural's output: every column
-- position, every run of whitespace, every digit and every punctuation mark where it was, with each
-- run of letters masked to the same length unless it is a word the parser itself reads. The parser
-- is structural, so structure is the whole of what a replay needs.
--
-- THIS TABLE'S RETENTION DEFAULT IS THE OPPOSITE OF EVERY OTHER ONE HERE. Presence keeps everything
-- until a deployment says otherwise, because a measurement not taken cannot be taken again. A
-- payload is not a measurement — it is the evidence a measurement was read out of, and its value
-- decays to nothing within a release cycle. Between an unsettled number and a deletion the
-- conservative default for a measurement is to keep; for this it is to drop.
CREATE TABLE probe_payload (
    -- Null while the address has not been identified as a game (§7.2). Those probes are the most
    -- interesting ones to replay — an unidentified address is often one we could not read — so the
    -- column is nullable rather than the row being skipped.
    game_id  uuid REFERENCES game (id),

    host     text NOT NULL,
    port     integer NOT NULL,
    at       timestamptz NOT NULL,

    -- Which exchange this is the shape of. One row per kind per probe.
    kind     text NOT NULL,

    -- The redaction. Never the payload.
    shape    text NOT NULL,

    PRIMARY KEY (host, port, at, kind),

    CONSTRAINT probe_payload_kind_vocabulary CHECK (kind IN ('who', 'mssp', 'banner')),
    CONSTRAINT probe_payload_port_is_a_port CHECK (port BETWEEN 1 AND 65535),
    CONSTRAINT probe_payload_host_is_canonical CHECK (
        host = lower(host) AND host = btrim(host) AND host NOT LIKE '%.')
);

-- "What did this game's probes look like lately?" — the replay question, and the join to the
-- GameField and FieldChange rows written at the same instant.
CREATE INDEX probe_payload_game_idx ON probe_payload (game_id, at);

-- The sweep's own question, and the only one it asks.
CREATE INDEX probe_payload_at_idx ON probe_payload (at);
