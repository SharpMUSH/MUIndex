-- Lets game_field name AresCentral as a source (§5.1). The AresMUSH community hub answers an
-- authenticated API with the games it lists, and those values are the game's own self-description
-- relayed by a third party — declared, never measured.
--
-- Precedence: below `mssp` (a game's own MSSP NAME is spoken to us directly, now; a hub entry is a
-- claim of unknown age relayed onward) and above `i3_mudlist` (AresCentral is authenticated, curated
-- by the codebase's author, and excludes In Development and long-offline games; the I3 mudlist is
-- none of those things, and carries `test` beside the real entries). Does NOT rank above `staff` or
-- `owner` — a hub does not police what a game calls itself.
--
-- field_change takes the same widening, or a source it cannot spell is not a change it can log.
--
-- No BEGIN/COMMIT: MigrationRunner opens its own transaction per script and writes the ledger
-- entry inside it.

ALTER TABLE game_field
    DROP CONSTRAINT game_field_source_vocabulary,
    ADD CONSTRAINT game_field_source_vocabulary CHECK (source IN (
        'staff', 'handshake', 'owner', 'who', 'mssp', 'ares_central', 'i3_mudlist', 'banner'));

ALTER TABLE field_change
    DROP CONSTRAINT field_change_source_vocabulary,
    ADD CONSTRAINT field_change_source_vocabulary CHECK (source IN (
        'staff', 'handshake', 'owner', 'who', 'mssp', 'ares_central', 'i3_mudlist', 'banner'));
