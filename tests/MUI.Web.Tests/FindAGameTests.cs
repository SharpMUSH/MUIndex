using System.Text.RegularExpressions;

using MUI.Catalog;
using MUI.Web.Components.Pages;

namespace MUI.Web.Tests;

/// <summary>
/// §9's find-a-game wizard, and the one property that makes it safe to have.
/// </summary>
/// <remarks>
/// The wizard's whole design is that it does not translate. It is a GET form aimed at the listing,
/// so the querystring it produces is parsed by the listing's own binding — which means the failure
/// mode to guard is a control whose name or value the binding would refuse. A wizard offering a
/// choice that comes back as a 400, or worse as a silently ignored filter, teaches a reader that
/// the site does not work.
/// </remarks>
public class FindAGameTests
{
    [Test]
    public async Task TheWizardAsksTheListingForItsAnswer()
    {
        var html = await Render.PageAsync<FindAGame>([]);

        // A GET at the listing, so the answer is a bookmarkable listing URL and not a page of its
        // own that would have to re-render the rows.
        await Assert.That(html).Contains("method=\"get\"");
        await Assert.That(html).Contains("action=\"/games\"");
    }

    /// <summary>
    /// Every control is named for a facet the binding reads, and no control invents one.
    /// </summary>
    /// <remarks>
    /// Read off the rendered markup rather than off the source, because what matters is the name
    /// that reaches the browser. A control named for a key <see cref="FacetKeys"/> does not define
    /// would be dropped on the floor by the listing, and the reader would see a filter they set
    /// having no effect at all — the silent no-op this codebase refuses everywhere else.
    /// </remarks>
    [Test]
    public async Task EveryControlIsNamedForAFacetTheListingReads()
    {
        var html = await Render.PageAsync<FindAGame>([]);

        var known = typeof(FacetKeys)
            .GetFields()
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);

        var named = Regex.Matches(html, @"name=""(?<name>[^""]+)""")
            .Select(m => m.Groups["name"].Value)
            .Distinct()
            .ToList();

        await Assert.That(named).IsNotEmpty();

        foreach (var name in named)
        {
            await Assert.That(known.Contains(name)).IsTrue();
        }
    }

    /// <summary>
    /// The activity bands it offers are spelled by the enum, so none can be unaskable.
    /// </summary>
    /// <remarks>
    /// The first draft of this page wrote the three tokens out by hand, which is exactly the drift
    /// its own design comment says it avoids: a band renamed in the enum would leave the wizard
    /// offering a value the binding refuses, and nothing would have failed to say so.
    /// </remarks>
    [Test]
    public async Task TheBandsItOffersAreTheOnesTheBindingAccepts()
    {
        var html = await Render.PageAsync<FindAGame>([]);

        var offered = Regex.Matches(html, @"name=""band"" value=""(?<token>[^""]*)""")
            .Select(m => m.Groups["token"].Value)
            .Where(t => t.Length > 0)
            .ToList();

        await Assert.That(offered).IsNotEmpty();

        foreach (var token in offered)
        {
            await Assert.That(FacetTokens.Bands.Contains(token)).IsTrue();
        }
    }

    /// <summary>No rating, no score, no recommendation — here least of all.</summary>
    /// <remarks>
    /// A page that asks somebody what they want is the most natural place on this site for a
    /// "best match" to appear, and the absence of one is the thing worth guarding. Rankings are
    /// computed from measured data; a wizard that scored games against a questionnaire would be the
    /// vote this project exists without.
    /// </remarks>
    [Test]
    public async Task TheWizardRecommendsNothing()
    {
        var words = Render.Words(await Render.PageAsync<FindAGame>([]));

        foreach (var forbidden in (string[])["recommend", "best match", "top pick", "rating", "score"])
        {
            await Assert.That(words.Contains(forbidden, StringComparison.OrdinalIgnoreCase)).IsFalse();
        }
    }
}
