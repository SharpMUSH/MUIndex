namespace MUI.Discovery.Tests.Support;

/// <summary>
/// TXT lookups, scripted.
/// </summary>
/// <remarks>
/// The three answers a resolver can give are all here and they are three rather than two: a name with
/// records, a name with none, and a resolver that did not answer at all. Only the middle one may
/// withdraw an opt-out, so a fake that could not express the third would make the difference
/// untestable.
/// </remarks>
public sealed class FakeDnsTxtResolver : IDnsTxtResolver
{
    private readonly Dictionary<string, DnsTxtAnswer> _answers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every name asked about, so "it never looked" is assertable.</summary>
    public List<string> Asked { get; } = [];

    /// <summary>What an unscripted name answers. "No such record" by default, as DNS mostly does.</summary>
    public DnsTxtAnswer Default { get; set; } = DnsTxtAnswer.NoRecord;

    public FakeDnsTxtResolver Publishing(string name, params string[] records)
    {
        _answers[name] = DnsTxtAnswer.Of(records);
        return this;
    }

    /// <summary>A resolver that did not answer — a timeout or a SERVFAIL, not an absence.</summary>
    public FakeDnsTxtResolver Silent(string name)
    {
        _answers[name] = DnsTxtAnswer.NoAnswer;
        return this;
    }

    public Task<DnsTxtAnswer> LookupAsync(string name, CancellationToken cancellationToken = default)
    {
        Asked.Add(name);

        return Task.FromResult(_answers.TryGetValue(name, out var scripted) ? scripted : Default);
    }
}
