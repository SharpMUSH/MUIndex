using System.Globalization;

namespace MUI.Catalog;

/// <summary>What is wrong with one MSSP variable, in the operator's terms.</summary>
public enum MsspFindingKind
{
    /// <summary>MSSP defines the variable and the report does not carry it.</summary>
    Missing,

    /// <summary>Carried, but still holding a codebase default or template text.</summary>
    Unanswered,

    /// <summary>Carried, but not the shape MSSP says the variable holds.</summary>
    WrongType,

    /// <summary>Carried and readable, but not one of the values MSSP lists for it.</summary>
    NonStandard,

    /// <summary>
    /// Carried, well-formed, and answered differently by an owner here (spec §8.5).
    /// </summary>
    /// <remarks>
    /// <b>Not a defect, and counted as none.</b> The other four say something is wrong with the
    /// report; this one says the report is fine and we are showing something else, which an operator
    /// is entitled to know because <em>every other crawler still reads the report</em>. Saying it
    /// once is what keeps this site from quietly becoming the only place a game's genre is right —
    /// a worse outcome for the hobby than the wrong genre.
    /// </remarks>
    Overridden,
}

/// <summary>How much an operator should care.</summary>
/// <remarks>
/// Three levels rather than a number, because a number invites a total and a total invites a
/// league table — and a public ranking of how well games fill in a config file is a rating
/// affordance with extra steps (§2). This is advice to one operator about their own server.
/// </remarks>
public enum MsspImportance
{
    /// <summary>MSSP requires it. Every crawler on the internet reads these three.</summary>
    Required,

    /// <summary>What a directory shows a reader. Absent, we can only publish what we measured.</summary>
    Recommended,

    /// <summary>Worth having, and nothing breaks without it.</summary>
    Optional,
}

/// <summary>
/// One remark about one variable. Ours, and phrased as ours.
/// </summary>
/// <remarks>
/// <see cref="Detail"/> says what we read and what MSSP expects; it never says the game is wrong,
/// because a lint is our reading of a protocol document and not a measurement of anybody. The
/// distinction is rule 5 in the one place it would be easiest to drop: a scorecard is an opinion,
/// and an opinion published as a finding about a game is exactly the thing this site refuses to do.
/// </remarks>
public sealed record MsspFinding(
    string Field,
    MsspFindingKind Kind,
    MsspImportance Importance,
    string? Value,
    string Detail);

/// <summary>
/// The MSSP linter of spec §8.5 — continuous, and a view rather than a verdict.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing here is stored.</b> §8.5 asks for a scorecard that is continuous rather than one-shot,
/// and the way to get that is to derive it on read from the MSSP rows the crawler already writes —
/// so it is never a score that has gone stale against the report it describes, and there is no
/// button, no queue and no job. A game that fixes its <c>mush.cnf</c> is clean on the next probe,
/// with nothing to press.
/// </para>
/// <para>
/// <b>It reads measurements and writes nothing about them.</b> No <c>GameField</c>, no
/// <c>FieldChange</c>, no column on <c>game</c>. A lint result is a decision of ours, and rule 5
/// forbids recording one as a fact about somebody else's game.
/// </para>
/// <para>
/// <b>Silence is never read as a fault.</b> If we hold no MSSP rows at all, the answer is
/// <see cref="MsspScorecard.HasReport"/> false and an empty finding list — never twenty-seven
/// "missing" lines. We did not measure an absence of fields; we have no report, which is a
/// statement about us and is worded as one wherever it is rendered. Getting that backwards would
/// publish our own gap as somebody's neglect, on the page of the one person who could tell the
/// difference.
/// </para>
/// </remarks>
public static class MsspLint
{
    /// <summary>The three MSSP requires of every server.</summary>
    public static IReadOnlyList<string> Required { get; } = ["NAME", "PLAYERS", "UPTIME"];

    /// <summary>
    /// What a directory needs to describe a game as anything other than an address.
    /// </summary>
    /// <remarks>
    /// Chosen by what this site actually renders: the listing's facets and the game page's own
    /// description. A variable nothing here reads is optional however much MSSP likes it.
    /// </remarks>
    public static IReadOnlyList<string> Recommended { get; } =
        ["CODEBASE", "DESCRIPTION", "GENRE", "FAMILY", "LANGUAGE", "WEBSITE", "CONTACT", "STATUS"];

    /// <summary>Variables holding a whole number.</summary>
    private static readonly HashSet<string> Integers =
        new(StringComparer.Ordinal) { "PLAYERS", "UPTIME", "PORT", "MINIMUM AGE" };

