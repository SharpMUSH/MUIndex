using MUI.Catalog;
using MUI.Web.Data;
using MUI.Web.Fixtures;
using Microsoft.AspNetCore.Components;
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
        ComponentAsync<TComponent>(parameters, services =>
        {
            var fixture = new FixtureGameQueries();

            services.AddSingleton<IGameQueries>(fixture);
            services.AddSingleton<IAvailabilityHistory>(fixture);
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton(new CatalogueSource(IsMeasured: false));
        });

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
}
