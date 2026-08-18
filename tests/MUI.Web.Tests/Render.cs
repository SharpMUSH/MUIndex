using System.Text.RegularExpressions;

using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Web.Data;
using MUI.Web.Fixtures;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MUI.Web.Tests;

/// <summary>
/// Renders one component to HTML, headlessly.
/// </summary>
/// <remarks>
/// A claim about markup has to be read off a rendered frame rather than off the source. "The
/// heatmap is a real table with headers" and "a hatched hour is a different element from an empty
/// one" are assertions about what a browser and a screen reader receive, and the only way to check
/// them is to look at what came out.
/// </remarks>
public static class Render
{
    /// <summary>
    /// A whole routable page, with the services a page injects.
    /// </summary>
    /// <remarks>
    /// Wired to the same fixture the site falls back on, and deliberately <em>without</em> the
    /// account services — which is the condition under test for anything about claiming: those are
    /// registered only when a connection string is, so a page rendered here sees exactly what a
    /// reader of the demo site sees.
    /// </remarks>
    public static Task<string> PageAsync<TComponent>(Dictionary<string, object?> parameters)
        where TComponent : IComponent =>
        PageAsync<TComponent>(parameters, string.Empty);

    /// <summary>
    /// The same page, rendered at a URL.
    /// </summary>
    /// <remarks>
    /// The listing reads its own querystring rather than binding parameter by parameter, because one
    /// parser answers both it and the read API — so rendering it at all means giving it somewhere to
    /// read that from. Nothing here did until there was a sort whose survival of the URL had to be
    /// proved, which is also how "last reached now ago" reached a real page: no test had ever looked
    /// at a rendered listing row.
    /// </remarks>
    public static Task<string> PageAsync<TComponent>(
        Dictionary<string, object?> parameters,
        string query,
        bool measured = false,
        IReadOnlyList<GameRecord>? games = null)
        where TComponent : IComponent =>
        ComponentAsync<TComponent>(parameters, services =>
        {
            var fixture = new FixtureGameQueries();

            services.AddSingleton<IGameQueries>(fixture);
            services.AddSingleton<IAvailabilityHistory>(fixture);
            services.AddSingleton(TimeProvider.System);

            // The same answer the demo path gives: there is no crawler behind a fixture, so the
            // front page's strip renders nothing rather than a heartbeat nobody measured.
            services.AddSingleton<ICrawlerPulse, NoCrawlerPulse>();

            // The stored rows, for the surfaces that read a game rather than a game page — claiming,
            // and the submission form's link. Registered only when a caller supplies them, because
            // its absence is what the demo fixture looks like and several pages switch on that.
            if (games is not null)
            {
                services.AddSingleton<IGameStore>(new StubGameStore(games));
            }

            // Whether a database is configured, which several surfaces switch on: claiming and
            // submitting are absent over the fixture rather than present and unable to do anything.
            // The queries behind them stay the fixture's either way — this asks what the page renders
            // when a catalogue exists, not what a real one holds.
            services.AddSingleton(new CatalogueSource(measured));
            services.AddSingleton<NavigationManager>(new StubNavigation(query));
            services.AddSingleton<AntiforgeryStateProvider, StubAntiforgery>();
        });

    /// <summary>
    /// The two lookups a page makes against stored rows. Everything else throws rather than
    /// pretending, because a surface reaching for a writer here is a surface in the wrong layer.
    /// </summary>
    private sealed class StubGameStore(IReadOnlyList<GameRecord> games) : IGameStore
    {
        public Task<GameRecord?> ByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(games.FirstOrDefault(g => g.Id == id));

        public Task<GameRecord?> BySlugAsync(string slug, CancellationToken cancellationToken = default) =>
            Task.FromResult(games.FirstOrDefault(g => g.Slug == slug));

