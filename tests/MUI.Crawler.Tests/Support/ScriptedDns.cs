using System.Collections.Concurrent;

using MUI.Discovery;

namespace MUI.Crawler.Tests.Support;

/// <summary>
/// TXT lookups, scripted, so a cycle test can exercise §11's DNS opt-out without a nameserver.
/// </summary>
/// <remarks>
/// The real resolver has its own test in this suite, against a nameserver the test runs itself —
/// this one exists so that every <em>other</em> assertion about a crawl cycle costs no DNS traffic
/// and cannot go red because somebody's resolver was slow.
/// </remarks>
public sealed class ScriptedDns : IDnsTxtResolver
{
    private readonly Dictionary<string, DnsTxtAnswer> _answers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Every name asked about. Concurrent because a crawl cycle asks from its worker pool — a plain
    /// list written from several threads is a flake waiting for a busy machine.
    /// </summary>
    public ConcurrentQueue<string> Asked { get; } = [];

    public ScriptedDns Publishing(string host, params string[] records)
    {
        _answers[OptOutVocabulary.DnsNameFor(host)] = DnsTxtAnswer.Of(records);
        return this;
    }

    public Task<DnsTxtAnswer> LookupAsync(string name, CancellationToken cancellationToken = default)
    {
        Asked.Enqueue(name);

        return Task.FromResult(_answers.TryGetValue(name, out var scripted) ? scripted : DnsTxtAnswer.NoRecord);
    }
}
