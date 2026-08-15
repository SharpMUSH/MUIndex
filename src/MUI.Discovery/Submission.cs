using System.Net;
using System.Security.Cryptography;
using System.Text;

using MUI.Crawl;

namespace MUI.Discovery;

/// <summary>
/// What we did about one submitted address.
/// </summary>
/// <remarks>
/// <b>Every member is a fact about the submission, and none of them is a fact about a game.</b> A
/// refusal is a decision of ours about where our own socket may go; it is counted here and written
/// into no game's record, for the same reason §7.2's scope refusal writes no availability sample.
/// </remarks>
public enum SubmissionOutcome
{
    /// <summary>The address is in the crawl registry and will be dialled on its own schedule.</summary>
    Accepted,

    /// <summary>A game already answers there. Nothing was created and nothing was changed.</summary>
    AlreadyListed,

    /// <summary>The registry already holds that address, which has not answered for itself yet.</summary>
    AlreadyQueued,

    /// <summary>Nothing that could be dialled was submitted.</summary>
    Malformed,

    /// <summary>
    /// §7.2 — the name resolved somewhere we will not go. <b>Not a measurement of anything.</b>
    /// </summary>
    RefusedNotRoutable,

    /// <summary>
    /// The name did not resolve. An ordinary DNS failure and a fact about the world, which is why it
    /// is a different member from <see cref="RefusedNotRoutable"/> (§7.2's own distinction).
    /// </summary>
    Unresolvable,

    /// <summary>
    /// The bound on how much one source may submit. Recorded nowhere: the source is already at its
    /// limit, and logging refusals would make the window slide forward on every retry.
    /// </summary>
    TooMany,
}

/// <summary>An address the submission form accepted as an address, and nothing beyond that.</summary>
public sealed record SubmittedAddress(string Host, int Port)
{
    public override string ToString() => Host.Contains(':') ? $"[{Host}]:{Port}" : $"{Host}:{Port}";
}

/// <summary>What became of a submission, in enough detail for a page to say so.</summary>
/// <param name="Outcome">What we did.</param>
/// <param name="Address">The address as we read it, or null when nothing could be read.</param>
/// <param name="GameId">The game that already answers there, for <see cref="SubmissionOutcome.AlreadyListed"/>.</param>
/// <param name="Detail">
/// Why, for an operator's log. <b>Never rendered to the submitter for a refusal</b>: it names the
/// address the host resolved to, which is a free scan of our network for whoever asked.
/// </param>
public sealed record SubmissionReceipt(
    SubmissionOutcome Outcome,
    SubmittedAddress? Address = null,
    Guid? GameId = null,
    string? Detail = null);

/// <summary>
/// The submissions we have taken, and the bound on how many one source may make.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two calls because the bound has to be atomic.</b> Counting rows and then inserting one is
/// check-then-act: a burst of concurrent requests all read a count that none of them has yet
/// written to, and every one of them passes. On an unauthenticated form that check <em>is</em> the
/// limit, so <see cref="TryBeginAsync"/> takes the slot and writes the row in one serialised step,
/// and <see cref="CompleteAsync"/> fills in what happened once we know.
/// </para>
/// <para>
/// A row left pending by a process that died still counts against the bound. That is the direction
/// to fail in: an hour of a slightly tighter limit costs a submitter one wait, and the other way
/// round costs us the limit.
/// </para>
/// </remarks>
public interface ISubmissionLog
{
    /// <summary>
    /// Takes one of this source's slots and writes a pending row, or returns false if it has none
    /// left. Serialised per source against every other caller, in this process or another.
    /// </summary>
    Task<bool> TryBeginAsync(
        Guid id,
        string source,
        DateTimeOffset now,
        int perSource,
        DateTimeOffset since,
        CancellationToken ct);

    /// <summary>Fills in what became of a reservation.</summary>
    Task CompleteAsync(
        Guid id,
        SubmittedAddress? address,
        SubmissionOutcome outcome,
        Guid? crawlTargetId,
        CancellationToken ct);
}

/// <summary>
/// §11's rotating salt, shared by every replica.
/// </summary>
/// <remarks>
/// <b>Not a per-process random value, and the difference is the whole bound.</b> Two web replicas
/// with two salts derive two digests for one address, so a limit of five per hour becomes five per
/// replica per hour and a restart clears it. §11's construction is a <em>rotating</em> salt, and a
/// salt that rotates is one that is stored for the length of an epoch. What that buys — that rows
/// written under a retired salt can never be lined up against rows written under the current one —
/// survives being shared, and is the property worth having.
/// </remarks>
public interface ISubmissionSalt
{
    Task<byte[]> CurrentAsync(CancellationToken ct);
}

/// <summary>The bounds on an unauthenticated form.</summary>
public sealed record SubmissionOptions
{
    /// <summary>
    /// How many submissions one source may make inside <see cref="Window"/>.
    /// </summary>
    /// <remarks>
    /// Low on purpose. Somebody with a list of games to tell us about is doing us a favour and can
    /// come back in an hour; somebody feeding a scanner through the form cannot. The bound is on the
    /// form and not on the crawler — <see cref="CrawlRateLimiter"/> already governs what we do to
    /// other people's servers, and these are two different politenesses.
    /// </remarks>
    public int PerSource { get; init; } = 5;

