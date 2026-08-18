using System.Buffers;
using System.Text.Json;

using Microsoft.Extensions.Options;

using MUI.Catalog;
using MUI.Web.Data;

namespace MUI.Web.Api;

/// <summary>
/// The bulk dump: the whole catalogue in one response, written straight to the socket.
/// </summary>
/// <remarks>
/// Two shapes: <c>games.json</c> is one document for a whole-payload JSON parser, <c>games.ndjson</c>
/// is one game per line for <c>jq -c</c>, a bulk loader, or a process that must not hold the catalogue
/// in memory. <b>Archived games are in the dump</b> — archiving removes a game from the default
/// listing and nothing else (spec §7.5).
/// </remarks>
public static class DumpEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet(ApiRoutes.Dump, (HttpContext http,
            IGameQueries queries,
            IAvailabilityHistory availability,
            IOptions<DatasetLicenceOptions> licence,
            IAttributionSource attribution,
            TimeProvider clock) =>
            WriteAsync(http, DumpFormat.Document, queries, availability, licence, attribution, clock));

        api.MapGet(ApiRoutes.DumpLines, (HttpContext http,
            IGameQueries queries,
            IAvailabilityHistory availability,
            IOptions<DatasetLicenceOptions> licence,
            IAttributionSource attribution,
            TimeProvider clock) =>
            WriteAsync(http, DumpFormat.Lines, queries, availability, licence, attribution, clock));
    }

    public enum DumpFormat
    {
        /// <summary>One JSON object, licence and attribution first, then every game.</summary>
        Document,

        /// <summary>One game per line. Newline-delimited JSON, the format a loader wants.</summary>
        Lines,
    }

    public static async Task WriteAsync(
        HttpContext http,
        DumpFormat format,
        IGameQueries queries,
        IAvailabilityHistory availability,
        IOptions<DatasetLicenceOptions> licence,
        IAttributionSource attribution,
        TimeProvider clock)
    {
        var now = ApiClock.Now(clock);
        var header = new DumpHeaderView(
            ApiVersion.Current,
            now,
            licence.Value.View(),
            attribution.Sources(),
            licence.Value.Notice);

        // Pass one: the same writer, into a sink that keeps only the hash, so the ETag is a hash of
        // the exact bytes pass two writes rather than a stamp standing in for them. Correctness
        // requires both passes see the same data — free against the in-memory catalogue, but against
        // Postgres this needs one repeatable-read transaction around both.
        using var hasher = new ETag.HashSink();
        await WriteBodyAsync(
            hasher, _ => ValueTask.CompletedTask, format, header, queries, availability, now,
            http.RequestAborted);
        var etag = hasher.Tag();

        var contentType = format is DumpFormat.Lines
            ? "application/x-ndjson; charset=utf-8"
            : "application/json; charset=utf-8";

        ApiResponse.Prepare(http, contentType, etag);

        // Large and changes only at the pace of a crawl cycle — the one route where a conditional
        // request saves something worth saving.
        if (ApiResponse.NotModified(http, etag))
        {
            return;
        }

        // No Content-Length: the body is produced as it is written, never assembled in one place.
        // Response.BodyWriter, not Response.Body — a Utf8JsonWriter over a Stream flushes itself
        // synchronously when its buffer fills, and Kestrel forbids synchronous writes.
        var body = http.Response.BodyWriter;
        await WriteBodyAsync(
            body,
            async token => await body.FlushAsync(token),
            format,
            header,
            queries,
            availability,
            now,
            http.RequestAborted);
    }

    private static async Task WriteBodyAsync(
        IBufferWriter<byte> sink,
        Func<CancellationToken, ValueTask> flushAsync,
        DumpFormat format,
        DumpHeaderView header,
        IGameQueries queries,
        IAvailabilityHistory availability,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var games = await queries.ListAsync(
            new GameFilter { IncludeArchived = true }, cancellationToken);

        using var writer = new Utf8JsonWriter(sink, new JsonWriterOptions
        {
            Indented = false,
            Encoder = ApiJson.Options.Encoder,

            // NDJSON is a sequence of root-level values, which the writer's validator otherwise forbids.
            SkipValidation = format is DumpFormat.Lines,
        });

        if (format is DumpFormat.Document)
        {
            writer.WriteStartObject();

            // Spliced from DumpHeaderView rather than re-described here, so there's one definition.
            using (var head = JsonSerializer.SerializeToDocument(header, ApiJson.Options))
            {
                foreach (var property in head.RootElement.EnumerateObject())
                {
                    property.WriteTo(writer);
                }
            }

            writer.WritePropertyName("games");
            writer.WriteStartArray();
        }

        foreach (var summary in games)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var page = await queries.FindAsync(summary.Slug, cancellationToken);
            if (page is null)
            {
                continue;
            }

            var intervals = await availability.ForGameAsync(summary.Id, cancellationToken);
            JsonSerializer.Serialize(writer, ApiMapper.Game(page, intervals, now), ApiJson.Options);

            if (format is DumpFormat.Lines)
            {
                // Newline-terminated per record, writer reset to root — a reader can act on game one
                // without waiting for game ten thousand.
                writer.Flush();
                sink.GetSpan(1)[0] = (byte)'\n';
                sink.Advance(1);
                writer.Reset();
            }

            if (writer.BytesPending > FlushThreshold)
            {
                writer.Flush();
            }

            await flushAsync(cancellationToken);
        }

        if (format is DumpFormat.Document)
        {
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        writer.Flush();
        await flushAsync(cancellationToken);
    }

    /// <summary>Pending bytes before the writer hands them to the pipe. One TCP segment's worth.</summary>
    private const int FlushThreshold = 16 * 1024;
}
