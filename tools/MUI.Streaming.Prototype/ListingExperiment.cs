using MUI.Catalog;
using MUI.Web.Components;

namespace MUI.Streaming.Prototype;

public sealed class ListingExperiment(ListingSnapshot snapshot, Action? rendered = null)
{
    public async Task WriteAsync(int batchSize, Func<string, Task> write, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(batchSize);
        cancellationToken.ThrowIfCancellationRequested();
        var renderer = new BatchRenderer(snapshot.Context, rendered);
        var document = await renderer.RenderAsync<ListingDocument>(new()
        {
            [nameof(ListingDocument.Snapshot)] = snapshot,
            [nameof(ListingDocument.Rows)] = batchSize == 0 ? null : HtmlInsertion.Placeholder,
        });
        if (batchSize == 0 || HtmlInsertion.Split(document) is not { } parts)
        {
            await Send(document);
            return;
        }

        await Send(parts.Prefix);
        var games = snapshot.Listing.Games;
        var presentation = new GameRowPresentation(snapshot.Filter.Sort, snapshot.Now, snapshot.Context,
            games.FirstOrDefault(game => GameSorting.IsUnranked(game, snapshot.Filter.Sort)));
        for (var offset = 0; offset < games.Count; offset += batchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var html = await renderer.RenderAsync<ListingBatch>(new()
            {
                [nameof(ListingBatch.Games)] = games.Skip(offset).Take(batchSize),
                [nameof(ListingBatch.Presentation)] = presentation,
            });
            // RenderAsync has disposed its renderer before waiting for the consumer.
            await Send(html);
        }
        await Send(parts.Suffix);

        Task Send(string html)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return write(html);
        }
    }
}