    public TimeSpan Window { get; init; } = TimeSpan.FromHours(1);

    /// <summary>
    /// How long one salt epoch lasts (spec §11).
    /// </summary>
    /// <remarks>
    /// Far longer than <see cref="Window"/>, so a rotation costs at most one window's worth of bound
    /// — and short enough that the set of rows any one salt can link together stays small.
    /// </remarks>
    public TimeSpan SaltEpoch { get; init; } = TimeSpan.FromDays(7);

    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(PerSource, 1, nameof(PerSource));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(Window, TimeSpan.Zero, nameof(Window));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(SaltEpoch, Window, nameof(SaltEpoch));
    }
}

/// <summary>
/// Who submitted something, as much as we are willing to know (spec §11).
/// </summary>
/// <remarks>
/// <para>
/// <b>The submitter's address is never stored.</b> What the rate limit needs is whether two
/// submissions came from one place inside the hour, and it needs nothing else, ever — so what is
/// stored is a digest under <see cref="ISubmissionSalt"/>'s shared, rotating salt. A plain hash of
/// an IPv4 address would be reversible by anybody with an afternoon and four billion guesses, which
/// is what the salt is for; the rotation is what stops one epoch's rows being lined up against the
/// next one's.
/// </para>
/// <para>
/// <b>An IPv6 address is bucketed to its /64, and that is a bound rather than a nicety.</b> The
/// smallest block anybody is assigned is a /64 and a great many home connections get a /48 or /56,
/// so an attacker keyed on the full address holds eighteen quintillion buckets and the limit is
/// decorative. The /64 is the unit that costs something to obtain, so it is the unit the bound is
/// counted in. IPv4 is kept whole, because there a single address is already scarce.
/// </para>
/// </remarks>
public sealed class SubmissionSource(ISubmissionSalt salt)
{
    /// <summary>What a request with no address at all counts as. One bucket, so it is still bounded.</summary>
    public const string Unknown = "unknown";

    public async Task<string> OfAsync(IPAddress? address, CancellationToken ct = default) =>
        Digest(await salt.CurrentAsync(ct).ConfigureAwait(false), Bucket(address));

    /// <summary>
    /// The unit the bound is counted in: an IPv4 address whole, an IPv6 address's /64 prefix.
    /// </summary>
    public static string Bucket(IPAddress? address)
    {
        if (address is null)
        {
            return Unknown;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily is not System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            return address.ToString();
        }

        var octets = address.GetAddressBytes();
        Array.Clear(octets, 8, 8);

        return $"{new IPAddress(octets)}/64";
    }

    /// <summary>Exposed so a test can prove two salts do not agree about one address.</summary>
    public static string Digest(ReadOnlySpan<byte> salt, string bucket)
    {
        ArgumentNullException.ThrowIfNull(bucket);

        return Convert.ToHexStringLower(HMACSHA256.HashData(salt, Encoding.UTF8.GetBytes(bucket)));
    }
}

/// <summary>
/// Reads a host and a port out of what somebody typed, and refuses everything else.
/// </summary>
/// <remarks>
/// <b>Nothing here fabricates.</b> A host with no port is refused rather than given 4201: guessing a
/// port would aim a socket at something nobody advertised, and §6.4's rule that parsers never
/// fabricate applies to addresses exactly as it applies to player counts. What it does do is read the
/// spellings people actually type — <c>host:port</c> and <c>host port</c> pasted whole into the first
/// box — because a form that refuses a correct address on a formatting technicality has taught
/// somebody that we are broken.
/// </remarks>
public static class SubmittedAddressReader
{
    /// <summary>The longest a DNS name can be, and the bound the table carries.</summary>
    public const int MaxHostLength = 253;

    public static bool TryRead(string? host, string? port, out SubmittedAddress address)
    {
        address = null!;

        var typed = host?.Trim();

        if (string.IsNullOrEmpty(typed))
        {
            return false;
        }

        // A whole address in the first box. Tried only when the port box is empty, so that somebody
        // who filled both in cannot be overruled by a stray colon in the first.
        if (string.IsNullOrWhiteSpace(port))
        {
            return MsspReferrals.TryParseEntry(typed, out var candidate)
                && TryRead(candidate.Host, candidate.Port.ToString(System.Globalization.CultureInfo.InvariantCulture), out address);
        }

        if (!int.TryParse(port.Trim(), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var number)
            || number is < 1 or > 65535)
        {
            return false;
        }

        var canonical = CanonicalHost.Normalize(typed);

        if (canonical.Length is 0 or > MaxHostLength || !HostText.IsPlausible(canonical))
        {
            return false;
        }

        address = new SubmittedAddress(canonical, number);
        return true;
    }
}

