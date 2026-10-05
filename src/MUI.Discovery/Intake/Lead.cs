using System.Globalization;
using System.Text.RegularExpressions;

using MUI.Catalog;
using MUI.Crawl;

namespace MUI.Discovery;

/// <summary>
/// Where a lead's address was read, and which channel led us there.
/// </summary>
/// <param name="EvidenceUrl">
/// The page the address was read from, word for word — the announcement itself, or the game's own
/// website that the announcement linked to.
/// </param>
/// <param name="PostUrl">
/// The announcement, when the address was found one link further on. Null when it is the evidence page
/// itself.
/// </param>
/// <param name="Channel">
/// A short label for where the routine was reading (<c>reddit</c>, <c>gemini</c>, …). Our note about
/// our own crawl; never reaches a game.
/// </param>
public sealed record LeadEvidence(Uri EvidenceUrl, Uri? PostUrl, string Channel);

/// <summary>What became of one lead, as the <c>crawl_lead</c> table holds it.</summary>
public sealed record LeadRecord(
    Guid Id,
    string? Host,
    int? Port,
    LeadEvidence Evidence,
    DateTimeOffset FoundAt,
    SubmissionOutcome? Outcome,
    Guid? CrawlTargetId);

/// <summary>What <see cref="LeadService"/> did, or in a dry run would have done.</summary>
/// <param name="Outcome">What we did. <see cref="SubmissionOutcome.TooMany"/> when the daily bound is spent.</param>
/// <param name="Address">The address as we read it, or null when nothing could be read.</param>
/// <param name="GameId">The game that already answers there, for <see cref="SubmissionOutcome.AlreadyListed"/>.</param>
/// <param name="Detail">The scope gate's own words, when it refused.</param>
public sealed record LeadReceipt(
    SubmissionOutcome Outcome,
    SubmittedAddress? Address = null,
    Guid? GameId = null,
    string? Detail = null);

/// <summary>The <c>crawl_lead</c> table: every lead handed in, and the bound on how many.</summary>
/// <remarks>
/// The same two-step shape as <see cref="ISubmissionLog"/>, for the same reason: counting then
/// inserting is check-then-act, so <see cref="TryBeginAsync"/> takes the slot and writes the row in one
/// serialised step, and <see cref="CompleteAsync"/> fills in what happened.
/// </remarks>
public interface ILeadLog
{
    Task<bool> TryBeginAsync(
        Guid id,
        LeadEvidence evidence,
        DateTimeOffset now,
        int perWindow,
        DateTimeOffset since,
        CancellationToken ct);

    Task CompleteAsync(
        Guid id,
        SubmittedAddress? address,
        SubmissionOutcome outcome,
        Guid? crawlTargetId,
        CancellationToken ct);

    /// <summary>The newest leads first, so the routine can skip pages it has already read.</summary>
    Task<IReadOnlyList<LeadRecord>> RecentAsync(int limit, CancellationToken ct);
}

/// <summary>The bound on how many leads one window may hand in.</summary>
public sealed record LeadOptions
{
    /// <summary>
    /// How many leads <see cref="Window"/> may take.
    /// </summary>
    /// <remarks>
    /// A ceiling on a routine that misreads a page, not a politeness bound — what we do to other
    /// people's servers is <c>CrawlRateLimiter</c>'s business, and a lead is probed on the same
    /// schedule as anything else. Thirty a day is a guess to revisit once real runs show the volume.
    /// </remarks>
    public int PerWindow { get; init; } = 30;

    public TimeSpan Window { get; init; } = TimeSpan.FromDays(1);

    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(PerWindow, 1, nameof(PerWindow));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(Window, TimeSpan.Zero, nameof(Window));
    }
}

