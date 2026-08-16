-- Lets a game field name the Intermud-3 MUDLIST as its source, so a mud's I3 name can be
-- recorded (spec §5.1).
--
-- **This reverses a decision migration 0020 wrote down, deliberately and on the record.** That one
-- said game_field's vocabulary was not being widened because a mudlist entry is the mud's claim to
-- a router at its last startup, relayed onward and undated — a weaker thing than an MSSP variable
-- read off a socket we opened, and this site shows facts it measured. That reasoning was right
-- about what the value *is* and wrong about what to do with it.
--
-- What changed the answer. The seeding pass created 31 games named after an IP address, because
-- nothing else was known about them: they are LP-family, they offer no MSSP, and their connect
-- screens are login prompts with no title in them. There is no measurement of their names to be
-- had — not "not yet", but not at all through the telnet probe — and `82-153-225-173-4242` is not a
-- more honest listing than `Nightfall`, it is an unusable one. A weak source, labelled as weak, is
-- what the provenance system exists to carry; refusing the value only looked principled while
-- something else was supplying it.
--
-- **It is a separate source from `i3`, and the distinction is the measured/declared line.** An I3
-- who-reply is a list of people the mud built when we asked and WE counted the rows, which is a
-- measurement; a mudlist entry is a value the mud handed a router, which the router repeated to us.
-- Same network, two different kinds of claim, so two sources — because one enum member marked
-- measured would put "measured" on the chip beside a name nobody measured.
--
-- It ranks BELOW mssp and above banner. Below mssp because a game filling in its own MSSP NAME is
-- speaking to us directly and now, while a mudlist entry is a third party repeating what it was
-- told at some past startup. Above banner because a name lifted out of ASCII art is a guess at
-- where the title is, and this is a field the mud filled in.
--
-- It does NOT rank above `staff`. A human naming a game by hand still wins, which matters here
-- because the mudlist carries `Your MUD Name`, `test` and `DeadSouls-FluffOS2019` alongside real
-- ones — the network does not police what a mud calls itself, so neither may we treat it as final.
--
-- `field_change` takes the same widening: the change feed records who said a value changed, and a
-- source it cannot spell is a change it cannot log.
--
-- No BEGIN/COMMIT here: MigrationRunner opens a transaction around each script and writes the ledger
-- entry inside it, so a script opening its own commits half the work and then fails the ledger
-- insert with "transaction is already completed".

ALTER TABLE game_field
    DROP CONSTRAINT game_field_source_vocabulary,
    ADD CONSTRAINT game_field_source_vocabulary CHECK (source IN (
        'staff', 'handshake', 'owner', 'who', 'mssp', 'i3_mudlist', 'banner'));

ALTER TABLE field_change
    DROP CONSTRAINT field_change_source_vocabulary,
    ADD CONSTRAINT field_change_source_vocabulary CHECK (source IN (
        'staff', 'handshake', 'owner', 'who', 'mssp', 'i3_mudlist', 'banner'));
