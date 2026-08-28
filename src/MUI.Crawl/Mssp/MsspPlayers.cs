using System.Globalization;

namespace MUI.Crawl;

/// <summary>
/// The player count a game states about itself in its own MSSP report.
/// </summary>
/// <remarks>
/// One reader, because two callers ask the same question for opposite purposes and must never
/// disagree: <c>PresenceChoice</c> asks it to decide what to publish, and <c>TelnetProbe</c> asks it
/// to decide whether <c>WHO</c> still needs typing at somebody's login screen. A count accepted by
/// one and refused by the other would mean a probe that declined to ask and then had nothing to
/// publish — our own decision recorded as the game answering no <c>WHO</c>, which is rule 5 exactly.
/// <para>
/// Invariant culture on purpose: <c>PLAYERS</c> is a wire value, not something formatted for the
/// host this crawler happens to run on.
/// </para>
/// </remarks>
public static class MsspPlayers
{
    /// <summary>The MSSP variable a game states its own player count in.</summary>
    public const string Variable = "PLAYERS";

    /// <summary>
    /// The count a declared <c>PLAYERS</c> value carries, or null when it carried none we can use.
    /// </summary>
    /// <remarks>
    /// Negative is refused rather than clamped: a server that reports <c>-1</c> is saying it does not
    /// know, and publishing that as a number would be inventing one (rule 4).
    /// </remarks>
    public static int? Read(string? declared) =>
        declared is not null
        && int.TryParse(declared.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var stated)
        && stated >= 0
            ? stated
            : null;
}
