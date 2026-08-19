-- spec §8.3 — DNS TXT joins MSSP and the connect screen as a channel a claim token may be
-- published in. A record at _muindex.<host> carries the token qualified with the port it speaks
-- for: "muidx-…=4201".
--
-- The qualifier is required by the reader, not by this constraint, because the constraint sees only
-- the verdict. What it records is which channel proved the claim, and the three are not equally
-- strong: the other two are published by the listener being claimed, and this one by whoever
-- controls the domain — on shared MU* hosting, the host's operator rather than the game's. That is
-- why ClaimService refuses to complete an 'assume' claim over this channel: DNS may add an owner and
-- may never displace one who proved control of the server itself.
ALTER TABLE game_claim
    DROP CONSTRAINT game_claim_channel_vocabulary;

ALTER TABLE game_claim
    ADD CONSTRAINT game_claim_channel_vocabulary CHECK (
        verified_via IS NULL OR verified_via IN ('mssp', 'connect_screen', 'dns_txt'));

COMMENT ON COLUMN game_claim.verified_via IS
    'Which channel a probe or lookup read the token from. dns_txt is a port-qualified TXT record at '
    '_muindex.<host> and can only ever carry a join, never a takeover (§8.3, §8.4).';
