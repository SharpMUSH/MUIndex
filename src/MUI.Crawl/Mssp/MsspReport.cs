using System.Collections.Generic;
using System.Text;

using TelnetNegotiationCore.Models;

namespace MUI.Crawl;

/// <summary>
/// An MSSP report as the server sent it: every variable, every value, in wire order.
/// </summary>
/// <remarks>
/// A shape, not a parser — the decoding is TelnetNegotiationCore's; nothing here invents, discards or
/// reformats anything on the way through. <b>Order is meaningful and is preserved:</b> MSSP has no
/// notion of a sorted report, and a game publishing several <c>REFERRAL</c>s is listing them, not
/// naming a set.
/// </remarks>
public static class MsspReport
{
    /// <summary>A report with nothing in it — the shared "no variables" instance.</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Empty =
        new OrderedDictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Everything a telnet-option report contained, read with <paramref name="encoding"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read from <see cref="MSSPConfig.Variables"/>, the lossless record — not the library's typed
    /// properties, which cannot hold a repeated variable and have none at all for a name a codebase
    /// invented. Deliberately no allow-list: the crawler's job is to record what was said, and a
    /// field it declined to carry cannot be reconsidered later downstream.
    /// </para>
    /// <para>
    /// <b>Decoded here, from the bytes, rather than taken as the library decoded them.</b> The
    /// library reads an MSSP value with whatever CHARSET negotiated, and a declared encoding is not a
    /// measurement of the bytes: <c>bl.mud.at</c> answers <c>;UTF-8</c> and sends ISO-8859-1,
    /// <c>mud.ren</c> the same with GBK. Anything the negotiated encoding cannot read becomes
    /// <c>U+FFFD</c> at that point and the original byte is gone, so an operator's <c>CHARSET</c>
    /// override could never reach an MSSP field — it fixed the connect screen and left the name
    /// beside it broken for ever. <see cref="WireEncoding"/> makes one decision for the session and
    /// this reads every value with it.
    /// </para>
    /// <para>
    /// A value the report carries no bytes for keeps the text it arrived with. On this path that is
    /// only ever a value the peer sent as zero bytes, which the specification allows.
    /// </para>
    /// </remarks>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> From(MSSPConfig? config, Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(encoding);

        var report = new OrderedDictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        if (config is null)
        {
            return report;
        }

        foreach (var variable in config.Variables.Keys)
        {
            var values = config.Variables[variable];
            if (values.Count == 0)
            {
                continue;
            }

            var raw = config.Variables.Raw(variable);
            var read = new string[values.Count];

            for (var index = 0; index < values.Count; index++)
            {
                var bytes = index < raw.Count ? raw[index] : default;

                // MSSP is a second door into the crawler and never passes through OnSubmit, so it
                // needs its own NUL cleaning — see WireText.
                read[index] = WireText.Clean(
                    bytes.IsEmpty ? values[index] : encoding.GetString(bytes.Span));
            }

            report[WireText.Clean(variable)] = read;
        }

        return report;
    }

    /// <summary>
    /// Every value in the report as it came off the wire, for <see cref="WireEncoding.Read"/> to
    /// decide from.
    /// </summary>
    /// <remarks>
    /// A game can have an ASCII connect screen and a name in GBK, and MSSP is where that name
    /// arrives; deciding the session's encoding from the screen alone would never see the evidence
    /// that matters. Order is not meaningful here — this is evidence, not content.
    /// </remarks>
    public static IReadOnlyList<byte[]> RawValues(MSSPConfig? config)
    {
        if (config is null)
        {
            return [];
        }

        var bytes = new List<byte[]>();

        foreach (var variable in config.Variables.Keys)
        {
            foreach (var value in config.Variables.Raw(variable))
            {
                if (!value.IsEmpty)
                {
                    bytes.Add(value.ToArray());
                }
            }
        }

        return bytes;
    }
}
