namespace MUI.Crawl;

/// <summary>
/// Whether a probe shows a MU* on the other end of the socket (spec §7.8) — this is the measurement
/// that replaces waiting on an unclaimed submission (§4.4).
/// </summary>
/// <remarks>
/// One signal is enough: requiring two cost real games (Diku/LP families say almost nothing at a
/// login screen) and bought nothing measurable. Two tiers by what a host has to be able to do — a
/// protocol signal (option negotiation, MSSP, a parseable WHO) is something no non-game produces;
/// the vocabulary tier (<see cref="CharacterIdiom"/>) is weaker and narrower on purpose.
/// </remarks>
public static class MuLikeness
{
    /// <summary>The MSSP signal's name, which is the same whichever way MSSP arrived.</summary>
    private const string Mssp = "mssp";

    /// <summary>
    /// The telnet options only a MU* negotiates, and the name each is recorded under.
    /// </summary>
    /// <remarks>
    /// Generic telnet options (TTYPE, NAWS, CHARSET, NEW-ENVIRON, EOR, SUPPRESS GO AHEAD, ECHO) are
    /// deliberately excluded — every telnet daemon negotiates them, so admitting one would publish
    /// any host that speaks telnet at all. MCCP2/MCCP3 fold into <c>mccp</c> (matching
    /// <c>CapabilityFields</c>) because the option is versioned and the capability is not; the alias
    /// is repeated rather than shared since <c>MUI.Crawl</c> deliberately has no reference to
    /// <c>MUI.Catalog</c>.
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, string> MuOptions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["MSSP"] = Mssp,
            ["GMCP"] = "gmcp",
            ["MSDP"] = "msdp",
            ["MXP"] = "mxp",
            ["MSP"] = "msp",
            ["MCCP"] = "mccp",
            ["MCCP2"] = "mccp",
            ["MCCP3"] = "mccp",
            ["ATCP"] = "atcp",
            ["ZMP"] = "zmp",
            ["PUEBLO"] = "pueblo",
        };

    /// <summary>
    /// Phrases that a MU* login screen produces <em>in reply to something we typed</em>, and that
    /// other multi-user telnet services do not.
    /// </summary>
    /// <remarks>
    /// Every entry is about a character or a player, never an account/login/password — that omission
    /// is the discriminator that keeps this from also matching non-game telnet services whose account
    /// prompts read almost identically. Grow this list only from a real capture, never from a phrase
    /// that merely sounds MUD-like — that's the per-codebase treadmill §6.3 refused for WHO parsing.
    /// <c>MuLikenessTests</c> carries the negative fixtures that guard it.
    /// </remarks>
    private static readonly string[] CharacterIdiom =
    [
        "no such player",        // batmud.bat.org:23
        "create a new character",// batmud.bat.org:23
        "create a character",    // kotl.org:2221
        "new character",         // the same menu, worded the other way round
        "character name",
        "who is online",         // kotl.org:2221
        "who is playing",        // batmud.bat.org:23
        "enter the game",        // batmud.bat.org:23
        "by what name",          // Diku's stock prompt: "By what name do you wish to be known?"
    ];

    /// <summary>The order signals are reported in, so the stored record of a publication is stable.</summary>
    private static readonly string[] Order =
        [Mssp, "gmcp", "msdp", "mxp", "msp", "mccp", "atcp", "zmp", "pueblo", "who", "codebase", "vocabulary"];

    /// <summary>
    /// The signals this probe carries. Empty means "not a game, as far as this probe can tell" —
    /// which is a statement about the probe and never about the host.
    /// </summary>
    public static IReadOnlyList<string> Signals(ProbeResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.Outcome is not ProbeOutcome.Answered)
        {
            return [];
        }

        var found = new HashSet<string>(StringComparer.Ordinal);

        // A report dropped at our own size ceiling is still a report the server sent (§6.4) —
        // treating our limit as the server's silence is the bug MsspOutcome exists to prevent.
        if (result.MsspOutcome is MsspOutcome.Received or MsspOutcome.RejectedTooLarge)
        {
            found.Add(Mssp);
        }

        foreach (var option in result.OfferedOptions)
        {
            if (MuOptions.TryGetValue(option, out var name))
            {
                found.Add(name);
            }
        }

        // A count, not an attempt — an unreadable WHO says nothing about what's behind it (§5.4).
        if (result.Who.HasCount)
        {
            found.Add("who");
        }

        // Structured and elicited, so strictly stronger than the phrase list below: a MUSH answering
        // INFO doesn't depend on having said "create a character".
        if (LoginCommandReading.MeaningfulCodebase(result.Info, result.Version) is not null)
        {
            found.Add("codebase");
        }

        if (SpeaksOfCharacters(result.Info) || SpeaksOfCharacters(result.Version))
        {
            found.Add("vocabulary");
        }

        return [.. Order.Where(found.Contains)];
    }

    /// <summary>Whether §7.8 would publish a submission on the strength of this probe.</summary>
    public static bool LooksLikeAGame(ProbeResult result) => Signals(result).Count > 0;

    /// <summary>
    /// Whether an elicited reply talks about characters.
    /// </summary>
    /// <remarks>
    /// Elicited only, never the banner — a connect screen is bytes anyone can paste, so reading these
    /// phrases off one would let a host copy a MUD's login screen into passing. <see
    /// cref="BannerText.Flatten"/> strips colour codes first: some servers split a matched phrase
    /// across an SGR run, so an unflattened match would miss it.
    /// </remarks>
    private static bool SpeaksOfCharacters(string? elicited)
    {
        if (elicited is not { Length: > 0 })
        {
            return false;
        }

        var flattened = BannerText.Flatten(elicited);

        return CharacterIdiom.Any(phrase =>
            flattened.Contains(phrase, StringComparison.OrdinalIgnoreCase));
    }
}
