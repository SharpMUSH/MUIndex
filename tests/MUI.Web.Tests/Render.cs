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
    public static async Task<string> ComponentAsync<TComponent>(Dictionary<string, object?> parameters)
        where TComponent : IComponent
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
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
