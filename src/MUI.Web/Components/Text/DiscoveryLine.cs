using MUI.Catalog;

using MUI.Web.Localization;

namespace MUI.Web.Components;

/// <summary>
/// How this site came to know about a game, in one dated sentence.
/// </summary>
/// <remarks>
/// Named <c>DiscoveryLine</c> rather than <c>Discovery</c>: a type called <c>Discovery</c> collides
/// with the <c>MUI.Discovery</c> namespace at every call site that has <c>MUI</c> in scope, which is
/// most of them. The name also says what this is — one line on one page.
/// </remarks>
/// <remarks>
/// <para>
/// <b>Every sentence here is about this site, not about the game.</b> "First seen on 22 August 2026,
/// listed on AresCentral" says when we found out and through which channel. It does not say the game
/// originated there, does not say it is listed only there, and <b>must never be shortened to a badge
/// reading the source's name</b>. §7.6 rejected an origin field precisely because any game worth
/// listing appears in several directories at once; the date is what keeps this honest, so no caller
/// may render the channel without it.
/// </para>
/// <para>
/// One message id per source rather than one template with a noun slotted in. A submission is
/// somebody handing us an address, a referral is another game's list naming it, and a backfill is a
/// pile of directories we cannot attribute individually — three different sentences in English, and
/// more in languages where the verb has to agree with the thing that did it.
/// </para>
/// </remarks>
public static class DiscoveryLine
{
    /// <summary>
    /// The sentence for one source and one already-formatted date.
    /// </summary>
    /// <remarks>
    /// The date arrives formatted rather than as a <c>DateTimeOffset</c>, so the caller's locale and
    /// the caller's <c>Dates</c> helper decide how it reads — this class chooses words, not formats.
    /// The switch throws rather than falling back to <c>ToString</c>, so a source added without a
    /// sentence fails loudly instead of leaking an enum name onto a page.
    /// </remarks>
    public static string FirstSeen(string tag, DiscoverySource source, string date)
    {
        ArgumentNullException.ThrowIfNull(tag);
        ArgumentNullException.ThrowIfNull(date);

        return Messages.For(
            tag,
            source switch
            {
                DiscoverySource.OperatorSeed => "game.firstSeen.operatorSeed",
                DiscoverySource.Submission => "game.firstSeen.submission",
                DiscoverySource.Referral => "game.firstSeen.referral",
                DiscoverySource.I3Mudlist => "game.firstSeen.i3Mudlist",
                DiscoverySource.AresCentral => "game.firstSeen.aresCentral",
                DiscoverySource.Backfill => "game.firstSeen.backfill",
                _ => throw new ArgumentOutOfRangeException(
                    nameof(source),
                    source,
                    "No sentence for this discovery source. Add one rather than letting ToString answer."),
            },
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["date"] = date });
    }
}
