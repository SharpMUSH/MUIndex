using System.Text.RegularExpressions;

namespace MUI.Crawl;

/// <summary>
/// The codebase a connect screen credits, read from the attribution the screen carries.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is not pattern-matching a stranger's ASCII art for a family name. It is reading a licence
/// notice.</b> The DikuMUD licence requires the original authors to be credited on the login screen,
/// and every descendant carries the line forward and adds its own — which is why the same five names
/// appear verbatim on games thirty years apart, and why <c>badtrip</c> credits Diku, Merc <em>and</em>
/// ROM in three consecutive sentences.
/// </para>
/// <para>
/// <b>Run over the 303 stored connect screens of games with no codebase on record, this names 132 of
/// them</b> — 27 DikuMUD, 26 CircleMUD, 21 Merc, 21 ROM, 10 SMAUG, and the rest across the LP line and
/// CoffeeMUD and Rapture. Every one of those 132 has either an author's name in it or an attribution
/// phrase within 120 characters of the family it named; none was matched loosely. That check is worth
/// re-running after any widening here, because it is the difference between this file and the guess
/// the comment above says it is not.
/// </para>
/// <para>
/// <b>The most derived family wins, because that is what the stack of credits means.</b> A screen
/// naming Diku and Merc and ROM is a ROM: each layer credits the one below it, so the outermost is
/// the answer and the ones beneath it are its ancestry. Reading Diku off <c>badtrip</c> would be
/// technically true and useless.
/// </para>
/// <para>
/// <b>The family name and never a version.</b> <c>addictmud</c> says
/// <c>Based on CircleMUD 3.0bpl10</c> and this reads <c>CircleMUD</c>. Lifting the version out is
/// where PR #51 went wrong — the value that comes out of free text is the one nobody thinks to
/// doubt, and a version parsed out of prose is a guess wearing a decimal point. The value lands on
/// <see cref="MUI.Catalog.FieldSource.Banner"/>, the bottom rung, where a game that fills in its own
/// MSSP <c>CODEBASE</c> outranks it and the disagreement between them is the interesting fact
/// (rule 1).
/// </para>
/// <para>
/// <b>Two tiers, because two kinds of evidence are not the same evidence.</b> <c>Katja Nyboe</c> and
/// <c>Russ Taylor</c> are people who appear on a MU* connect screen for exactly one reason, so they
/// are read wherever they occur. <c>SMAUG</c> and <c>MudOS</c> are words that could be a realm, a
/// player or a room, so they are read only on a line that is doing attribution — <c>based on</c>,
/// <c>derived from</c>, <c>powered by</c>, <c>copyright</c>. <c>rom</c>, <c>merc</c> and <c>moo</c>
/// are in neither tier at any length: three letters that occur inside ordinary words, and their
/// families are reachable through their authors' names instead.
/// </para>
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

        // A credit is read within one sentence, never across a whole screen. Two physical lines are
        // joined first, because a licence notice wraps — Diku's five authors span three lines on
        // most screens that carry them — and then split on sentence punctuation, because a screen
        // that says "Powered by dreams." above "Come and visit MudOS Tavern." must not lend the
        // second sentence the first one's attribution.
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
    /// <para>
    /// The order is the derivation graph read outwards, and every entry in the Diku half of it was
    /// taken from a live connect screen: <c>tbaMUD</c> and <c>SMAUG</c> and <c>ROM</c> all sit on
    /// Merc, Merc sits on Diku, and CircleMUD sits on Diku beside Merc rather than under it. A screen
    /// that credits two of them is not ambiguous — it is a game telling us its ancestry, outermost
    /// layer first.
    /// </para>
    /// <para>
    /// <b>The named-engine block is first and unordered within itself</b>, because those are families
    /// nothing else derives from: a screen that says <c>Evennia</c> or <c>CoffeeMud</c> or
    /// <c>AresMUSH</c> is not also a ROM. <c>achaea-dreams-of-divine-lands</c> opens
    /// <c>Rapture Runtime Environment v2.4.9.1 -- (c) 2026 -- Iron Realms Entertainment</c>, which is
    /// the only public statement that engine makes about itself anywhere.
    /// </para>
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
    /// Every phrase is one a live screen uses: <c>Based on CircleMUD 3.0bpl10,</c> (addictmud),
    /// <c>Based upon DikuMUD I (GAMMA 0.0) created by</c> (alexmud),
    /// <c>A derivative of DikuMUD (GAMMA 0.0)</c> (addictmud),
    /// <c>running the MudOSa4DGD v1.1 mudlib on DGD v1.2p2</c> (asgard-s-honor),
    /// <c>Rom 2.4 copyright (c) 1993-1996</c> (badtrip), and
    /// <c>Rapture Runtime Environment v2.4.9.1 -- (c) 2026</c> (achaea).
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
