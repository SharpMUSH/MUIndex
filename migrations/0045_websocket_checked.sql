-- crawl_target.websocket_checked_at: when the WebSocket URL this address's game declares was last
-- dialled to see whether it answers telnet.
--
-- A game's MSSP may name a WEBSOCKET URL (the mudstandards.org proposal; TelnetNegotiationCore's
-- MSSPConfig.WebSocket). That is a claim, written capability.websocket.declared. The crawl loop checks
-- it the way the handshake checks GMCP, by dialling the URL and reading the telnet session behind it,
-- and writes capability.websocket.measured. Measured beats declared, and both are shown (rule 1).
--
-- The check is a second dial to somebody's server, so it is not repeated on every crawl: this column
-- spaces it out (DiscoveryOptions.WebSocketCheckInterval). NULL means never checked.
--
-- Our note about our own schedule, never rendered. Kept on the address rather than the game because
-- the URL arrived in this listener's report.
--
-- No BEGIN/COMMIT: MigrationRunner opens its own transaction per script and writes the ledger entry
-- inside it.

ALTER TABLE crawl_target
    ADD COLUMN websocket_checked_at timestamptz;

COMMENT ON COLUMN crawl_target.websocket_checked_at IS
    'When the WebSocket URL this address''s MSSP declares was last dialled to check it answers telnet. '
    'NULL: never checked. Spaces the check out; never rendered.';