        public Task InsertAsync(GameRecord game, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task ExcludeAsync(Guid id, string reason, DateTimeOffset at, CancellationToken ct = default) =>
            SetStateAsync(id, LifecycleState.Excluded, at, ct);

        public Task IncludeAsync(Guid id, DateTimeOffset at, CancellationToken ct = default) =>
            SetStateAsync(id, LifecycleState.Active, at, ct);

        public Task UnlistAsync(Guid id, Guid byUserId, DateTimeOffset at, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task RelistAsync(Guid id, DateTimeOffset at, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task SetStateAsync(Guid id, LifecycleState state, DateTimeOffset at, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task MarkReachableAsync(Guid id, DateTimeOffset at, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<string?> RenameAsync(
            Guid id,
            string name,
            string slug,
            DateTimeOffset at,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SetClaimedAsync(Guid id, bool isClaimed, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task CorroborateAsync(
            Guid id,
            DateTimeOffset at,
            IReadOnlyList<string> signals,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<GameRecord>> UnarchivedAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    /// <summary>
    /// Enough of an antiforgery provider for <c>&lt;AntiforgeryToken /&gt;</c> to render.
    /// </summary>
    /// <remarks>
    /// The framework's own provider is internal and wants a request behind it. Without one the
    /// component renders nothing at all, silently — so a test asking "is this form token-protected"
    /// would pass on a page that had never had a token, and fail on one that did. This is what makes
    /// the absence of a hidden field mean something.
    /// </remarks>
    private sealed class StubAntiforgery : AntiforgeryStateProvider
    {
        public override AntiforgeryRequestToken GetAntiforgeryToken() =>
            new("a-token", "__RequestVerificationToken");
    }

    /// <summary>A navigation manager with nothing to do but answer "which URL am I on".</summary>
    private sealed class StubNavigation : NavigationManager
    {
        public StubNavigation(string query) =>
            Initialize("http://localhost/", "http://localhost/games" + query);

        protected override void NavigateToCore(string uri, bool forceLoad)
        {
        }
    }

    public static async Task<string> ComponentAsync<TComponent>(
        Dictionary<string, object?> parameters,
        Action<IServiceCollection>? configure = null)
        where TComponent : IComponent
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        configure?.Invoke(services);
        await using var provider = services.BuildServiceProvider();

        await using var renderer = new HtmlRenderer(provider, NullLoggerFactory.Instance);

        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<TComponent>(
                ParameterView.FromDictionary(parameters));
            return output.ToHtmlString();
        });
    }

    /// <summary>
    /// A rendered page with its entities decoded and its whitespace collapsed.
    /// </summary>
    /// <remarks>
    /// An assertion about a sentence should not fail because the renderer escaped an em dash or the
    /// plain surface wrapped at eighty columns. Both are correct behaviour, and a test that reads
    /// the raw bytes is asserting on the formatting rather than on the claim.
    /// </remarks>
    public static string Words(string markupOrText) =>
        string.Join(' ', System.Net.WebUtility.HtmlDecode(markupOrText)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>
    /// What a reader actually sees: the elements removed, then decoded and collapsed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Words"/> keeps the tags, which is right for most assertions here and wrong for one
    /// kind: a sentence that places its own link or code span is broken up by elements, so asserting
    /// it against <c>Words</c> asserts the markup as well as the sentence. It is also why a
    /// class name — <c>&lt;details class="history"&gt;</c> — reads as the word "history" and can
    /// satisfy a check that was about the copy.
    /// </para>
    /// <para>
    /// Tags are stripped <em>before</em> decoding, on purpose. The owner dashboard prints a badge
    /// snippet as escaped markup for an operator to copy; decoding first would turn it into
    /// something this regex then ate, and the snippet is text a reader is meant to see.
    /// </para>
    /// </remarks>
    public static string Text(string markup) =>
        Words(Regex.Replace(markup, "<[^>]*>", " ", RegexOptions.None, TimeSpan.FromSeconds(5)));
}
