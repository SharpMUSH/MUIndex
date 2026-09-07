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

**MUIndex web:** a canonical URL is not a crawl restriction. `robots.txt` now excludes
query-bearing listing URLs in every locale. A process-wide concurrency limiter admits
eight simultaneous Razor renders, without queueing additional requests; excess work
receives a non-cacheable 503 with a one-second retry hint. The permit spans response
writing, including slow clients. Health, metrics, static assets and API routes do not
consume it. The rendered not-found path must pass through the limiter on re-execution.
The limit is an operational starting point, adjustable through
`MUI_PAGE_RENDER_CONCURRENCY`; observe rejection rates and memory when tuning it.

**Proxy deployment:** `GOMEMLIMIT=96MiB` gives Go a soft collection target below the
existing 128 MiB cgroup kill boundary. This is not a guarantee about RSS; monitor OOM
events and working set. The limit was applied to the production proxy during diagnosis.

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
during overload, crawler exclusions and locale-preserving redirects. The full Web and
Catalog suites exercise PostgreSQL; Crawl exercises the pinned protocol dependency.
Deployment validation must check the effective container settings, public probes,
private scrape health, memory and proxy restarts under continuing traffic.
