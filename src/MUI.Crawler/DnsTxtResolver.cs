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
/// often they want to be asked. This is not §7.2's resolution, where a cache would <em>widen</em> the
/// time-of-check-to-time-of-use window — nothing here decides where a socket goes.
/// </para>
/// </remarks>
public sealed class DnsTxtResolver : IDnsTxtResolver
{
    private readonly ILookupClient _client;

    public DnsTxtResolver()
        : this(new LookupClient(new LookupClientOptions
        {
            // Short, because this runs inside a crawl cycle's budget and a slow nameserver must not
            // hold a probe slot. A lookup that does not finish is "we heard nothing", which is safe.
            Timeout = TimeSpan.FromSeconds(3),
            Retries = 1,
            ThrowDnsErrors = false,
        }))
    {
    }

    /// <param name="client">
    /// The resolver to ask. Injected so a test can point this at a nameserver it runs itself and
    /// exercise the real wire format without depending on anybody's zone file.
    /// </param>
    public DnsTxtResolver(ILookupClient client) => _client = client;

    public async Task<DnsTxtAnswer> LookupAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        IDnsQueryResponse response;

        try
        {
            response = await _client.QueryAsync(name, QueryType.TXT, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (DnsResponseException)
        {
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
