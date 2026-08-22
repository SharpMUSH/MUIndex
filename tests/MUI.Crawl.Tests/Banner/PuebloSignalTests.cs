using MUI.Crawl;

namespace MUI.Crawl.Tests;

/// <summary>
/// Reading Pueblo off what a server sends, and taking its markup back out of the screen.
/// </summary>
/// <remarks>
/// The reason this exists rather than a wider <see cref="MxpSignal"/>: Pueblo sends actual HTML, and
/// MXP's tag list deliberately omits the tags that "collide with Pueblo, with HTML a game might
/// quote, and with prose". Everything here turns on the gate — nothing is stripped from a screen that
/// has not first proven itself Pueblo.
/// </remarks>
public class PuebloSignalTests
{
    [Test]
    [Arguments("<a xch_cmd=\"WHO\">WHO</a>")]
    [Arguments("<img xch_mode=purehtml>")]
    [Arguments("<!EL RName FLAG=\"RoomName\" OPEN>")]
    [Arguments("<!ELEMENT samp \"<tt>\">")]
    public async Task TheMarkersThatSayThisIsPueblo(string text)
    {
        await Assert.That(PuebloSignal.IsPresent(text)).IsTrue();
    }

    /// <summary>
    /// A connect screen is full of angle brackets that are not markup, and none of them is a marker.
    /// </summary>
    [Test]
    [Arguments("<<<<< WELCOME TO THE KEEP >>>>>")]
    [Arguments("Type <name> to begin, or <quit> to leave.")]
    [Arguments("  /\\  --> the north road")]
    [Arguments("<b>bold</b> is not a Pueblo marker on its own")]
    public async Task OrdinaryAngleBracketsAreNotPueblo(string text)
    {
        await Assert.That(PuebloSignal.IsPresent(text)).IsFalse();
    }

    /// <summary>
    /// The gate, stated as a test: a screen with no marker comes back byte-for-byte, so the artwork on
    /// the 898 screens that are not Pueblo cannot be eaten to tidy the four that are.
    /// </summary>
    [Test]
    [Arguments("<<<<< WELCOME >>>>>\n<b>not stripped</b>\nType <name>:")]
    [Arguments("  |  <-- a door\n  |  --> a corridor\n")]
    public async Task AScreenWithNoMarkerIsUntouched(string text)
    {
        await Assert.That(PuebloSignal.Strip(text)).IsEqualTo(text);
    }

    [Test]
    public async Task PuebloMarkupIsRemovedOnceTheScreenHasProvenItself()
    {
        const string screen = "<a xch_cmd=\"WHO\" xch_hint=\"See who is online\">WHO</a> to look around.";

        var stripped = PuebloSignal.Strip(screen);

        await Assert.That(stripped).DoesNotContain("xch_");
        await Assert.That(stripped).DoesNotContain("<a ");
        // The attribute text is the whole point: it read as visible prose before this existed.
        await Assert.That(stripped).DoesNotContain("See who is online");
        // What a player would actually have seen survives.
        await Assert.That(stripped).Contains("WHO");
        await Assert.That(stripped).Contains("to look around.");
    }

    /// <summary>
    /// A <c>&lt;br&gt;</c> is a line ending, and dropping it silently welds the lines either side into
    /// one — which is what let a version number's digit be read as a menu token whose label then ran on
    /// into a different line's words. Measured on <c>elendor</c>.
    /// </summary>
    [Test]
    public async Task ABreakBecomesALineRatherThanNothing()
    {
        const string screen = "<!EL x OPEN>Running: PennMUSH 1.7.1 pl3<br>Pueblo enhanced mode!<br>Use \"WHO\" to see who is online.";

        var lines = PuebloSignal.Strip(screen)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        await Assert.That(lines.Length).IsEqualTo(3);
        await Assert.That(lines[0]).IsEqualTo("Running: PennMUSH 1.7.1 pl3");
        await Assert.That(lines[1]).IsEqualTo("Pueblo enhanced mode!");
        await Assert.That(lines[2]).IsEqualTo("Use \"WHO\" to see who is online.");
    }

    [Test]
    public async Task EntitiesAreDecodedToTheCharactersTheyStandFor()
    {
        var stripped = PuebloSignal.Strip("<!EL x OPEN>Use &quot;connect &lt;name&gt;&quot; to play.");

        await Assert.That(stripped).IsEqualTo("Use \"connect <name>\" to play.");
    }

    /// <summary>
    /// Stripping twice is stripping once — the pass is applied wherever lines become a screen, so it
    /// must not matter if something upstream already ran it.
    /// </summary>
    [Test]
    public async Task StrippingIsIdempotent()
    {
        const string screen = "<!EL x OPEN><a xch_cmd=\"WHO\">WHO</a><br>Welcome.";

        var once = PuebloSignal.Strip(screen);

        await Assert.That(PuebloSignal.Strip(once)).IsEqualTo(once);
    }

    /// <summary>
    /// Pueblo and MXP are different protocols, and this one must not answer for the other: a screen
    /// stripped of Pueblo still has to read as MXP to <see cref="MxpSignal"/> if it carried MXP too.
    /// </summary>
    [Test]
    public async Task StrippingPuebloDoesNotHideMxp()
    {
        const string screen = "<!EL x OPEN>\e[1z<VERSION>Welcome.";

        await Assert.That(MxpSignal.IsPresent(PuebloSignal.Strip(screen))).IsTrue();
    }
}
