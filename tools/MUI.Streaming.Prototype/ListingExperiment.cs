using System.Reflection;

using MUI.Catalog;
using MUI.Web.Api;
using MUI.Web.Components.Pages;
using MUI.Web.Localization;
using MUI.Web.Tests;

namespace MUI.Streaming.Prototype;

public sealed class ListingExperiment(IGameQueries queries, string query = "", HttpContext? context = null, TimeProvider? clock = null)
{
    private const string Marker = "<!--MUI-STREAM-ROWS-->";
    private string Prefix => $"<!doctype html><html lang=\"{context.LocaleOf().Tag}\"><head><meta charset=\"utf-8\"><title>Streaming prototype</title></head><body>";
    private const string Suffix = "</body></html>";

    public async Task WriteAsync(int batch, Func<string, Task> write, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(batch);
        cancellationToken.ThrowIfCancellationRequested();
        var requestClock = new SnapshotClock((clock ?? TimeProvider.System).GetUtcNow());
        var valid = GameFilterBinding.TryRead(query, out var bound, out _);
        // Pin a single answer before sending headers; every batch must see the same rows.
        var listing = valid ? await queries.SearchAsync(bound.Filter, cancellationToken) : GameListing.Empty;
        var snapshot = DispatchProxy.Create<IGameQueries, SnapshotQueries>();
        ((SnapshotQueries)(object)snapshot).Listing = listing;
        if (batch == 0)
        {
            var page = await StreamingRender.PageAsync<Games>([], query, queries: snapshot, http: context, clock: requestClock);
            await Send(Prefix + page + Suffix);
            return;
        }
        var shell = await StreamingRender.PageAsync<StreamedGames>([], query, queries: snapshot, http: context, clock: requestClock);
        var marker = shell.IndexOf(Marker, StringComparison.Ordinal);
        // Plain mode and validation errors have no row loop; return their normal document.
        if (marker < 0)
        {
            await Send(Prefix + shell + Suffix);
            return;
        }
        if (shell.LastIndexOf(Marker, StringComparison.Ordinal) != marker)
            throw new InvalidOperationException("Exactly one row insertion point is required.");
        await Send(Prefix + shell[..marker]);
        cancellationToken.ThrowIfCancellationRequested();
        var unranked = listing.Games.FirstOrDefault(g => GameSorting.IsUnranked(g, bound.Filter.Sort));
        for (var offset = 0; offset < listing.Games.Count; offset += batch)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // PageAsync disposes its renderer and service provider before returning the batch.
            var html = await StreamingRender.PageAsync<StreamedGames>(new()
            {
                [nameof(StreamedGames.RowsOnly)] = true,
                [nameof(StreamedGames.UnrankedStart)] = unranked,
                [nameof(StreamedGames.Offset)] = offset,
                [nameof(StreamedGames.BatchSize)] = batch,
            }, query, queries: snapshot, http: context, clock: requestClock);
            await Send(html);
        }
        await Send(shell[(marker + Marker.Length)..] + Suffix);

        Task Send(string html)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return write(html);
        }
    }
}

public sealed class SnapshotClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

public sealed class RenderProbe(long baseline)
{
    public static readonly AsyncLocal<RenderProbe?> Current = new();
    public long Peak { get; private set; }
    public void Observe() => Peak = Math.Max(Peak, GC.GetTotalMemory(true) - baseline);
}

public class SnapshotQueries : DispatchProxy
{
    private int _calls;
    public int Calls => Volatile.Read(ref _calls);
    public GameListing Listing { get; set; } = GameListing.Empty;
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        if (method?.Name != nameof(IGameQueries.SearchAsync)) throw new NotSupportedException($"Unexpected listing query: {method?.Name}");
        Interlocked.Increment(ref _calls);
        return Task.FromResult(Listing);
    }
}
