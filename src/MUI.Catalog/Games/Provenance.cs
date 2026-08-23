namespace MUI.Catalog;

/// <summary>
/// How a descriptive field's value was obtained. This is the spine of the whole product: nothing is
/// displayed without it, and the ordering below is the precedence when sources disagree (spec §5.1).
/// </summary>
/// <remarks>
/// Declared order is precedence order, highest first. <see cref="Staff"/> outranks everything and is
/// always logged; <see cref="Handshake"/> beats <see cref="Mssp"/> for capability fields because a
/// server offering an option is an observation and a game claiming it is an assertion.
/// </remarks>
public enum FieldSource
{
    Staff,
    Handshake,
    Owner,
    Who,

    /// <summary>
    /// An Intermud-3 <c>who-reply</c>: a mud on the I3 network enumerated its users for us and we
    /// counted them (spec §5.2).
    /// </summary>
    /// <remarks>
    /// Presence only, ranked beside <see cref="Who"/>: the remote mud stated no number, the arithmetic
    /// is ours, on a list it built when we asked. The reply crosses a router we do not run and reflects
    /// the remote mud's own visibility rules — same as telnet <c>WHO</c> — so an I3 count and a
    /// telnet count for one game may legitimately differ; a mismatch is not a defect in either.
    /// </remarks>
    I3,
    Mssp,

    /// <summary>
    /// The <c>INFO</c> block a MUSH-family server prints at the login screen, which states its own
    /// <c>Connected:</c> count (spec §5.2).
    /// </summary>
    /// <remarks>
    /// Presence only. Nothing writes an <c>info</c> <c>GameField</c> — the name and codebase parsed
    /// out of the same block go in under <see cref="Banner"/> instead, since they're free text rather
    /// than a labelled line. Ranked here for if that ever changes: below the game's structured
    /// self-description, above a number found in ASCII art.
    /// </remarks>
    Info,

    /// <summary>
    /// A value AresCentral holds: the game told the AresMUSH community hub, and the hub told us
    /// through an API whose maintainer issued us credentials.
    /// </summary>
    /// <remarks>
    /// Declared, not measured — nobody here read this off the game's own socket. Ranks below
    /// <see cref="Mssp"/>, because a game speaking to us directly and now beats a hub repeating a
    /// claim of unknown age, and above <see cref="I3Mudlist"/>, because AresCentral is
    /// authenticated, curated by the codebase's own author, and excludes games marked In Development
    /// and games long offline, where the live I3 mudlist carries <c>test</c> and <c>Your MUD Name</c>
    /// beside the real entries. Never above <see cref="Staff"/> or <see cref="Owner"/>: a hub does
    /// not police what a game calls itself, and a human correction wins.
    /// </remarks>
    AresCentral,

    /// <summary>
    /// A value the Intermud-3 mudlist carried: the mud told a router, and the router told us.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="I3"/> — the gap between them is the measured/declared line. A
    /// who-reply is a list the mud built when we asked and whose rows we counted ourselves; a mudlist
    /// entry is a value handed to a third party at some past startup and repeated onward, undated.
    /// Ranks below <see cref="Mssp"/> (a direct, current self-report beats an indirect, undated one)
    /// and above <see cref="Banner"/>, but never above <see cref="Staff"/>: the network doesn't police
    /// what a mud calls itself, and the live list carries <c>Your MUD Name</c> and <c>test</c> beside
    /// the real ones.
    /// </remarks>
    I3Mudlist,
    Banner,

    // Deliberately no imported source: the backfill contributes addresses only (spec §7.6). A source
    // nothing can produce is a ladder rung that only invites reuse.
}

/// <summary>
/// Which sources are observations of ours and which are a game's report of itself.
/// </summary>
/// <remarks>
/// <para>
/// The one spelling of the measured/declared line: decides the word on every chip, the state the API
/// names beside every count, and whether a badge on somebody else's site shows a number or unknown.
/// </para>
/// <para>
/// The line is who read the value, not who authored it. A telnet handshake, a pre-login <c>WHO</c>,
/// and <see cref="FieldSource.Banner"/> are all ours to have observed — several games publish their
/// player count only on the connect screen, and we parse that text ourselves on every probe, so its
/// freshness is ours even where its arithmetic is theirs.
/// </para>
/// <para>
/// <see cref="FieldSource.Mssp"/> and <see cref="FieldSource.Info"/> stay on the declared side even
/// though we open the socket for both: what we read is a labelled line the codebase generated about
/// itself, the same class of statement as any self-report, just arriving down a different pipe. A
/// game handing us its own figure has reported it however it was delivered.
/// </para>
/// <para>
/// This is a different axis from precedence, and the two disagree on purpose: <c>banner</c> is the
/// lowest rung of both ladders — a number in a stranger's ASCII art may be stale or a high score, so
/// it's picked last (see <c>PresenceChoice</c>) — yet still an observation when it's what we have.
/// Least trusted to be the right number, still measured.
/// </para>
/// </remarks>
public static class FieldSources
{
    public static bool IsMeasured(FieldSource source) =>
        source is FieldSource.Handshake or FieldSource.Who or FieldSource.I3 or FieldSource.Banner;
}

/// <summary>
/// A value together with where it came from and how old it is. There is no unlabelled data on this
/// site, so there is no way to carry a value without one of these.
/// </summary>
public sealed record Provenance(
    FieldSource Source,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastConfirmedAt)
{
    /// <summary>Whether this was observed by somebody rather than asserted by the game itself.</summary>
    public bool IsMeasured => FieldSources.IsMeasured(Source);

    public TimeSpan AgeAt(DateTimeOffset now) => now - LastConfirmedAt;
}
