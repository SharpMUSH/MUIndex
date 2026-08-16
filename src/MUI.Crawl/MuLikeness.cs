namespace MUI.Crawl;

/// <summary>
/// Whether a probe shows a MU* on the other end of the socket (spec §7.8).
/// </summary>
/// <remarks>
/// <para>
/// <b>This exists to publish submissions, and nothing else reads it.</b> An address the crawler
/// found for itself is listed on sight (§4.4); an address a stranger typed into a form waited for a
/// claim that mostly never came. §7.8 replaces that wait with a measurement, and this is the
/// measurement.
/// </para>
/// <para>
/// <b>The rubric is what survived being measured, not what sounded principled.</b> The first draft
/// wanted two signals with one of them behavioural. Run against the 432 games in the live catalogue
/// on 16 August 2026 it published 189 of them — it refused Achaea, BatMUD and ArcticMUD, because the
/// Diku and LP families say almost nothing at a login screen. Requiring a second signal cost thirty
/// games and bought nothing measurable, so one is enough.
/// </para>
/// <para>
/// <b>Two tiers, and the split is about what a host has to be able to do.</b> A protocol signal is
/// something no non-game produces: freechess.org, telehack and towel.blinkenlights.nl were dialled
/// the same day and not one of them negotiated a MU* option, answered MSSP, or returned a WHO that
/// parses. The vocabulary tier is weaker and narrower on purpose — see
/// <see cref="CharacterIdiom"/>.
/// </para>
/// <para>
/// It lives in <c>MUI.Crawl</c> beside the other readings of a <c>ProbeResult</c> and, like them,
/// knows nothing about the catalogue. Whether a game is submitted, hidden or claimed is not a
/// question about the socket.
/// </para>
/// </remarks>
public static class MuLikeness
{
    /// <summary>The MSSP signal's name, which is the same whichever way MSSP arrived.</summary>
    private const string Mssp = "mssp";

    /// <summary>
    /// The telnet options only a MU* negotiates, and the name each is recorded under.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The list is short because the exclusions are the point.</b> TTYPE, NAWS, CHARSET,
    /// NEW-ENVIRON, EOR, SUPPRESS GO AHEAD and ECHO are on this list nowhere: every telnet daemon
    /// negotiates them, so admitting one would publish any host that speaks telnet at all.
    /// </para>
    /// <para>
    /// <c>MCCP2</c> and <c>MCCP3</c> fold into <c>mccp</c> for the reason <c>CapabilityFields</c>
    /// folds them: the option is versioned and the capability is not. That class cannot be reached
    /// from here — <c>MUI.Crawl</c> holds no reference to <c>MUI.Catalog</c>, deliberately — so the
    /// alias is repeated rather than shared, which is a smaller cost than the arrow it would take.
    /// </para>
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
    /// <para>
    /// <b>Every entry is about a character or a player.</b> Not one is about an account, a login or a
    /// password, and that omission is the whole discriminator. <c>freechess.org:5000</c> — a chess
    /// server — answers <c>INFO</c> with <c>"who" is a registered name … password: … login:</c>, and
    /// <c>tirradyn.com:9010</c> — a real MUD — answers with <c>No account by that name exists. Type
    /// 'new' to create a new account.</c> The two are the same sentence. A list wide enough to
    /// publish tirradyn publishes the chess server too, so tirradyn waits for the operator queue.
    /// </para>
    /// <para>
    /// <b>The list may only grow from a capture.</b> Each entry below was read off a real server on
    /// 16 August 2026 or is the Diku family's stock prompt; adding a phrase because it sounds like
    /// something a MUD might say is how this becomes the per-codebase treadmill §6.3 refused for WHO
    /// parsing. The negative fixtures in <c>MuLikenessTests</c> are the guard on that.
    /// </para>
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
            // Nothing was measured, so there is nothing to have shown. A dial that never got in is
            // the same silence from a game and from a closed port.
            return [];
        }

        var found = new HashSet<string>(StringComparer.Ordinal);

        // Asked and answered. A report we dropped at our own ceiling is still a report the server
        // sent (§6.4) — reading our size limit as the server's silence is the error MsspOutcome
        // exists to prevent.
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

        // A count, not an attempt. An unreadable WHO is the parser meeting a dialect it cannot read
        // and says nothing about what is behind it (§5.4).
        if (result.Who.HasCount)
        {
            found.Add("who");
        }

        // An elicited reply that names a MU* family, read by the same conservative reader the
        // catalogue's CODEBASE field goes through. Elicited, structured, and out of reach of
        // anything that is not running the software — strictly stronger than the phrase list below,
        // and the reason a MUSH answering INFO does not depend on having said "create a character".
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
    /// <b>Elicited only, never the banner.</b> The tier's whole claim is that the server processed
    /// what we typed and answered in its own idiom; a connect screen is bytes anyone can paste, and
    /// reading these phrases off one would hand the rubric to the first host that copied a MUD's
    /// login screen. <see cref="BannerText.Flatten"/> does the reading because colour codes run
    /// through the middle of these phrases — <c>kotl.org</c> writes <c>Create a </c> and
    /// <c>Character</c> either side of an SGR run — and a matcher that does not strip them reads
    /// nothing at all.
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
