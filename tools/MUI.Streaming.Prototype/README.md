# Sequential HTML streaming prototype

An experiment based on the listing after PR #173. The normal page and prototype now share ordinary
Razor presentation components; there is no source generator, reflection proxy or test-project
dependency. Streaming is not enabled on production routes. Output is ordinary HTML without JavaScript.

## Run

Requires the repository's .NET 10 SDK. Python 3 is needed only for the optional HTTP check. From the repository root:

```bash
dotnet run -c Release --project tools/MUI.Streaming.Prototype </dev/null
python3 tools/MUI.Streaming.Prototype/http_check.py
```

The first command runs 75 exact-output comparisons plus backpressure/cancellation checks, then
prints allocation, timing and live-heap samples. The second starts and stops its own loopback
server on port 5187 and checks real HTTP. Python is used only for that optional HTTP check.

For manual requests:

```bash
dotnet run -c Release --no-build --project tools/MUI.Streaming.Prototype -- --serve
curl --no-buffer http://127.0.0.1:5187/streamed?batch=50
```

`/buffered` renders the whole listing. `/streamed?batch=25`, `50` or `100` sends its shell first,
then rows in batches, then the suffix. The wrapper is intentionally minimal and unstyled.

## Structure

- `Games.razor` binds the request and loads its data. `GamesView.razor` renders the listing and
  `GameRows.razor` supplies shared row markup. `GameRowPresentation` owns the row's formatting rules.
- `ListingSnapshot` holds the experiment's query result, locale and timestamp. Every batch reads
  the same snapshot. The preview server shares a prepared catalogue across requests.
- `BatchRenderer` owns and disposes each HtmlRenderer and its services. `ListingExperiment` writes
  the document prefix, row batches and suffix, awaiting each write before rendering another batch.
- `ListingDocument` and `ListingBatch` are ordinary Razor components. `HtmlInsertion` provides one
  explicit insertion point because Razor components produce balanced markup. It splits rendered
  HTML only; it does not rewrite source or manually construct document tags.
- `RenderBenchmark`, `Compatibility` and `PreviewServer` separate measurement, validation and hosting.
  Measurement is an instance callback; it has no global state.

The ranking separator is resolved once over the full result. Cancellation stops further writes.
Plain mode and invalid filters return their normal complete document. All data is synthetic.

## Measurements

One warmed local Release run over 900 games, ten timing iterations per mode:

| Batch | Allocated/request | Sampled live render heap delta | Ready for first write | Total render/write-callback time |
| --- | ---: | ---: | ---: | ---: |
| Whole listing | 6.70 MB | 1.65 MB | 11.62 ms | 11.64 ms |
| 25 rows | 7.13 MB | 0.27 MB | 0.52 ms | 12.98 ms |
| 50 rows | 6.52 MB | 0.24 MB | 0.51 ms | 12.42 ms |
| 100 rows | 6.22 MB | 0.33 MB | 0.49 ms | 11.62 ms |

Timing varied between runs; some batched runs were slower overall. The repeatable result was lower
sampled live render memory and earlier first-write readiness, not guaranteed throughput improvement.
Fifty rows is a useful next integration candidate: approximately 86% less sampled live render heap
in this fixture, with less renderer setup churn than 25 rows.

A separate warmed loopback HTTP run measured median time to first byte of **12.17 ms buffered**
and **2.38 ms for 50-row batches** (ten requests). Median completion was 13.08 vs. 10.41 ms.
Each response contained all 900 rows in 605,998 UTF-8 bytes. Chunked delivery, invalid-batch 400 and
completion with a throttled reader passed. These measurements exclude TLS and Traefik.

## What the checks establish

- 75 byte-for-byte comparisons: English, German and Chinese; default/name sort, empty results,
  plain mode and invalid filters; batch sizes 1, 25, 50, 100 and 900.
- Mixed Latin, Han and Arabic names, HTML metacharacters, icons, growth indicators and unknown
  counts exercise encoding, direction and ranking-separator placement across batches.
- An awaited output callback stops subsequent rendering; cancellation while held prevents row writes.
- Every fixture page, including plain and invalid-query responses, carries a visible demo banner.
- Every chunk in a response uses one frozen timestamp and one query result. HTTP requests take
  fresh timestamps; the benchmark uses 2026-09-01 at 12:00 UTC for both fixture observations and rendering.

## Limits and next integration work

The live-memory number is a **sampled whole-process managed-heap delta at renderer completion**,
after ToHtmlString and a forced collection. It is not peak request memory, allocation volume, RSS,
or a production memory guarantee. GC sampling runs separately from the timed pass. Timing uses a
completed-task sink; only the separately reported HTTP check measures network arrival.

The baseline here renders the complete shared listing into a string, whereas production's
RazorComponentEndpointInvoker uses its own buffered writer. The production layout, HeadOutlet,
authentication, antiforgery, cookies, compression, middleware and Traefik are not integrated or
validated by this tool. The 75 exact comparisons cover buffered versus batched GamesView/GameRows with the same
experimental wrapper, not the pre-extraction implementation or full production application. The
1,188 Web tests validate the extraction through the real page and its existing surface contracts. A throttled localhost reader may fit in OS buffers; the held
callback is the deterministic backpressure check. Concurrent slow-reader RSS remains unmeasured.

Before shipping, integrate the real application shell while preserving personalization and header
behavior; settle status/headers before the first flush; abort correctly on errors after a flush;
exercise cancellation and concurrent slow clients through the proxy; and measure allocations,
retained memory and throughput at production-like load. Keep the catalogue snapshot coalesced and
bounded. Do not route each batch through independent database queries or accumulate batches in a
single long-lived renderer. The explicit HTML insertion point remains an experimental boundary
until the full application shell is integrated.
