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
    /// Values that mean "nobody filled this in". Compared case-insensitively after trimming.
    /// </summary>
    private static readonly HashSet<string> Placeholders = new(StringComparer.OrdinalIgnoreCase)
    {
        // Codebase names published as the game's name. AresMUSH and CobraMUSH were missing and are
        // the reason this comment exists: both are MUSH codebases an operator can leave unedited,
        // both are in the survey, and the list had every one of their siblings.
        //
        // ONLY NAMES NOBODY WOULD CALL A GAME. This list erases a name, so a codebase whose name is
        // also a plausible game title stays off it however often it is left unedited — Last Outpost,
        // Luminari and GodWars are games as well as codebases, and refusing them would delete a real
        // answer to stop a default. MeaningfulName already catches those from the other side, by
        // refusing a name that merely restates the game's own CODEBASE.
        "PennMUSH", "TinyMUSH", "TinyMUX", "MUX", "RhostMUSH", "Rhost", "CobraMUSH", "AresMUSH",
        "Evennia", "CoffeeMud", "CircleMUD", "tbaMUD", "ROM", "SMAUG", "Diku", "DikuMUD",
        "MudOS", "FluffOS", "LPMud", "LDMud", "MOO", "LambdaMOO", "TinyMUCK", "MUCK", "Fuzzball",

        // Template text left in place.
        "Unknown", "Unnamed", "Untitled", "N/A", "None", "TBD", "Change Me", "ChangeMe",
        "Your MUD Name", "Your MUD", "MUD Name", "My Server", "Example", "Test",
        "localhost", "0", "-1",
    };

    /// <summary>
    /// Whether a value is a codebase default, template text, or blank — all of which mean the field
    /// was never answered.
    /// </summary>
    public static bool IsPlaceholder(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        return Placeholders.Contains(value.Trim());
    }

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