    /// <summary>
    /// Values MSSP lists for the variables that enumerate them.
    /// </summary>
    /// <remarks>
    /// Advisory, and worded that way. MSSP's lists are not exhaustive in practice — games run
    /// genres nobody wrote down in 2011 — so a value outside them is reported as unrecognised by
    /// the facets rather than as an error. The consequence is real and concrete and belongs in the
    /// message: a <c>GENRE</c> we do not recognise lands in the listing's unknown bucket.
    /// </remarks>
    private static readonly Dictionary<string, string[]> Enumerated = new(StringComparer.Ordinal)
    {
        ["FAMILY"] =
        [
            "AberMUD", "CoffeeMUD", "Custom", "DikuMUD", "Evennia", "LPMud", "MajorMUD", "MOO",
            "Mordor", "Nakedmud", "SocketMud", "TinyMUCK", "TinyMUD", "TinyMUSH",
        ],
        ["STATUS"] = ["Alpha", "Closed Beta", "Open Beta", "Live"],
        ["GENRE"] =
        [
            "Adult", "Fantasy", "Historical", "Horror", "Modern", "None", "Science Fiction",
        ],
        ["GAMEPLAY"] =
        [
            "Adventure", "Educational", "Hack and Slash", "None", "Player versus Player",
            "Player versus Environment", "Roleplaying", "Simulation", "Social", "Strategy",
        ],
    };

