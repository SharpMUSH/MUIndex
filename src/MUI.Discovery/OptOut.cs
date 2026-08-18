using MUI.Crawl;

using Microsoft.Extensions.Logging;

namespace MUI.Discovery;

/// <summary>
/// What we tell a server operator to type when they want the crawler to stop (spec §11).
/// </summary>
/// <remarks>
/// <para>
/// <b>These spellings are a published contract</b>, exactly as <see cref="ClaimTokenBeacon"/>'s are.
/// They appear on the about page, which reads them off this class rather than restating them.
/// Changing a value here is a migration, not an edit: it changes what every operator who already
/// opted out has typed into their config or zone file.
/// </para>
/// <para>
/// <b>The two channels are scoped differently on purpose.</b> An MSSP field is published by the
/// listener that answered and stops only that listener — two unrelated games sharing a hosting
/// domain, separated only by port, must never be able to silence each other. A TXT record at
/// <c>_muindex.&lt;host&gt;</c> is the domain's own operator speaking about a machine they run, so it
/// stops every port on that host unless it names one.
/// </para>
/// </remarks>
public static class OptOutVocabulary
{
    /// <summary>The MSSP variable a game sets to be left alone.</summary>
    public const string MsspVariable = "MUINDEX OPT-OUT";

    /// <summary>
    /// Every MSSP spelling an opt-out is accepted from, canonical first.
    /// </summary>
    /// <remarks>
    /// A variable name doesn't reliably survive a config file, and an operator who did what they were
    /// told must not go on being crawled because their codebase folded a space into an underscore —
    /// of every place to be strict, the one where somebody is asking us to go away is the worst.
    /// </remarks>
    public static readonly IReadOnlyList<string> AcceptedMsspVariables =
        [MsspVariable, "MUINDEX_OPT_OUT", "MUINDEX OPTOUT", "CRAWL_OPT_OUT"];

    /// <summary>The DNS label the record lives under — the same one §8's deferred claim channel names.</summary>
    public const string DnsLabel = ClaimTokenBeacon.DnsLabel;

    /// <summary>The token a TXT record carries, e.g. <c>_muindex.example.org. IN TXT "opt-out"</c>.</summary>
    public const string DnsValue = "opt-out";

    /// <summary>
    /// The values that mean "no, keep crawling", so a game can leave the line in its config and turn
    /// it off.
    /// </summary>
    private static readonly string[] Negatives = ["0", "no", "false", "off", "opt-in", "optin"];

    /// <summary>The fully-qualified name an opt-out record for this host lives at.</summary>
    public static string DnsNameFor(string host) => $"{DnsLabel}.{CanonicalHost.Normalize(host)}";

