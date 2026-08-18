-- app_user, user_passkey, game_claim, claim_event: accounts, passkeys, and claims (§8).

-- §8.2 — an account, deliberately minimal.
--
-- No email or password columns: sign-in is passkeys only. Recovery is a new account
-- re-verified through the game, not a reset flow — the root of trust is the server the operator
-- controls (§8.2).
CREATE TABLE app_user (
    id              uuid PRIMARY KEY,

    -- Chosen by the account holder; not an identity claim and never verified as one.
    display_name    text NOT NULL,

    -- Case/diacritic-insensitive lookup key, unique.
    normalised_name text NOT NULL,

    -- Identity's optimistic-concurrency token; ours to store, not to interpret.
    security_stamp  text NOT NULL,
    concurrency_stamp text NOT NULL,

    created_at      timestamptz NOT NULL,
    last_signed_in_at timestamptz,

    CONSTRAINT app_user_name_is_not_blank CHECK (btrim(display_name) <> ''),
    CONSTRAINT app_user_normalised_name_is_canonical CHECK (
        normalised_name = upper(normalised_name) AND normalised_name = btrim(normalised_name))
);

CREATE UNIQUE INDEX app_user_normalised_name_idx ON app_user (normalised_name);

-- §8.2 — one WebAuthn credential.
CREATE TABLE user_passkey (
    credential_id       bytea PRIMARY KEY,
    user_id             uuid NOT NULL REFERENCES app_user (id) ON DELETE CASCADE,

    public_key          bytea NOT NULL,

    -- Replay protection: a presented counter no higher than stored means cloned/replayed.
    sign_count          bigint NOT NULL DEFAULT 0,

    -- Synced-to-provider vs. device-only. Drives the dashboard's "add a second passkey" nudge.
    is_backed_up        boolean NOT NULL DEFAULT false,
    is_backup_eligible  boolean NOT NULL DEFAULT false,

    -- Stored as an array (not re-delimited text) so a round trip can't silently reorder it.
    transports          text[],

    is_user_verified    boolean NOT NULL DEFAULT false,

    -- Kept for the AAGUID: if an authenticator model is later found compromised, affected
    -- credentials need to be findable. We do not validate attestation statements.
    attestation_object  bytea,

    client_data_json    bytea,

    -- Bounded rather than free text: a person with several passkeys needs to tell them apart
    -- (§8's resource-limit note).
    name                text,

    created_at          timestamptz NOT NULL,
    last_used_at        timestamptz,

    CONSTRAINT user_passkey_name_is_bounded CHECK (name IS NULL OR length(name) <= 64),
    CONSTRAINT user_passkey_sign_count_is_not_negative CHECK (sign_count >= 0)
);

CREATE INDEX user_passkey_user_idx ON user_passkey (user_id);

-- §8.1 — a claim, pending or verified, always bound to the account that started it. One row
-- covers both states so "did this account already ask?" has one place to look.
--
-- The token is not a secret (§8.1) — it's published on a connect screen for anyone to read and
-- stored in the clear on purpose; what it proves is that the claimant has write access to the
-- server, not who they are.
CREATE TABLE game_claim (
    id                  uuid PRIMARY KEY,
    game_id             uuid NOT NULL REFERENCES game (id),
    user_id             uuid NOT NULL REFERENCES app_user (id),

    token               text NOT NULL,

    -- Written once, when a probe first matches. NULL means pending.
    claimed_at          timestamptz,

    -- §8.4 — separate from claimed_at: absence of the beacon never revokes a claim (a transient
    -- MSSP failure shouldn't unclaim someone). This only records when we last still saw it.
    beacon_last_seen_at timestamptz,

    -- Which channel verified the token. NULL while pending.
    verified_via        text,

    issued_at           timestamptz NOT NULL,

    -- §8.1 — pending tokens expire so abandoned ones don't linger as identity beacons on connect
    -- screens. A verified claim does not expire.
    expires_at          timestamptz NOT NULL,

    -- Explicit revocation, or the loser of a counter-claim (§8.4). Never set by a missing beacon.
    revoked_at          timestamptz,
    revoked_reason      text,

    -- Rate-limits the on-demand check per claim rather than per source address (§8.1).
    last_checked_at     timestamptz,

    CONSTRAINT game_claim_token_is_not_blank CHECK (btrim(token) <> ''),
    CONSTRAINT game_claim_verified_names_its_channel CHECK (
        (claimed_at IS NULL AND verified_via IS NULL) OR
        (claimed_at IS NOT NULL AND verified_via IS NOT NULL)),
    CONSTRAINT game_claim_channel_vocabulary CHECK (
        verified_via IS NULL OR verified_via IN ('mssp', 'connect_screen')),
    CONSTRAINT game_claim_expires_after_it_is_issued CHECK (expires_at > issued_at)
);

-- One pending token per (account, game), partial so an account may still hold a separate
-- verified claim. A retry while one is outstanding reuses the published token.
CREATE UNIQUE INDEX game_claim_one_pending_per_account_idx
    ON game_claim (game_id, user_id)
 WHERE claimed_at IS NULL AND revoked_at IS NULL;

-- Unique across the table: a collision would let one game's token complete another's claim.
CREATE UNIQUE INDEX game_claim_token_idx ON game_claim (token);

CREATE INDEX game_claim_game_idx ON game_claim (game_id);
CREATE INDEX game_claim_user_idx ON game_claim (user_id);

-- §8.5 — append-only audit log.
CREATE TABLE claim_event (
    id          bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    claim_id    uuid NOT NULL REFERENCES game_claim (id),
    at          timestamptz NOT NULL,

    kind        text NOT NULL,
    detail      text,

    CONSTRAINT claim_event_kind_vocabulary CHECK (kind IN (
        'issued', 'reissued', 'verified', 'beacon_seen', 'beacon_missing', 'revoked', 'expired',
        'counter_claimed', 'check_requested'))
);

CREATE INDEX claim_event_claim_idx ON claim_event (claim_id, at);
