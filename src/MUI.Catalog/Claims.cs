namespace MUI.Catalog;

/// <summary>
/// Where a claim token was read from. Only channels a probe can see are here.
/// </summary>
/// <remarks>
/// DNS is deliberately absent (spec §8.3), and not merely for want of a resolver: a TXT record proves
/// control of a <em>hostname</em>, and a hostname is not a game. MU* hosting routinely puts many
/// unrelated games on one domain separated only by port, so the host's operator could claim all of
/// them and a game on somebody else's domain could use the channel not at all. Both members here
/// prove control of <em>that listener</em>, which is the thing being claimed.
/// </remarks>
public enum ClaimChannel
{
    Mssp,
    ConnectScreen,
}

/// <summary>
/// A claim on a game by an account: pending while <see cref="ClaimedAt"/> is null, verified after.
/// </summary>
/// <remarks>
/// <para>
/// <b>One record covers both states on purpose.</b> A pending claim and a verified one are the same
/// fact at two moments, and splitting them would make "has this account already asked?" a question
/// with two places to look — which is how a second token gets minted while the first is still printed
/// on somebody's connect screen.
/// </para>
/// <para>
/// <b><see cref="Token"/> is a nonce, not a credential.</b> We ask an operator to publish it where
/// every anonymous connection reads it (spec §8.1), so holding it can never confer anything. It
/// proves that somebody with write access to that server published it; <see cref="UserId"/> answers
/// the separate question of who asked.
/// </para>
/// </remarks>
public sealed record GameClaim
{
    public required Guid Id { get; init; }

    public required Guid GameId { get; init; }

    /// <summary>The account the claim binds to — never whoever holds the token.</summary>
    public required Guid UserId { get; init; }

    public required string Token { get; init; }

    public required DateTimeOffset IssuedAt { get; init; }

    /// <summary>
    /// When a pending token stops being offered. A verified claim does not expire.
    /// </summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>Written once, when a probe first matched. Null means pending.</summary>
    public DateTimeOffset? ClaimedAt { get; init; }

    /// <summary>
    /// When a probe last still saw the token, which is a different fact from <see cref="ClaimedAt"/>.
    /// </summary>
    /// <remarks>
    /// Spec §8.4: presence establishes, absence never revokes. Two timestamps exist so that "this
    /// account proved control" and "the beacon is still up" can be told apart — collapsing them would
    /// hand revocation to any transient failure, and this project has already watched MCCP swallow a
    /// connection's payload whole.
    /// </remarks>
    public DateTimeOffset? BeaconLastSeenAt { get; init; }

    public ClaimChannel? VerifiedVia { get; init; }

    public DateTimeOffset? RevokedAt { get; init; }

    public string? RevokedReason { get; init; }

    /// <summary>When the claimant last asked us to look, so the on-demand check can be bounded.</summary>
    public DateTimeOffset? LastCheckedAt { get; init; }

    public bool IsVerified => ClaimedAt is not null && RevokedAt is null;

    public bool IsPending(DateTimeOffset now) =>
        ClaimedAt is null && RevokedAt is null && now < ExpiresAt;
}

/// <summary>Something that happened to a claim. Append-only (spec §8.5).</summary>
public sealed record ClaimEvent(Guid ClaimId, DateTimeOffset At, ClaimEventKind Kind, string? Detail = null);

public enum ClaimEventKind
{
    Issued,
    Reissued,
    Verified,
    BeaconSeen,
    BeaconMissing,
    Revoked,
    Expired,
    CounterClaimed,
    CheckRequested,
}

/// <summary>
/// Mints the token an operator publishes.
/// </summary>
/// <remarks>
/// <para>
/// Randomness rather than a derivation of the game or the account, because a token an observer can
/// <em>predict</em> is one they can publish first. It is public once verified — that is the point —
/// but it must be unguessable until the operator chooses to put it somewhere.
/// </para>
/// <para>
/// The alphabet excludes the characters people confuse when reading a connect screen back to
/// themselves. The token is meant to be copied, never transcribed, but a scheme that punishes the
/// person who does transcribe it is a support mail waiting to happen.
/// </para>
/// </remarks>
public static class ClaimToken
{
    /// <summary>The prefix every token carries, so one is recognisable in a config file at a glance.</summary>
    public const string Prefix = "muidx-";

