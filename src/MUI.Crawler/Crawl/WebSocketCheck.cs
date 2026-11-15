using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Crawl;
using MUI.Discovery;

namespace MUI.Crawler;

/// <summary>
/// Whether the WebSocket URL a game's MSSP declares answers telnet: what to dial, when, and what the
/// answer means.
/// </summary>
/// <remarks>
/// <para>
/// <c>WEBSOCKET</c> (the mudstandards.org proposal, TelnetNegotiationCore's
/// <c>MSSPConfig.WebSocket</c>) is a claim, and <c>FieldObservations</c> writes it as
/// <c>capability.websocket.declared</c> like any other capability variable. This is the other half:
/// dialling the URL and reading the telnet session behind it, written as
/// <c>capability.websocket.measured</c> under <see cref="FieldSource.Handshake"/>, so the page can say
/// "declared, and answered" or "declared, and refused" the way it does for GMCP (rule 1).
/// </para>
/// <para>
/// Only the first URL a report names is checked. The variable is a comma-separated list, and a game
/// with several is almost always one server behind several names; one dial answers the capability
/// question, and a dial per name would be the same noise on the same console several times over.
/// </para>
/// </remarks>
public static class WebSocketCheck
{
    /// <summary>The MSSP variable that names the URLs.</summary>
    public const string Variable = "WEBSOCKET";

    /// <summary>The capability, as <see cref="CapabilityFields"/> spells it.</summary>
    public const string Capability = "WEBSOCKET";

    /// <summary>
    /// The URL to check, from this probe's report: the first <c>ws://</c> or <c>wss://</c> address in
    /// the last <c>WEBSOCKET</c> value, or null when the report names none.
    /// </summary>
    /// <remarks>
    /// The last value, by MSSP's own rule, as <c>FieldObservations</c> reads every variable. A value
    /// that is not an absolute ws or wss URL (<c>1</c>, a bare host, an <c>https</c> page) names
    /// nothing this check can dial, and is left to stand as the declaration it is.
    /// </remarks>
    public static Uri? Declared(ProbeResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.Outcome is not ProbeOutcome.Answered
            || result.MsspOutcome is not MsspOutcome.Received
            || result.MsspValues(Variable) is not [.., var value])
        {
            return null;
        }

        foreach (var candidate in value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri) && uri.Scheme is "ws" or "wss")
            {
                return uri;
            }
        }

        return null;
    }

    /// <summary>Whether this address's declared URL is due another check.</summary>
    public static bool Due(CrawlTarget target, DateTimeOffset now, TimeSpan interval)
    {
        ArgumentNullException.ThrowIfNull(target);

        return target.WebSocketCheckedAt is not { } at || now - at >= interval;
    }

    /// <summary>
    /// What a check found, as the row it writes, or null when it found nothing either way.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>true</c> needs a telnet session that said something — a screen or a negotiated option. An
    /// upgrade that completed into silence proves a WebSocket, not a game behind it, and writes
    /// nothing.
    /// </para>
    /// <para>
    /// <c>false</c> only for an answer from the far end: a refused connection, a handshake it would
    /// not complete, or an upgrade it turned down (which the dial reports as an error). A timeout, no
    /// route or a failed lookup is a fact about the path from here, and the game's record is not where
    /// that goes (rule 5).
    /// </para>
    /// </remarks>
    public static FieldObservation? Observation(ProbeResult check)
    {
        ArgumentNullException.ThrowIfNull(check);

        var answer = check switch
        {
            { Outcome: ProbeOutcome.Answered } when check.OfferedOptions.Count > 0
                || !string.IsNullOrWhiteSpace(check.Banner) => "true",
            { Outcome: ProbeOutcome.Failed, Failure.Cause: DialFailureCause.Refused or DialFailureCause.Tls or DialFailureCause.Error } => "false",
            _ => null,
        };

        return answer is null
            ? null
            : new FieldObservation(CapabilityFields.Measured(Capability), FieldSource.Handshake, answer);
    }
}
