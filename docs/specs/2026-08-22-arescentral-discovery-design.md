# AresCentral as a discovery source

**Status:** design, approved 2026-08-22
**Scope:** the crawler's discovery phase, the about page's attribution, and one line on a game page.

## 1. What this is

AresCentral is the community hub for AresMUSH games. Its maintainer has issued us API credentials
for `GET https://arescentral.aresmush.com/api/games`, which answers with the games it lists:
`name`, `description`, `hostname`, `port`, `genre`, `website`, `last_ping`, `status`. The list
already excludes games marked In Development and games offline for a long stretch; Alpha and Beta
games are in it.

This is the first source we read by invitation. §7.6's etiquette clause says to ask for a documented
API in preference to scraping; this is that policy having worked, and the design treats it as
materially different from a directory we merely crawl politely.

## 2. What we take

Addresses, and the values the hub holds, written under a new and deliberately weak provenance.

Taking more than addresses is a departure from §7.6, which says a backfill contributes host and port
and nothing else. It is the second such departure — Intermud-3's mudlist was the first (migration
`0023`) — and it is granted for the same reason: §7.6's argument is about *scraped one-time
backfills*, where any game worth listing appears in several directories and "imported from X" records
which fetch happened to run first. A standing, authenticated, single-codebase hub is not that. An
AresMUSH game is on AresCentral or it is not, and the answer is a fact about the game rather than an
accident of our fetch order.

| AresCentral field | Written as | Notes |
|---|---|---|
| `name` | `NAME` | |
| `description` | `DESCRIPTION` | Markdown, stored as given |
| `genre` | `GENRE` | |
| `website` | `WEBSITE` | |
| `status` | `STATUS` | Open / Alpha / Beta |
| — | `CODEBASE` = `AresMUSH` | Inferred from the list's definition, not read from a field |
| `hostname`, `port` | a `crawl_target` | An address to probe, never a field |
| `last_ping` | nothing | See §5 |

### Precedence

New `FieldSource.AresCentral`, inserted **between `Mssp` and `I3Mudlist`**.

Below `Mssp`: a game speaking to us directly, now, beats a hub repeating a claim of unknown age.
Above `I3Mudlist`: AresCentral is authenticated, curated by the codebase's own author, and excludes
dead and in-development games, where the live I3 mudlist carries `test` and `Your MUD Name` beside
the real entries. Never above `Staff` or `Owner` — a human correction wins over a hub, always.

`FieldPrecedence.RankOf` is `(int)source`, so inserting mid-enum renumbers every source below it.
The database stores `source` as text, so this should be inert; implementation must confirm that no
integer form of `FieldSource` is persisted, cached, or serialised on any wire before relying on it.

## 3. Boundaries

### Why this client lives on `main`

`CLAUDE.md` forbids fetchers and third-party parsers on `main`. That rule is about *harvesting a
catalogue*: one-time machinery, pointed at somebody's HTML, that rots in CI for years after the fetch
it existed for. AresCentral is the opposite shape and is the same shape as `MUI.I3`, which is already
on `main` — a standing, documented, authenticated source that is read on a schedule for as long as
the site runs. There is no one-time import here and nothing to move to `import/one-time`.

The `IconFetcher` constraints apply in full, because they are about how we hold an `HttpClient` and
not about what we fetch: a **typed client through `IHttpClientFactory`**, never `new HttpClient()`,
and `AllowAutoRedirect = false` — a redirect is a second address nobody ruled on. The response body
is read to a ceiling rather than trusted to `Content-Length`. Unlike `IconFetcher` the URL here is
ours and constant rather than attacker-chosen, so the DNS-pinning argument is weaker, but the
registration follows the same pattern so there is one way to do this in the tree.

New project `src/MUI.Ares`:

- `AresGamesClient` — a typed `HttpClient`, one method, returns the listing.
- `AresListedGame` — the DTO, one per entry.
- `AresOptions` — base URL, client id, key, timeout.

No reference to `MUI.Catalog` and none to `MUI.Crawl`, matching the arrow `MUI.I3` documents: what
comes back off the network is raw observation, and turning it into catalogue state belongs to
whatever assembles a reading. The practical payoff is that `AresCycle`'s tests run against a captured
JSON body with no network and no database in sight.

In `MUI.Crawler`, beside their I3 counterparts:

- `AresCycle` — one pass.
- `AresService` — a `BackgroundService` gated on its own Postgres advisory lock.

## 4. One pass

1. Fetch the list. A non-200, an authentication failure, or a body that will not parse ends the pass
   having written nothing.
2. For each entry, upsert an `ares_listing` row keyed on (hostname, port).
3. An entry with a blank hostname or a port of 0 or less is recorded and not dialled.
4. Look up `crawl_target` by (host, port). If absent, add one with `IsOperatorSeed = false`, so
   `HostScopeGuard` rules on it exactly as it does on a stranger's `REFERRAL`. `DiscoveredVia` is
   `ares_central` (§6).
5. If the target already carries a `GameId`, write the fields from §2 and record the binding on the
   listing row.
6. Sweep: a (hostname, port) we listed last pass and did not see this pass gets `delisted_at`
   stamped. Nothing is deleted, and a delisting does not end our listing — §7.4's promise that a
   game dark for years is still probed weekly is unaffected by a third party's opinion.

The sweep runs only after a wholly successful fetch. A truncated or empty response must never read
as "everyone left".

### `last_ping`

Stored on the listing row, shown in the pass log, and reaching nothing else — not availability, not
archive grace, not the probe schedule. §7.6 forbids importing another prober's history, and a game
we cannot reach must not look reachable on the strength of someone else's check.

## 5. Persistence