    /// <summary>How long a pending token is offered before it expires (spec §8.1).</summary>
    public static readonly TimeSpan PendingLifetime = TimeSpan.FromDays(30);

    /// <summary>
    /// Digits and lower-case letters with <c>0/o</c>, <c>1/l/i</c> and <c>u/v</c> reduced to one
    /// member each.
    /// </summary>
    private const string Alphabet = "23456789abcdefghjkmnpqrstwxyz";

    /// <summary>Body length. 20 characters of this alphabet is ~97 bits.</summary>
    private const int BodyLength = 20;

    public static string Mint() => Mint(System.Security.Cryptography.RandomNumberGenerator.GetBytes(BodyLength));

    /// <summary>Deterministic overload, so a test can assert the shape without asserting the randomness.</summary>
    public static string Mint(ReadOnlySpan<byte> entropy)
    {
        var body = new char[entropy.Length];

        for (var i = 0; i < entropy.Length; i++)
        {
            body[i] = Alphabet[entropy[i] % Alphabet.Length];
        }

        return Prefix + new string(body);
    }

    /// <summary>
    /// Whether a string read off a server could be one of ours — a cheap filter, never a verification.
    /// </summary>
    /// <remarks>
    /// A token that passes this has still proved nothing. Verification is a lookup against an issued
    /// pending claim and cannot be replaced by a shape check, however tempting that is at the call
    /// site.
    /// </remarks>
    public static bool LooksLikeOne(string? candidate) =>
        candidate is not null
        && candidate.StartsWith(Prefix, StringComparison.Ordinal)
        && candidate.Length == Prefix.Length + BodyLength;
}

/// <summary>Reads and writes claims. Storage-agnostic by construction.</summary>
public interface IClaimStore
{
    Task<GameClaim?> FindAsync(Guid claimId, CancellationToken cancellationToken = default);

    /// <summary>Every claim on a game, verified and pending, newest first.</summary>
    Task<IReadOnlyList<GameClaim>> ForGameAsync(Guid gameId, CancellationToken cancellationToken = default);

    /// <summary>Every claim an account holds.</summary>
    Task<IReadOnlyList<GameClaim>> ForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The live pending claim for <paramref name="gameId"/> whose token is <paramref name="token"/>,
    /// or null.
    /// </summary>
    /// <remarks>
    /// Scoped to the game as well as the token. A token is unique across the table, so the game is
    /// redundant for correctness — and it is passed anyway, because a lookup that would silently
    /// complete <em>a different game's</em> claim if the uniqueness ever lapsed is one refactor away
    /// from being a real hole.
    /// </remarks>
    Task<GameClaim?> FindPendingByTokenAsync(
        Guid gameId,
        string token,
        CancellationToken cancellationToken = default);

    Task InsertAsync(GameClaim claim, CancellationToken cancellationToken = default);

    Task UpdateAsync(GameClaim claim, CancellationToken cancellationToken = default);

    Task RecordEventAsync(ClaimEvent claimEvent, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ClaimEvent>> EventsAsync(Guid claimId, CancellationToken cancellationToken = default);
}

/// <summary>What happened when a probe's beacon was offered to the claim store.</summary>
public enum ClaimVerdict
{
    /// <summary>No token was published, or none we issued. The common case, and not an error.</summary>
    NothingToDo,

    /// <summary>A pending claim matched and is now verified.</summary>
    Verified,

    /// <summary>An already-verified claim's beacon is still up (spec §8.4).</summary>
    StillSeen,

    /// <summary>A token we issued, matching a claim that has expired or been revoked.</summary>
    Stale,
}
