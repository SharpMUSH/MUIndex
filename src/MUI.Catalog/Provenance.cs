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
    /// <para>
    /// Presence only, like <see cref="Info"/>, and it ranks beside <see cref="Who"/> because it is
    /// the same kind of answer — a list of people rather than a figure. The remote mud stated no
    /// number; the arithmetic is ours, on a list it built when we asked, which is what separates this
    /// from an MSSP <c>PLAYERS</c> the codebase may have cached.
    /// </para>
    /// <para>
    /// <b>The caveat, stated rather than buried:</b> the reply crosses a router we do not run, and it
    /// contains whoever the remote mud's own visibility rules chose to list. So does a telnet
    /// <c>WHO</c>, which is why this is not a reason to rank it lower — but it does mean an I3 count
    /// and a telnet count for one game may legitimately differ, and a mismatch between them is not a
    /// defect in either.
    /// </para>
    /// </remarks>
    I3,
    Mssp,

    /// <summary>
    /// The <c>INFO</c> block a MUSH-family server prints at the login screen, which states its own
    /// <c>Connected:</c> count (spec §5.2).
    /// </summary>
    /// <remarks>
    /// Presence only. Nothing writes an <c>info</c> <c>GameField</c> — the name and codebase read out
    /// of the same block go in under <see cref="Banner"/>, because they are parsed out of free text
    /// rather than lifted off a labelled line the codebase generates — so <c>game_field</c>'s
    /// vocabulary does not carry it, in the same way <c>presence_sample</c>'s does not carry
    /// <see cref="Staff"/>. Its rank here is the one it would take if that ever changed: below the
    /// game's structured self-description, above a number found in ASCII art.
    /// </remarks>
    Info,

    /// <summary>
    /// A value the Intermud-3 mudlist carried: the mud told a router, and the router told us.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Separate from <see cref="I3"/>, and the gap between them is the measured/declared line.</b>
    /// A who-reply is a list the mud built when we asked and whose rows we counted ourselves; a
    /// mudlist entry is a value handed to a third party at some past startup and repeated onward,
    /// undated. Same network, two different kinds of claim.
    /// </para>
    /// <para>
    /// It ranks here — below <see cref="Mssp"/>, above <see cref="Banner"/> — because a game filling
    /// in its own MSSP is speaking to us directly and now, while a name lifted out of ASCII art is a
    /// guess at where the title is and this is a field the mud filled in. It does not outrank
    /// <see cref="Staff"/>: the network does not police what a mud calls itself, and the live list
    /// carries <c>Your MUD Name</c> and <c>test</c> beside the real ones.
    /// </para>
    /// </remarks>
    I3Mudlist,
    Banner,

    // There is deliberately no imported source here, and there was: ImportedMeasured for a directory
    // that ran its own probe, ImportedAsserted for a hand-maintained list. The backfill contributes
    // *addresses* and nothing else now (spec §7.6) — every value about a game is measured by this
    // crawler — so an imported field is a row that can no longer be written, and a source nothing can
    // produce is a ladder rung that only invites somebody to reach for it.
}

/// <summary>
/// Which sources are observations of ours and which are a game's report of itself.
/// </summary>
/// <remarks>
/// <para>
/// <b>The one spelling of the measured/declared line.</b> It decides the word on every chip, the
/// state the API names beside every count, and whether a badge on somebody else's site shows a
/// number or says the count is unknown — and it lived in two records that each wrote it out for
/// themselves, so a decision about one source had to be remembered in two files.
/// </para>
/// <para>
/// <b>The line is who read the value, not who authored it.</b> A telnet handshake and a pre-login
/// <c>WHO</c> are ours to have observed. So is <see cref="FieldSource.Banner"/>: several games
/// publish their player count only on the connect screen, and we open a socket and parse that text
/// ourselves on every probe — its freshness is ours even where its arithmetic is theirs. Migration
/// 0003 has said exactly that since the presence table was written.
/// </para>
/// <para>
/// <see cref="FieldSource.Mssp"/> stays on the declared side, and the pairing is not a contradiction:
/// a game filling in a structured self-description is reporting rather than being read, and its
/// <c>PLAYERS</c> may be whatever the codebase last cached. <see cref="FieldSource.Info"/> joins it
/// there for the identical reason and against the identical temptation: we do open the socket and
/// read the block ourselves, but what we read is a labelled line the codebase generated about itself
/// — the same class of statement as an MSSP variable, arriving down a different pipe. The line is
/// who read the value <em>where a value was read</em>; a game handing us its own figure has reported
/// it however it was delivered. <see cref="FieldSource.Owner"/> and <see cref="FieldSource.Staff"/>
/// are people typing, one of them us.
/// </para>
/// <para>
/// This is a different axis from precedence and the two disagree on purpose. <c>banner</c> is the
/// <em>lowest</em> rung of both ladders — a number in a stranger's ASCII art may be a high score or
/// last week's figure, so it is picked last (see <c>PresenceChoice</c>) — and it is still an
/// observation when it is what we have. Least trusted to be the right number, still measured.
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
