using System.Globalization;

namespace MUI.Crawl;

/// <summary>
/// How well a game's MSSP report answers "how many are online".
/// </summary>
/// <remarks>
/// Two levels and an absence, and the gap between the two levels was measured rather than assumed —
/// see <see cref="MsspPresence"/> for the servers it came from. This is the confidence ladder for
/// the MSSP channel, the counterpart to <c>WhoConfidence</c> for the telnet one; there is no
/// separate confidence type, because a second enum would be a second name for this one fact.
/// </remarks>
public enum MsspCountKind
{
    /// <summary>The report answers the question in none of the ways below.</summary>
    None,

    /// <summary>
    /// The game stated the number itself: <c>PLAYERS = 70</c>. Exact — it is the game's own answer
    /// to the exact question, computed by the same call its <c>WHO</c> would use.
    /// </summary>
    Stated,

    /// <summary>
    /// The game published who is online and the arithmetic is ours. <b>A floor, not a total.</b>
    /// </summary>
    /// <remarks>
    /// Measured against a server that publishes both: <c>tdome.nukefire.org:4000</c> reported
    /// <c>PLAYERS = 70</c> and sixty-nine names, stably, across three probes minutes apart — a
    /// roster leaves out whoever the game does not show, and every codebase has someone it does not
    /// show. So a roster count may be published where nothing better exists, but it may never
    /// overwrite a stated one, and it is never presented as a total.
    /// </remarks>
    Roster,
}

/// <summary>
/// A count read out of an MSSP report, and how the report gave it.
/// </summary>
/// <remarks>
/// <see cref="Variable"/> names the variable it came from, so a surface or a log can say which one
/// answered rather than implying the whole report agreed. <see cref="MsspCountKind.None"/> carries
/// no count: the field is left at zero and must not be read, which is what <see cref="Found"/> is
/// for — a zero <em>count</em> is a real measurement here (see <see cref="MsspPresence"/>), so the
/// two states cannot be told apart by the number.
/// </remarks>
public readonly record struct MsspCount(int Count, MsspCountKind Kind, string? Variable)
{
    /// <summary>Nothing in the report answered the question.</summary>
    public static readonly MsspCount None = new(0, MsspCountKind.None, null);

    /// <summary>Whether there is a count here at all.</summary>
    public bool Found => Kind is not MsspCountKind.None;

    /// <summary>Whether the game stated the number rather than leaving us to count a list.</summary>
    public bool IsExact => Kind is MsspCountKind.Stated;
}

/// <summary>
/// Reads a player count out of an MSSP report — the stated one, or a roster the game published.
/// </summary>
/// <remarks>
/// <para>
/// Every variable below was found in the live catalogue rather than in the MSSP document, and the
/// shapes are the shapes real servers send (surveyed 2026-08-28 across 934 games with an MSSP
/// report). <c>PLAYERS</c> is the only one MSSP defines; the roster variables are conventions three
/// codebase families arrived at separately, which is why they are read by name and not by pattern.
/// </para>
/// <para>
/// This is one reader for two callers with opposite purposes. <c>PresenceChoice</c> asks it to
/// decide what to publish; <c>TelnetProbe</c> asks it to decide whether <c>WHO</c> still needs
/// typing at somebody's login screen. A count accepted by one and refused by the other would be a
/// probe that declined to ask and then had nothing to publish — our own restraint written down as
/// <c>who_not_offered</c>, a fact about their server (rule 5).
/// </para>
/// </remarks>
public static class MsspPresence
{
    /// <summary>The variable MSSP defines for a stated count, and requires of every server.</summary>
    public const string PlayersVariable = "PLAYERS";

    /// <summary>
    /// The variables a game publishes its roster in, best first.
    /// </summary>
    /// <remarks>
    /// Ordered rather than merged, so that two rosters in one report resolve the same way every
    /// time. No server has yet been seen publishing two, so the order is by how well each is
    /// understood rather than by any measured accuracy:
    /// <list type="bullet">
    /// <item><c>PLAYERNAMES</c> — one value, comma-separated. Circle/Nukefire.</item>
    /// <item><c>WHO</c> — <b>repeated</b>, one occurrence per player, and one empty occurrence when
    /// nobody is on. The Dead Souls/LPMud family, and the most widespread of the three (nine games
    /// in the catalogue).</item>
    /// <item><c>PLAYER INFO</c> — one value, comma-separated, each entry <c>name:role</c>
    /// (<c>Krem:arch</c>). Rise of Praxis, LPMud.</item>
    /// </list>
    /// Read by name and never by "does this value look like names": every roster here is
    /// indistinguishable in shape from <c>CLASSES - BASE 1 = Barbarian, Assassin, Slinger…</c>, and
    /// guessing between them would publish a class list as a population.
    /// </remarks>
    public static readonly IReadOnlyList<string> RosterVariables = ["PLAYERNAMES", "WHO", "PLAYER INFO"];

