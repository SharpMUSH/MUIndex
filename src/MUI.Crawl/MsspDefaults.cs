namespace MUI.Crawl;

/// <summary>
/// MSSP values that are the codebase's own defaults rather than anything an operator chose.
/// </summary>
/// <remarks>
/// <para>
/// Observed rather than theorised: the second real server this crawler ever probed publishes
/// <c>NAME "PennMUSH"</c>, because whoever installed it never edited that line. A directory that
/// trusts the field lists the game under its codebase's name.
/// </para>
/// <para>
/// <b>The dangerous consequence is identity, not display.</b> Spec §7.3 weights MSSP <c>NAME</c>
/// heavily when deciding whether two endpoints are the same game — and every unedited PennMUSH on
/// the internet publishes the same one. Treating that as a signal would score dozens of unrelated
/// games as matches for each other and auto-merge them. A default is not a weak signal to be
/// discounted; it is <b>the absence of a signal</b>, and must be read as unset.
/// </para>
/// </remarks>
public static class MsspDefaults
{
    /// <summary>
    /// Codebase names, which mean "nobody filled this in" <em>when they arrive as a game's name</em>.
    /// </summary>
    /// <remarks>
    /// ONLY NAMES NOBODY WOULD CALL A GAME. This list erases a name, so a codebase whose name is also
    /// a plausible game title stays off it however often it is left unedited — Last Outpost, Luminari
    /// and GodWars are games as well as codebases, and refusing them would delete a real answer to
    /// stop a default. <see cref="MeaningfulName"/> already catches those from the other side, by
    /// refusing a name that merely restates the game's own <c>CODEBASE</c>.
    ///
    /// AresMUSH and CobraMUSH were missing and are the reason this comment exists: both are MUSH
    /// codebases an operator can leave unedited, both are in the survey, and the list had every one
    /// of their siblings.
    /// </remarks>
    private static readonly HashSet<string> CodebaseNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "PennMUSH", "TinyMUSH", "TinyMUX", "MUX", "RhostMUSH", "Rhost", "CobraMUSH", "AresMUSH",
        "Evennia", "CoffeeMud", "CircleMUD", "tbaMUD", "ROM", "SMAUG", "Diku", "DikuMUD",
        "MudOS", "FluffOS", "LPMud", "LDMud", "MOO", "LambdaMOO", "TinyMUCK", "MUCK", "Fuzzball",
    };

    /// <summary>
    /// Template text left in place, which means "nobody filled this in" <em>in any field</em>.
    /// </summary>
    private static readonly HashSet<string> Templates = new(StringComparer.OrdinalIgnoreCase)
    {
        "Unknown", "Unnamed", "Untitled", "N/A", "None", "TBD", "Change Me", "ChangeMe",
        "Your MUD Name", "Your MUD", "MUD Name", "My Server", "Example", "Test",
        "localhost", "0", "-1",
    };

    /// <summary>
    /// Whether a value is a codebase default, template text, or blank — all of which mean the field
    /// was never answered.
    /// </summary>
    /// <remarks>
    /// <b>For fields where a codebase's name is not an answer.</b> Its first caller was
    /// <c>NAME</c>, where "PennMUSH" is the absence of a signal, and the two halves of the list were
    /// one set until a reader of <c>CODEBASE</c> arrived — for which "PennMUSH" is the answer itself
    /// and this test would have deleted it. See <see cref="IsTemplate"/>.
    /// </remarks>
    public static bool IsPlaceholder(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var trimmed = value.Trim();

        return CodebaseNames.Contains(trimmed) || Templates.Contains(trimmed);
    }

    /// <summary>
    /// Whether a value is template text or blank, without treating a codebase's name as one.
    /// </summary>
    /// <remarks>
    /// The test for a field whose whole purpose is to carry a codebase's name — <c>CODEBASE</c>,
    /// <c>MUDLIB</c>, <c>FAMILY</c>, and the driver and mudlib an I3 mudlist entry carries.
    /// <c>Unknown</c> there is still nothing; <c>FluffOS</c> there is the answer.
    /// </remarks>
    public static bool IsTemplate(string? value) =>
        string.IsNullOrWhiteSpace(value) || Templates.Contains(value.Trim());

    /// <summary>
    /// A game's name as declared, or null when the declaration is a default.
    /// </summary>
    /// <remarks>
    /// Also refuses a name that merely restates the codebase — <c>NAME "PennMUSH 1.8.8p0"</c> is the
    /// same non-answer as <c>NAME "PennMUSH"</c>, and the pair travels together often enough to be
    /// worth catching without adding every version string to the list above.
    /// </remarks>
    public static string? MeaningfulName(string? name, string? codebase)
    {
        if (IsPlaceholder(name))
        {
            return null;
        }

        var trimmed = name!.Trim();

        if (!string.IsNullOrWhiteSpace(codebase))
        {
            var family = codebase.Trim().Split(' ')[0];
            if (trimmed.Equals(codebase.Trim(), StringComparison.OrdinalIgnoreCase)
                || trimmed.Equals(family, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }

        return trimmed;
    }
}
