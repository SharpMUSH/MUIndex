# Memory pressure during a facet crawl, 2026-09-07

## Evidence

The memory step began around 03:20 UTC (22:20 CDT), nearly four hours after image
`cbe88ef` started. Prometheus recorded web working-set peaks of 1,693 and 1,702 MiB;
host available memory reached 39 MiB. Neither web container restarted during that period.

The surviving proxy access-log window contained 67,014 filtered-listing requests out
of 71,822 requests to the site, over approximately 73 minutes. Requests combined many
facet choices and locales. The window's overall request-duration p95 was 4.54 seconds.
Only aggregate counts and latency statistics were retained in this report.

Microsoft `dotnet-counters` sampled both processes for 25 seconds. Allocation rates
ranged from approximately 29–130 MB/s on one and 54–142 MB/s on the other. A subsequent
server-local `dotnet-gcdump` of web-2 reported 274 MB of heap objects, including 24
`EndpointHtmlRenderer` instances, thousands of HTML `TextChunk[]` buffers and large
`RenderTreeFrame[]` arrays. This identifies overlapping page rendering as a substantial
live allocation source. A short sample is not evidence that every possible leak is absent.

Docker also recorded OOM kills of Traefik at its 128 MiB container limit. Its restart
count had reached 58 before the proxy configuration was corrected.

## Attribution and changes

**MUIndex deployment:** the checked-out Compose configuration already specified one
web replica and loopback-only runtime metrics, but running containers still had two
replicas and no metrics listener. Watchtower updates images, not Compose configuration.
Applying the existing configuration restored the intended memory budget and enabled
Prometheus's already-configured application scrape.

**MUIndex catalogue:** database assembly was already cached for one minute with FusionCache's
per-key request coalescing. It was not repeated for each filter URL. The uncached facet search,
however, repeatedly evaluated every other selection while counting each facet, allocating token
arrays, derived codebase strings and selection objects along the way. The revised cache stores
prepared facet values alongside each bounded catalogue snapshot. Each request evaluates every
choice at most once per row. A row failing one choice contributes only to that choice's lifted
domain; a row failing two cannot contribute to any. Spelling frequencies are counted without
retaining one string-list entry per game. Cache keys, invalidation, expiry and coalescing stay the
same; arbitrary query strings never become cache keys.

**MUIndex web:** a listing instantiated GameName, GamePlate and Moment components for each row,
incurring individual component state and render buffers. Shared Razor fragments now render the
same markup directly inside the listing. The first unranked row is found once after loading,
instead of scanning the listing again for every row. Request cancellation is passed to the
catalogue caller without cancelling the shared cache factory.

Faceted listings remain crawlable. No new robots exclusions are added. A process-wide concurrency
guard admits eight simultaneous Razor renders without queueing; excess work receives a
non-cacheable 503. This is a secondary overload guard, not cache stampede protection. It supplies
no fixed retry interval, which could synchronize rejected clients. The permit includes response
writing for slow clients; health, metrics, assets and API routes do not consume it. The rendered
404 path passes through it on re-execution. Tune `MUI_PAGE_RENDER_CONCURRENCY` using rejection rates
and memory after deployment.

### Local allocation measurements

A synthetic 900-game catalogue reproduces the cost without a database call. The warmed Release
benchmark includes a filtered request selecting band, codebase, language and genre, and an HTML
render of all 900 rows. These are local measurements, not production latency guarantees.

| Operation | Before | Revised |
| --- | ---: | ---: |
| Unfiltered facets, allocated/request | 3.51 MB | 0.46 MB |
| Filtered facets, allocated/request | 11.37 MB | 0.41 MB |
| Render 900 rows, allocated/request | 10.54 MB | 5.93 MB |
| Render 900 rows, mean duration | 21.5 ms | 8.0 ms |
| Rendered document length | 604,368 characters | 604,368 characters |

Reproduce the revised measurements with
`dotnet run -c Release --project tools/MUI.Listing.Benchmarks </dev/null`.

