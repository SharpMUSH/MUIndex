using System.Collections.Generic;
using TelnetNegotiationCore.Models;

namespace MUI.Crawl;

/// <summary>
/// An MSSP report as the server sent it: every variable, every value, in wire order.
/// </summary>
/// <remarks>
/// <para>
/// This is a shape, not a parser. The decoding is TelnetNegotiationCore's; nothing here invents,
/// discards or reformats anything on the way through. Should the plaintext <c>MSSP-REQUEST</c> form
/// arrive — upstream, as issue #61, rather than here — it produces the same shape, and which route
/// answered is recorded as <see cref="MsspTransport"/> rather than resolved away.
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
                // The subnegotiation is a second door into the crawler and needs the same cleaning
                // as the line reader — a NUL in an MSSP value is no more storable than one in a
                // banner, and it arrives without ever passing through OnSubmit. See WireText.
                report[WireText.Clean(variable)] = [.. values.Select(WireText.Clean)];
            }
        }

        return report;
    }
}
