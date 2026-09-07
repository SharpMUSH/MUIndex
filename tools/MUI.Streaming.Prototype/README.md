# Sequential HTML streaming prototype

An isolated experiment based on the listing after PR #173. No production routes, container
settings or crawler policies are changed. This is ordinary HTML, with no Blazor streaming patches
or JavaScript dependency.

## Run

Requires the repository's .NET 10 SDK and Python 3. From the repository root:

```bash
dotnet run -c Release --project tools/MUI.Streaming.Prototype </dev/null
python3 tools/MUI.Streaming.Prototype/http_check.py
```

The first command generates experimental Razor components from the **current** Games.razor and
headless renderer, runs 75 exact-output comparisons plus backpressure/cancellation checks, and
prints allocation, timing and live-heap samples. Generation is a build dependency; generated files
are ignored by Git. Expected source shapes are checked so a changed template fails visibly.
The second command starts and stops its own loopback server on port 5187 and checks real HTTP.

For manual requests:

```bash
dotnet run -c Release --no-build --project tools/MUI.Streaming.Prototype -- --serve
curl --no-buffer http://127.0.0.1:5187/streamed?batch=50
```

`/buffered` renders the whole listing. `/streamed?batch=25`, `50` or `100` sends its shell first,
then rows in batches, then the suffix. The wrapper is intentionally minimal and unstyled.

## Mechanism

Capture one GameListing and one timestamp before writing. Render the shell without its row loop,
keeping the facets and controls computed over the complete result. Each row batch uses a new,
short-lived HtmlRenderer and the same immutable answer. Dispose that renderer before awaiting the
output write. Await each write/flush before rendering the next batch, so a slow consumer does not
cause a queue of rendered batches. Cancellation prevents further rendering/writes. Compute the
ranking separator once for the whole result, not once per batch.

The generated components use the original Razor row markup and its encoding/localization helpers.
The original, unmodified Games component is the comparison baseline. Plain mode and invalid filter
responses fall back to their normal non-batched document. The declaration and closing document tags
are shared between the two experiment endpoints. Synthetic games only; no production data is read.

## Measurements

One warmed local Release run over 900 games, ten timing iterations per mode:

| Batch | Allocated/request | Sampled live render heap delta | Ready for first write | Total render/write-callback time |
| --- | ---: | ---: | ---: | ---: |
| Whole listing | 7.13 MB | 1.61 MB | 13.26 ms | 13.27 ms |
| 25 rows | 7.32 MB | 0.19 MB | 0.49 ms | 10.25 ms |
| 50 rows | 6.64 MB | 0.26 MB | 0.56 ms | 9.50 ms |
| 100 rows | 6.30 MB | 0.41 MB | 0.70 ms | 10.11 ms |

Timing varied between runs; some batched runs were slower overall. The repeatable result was lower
sampled live render memory and earlier first-write readiness, not guaranteed throughput improvement.
Fifty rows is a useful next integration candidate: approximately 84% less sampled live render heap
in this fixture, with less renderer setup churn than 25 rows.

A separate warmed loopback HTTP run measured median time to first byte of **11.70 ms buffered**
and **1.08 ms for 50-row batches** (ten requests). Median completion was 12.44 vs. 9.76 ms.
Each response contained all 900 rows in 605,853 UTF-8 bytes. Chunked delivery, invalid-batch 400 and
completion with a throttled reader passed. These measurements exclude TLS and Traefik.

## What the checks establish

- 75 byte-for-byte comparisons: English, German and Chinese; default/name sort, empty results,
  plain mode and invalid filters; batch sizes 1, 25, 50, 100 and 900.
- Mixed Latin, Han and Arabic names, HTML metacharacters, icons, growth indicators and unknown
  counts exercise encoding, direction and ranking-separator placement across batches.
- An awaited output callback stops subsequent rendering; cancellation while held prevents row writes.
- Every chunk in a response uses one frozen timestamp and one query result. HTTP requests take
  fresh timestamps; the benchmark deliberately freezes its clock across comparison runs.

## Limits and next integration work

The live-memory number is a **sampled whole-process managed-heap delta at renderer completion**,
after ToHtmlString and a forced collection. It is not peak request memory, allocation volume, RSS,
or a production memory guarantee. GC sampling runs separately from the timed pass. Timing uses a
completed-task sink; only the separately reported HTTP check measures network arrival.

The baseline here uses the headless renderer and a full HTML string, whereas production's
RazorComponentEndpointInvoker uses its own buffered writer. The production layout, HeadOutlet,
authentication, antiforgery, cookies, compression, middleware and Traefik are not integrated or
validated by this tool. Exact output means the Games component plus the common experimental wrapper,
not the full production application. A throttled localhost reader may fit in OS buffers; the held
callback is the deterministic backpressure check. Concurrent slow-reader RSS remains unmeasured.

Before shipping, integrate the real application shell while preserving personalization and header
behavior; settle status/headers before the first flush; abort correctly on errors after a flush;
exercise cancellation and concurrent slow clients through the proxy; and measure allocations,
retained memory and throughput at production-like load. Keep the catalogue snapshot coalesced and
bounded. Do not route each batch through independent database queries or accumulate batches in a
single long-lived renderer. Generated-source surgery is for this experiment, not the intended
production architecture.