Facet measurements use thread allocation counters around synchronous searches. Render measurements
use total allocation counters in an isolated process, including the headless renderer's setup and
output string. The prepared index is built once, outside the request loop, as in production's
catalogue cache. The allocation regression failed at 13.35 MB on the old implementation and passes
below 1 MB with the prepared snapshot. All existing facet semantics and rendered-surface tests pass.
This reduces request cost; it does not establish that production memory has recovered before the
new image is deployed and observed under traffic.

### Streaming assessment

Microsoft's [.NET 10 streaming-rendering documentation](https://learn.microsoft.com/en-us/aspnet/core/blazor/components/rendering?view=aspnetcore-10.0#streaming-rendering)
describes sending placeholders while asynchronous work completes, then patching completed content
into the document. It is not row-at-a-time disposal of a component's render tree. In the deployed
[10.0.8 endpoint implementation](https://github.com/dotnet/aspnetcore/blob/v10.0.8/src/Components/Endpoints/src/RazorComponentEndpointInvoker.cs),
component HTML is written to a buffered writer; asynchronous streaming updates are sent only when
quiescence is incomplete. The [client implementation](https://github.com/dotnet/aspnetcore/blob/v10.0.8/src/Components/Web.JS/src/Rendering/StreamingRendering.ts)
applies templates to the DOM using JavaScript. MUIndex intentionally omits that script.

Adding `[StreamRendering]` to the current synchronous, cached listing would therefore not turn its
900-row loop into bounded batches. Making the whole component grow in batches can also repeat
rendering and transmission of earlier rows. It is not enabled by this fix.

A separate sequential HTML response could render and dispose small row batches, flush them to
[`HttpResponse.BodyWriter`](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/middleware/request-response?view=aspnetcore-10.0),
and preserve a single complete scriptless document. That requires settling headers/status before
flushing, reusing encoding/localization/layout correctly, and measuring retained memory under slow
clients through the proxy. It remains a follow-up experiment, not a measured improvement claimed
here. Ordinary linked pagination would also reduce per-response rows while remaining crawlable,
but changes the listing's user experience.

**Proxy deployment:** `GOMEMLIMIT=96MiB` gives Go a soft collection target below the
256 MiB cgroup kill boundary. Keeping the old 128 MiB hard limit still produced one
OOM kill after about ten minutes despite the soft target, so the hard limit was raised
to provide transient headroom. This is not a guarantee about RSS; monitor OOM events
and working set. Both settings were applied to the production proxy during diagnosis.

**TelnetNegotiationCore:** the deployed version is 2.14.0. MUIndex disposes each
interpreter with `await using`; that version cancels and joins its processing loop
during disposal. The heap evidence points to web rendering, not accumulated Telnet
sessions. No TelnetNegotiationCore change is justified by this incident's evidence.

**Measurement descriptions:** GC heap size includes fragmentation and describes the
last collection, which need not collect all generations. Managed committed bytes
exclude native memory and other cgroup charges. Corrected descriptions avoid claiming
that either number alone is a live-object census or the container's total usage.

An adjacent regression in listing URL cleanup dropped `PathBase`, losing the selected
locale when removing empty filters. Cleanup now preserves it and skips non-listing URLs.

## Diagnostics and verification

Microsoft's `dotnet-counters` and `dotnet-gcdump` are installed in the root-only
`/opt/mui-diagnostics` directory on production. They use a copy of the deployed .NET
runtime in `/opt/mui-diagnostics/runtime`, with `DOTNET_ROOT` pointing there and
`DOTNET_ROLL_FORWARD=Major`. Counter files and the heap graph remain on that server;
no raw access logs or heap payloads were exported into this repository.

To attach after a container recreation, resolve its current host PID with
`docker inspect --format '{{.State.Pid}}' <container>`, find the diagnostic socket under
`/proc/<pid>/root/tmp`, and pass `--diagnostic-port <socket>,connect`. Heap collection
triggers a full GC; use numeric `/metrics` samples for ordinary monitoring.

HTTP regressions cover overloaded renders, shared capacity across locales, permit
release after both success and failure, the rendered 404 path, health/robots access
during overload and locale-preserving redirects. No crawler exclusions were introduced. The full Web and
Catalog suites exercise PostgreSQL; Crawl exercises the pinned protocol dependency.
Deployment validation must check the effective container settings, public probes,
private scrape health, memory and proxy restarts under continuing traffic.
