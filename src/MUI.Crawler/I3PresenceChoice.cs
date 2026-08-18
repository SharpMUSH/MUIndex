using MUI.Catalog;
using MUI.I3;

namespace MUI.Crawler;

/// <summary>
/// What an Intermud-3 <c>who-reply</c> means as a presence reading (spec §5.2, §5.4).
/// </summary>
/// <remarks>
/// Deliberately not a rung on <see cref="PresenceChoice"/>'s ladder: that ladder picks one count off
/// several read from a single telnet probe, while an I3 reply arrives on its own schedule through a
/// different pipe and writes its own row at <c>(game_id, at)</c>. A game reachable by both will
/// accumulate two series that may disagree — not a conflict to resolve, but two labelled vantage
/// points on the same game.
/// </remarks>
public static class I3PresenceChoice
{
    /// <summary>
    /// The reading for a mud we asked. <paramref name="reply"/> is <see langword="null"/> when
    /// nothing came back inside the wait.
    /// </summary>
    /// <remarks>
    /// An empty <c>users</c> array is a measured zero — the mud answered that nobody is on. Silence
    /// (a null reply) is the uncountable middle state of §5.4 and must carry a reason. The two look
    /// alike coming down the same pipe, so the distinction is made once, here.
    /// </remarks>
    public static PresenceReading From(I3WhoReply? reply) =>
        reply is null
            ? PresenceReading.Unmeasurable(UnmeasurableReason.I3NoReply, FieldSource.I3)
            : PresenceReading.Counted(reply.Users.Count, FieldSource.I3);

    /// <summary>
    /// Whether this mud may be asked at all.
    /// </summary>
    /// <remarks>
    /// Two gates, both the network's own words rather than our guesses: a mud the router reports as
    /// down won't answer, and one that doesn't list <c>who</c> in its services mapping has opted out
    /// of that question — asking anyway would be the I3 equivalent of dialling a host that asked us
    /// not to (§11).
    /// </remarks>
    public static bool MayAsk(I3Mud mud)
    {
        ArgumentNullException.ThrowIfNull(mud);
        return mud.IsUp && mud.Answers("who");
    }
}
