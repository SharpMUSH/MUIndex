using System.Text.RegularExpressions;

using MUI.Catalog;
using MUI.Catalog.Persistence;
using MUI.Web.Data;
using MUI.Web.Fixtures;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MUI.Web.Tests;

/// <summary>
/// Renders one component to HTML, headlessly.
/// </summary>
/// <remarks>
/// A claim about markup has to be read off a rendered frame rather than off the source — "the
/// heatmap is a real table with headers" is a claim about what a browser or screen reader receives.
/// </remarks>
public static class Render
{
    /// <summary>
    /// A whole routable page, with the services a page injects.
    /// </summary>
    /// <remarks>
    /// Wired to the same fixture the site falls back on, and deliberately <em>without</em> the
    /// account services, which are registered only when a connection string is — so a page rendered
    /// here sees exactly what a reader of the demo site sees.
    /// </remarks>
    public static Task<string> PageAsync<TComponent>(Dictionary<string, object?> parameters)
        where TComponent : IComponent =>
        PageAsync<TComponent>(parameters, string.Empty);

    /// <summary>
    /// The same page, rendered at a URL.
    /// </summary>
    /// <remarks>
    /// The listing reads its own querystring rather than binding parameter by parameter, since one
    /// parser answers both it and the read API — so rendering it at all means giving it somewhere to
    /// read that from.
    /// </remarks>
    /// <param name="yielding">
    /// Whether the catalogue answers on a later turn of the scheduler, as a real one does.
    /// <see cref="ComponentBase"/> calls <c>StateHasChanged</c> before awaiting
    /// <c>OnParametersSetAsync</c>, so a synchronous fixture's load completes within its own first
    /// turn and the interim render never sees the initial (possibly null) state that a suspended,
    /// real-database load would expose. This parameter reproduces that suspension.
    /// </param>
    public static Task<string> PageAsync<TComponent>(
        Dictionary<string, object?> parameters,
        string query,
        bool measured = false,
        IReadOnlyList<GameRecord>? games = null,
        bool yielding = false,
        ClaimService? claimService = null,
        HttpContext? http = null,
        IGameQueries? queries = null)
        where TComponent : IComponent =>
        ComponentAsync<TComponent>(parameters, services =>
        {
            var fixture = new FixtureGameQueries();

            services.AddSingleton<IGameQueries>(queries ?? (yielding ? new Suspending(fixture) : fixture));
            services.AddSingleton<IAvailabilityHistory>(fixture);
            services.AddSingleton(TimeProvider.System);

            // Absent by default, matching the demo fixture's "no database" state — several pages
            // switch on whether this resolves.
            if (claimService is not null)
            {
                services.AddSingleton(claimService);
            }

            // The demo path's own answer: no crawler behind a fixture, so the strip renders nothing
            // rather than an unmeasured heartbeat.
            services.AddSingleton<ICrawlerPulse, NoCrawlerPulse>();

            // The stored rows, for surfaces that read a game rather than a game page — claiming, and
            // the submission form's link. Registered only when a caller supplies them.
            if (games is not null)
            {
                services.AddSingleton<IGameStore>(new StubGameStore(games));
            }

            // Whether a database is configured, which several surfaces switch on: claiming and
            // submitting are absent over the fixture rather than present and unable to do anything.
            services.AddSingleton(new CatalogueSource(measured));
            services.AddSingleton<NavigationManager>(new StubNavigation(query));
            services.AddSingleton<AntiforgeryStateProvider, StubAntiforgery>();

            // Absent by default, matching a component rendered with no HttpContext at all. Supplied
            // only by a caller with something to ask of the request, e.g. an Accept-Language header.
            if (http is not null)
            {
                services.AddCascadingValue(_ => http);
            }
        });

    /// <summary>
    /// The fixture's answers, delivered on a later turn of the scheduler.
    /// </summary>
    /// <remarks>
    /// One <c>await Task.Yield()</c> in front of each call is enough to make the returned task
    /// incomplete when the caller first looks at it — the one property of a real database this needs
    /// to reproduce. See the <c>yielding</c> parameter above.
    /// </remarks>
    private sealed class Suspending(IGameQueries inner) : IGameQueries
    {
        public async Task<GameListing> SearchAsync(GameFilter filter, CancellationToken cancellationToken = default)
        {
            await Task.Yield();

            return await inner.SearchAsync(filter, cancellationToken);
        }

        public async Task<IReadOnlyList<GameSummary>> ListAsync(
            GameFilter filter,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();

            return await inner.ListAsync(filter, cancellationToken);
        }

        public async Task<GamePage?> FindAsync(string slug, CancellationToken cancellationToken = default)
        {
            await Task.Yield();

            return await inner.FindAsync(slug, cancellationToken);
        }

        public async Task<GamePage?> FindAsync(Guid id, CancellationToken cancellationToken = default)
        {
            await Task.Yield();

            return await inner.FindAsync(id, cancellationToken);
        }

        public async Task<GameSummary?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            await Task.Yield();

            return await inner.FindByIdAsync(id, cancellationToken);
        }

        public async Task<LivenessFeeds> FeedsAsync(CancellationToken cancellationToken = default)
        {
            await Task.Yield();

            return await inner.FeedsAsync(cancellationToken);
        }

        public async Task<EcosystemDashboard> EcosystemAsync(CancellationToken cancellationToken = default)
        {
            await Task.Yield();

            return await inner.EcosystemAsync(cancellationToken);
        }

        public async Task<Rankings> RankingsAsync(
            RankingSpan span = RankingSpan.Week,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();

            return await inner.RankingsAsync(span, cancellationToken);
        }

        public async Task<IReadOnlyList<RecentGameChange>> RecentFieldChangesAsync(
            int limit, int perGameLimit = 3, CancellationToken cancellationToken = default)
        {
            await Task.Yield();

            return await inner.RecentFieldChangesAsync(limit, perGameLimit, cancellationToken);
        }
    }

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
    /// The framework's own provider is internal and wants a request behind it; without one the
    /// component renders nothing at all, silently — so a test for "is this token-protected" needs
    /// this to make the field's absence mean something.
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
    /// plain surface wrapped at eighty columns — both are correct behaviour.
    /// </remarks>
    public static string Words(string markupOrText) =>
        string.Join(' ', System.Net.WebUtility.HtmlDecode(markupOrText)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>
    /// What a reader actually sees: the elements removed, then decoded and collapsed.
    /// </summary>
    /// <remarks>
    /// <see cref="Words"/> keeps the tags, which breaks a sentence that places its own link or code
    /// span across elements, and lets a class name like <c>class="history"</c> read as the word
    /// "history". Tags are stripped <em>before</em> decoding: the owner dashboard prints a badge
    /// snippet as escaped markup, and decoding first would corrupt it before this regex ran.
    /// </remarks>
    public static string Text(string markup) =>
        Words(Regex.Replace(markup, "<[^>]*>", " ", RegexOptions.None, TimeSpan.FromSeconds(5)));
}
