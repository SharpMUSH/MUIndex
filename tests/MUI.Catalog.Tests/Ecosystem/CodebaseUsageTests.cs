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
    /// Evennia is identified but unclassified, so four of five placed games are MUSH-or-Diku and one
    /// is neither — not that the four are 100% of anything. Dividing by the classified count would
    /// hand our own abstention out as extra share to everybody we did place.
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

    /// <summary>A codebase exactly one game runs is separated from the shares that are shares.</summary>
    /// <remarks>
    /// The dashboard's question is which codebases the hobby shares; a population of one can't
    /// answer it. The split is on the count, never on the name — matching codebase to game name would
    /// wrongly blank real families sharing a name with their flagship game, and rejecting the
    /// measurement outright would count a game that told us what it runs among the ones that told us
    /// nothing.
    /// </remarks>
    [Test]
    public async Task ACodebaseOnlyOneGameRunsIsNotAShare()
    {
        var usage = CodebaseUsage.Of(Catalogue, listed: 8);

        await Assert.That(usage.Shared.Select(f => f.Label)).IsEquivalentTo(new[] { "PennMUSH" });
        await Assert.That(usage.SoleUse.Select(f => f.Label))
            .IsEquivalentTo(new[] { "Evennia", "ROM", "TinyMUX" });

        // Folded, never dropped. Every one of them is still on Families, still countable, and the
        // page lists them — a share of one is not a share and it is not a secret either.
        await Assert.That(usage.Families.Count)
            .IsEqualTo(usage.Shared.Count + usage.SoleUse.Count);
    }

    [Test]
    public async Task TheFoldedGamesStayInsideTheDenominator()
    {
        // The bars plus the folded line must equal every game that told us, exactly — a fold that
        // quietly shrank the denominator would inflate every share above it.
        var usage = CodebaseUsage.Of(Catalogue, listed: 8);

        await Assert.That(usage.SoleUseTotal.Denominator).IsEqualTo(usage.Identified);
        await Assert.That(usage.Shared.Sum(f => f.Count) + usage.SoleUseTotal.Count)
            .IsEqualTo(usage.Identified);
        await Assert.That(usage.Shared.Select(f => f.Denominator).Distinct())
            .IsEquivalentTo(new[] { usage.Identified });
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
