namespace MUI.Crawl;

/// <summary>
/// MSSP values that are the codebase's own defaults rather than anything an operator chose.
/// </summary>
/// <remarks>
/// A default is not a weak signal to be discounted — spec §7.3 weights MSSP <c>NAME</c> heavily
/// when matching identity, and every unedited PennMUSH publishes the same <c>NAME "PennMUSH"</c>.
/// Treating it as a signal would auto-merge unrelated games; it must be read as unset.
/// </remarks>
public static class MsspDefaults
{
    /// <summary>
    /// Codebase names, which mean "nobody filled this in" <em>when they arrive as a game's name</em>.
    /// </summary>
    /// <remarks>
    /// ONLY NAMES NOBODY WOULD CALL A GAME. This list erases a name, so a codebase whose name is also
    /// a plausible game title (Last Outpost, Luminari, GodWars) stays off it — deleting a real answer
    /// to stop a default is worse than missing one. <see cref="MeaningfulName"/> catches the other
    /// case, a name that merely restates the game's own <c>CODEBASE</c>.
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
    /// For fields where a codebase's name is not an answer, e.g. <c>NAME</c>. Not for <c>CODEBASE</c>
    /// itself, where "PennMUSH" is the answer — see <see cref="IsTemplate"/>.
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
    /// For fields whose purpose is to carry a codebase's name — <c>CODEBASE</c>, <c>MUDLIB</c>,
    /// <c>FAMILY</c>. <c>FluffOS</c> is a valid answer there; <c>Unknown</c> still isn't.
    /// </remarks>
    public static bool IsTemplate(string? value) =>
        string.IsNullOrWhiteSpace(value) || Templates.Contains(value.Trim());

    /// <summary>
    /// A game's name as declared, or null when the declaration is a default.
    /// </summary>
    /// <remarks>
    /// Also refuses a name that merely restates the codebase, e.g. <c>NAME "PennMUSH 1.8.8p0"</c>.
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
