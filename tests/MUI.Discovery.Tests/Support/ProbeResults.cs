using MUI.Crawl;

namespace MUI.Discovery.Tests.Support;

/// <summary>
/// Captured-fixture-shaped <see cref="ProbeResult"/>s. Every downstream behaviour in this suite is
/// exercised against one of these with no network anywhere in sight (spec §6.5, §13).
/// </summary>
public static class ProbeResults
{
    public static readonly DateTimeOffset Observed = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// An MSSP report, in the shape the probe now produces: name → value <b>list</b>, in wire order.
    /// </summary>
    /// <remarks>
    /// Repeating a variable here repeats it on the wire, which is MSSP's own way of expressing a list
    /// and the one <c>REFERRAL</c> and <c>PORT</c> arrive in — <c>coffeemud.net:2327</c> reports
    /// <c>PORT</c> nine times. This used to be a flat map, so a fixture had to glue several entries
    /// into one value and the parser had to pull them apart again; both halves of that are gone.
    /// <see cref="List"/> remains for the other real shape, several entries <em>inside</em> one value.
    /// </remarks>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Mssp(
        params (string Variable, string Value)[] variables)
    {
        var report = new OrderedDictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (variable, value) in variables)
        {
            report[variable] = report.TryGetValue(variable, out var existing)
                ? [.. existing, value]
                : [value];
        }

        return report;
    }

    /// <summary>Several entries packed into one value, newline-separated — the other shape servers send.</summary>
    public static string List(params string[] entries) => string.Join('\n', entries);

    public static ProbeResult Answered(
        string host = "mud.example.org",
        int port = 4201,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? mssp = null,
        string? banner = null,
        WhoReading? who = null,
        DateTimeOffset? at = null) => new()
    {
        Host = host,
        Port = port,
        ObservedAt = at ?? Observed,
        Outcome = ProbeOutcome.Answered,
        Mssp = mssp ?? MsspReport.Empty,
        // NotOffered, not Received: a fixture that says nothing about MSSP must not claim the server
        // answered with an empty report, which is a different fact.
        MsspOutcome = mssp is null ? MsspOutcome.NotOffered : MsspOutcome.Received,
        MsspTransport = mssp is null ? MsspTransport.None : MsspTransport.TelnetOption70,
        Banner = banner,
        // NotAsked rather than an unreadable answer, for the same reason: a fixture that says nothing
        // about WHO must not claim we asked and could not read the reply.
        Who = who ?? WhoReading.NotAsked,
    };

    public static ProbeResult Failed(
        string host = "mud.example.org",
        int port = 4201,
        string cause = "Refused",
        DateTimeOffset? at = null) => new()
    {
        Host = host,
        Port = port,
        ObservedAt = at ?? Observed,
        Outcome = ProbeOutcome.Failed,
        Failure = new FailureDetail(cause),
    };
}
