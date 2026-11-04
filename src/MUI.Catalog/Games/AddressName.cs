using System.Globalization;
using System.Text.RegularExpressions;

namespace MUI.Catalog;

/// <summary>
/// Whether a game's name is only the address we found it at — <c>mud.example.org:4000</c>,
/// <c>203.0.113.7:23</c> — because the game told us no name of its own.
/// </summary>
/// <remarks>
/// <para>
/// <c>CatalogueBinder.NameOf</c> falls back to <c>{host}:{port}</c> when neither MSSP nor a login
/// command gave a meaningful name, and that name stays until a probe, an owner or staff supplies a
/// better one. This reads the name rather than a flag, so a rename takes the game out of the set with
/// nothing else to update.
/// </para>
/// <para>
/// A statement about the name <em>we</em> list the game under, never that the game has no name —
/// it may well have one it did not publish (rule 5).
/// </para>
/// <para>
/// The host has to look like an address: a dotted name, an IPv4 literal, or an IPv6 literal (bare,
/// which is how the fallback writes it, or bracketed). A name such as <c>Babylon:5</c> carries no dot
/// and stays a name.
/// </para>
/// </remarks>
public static partial class AddressName
{
    public static bool Is(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var match = Shape().Match(name.Trim());

        if (!match.Success
            || !int.TryParse(
                match.Groups["port"].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out var port)
            || port is < 1 or > 65535)
        {
            return false;
        }

        var host = match.Groups["host"].Value;

        return host.Contains('.', StringComparison.Ordinal) || host.Contains(':', StringComparison.Ordinal);
    }

    [GeneratedRegex(
        @"^(?<host>\[[0-9A-Fa-f:.]+\]|[A-Za-z0-9.-]+|[0-9A-Fa-f:.]+):(?<port>[0-9]{1,5})$",
        RegexOptions.CultureInvariant)]
    private static partial Regex Shape();
}
