using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace MUI.Crawl;

/// <summary>
/// How often the hashing salt rotates, and what it is derived from (spec §11, §15.4).
/// </summary>
/// <remarks>
/// <para>
/// §11 promises that "aggregates use salted hashes with a rotating salt, so a unique-player estimate
/// is possible while re-identification across salt epochs is not". Both halves of that sentence are
/// this record: <see cref="Period"/> is what makes an estimate possible inside a window, and it is
/// also what makes linking across two of them impossible.
/// </para>
/// <para>
/// <b>The period is configuration because §15.4 says it is open, and the default is short because the
/// two ways of being wrong are not symmetrical.</b> An epoch that turns out to be too short costs
/// precision in an estimate, and lengthening it later works from the next epoch onwards. An epoch
/// that turns out to be too long has already linked a season of observations together by the time
/// anybody decides to shorten it, and no later setting un-links them. Ship conservative and tune.
/// </para>
/// </remarks>
public sealed record SaltRotationOptions
{
    /// <summary>How long one salt lasts.</summary>
    public TimeSpan Period { get; init; } = TimeSpan.FromDays(7);

    /// <summary>
    /// The instant epochs are counted from, so that every replica agrees on where one ends.
    /// </summary>
    public DateTimeOffset Anchor { get; init; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A deployment-wide secret the per-epoch salts are derived from, or null for a random one.
    /// </summary>
    /// <remarks>
    /// <b>Null is the safe default and the less useful one.</b> With no configured secret each process
    /// invents its own, so hashes from two replicas cannot be compared even inside one epoch and the
    /// estimate is per-replica. Set it — from a secret store, never from source control — and the
    /// estimate becomes deployment-wide. The failure of the unset case is a weaker number; the failure
    /// of a hard-coded one would be a salt anybody could reproduce, which is no salt at all.
    /// </remarks>
    public string? Secret { get; init; }

    public void Validate()
    {
        if (Period <= TimeSpan.Zero)
        {
            throw new ArgumentException("A salt epoch has to last a positive amount of time.");
        }
    }
}

/// <summary>
/// One salt and the label that names it. The salt never leaves the process; the label is the only
/// part that is ever written down.
/// </summary>
public sealed class SaltEpoch(string label, byte[] salt)
{
    /// <summary>
    /// Which epoch this is, in a form that sorts and reads: the UTC instant it began.
    /// </summary>
    /// <remarks>
    /// Recorded beside every aggregate (<c>PresenceAggregates.SaltEpoch</c>) so that a reader can tell
    /// which estimates were taken under one salt. It names a time and nothing else — there is nothing
    /// in it to derive the salt from.
    /// </remarks>
    public string Label { get; } = label;

    /// <summary>The salt itself. Not a property, so it does not land in a log line or a JSON dump.</summary>
    internal ReadOnlySpan<byte> Salt => salt;

    /// <summary>
    /// Hashes a player name under this epoch's salt.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The one place a player name is allowed to be, and it leaves as sixteen bytes.</b> §11 and
    /// CLAUDE.md both say names are never persisted: <c>WHO</c> is parsed in memory, names are counted
    /// through this method, and the names themselves are discarded with the parse. Nothing that takes
    /// a name may return anything a name can be recovered from, and nothing that returns one of these
    /// may be given a way to write the name beside it.
    /// </para>
    /// <para>
    /// HMAC rather than a plain digest of salt-then-name, because a plain digest of a short, guessable
    /// name is a rainbow table the moment the salt leaks, and because HMAC is what a keyed hash is
    /// specified to be. Truncated to 128 bits, which is more than a distinct-count over the few
    /// hundred names a game has ever had online at once needs, and less to keep around.
    /// </para>
    /// </remarks>
    public string Hash(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        Span<byte> digest = stackalloc byte[HMACSHA256.HashSizeInBytes];
        HMACSHA256.HashData(salt, Encoding.UTF8.GetBytes(name), digest);

        return Convert.ToHexStringLower(digest[..16]);
    }
}

/// <summary>Which salt is current (spec §11).</summary>
public interface ISaltProvider
{
    SaltEpoch Current(DateTimeOffset now);
}

/// <summary>
/// A salt that rotates on a fixed period, derived so that every replica computes the same one.
/// </summary>
/// <remarks>
/// <para>
/// <b>No salt is ever stored.</b> Each epoch's salt is HMAC(secret, label), so a replica that starts
/// halfway through an epoch derives the salt the others are using without anything having to hold it
/// between them — and a past epoch's salt is not sitting in a table waiting to be joined against.
/// </para>
/// <para>
/// The provider is a singleton and holds one epoch at a time, recomputing when the clock crosses a
/// boundary. That is cheap, and it means a long-running process cannot go on hashing under an epoch
/// that ended while it was busy.
/// </para>
/// </remarks>
public sealed class RotatingSaltProvider : ISaltProvider
{
    private readonly SaltRotationOptions _options;
    private readonly byte[] _secret;
    private readonly Lock _gate = new();

    private SaltEpoch? _current;
    private long _currentIndex = -1;

    public RotatingSaltProvider(SaltRotationOptions? options = null)
    {
        _options = options ?? new SaltRotationOptions();
        _options.Validate();

        _secret = _options.Secret is { Length: > 0 } secret
            ? Encoding.UTF8.GetBytes(secret)
            : RandomNumberGenerator.GetBytes(32);
    }

    public SaltEpoch Current(DateTimeOffset now)
    {
        var index = IndexOf(now);

        lock (_gate)
        {
            if (_current is { } held && _currentIndex == index)
            {
                return held;
            }

            var label = LabelOf(index);
            _currentIndex = index;

            return _current = new SaltEpoch(label, HMACSHA256.HashData(_secret, Encoding.UTF8.GetBytes(label)));
        }
    }

    /// <summary>Which epoch an instant falls in, counted from the anchor.</summary>
    private long IndexOf(DateTimeOffset now) =>
        (long)Math.Floor((now - _options.Anchor) / _options.Period);

    private string LabelOf(long index) =>
        (_options.Anchor + index * _options.Period)
        .UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
}