/// <summary>
/// An address a public announcement named, handed in by staff's lead-finding routine.
/// </summary>
/// <remarks>
/// <para>
/// <b>A lead is a submission with a different author, and is held to the same standard.</b> The
/// routine is a model reading forum posts and the websites they link to; what it hands in is an
/// address it believes a game answers at, which is exactly what a stranger's form submission is. So
/// the target carries <see cref="CrawlTarget.SubmittedAt"/>: <c>CatalogueBinder</c> mints a game only
/// once the server identifies itself, and the listing withholds it until §7.8 corroborates it. A model
/// that misreads a web server's port costs one probe and puts nothing on a page.
/// </para>
/// <para>
/// <b>An address and nothing else.</b> No name, codebase or description travels with a lead, for the
/// reason the form takes none (§7.6): every fact on this site is measured. The evidence URL is kept in
/// <c>crawl_lead</c>, our own note, and never on a game — <see cref="DiscoverySource.Announcement"/>
/// names no site, because a game announced on one forum is announced on three.
/// </para>
/// <para>
/// <b>Not <c>crawl_seed_add</c>.</b> An operator seed is somebody at our end choosing an address on
/// purpose: it records <c>operator_seed</c>, and answering at all mints a public game. Neither is true
/// of a lead.
/// </para>
/// </remarks>
public sealed partial class LeadService(
    ICrawlTargetRepository targets,
    IEndpointDirectory endpoints,
    IHostScopeGuard scope,
    OptOutGate optOut,
    ILeadLog log,
    LeadOptions options,
    TimeProvider time)
{
    /// <summary>The longest evidence URL we keep. Long enough for any real link, short enough to index.</summary>
    public const int MaxUrlLength = 2048;

    private readonly AddressIntake intake = new(targets, endpoints, scope, optOut);

    /// <summary>
    /// Hands one lead in.
    /// </summary>
    /// <param name="dryRun">
    /// Run every check and say what would happen, writing nothing: no target, no <c>crawl_lead</c> row,
    /// no slot taken. For checking a routine's output by hand before it is trusted to write.
    /// </param>
    /// <exception cref="ArgumentException">The evidence is not something we can keep.</exception>
    public async Task<LeadReceipt> SubmitAsync(
        string? host,
        int port,
        LeadEvidence evidence,
        bool dryRun = false,
        CancellationToken ct = default)
    {
        Validate(evidence);

        var now = time.GetUtcNow();
        var reservation = Guid.CreateVersion7();

        if (!dryRun
            && !await log.TryBeginAsync(reservation, evidence, now, options.PerWindow, now - options.Window, ct))
        {
            return new LeadReceipt(SubmissionOutcome.TooMany);
        }

        if (!SubmittedAddressReader.TryRead(host, port.ToString(CultureInfo.InvariantCulture), out var address))
        {
            return await CompleteAsync(dryRun, reservation, new LeadReceipt(SubmissionOutcome.Malformed), null, ct);
        }

        var ruling = await intake.RuleAsync(address, ct);

        if (ruling.Refusal is { } refusal)
        {
            return await CompleteAsync(
                dryRun, reservation, new LeadReceipt(refusal, address, ruling.GameId, ruling.Detail), null, ct);
        }

        if (dryRun)
        {
            return new LeadReceipt(SubmissionOutcome.Accepted, address);
        }

        // Due now: an announcement is a game that has just opened, and the probe is what decides
        // whether it is one. IsOperatorSeed stays false — §7.2's exemption is never inferred.
        var id = await targets.AddAsync(
            new CrawlTarget
            {
                Id = Guid.CreateVersion7(),
                Host = address.Host,
                Port = address.Port,
                NextProbeAt = now,
                FirstSeenAt = now,
                SubmittedAt = now,
                DiscoveredVia = DiscoverySource.Announcement,
            },
            ct);

        return await CompleteAsync(dryRun, reservation, new LeadReceipt(SubmissionOutcome.Accepted, address), id, ct);
    }

    /// <summary>
    /// Refuses evidence that is not a web page we could show an operator.
    /// </summary>
    /// <remarks>
    /// Thrown rather than logged as <see cref="SubmissionOutcome.Malformed"/>: a lead with no source
    /// page is the routine's mistake, not a fact about an address, and it takes no slot.
    /// </remarks>
    public static void Validate(LeadEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        RequireWebPage(evidence.EvidenceUrl, nameof(LeadEvidence.EvidenceUrl));

        if (evidence.PostUrl is { } post)
        {
            RequireWebPage(post, nameof(LeadEvidence.PostUrl));
        }

        if (evidence.Channel is null || !ChannelShape().IsMatch(evidence.Channel))
        {
            throw new ArgumentException(
                "channel must be a short lower-case label such as reddit or gemini (a-z, 0-9, - and _, at most 32).",
                nameof(evidence));
        }
    }

    private static void RequireWebPage(Uri url, string name)
    {
        if (url is not { IsAbsoluteUri: true } || (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp))
        {
            throw new ArgumentException($"{name} must be an absolute http or https URL.", name);
        }

        if (url.AbsoluteUri.Length > MaxUrlLength)
        {
            throw new ArgumentException($"{name} is longer than {MaxUrlLength} characters.", name);
        }
    }

    private async Task<LeadReceipt> CompleteAsync(
        bool dryRun, Guid reservation, LeadReceipt receipt, Guid? targetId, CancellationToken ct)
    {
        if (!dryRun)
        {
            await log.CompleteAsync(reservation, receipt.Address, receipt.Outcome, targetId, ct);
        }

        return receipt;
    }

    [GeneratedRegex("^[a-z0-9_-]{1,32}$", RegexOptions.CultureInvariant)]
    private static partial Regex ChannelShape();
}
