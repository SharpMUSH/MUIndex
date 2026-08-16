using MUI.Catalog;
using MUI.I3;

namespace MUI.Crawler;

/// <summary>
/// What an Intermud-3 <c>who-reply</c> means as a presence reading (spec §5.2, §5.4).
/// </summary>
/// <remarks>
/// <para>
/// <b>This is not a rung on <see cref="PresenceChoice"/>'s ladder, and that is the design.</b> That
/// ladder settles which of one telnet probe's several counts becomes the single row keyed
/// <c>(game_id, at)</c> — WHO against MSSP against INFO against the banner, all read off one socket
/// in one breath. An I3 answer is not one of those: it arrives on its own schedule, through a
/// different pipe, about a game we may not have dialled at all this hour. It is a separate
/// observation and it writes a separate row, which is why nothing in this file touches that one.
/// </para>
/// <para>
/// The consequence worth naming: a game on I3 that the telnet crawler also reaches will accumulate
/// two series, and they may disagree. That is not a conflict to resolve here — it is two vantage
/// points on the same game, each labelled with how it was obtained, which is the whole premise of
/// the site.
/// </para>
/// </remarks>
public static class I3PresenceChoice
{
    /// <summary>
    /// The reading for a mud we asked. <paramref name="reply"/> is <see langword="null"/> when
    /// nothing came back inside the wait.
    /// </summary>
    /// <remarks>
    /// <b>Two states, and telling them apart is the entire job.</b> An empty <c>users</c> array is
    /// the mud answering that nobody is on — a measured zero, a filled cell, and something we
    /// observed rather than inferred; <c>The Zone</c> answered exactly that on the first live run.
    /// Silence is the middle state of §5.4 and must carry a reason. They arrive down one pipe and
    /// look alike, so the distinction is made once, here, rather than by every caller.
    /// </remarks>
    public static PresenceReading From(I3WhoReply? reply) =>
        reply is null
            ? PresenceReading.Unmeasurable(UnmeasurableReason.I3NoReply, FieldSource.I3)
            : PresenceReading.Counted(reply.Users.Count, FieldSource.I3);

    /// <summary>
    /// Whether this mud may be asked at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two gates, and both are the network's own words rather than our guesses. A mud the router
    /// reports as down will not answer, so asking spends a packet to learn what the mudlist already
    /// said. A mud that does not list <c>who</c> in its services mapping has stated it does not
    /// answer that question, and I3's services mapping is the network's opt-in mechanism — asking
    /// anyway is the I3 equivalent of dialling a host that asked us not to (§11).
    /// </para>
    /// <para>
    /// This refuses very little: of 60 online muds sampled, 59 advertise <c>who</c>. That is the
    /// argument for honouring it rather than against — a gate that almost never fires costs almost
    /// nothing to respect, and the one mud it does refuse is the one that asked.
    /// </para>
    /// </remarks>
    public static bool MayAsk(I3Mud mud)
    {
        ArgumentNullException.ThrowIfNull(mud);
        return mud.IsUp && mud.Answers("who");
    }
}
