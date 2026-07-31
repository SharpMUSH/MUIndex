using System.Collections.Generic;
using TelnetNegotiationCore.Models;

namespace MUI.Crawl;

/// <summary>
/// An MSSP report as the server sent it: every variable, every value, in wire order.
/// </summary>
/// <remarks>
/// <para>
/// This is a shape, not a parser. Both routes into layer 4 produce one of these — the telnet option,
/// whose decoding is TelnetNegotiationCore's, and the plaintext <c>MSSP-REQUEST</c> reply, whose
/// decoding is <see cref="PlaintextMssp"/>'s — and neither invents, discards or reformats anything
/// on the way. Where the two disagree is recorded as <see cref="MsspTransport"/> rather than
/// resolved here.
/// </para>
/// <para>
/// <b>Order is meaningful and is preserved.</b> MSSP has no notion of a sorted report, and the
/// variable this project most depends on is one where sequence carries intent: a game publishing
/// several <c>REFERRAL</c>s is listing them, not naming a set.
/// </para>
/// </remarks>
public static class MsspReport
{
    /// <summary>A report with nothing in it — the shared "no variables" instance.</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Empty =
        new OrderedDictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Everything a decoded telnet-option report contained.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read from <see cref="MSSPConfig.Variables"/>, which upstream PR #56 made the lossless record
    /// the library's strongly typed properties are projected <em>from</em>. Reading the properties
    /// instead is what lost data: they cannot hold a repeated variable, and there is no property at
    /// all for a name a codebase invented — which, for a protocol whose entire purpose is servers
    /// describing themselves, is a large share of what is interesting.
    /// </para>
    /// <para>
    /// So the projection is deliberately dumb. There is no allow-list of variables worth keeping and
    /// there should never be one: the crawler's job is to record what was said, and a field it
    /// declined to carry cannot be reconsidered later by anything downstream.
    /// </para>
    /// </remarks>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> From(MSSPConfig? config)
    {
        var report = new OrderedDictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        if (config is null)
        {
            return report;
        }

        foreach (var variable in config.Variables.Keys)
        {
            var values = config.Variables[variable];
            if (values.Count > 0)
            {
                report[variable] = [.. values];
            }
        }

        return report;
    }
}