/// <summary>
/// The public form's whole decision (spec §7.2, §7.6, §8).
/// </summary>
/// <remarks>
/// <para>
/// <b>An address, and nothing else.</b> A submitter asserts nothing about the game — no name, no
/// description, no codebase, no players — because every fact on this site is measured by this crawler
/// and there is nothing here for a stranger's claim to become. That is §7.6's rule for the backfill,
/// applied to the one-at-a-time case.
/// </para>
/// <para>
/// <b>The order of the checks is the design.</b> The rate limit first — as a reservation rather than
/// a count, so that a burst cannot walk through a check none of it has written to yet — because it
/// must not be bypassable by sending rubbish and a source at its bound must not be able to make us
/// do work. Then the address, then our own catalogue, and only then DNS, so the form cannot be used
/// as a free resolver. §7.2's gate runs on the resolved address, before the target is written, so a
/// refused name never reaches the registry and is never dialled by anything.
/// </para>
/// <para>
/// <b>A refusal creates nothing and measures nothing.</b> No game, no target, no availability sample,
/// no presence row. <see cref="SubmissionOutcome.RefusedNotRoutable"/> is counted on the submission
/// and nowhere else, which is where a decision of ours belongs — and it is emphatically not a
/// <c>ProbeOutcome</c>, because no probe happened and none could have.
/// </para>
/// <para>
/// <b>A duplicate collapses onto what exists.</b> An address a game already answers at is
/// <see cref="SubmissionOutcome.AlreadyListed"/> and one already in the registry is
/// <see cref="SubmissionOutcome.AlreadyQueued"/>; neither writes anything. Even if both checks were
/// removed, <see cref="ICrawlTargetRepository.AddAsync"/> is keyed on the address and would return
/// the existing row — so the second listing this form could otherwise have produced is prevented
/// twice, and the checks exist to say something true to the submitter rather than to hold the line.
/// </para>
/// </remarks>
public sealed class SubmissionService(
    ICrawlTargetRepository targets,
    IEndpointDirectory endpoints,
    IHostScopeGuard scope,
    ISubmissionLog log,
    SubmissionOptions options,
    TimeProvider time)
{
    public async Task<SubmissionReceipt> SubmitAsync(
        string? host,
        string? port,
        string source,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        var now = time.GetUtcNow();
        var reservation = Guid.CreateVersion7();

        // One serialised step: count this source's slots and take one, or take none. Deliberately
        // before anything else — a source at its limit must not be able to make us parse, read or
        // resolve anything — and deliberately the only place the bound is consulted, because a
        // separate count would be a check somebody could walk through while it was still true.
        if (!await log.TryBeginAsync(reservation, source, now, options.PerSource, now - options.Window, ct))
        {
            return new SubmissionReceipt(SubmissionOutcome.TooMany);
        }

        if (!SubmittedAddressReader.TryRead(host, port, out var address))
        {
            await log.CompleteAsync(reservation, null, SubmissionOutcome.Malformed, null, ct);
            return new SubmissionReceipt(SubmissionOutcome.Malformed);
        }

        if (await endpoints.ByAddressAsync(address.Host, address.Port, ct) is { } known)
        {
            await log.CompleteAsync(reservation, address, SubmissionOutcome.AlreadyListed, null, ct);
            return new SubmissionReceipt(SubmissionOutcome.AlreadyListed, address, known.GameId);
        }

        if (await targets.ByAddressAsync(address.Host, address.Port, ct) is not null)
        {
            await log.CompleteAsync(reservation, address, SubmissionOutcome.AlreadyQueued, null, ct);
            return new SubmissionReceipt(SubmissionOutcome.AlreadyQueued, address);
        }

        var decision = await scope.InspectAsync(address.Host, ct);

        if (decision.Ruling is not HostScopeRuling.Allowed)
        {
            // Two outcomes, because §7.2 says they are two facts and the record has to keep them
            // apart. What the *submitter* is told collapses them into one sentence — see
            // SubmitCopy — because telling a stranger which of the two happened is an oracle over
            // our own resolver's view of names they cannot otherwise see.
            var outcome = decision.Ruling is HostScopeRuling.RefusedNonGlobal
                ? SubmissionOutcome.RefusedNotRoutable
                : SubmissionOutcome.Unresolvable;

            await log.CompleteAsync(reservation, address, outcome, null, ct);
            return new SubmissionReceipt(outcome, address, Detail: decision.Detail);
        }

        // Due now: an address somebody just typed is the one case where waiting has a person behind
        // it. Everything after this is the ordinary schedule, and IsOperatorSeed stays false — a
        // stranger with a browser is not an operator, and §7.2's exemption is never inferred.
        var target = new CrawlTarget
        {
            Id = Guid.CreateVersion7(),
            Host = address.Host,
            Port = address.Port,
            NextProbeAt = now,
            FirstSeenAt = now,
            SubmittedAt = now,
        };

        var id = await targets.AddAsync(target, ct);

        await log.CompleteAsync(reservation, address, SubmissionOutcome.Accepted, id, ct);

        return new SubmissionReceipt(SubmissionOutcome.Accepted, address);
    }
}
