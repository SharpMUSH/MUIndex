using MUI.Crawl;

namespace MUI.Crawler.Tests.Support;

/// <summary>
/// Captured-fixture-shaped <see cref="ProbeResult"/>s (spec §6.5, §13).
/// </summary>
/// <remarks>
/// Every ingestion rule in this suite is exercised against one of these with no socket anywhere,
/// which is the property the one-way arrow from <c>MUI.Crawl</c> to <c>MUI.Catalog</c> exists to
/// guarantee. The defaults are the honest ones: a fixture that says nothing about MSSP reports
/// <see cref="MsspOutcome.NotOffered"/> rather than an empty report, and one that says nothing about
/// <c>WHO</c> reports <see cref="WhoReading.NotAsked"/> rather than an unreadable answer — because
/// those are different facts and the whole design turns on not collapsing them.
/// </remarks>
public static class Probes
{
    public static readonly DateTimeOffset Observed = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    /// <summary>An MSSP report in wire shape: name → value <b>list</b>, repeats preserved.</summary>
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

    public static ProbeResult Answered(
        string host = "mud.example.org",
        int port = 4201,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? mssp = null,
        string? banner = null,
        WhoReading? who = null,
        IReadOnlySet<string>? offered = null,
        Negotiation? negotiation = null,
        DateTimeOffset? at = null,
        MsspOutcome? msspOutcome = null,
        string? info = null,
        string? version = null) => new()
    {
        Host = host,
        Port = port,
        ObservedAt = at ?? Observed,
        Outcome = ProbeOutcome.Answered,
        OfferedOptions = offered ?? new HashSet<string>(StringComparer.Ordinal),
        Negotiation = negotiation ?? new Negotiation(),
        Mssp = mssp ?? MsspReport.Empty,
        MsspOutcome = msspOutcome ?? (mssp is null ? MsspOutcome.NotOffered : MsspOutcome.Received),
        MsspTransport = mssp is null ? MsspTransport.None : MsspTransport.TelnetOption70,
        Banner = banner,
        BannerPlayerCount = banner is null ? null : BannerCount.Find(banner),
        Who = who ?? WhoReading.NotAsked,
        Info = info,
        Version = version,
    };

    public static ProbeResult Failed(
        string host = "mud.example.org",
        int port = 4201,
        DialFailureCause cause = DialFailureCause.Refused,
        IReadOnlySet<string>? offered = null,
        DateTimeOffset? at = null,
        string? detail = null) => new()
    {
        Host = host,
        Port = port,
        ObservedAt = at ?? Observed,
        Outcome = ProbeOutcome.Failed,
        OfferedOptions = offered ?? new HashSet<string>(StringComparer.Ordinal),
        Failure = new FailureDetail(cause, detail),
    };

    public static IReadOnlySet<string> Offered(params string[] protocols) =>
        new HashSet<string>(protocols, StringComparer.Ordinal);
}
