using System.Collections.Generic;
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
    /// Everything a decoded telnet-option report contained.
    /// </summary>
    /// <remarks>
    /// Read from <see cref="MSSPConfig.Variables"/>, the lossless record — not the library's typed
    /// properties, which cannot hold a repeated variable and have none at all for a name a codebase
    /// invented. Deliberately no allow-list: the crawler's job is to record what was said, and a
    /// field it declined to carry cannot be reconsidered later downstream.
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
                // MSSP is a second door into the crawler and never passes through OnSubmit, so it needs
                // its own NUL cleaning — see WireText.
                report[WireText.Clean(variable)] = [.. values.Select(WireText.Clean)];
            }
        }

        return report;
    }
}