    /// <summary>
    /// The best count this report gives, or <see cref="MsspCount.None"/>.
    /// </summary>
    /// <remarks>
    /// Stated beats roster and is not corroborated against it: they disagree by design (see
    /// <see cref="MsspCountKind.Roster"/>), so treating a disagreement as a fault would hatch a cell
    /// for the one kind of server that answered the question twice.
    /// </remarks>
    public static MsspCount Read(IReadOnlyDictionary<string, IReadOnlyList<string>>? report)
    {
        if (report is null)
        {
            return MsspCount.None;
        }

        if (Stated(report) is { Found: true } stated)
        {
            return stated;
        }

        return Roster(report);
    }

    /// <summary>
    /// The count the game stated in <c>PLAYERS</c>, or <see cref="MsspCount.None"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Invariant culture, because <c>PLAYERS</c> is a wire value and not something formatted for the
    /// host this crawler happens to run on.
    /// </para>
    /// <para>
    /// Negative is refused rather than clamped, and that is not defensive coding: the
    /// Dragonfire/Void family publishes <c>-1</c> for anything it cannot answer —
    /// <c>dragonfiremud.com:1999</c> sends <c>OBJECTS = -1</c>, <c>SKILLS = -1</c>,
    /// <c>RACES = -1</c>, <c>INTERMUD = -1</c> alongside a real <c>PLAYERS</c>. It is that codebase's
    /// spelling of "I do not know", and publishing it as a number would invent one (rule 4).
    /// </para>
    /// </remarks>
    public static MsspCount Stated(IReadOnlyDictionary<string, IReadOnlyList<string>>? report) =>
        MsspReport.Last(report, PlayersVariable) is { } declared
        && int.TryParse(declared.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var count)
            ? new MsspCount(count, MsspCountKind.Stated, PlayersVariable)
            : MsspCount.None;

    /// <summary>
    /// The count of a roster the game published, or <see cref="MsspCount.None"/> where it published
    /// none.
    /// </summary>
    /// <remarks>
    /// <b>An empty roster is a measured zero, not a missing reading.</b> Dead Souls sends the
    /// variable with no value at all when nobody is on — <c>vithasnir</c> and <c>xanth-mud</c> were
    /// both observed sending <c>WHO = </c> beside <c>PLAYERS = 0</c>, which is what settles it. The
    /// distinction is the same one §5.4 draws for an empty I3 <c>users</c> array, and getting it
    /// backwards costs a real zero every time a game is quiet.
    /// </remarks>
    public static MsspCount Roster(IReadOnlyDictionary<string, IReadOnlyList<string>>? report)
    {
        if (report is null)
        {
            return MsspCount.None;
        }

        foreach (var variable in RosterVariables)
        {
            if (report.TryGetValue(variable, out var values))
            {
                return new MsspCount(Names(values).Count, MsspCountKind.Roster, variable);
            }
        }

        return MsspCount.None;
    }

    /// <summary>
    /// The names a roster's values hold, however the codebase spelled the list.
    /// </summary>
    /// <remarks>
    /// The two spellings are one loop: a repeated variable arrives as several values, a delimited
    /// list as one value with commas in it, and a codebase doing both would still be read correctly.
    /// Only the comma is a delimiter — the vertical bar MSSP uses elsewhere (<c>INTERMUD = i3 | IMC2</c>)
    /// has never been seen inside a roster, and admitting a separator on speculation is how a name
    /// with punctuation in it becomes two players.
    /// <para>
    /// Counted case-insensitively distinct: a MU* cannot have two players by one name, so a repeat is
    /// the report saying the same thing twice rather than a second person.
    /// </para>
    /// </remarks>
    private static HashSet<string> Names(IReadOnlyList<string> values)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var value in values)
        {
            foreach (var entry in value.Split(','))
            {
                // "Krem:arch" — the role a game annotates a name with is not part of the name, and
                // two roles for one player would otherwise count twice.
                var name = entry.Split(':')[0].Trim();

                if (name.Length > 0)
                {
                    names.Add(name);
                }
            }
        }

        return names;
    }

}
