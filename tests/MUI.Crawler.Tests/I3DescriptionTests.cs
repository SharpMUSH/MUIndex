using System.Text.Json;

using MUI.Catalog;
using MUI.Crawler;
using MUI.I3;

namespace MUI.Crawler.Tests;

/// <summary>
/// Mapping an Intermud-3 mudlist entry's three self-description fields onto <c>game_field</c>.
/// </summary>
/// <remarks>
/// Every fixture is a verbatim entry from the live network, taken from the gateway on 2026-08-17.
/// </remarks>
public class I3DescriptionTests
{
    private static I3Mud Mud(string driver = "", string mudlib = "", string type = "") =>
        new()
        {
            Name = "Somewhere",
            Host = "203.0.113.10",
            Port = 4242,
            Services = new Dictionary<string, JsonElement>(),
            Driver = driver,
            Mudlib = mudlib,
            MudType = type,
        };

    private static string? Value(IReadOnlyList<FieldObservation> observed, string field) =>
        observed.SingleOrDefault(o => o.Field == field)?.Value;

    [Test]
    public async Task TheDriverIsTheCodebaseAndTheMudlibIsNot()
    {
        // The Zone, live. DGD is the engine and WOTFlib is the library on top of it — two different
        // facts, and folding the second into CODEBASE would publish a library as a codebase.
        var observed = I3Description.From(Mud("DGD 1.4.1", "WOTFlib 0.90", "LPMud"));

        await Assert.That(Value(observed, FieldObservations.CodebaseField)).IsEqualTo("DGD 1.4.1");
        await Assert.That(Value(observed, I3Description.MudlibField)).IsEqualTo("WOTFlib 0.90");
        await Assert.That(Value(observed, I3Description.FamilyField)).IsEqualTo("LPMud");
    }

    [Test]
    public async Task AMudlibThatOnlyRepeatsTheDriverIsNotASecondFact()
    {
        // Every CoffeeMud on the network writes its own version into both fields. A MUDLIB row
        // repeating CODEBASE verbatim is one fact stored twice for a reader to reconcile.
        var observed = I3Description.From(
            Mud("CoffeeMud v5.11.0.3", "CoffeeMud v5.11.0.3", "CoffeeMud"));

        await Assert.That(Value(observed, FieldObservations.CodebaseField))
            .IsEqualTo("CoffeeMud v5.11.0.3");
        await Assert.That(Value(observed, I3Description.MudlibField)).IsNull();
        await Assert.That(Value(observed, I3Description.FamilyField)).IsEqualTo("CoffeeMud");
    }

    [Test]
    public async Task ADistinctMudlibIsKeptEvenWhereTheFamilyRepeatsTheDriver()
    {
        // CyberASSAULT, live: the driver and the family agree and the mudlib is the specific fork.
        var observed = I3Description.From(Mud("CircleMUD", "LuminariMUD", "CircleMUD"));

        await Assert.That(Value(observed, FieldObservations.CodebaseField)).IsEqualTo("CircleMUD");
        await Assert.That(Value(observed, I3Description.MudlibField)).IsEqualTo("LuminariMUD");
    }

    [Test]
    public async Task EverythingIsRecordedAsTheNetworksClaimAndNotAsAMeasurement()
    {
        var observed = I3Description.From(Mud("FluffOS v2.23-ds03", "Dead Souls 3.9", "LPMud"));

        await Assert.That(observed.Select(o => o.Source).Distinct().Single())
            .IsEqualTo(FieldSource.I3Mudlist);
    }

    /// <summary>
    /// The placeholder test, and the one that must <em>not</em> be <c>IsPlaceholder</c>.
    /// </summary>
    /// <remarks>
    /// <c>MsspDefaults.IsPlaceholder</c> refuses <c>FluffOS</c> and <c>CircleMUD</c>, because a game
    /// <em>named</em> after its codebase never edited its config. Here the codebase's name is the
    /// answer, so the narrower <c>IsTemplate</c> is what runs — template text and nothing else.
    /// </remarks>
    [Test]
    public async Task ACodebaseNameIsAnAnswerHereEvenThoughItIsNotAName()
    {
        var observed = I3Description.From(Mud("FluffOS", type: "LPMud"));

        await Assert.That(Value(observed, FieldObservations.CodebaseField)).IsEqualTo("FluffOS");
    }

    [Test]
    [Arguments("Unknown")]
    [Arguments("none")]
    [Arguments("N/A")]
    [Arguments("  ")]
    [Arguments("")]
    public async Task TemplateTextIsNotAValue(string driver)
    {
        // Nothing polices what a mud registers: the live network carries "Your MUD Name" and "test"
        // in the name field, so the others get the same treatment.
        var observed = I3Description.From(Mud(driver, type: "LPMud"));

        await Assert.That(Value(observed, FieldObservations.CodebaseField)).IsNull();
        await Assert.That(Value(observed, I3Description.FamilyField)).IsEqualTo("LPMud");
    }

    [Test]
    public async Task AnEntryThatFilledNothingInSaysNothing()
    {
        await Assert.That(I3Description.From(Mud())).IsEmpty();
    }
}
