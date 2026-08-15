-- spec §8.4, §8.5 — what a second person publishing a token on the same server MEANS.
--
-- §8.5 says a game may have several owners, each having verified a token of their own. §8.4 says a
-- counter-claim — a different account proving control now — is the correct handling of a game
-- changing hands. Both are true, they are opposite outcomes, and NOTHING IN A PROBE CAN TELL THEM
-- APART: a co-founder joining and a new operator taking over publish the identical line in the
-- identical config file, and the crawler reads the identical string.
--
-- So the difference is not inferred, it is DECLARED, and it is declared before the token is
-- published rather than after it verifies. The claimant says which they are doing at the moment they
-- ask for a token, on a page that explains both, and the answer is stored here beside the token it
-- belongs to. A design that guessed — "a claim on an already-claimed game must be a takeover" —
-- would silently unclaim a co-founder's partner the first time two people ran one game; guessing the
-- other way would leave a departed operator holding a listing they no longer run.
--
-- Neither is more trusted than the other. Both publish a token on the server, which is the whole
-- test (§8.1), so an account choosing 'assume' has proved exactly what an account choosing 'join'
-- proved. The choice decides what happens to the OTHER claims, not how hard this one was to make.
ALTER TABLE game_claim
    ADD COLUMN intent text NOT NULL DEFAULT 'join';

ALTER TABLE game_claim
    ADD CONSTRAINT game_claim_intent_vocabulary CHECK (intent IN ('join', 'assume'));

-- Every claim that existed before this column did was a first claim on an unclaimed game, so 'join'
-- is the honest backfill: none of them displaced anybody, and the DEFAULT records that rather than
-- inventing an intent nobody expressed.
COMMENT ON COLUMN game_claim.intent IS
    'join: become one of the game''s owners. assume: take it over, revoking the others on '
    'verification (§8.4''s counter-claim). Declared when the token is issued, never inferred.';
