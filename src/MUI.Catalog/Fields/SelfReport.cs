using System.Globalization;

namespace MUI.Catalog;

/// <summary>
/// Unofficial MSSP variables this site reads for more than their text: names, and the rules for
/// reading each.
/// </summary>
/// <remarks>
/// <para>
/// SharpMUSH reports all six; any codebase may. Every one is the game's own statement about itself,
/// stored under <see cref="FieldSource.Mssp"/> like any other variable and rendered as declared —
/// none of them is something we observed, and none of them reaches presence, reachability or a
/// ranking.
/// </para>
/// <para>
/// One spelling here, read by the crawler (which variables keep every value), the reconciler (which
/// variables move without an event) and every surface, so the three cannot disagree about a name.
/// </para>
/// </remarks>
public static class SelfReport
{
    /// <summary>Distinct players connected in the trailing 24 hours, by the game's own count.</summary>
    public const string PlayersDay = "PLAYERS DAY";

    /// <summary>Distinct players connected in the trailing seven days, by the game's own count.</summary>
    public const string PlayersWeek = "PLAYERS WEEK";

    /// <summary><c>1</c> when players may log in.</summary>
    public const string Logins = "LOGINS";

    /// <summary><c>1</c> when a new character may be created at the login screen.</summary>
    public const string PlayerCreation = "PLAYER CREATION";

    /// <summary><c>1</c> when guests may connect.</summary>
    public const string Guests = "GUESTS";

    /// <summary>
    /// A page of the game's web portal anyone signed in may open: an absolute URL, one space, a
    /// display name. Repeated, one value per page.
    /// </summary>
    public const string PortalApplication = "PORTAL APPLICATION";

    /// <summary>
    /// The variables MSSP sends repeated where every value is a separate fact, and which are
    /// therefore stored as all of them rather than the last.
    /// </summary>
    /// <remarks>
    /// Everything else keeps MSSP's own rule, last value wins. A list is joined with
    /// <see cref="ListSeparator"/>, which cannot occur inside one of these values as defined — a
    /// URL holds no line break and the name is the rest of one line.
    /// </remarks>
    public static IReadOnlySet<string> Lists { get; } =
        new HashSet<string>(StringComparer.Ordinal) { PortalApplication };

    /// <summary>How the values of one of <see cref="Lists"/> are joined into one stored value.</summary>
    public const char ListSeparator = '\n';

    /// <summary>
    /// A flag's value as yes or no, or null for anything else — which then prints as the game wrote
    /// it rather than as our reading of it.
    /// </summary>
    /// <remarks>
    /// Only <c>1</c> and <c>0</c>. MSSP's capability rule (anything not an explicit no is a yes)
    /// is not borrowed: a <c>LOGINS</c> of <c>wizards only</c> is a sentence, and calling it "yes"
    /// would be our paraphrase published as their answer.
    /// </remarks>
    public static bool? Flag(string? value) => value?.Trim() switch
    {
        "1" => true,
        "0" => false,
        _ => null,
    };

    /// <summary>
    /// A count as a number, or null when it is not a whole number at or above zero.
    /// </summary>
    /// <remarks>
    /// Invariant and unsigned for the same reason <see cref="MsspLint"/> reads counts that way: a
    /// current-culture parse would accept a grouped <c>1,024</c> on one deployment and refuse it on
    /// another, and no count of players means anything below zero. A value that fails is not
    /// dropped — the row keeps exactly what the game sent — it is only never read as a number.
    /// </remarks>
    public static int? Count(string? value) =>
        int.TryParse(value?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var count)
            ? count
            : null;
}

