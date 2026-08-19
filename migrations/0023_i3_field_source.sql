-- Lets game_field name the Intermud-3 MUDLIST as a source (§5.1), reversing 0020's decision not
-- to record it — I3-only games (LP-family, no MSSP, no parseable connect-screen title) have no
-- other way to get a real name.
--
-- Separate source from `i3`: an I3 who-reply is measured (a list we built and counted
-- ourselves); a mudlist name is declared (a value the mud handed a router at some past startup,
-- relayed to us).
--
-- Precedence: below `mssp` (a game's own MSSP NAME is spoken to us directly, now; a mudlist
-- entry is a third party repeating a past claim) and above `banner` (a mudlist name is filled in
-- by the mud; a banner name is a guess at where the title is in ASCII art). Does NOT rank above
-- `staff` — the mudlist carries junk names like `test` alongside real ones, so a human correction
-- still wins.
--
-- field_change takes the same widening, so a source it can't spell isn't a change it can log.
--
-- No BEGIN/COMMIT: MigrationRunner opens its own transaction per script and writes the ledger
-- entry inside it.

ALTER TABLE game_field
    DROP CONSTRAINT game_field_source_vocabulary,
    ADD CONSTRAINT game_field_source_vocabulary CHECK (source IN (
        'staff', 'handshake', 'owner', 'who', 'mssp', 'i3_mudlist', 'banner'));

ALTER TABLE field_change
    DROP CONSTRAINT field_change_source_vocabulary,
    ADD CONSTRAINT field_change_source_vocabulary CHECK (source IN (
        'staff', 'handshake', 'owner', 'who', 'mssp', 'i3_mudlist', 'banner'));
