namespace MUI.Catalog;

/// <summary>
/// Where a claim token was read from (spec §8.3).
/// </summary>
/// <remarks>
/// <b>The three are not equally strong, and the difference is load-bearing.</b> An MSSP field and a
/// connect-screen line are published by the listener being claimed, so reading one proves control of
/// <em>that listener</em> — the thing a claim is about. A TXT record proves control of a
/// <em>hostname</em>, and MU* hosting routinely puts many unrelated games on one domain behind
/// different ports, so on a shared host the publisher is the hosting operator rather than the game's.
/// The record must therefore name the port it speaks for, and
/// <see cref="Persistence.ClaimService.OfferBeaconAsync"/> will not let it complete a
/// <see cref="ClaimIntent.Assume"/>: DNS may add an owner and may never displace one who proved
/// control of the server itself.
/// </remarks>
public enum ClaimChannel
{
    Mssp,
    ConnectScreen,

    /// <summary>A port-qualified TXT record at <c>_muindex.&lt;host&gt;</c>. Join-only; see above.</summary>
    DnsTxt,
}

/// <summary>
/// Whether a claimant is joining a game's owners or taking it over (spec §8.4, §8.5).
/// </summary>
/// <remarks>
/// Declared, never inferred: a co-founder joining and a new operator taking over publish the
/// identical token in the identical config file, so nothing in a probe can tell them apart. Guessing
/// fails both ways — treating every claim on an already-claimed game as a takeover unclaims a
/// partner; the opposite leaves a departed operator holding a listing. Neither intent is more
/// trusted; both publish the same token, so the choice only decides what happens to the other claims.
/// </remarks>
public enum ClaimIntent
{
    /// <summary>Become one of the game's owners, alongside whoever else has proved it.</summary>
    Join,

    /// <summary>Take the game over. On verification, every other verified claim is revoked.</summary>
    Assume,
}

/// <summary>
/// A claim on a game by an account: pending while <see cref="ClaimedAt"/> is null, verified after.
/// </summary>
/// <remarks>
/// One record covers both states on purpose: splitting pending from verified would make "has this
/// account already asked?" a question with two places to look, risking a second token minted while
/// the first is still live. <see cref="Token"/> is a nonce, not a credential — published where every
/// anonymous connection reads it (spec §8.1), so holding it confers nothing; it proves only that
/// whoever published it had write access to that server.
/// </remarks>
public sealed record GameClaim
{
    public required Guid Id { get; init; }

    public required Guid GameId { get; init; }

    /// <summary>The account the claim binds to — never whoever holds the token.</summary>
    public required Guid UserId { get; init; }

    public required string Token { get; init; }

    /// <summary>Whether verifying this joins the game's owners or displaces them (spec §8.4).</summary>
    public ClaimIntent Intent { get; init; } = ClaimIntent.Join;

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
    /// Spec §8.4: presence establishes, absence never revokes. Two timestamps let "this account
    /// proved control" and "the beacon is still up" be told apart — collapsing them would hand
    /// revocation to any transient failure.
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
/// Random, not derived from the game or the account — a predictable token is one an observer could
/// publish first. The alphabet excludes characters people confuse when reading a connect screen back,
/// since the token is meant to be copied, not transcribed.
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
    /// A token that passes this has proved nothing. Verification is a lookup against an issued
    /// pending claim and cannot be replaced by a shape check.
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
    /// <para>
    /// Scoped to the game as well as the token, even though a token is unique across the table: a
    /// lookup that could silently complete a different game's claim if uniqueness ever lapsed is one
    /// refactor away from a real hole.
    /// </para>
    /// <para>
    /// <b><paramref name="now"/> is passed in rather than read from the store's own clock.</b>
    /// <see cref="GameClaim.IsPending"/> asks a <see cref="TimeProvider"/> and this used to ask
    /// <c>now()</c> in SQL, so one claim could be pending in C# and expired in the database — which
    /// is not a fixture problem: it is the same predicate answered by two clocks, and the caller's is
    /// the one every other decision about the claim already uses.
    /// </para>
    /// </remarks>
    Task<GameClaim?> FindPendingByTokenAsync(
        Guid gameId,
        string token,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every claim a DNS lookup could still change: live pending ones, and the ones DNS itself proved.
    /// </summary>
    /// <remarks>
    /// The set the §8.3 sweep walks, and it is deliberately not "every claim". A pending claim is
    /// swept because a record may have appeared; a DNS-verified one because its beacon should keep
    /// moving while the record is up (§8.4's second timestamp is worth nothing if nothing writes it).
    /// A claim proved on the wire is left alone — re-reading a zone says nothing about whether a
    /// listener still publishes anything, so the lookup could only ever be wasted. An expired or
    /// revoked claim is left alone because no answer could revive it.
    /// </remarks>
    Task<IReadOnlyList<GameClaim>> PendingOrDnsVerifiedAsync(
        DateTimeOffset now,
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

    /// <summary>
    /// A counter-claim completed: this account now owns the game and the others were revoked (§8.4).
    /// </summary>
    Assumed,
}

/// <summary>
/// Brings a game's next probe forward, for §8.1's on-demand check.
/// </summary>
/// <remarks>
/// An interface here, implemented over <c>crawl_target</c> in <c>MUI.Crawler</c>, since due-ness is
/// the crawl registry's business and <c>MUI.Catalog</c> may not know a socket exists.
/// <see cref="ClaimService"/> decides whether a claimant may ask; this only moves the schedule. It
/// brings a probe forward and never dials — the crawler's own loop still honours <c>CRAWL DELAY</c>
/// and scope, so a button on a page cannot become a way to connect to a stranger's server on demand.
/// </remarks>
public interface IOnDemandProbes
{
    /// <summary>
    /// Asks for <paramref name="gameId"/> to be probed no later than <paramref name="at"/>.
    /// </summary>
    /// <returns>Whether any target was brought forward.</returns>
    Task<bool> BringForwardAsync(Guid gameId, DateTimeOffset at, CancellationToken cancellationToken = default);
}