/// <summary>
/// The game's own count of distinct players over the last day and week, as the game page reads it.
/// </summary>
/// <remarks>
/// Built only from rows whose value is a count (<see cref="SelfReport.Count"/>); a side that is not
/// a count is null here and stays on the page verbatim under "Declared by the game". Never a
/// presence sample and never a ranking input — it is a figure the game computed about itself, and
/// the presence series holds only counts read at a moment.
/// </remarks>
public sealed record RecentPlayers(int? Day, int? Week, ProvenanceChip Provenance)
{
    /// <summary>
    /// What the game said, or null where it said neither as a count.
    /// </summary>
    /// <param name="declared">A game page's declared fields, keyed lower-invariant.</param>
    /// <remarks>
    /// One chip for the sentence: the two arrive in one report, so they share a source; the older of
    /// the two ages is the one shown, since a sentence dated by its fresher half would overstate the
    /// staler one.
    /// </remarks>
    public static RecentPlayers? From(IReadOnlyDictionary<string, ProvenanceChip> declared)
    {
        ArgumentNullException.ThrowIfNull(declared);

        var day = Read(declared, SelfReport.PlayersDay);
        var week = Read(declared, SelfReport.PlayersWeek);

        if (day is null && week is null)
        {
            return null;
        }

        var chip = (day, week) switch
        {
            ({ } d, { } w) => d.Chip.LastConfirmedAt <= w.Chip.LastConfirmedAt ? d.Chip : w.Chip,
            ({ } d, null) => d.Chip,
            (null, { } w) => w.Chip,
            _ => throw new InvalidOperationException("unreachable"),
        };

        return new RecentPlayers(day?.Count, week?.Count, chip);
    }

    /// <summary>
    /// Whether a declared field is said by this sentence and so need not be listed again.
    /// </summary>
    public static bool Says(string field, ProvenanceChip chip) =>
        (string.Equals(field, SelfReport.PlayersDay, StringComparison.OrdinalIgnoreCase)
            || string.Equals(field, SelfReport.PlayersWeek, StringComparison.OrdinalIgnoreCase))
        && SelfReport.Count(chip.Value) is not null;

    private static (int Count, ProvenanceChip Chip)? Read(
        IReadOnlyDictionary<string, ProvenanceChip> declared,
        string field) =>
        declared.TryGetValue(field.ToLowerInvariant(), out var chip)
        && SelfReport.Count(chip.Value) is { } count
            ? (count, chip)
            : null;
}

/// <summary>One page of a game's web portal, as the game named it.</summary>
/// <param name="Href">
/// Normalised through <see cref="ExternalUrl"/>, never the stored text, so a surface putting it in an
/// <c>href</c> need not judge a stranger's scheme. Null when the line is not a URL and a name.
/// </param>
/// <param name="Name">The name the game gave the page, or null where <see cref="Href"/> is.</param>
/// <param name="Shown">The line exactly as the game sent it, for the reader who wants it.</param>
public sealed record PortalApplication(string? Href, string? Name, string Shown);

/// <summary>Reads <see cref="SelfReport.PortalApplication"/>.</summary>
/// <remarks>
/// <para>
/// <b>It refuses rather than repairs, like <see cref="ExternalUrl"/>.</b> A line becomes a link only
/// when its first word is an absolute http or https address and something follows it: a URL with no
/// name would make us choose the link text, and a name with no URL is not a destination. Anything
/// else comes back with no <see cref="PortalApplication.Href"/> and renders as the text the game
/// sent, so nothing it published is hidden and nothing it did not publish is linked.
/// </para>
/// <para>
/// The first space separates and every later one belongs to the name — <c>Character Application</c>
/// is one name, not a URL followed by two words.
/// </para>
/// </remarks>
public static class PortalApplications
{
    public static IReadOnlyList<PortalApplication> Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var pages = new List<PortalApplication>();

        foreach (var raw in value.Split(SelfReport.ListSeparator))
        {
            var line = raw.Trim();

            if (line.Length == 0)
            {
                continue;
            }

            pages.Add(Read(line));
        }

        return pages;
    }

    private static PortalApplication Read(string line)
    {
        var space = line.IndexOf(' ', StringComparison.Ordinal);

        if (space <= 0)
        {
            return new PortalApplication(null, null, line);
        }

        var name = line[(space + 1)..].Trim();

        return name.Length > 0 && ExternalUrl.Normalise(line[..space], FieldShape.Url) is { } href
            ? new PortalApplication(href, name, line)
            : new PortalApplication(null, null, line);
    }
}
