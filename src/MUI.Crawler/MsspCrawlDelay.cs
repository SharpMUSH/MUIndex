using MUI.Crawl;

namespace MUI.Crawler;

/// <summary>
/// The gap a server asked for, read off its own MSSP report (spec §7.7, §11).
/// </summary>
/// <remarks>
/// <para>
/// MSSP defines <c>CRAWL DELAY</c> as the "preferred minimum number of hours between crawls", and
/// <c>-1</c> as "no preference" — which is a different fact from zero and must not collapse into it.
/// A server stating <c>0</c> has said "as often as you like"; a server stating <c>-1</c> has said
/// nothing, and the difference is only visible if the reader keeps it.
/// </para>
/// <para>
/// <b>This decides nothing about when to probe.</b> <see cref="MUI.Discovery.ProbeSchedule"/> owns
/// that, and composes <c>max(CRAWL DELAY, backoff)</c> so politeness wins over §7.4's weekly floor.
/// All this does is turn a string a stranger typed into the <c>TimeSpan?</c> that argument takes.
/// </para>
/// <para>
/// <b>Where this belongs, eventually.</b> Beside <c>MsspReferrals</c> in <c>MUI.Discovery</c>, which
/// is where CLAUDE.md files "the domain readings TNC does not provide (<c>CRAWL DELAY -1</c> as 'no
/// preference', ports validated as ports, <c>REFERRAL</c> as crawlable hosts)". It is here because
/// this project's plan scoped edits to <c>MUI.Crawler</c>; move it when that closes.
/// </para>
/// </remarks>
public static class MsspCrawlDelay
{
    /// <summary>The MSSP variable, matched case-insensitively as MSSP variables are.</summary>
    public const string Variable = "CRAWL DELAY";

    /// <summary>
    /// A hard ceiling on what a server may ask for. Politeness is honoured; a typo or a hostile value
    /// is not allowed to remove a game from the crawl for a century, which is retirement by the back
    /// door and is the one thing §7.4 forbids outright.
    /// </summary>
    public static readonly TimeSpan Longest = TimeSpan.FromDays(90);

    /// <summary>
    /// The delay this probe's server asked for, or null when it asked for nothing. Null for a report
    /// we did not receive, for <c>-1</c>, and for anything that does not read as a number of hours —
    /// a value we cannot parse is a value the server did not successfully state.
    /// </summary>
    public static TimeSpan? From(ProbeResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.MsspOutcome is MsspOutcome.Received ? Parse(result.MsspField(Variable)) : null;
    }

    /// <summary>One <c>CRAWL DELAY</c> value, in hours.</summary>
    public static TimeSpan? Parse(string? value)
    {
        if (value is null || !double.TryParse(value.Trim(), out var hours) || double.IsNaN(hours))
        {
            return null;
        }

        // -1 is the specification's own "no preference". Any other negative is a value nobody meant,
        // and reading it as a request would be reading a typo as consent.
        if (hours < 0)
        {
            return null;
        }

        var requested = TimeSpan.FromHours(hours);

        return requested > Longest ? Longest : requested;
    }
}