    /// <summary>
    /// The opt-out this MSSP report carries, or null.
    /// </summary>
    /// <remarks>
    /// <b>Any value not in <see cref="Negatives"/> is an opt-out</b> — the safe direction: an operator
    /// who typed the variable meant something by it. Negatives are enumerated so leaving the line set
    /// to <c>0</c> works, for an operator who changed their mind. A negative does not end the search:
    /// one spelling saying stop is the report saying stop, so a template shipping
    /// <c>MUINDEX OPT-OUT 0</c> can't silently overrule the <c>CRAWL_OPT_OUT 1</c> an operator added
    /// themselves.
    /// </remarks>
    public static MsspOptOut? ReadMssp(IReadOnlyDictionary<string, IReadOnlyList<string>> mssp)
    {
        ArgumentNullException.ThrowIfNull(mssp);

        foreach (var variable in AcceptedMsspVariables)
        {
            if (MsspReading.Value(mssp, variable) is not { } raw)
            {
                continue;
            }

            var value = raw.Trim();

            if (value.Length == 0 || Negatives.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            return new MsspOptOut(variable, value);
        }

        return null;
    }

    /// <summary>
    /// What a TXT answer says about dialling this port, or null when it says nothing about it.
    /// </summary>
    /// <remarks>
    /// The grammar is deliberately forgiving: tokens separated by whitespace or semicolons, so
    /// <c>"v=muindex1; opt-out"</c> and a bare <c>"opt-out"</c> both work; a token may qualify itself
    /// with ports as <c>opt-out=4201,4202</c>; matching is case-insensitive. A record naming other
    /// ports is not an answer about this one — the caller proceeds as if there were no record at all.
    /// </remarks>
    public static DnsOptOut? ReadDns(IEnumerable<string> records, int port)
    {
        ArgumentNullException.ThrowIfNull(records);

        foreach (var record in records)
        {
            foreach (var token in record.Split([' ', '\t', ';'], StringSplitOptions.RemoveEmptyEntries))
            {
                if (Matches(token, out var qualifier) && Applies(qualifier, port, out var scope))
                {
                    return new DnsOptOut(scope, record.Trim());
                }
            }
        }

        return null;

        static bool Matches(string token, out string qualifier)
        {
            qualifier = string.Empty;

            foreach (var spelling in (string[])[DnsValue, "optout"])
            {
                if (!token.StartsWith(spelling, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var rest = token[spelling.Length..];

                if (rest.Length == 0)
                {
                    return true;
                }

                if (rest[0] is '=' or ':')
                {
                    qualifier = rest[1..];
                    return true;
                }
            }

            return false;
        }

        static bool Applies(string qualifier, int port, out int? scope)
        {
            scope = null;

            if (qualifier.Length == 0)
            {
                // Unqualified: the whole host.
                return true;
            }

            var parts = qualifier.Split(',', StringSplitOptions.RemoveEmptyEntries);

            // An unparseable qualifier (e.g. "opt-out=all") reads as the whole host, not as no
            // opt-out — the crawler must not carry on dialling because it didn't recognise the value.
            if (parts.Any(part => !int.TryParse(part.Trim(), out _)))
            {
                return true;
            }

            foreach (var part in parts)
            {
                if (int.TryParse(part.Trim(), out var named) && named == port)
                {
                    scope = named;
                    return true;
                }
            }

            return false;
        }
    }
}

/// <summary>An opt-out read off an MSSP report, with the spelling the server actually used.</summary>
public sealed record MsspOptOut(string Variable, string Value)
{
    public string Detail => $"MSSP {Variable} = {Value}";
}

/// <summary>An opt-out read out of DNS, and how much of the host it covers.</summary>
/// <param name="Port">The single port named by the record, or null for every port on the host.</param>
/// <param name="Record">The record as published, so the decision can be explained later.</param>
public sealed record DnsOptOut(int? Port, string Record)
{
    public string Detail => $"TXT {Record}";
}

/// <summary>Which of §11's three routes carried an opt-out.</summary>
/// <remarks>
/// Not a detail of storage: the routes behave differently. Only <see cref="DnsTxt"/> can be re-read
/// without connecting to a server that has asked us not to, so only <see cref="DnsTxt"/> can withdraw
/// itself. The other two stand until somebody tells us otherwise, which is the honest consequence of
/// having stopped connecting.
/// </remarks>
public enum OptOutSource
{
    /// <summary>The game published <see cref="OptOutVocabulary.MsspVariable"/> in its MSSP report.</summary>
    Mssp,

    /// <summary>A TXT record at <c>_muindex.&lt;host&gt;</c> asked us to stop.</summary>
    DnsTxt,

    /// <summary>
    /// Somebody asked, and an operator of this deployment recorded it.
    /// </summary>
    /// <remarks>
    /// Recorded by a person who can say who asked — never defaulted, never inferred. A claim about
    /// somebody else's wishes must never be compiled in by whoever typed the code; the detail is
    /// required and must say who asked.
    /// </remarks>
    Request,
}

/// <summary>
/// A standing request not to be crawled (spec §11).
/// </summary>
/// <remarks>
/// Keyed on the address rather than on a game, because the address is what we dial and because an
/// opt-out has to work for a host we have never listed — the operator most likely to want us gone is
/// the one least likely to have claimed anything with us first.
/// </remarks>
public sealed record CrawlOptOut
{
    public required string Host { get; init; }

    /// <summary>The single port this covers, or null for every port on the host.</summary>
    public int? Port { get; init; }

    public required OptOutSource Source { get; init; }

    /// <summary>When they first asked.</summary>
    public required DateTimeOffset RecordedAt { get; init; }

    /// <summary>When we last saw them say it. Only DNS can move this; see <see cref="OptOutSource"/>.</summary>
    public required DateTimeOffset LastConfirmedAt { get; init; }

    /// <summary>What we read, or who asked and how.</summary>
    public required string Detail { get; init; }

    /// <summary>Set when the route that carried it took it back. The row is kept.</summary>
    public DateTimeOffset? WithdrawnAt { get; init; }

    public bool Standing => WithdrawnAt is null;

    /// <summary>Whether this covers a given address.</summary>
    public bool Covers(string host, int port) =>
        string.Equals(Host, CanonicalHost.Normalize(host), StringComparison.OrdinalIgnoreCase)
        && (Port is null || Port == port);

    /// <summary>One line an operator reading a log can act on.</summary>
    public string Wording =>
        $"{Host}{(Port is { } port ? $":{port}" : " (every port)")} asked not to be crawled "
        + $"on {RecordedAt:yyyy-MM-dd} via {Source switch
        {
            OptOutSource.Mssp => "MSSP",
            OptOutSource.DnsTxt => "DNS",
            _ => "a recorded request",
        }} — {Detail}";
}

/// <summary>
/// Where standing opt-outs are kept.
/// </summary>
/// <remarks>
/// <b>There is no delete.</b> An opt-out that is taken back is withdrawn and keeps its row, because
/// "they asked us to stop, and later asked us back" is a thing this record has to be able to say —
/// and because a deletion is the one edit that cannot be reviewed afterwards.
/// </remarks>
public interface ICrawlOptOutRepository
{
    /// <summary>The standing opt-out covering this address, or null. The earliest ask wins the report.</summary>
    Task<CrawlOptOut?> StandingAsync(string host, int port, CancellationToken ct);

    /// <summary>
    /// Records an opt-out, or confirms one we already hold, and returns what is now stored.
    /// </summary>
    /// <remarks>
    /// A repeat sighting moves <c>last_confirmed_at</c> and un-withdraws, leaving the date they first
    /// asked alone. <see cref="OptOutRecording.IsFirstAsk"/> is reported by the store rather than
    /// worked out by the caller: comparing the stored date against the clock is wrong against a real
    /// database, since <c>timestamptz</c> keeps microseconds and <see cref="DateTimeOffset"/> counts
    /// 100ns ticks, so the round-tripped value rarely equals the one that went in.
    /// </remarks>
    Task<OptOutRecording> RecordAsync(CrawlOptOut optOut, CancellationToken ct);

    /// <summary>Marks one route's opt-out as taken back, keeping the row.</summary>
    Task WithdrawAsync(string host, int? port, OptOutSource route, DateTimeOffset at, CancellationToken ct);

    /// <summary>Everything ever recorded, withdrawn included — the "and recorded" half of §11.</summary>
    Task<IReadOnlyList<CrawlOptOut>> AllAsync(CancellationToken ct);
}

/// <summary>What one write to the register did.</summary>
/// <param name="OptOut">The row as it now stands, with the date they first asked.</param>
/// <param name="IsFirstAsk">Whether this call is the one that recorded it (see <see cref="ICrawlOptOutRepository.RecordAsync"/>).</param>
public sealed record OptOutRecording(CrawlOptOut OptOut, bool IsFirstAsk);

/// <summary>A TXT lookup's answer, which has three outcomes and not two.</summary>
/// <param name="Answered">
/// Whether DNS answered at all. <b>"No such record" is an answer; "the resolver did not reply" is
/// not</b>, and only the first may withdraw a standing opt-out — treating a timeout as a withdrawal
/// would let a resolver hiccup put us back on somebody's doorstep.
/// </param>
/// <param name="Records">The TXT records at that name, each already joined from its wire chunks.</param>
public sealed record DnsTxtAnswer(bool Answered, IReadOnlyList<string> Records)
{
    /// <summary>DNS did not answer. Nothing may be concluded from it.</summary>
    public static readonly DnsTxtAnswer NoAnswer = new(false, []);

    /// <summary>DNS answered, and there is no such record.</summary>
    public static readonly DnsTxtAnswer NoRecord = new(true, []);

    public static DnsTxtAnswer Of(params string[] records) => new(true, records);
}

/// <summary>
/// TXT lookups, injected so that no test in any suite depends on somebody else's zone file.
/// </summary>
/// <remarks>
/// Separate from <see cref="IHostResolver"/> because they answer different questions and fail
/// differently: that one decides whether an address is safe to dial and must fail closed, and this one
/// decides whether we have been asked to stop and must fail towards not concluding anything.
/// </remarks>
public interface IDnsTxtResolver
{
    Task<DnsTxtAnswer> LookupAsync(string name, CancellationToken cancellationToken = default);
}

/// <summary>
/// Spec §11's opt-out, asked before every dial.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is a gate, not a measurement.</b> A refusal happens before a <see cref="ProbeResult"/>
/// exists, so there is no route by which an opt-out can write an availability transition, a presence
/// row or a field — the same structural guarantee <see cref="HostScopeGuard"/> gives §7.2's refusals.
/// <b>Do not reach for <c>ProbeResult.Failed("refused", …)</c></b>:
/// <see cref="MUI.Catalog.FailureCause.Refused"/> means the far end sent an RST, a real measurement of
/// a real host, and dressing our own politeness as one would put it into a game's public reachability
/// history for ever.
/// </para>
/// <para>
/// <b>DNS is re-read every time; the other two routes are never re-read.</b> A TXT record is readable
/// without connecting to a server that asked us not to, so an operator can take their opt-out back by
/// deleting it and we notice within one cycle. An MSSP field can't be re-read without doing the exact
/// thing they asked us to stop doing, so an MSSP or requested opt-out stands until somebody says
/// otherwise.
/// </para>
/// </remarks>
public sealed class OptOutGate(
    ICrawlOptOutRepository optOuts,
    IDnsTxtResolver dns,
    TimeProvider time,
    ILogger<OptOutGate>? logger = null)
{
    /// <summary>
    /// The standing opt-out that forbids this dial, or null to go ahead.
    /// </summary>
    public Task<CrawlOptOut?> RuleOnAsync(CrawlTarget target, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);

        return RuleOnAsync(target.Host, target.Port, cancellationToken);
    }

    /// <summary>
    /// The same ruling on a bare address, for a caller that has no target and must not invent one.
    /// </summary>
    /// <remarks>
    /// The gate is about an address — keyed on one, reads a TXT record at one — and touches no
    /// schedule, depth or game. The overload above is a convenience for the crawl loop, which happens
    /// to hold a target; <b>the public submission form is the other caller</b> (spec §9), with only a
    /// host and port somebody typed, so fabricating a <see cref="CrawlTarget"/> just to ask this
    /// question would put a fake registry row into the world. Ordering is deliberate: the stored
    /// register first (one indexed read), DNS only if that says nothing — an unauthenticated form
    /// reaching this must not spend a lookup per request.
    /// </remarks>
    public async Task<CrawlOptOut?> RuleOnAsync(
        string host,
        int port,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        host = CanonicalHost.Normalize(host);
        var standing = await optOuts.StandingAsync(host, port, cancellationToken);

        // A standing MSSP or requested opt-out is final here: DNS cannot withdraw a request made
        // through another channel, and asking would be a lookup that no answer could act on.
        if (standing is { Source: not OptOutSource.DnsTxt })
        {
            return standing;
        }

        var answer = await dns.LookupAsync(OptOutVocabulary.DnsNameFor(host), cancellationToken);

        if (!answer.Answered)
        {
            // We heard nothing. Not a withdrawal, and not a new opt-out either.
            return standing;
        }

        if (OptOutVocabulary.ReadDns(answer.Records, port) is { } asked)
        {
            var now = time.GetUtcNow();

            var recorded = await optOuts.RecordAsync(
                new CrawlOptOut
                {
                    Host = host,
                    Port = asked.Port,
                    Source = OptOutSource.DnsTxt,
                    RecordedAt = now,
                    LastConfirmedAt = now,
                    Detail = asked.Detail,
                },
                cancellationToken);

            if (recorded.IsFirstAsk)
            {
                logger?.LogInformation(
                    "{Host}:{Port} published {Name} IN TXT \"{Record}\"; we stop dialling it",
                    host, port, OptOutVocabulary.DnsNameFor(host), asked.Record);
            }

            return recorded.OptOut;
        }

        if (standing is null)
        {
            return null;
        }

        // The record they opted out with is gone, and DNS said so rather than failing to answer. An
        // opt-out taken back through the channel that made it is a withdrawal, and the row is kept.
        await optOuts.WithdrawAsync(standing.Host, standing.Port, OptOutSource.DnsTxt, time.GetUtcNow(), cancellationToken);

        logger?.LogInformation(
            "{Name} no longer asks us to stop; {Host}:{Port} is dialled again",
            OptOutVocabulary.DnsNameFor(host), host, port);

        // Another route may still be standing for this address — withdrawing one never speaks for
        // the others.
        return await optOuts.StandingAsync(host, port, cancellationToken);
    }

    /// <summary>
    /// Records an opt-out a probe just read off an MSSP report, and returns it.
    /// </summary>
    /// <remarks>
    /// The probe that reads the field is the last one: what it measured is stored like any other
    /// measurement, and everything after it is refused before a socket is opened. Scoped to the
    /// listener that published it — see <see cref="OptOutVocabulary"/> for why an MSSP field may not
    /// speak for a port it did not answer on.
    /// </remarks>
    public async Task<CrawlOptOut?> HearAsync(
        CrawlTarget target,
        ProbeResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(result);

        if (result.Outcome is not ProbeOutcome.Answered || OptOutVocabulary.ReadMssp(result.Mssp) is not { } asked)
        {
            return null;
        }

        var host = CanonicalHost.Normalize(target.Host);
        var now = time.GetUtcNow();

        var recorded = await optOuts.RecordAsync(
            new CrawlOptOut
            {
                Host = host,
                Port = target.Port,
                Source = OptOutSource.Mssp,
                RecordedAt = now,
                LastConfirmedAt = now,
                Detail = asked.Detail,
            },
            cancellationToken);

        if (recorded.IsFirstAsk)
        {
            logger?.LogInformation(
                "{Host}:{Port} publishes {Variable}; that was the last probe of it",
                host, target.Port, asked.Variable);
        }

        return recorded.OptOut;
    }

    /// <summary>
    /// Records a request somebody made off the wire — mail, a forum post, a message to an operator.
    /// </summary>
    /// <param name="host">The host they asked about.</param>
    /// <param name="port">One port, or null for every port on that host.</param>
    /// <param name="detail">
    /// <b>Who asked and how, and it is required.</b> This deployment's operator is making a claim
    /// about somebody else's wishes, and that claim must never come from a default.
    /// </param>
    public async Task<CrawlOptOut> RecordRequestAsync(
        string host,
        int? port,
        string detail,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);

        var now = time.GetUtcNow();

        var recorded = await optOuts.RecordAsync(
            new CrawlOptOut
            {
                Host = CanonicalHost.Normalize(host),
                Port = port,
                Source = OptOutSource.Request,
                RecordedAt = now,
                LastConfirmedAt = now,
                Detail = detail.Trim(),
            },
            cancellationToken);

        return recorded.OptOut;
    }

    /// <summary>
    /// Takes back a recorded request, on the say-so of somebody who can make it.
    /// </summary>
    /// <remarks>
    /// Deliberately narrower than it looks: withdraws only the <see cref="OptOutSource.Request"/>
    /// route. An MSSP field or TXT record is the game still saying stop on every probe, and the only
    /// way to take one of those back is to stop publishing it — withdrawing them from here would leave
    /// us assuming a retraction we never actually heard, having stopped connecting.
    /// </remarks>
    public Task WithdrawRequestAsync(
        string host,
        int? port,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        return optOuts.WithdrawAsync(
            CanonicalHost.Normalize(host),
            port,
            OptOutSource.Request,
            time.GetUtcNow(),
            cancellationToken);
    }
}
