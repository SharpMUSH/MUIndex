namespace MUI.Crawl;

/// <summary>
/// Whether an MSSP report already answers everything <c>INFO</c> and <c>VERSION</c> are asked for.
/// </summary>
/// <remarks>
/// <para>
/// The same argument as <c>MsspPresence</c>, one step further out. A game that has published its
/// name, what it runs, and how many are on has answered all three login-screen questions, and
/// typing any of them at its prompt is noise on somebody else's console — which is not a figure of
/// speech here. Against <c>playdecay.com:3003</c>, whose operator raised this, the old probe typed
/// <c>WHO</c> at "By what name do you wish to be known?", had it taken as a character name, was
/// asked for a password, and sent <c>INFO</c> as the password. <i>Wrong password.</i> Every crawl,
/// reproducibly, for a game whose report carried the answers the whole time.
/// </para>
/// <para>
/// Three conditions, one per consumer, so nothing downstream loses an input it had:
/// </para>
/// <list type="bullet">
/// <item>A meaningful <c>NAME</c> — what <c>LoginCommandReading.MeaningfulName</c> reads INFO for,
/// and what <c>CatalogueBinder.IdentifiedItself</c> needs before a stranger-proposed address may be
/// listed.</item>
/// <item>A <c>CODEBASE</c> that is not template text <b>and that names an engine this project
/// recognises</b> — what <c>MeaningfulCodebase</c> reads both replies for.
/// <see cref="MsspDefaults.IsTemplate"/> rather than <c>IsPlaceholder</c>, because <c>FluffOS</c> is
/// the answer to this question rather than a sign nobody filled it in.</item>
/// <item>A count — what <c>LoginCommandReading.ConnectedPlayers</c> reads INFO's <c>Connected:</c>
/// line for, and §5.2's rung above it.</item>
/// </list>
/// <para>
/// The engine condition is not belt-and-braces, and it was measured against the whole catalogue.
/// Gating on a non-template <c>CODEBASE</c> alone silences 170 games and costs 33 of them the
/// <c>banner</c>-sourced <c>CODEBASE</c> row <c>INFO</c> had been filling. Thirty-one of those rows
/// merely restated the report. <b>Two did not</b>: <c>northern-crossroads-ncmud</c> declares
/// <c>NC-7.0.357.7940b961</c> and its <c>INFO</c> is the only thing that says <c>DikuMUD</c>;
/// <c>primal-darkness-ii</c> declares <c>PD/NM III</c> and its <c>INFO</c> is the only thing that
/// says <c>FluffOS</c>. Both are one shape — a custom build string naming no engine — so requiring
/// the report to name one keeps exactly those games being asked.
/// <para>
/// With the condition in, 72 of the 170 keep their <c>INFO</c> and <c>VERSION</c>, and of the rows
/// still given up not one loses an engine: six restate the report in coarser words
/// (<c>CoffeeMUD</c> for <c>CoffeeMUD v5.11.0.1</c>, <c>ROM</c> for <c>Diku Merc Rom RoT AoD</c>),
/// and five are mangled readings of an Evennia <c>INFO</c> block — <c>enniaA 5.0.1</c>,
/// <c>enniaF 6.0.0 (rev ea0da3ed8)R ##D HRINFO0m</c> — that the report states cleanly as
/// <c>Evennia</c>. One is a judgement call worth writing down: <c>neonmoo</c>'s screen reads
/// <c>LambdaMOO 1.8.4a+NeonMOO+pronouns</c> where its report says <c>LambdaMOO 1.8.3</c>, so the
/// engine survives and some patch detail does not.
/// </para>
/// <para>
/// <c>FAMILY</c> is deliberately not accepted in <c>CODEBASE</c>'s place, even though it is coarser
/// and both protected games declare one (<c>DikuMUD</c>, <c>LPMud</c>). Admitting it would silence
/// exactly the two games this condition exists to protect.
/// </para>
/// <para>
/// <c>MuLikeness</c>'s vocabulary signal is the fourth consumer and needs nothing here: it is the
/// weakest tier by design, and a report is a protocol signal that already outranks it. A game that
/// publishes no MSSP, or an unedited one whose <c>NAME</c> is its codebase, fails the first
/// condition and is asked exactly as it is today — including
/// <c>game.convergencemush.org</c>, the game the <c>INFO</c> reader exists for.
/// </para>
/// </remarks>
public static class MsspSelfDescription
{
    /// <summary>The variable a game states its own name in.</summary>
    public const string NameVariable = "NAME";

    /// <summary>The variable a game states its engine in.</summary>
    public const string CodebaseVariable = "CODEBASE";

    /// <summary>
    /// Whether this report leaves <c>INFO</c> and <c>VERSION</c> with nothing left to ask.
    /// </summary>
    public static bool AnswersTheLoginCommands(IReadOnlyDictionary<string, IReadOnlyList<string>>? report)
    {
        if (report is null)
        {
            return false;
        }

        var codebase = MsspReport.Last(report, CodebaseVariable);

        return !MsspDefaults.IsTemplate(codebase)
            && NamesAnEngine(report)
            && MsspDefaults.MeaningfulName(MsspReport.Last(report, NameVariable), codebase) is not null
            && MsspPresence.Read(report).Found;
    }

    /// <summary>
    /// Whether any value of <c>CODEBASE</c> names an engine we recognise.
    /// </summary>
    /// <remarks>
    /// <b>Every</b> value, not the last one, because MSSP allows a variable to repeat and
    /// <c>playdecay.com:3003</c> uses that: it sends <c>CODEBASE</c> twice, as
    /// <c>FluffOS v2025</c> and then <c>Moral Decay v9.0</c>. Reading only the latest word — which is
    /// right everywhere else, and is what a publisher stores — would see a custom build string, find
    /// no engine in it, and go on typing at the login screen of the game that asked us to stop.
    /// </remarks>
    private static bool NamesAnEngine(IReadOnlyDictionary<string, IReadOnlyList<string>> report) =>
        report.TryGetValue(CodebaseVariable, out var values)
        && values.Any(LoginCommandReading.NamesAKnownFamily);
}
