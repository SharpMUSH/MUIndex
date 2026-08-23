-- Records which channel first brought an address into the registry, and carries it onto the game
-- the address is promoted to.
--
-- This is a fact about our crawl, not about the game: "first seen via AresCentral on this date",
-- never "this game came from AresCentral". §7.6 rejected an origin field on the grounds that a
-- game's origin is not one fact — any game worth listing is in several directories — and that
-- objection is answered by what the value is allowed to say rather than by not storing it. Nothing
-- reads this as exclusivity and no surface may render it as a badge.
--
-- Nullable with no default and no backfill. Every row that exists today predates the column, and a
-- guess would be exactly the accident §7.6 warned about; unknown renders nothing.
--
-- crawl_target's ON CONFLICT (host, port) DO UPDATE touches depth alone, so a second channel finding
-- a known address cannot overwrite the first. That is the write-once rule, and it is enforced by the
-- statement that already exists rather than by a trigger added here.
--
--
-- Validated in place rather than added NOT VALID, same as 0032 and for the same measured reason:
-- on production 2026-08-22 crawl_target holds 1,530 rows and game 921, so the ACCESS EXCLUSIVE
-- scan is milliseconds. Every existing row is NULL in a column that did not exist a statement ago,
-- which the constraint permits explicitly, so there is nothing for the scan to find.
-- No BEGIN/COMMIT: MigrationRunner opens its own transaction per script and writes the ledger
-- entry inside it.

ALTER TABLE crawl_target ADD COLUMN discovered_via text;
ALTER TABLE crawl_target ADD CONSTRAINT crawl_target_discovered_via_vocabulary CHECK (
    discovered_via IS NULL OR discovered_via IN (
        'operator_seed', 'submission', 'referral', 'i3_mudlist', 'ares_central', 'backfill'));

ALTER TABLE game ADD COLUMN discovered_via text;
ALTER TABLE game ADD CONSTRAINT game_discovered_via_vocabulary CHECK (
    discovered_via IS NULL OR discovered_via IN (
        'operator_seed', 'submission', 'referral', 'i3_mudlist', 'ares_central', 'backfill'));
