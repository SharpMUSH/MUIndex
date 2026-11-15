-- game_endpoint gains a path, and a kind for a WebSocket behind TLS.
--
-- A WebSocket endpoint is a URL, not a host and port: MUME answers at wss://mume.org/ws-play/, and
-- the path is as much the address as the port is. 'websocket' was in the vocabulary from 0005 and
-- never written; 'wss' is added beside it rather than a tls flag on every row, because for telnet the
-- kind already says whether a TLS handshake completed ('tls'), and a WebSocket should say the same
-- thing the same way. The CHECK ties the two together: a WebSocket kind always has a path, and no
-- other kind has one.
--
-- The address stays (host, port) for identity (game_endpoint_address_idx). Two games behind one
-- gateway on one host and port, told apart only by path, would collide on it; no such pair has been
-- observed, and the writer refuses an address another game holds rather than taking it.
ALTER TABLE game_endpoint ADD COLUMN path text;

-- Nothing has written 'websocket' yet, but a row that did would otherwise fail the CHECK below.
UPDATE game_endpoint SET path = '/' WHERE kind = 'websocket' AND path IS NULL;

ALTER TABLE game_endpoint DROP CONSTRAINT game_endpoint_kind_vocabulary;
ALTER TABLE game_endpoint ADD CONSTRAINT game_endpoint_kind_vocabulary
    CHECK (kind IN ('telnet', 'tls', 'websocket', 'wss', 'http'));

ALTER TABLE game_endpoint ADD CONSTRAINT game_endpoint_path_only_on_websockets
    CHECK ((path IS NOT NULL) = (kind IN ('websocket', 'wss')));

-- What a request line can carry and nothing else: a leading slash, printable ASCII without spaces,
-- and a bound. The value comes from a game's MSSP, so it is somebody else's text.
ALTER TABLE game_endpoint ADD CONSTRAINT game_endpoint_path_is_a_path
    CHECK (path IS NULL OR (path ~ '^/[!-~]*$' AND length(path) <= 256));
