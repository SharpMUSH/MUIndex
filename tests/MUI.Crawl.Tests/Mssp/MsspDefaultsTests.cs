using MUI.Crawl;

namespace MUI.Crawl.Tests;

/// <summary>
/// Default MSSP values are the absence of a signal, not a weak one.
/// </summary>
/// <remarks>
/// These cases come from real servers, not imagination. The second game this crawler probed —
/// eldertaleonline.com:7705 — publishes <c>NAME "PennMUSH"</c>, and every unedited PennMUSH on the
/// internet publishes the same thing.
/// </remarks>
public class MsspDefaultsTests
{
    [Test]
    public async Task ACodebaseNamePublishedAsTheGameNameIsNotAName()
    {
        // Observed on eldertaleonline.com:7705.
        await Assert.That(MsspDefaults.MeaningfulName("PennMUSH", "PennMUSH 1.8.8p0")).IsNull();
    }

    [Test]
    [Arguments("PennMUSH")]
    [Arguments("Evennia")]
    [Arguments("TinyMUX")]
    [Arguments("RhostMUSH")]

    // AresMUSH and CobraMUSH ship the same unedited NAME line every other MUSH codebase here does.
    [Arguments("AresMUSH")]
    [Arguments("aresmush")]
    [Arguments("CobraMUSH")]
    [Arguments("Unknown")]
    [Arguments("Change Me")]
    [Arguments("Your MUD Name")]
    [Arguments("   ")]
    [Arguments(null)]
    public async Task APlaceholderIsRecognisedWhateverItsShape(string? value)
    {
        await Assert.That(MsspDefaults.IsPlaceholder(value)).IsTrue();
    }

    [Test]
    [Arguments("M*U*S*H")]
    [Arguments("Tidewater Nights")]
    [Arguments("Eldertale")]

    // A codebase name that is also a real game's name stays off the placeholder list — erasing a
    // real name costs more than keeping a default.
    [Arguments("Last Outpost")]
    [Arguments("LuminariMUD")]
    [Arguments("GodWars")]
    public async Task ARealNameSurvives(string value)
    {
        await Assert.That(MsspDefaults.IsPlaceholder(value)).IsFalse();
        await Assert.That(MsspDefaults.MeaningfulName(value, "PennMUSH 1.8.8p0")).IsEqualTo(value);
    }

    [Test]
    public async Task ANameThatMerelyRestatesTheCodebaseWithItsVersionIsAlsoNotAName()
    {
        await Assert.That(MsspDefaults.MeaningfulName("PennMUSH 1.8.8p0", "PennMUSH 1.8.8p0")).IsNull();
    }

    [Test]
    public async Task TwoUneditedServersMustNotLookLikeTheSameGame()
    {
        // Spec §7.3 weights MSSP NAME heavily when deciding whether two endpoints are one game; if a
        // default counted as a signal, every unedited PennMUSH would auto-merge into one game.
        var first = MsspDefaults.MeaningfulName("PennMUSH", "PennMUSH 1.8.8p0");
        var second = MsspDefaults.MeaningfulName("PennMUSH", "PennMUSH 1.8.7");

        await Assert.That(first).IsNull();
        await Assert.That(second).IsNull();

        // Both null: the matcher has nothing to compare, which is correct. It must not conclude
        // "equal" from two absences.
        await Assert.That(first is not null && first == second).IsFalse();
    }

    [Test]
    public async Task TheStringZeroIsAPlaceholderButAMeasuredZeroCountIsNot()
    {
        // The opposite error: a measured `PLAYERS 0` is a real fact (rule 2), not a non-answer. Only
        // a name-shaped field reading "0" is a placeholder.
        await Assert.That(MsspDefaults.IsPlaceholder("0")).IsTrue();
        await Assert.That(MsspDefaults.MeaningfulName("0", "PennMUSH")).IsNull();
    }

    [Test]
    public async Task AUrlWithNoHostIsAPlaceholder()
    {
        // What an unconfigured CoffeeMud publishes for WEBSITE. It is a non-answer wearing a URL's
        // clothes, and it reached §7.3 as a matched WebsiteOrContact between every game that had not
        // filled it in -- four such pairs were open in production on 2026-08-21, all of them different
        // games on different hosts agreeing only on having left the field alone.
        await Assert.That(MsspDefaults.IsPlaceholder("http:///")).IsTrue();
        await Assert.That(MsspDefaults.IsPlaceholder("https:///")).IsTrue();
        await Assert.That(MsspDefaults.IsPlaceholder("http://")).IsTrue();
    }

    [Test]
    public async Task ARealUrlIsNotAPlaceholder()
    {
        // The other half, which matters more: this list erases an answer, so it must not reach a URL
        // that names a host.
        await Assert.That(MsspDefaults.IsPlaceholder("http://corvid.example.org")).IsFalse();
        await Assert.That(MsspDefaults.IsPlaceholder("https://coffeemud.net:27744/")).IsFalse();
    }
}
