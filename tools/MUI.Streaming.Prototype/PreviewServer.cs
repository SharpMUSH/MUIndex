using MUI.Catalog;

namespace MUI.Streaming.Prototype;

public static class PreviewServer
{
    public static async Task RunAsync(GameFacetRow[] rows)
    {
        var catalogue = FacetedSearch.Prepare(rows);
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:5187");
        await using var app = builder.Build();
        app.MapGet("/{mode}", async (string mode, HttpContext http) =>
        {
            if (mode is not ("buffered" or "streamed"))
            {
                http.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            var batchSize = 50;
            if ((http.Request.Query.TryGetValue("batch", out var value) && !int.TryParse(value, out batchSize))
                || batchSize is < 1 or > 900)
            {
                http.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            var snapshot = ListingSnapshot.FromCatalogue(catalogue);
            var experiment = new ListingExperiment(snapshot);
            http.Response.ContentType = "text/html; charset=utf-8";
            http.Response.Headers.CacheControl = "no-store";
            await experiment.WriteAsync(mode == "buffered" ? 0 : batchSize, async text =>
            {
                await http.Response.WriteAsync(text, http.RequestAborted);
                await http.Response.Body.FlushAsync(http.RequestAborted);
            }, http.RequestAborted);
        });
        await app.RunAsync();
    }
}
