using System.Text.RegularExpressions;

namespace MUI.Crawl;

/// <summary>
/// The codebase a connect screen credits, read from the attribution the screen carries.
/// </summary>
/// <remarks>
/// This reads a licence notice, not a stranger's ASCII art: the DikuMUD licence requires the original
/// authors to be credited on the login screen, and every descendant carries the line forward and adds
/// its own, so a screen can credit several families at once (outermost/most-derived first — a screen
/// naming Diku, Merc and ROM is a ROM). The family name is kept, never a version — a version parsed
/// out of free text is a guess wearing a decimal point, so it lands on
/// <see cref="MUI.Catalog.FieldSource.Banner"/>, the bottom rung, where a game's own MSSP
/// <c>CODEBASE</c> outranks it. Two evidence tiers: a person's name (<c>Russ Taylor</c>) is credited
/// wherever it occurs, but an engine name that's also an ordinary word (<c>SMAUG</c>, <c>MudOS</c>)
/// only counts on a line that's doing attribution (<c>based on</c>, <c>derived from</c>, …).
/// </remarks>
public static partial class CodebaseCredits
{
    /// <summary>
    /// The family this screen credits, most derived first, or null when it credits none.
    /// </summary>
    public static string? Named(string? banner)
    {
        if (string.IsNullOrWhiteSpace(banner))
        {
            return null;
        }

        var lines = Sentences(banner);

        // A credit is read within one sentence, never across the whole screen — otherwise an
        // unrelated line below an attribution (e.g. a tavern's name mentioning "MudOS") would borrow
        // its credit.
        foreach (var (marker, family, tier) in Credits)
        {
            foreach (var line in lines)
            {
                if (!FamilyWord.Names(line, marker))
                {
                    continue;
                }

                if (tier is Tier.Author || AttributionPattern().IsMatch(line))
                {
                    return family;
                }
            }
        }

        return null;
    }

    /// <summary>How much a marker has to be helped before it can be believed.</summary>
    private enum Tier
    {
        /// <summary>A person credited by name, which happens on these screens for one reason only.</summary>
        Author,

        /// <summary>An engine's name, which is also a word — believed only where a line is crediting.</summary>
        Engine,
    }

    /// <summary>
    /// Every marker, in precedence order: the first family whose credit appears is the answer.
    /// </summary>
    /// <remarks>
    /// Order follows the derivation graph, outermost first: tbaMUD/SMAUG/ROM sit on Merc, Merc sits
    /// on Diku, CircleMUD sits on Diku beside Merc rather than under it. The named-engine block comes
    /// first and is unordered within itself, since those families derive from nothing else.
    /// </remarks>
    private static readonly (string Marker, string Family, Tier Tier)[] Credits =
    [
        // Engines with no descendants in the hobby, so nothing can outrank them.
        ("evennia", "Evennia", Tier.Engine),
        ("coffeemud", "CoffeeMUD", Tier.Engine),
        ("aresmush", "AresMUSH", Tier.Engine),
        ("ranvier", "Ranvier", Tier.Engine),
        ("rapture runtime", "Rapture", Tier.Engine),
        ("pennmush", "PennMUSH", Tier.Engine),
        ("rhostmush", "RhostMUSH", Tier.Engine),
        ("cobramush", "CobraMUSH", Tier.Engine),
        ("tinymux", "TinyMUX", Tier.Engine),
        ("tinymush", "TinyMUSH", Tier.Engine),
        ("protomuck", "ProtoMUCK", Tier.Engine),
        ("glowmuck", "GlowMUCK", Tier.Engine),
        ("fuzzball", "Fuzzball MUCK", Tier.Engine),
        ("tinymuck", "TinyMUCK", Tier.Engine),

        // The Diku line, outermost first.
        ("tbamud", "tbaMUD", Tier.Engine),
        ("smaug", "SMAUG", Tier.Engine),
        ("thoric", "SMAUG", Tier.Author),
        ("derek snider", "SMAUG", Tier.Author),
        ("russ taylor", "ROM", Tier.Author),
        ("circlemud", "CircleMUD", Tier.Engine),
        ("circle mud", "CircleMUD", Tier.Engine),
        ("jeremy elson", "CircleMUD", Tier.Author),
        ("hatchet", "Merc", Tier.Author),
        ("dikumud", "DikuMUD", Tier.Engine),
        ("diku", "DikuMUD", Tier.Engine),
        ("staerfeldt", "DikuMUD", Tier.Author),
        ("stærfeldt", "DikuMUD", Tier.Author),
        ("nyboe", "DikuMUD", Tier.Author),
        ("michael seifert", "DikuMUD", Tier.Author),
        ("sebastian hammer", "DikuMUD", Tier.Author),

        // The LP line. `dgd` and `lpc` are three letters and are read as engines rather than
        // authors for the same reason `rom` is absent altogether.
        ("fluffos", "FluffOS", Tier.Engine),
        ("mudos", "MudOS", Tier.Engine),
        ("ldmud", "LDMud", Tier.Engine),
        ("dgd", "DGD", Tier.Engine),
        ("lpmud", "LPMud", Tier.Engine),
        ("mudlib", "LPMud", Tier.Engine),
    ];

    /// <summary>
    /// Whether this sentence is doing attribution, which is what makes an engine's name a credit
    /// rather than a word.
    /// </summary>
    /// <remarks>
    /// E.g. <c>Based on CircleMUD 3.0bpl10,</c>, <c>A derivative of DikuMUD (GAMMA 0.0)</c>,
    /// <c>Rom 2.4 copyright (c) 1993-1996</c>.
    /// </remarks>
    [GeneratedRegex(
        @"\b(?:based\s+(?:on|upon)|derivative\s+of|derived\s+from|powered\s+by|running(?:\s+on)?"
        + @"|built\s+(?:on|with)|code\s+by|coded\s+(?:by|in)|created\s+by|written\s+by|copyright"
        + @"|original)\b|\(c\)|©",
        RegexOptions.IgnoreCase)]
    private static partial Regex AttributionPattern();

    /// <summary>
    /// The screen as sentences: escape sequences gone, wrapped lines rejoined, claims kept apart.
    /// </summary>
    private static string[] Sentences(string banner) =>
        SentenceBreakPattern()
            .Split(BannerText.Flatten(banner))
            .Select(part => part.Trim())
            .Where(part => part.Length > 0)
            .ToArray();

    [GeneratedRegex(@"(?<=[.!?;])\s+")]
    private static partial Regex SentenceBreakPattern();
}
