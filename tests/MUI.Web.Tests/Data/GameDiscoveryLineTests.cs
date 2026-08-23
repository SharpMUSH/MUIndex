using MUI.Catalog;
using MUI.Web.Components;

namespace MUI.Web.Tests.Data;

/// <summary>
/// The line on a game page that says how this site came to know about the game.
/// </summary>
/// <remarks>
/// The whole design of this sentence is in its shape: a dated statement about our own crawl, never a
/// badge on the game. §7.6 rejected an origin field because any game worth listing appears in
/// several directories at once, so "first seen via X" has to be readable as "which channel reached
/// us first", and the date is what makes that unmistakable.
/// </remarks>
public class GameDiscoveryLineTests
{
    private const string English = "en";

    /// <summary>
    /// The channel and the date arrive together. A source name on its own would read as a claim
    /// about where the game came from, which is not a thing this site knows.
    /// </summary>
    [Test]
    public async Task TheLineNamesTheChannelAndTheDate()
    {
        var line = DiscoveryLine.FirstSeen(English, DiscoverySource.AresCentral, "22 August 2026");

        await Assert.That(line).Contains("AresCentral");
        await Assert.That(line).Contains("22 August 2026");
    }

    /// <summary>
    /// The backfill read several directories and recorded which one supplied a given address
    /// nowhere at all. Naming one here would be the accident-as-fact §7.6 exists to prevent.
    /// </summary>
    [Test]
    public async Task TheBackfillNamesNoDirectory()
    {
        var line = DiscoveryLine.FirstSeen(English, DiscoverySource.Backfill, "30 July 2026");

        await Assert.That(line).DoesNotContain("MudStats");
        await Assert.That(line).DoesNotContain("Mud Connector");
        await Assert.That(line).DoesNotContain("MudVerse");
        await Assert.That(line).Contains("30 July 2026");
    }

    /// <summary>
    /// Every source has its own sentence, and none of them reaches a reader as a C# enum member.
    /// </summary>
    [Test]
    public async Task EverySourceHasItsOwnSentenceAndNoneLeaksItsEnumMember()
    {
        foreach (var source in Enum.GetValues<DiscoverySource>())
        {
            var line = DiscoveryLine.FirstSeen(English, source, "22 August 2026");

            await Assert.That(string.IsNullOrWhiteSpace(line))
                .IsFalse()
                .Because($"{source} has no sentence");

            // AresCentral is a proper noun whose C# spelling is already the reader's, exactly as
            // FieldSource.AresCentral is in TimeSurfaceTests. Every other member's ToString on a
            // page would be a defect, and that is what this catches.
            if (source is not DiscoverySource.AresCentral)
            {
                await Assert.That(line)
                    .DoesNotContain(source.ToString())
                    .Because($"{source} is reaching the page as its own C# spelling");
            }

            await Assert.That(line)
                .IsNotEqualTo(source.ToString())
                .Because($"{source}'s sentence is just its own name");

            await Assert.That(line).Contains("22 August 2026");
        }
    }

    /// <summary>
    /// The six sentences are six different sentences. A shared template with a noun slotted in would
    /// pass every other test here and read as machine-generated in every language.
    /// </summary>
    [Test]
    public async Task TheSourcesDoNotShareOneSentence()
    {
        var lines = Enum.GetValues<DiscoverySource>()
            .Select(s => DiscoveryLine.FirstSeen(English, s, "22 August 2026"))
            .ToList();

        await Assert.That(lines.Distinct().Count()).IsEqualTo(lines.Count);
    }
}
