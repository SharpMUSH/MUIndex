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
    /// An MSSP report. Multi-valued variables — <c>REFERRAL</c> above all — are written as one value
    /// with newline-separated entries, which is one of the shapes real servers send and the one a flat
    /// dictionary can carry.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Mssp(params (string Variable, string Value)[] variables) =>
        variables.ToDictionary(v => v.Variable, v => v.Value, StringComparer.OrdinalIgnoreCase);

    /// <summary>Several entries of one multi-valued variable.</summary>
    public static string List(params string[] entries) => string.Join('\n', entries);

    public static ProbeResult Answered(
        string host = "mud.example.org",
        int port = 4201,
        IReadOnlyDictionary<string, string>? mssp = null,
        string? banner = null,
        WhoReading? who = null,
        DateTimeOffset? at = null) => new()
    {
        Host = host,
        Port = port,
        ObservedAt = at ?? Observed,
        Outcome = ProbeOutcome.Answered,
        Mssp = mssp ?? new Dictionary<string, string>(),
        // NotOffered, not Received: a fixture that says nothing about MSSP must not claim the server
        // answered with an empty report, which is a different fact.
        MsspOutcome = mssp is null ? MsspOutcome.NotOffered : MsspOutcome.Received,
        MsspTransport = mssp is null ? MsspTransport.None : MsspTransport.TelnetOption70,
        Banner = banner,
        Who = who ?? WhoReading.Unread,
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
