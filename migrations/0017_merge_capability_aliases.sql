-- Folds game_field rows written under a capability spelling we now treat as a duplicate of
-- another (CapabilityFields.Canonical folds both on the way in; this backfills what was already
-- written so the read path never needs a second fold).
--
-- MCCP2 -> MCCP is a pure rename: nothing writes both spellings for the same field, so no
-- collision is possible.
--
-- SSL -> TLS can collide (six games declare both), so those rows are merged first: `true` wins
-- if either says so, the row keeps the earliest first_seen_at and latest last_confirmed_at, and
-- any SSL row with a matching TLS row is then dropped before the remaining SSL rows are renamed.
-- A capability value beyond a plain boolean (e.g. `SSL 4202`) survives verbatim under its new name.
--
-- field_change is untouched — it records what was recorded and when, not to be retyped for a
-- later renaming choice.
--
-- No BEGIN/COMMIT: MigrationRunner opens its own transaction per script and writes the ledger
-- entry inside it.

UPDATE game_field
   SET field = 'capability.mccp.measured'
 WHERE field = 'capability.mccp2.measured';

-- Fold each SSL row into its game's TLS row where one exists ...
UPDATE game_field tls
   SET value = CASE WHEN 'true' IN (tls.value, ssl.value) THEN 'true' ELSE tls.value END,
       first_seen_at = least(tls.first_seen_at, ssl.first_seen_at),
       last_confirmed_at = greatest(tls.last_confirmed_at, ssl.last_confirmed_at)
  FROM game_field ssl
 WHERE ssl.field = 'capability.ssl.declared'
   AND tls.field = 'capability.tls.declared'
   AND tls.game_id = ssl.game_id
   AND tls.source = ssl.source;

DELETE FROM game_field ssl
 WHERE ssl.field = 'capability.ssl.declared'
   AND EXISTS (SELECT 1 FROM game_field tls
                WHERE tls.field = 'capability.tls.declared'
                  AND tls.game_id = ssl.game_id
                  AND tls.source = ssl.source);

-- ... and rename the rest, which now have nothing to collide with.
UPDATE game_field
   SET field = 'capability.tls.declared'
 WHERE field = 'capability.ssl.declared';
