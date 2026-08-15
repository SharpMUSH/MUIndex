using MUI.Catalog.Persistence;

namespace MUI.Catalog.Tests;

/// <summary>
/// The MSSP scorecard of spec §8.5 — what it says, and the two things it must never say.
/// </summary>
/// <remarks>
/// A linter is the one surface on this site whose whole output is <em>our opinion</em>, which makes
/// it the easiest place to break rule 5 without noticing. Half these tests are about what it declines
/// to claim.
/// </remarks>
public class MsspLintTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid Game = Guid.CreateVersion7();

    /// <summary>
    /// No report is not a bad report, and must never be rendered as twenty-seven faults.
    /// </summary>
    /// <remarks>
    /// A game we have never read MSSP from has an empty scorecard and <c>HasReport</c> false. The
    /// alternative — treating every variable as missing — publishes our own gap as somebody's
    /// neglect on the page of the one person who could tell the difference, which is rule 5 exactly.
    /// </remarks>
    [Test]
    public async Task AGameWeHoldNoMsspReportForIsNotACriticisedGame()
    {
        var card = MsspLint.Inspect([
            Field("connect_screen", "Welcome", FieldSource.Banner),
            Field(CapabilityFields.Measured("GMCP"), "true", FieldSource.Handshake),
        ]);

        await Assert.That(card.HasReport).IsFalse();
        await Assert.That(card.Findings).IsEmpty();
        await Assert.That(card.MeetsTheStandard).IsFalse();
    }

    /// <summary>A complete report earns silence, which is the only praise this site offers.</summary>
    [Test]
    public async Task AWellFilledReportHasNothingToSayAboutTheRequiredThree()
    {
        var card = MsspLint.Inspect([
            Field("NAME", "Corvid"),
            Field("PLAYERS", "14"),
            Field("UPTIME", "1753876000"),
        ]);

        await Assert.That(card.MeetsTheStandard).IsTrue();
        await Assert.That(card.RequiredFindings).IsEqualTo(0);
        await Assert.That(card.HasReport).IsTrue();
    }

    /// <summary>A required variable the report does not carry is the headline finding.</summary>
    [Test]
    public async Task AMissingRequiredVariableIsFlaggedAsRequired()
    {
        var card = MsspLint.Inspect([Field("NAME", "Corvid"), Field("PLAYERS", "14")]);

        var finding = card.Findings.Single(f => f.Field == "UPTIME");

        await Assert.That(finding.Kind).IsEqualTo(MsspFindingKind.Missing);
        await Assert.That(finding.Importance).IsEqualTo(MsspImportance.Required);
        await Assert.That(card.MeetsTheStandard).IsFalse();
    }

    /// <summary>
    /// The most useful lint there is: the name is still the codebase's.
    /// </summary>
    /// <remarks>
    /// The second real server this crawler probed publishes <c>NAME "PennMUSH"</c> because nobody
    /// edited that line. It is carried, it is well-formed, and it answers nothing — which is a
    /// different finding from missing and is worth its own kind.
    /// </remarks>
    [Test]
    public async Task ACodebaseDefaultLeftInPlaceIsUnansweredRatherThanPresent()
    {
        var card = MsspLint.Inspect(
            [Field("NAME", "PennMUSH"), Field("PLAYERS", "3"), Field("UPTIME", "9")],
            isUnanswered: value => value is "PennMUSH");

        var finding = card.Findings.Single(f => f.Field == "NAME");

        await Assert.That(finding.Kind).IsEqualTo(MsspFindingKind.Unanswered);
        await Assert.That(finding.Value).IsEqualTo("PennMUSH");
        await Assert.That(card.Answered).IsEqualTo(2);
        await Assert.That(card.Carried).IsEqualTo(3);
    }

    /// <summary>A number that is not a number.</summary>
    [Test]
    public async Task AVariableMsspSaysIsANumberIsCheckedForBeingOne()
    {
        var card = MsspLint.Inspect([
            Field("NAME", "Corvid"),
            Field("PLAYERS", "about a dozen"),
            Field("UPTIME", "1753876000"),
        ]);

        var finding = card.Findings.Single(f => f.Field == "PLAYERS");

        await Assert.That(finding.Kind).IsEqualTo(MsspFindingKind.WrongType);
        await Assert.That(finding.Detail).Contains("number");
    }

    /// <summary>A number MSSP calls a count may not be negative, or culture-shaped.</summary>
    /// <remarks>
    /// A bare <c>int.TryParse</c> accepted both: “-3” parses perfectly well and means nothing MSSP
    /// can express, and a current-culture parse makes the grouped “1,024” a pass or a fault
    /// depending on which machine the deployment happens to be running on — a scorecard that
    /// disagrees with itself across hosts is worse than one that is merely strict.
    /// </remarks>
    [Test]
    public async Task ACountIsReadTheSameEverywhereAndCannotBeNegative()
    {
        var negative = MsspLint.Inspect([Required(), Field("PLAYERS", "-3")]);
        var grouped = MsspLint.Inspect([Required(), Field("PLAYERS", "1,024")]);
        var plain = MsspLint.Inspect([Required(), Field("PLAYERS", "1024")]);

        await Assert.That(negative.Findings.Single(f => f.Field == "PLAYERS").Kind)
            .IsEqualTo(MsspFindingKind.WrongType);
        await Assert.That(grouped.Findings.Single(f => f.Field == "PLAYERS").Kind)
            .IsEqualTo(MsspFindingKind.WrongType);
        await Assert.That(plain.Findings.Any(f => f.Field == "PLAYERS")).IsFalse();
    }

    /// <summary>A year that is not a year, and a year that is.</summary>
    [Test]
    public async Task CreatedIsCheckedForBeingAYear()
    {
        var bad = MsspLint.Inspect([Required(), Field("CREATED", "March 2003")]);
        var good = MsspLint.Inspect([Required(), Field("CREATED", "2003")]);

        await Assert.That(bad.Findings.Single(f => f.Field == "CREATED").Kind)
            .IsEqualTo(MsspFindingKind.WrongType);
        await Assert.That(good.Findings.Any(f => f.Field == "CREATED")).IsFalse();
    }

    /// <summary>
    /// A value outside MSSP's list is reported by its consequence, not as an error.
    /// </summary>
    /// <remarks>
    /// MSSP's enumerations are not exhaustive in practice, and a game running a genre nobody wrote
    /// down in 2011 is not doing anything wrong. What is true and useful is the effect: our facets
    /// do not recognise it, so the game lands in the unknown bucket. The message says that.
    /// </remarks>
    [Test]
    public async Task AnUnlistedEnumeratedValueIsReportedByWhatItCostsRatherThanAsAFault()
    {
        var card = MsspLint.Inspect([Required(), Field("GENRE", "Cyberpunk Noir")]);

        var finding = card.Findings.Single(f => f.Field == "GENRE");

        await Assert.That(finding.Kind).IsEqualTo(MsspFindingKind.NonStandard);
        await Assert.That(finding.Detail).Contains("unknown bucket");
        await Assert.That(finding.Detail).Contains("Science Fiction");

        // Never the language of error. The game is allowed to say this.
        await Assert.That(finding.Detail).DoesNotContain("invalid");
        await Assert.That(finding.Detail).DoesNotContain("wrong");
    }

    /// <summary>MSSP's own listed values pass, whatever their casing.</summary>
    [Test]
    public async Task AListedValuePassesRegardlessOfCasing()
    {
        var card = MsspLint.Inspect([Required(), Field("FAMILY", "tinymush"), Field("STATUS", "Live")]);

        await Assert.That(card.Findings.Any(f => f.Field is "FAMILY" or "STATUS")).IsFalse();
    }

    /// <summary>
    /// It reads what a game declared, and never what we measured.
    /// </summary>
    /// <remarks>
    /// A scorecard is about the operator's MSSP configuration, so an owner's enrichment and a
    /// handshake observation are both none of its business. Linting a measured row would also be
    /// telling an operator to go and edit something they cannot edit.
    /// </remarks>
    [Test]
    public async Task OnlyMsspRowsAreLinted()
    {
        var card = MsspLint.Inspect([
            Field("NAME", "Corvid"),
            Field("PLAYERS", "14"),
            Field("UPTIME", "9"),

            // Same field names, other sources. None of these may produce or silence a finding.
            Field("GENRE", "Cyberpunk Noir", FieldSource.Owner),
            Field("CODEBASE", "PennMUSH 1.8.8p0", FieldSource.Handshake),
        ]);

        await Assert.That(card.Carried).IsEqualTo(3);
        await Assert.That(card.Findings.Any(f => f.Field == "GENRE" && f.Kind is MsspFindingKind.NonStandard))
            .IsFalse();
        await Assert.That(card.Findings.Single(f => f.Field == "CODEBASE").Kind)
            .IsEqualTo(MsspFindingKind.Missing);
    }

    /// <summary>
    /// Capability variables are not linted, because they have a column that answers them better.
    /// </summary>
    /// <remarks>
    /// Advising an operator to declare <c>GMCP 1</c> would be advising them to make an assertion
    /// this site exists to distrust — and the handshake already answers the question by measurement.
    /// </remarks>
    [Test]
    public async Task NoCapabilityVariableIsEverLinted()
    {
        var card = MsspLint.Inspect([Required()]);

        foreach (var capability in CapabilityFields.Names)
        {
            await Assert.That(card.Findings.Any(f => f.Field == capability)).IsFalse();
        }
    }

    /// <summary>Required findings sort above the rest, because that is the order to fix them in.</summary>
    [Test]
    public async Task FindingsComeBackWorstFirst()
    {
        var card = MsspLint.Inspect([Field("PLAYERS", "14")]);

        var importances = card.Findings.Select(f => f.Importance).ToList();

        await Assert.That(importances).IsEquivalentTo(importances.OrderBy(i => i).ToList());
        await Assert.That(importances[0]).IsEqualTo(MsspImportance.Required);
    }

    /// <summary>
    /// The scorecard is a view and stores nothing — asserted by it having nowhere to store to.
    /// </summary>
    /// <remarks>
    /// <see cref="MsspLint.Inspect"/> is a static over a list with no store, no writer and no clock
    /// in reach, so "continuous rather than one-shot" (§8.5) is a property of the type rather than a
    /// discipline somebody keeps. A stored score would be a number that goes stale against the
    /// report it describes.
    /// </remarks>
    [Test]
    public async Task TheLinterHasNothingToWriteWith()
    {
        var parameters = typeof(MsspLint)
            .GetMethod(nameof(MsspLint.Inspect))!
            .GetParameters()
            .Select(p => p.ParameterType.Name)
            .ToList();

        await Assert.That(parameters).IsEquivalentTo(new[] { "IReadOnlyList`1", "Func`2" });
    }

    private static GameField Required() => Field("NAME", "Corvid");

    /// <summary>
    /// Where an owner has answered over the report, the scorecard says so — and says who else reads
    /// which.
    /// </summary>
    /// <remarks>
    /// The failure this exists to prevent is quiet: an owner fixes their genre here, the site shows
    /// the right one, and nobody ever tells them that every other crawler on the internet still
    /// reads the wrong one out of their config. MUIndex becoming the only place a game is described
    /// correctly is a worse outcome for the hobby than the wrong description.
    /// </remarks>
    [Test]
    public async Task WhereAnOwnerHasAnsweredOverTheReportTheScorecardSaysWhichIsWhich()
    {
        var card = MsspLint.Inspect(
        [
            Field("NAME", "Corvid"),
            Field("PLAYERS", "14"),
            Field("UPTIME", "1753876000"),
            Field("GENRE", "Fantasy"),
            Field("GENRE", "Horror", FieldSource.Owner),
        ]);

        var finding = card.Findings.Single(f => f.Kind is MsspFindingKind.Overridden);

        await Assert.That(finding.Field).IsEqualTo("GENRE");
        await Assert.That(finding.Value).IsEqualTo("Fantasy");
        await Assert.That(finding.Detail).Contains("Horror");
        await Assert.That(finding.Detail).Contains("Every other crawler still reads your report");
    }

    /// <summary>
    /// A field can be both malformed and overridden, and an operator hears about both.
    /// </summary>
    /// <remarks>
    /// The defect ladder reports one finding per variable because its findings are competing
    /// descriptions of the same fault — a value cannot usefully be called both malformed and
    /// non-standard. An override does not compete with a defect: a <c>GENRE</c> outside MSSP's
    /// listed values <em>and</em> answered here gives an operator two separate things to do, one in
    /// their config and one about what this site shows. Written inside the ladder, the second was
    /// silently swallowed by the first — which is what this test caught.
    /// </remarks>
    [Test]
    public async Task AMalformedValueAndAnOverrideAreBothWorthSaying()
    {
        var card = MsspLint.Inspect(
        [
            Field("NAME", "Corvid"),
            Field("PLAYERS", "14"),
            Field("UPTIME", "1753876000"),
            Field("GENRE", "Adventure"),
            Field("GENRE", "Horror", FieldSource.Owner),
        ]);

        var genre = card.Findings.Where(f => f.Field == "GENRE").ToList();

        await Assert.That(genre.Select(f => f.Kind))
            .IsEquivalentTo(new[] { MsspFindingKind.NonStandard, MsspFindingKind.Overridden });
    }

    /// <summary>
    /// An override is not a defect in the report, and may not stop one meeting the standard.
    /// </summary>
    /// <remarks>
    /// <c>NAME</c> is one of MSSP's three required variables, so an owner answering it over a
    /// perfectly good report would otherwise flip <c>MeetsTheStandard</c> to false — the scorecard
    /// telling an operator their config is broken because of something they did on our site.
    /// </remarks>
    [Test]
    public async Task AnOverrideIsNotAFaultInTheReportItOverrides()
    {
        var card = MsspLint.Inspect(
        [
            Field("NAME", "Corvid"),
            Field("PLAYERS", "14"),
            Field("UPTIME", "1753876000"),
            Field("NAME", "Corvid Court", FieldSource.Owner),
        ]);

        await Assert.That(card.Findings.Any(f => f.Kind is MsspFindingKind.Overridden)).IsTrue();
        await Assert.That(card.RequiredFindings).IsEqualTo(0);
        await Assert.That(card.MeetsTheStandard).IsTrue();
    }

    /// <summary>
    /// An owner who typed exactly what their report says has not overridden anything.
    /// </summary>
    /// <remarks>
    /// It is the same value, so there is nothing to tell them and nothing to fix in their config.
    /// A finding here would be the scorecard inventing a disagreement to report.
    /// </remarks>
    [Test]
    public async Task AnOverrideThatAgreesWithTheReportIsNotADisagreement()
    {
        var card = MsspLint.Inspect(
        [
            Field("NAME", "Corvid"),
            Field("PLAYERS", "14"),
            Field("UPTIME", "1753876000"),
            Field("NAME", "Corvid", FieldSource.Owner),
        ]);

        await Assert.That(card.Findings.Any(f => f.Kind is MsspFindingKind.Overridden)).IsFalse();
    }

    /// <summary>
    /// A withdrawn override is not an override, because withdrawing writes an empty value.
    /// </summary>
    /// <remarks>
    /// Nothing is ever deleted, so the row survives the withdrawal and the linter has to read the
    /// value rather than the row's existence. Otherwise a field somebody once answered and then gave
    /// up would go on being reported as overridden for ever.
    /// </remarks>
    [Test]
    public async Task AWithdrawnOverrideIsNotReportedAsOne()
    {
        var card = MsspLint.Inspect(
        [
            Field("NAME", "Corvid"),
            Field("PLAYERS", "14"),
            Field("UPTIME", "1753876000"),
            Field("NAME", string.Empty, FieldSource.Owner),
        ]);

        await Assert.That(card.Findings.Any(f => f.Kind is MsspFindingKind.Overridden)).IsFalse();
    }

    private static GameField Field(string field, string value, FieldSource source = FieldSource.Mssp) =>
        new(Game, field, source, value, Now.AddYears(-1), Now);
}
