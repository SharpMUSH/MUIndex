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
/// One submission, as the <c>game_submission</c> table holds it (migration 0010).
/// </summary>
/// <remarks>
/// There is no game id here and there must never be one. A submission is a thing somebody did to us,
/// and attaching it to a game would put our own handling of a stranger's form into that game's record.
/// </remarks>
public sealed record SubmissionRecord(
    Guid Id,
    string? Host,
    int? Port,
    DateTimeOffset SubmittedAt,
    SubmissionOutcome Outcome,
    Guid? CrawlTargetId,
    string Source);

/// <summary>The submissions we have taken, and how many one source has made lately.</summary>
public interface ISubmissionLog
{
    Task RecordAsync(SubmissionRecord record, CancellationToken ct);

    /// <summary>How many submissions this source has made since a moment. The rate limit's whole read.</summary>
    Task<int> CountSinceAsync(string source, DateTimeOffset since, CancellationToken ct);
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

    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(PerSource, 1, nameof(PerSource));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(Window, TimeSpan.Zero, nameof(Window));
    }
}

/// <summary>
/// Who submitted something, as much as we are willing to know (spec §11).
/// </summary>
/// <remarks>
/// <para>
/// <b>The submitter's address is never stored.</b> What the rate limit needs is whether two
/// submissions came from one place inside the hour, and it needs nothing else, ever — so what is
/// stored is a salted digest and the salt is a random value this process generated at startup and
/// never wrote down. Restarting rotates it, which is more often than the window is long, so a row
/// stops being comparable to anything well before it stops being a row.
/// </para>
/// <para>
/// This is §11's rule for player names applied to a form: hash with a rotating salt, keep the
/// aggregate, lose the identity. A plain hash of an IPv4 address would be reversible by anybody with
/// an afternoon and four billion guesses, so the salt is what makes the sentence above true.
/// </para>
/// </remarks>
public sealed class SubmissionSource
{
    private readonly byte[] _salt = RandomNumberGenerator.GetBytes(32);

    /// <summary>What a request with no address at all counts as. One bucket, so it is still bounded.</summary>
    public const string Unknown = "unknown";

    public string Of(IPAddress? address) => Of(address?.ToString() ?? Unknown);

    public string Of(string address)
    {
        ArgumentNullException.ThrowIfNull(address);

        var digest = HMACSHA256.HashData(_salt, Encoding.UTF8.GetBytes(address));

        return Convert.ToHexStringLower(digest);
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
/// <b>The order of the checks is the design.</b> The rate limit first, because it is the only one
/// that costs nothing and it must not be bypassable by sending rubbish. Then the address, then our
/// own catalogue, and only then DNS — so the form cannot be used as a free resolver. §7.2's gate runs
/// on the resolved address, before the target is written, so a refused name never reaches the
/// registry and is never dialled by anything.
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

        var recent = await log.CountSinceAsync(source, now - options.Window, ct);

        if (recent >= options.PerSource)
        {
            // Deliberately before anything else, and deliberately unlogged. A source at its limit
            // must not be able to make us parse, read or resolve anything, and a row per refusal
            // would slide the window forward for as long as somebody kept knocking.
            return new SubmissionReceipt(SubmissionOutcome.TooMany);
        }

        if (!SubmittedAddressReader.TryRead(host, port, out var address))
        {
            await RecordAsync(null, SubmissionOutcome.Malformed, null, source, now, ct);
            return new SubmissionReceipt(SubmissionOutcome.Malformed);
        }

        if (await endpoints.ByAddressAsync(address.Host, address.Port, ct) is { } known)
        {
            await RecordAsync(address, SubmissionOutcome.AlreadyListed, null, source, now, ct);
            return new SubmissionReceipt(SubmissionOutcome.AlreadyListed, address, known.GameId);
        }

        if (await targets.ByAddressAsync(address.Host, address.Port, ct) is not null)
        {
            await RecordAsync(address, SubmissionOutcome.AlreadyQueued, null, source, now, ct);
            return new SubmissionReceipt(SubmissionOutcome.AlreadyQueued, address);
        }

        var decision = await scope.InspectAsync(address.Host, ct);

        if (decision.Ruling is not HostScopeRuling.Allowed)
        {
            var outcome = decision.Ruling is HostScopeRuling.RefusedNonGlobal
                ? SubmissionOutcome.RefusedNotRoutable
                : SubmissionOutcome.Unresolvable;

            await RecordAsync(address, outcome, null, source, now, ct);
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

        await RecordAsync(address, SubmissionOutcome.Accepted, id, source, now, ct);

        return new SubmissionReceipt(SubmissionOutcome.Accepted, address);
    }

    private Task RecordAsync(
        SubmittedAddress? address,
        SubmissionOutcome outcome,
        Guid? crawlTargetId,
        string source,
        DateTimeOffset now,
        CancellationToken ct) =>
        log.RecordAsync(
            new SubmissionRecord(
                Guid.CreateVersion7(),
                address?.Host,
                address?.Port,
                now,
                outcome,
                crawlTargetId,
                source),
            ct);
}
