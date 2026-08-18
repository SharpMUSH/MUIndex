using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Web.Components.Pages;
using MUI.Web.Fixtures;
using MUI.Web.Localization;

using Microsoft.AspNetCore.Http;

namespace MUI.Web.Tests;

/// <summary>
/// A game may publish <c>DESCRIPTION-&lt;lang&gt;</c> variants of its own choosing over MSSP — not a
/// vocabulary MUIndex defines, just whatever an operator typed. When a browser's own
/// <c>Accept-Language</c> answers to one, the game page shows it instead of the plain
/// <c>DESCRIPTION</c>.
/// </summary>
public class LocalizedDescriptionTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 30, 20, 0, 0, TimeSpan.Zero);

    private static IReadOnlyDictionary<string, ProvenanceChip> Declared(params (string Field, string Value)[] fields) =>
        fields.ToDictionary(f => f.Field, f => new ProvenanceChip(f.Value, FieldSource.Mssp, Now, IsStale: false));

    [Test]
    [Arguments("de", "Deutsch")]
    [Arguments("de-AT,de;q=0.9,en;q=0.8", "Deutsch")]         // region falls back to the language subtag
    [Arguments("fr;q=0.9,de;q=0.5", "Französisch preferred")] // higher quality wins even when listed second
    public async Task TheHighestQualityMatchingVariantWins(string acceptLanguage, string expected)
    {
        var declared = Declared(
            ("description-de", "Deutsch"),
            ("description-fr", "Französisch preferred"));

        var chosen = LocalizedDescription.Choose(acceptLanguage, fallback: "default", declared);

        await Assert.That(chosen).IsEqualTo(expected);
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("es")]                // no DESCRIPTION-ES on offer
    [Arguments("de;q=0")]            // explicitly refused
    [Arguments("*")]
    public async Task FallsBackToTheDefaultDescriptionWhenNothingMatches(string? acceptLanguage)
    {
        var declared = Declared(("description-de", "Deutsch"));

        var chosen = LocalizedDescription.Choose(acceptLanguage, fallback: "default", declared);

        await Assert.That(chosen).IsEqualTo("default");
    }

    [Test]
    public async Task ARegionOnlyDeclaredVariantIsNotMatchedByASiblingRegion()
    {
        // The site does not invent en-US from en-GB, or vice versa — the match is on the whole
        // declared field name, keyed exactly the way the game spelled its suffix.
        var declared = Declared(("description-pt-br", "Português do Brasil"));

        var chosen = LocalizedDescription.Choose("pt-PT", fallback: "default", declared);

        await Assert.That(chosen).IsEqualTo("default");
    }

    [Test]
    public async Task AnEmptyDeclaredValueDoesNotWin()
    {
        // A cleared owner row is an absence, not an answer (see OwnerEnrichment) — the same rule
        // DeclaredOf already applies before a value reaches this dictionary at all, asserted here so
        // a caller that skips that filter still gets the fallback rather than an empty lede.
        var declared = Declared(("description-de", string.Empty));

        var chosen = LocalizedDescription.Choose("de", fallback: "default", declared);

        await Assert.That(chosen).IsEqualTo("default");
    }

    [Test]
    public async Task TheGamePageShowsTheMatchingVariantOverThePlainDescription()
    {
        var http = new DefaultHttpContext();
        http.Request.Headers.AcceptLanguage = "de";

        var html = await Render.PageAsync<Game>(
            new() { ["Slug"] = "m-u-s-h" },
            query: string.Empty,
            http: http,
            queries: new GermanVariant());

        // "Declared by the game" always lists description-de regardless of the browser's language —
        // it is the lede, and only the lede, that this feature is meant to change.
        await Assert.That(Lede(html)).Contains("Der Entwicklungsserver für PennMUSH");
        await Assert.That(Lede(html)).DoesNotContain("The development server for PennMUSH");
    }

    [Test]
    public async Task AGameWithNoMatchingVariantStillShowsThePlainDescriptionInTheLede()
    {
        var http = new DefaultHttpContext();
        http.Request.Headers.AcceptLanguage = "fr";

        var html = await Render.PageAsync<Game>(
            new() { ["Slug"] = "m-u-s-h" },
            query: string.Empty,
            http: http,
            queries: new GermanVariant());

        await Assert.That(Lede(html)).Contains("The development server for PennMUSH");
        await Assert.That(Lede(html)).DoesNotContain("Der Entwicklungsserver für PennMUSH");
    }

    private static string Lede(string html)
    {
        var start = html.IndexOf("<p class=\"lede\">", StringComparison.Ordinal);
        var end = html.IndexOf("</p>", start, StringComparison.Ordinal);

        return Render.Words(html[start..end]);
    }

    /// <summary>
    /// Wraps the shared demo fixture rather than editing it: m-u-s-h is a real game (spec §9's own
    /// remarks on <c>FixtureGameQueries</c> — "every hard state here came off a real game"), and it
    /// has never published a German description. Adding one to the fixture itself would misrepresent
    /// it on the one page every reader without a database sees. This decorator's German variant
    /// exists only for the lifetime of this test.
    /// </summary>
    private sealed class GermanVariant : IGameQueries
    {
        private static readonly ProvenanceChip Chip =
            new("Der Entwicklungsserver für PennMUSH.", FieldSource.Mssp, Now, IsStale: false);

        private readonly FixtureGameQueries _inner = new();

        public Task<GameListing> SearchAsync(GameFilter filter, CancellationToken cancellationToken = default) =>
            _inner.SearchAsync(filter, cancellationToken);

        public Task<IReadOnlyList<GameSummary>> ListAsync(
            GameFilter filter, CancellationToken cancellationToken = default) =>
            _inner.ListAsync(filter, cancellationToken);

        public async Task<GamePage?> FindAsync(string slug, CancellationToken cancellationToken = default) =>
            WithGerman(await _inner.FindAsync(slug, cancellationToken));

        public async Task<GamePage?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
            WithGerman(await _inner.FindAsync(id, cancellationToken));

        public Task<GameSummary?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            _inner.FindByIdAsync(id, cancellationToken);

        public Task<LivenessFeeds> FeedsAsync(CancellationToken cancellationToken = default) =>
            _inner.FeedsAsync(cancellationToken);

        public Task<EcosystemDashboard> EcosystemAsync(CancellationToken cancellationToken = default) =>
            _inner.EcosystemAsync(cancellationToken);

        public Task<Rankings> RankingsAsync(
            RankingSpan span = RankingSpan.Week, CancellationToken cancellationToken = default) =>
            _inner.RankingsAsync(span, cancellationToken);

        private static GamePage? WithGerman(GamePage? page) =>
            page is null || page.Summary.Slug != "m-u-s-h"
                ? page
                : page with
                {
                    Declared = new Dictionary<string, ProvenanceChip>(page.Declared, StringComparer.Ordinal)
                    {
                        ["description-de"] = Chip,
                    },
                };
    }
}
