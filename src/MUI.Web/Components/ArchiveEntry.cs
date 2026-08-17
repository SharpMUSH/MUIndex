using MUI.Catalog;

namespace MUI.Web.Components;

/// <summary>
/// A game in the archive: what it was, when it last answered, and how long it was known live.
/// </summary>
/// <remarks>
/// <para>
/// The archive is a first-class section rather than a hidden flag, because this is the historical
/// record every incumbent threw away and it is worth presenting as an asset rather than as a bin.
/// A game is here because it stopped answering; it is not here because it was judged.
/// </para>
/// <para>
/// The treatment is a library catalogue entry for a periodical that ceased publication: dimmed one
/// step, no red, no "dead", no strikethrough. Its page, URL and history are untouched, it is still
/// probed weekly forever, and one successful probe puts it back (spec §7.5).
/// </para>
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
    public string? Run => FirstSeenAt is { } from && LastReachableAt is { } to
        ? Wording.Run(from, to)
        : null;

    /// <summary>
    /// When it last answered, in the site's one absolute format — see <see cref="Dates"/>.
    /// </summary>
    public string LastAnswered => LastReachableAt is { } at
        ? Dates.Absolute(at)
        : "never, in anything we measured";

    /// <summary>
    /// How long we measured it reachable, cumulatively. The label supplies "known live" — saying it
    /// here as well produced "7.9 years known live known live" wherever a surface labelled its own.
    /// </summary>
    public string KnownLiveWording => KnownLive == TimeSpan.Zero
        ? "no reachable time measured"
        : KnownLive.TotalDays >= 365
            ? $"{KnownLive.TotalDays / 365.25:0.#} years"
            : $"{(int)KnownLive.TotalDays} days";

    public string DarkFor(DateTimeOffset now) =>
        LastReachableAt is { } at ? Relative.Format(now - at) : "unknown";
}