Three migrations, numbered against production at the time they are written — numbers collide across
worktrees here, and `mui_migration` keys on filename.

1. **`ares_listing`** — (hostname, port) primary key; the declared values as fetched; `last_ping`;
   `first_seen_at`, `last_listed_at`, `delisted_at`, `game_id` nullable.
2. **Source vocabulary** — widen the `source` CHECK on `game_field` and on `field_change` to admit
   `ares_central`, in the shape of `0023_i3_field_source.sql`. `field_change` takes the same
   widening, or a source it cannot spell is not a change it can log.
3. **`discovered_via`** — a nullable text column with a CHECK vocabulary on `crawl_target` and on
   `game`.

## 6. How a game got here

A new `DiscoverySource`: `operator_seed`, `submission`, `referral`, `i3_mudlist`, `ares_central`,
`backfill`.

Set once, when a `crawl_target` row is created, by each of the places that create one —
`CrawlerService` (operator seed), `Submission`, `ReferralGraphWriter`, `I3Cycle`, and now
`AresCycle`. `ICrawlTargetRepository.AddAsync` already collapses onto an existing row and changes
nothing but depth, so a second source finding the same address later cannot overwrite the first.
`CatalogueBinder` copies it onto the `game` row when it mints one, next to `first_seen_at`, in the
same way a submission's date already travels — so the page reads one column rather than taking a
`min()` across targets.

**Every row that exists today gets `NULL`**, including every address the one-time backfill seeded.
`NULL` renders nothing. Nothing is backfilled by guessing.

### On the page

One faint line, under the address block on the game page, next to the dates that are already there:

> First seen via AresCentral, 22 August 2026

A separate message id per source, so each gets an honest sentence rather than a shared template with
a noun slotted in. `backfill` says "in the day-one address backfill" and names no directory, because
§7.6 means we genuinely do not record which one.

**§7.6 is amended, not ignored.** Its objection — that a single-origin field presents an accident as
a fact — is answered by what the line says rather than by rejecting the feature. "First seen via X on
this date" is a dated statement about our crawl. It does not claim the game originated there, does
not claim exclusivity, and cannot be read as a badge, because the game may well be listed in four
other places we also read. The date is the honest part and the date is what is shown.

## 7. About page

AresCentral joins `AboutPage.Attribution`'s credited sources.

`ImportSourceState` gains a third member. Today it is `Read` or `Withheld`, rendered by a ternary;
the new state says a source is read continuously through an API whose maintainer issued us
credentials. That distinction is the whole point of §7.6's etiquette clause and is invisible if
AresCentral is filed under the same word as a site we scraped. The ternary becomes a switch, and the
new state gets its own message id.

New message ids: the source note, and the state's wording. Both translated for de, ja, nl and
zh-Hans, per the project's standing i18n rule.

**Conflict to resolve before merge:** the unmerged branch
`chore/about-page-drop-sources-and-licence` deletes the Sources section entirely. These two cannot
both land as written.

## 8. Credentials and configuration

`MUI_ARES_CLIENT_ID` and `MUI_ARES_API_KEY` through `compose.yaml`, alongside `MUI_I3_API_KEY`. The
header is `Authorization: Bearer <client id>:<api key>`.

`AresServiceOptions.Enabled` defaults **on**, unlike I3's. I3 is off by default because joining the
network registers a name on somebody else's router permanently and must never happen as a side
effect of `compose up`; a GET against a documented API with our own credentials registers nothing.
`Validate()` refuses at startup when enabled with no key, rather than discovering it as an
authentication failure on a timer.

Interval: hourly. The list moves on the order of days.

`mui-crawl --ares` forces a pass and honours the existing `--dry-run`.

## 9. Errors

`AresService` never faults out of `ExecuteAsync`; it logs and retries on the lease interval, exactly
as `I3Service` does. The commonest failure is a credential problem or the hub being down, and
neither is a reason to take the site down.

## 10. Testing

Test-first throughout.

`AresCycle`, against captured JSON with a fake client:

- seeds an address it has not seen;
- collapses onto an existing target rather than duplicating it;
- writes fields only once a game exists, never before;
- refuses to seed a port of 0, and records the entry anyway;
- stamps `delisted_at` when an entry disappears;
- **writes nothing at all when the fetch fails, and does not sweep.**

`AresGamesClient`: the `Bearer id:key` header shape, and a malformed body.

`DiscoverySource`: each of the five creation sites stamps its own value; a second source finding a
known address does not overwrite the first; a `NULL` renders no line.

Postgres integration: the three migrations, and the listing repository.

## 11. The agent brief has to change with it

Two entries in `CLAUDE.md`'s **Never** list are written against exactly what this design does, and
both are amended here rather than left to contradict the code:

- *"Import a value, or record where a game came from."* Both halves are now qualified. Importing a
  value stays forbidden for a **one-time scraped backfill**, and is permitted for a **standing
  authenticated source** under its own weak provenance — which is what `0023` already granted I3 and
  what §2 grants AresCentral. Recording where a game came from stays forbidden as an **origin claim
  about the game**, and is permitted as a **dated statement about our own crawl** — the distinction
  §6 turns on.
- *"No fetchers, no HTML parsers for third-party sites."* Unchanged in substance; the brief's
  existing carve-out section gains AresCentral beside `IconFetcher`, with §3's reasoning.

A design that needs a rule relaxed says so in the rule's own file. Leaving `CLAUDE.md` asserting the
opposite of the shipped code is how the next person re-litigates this from scratch.

## 12. Out of scope

Anything that reads `status` as a lifecycle signal — an AresCentral `Alpha` does not touch our own
`LifecycleState`. That is a real question and it is a separate one.
