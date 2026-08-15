using DnsClient;
using DnsClient.Protocol;

using MUI.Discovery;

namespace MUI.Crawler;

/// <summary>
/// The live TXT lookup behind spec §11's DNS opt-out.
/// </summary>
/// <remarks>
/// <para>
/// <b>Three answers, not two</b>, and the whole reason this type is not a one-liner. A name with
/// records and a name with none are both answers and both may be acted on; a resolver that timed out,
/// refused or SERVFAILed is <em>not</em> an answer and may not be. Collapsing the third into the
/// second would let one bad minute at a nameserver withdraw an opt-out somebody meant, which is the
/// same class of mistake as §8.4's "absence never revokes".
/// </para>
/// <para>
/// <b>NXDOMAIN is an answer.</b> It is what a host with no opt-out record replies, which is nearly
/// every host, so treating DnsClient's error flag as failure across the board would mean never
/// concluding anything about anybody.
/// </para>
/// <para>
/// The library's cache is left on and respects the record's own TTL, which is the operator saying how
/// often they want to be asked. Failures are cached too, briefly: the answer for nearly every host is
/// "no such record", and re-asking a nameserver that has just refused us, once per due target per
/// cycle, is traffic somebody else pays for. This is not §7.2's resolution, where a cache would
/// <em>widen</em> the time-of-check-to-time-of-use window — nothing here decides where a socket goes.
/// </para>
/// <para>
/// <b>Every failure is "we heard nothing", including the ones that are not exceptions from the
/// network.</b> DnsClient rejects a name it will not put on the wire — a label over 63 bytes, a name
/// over 255 — with <see cref="ArgumentException"/> before any packet is sent, and both arrive from a
/// stranger's <c>REFERRAL</c>: the second needs only a legal 243-byte name, because our own
/// <c>_muindex.</c> prefix pushes it past the limit. This gate runs before
/// <see cref="MUI.Discovery.HostScopeGuard"/>, which fails closed on anything, so an escape here
/// would land in the crawl loop's catch-all, which counts an error and never records the attempt —
/// leaving the target due for ever and burning a batch slot every cycle on a name an attacker chose.
/// </para>
/// </remarks>
public sealed class DnsTxtResolver : IDnsTxtResolver
{
    /// <summary>How long one lookup may take in total, whatever the client was configured with.</summary>
    private static readonly TimeSpan DefaultBound = TimeSpan.FromSeconds(2);

    private readonly ILookupClient _client;
    private readonly TimeSpan _bound;

    public DnsTxtResolver()
        : this(new LookupClient(new LookupClientOptions
        {
            // Short and un-retried, because this runs inside a crawl cycle's concurrency slot and a
            // slow nameserver must not hold one. A lookup that does not finish is "we heard nothing",
            // which changes no standing opt-out and is therefore safe to give up on.
            Timeout = TimeSpan.FromMilliseconds(1500),
            Retries = 0,
            ThrowDnsErrors = false,
            CacheFailedResults = true,
            FailedResultsCacheDuration = TimeSpan.FromMinutes(5),
        }))
    {
    }

    /// <param name="client">
    /// The resolver to ask. Injected so a test can point this at a nameserver it runs itself and
    /// exercise the real wire format without depending on anybody's zone file.
    /// </param>
    /// <param name="bound">
    /// <b>The bound this class applies on top of whatever the client promises</b>, for the reason
    /// <c>CrawlCycle</c> applies its own on top of <c>ProbeOptions.Timeout</c>: the crawler shares a
    /// process with the web tier (§12), and the caller does not get to trust a collaborator for the
    /// one bound that keeps the site up.
    /// </param>
    public DnsTxtResolver(ILookupClient client, TimeSpan? bound = null)
    {
        _client = client;
        _bound = bound ?? DefaultBound;
    }

    public async Task<DnsTxtAnswer> LookupAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        cancellationToken.ThrowIfCancellationRequested();

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(_bound);

        IDnsQueryResponse response;

        try
        {
            response = await _client.QueryAsync(name, QueryType.TXT, cancellationToken: budget.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The host is stopping, which is not a fact about anybody's nameserver.
            throw;
        }
        catch (Exception error) when (error is not OperationCanceledException || budget.IsCancellationRequested)
        {
            // A caller that stopped us is not a fact about anybody's nameserver, and some failures
            // arrive from the library as its own exception type rather than as a cancellation.
            cancellationToken.ThrowIfCancellationRequested();

            // Everything else is "we heard nothing": a refusal, a timeout, our own budget running
            // out, or a name DnsClient would not send. None of them may withdraw a standing opt-out,
            // and none of them may escape into a crawl loop that would then stop rescheduling this
            // target.
            return DnsTxtAnswer.NoAnswer;
        }

        if (response.HasError && response.Header.ResponseCode is not DnsHeaderResponseCode.NotExistentDomain)
        {
            return DnsTxtAnswer.NoAnswer;
        }

        // A TXT record is a list of strings chopped at 255 bytes on the wire, and the chop is a
        // transport detail rather than something an operator typed. Joining per record is what makes
        // one long record read as one string.
        var records = response.Answers
            .OfType<TxtRecord>()
            .Select(record => string.Concat(record.Text))
            .Where(text => text.Length > 0)
            .ToList();

        return new DnsTxtAnswer(true, records);
    }
}
