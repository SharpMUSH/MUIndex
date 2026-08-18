-- spec §8.4, §8.5 — declares whether a new claim on an already-claimed game is a co-owner
-- joining or a takeover. A probe can't tell these apart (both publish the identical token), so
-- the claimant states which at the moment they request a token, and it's stored beside it.
ALTER TABLE game_claim
    ADD COLUMN intent text NOT NULL DEFAULT 'join';

ALTER TABLE game_claim
    ADD CONSTRAINT game_claim_intent_vocabulary CHECK (intent IN ('join', 'assume'));

-- Every pre-existing claim was a first claim on an unclaimed game, so 'join' is the honest
-- backfill default.
COMMENT ON COLUMN game_claim.intent IS
    'join: become one of the game''s owners. assume: take it over, revoking the others on '
    'verification (§8.4''s counter-claim). Declared when the token is issued, never inferred.';
