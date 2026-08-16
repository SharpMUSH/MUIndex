-- Moves capability rows written under a spelling that names a capability we already carry.
--
-- Two vocabularies reach `game_field` and they disagree about two names. The telnet handshake
-- records the option it negotiated, which is MCCP2, while MSSP's variable is the version-less MCCP;
-- and MSSP's own specification says SSL where a good many games write TLS. The dashboard tallies
-- games per field name, so each disagreement rendered as two half-empty rows: MCCP2 measured by 87
-- games and declared by none, above MCCP declared by 35 and never measured. Holding measured beside
-- declared exists so the two can be compared, and they cannot be compared across two rows.
--
-- `CapabilityFields.Canonical` now folds both on the way in. This moves what was already written, so
-- that every stored row is under the canonical name and the read path never has to fold a second
-- time — which it must not do, because folding two field names into one capability at read time
-- would add their game counts rather than merge them, and count a game offering both spellings
-- twice in its own share.
--
-- MCCP2 -> MCCP cannot collide: nothing writes `capability.mccp.measured` (the handshake only ever
-- names the option) and nothing writes `capability.mccp2.declared` (MSSP has no such variable), so
-- the update is a rename.
--
-- SSL -> TLS can, and does: six games declare both. `true` wins the merge, because a game naming an
-- encrypted port under either word has declared one and the disagreement is between our two names
-- for it rather than between two claims of theirs. The row keeps the earliest first_seen_at and the
-- latest last_confirmed_at, so neither "we have known this since" nor "we saw this again on" is
-- moved by our own renaming.
--
-- No value a game published is lost. The boolean is what merges; a capability variable carrying more
-- than a yes still writes its own descriptive row, so `SSL 4202` survives verbatim under SSL.
--
-- `field_change` is left exactly as it is. Those rows say what this site recorded and when, and a
-- game's history is not ours to retype because we later chose a different name for a column.
--
-- No BEGIN/COMMIT here, and that is not an oversight: MigrationRunner opens a transaction around
-- each script and writes the ledger entry inside it, so a script opening its own commits half the
-- work and then fails the ledger insert with "transaction is already completed".

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
