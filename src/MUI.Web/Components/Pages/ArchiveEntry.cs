using MUI.Catalog;
using MUI.Web.Localization;

namespace MUI.Web.Components;

/// <summary>
/// A game in the archive: what it was, when it last answered, and how long it was known live.
/// </summary>
/// <remarks>
/// A game is here because it stopped answering, not because it was judged: dimmed one step, no red,
/// no "dead", no strikethrough. Its page, URL and history are untouched, it is still probed weekly
/// forever, and one successful probe puts it back (spec §7.5).
/// </remarks>
public sealed record ArchiveEntry(
    GameSummary Summary,
    DateTimeOffset? LastReachableAt,
    DateTimeOffset? FirstSeenAt,
    TimeSpan KnownLive)
{
    public static ArchiveEntry For(
        GameSummary summary,
        IReadOnlyList<AvailabilityInterval> intervals,
        DateTimeOffset now)
    {
        var (last, live, first) = ReachSeries.Known(intervals, now);
        return new ArchiveEntry(summary, last, first, live);
    }

    /// <summary>
    /// The run of years, which is the fact worth having about a game that has stopped. Null when we
    /// never measured it reachable — an imported name we could never reach has no run to state, and
    /// inventing one from the dates we happened to hold would be asserting rather than measuring.
    /// </summary>
    public string? Run(string tag) => FirstSeenAt is { } from && LastReachableAt is { } to
        ? Messages.Say(tag, "archive.run",
            ("from", from.UtcDateTime.Year),
            ("to", to.UtcDateTime.Year),
            ("span", RunSpan(tag, to - from)))
        : null;

    /// <summary>
    /// When it last answered, in the site's one absolute format — see <see cref="Dates"/>.
    /// </summary>
    /// <remarks>
    /// Never reached at all is not reached long ago, and the words say which: a game we have never
    /// once measured reachable has no date to print, and printing the oldest one we hold would be
    /// our record's shape presented as the game's history.
    /// </remarks>
    public string LastAnswered(string tag) => LastReachableAt is { } at
        ? Dates.Absolute(tag, at)
        : Messages.Say(tag, "archive.neverReachable");

    /// <summary>
    /// How long we measured it reachable, cumulatively. The label supplies "known live" — saying it
    /// here as well produced "7.9 years known live known live" wherever a surface labelled its own.
    /// </summary>
    /// <remarks>
    /// A zero here is an absence of measurement rather than a measured zero, so it says so in
    /// words: "0 days" would be this site claiming it watched a game stay unreachable throughout.
    /// </remarks>
    public string KnownLiveWording(string tag) => KnownLive == TimeSpan.Zero
        ? Messages.Say(tag, "archive.noReachableTime")
        : KnownLive.TotalDays >= 365
            ? Messages.Say(tag, "archive.knownLive.years", ("years", KnownLive.TotalDays / 365.25))
            : Messages.Say(tag, "archive.knownLive.days", ("days", (int)KnownLive.TotalDays));

    /// <summary>
    /// How long ago it last answered, or that we cannot say — never a cause, only a gap.
    /// </summary>
    public string DarkFor(string tag, DateTimeOffset now) => LastReachableAt is { } at
        ? Relative.Format(tag, now - at)
        : Messages.Say(tag, "archive.darkFor.unknown");

    /// <summary>A run's length, in the largest unit that says anything.</summary>
    private static string RunSpan(string tag, TimeSpan span) => span.TotalDays / 365.25 >= 1
        ? Messages.Say(tag, "archive.run.years", ("count", (int)(span.TotalDays / 365.25)))
        : Messages.Say(tag, "archive.run.months", ("count", (int)(span.TotalDays / 30.44)));
}
