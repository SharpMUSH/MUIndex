-- spec §8 — accounts, passkeys, and the claims that bind one to a game.
--
-- Everything before this migration is what a probe produced or what decides who gets probed. This is
-- the first table a *person* writes to, and the ordering of §8.1 is enforced here rather than trusted
-- in a handler: a claim carries a NOT NULL account, so a token that verified without anybody having
-- asked for it has nowhere to go.

-- §8.2 — an account, and deliberately almost nothing about a person.
--
-- NO EMAIL COLUMN, NO PASSWORD HASH, AND NEITHER IS AN OVERSIGHT. Sign-in is passkeys only, so there
-- is no password to store and no address to recover to; §8.2's recovery path is to make a new account
-- and re-verify through the game, because the root of trust is the server the operator controls. An
-- account here is a durable handle to hang a claim on, and it is worth almost nothing to steal.
--
-- §11 refuses to persist player names. An owner is not a player, but the same default applies: what
-- is stored is what the site cannot work without.
CREATE TABLE app_user (
    id              uuid PRIMARY KEY,

    -- What the account calls itself. Chosen by the account holder, shown only to them unless they
    -- publish a contact on a game they own. Not an identity claim and never verified as one.
    display_name    text NOT NULL,

    -- Identity's own case/diacritic-insensitive lookup key. Unique so two accounts cannot collide on
    -- a name a human would read as the same.
    normalised_name text NOT NULL,

    -- Identity's optimistic-concurrency token. Ours to store, not ours to interpret.
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
--
-- The public key is public by construction, which is the property that makes this table uninteresting
-- to steal and is the whole argument for passkeys over passwords here.
--
-- The full attestation object is kept because it carries the AAGUID identifying the authenticator
-- model: when a model is found to be compromised, the affected credentials have to be findable. We do
-- not validate attestation statements (ASP.NET Core Identity does not by default, and for a consumer
-- site that is the right default), so the AAGUID is evidence rather than proof — which is a reason to
-- keep it, not a reason to discard it.
CREATE TABLE user_passkey (
    credential_id       bytea PRIMARY KEY,
    user_id             uuid NOT NULL REFERENCES app_user (id) ON DELETE CASCADE,

    public_key          bytea NOT NULL,

    -- Replay protection. A credential that presents a counter no higher than the stored one has been
    -- cloned or replayed; the authenticator is expected to increment it.
    sign_count          bigint NOT NULL DEFAULT 0,

    -- Whether the credential is synced to a provider (backed up) or lives on one device. A passkey
    -- that is NOT backed up is one lost phone away from being gone, which is what the dashboard reads
    -- to suggest adding a second one.
    is_backed_up        boolean NOT NULL DEFAULT false,
    is_backup_eligible  boolean NOT NULL DEFAULT false,

    transports          text,
    attestation_object  bytea,
    attestation_format  text,

    -- "My phone", "the yubikey in the drawer". A person with three passkeys needs to know which is
    -- which before they can revoke one, so this is bounded rather than free — see §8's note on
    -- resource limits.
    name                text,

    created_at          timestamptz NOT NULL,
    last_used_at        timestamptz,

    CONSTRAINT user_passkey_name_is_bounded CHECK (name IS NULL OR length(name) <= 64),
    CONSTRAINT user_passkey_sign_count_is_not_negative CHECK (sign_count >= 0)
);

CREATE INDEX user_passkey_user_idx ON user_passkey (user_id);

-- §8.1 — a claim, pending or verified, always bound to the account that started it.
--
-- ONE ROW COVERS BOTH STATES ON PURPOSE. A pending claim and a verified one are the same fact at two
-- moments, and splitting them into two tables would make "did this account already ask?" a question
-- with two places to look — which is how a second pending token gets minted while the first is still
-- printed on somebody's connect screen.
--
-- THE TOKEN IS NOT A SECRET AND MUST NEVER BE TREATED AS ONE (§8.1). We ask an operator to publish it
-- where every anonymous connection reads it. It is stored in the clear because hashing it would imply
-- a confidentiality it cannot have, and because the crawler compares what it read against what we
-- issued. What the token proves is that somebody with write access to that server published it; the
-- account column answers who asked.
CREATE TABLE game_claim (
    id                  uuid PRIMARY KEY,
    game_id             uuid NOT NULL REFERENCES game (id),
    user_id             uuid NOT NULL REFERENCES app_user (id),

    token               text NOT NULL,

    -- Written once, when a probe first matched. NULL means pending.
    claimed_at          timestamptz,

    -- §8.4 — a second timestamp because there are two facts. Absence of the beacon never revokes a
    -- claim; a transient MSSP failure or a compression bug eating a subnegotiation would otherwise
    -- unclaim somebody silently. This says only when we last still saw it.
    beacon_last_seen_at timestamptz,

    -- Which channel the token was read from, for the audit trail and for telling an owner what we
    -- actually saw. NULL while pending.
    verified_via        text,

    issued_at           timestamptz NOT NULL,

    -- §8.1 — a pending token expires so that abandoned tokens do not accumulate on connect screens
    -- and linger as identity beacons for claims nobody completed. A *verified* claim does not expire.
    expires_at          timestamptz NOT NULL,

    -- Explicit revocation, or the loser of a counter-claim (§8.4). Never written because a beacon
    -- went missing.
    revoked_at          timestamptz,
    revoked_reason      text,

    -- When the claimant last asked us to look, so the on-demand check can be rate-limited per claim
    -- rather than per source address, which is the bound that actually matters (§8.1).
    last_checked_at     timestamptz,

    CONSTRAINT game_claim_token_is_not_blank CHECK (btrim(token) <> ''),
    CONSTRAINT game_claim_verified_names_its_channel CHECK (
        (claimed_at IS NULL AND verified_via IS NULL) OR
        (claimed_at IS NOT NULL AND verified_via IS NOT NULL)),
    CONSTRAINT game_claim_channel_vocabulary CHECK (
        verified_via IS NULL OR verified_via IN ('mssp', 'connect_screen')),
    CONSTRAINT game_claim_expires_after_it_is_issued CHECK (expires_at > issued_at)
);

-- §8.1 — one pending token per (account, game). Partial, so the same account may hold a verified
-- claim and nothing else: a second attempt while one is outstanding must reuse the token already
-- published rather than mint a rival.
CREATE UNIQUE INDEX game_claim_one_pending_per_account_idx
    ON game_claim (game_id, user_id)
 WHERE claimed_at IS NULL AND revoked_at IS NULL;

-- The token the crawler compares against. Unique across the table, because a collision would let one
-- game's published token complete another game's claim.
CREATE UNIQUE INDEX game_claim_token_idx ON game_claim (token);

CREATE INDEX game_claim_game_idx ON game_claim (game_id);
CREATE INDEX game_claim_user_idx ON game_claim (user_id);

-- §8.5 — the audit log. Append-only by convention: every row is something that happened, and nothing
-- here is ever updated or deleted, for the same reason §7.4 never deletes a game.
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
