using System.Net;
using MUI.Crawl;

namespace MUI.Discovery.Tests.Support;

/// <summary>
/// DNS, scripted.
/// </summary>
/// <remarks>
/// <b>No test in this suite performs a live lookup.</b> A guard tested against real DNS asserts what
/// somebody else's zone file says today, and would go green or red for reasons that have nothing to do
/// with this code.
/// </remarks>
public sealed class FakeHostResolver : IHostResolver
{
    private readonly Dictionary<string, IReadOnlyList<IPAddress>> _answers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every name the guard asked about, so "it never looked" is assertable.</summary>
    public List<string> Asked { get; } = [];

    public FakeHostResolver Resolving(string host, params string[] addresses)
    {
        _answers[host] = addresses.Select(IPAddress.Parse).ToList();
        return this;
    }

    /// <summary>A name with no record at all. Not a refusal — a dead host.</summary>
    public FakeHostResolver Failing(string host)
    {
        _answers[host] = [];
        return this;
    }

    public Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken = default)
    {
        Asked.Add(host);

        if (_answers.TryGetValue(host, out var scripted))
        {
            return Task.FromResult(scripted);
        }

        // A literal resolves to itself, exactly as SystemHostResolver does, so a test can hand the
        // guard a raw address without scripting it.
        return Task.FromResult<IReadOnlyList<IPAddress>>(
            IPAddress.TryParse(host, out var literal) ? [literal] : []);
    }
}
