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
/// <para>
/// A directory that publishes an unusable dump has published nothing, so this ships in the two
/// shapes a consumer actually wants. <c>games.json</c> is one document with its licence and
/// attribution in the payload, for anyone who will hand it to a JSON parser whole.
/// <c>games.ndjson</c> is one game per line, for anyone who will pipe it into <c>jq -c</c>, a
/// bulk loader, or a process that must not hold a catalogue in memory. Both carry the licence in
/// headers as well.
/// </para>
/// <para>
/// <b>Archived games are in the dump.</b> Archiving takes a game out of the default listing and out
/// of nothing else (spec §7.5); the dump is the historical record the incumbents threw away, and a
/// record with the dead games removed is the thing this project exists to replace.
/// </para>
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

        // Pass one: the same writer, into a sink that keeps only the hash. The ETag is therefore a
        // hash of the exact bytes pass two writes, not of a version stamp standing in for them — a
        // 304 on a dump has to be a promise about the body and not a guess at it.
        //
        // The cost is that the catalogue is read twice, and the correctness condition is that both
        // passes see the same data. Against the in-memory catalogue that is free. Against Postgres,
        // wrap the two in one repeatable-read transaction; without that, a write landing between
        // them would leave the validator describing a body nobody was sent.
        using var hasher = new ETag.HashSink();
        await WriteBodyAsync(
            hasher, _ => ValueTask.CompletedTask, format, header, queries, availability, now,
            http.RequestAborted);
        var etag = hasher.Tag();

        var contentType = format is DumpFormat.Lines
            ? "application/x-ndjson; charset=utf-8"
            : "application/json; charset=utf-8";

        ApiResponse.Prepare(http, contentType, etag);

        // The dump is large and changes at the pace of a crawl cycle, so it is the one route where a
        // conditional request saves something worth saving.
        if (ApiResponse.NotModified(http, etag))
        {
            return;
        }

        // No Content-Length: the body is produced as it is written and never exists in one place,
        // which is the whole point. Kestrel chunks it.
        //
        // Response.BodyWriter and not Response.Body: a Utf8JsonWriter over a Stream flushes itself
        // synchronously when its buffer fills, and Kestrel forbids synchronous writes — so a dump
        // over the stream dies partway through the first response big enough to need a flush. Over
        // the pipe, the writer's flush only advances a buffer and the socket write stays async and
        // explicit.
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

            // NDJSON is a sequence of root-level values, which is precisely what the writer's
            // validator exists to forbid. The records are still written one at a time by the
            // serialiser, so the shape of each is checked; what is switched off is the rule that
            // there may only be one.
            SkipValidation = format is DumpFormat.Lines,
        });

        if (format is DumpFormat.Document)
        {
            writer.WriteStartObject();

            // The header's own properties, spliced into a hand-driven writer rather than re-described
            // here: one definition of what a dump header is (DumpHeaderView), whichever end of the
            // file it is written from.
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
                // Newline-terminated per record, and the writer reset to root, so a reader on the
                // other end can act on game one without waiting for game ten thousand.
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
