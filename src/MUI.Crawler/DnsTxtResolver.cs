using DnsClient;
using DnsClient.Protocol;

using MUI.Discovery;

namespace MUI.Crawler;

/// <summary>
/// The live TXT lookup behind spec §11's DNS opt-out.
/// </summary>
/// <remarks>
/// Three answers, not two: a name with records and a name with none are both answers and may be acted
/// on, but a resolver that timed out, refused or SERVFAILed is <em>not</em> an answer — collapsing
/// that into "no record" would let one bad minute at a nameserver withdraw an opt-out somebody meant.
/// NXDOMAIN counts as an answer, since that's what nearly every host without an opt-out record
/// replies. The library's cache respects the record's TTL; failures are cached too, briefly, so a
/// nameserver that just refused us isn't re-asked every target every cycle.
/// <b>Every failure — including a local <see cref="ArgumentException"/> DnsClient throws before any
/// packet is sent, e.g. for a too-long name a stranger's <c>REFERRAL</c> can trigger — must return "we
/// heard nothing" rather than escape.</b> An escape here lands in the crawl loop's catch-all, which
/// counts an error and never records the attempt, leaving the target due forever and burning a batch
/// slot every cycle on a name an attacker chose.
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
            // Short and un-retried: this runs inside a crawl cycle's concurrency slot, and a slow
            // nameserver must not hold one.
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
    /// The bound this class applies on top of whatever the client promises — the crawler shares a
    /// process with the web tier (§12), so a collaborator isn't trusted for the one bound that keeps
    /// the site up.
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
            // A caller that stopped us is not a fact about anybody's nameserver.
            cancellationToken.ThrowIfCancellationRequested();

            // Everything else — refusal, timeout, budget expiry, a name DnsClient won't send — is
            // "we heard nothing" (see class remarks).
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
