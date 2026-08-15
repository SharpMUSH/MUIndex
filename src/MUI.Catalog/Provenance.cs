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
    Mssp,
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
/// <c>PLAYERS</c> may be whatever the codebase last cached. <see cref="FieldSource.Owner"/> and
/// <see cref="FieldSource.Staff"/> are people typing, one of them us.
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
        source is FieldSource.Handshake or FieldSource.Who or FieldSource.Banner;
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
