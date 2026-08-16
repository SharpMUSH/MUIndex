using MUI.Catalog;

namespace MUI.Catalog.Tests;

/// <summary>
/// The ecosystem dashboard's codebase panel: two groupings of one set of games, and the two
/// different things we do not know about it.
/// </summary>
/// <remarks>
/// Every assertion here is about a <em>denominator</em>. A share whose denominator quietly excludes
/// the games we have no answer for is the one failure this panel can produce that looks like a
/// finding rather than a bug — the bars add to 100%, every one of them is too big, and nothing on
/// the page says by how much.
/// </remarks>
public class CodebaseUsageTests
{
    private static readonly string[] Catalogue =
    [
        "PennMUSH 1.8.8p0",
        "PennMUSH 1.8.7p0",
        "TinyMUX 2.12",
        "ROM 2.4",
        "Evennia",
    ];

    [Test]
    public async Task FamiliesGatherEveryPatchlevelAndCountAgainstTheGamesThatToldUs()
    {
        var usage = CodebaseUsage.Of(Catalogue, listed: 8);

        await Assert.That(usage.Families.Select(f => f.Label))
            .IsEquivalentTo(new[] { "PennMUSH", "TinyMUX", "ROM", "Evennia" });

        var penn = usage.Families.Single(f => f.Label == "PennMUSH");
        await Assert.That(penn.Count).IsEqualTo(2);
        await Assert.That(penn.Denominator).IsEqualTo(5);

        // The three games that told us nothing are outside the denominator, not a share of it.
        await Assert.That(usage.Identified).IsEqualTo(5);
        await Assert.That(usage.NotIdentified).IsEqualTo(3);
    }

    [Test]
    public async Task LineagesGatherCodebasesThatShareNoName()
    {
        var usage = CodebaseUsage.Of(Catalogue, listed: 8);

        // PennMUSH twice and TinyMUX once: three games, three separate answers to CODEBASE, and no
        // declaration anywhere that groups them.
        await Assert.That(usage.Lineages.Single(l => l.Label == CodebaseLineage.Mush).Count)
            .IsEqualTo(3);
        await Assert.That(usage.Lineages.Single(l => l.Label == CodebaseLineage.Diku).Count)
            .IsEqualTo(1);
    }

    /// <summary>
    /// A lineage share is measured against the games that told us their codebase, never against the
    /// ones we managed to place.
    /// </summary>
    /// <remarks>
    /// This is the whole test. Evennia is identified and unclassified, so the honest reading is that
    /// four of five placed games are MUSH-or-Diku and one is neither — not that the four are 100% of
    /// anything. Dividing by the classified count would take our own abstention and hand it out as
    /// extra market share to everybody we did place.
    /// </remarks>
    [Test]
    public async Task OurAbstentionInflatesNobodysShare()
    {
        var usage = CodebaseUsage.Of(Catalogue, listed: 8);

        await Assert.That(usage.Lineages.Select(l => l.Denominator).Distinct())
            .IsEquivalentTo(new[] { 5 });

        await Assert.That(usage.NotClassified).IsEqualTo(1);
        await Assert.That(usage.Lineages.Sum(l => l.Count) + usage.NotClassified)
            .IsEqualTo(usage.Identified);
    }

    [Test]
    public async Task OneGamesCapitalisationDoesNotNameAFamily()
    {
        // The same rule the facet panel labels its values with. Two spellings of one family are one
        // bar, and the bar is named by the spelling the most games used rather than by read order.
        var usage = CodebaseUsage.Of(["pennmush 1.8.8p0", "PennMUSH 1.8.7", "PennMUSH 1.8.6"], listed: 3);

        await Assert.That(usage.Families.Single().Label).IsEqualTo("PennMUSH");
        await Assert.That(usage.Families.Single().Count).IsEqualTo(3);
    }

    [Test]
    public async Task NothingMeasuredIsNotNoughtPerCent()
    {
        var usage = CodebaseUsage.Of([], listed: 4);

        await Assert.That(usage.Families).IsEmpty();
        await Assert.That(usage.Lineages).IsEmpty();
        await Assert.That(usage.NotIdentified).IsEqualTo(4);
        await Assert.That(usage.NotClassified).IsEqualTo(0);
        await Assert.That(CodebaseUsage.None.Identified).IsEqualTo(0);
    }
}
