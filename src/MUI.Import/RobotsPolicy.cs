using System.Globalization;
using System.Text.RegularExpressions;

namespace MUI.Import;

/// <summary>
/// A parsed <c>robots.txt</c>: which paths a given user agent may fetch, and how long it must wait
/// between fetches.
/// </summary>
/// <remarks>
/// <para>
/// Enough of the de-facto standard to be honest and no more: <c>User-agent</c> groups (consecutive
/// agent lines share one group), <c>Allow</c>, <c>Disallow</c>, <c>Crawl-delay</c>, <c>#</c>
/// comments, and <c>*</c>/<c>$</c> wildcards. Longest-match wins, and <c>Allow</c> beats
/// <c>Disallow</c> at equal length, which is what every major crawler does.
/// </para>
/// <para>
/// The most specific matching agent group wins over <c>*</c>, so a site that names us specifically
/// gets what it asked for rather than the wildcard's terms.
/// </para>
/// </remarks>
public sealed class RobotsPolicy
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);

    private readonly IReadOnlyList<RobotsGroup> _groups;

    private RobotsPolicy(IReadOnlyList<RobotsGroup> groups) => _groups = groups;

    /// <summary>
    /// What a missing or unreadable <c>robots.txt</c> means: nothing is forbidden.
    /// </summary>
    /// <remarks>
    /// Deliberately permissive, and deliberately still a <em>policy object</em> — the gate stays shut
    /// until something is adopted, so "we never asked" and "we asked and were told nothing" remain
    /// different states.
    /// </remarks>
    public static RobotsPolicy AllowAll { get; } = new([]);

    public static RobotsPolicy Parse(string robotsTxt)
    {
        ArgumentNullException.ThrowIfNull(robotsTxt);

        var groups = new List<RobotsGroup>();
        RobotsGroup? current = null;
        var acceptingAgents = false;

        foreach (var rawLine in robotsTxt.Split('\n'))
        {
            var line = rawLine;
            var hash = line.IndexOf('#', StringComparison.Ordinal);
            if (hash >= 0)
            {
                line = line[..hash];
            }

            line = line.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon < 0)
            {
                continue;
            }

            var key = line[..colon].Trim().ToLowerInvariant();
            var value = line[(colon + 1)..].Trim();

            switch (key)
            {
                case "user-agent":
                    // Consecutive User-agent lines share one group; the first rule line after them
                    // closes the agent list.
                    if (current is null || !acceptingAgents)
                    {
                        current = new RobotsGroup();
                        groups.Add(current);
                        acceptingAgents = true;
                    }

                    current.Agents.Add(value.ToLowerInvariant());
                    break;

                case "disallow":
                    acceptingAgents = false;
                    // An empty Disallow is the standard's way of saying "nothing is forbidden", so it
                    // must not become a rule matching every path.
                    if (current is not null && value.Length > 0)
                    {
                        current.Disallow.Add(value);
                    }

                    break;

                case "allow":
                    acceptingAgents = false;
                    if (current is not null && value.Length > 0)
                    {
                        current.Allow.Add(value);
                    }

                    break;

                case "crawl-delay":
                    acceptingAgents = false;
                    if (current is not null
                        && double.TryParse(value, CultureInfo.InvariantCulture, out var seconds)
                        && seconds > 0)
                    {
                        current.CrawlDelay = TimeSpan.FromSeconds(seconds);
                    }

                    break;

                default:
                    break;
            }
        }

        return new RobotsPolicy(groups);
    }

    public bool Allows(string path, string userAgent)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(userAgent);

        if (GroupFor(userAgent) is not { } group)
        {
            return true;
        }

        var disallow = LongestMatch(group.Disallow, path);
        if (disallow < 0)
        {
            return true;
        }

        return LongestMatch(group.Allow, path) >= disallow;
    }

    /// <summary>The site's own requested delay for this agent, or null if it named none.</summary>
    public TimeSpan? CrawlDelayFor(string userAgent)
    {
        ArgumentNullException.ThrowIfNull(userAgent);

        return GroupFor(userAgent)?.CrawlDelay;
    }

    private RobotsGroup? GroupFor(string userAgent)
    {
        var token = Token(userAgent);
        RobotsGroup? best = null;
        var bestLength = -1;

        foreach (var group in _groups)
        {
            foreach (var agent in group.Agents)
            {
                if (agent == "*")
                {
                    if (bestLength < 0)
                    {
                        best = group;
                        bestLength = 0;
                    }

                    continue;
                }

                if (token.StartsWith(agent, StringComparison.Ordinal) && agent.Length > bestLength)
                {
                    best = group;
                    bestLength = agent.Length;
                }
            }
        }

        return best;
    }

    /// <summary>The product token: everything before the version slash, lower-cased.</summary>
    private static string Token(string userAgent)
    {
        var slash = userAgent.IndexOf('/', StringComparison.Ordinal);
        var head = slash < 0 ? userAgent : userAgent[..slash];

        return head.Trim().ToLowerInvariant();
    }

    private static int LongestMatch(IReadOnlyList<string> rules, string path)
    {
        var best = -1;

        foreach (var rule in rules)
        {
            if (Matches(rule, path) && rule.Length > best)
            {
                best = rule.Length;
            }
        }

        return best;
    }

    private static bool Matches(string rule, string path)
    {
        if (!rule.Contains('*', StringComparison.Ordinal) && !rule.EndsWith('$'))
        {
            return path.StartsWith(rule, StringComparison.Ordinal);
        }

        var pattern = "^" + Regex.Escape(rule).Replace("\\*", ".*", StringComparison.Ordinal);
        if (pattern.EndsWith("\\$", StringComparison.Ordinal))
        {
            pattern = pattern[..^2] + "$";
        }

        return Regex.IsMatch(path, pattern, RegexOptions.None, MatchTimeout);
    }

    private sealed class RobotsGroup
    {
        public List<string> Agents { get; } = [];

        public List<string> Disallow { get; } = [];

        public List<string> Allow { get; } = [];

        public TimeSpan? CrawlDelay { get; set; }
    }
}
