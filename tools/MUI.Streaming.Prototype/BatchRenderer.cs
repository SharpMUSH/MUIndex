using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging.Abstractions;

using MUI.Web.Data;

namespace MUI.Streaming.Prototype;

/// <summary>Owns each render's services and buffers until its HTML has been produced.</summary>
public sealed class BatchRenderer(HttpContext? context, Action? rendered = null)
{
    public async Task<string> RenderAsync<TComponent>(Dictionary<string, object?> parameters)
        where TComponent : IComponent
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new CatalogueSource(IsMeasured: false));
        if (context is not null)
        {
            services.AddCascadingValue(_ => context);
        }
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, NullLoggerFactory.Instance);
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<TComponent>(ParameterView.FromDictionary(parameters));
            var html = output.ToHtmlString();
            rendered?.Invoke();
            return html;
        });
    }
}