    /// <summary>
    /// Lints the MSSP report we hold for a game.
    /// </summary>
    /// <param name="fields">Every stored field for one game, of every source.</param>
    /// <param name="isUnanswered">
    /// Whether a value is a codebase default or template text. Injected rather than reimplemented:
    /// <c>MsspDefaults.IsPlaceholder</c> already knows that <c>NAME "PennMUSH"</c> means nobody
    /// filled it in, and it lives in <c>MUI.Crawl</c>, which <c>MUI.Catalog</c> may never reference.
    /// A second copy of that list here would be a second spelling of the same judgement, and the two
    /// would drift the first time somebody added a placeholder to one of them.
    /// </param>
    public static MsspScorecard Inspect(
        IReadOnlyList<GameField> fields,
        Func<string?, bool>? isUnanswered = null)
    {
        ArgumentNullException.ThrowIfNull(fields);

        var unanswered = isUnanswered ?? (value => string.IsNullOrWhiteSpace(value));

        var declared = fields
            .Where(field => field.Source is FieldSource.Mssp)
            .ToDictionary(field => field.Field, StringComparer.Ordinal);

        // What an owner has answered over the report (§8.5). Read here so the scorecard can say
        // where the two disagree — and kept strictly apart from `declared`, because this scores the
        // REPORT. An override is not a variable their mush.cnf carries, and folding it in would tell
        // an operator their config was fine when it is the thing they came here to check.
        var overridden = fields
            .Where(field => field.Source is FieldSource.Owner)
            .ToDictionary(field => field.Field, StringComparer.Ordinal);

        // No report, no findings. We did not measure an absence of variables — we have not read an
        // MSSP report, which is a fact about our crawl and not about their server.
        if (declared.Count == 0)
        {
            return MsspScorecard.NoReport;
        }

        var findings = new List<MsspFinding>();

        foreach (var (field, importance) in Vocabulary())
        {
            if (!declared.TryGetValue(field, out var row))
            {
                findings.Add(new MsspFinding(
                    field,
                    MsspFindingKind.Missing,
                    importance,
                    null,
                    importance is MsspImportance.Required
                        ? "MSSP requires this and your report does not carry it."
                        : "Not in your report. We can only show what we measured instead."));
                continue;
            }

            if (unanswered(row.Value))
            {
                findings.Add(new MsspFinding(
                    field,
                    MsspFindingKind.Unanswered,
                    importance,
                    row.Value,
                    $"Reads “{row.Value}”, which is a codebase default rather than an "
                    + "answer. We treat it as unset."));
                continue;
            }

            // Invariant, and non-negative. A wire value is not written in the host's culture, so a
            // current-culture parse would let a grouped “1,024” through on one deployment and flag
            // it on another — and none of the counts MSSP calls a number has a meaning below zero,
            // so a bare TryParse was reading “-3” as a well-formed answer.
            if (Integers.Contains(field)
                && !(int.TryParse(row.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                     && number >= 0))
            {
                findings.Add(new MsspFinding(
                    field,
                    MsspFindingKind.WrongType,
                    importance,
                    row.Value,
                    $"MSSP says this is a number and it reads “{row.Value}”."));
                continue;
            }

            if (string.Equals(field, "CREATED", StringComparison.Ordinal)
                && !(int.TryParse(row.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var year)
                     && year is >= 1975 and <= 2100))
            {
                findings.Add(new MsspFinding(
                    field,
                    MsspFindingKind.WrongType,
                    importance,
                    row.Value,
                    $"MSSP says this is a four-digit year and it reads “{row.Value}”."));
                continue;
            }

            if (Enumerated.TryGetValue(field, out var allowed)
                && !allowed.Contains(row.Value, StringComparer.OrdinalIgnoreCase))
            {
                findings.Add(new MsspFinding(
                    field,
                    MsspFindingKind.NonStandard,
                    importance,
                    row.Value,
                    $"“{row.Value}” is not one of MSSP's listed values, so our facets do "
                    + $"not recognise it and the game lands in the unknown bucket. MSSP lists: "
                    + $"{string.Join(", ", allowed)}."));
            }
        }

        // A second pass, and separate from the ladder above on purpose.
        //
        // That loop reports at most one finding per variable, most important first, because its
        // findings are competing descriptions of the same defect — a value cannot usefully be called
        // both malformed and non-standard. An override is not a defect and does not compete with
        // one: a GENRE that is both outside MSSP's listed values *and* answered here is a field an
        // operator has two separate things to know about. Folded into the ladder the second one was
        // silently swallowed by the first, which is how this was found.
        foreach (var (field, importance) in Vocabulary())
        {
            if (!declared.TryGetValue(field, out var reported)
                || !overridden.TryGetValue(field, out var owner))
            {
                continue;
            }

            // An empty owner row is a withdrawal, not an answer — nothing is deleted, so the row
            // outlives the override and must stop counting when the value goes. And an owner who
            // typed exactly what their report says has not disagreed with anything.
            if (owner.Value.Length == 0
                || string.Equals(owner.Value, reported.Value, StringComparison.Ordinal))
            {
                continue;
            }

            findings.Add(new MsspFinding(
                field,
                MsspFindingKind.Overridden,
                importance,
                reported.Value,
                $"Your report says “{reported.Value}” and you have told us “{owner.Value}” here, so "
                + "that is what this site shows. Every other crawler still reads your report — "
                + "putting it in your config fixes it everywhere."));
        }

        return new MsspScorecard(
            HasReport: true,
            Answered: declared.Count(row => !unanswered(row.Value.Value)),
            Carried: declared.Count,
            [.. findings.OrderBy(f => f.Importance).ThenBy(f => f.Field, StringComparer.Ordinal)]);
    }

    /// <summary>
    /// Every variable worth remarking on, with how much it matters.
    /// </summary>
    /// <remarks>
    /// Drawn from <c>FieldRegistry</c>'s own MSSP names where it can be, so the linter and the
    /// catalogue cannot disagree about what an MSSP variable is. Capability variables are excluded:
    /// they have a surface of their own where measured sits beside declared, and telling an operator
    /// to declare <c>GMCP 1</c> when the handshake already answers the question would be advice to
    /// make an assertion this site is built to distrust.
    /// </remarks>
    private static IEnumerable<(string Field, MsspImportance Importance)> Vocabulary()
    {
        foreach (var field in Required)
        {
            yield return (field, MsspImportance.Required);
        }

        foreach (var field in Recommended)
        {
            yield return (field, MsspImportance.Recommended);
        }

        foreach (var field in Optional)
        {
            yield return (field, MsspImportance.Optional);
        }
    }

    private static IReadOnlyList<string> Optional { get; } =
        [
            "CREATED", "DISCORD", "GAMEPLAY", "GAMESYSTEM", "ICON", "LOCATION", "MINIMUM AGE",
            "PORT", "SUBGENRE",
        ];
}

/// <summary>
/// What we make of a game's MSSP report — or the fact that we hold none.
/// </summary>
/// <remarks>
/// <see cref="HasReport"/> is the first thing every surface must branch on. A scorecard with no
/// report is not a clean scorecard and not a failing one; it is a page that has to say we have not
/// read one, because the alternative renders our own silence as somebody's neglect.
/// </remarks>
public sealed record MsspScorecard(
    bool HasReport,
    int Answered,
    int Carried,
    IReadOnlyList<MsspFinding> Findings)
{
    public static readonly MsspScorecard NoReport = new(false, 0, 0, []);

    /// <summary>
    /// Findings against the three variables MSSP requires — defects only.
    /// </summary>
    /// <remarks>
    /// <see cref="MsspFindingKind.Overridden"/> is excluded, and the exclusion is the point rather
    /// than a tidiness: an owner answering <c>NAME</c> here would otherwise make a perfectly
    /// well-formed report stop meeting the standard, which is this scorecard telling an operator
    /// their config is broken because of something they did on our site.
    /// </remarks>
    public int RequiredFindings => Findings.Count(f =>
        f.Importance is MsspImportance.Required && f.Kind is not MsspFindingKind.Overridden);

    /// <summary>Whether the report carries all three required variables, answered and well-formed.</summary>
    public bool MeetsTheStandard => HasReport && RequiredFindings == 0;
}
