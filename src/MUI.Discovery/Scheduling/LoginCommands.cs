using MUI.Crawl;

namespace MUI.Discovery;

/// <summary>
/// What the crawl loop remembers about <c>INFO</c> and <c>VERSION</c> at one address
/// (<c>crawl_target.login_commands_*</c>, migration 0046).
/// </summary>
/// <param name="Unanswered">
/// Consecutive sessions in which <c>INFO</c> was typed and nothing came back that a reader stands
/// behind, capped at <see cref="LoginCommands.Patience"/>.
/// </param>
/// <param name="QuietAt">
/// When this address stopped being asked, for good, or null while it is still asked.
/// </param>
public readonly record struct LoginCommandMemory(int Unanswered, DateTimeOffset? QuietAt);

/// <summary>
/// The two decisions <see cref="LoginCommandMemory"/> exists for: whether to type <c>INFO</c> and
/// <c>VERSION</c> this time, and what a session teaches. Issue #182.
/// </summary>
/// <remarks>
/// <para>
/// <c>MsspSelfDescription</c> stops both where an MSSP report has already answered them, which a game
/// with no MSSP never can — so it was asked on every crawl, for ever. At <c>fs.twkang.net:5555</c> and
/// its three siblings that means <c>INFO</c> read as a character name and <c>VERSION</c> answered with
/// <em>create a new character called version? (y/n)</em>, every thirty minutes, from a server whose
/// answer has not changed since 1995. That is a question to settle from the address's own history,
/// not to re-derive in every session.
/// </para>
/// <para>
/// A pure function of a target's memory and a <see cref="ProbeResult"/>, as <see cref="MsspRoutes"/>
/// and <see cref="WhoAnswers"/> are, so it is testable without a socket or a database.
/// </para>
/// </remarks>
public static class LoginCommands
{
    /// <summary>
    /// How many sessions in a row must come back with nothing readable before the address is left
    /// alone.
    /// </summary>
    /// <remarks>
    /// More than one, so a single bad session — a reply cut off by a hang-up, a server mid-restart —
    /// cannot silence a game whose <c>INFO</c> works. Not many more, because every one of them is two
    /// lines at a stranger's login prompt.
    /// </remarks>
    public const int Patience = 3;

    /// <summary>Whether this session may type <c>INFO</c> and <c>VERSION</c>.</summary>
    /// <remarks>
    /// <para>
    /// Never again, once an address has gone quiet. There is no retrial: a codebase that does not
    /// answer <c>INFO</c> today almost certainly never will, and a monthly retrial would still be two
    /// lines at a stranger's login prompt for an answer we already have. A game that changes codebase
    /// is still read through its connect screen and MSSP, which are never gated by this.
    /// </para>
    /// <para>
    /// The one exception is a game still awaiting §7.8: <c>MuLikeness</c> reads its vocabulary signal
    /// from these replies, and for a game that negotiates nothing they may be the only evidence that it
    /// is a game at all. The same escape <c>WHO</c> has (<see cref="ProbeTarget.AwaitingCorroboration"/>).
    /// </para>
    /// </remarks>
    public static bool Ask(CrawlTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        return target.AwaitingCorroboration || target.LoginCommands.QuietAt is null;
    }

    /// <summary>
    /// The memory after this probe, or <paramref name="current"/> when it taught nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only a session that typed <c>INFO</c> teaches anything (<see cref="ProbeResult.InfoAsked"/>).
    /// A failed dial, a session cut short before <c>INFO</c>, and a session in which we chose not to
    /// ask all leave the memory as it was: our silence is not their silence (rule 5).
    /// </para>
    /// <para>
    /// "Answered" is judged by <see cref="LoginCommandReading.Answered"/>, the consumers' own readers,
    /// so a name prompt and a plain <c>Huh?</c> count alike. One answer clears the whole run.
    /// </para>
    /// </remarks>
    public static LoginCommandMemory Learn(LoginCommandMemory current, ProbeResult result, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.Outcome is not ProbeOutcome.Answered || !result.InfoAsked)
        {
            return current;
        }

        if (LoginCommandReading.Answered(result.Info, result.Version))
        {
            return new LoginCommandMemory(0, null);
        }

        var unanswered = Math.Min(current.Unanswered + 1, Patience);

        // Dated once, when the address went quiet. A miss while quiet (only a submission awaiting
        // §7.8 is asked then) leaves the date where it was.
        return new LoginCommandMemory(unanswered, unanswered >= Patience ? current.QuietAt ?? now : null);
    }
}
