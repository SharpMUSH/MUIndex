# Owner overrides — design

Extends spec §8.5. Three complaints, one of which turns out to be two:

1. Somebody holding a passkey has no way to get back into their account quickly, and no link to it.
2. Too few facts about a game can be edited — the listed name above all.
3. A verified owner should be able to override what MSSP would otherwise display, across that
   whole scope.

(2) and (3) are one change. (1) is its own, and is first because it is the one that makes the rest
reachable.

## 1. Reaching your account

**There is no account link on this site.** `MainLayout.razor` renders nine navigation items and none
of them is `/account` or `/account/sign-in`. An operator who has claimed a game reaches their own
dashboard by remembering a URL nothing ever showed them. The dashboard is where §8.5's whole write
surface lives, so every feature below is behind a door with no handle.

**And the session does not survive the browser.** `PasskeySignInAsync(submission.Credential)`
requests no persistent cookie, so what Identity issues is a session cookie;
`options.SlidingExpiration = true` then slides a cookie that is gone the moment the window closes.
Sliding expiry on a session cookie is the shape of a bug rather than a policy — it reads as though
somebody meant the sign-in to last and the call site never said so.

The fix is three things, and none of them is a redesign:

- **An account slot in the site header**, beside the mark rather than appended to the catalogue nav —
  that list is already nine items long and a tenth would read as a tenth catalogue. It says *sign in*
  to an anonymous reader and *your games* to a signed-in one, naming them. It is rendered from the
  cascading `HttpContext`, so it costs no query and works with scripting off like everything except
  the ceremony itself.
- **A persistent cookie at sign-in.** An operator administers a game server; asking them to redo a
  WebAuthn ceremony every time they close a tab is friction with nothing on the other side of it.
  The account is worth almost nothing to steal — §8.2 argues this already, and it is the design
  working rather than a shortcut — so the expiry that matters is `ExpireTimeSpan`, not the window.
- **Conditional mediation on the sign-in page.** Sign-in is already usernameless:
  `MakePasskeyRequestOptionsAsync(user: null)` with `ResidentKeyRequirement = "required"`, so the
  browser holds a discoverable credential for this domain and can offer it unprompted. Requesting
  `mediation: 'conditional'` on page load makes a return visit a single gesture instead of a click
  into a ceremony. It degrades to exactly today's behaviour where it is unsupported.

Nothing here touches the ceremony, the credential store or the recovery story. §8.2's recovery path
is unchanged: lose every passkey and the way back is to publish a fresh token on the game, because
the root of trust is the server rather than the credential.

## 2. What a verified owner may write

Today the writable set is four fields — `FANDOM`, `APPLICATION PROCESS`, `RP ENFORCEMENT`,
`CONSENT TOOLS` — chosen in §3.2 because MSSP has no room for them. That is the right *floor* and the
wrong *ceiling*. The argument for the floor was never "an owner cannot be trusted with a genre"; it
was that an owner may never edit a **measurement**. An MSSP report is not a measurement. It is a
game filling in a structured self-description, and §5.1 says so in as many words: `mssp` is a
*report*, and `owner` is *a person typing* — the same kind of fact, produced by the same person,
arriving by a different road.

So the ceiling moves to exactly where the argument puts it: **an owner may override anything MSSP
could have declared, and nothing a probe measured.**

### 2.1 The registry carries three states, not a flag

`FieldDefinition.OwnerEnrichable` becomes `FieldDefinition.OwnerWritable`, of:

| State | Meaning |
|---|---|
| `No` | A measurement, or machinery. Refused out loud, as now. |
| `Enrichment` | MSSP has no such variable. The existing four. |
| `Override` | MSSP has this variable and the owner's answer outranks it. |

`Override` covers the fields a person types into `mush.cnf`: `NAME`, `GENRE`, `SUBGENRE`,
`GAMEPLAY`, `GAMESYSTEM`, `DESCRIPTION`, `STATUS`, `WEBSITE`, `CONTACT`, `DISCORD`, `ICON`,
`LANGUAGE`, `LOCATION`, `MINIMUM AGE`, `CREATED`, `INTERMUD`.

It does **not** cover `HOSTNAME`, `PORT`, `IP`, `IPV6`, `CODEBASE`, `FAMILY`, `CHARSET` or
`CRAWL DELAY`. Those are auto-filled by the codebase and describe the connection rather than the
game, and they are the ones where a hand-typed answer and a measured one mean genuinely different
things. It does not cover `capability.*.declared` either: the capability matrix's whole job is to
show what a game claims beside what its handshake offered, and an owner editing the claimed column
is editing one half of a comparison about themselves.

Three states rather than two because the surfaces need to tell them apart. An `Enrichment` field
appears under "what only you can tell us"; an `Override` field appears under "what your MSSP says,
and what you would rather we showed" with the MSSP value beside the box. Rolling them together would
produce a form that offers an empty box next to a field the game already answers, which reads as an
invitation to fill in something we already have.

**The distinction is presentational and the authorisation is not.** `OwnerEnrichment.ApplyAsync`
gates on `OwnerWritable is not No` and its out-loud refusal is unchanged. There is still exactly one
spelling of what may be written, it is still the registry's own property, and a second caller still
cannot acquire a more generous one.

### 2.2 Nothing about precedence changes

`FieldPrecedence.Winner` orders on the `FieldSource` enum — `Staff, Handshake, Owner, Who, Mssp,
Banner` — with no per-field special case, so `Owner` already outranks `Mssp` everywhere. §5.1's
prose says "`owner` for enrichment-only fields", and the code has never implemented that
qualification because until now no owner-writable field had an MSSP counterpart for it to matter to.
Widening the writable set is what makes the ladder's existing shape load-bearing.

That is a claim worth stating plainly: **the display machinery for this feature already exists and is
already tested.** The change is on the write side.

The rows go on coexisting. `GameField` is keyed `(game, field, source)`, so an override is a row
beside the MSSP row and never instead of it, and the game page's existing "losing sources" rendering
starts doing real work:

> **Genre** — Fantasy · *owner-declared, 3 weeks ago*
> MSSP says *Adventure*, last confirmed 2 days ago

Both are declared, both are labelled, neither is hidden. `FieldSources.IsMeasured` is untouched, so
every chip, every API `state`, and the player-count badge on somebody else's site all keep saying
what they say now.

### 2.3 The listed name needs more than a flag

`game.name` is a denormalised column on the games table. Nothing derives it at read time; it is
written by `CatalogueBinder` at creation and by `SlugMinter.ConsiderAsync` thereafter, and
`SlugMinter` mints from `FieldPrecedence.Winner(NAME)` only after the winning value has held for
`DefaultGrace` — fourteen days, being twice the longest probe interval.

Adding `NAME` to the writable set therefore does nothing visible for two weeks, and then does
something on a crawl cycle rather than on a save. That is the wrong behaviour twice over, and the
grace period's own reasoning says why: it exists because **MSSP flaps** — a name published while a
config was half-edited should not churn a URL. An owner pressing save is not a flap.

So an owner write to `NAME` calls the rename path directly and immediately:

- The new name is applied at once. No grace, because the grace answers a question — "has this
  settled?" — that a deliberate act has already answered.
- `MsspDefaults.IsPlaceholder` is bypassed for owner rows. That filter stops an *unedited* codebase
  publishing its own name from minting a dozen listings called PennMUSH; a name a verified owner
  typed on purpose is edited by definition, and the operator of the PennMUSH development server is
  entitled to call it PennMUSH.
- The slug is re-minted through the same `GameSlug.UniqueAsync` and the old one retires into
  `game_slug_history`, redirecting for ever. §5.7's promise is kept by the same writer that keeps it
  for a measured rename; there is no second rename path.
- `SlugMinter` keeps its grace for MSSP-driven renames, and gains one rule: it does not re-mint from
  an MSSP name while an owner row for `NAME` exists. Otherwise the crawler would spend every cycle
  trying to rename a game back to what its config says, and the owner's override would win the
  display while losing the URL.

An empty owner `NAME` withdraws the override, and the next cycle lets MSSP have the name back under
the ordinary grace. Nothing is deleted: the withdrawal is a new value of a row that goes on
existing, and it reaches the change feed like any other.

## 3. The three places this must not leak

Widening what an owner may assert widens what an owner may assert *about*. Three consumers read
stored fields and must not start reading owner rows.

### 3.1 Identity — the one that would actually bite

`IdentityMatcher.GatherAsync` reads `fields.ForGameAsync(gameId)` and reduces each field through
`FieldPrecedence.Winner`. Since `Owner` outranks `Mssp`, an owner-written `NAME`, `WEBSITE`,
`CONTACT`, `CREATED` or `CODEBASE` would become the value §7.3 scores candidates against — and §7.3
auto-merges above a threshold. An owner could type their way into another game's fingerprint, or out
of a merge with a second address of their own.

**`IdentityMatcher` drops `FieldSource.Owner` rows and keeps the ladder for the rest.**
De-duplication asks which host is which game by comparing what servers said to each other; a person's
typing is not evidence in that question, however true it is. This is the same rule as §5.4's, one
layer down: our record of somebody else's decision may not be laundered into a measurement of theirs.

**`FieldSource.Staff` stays in, and this was a correction.** The first draft of this section excluded
it alongside `Owner` on the grounds that it is also a person typing — but `IdentityMatcherTests`
already asserts, deliberately, that a staff row is what identity compares against. That test is
right. A staff row is the curator's correction; it exists to fix a catalogue that has merged two
games or split one, which makes it precisely the value identity should be steered by, and there is no
surface through which anybody but us can write one. The difference is not that we trust ourselves
more — it is that an owner writes about their own game and a curator writes about the catalogue.

### 3.2 The MSSP scorecard

`MsspLint` scores the game's MSSP report. It goes on scoring the report, not the merged view — an
override on file here is not a field their `mush.cnf` has — and it gains one line where the two
disagree:

> `GENRE` — your MSSP says *Adventure*; you have told us *Fantasy* here. Every other crawler still
> reads the first one.

Said plainly and once, because the alternative is that MUIndex quietly becomes the only place a
game's genre is right, which is a worse outcome for the hobby than the wrong genre. The scorecard's
existing tone holds: it reads an operator their own MSSP back and never calls it a fault.

### 3.3 The auto-listing gate

`CatalogueBinder.MayBeListed` admits a stranger-proposed address only when the live probe carried a
meaningful `NAME` or a `HOSTNAME`. It reads `result.Mssp` — the probe, not the store — so it is
already unreachable from an owner row, and it stays that way. Worth writing down because the failure
mode is a listing minted from a claim on a game that was never listed, and the only thing preventing
it is which object that method reads.

## 4. The icon

`ICON` is an MSSP variable holding a URL, and it is in the override set — so an owner supplies one,
and so does any game that already declares it in `mush.cnf`. **The icon is therefore not an owner
feature.** Every game with a declared `ICON` gets one; claiming a game is how you change it, not how
you get one.

It renders on `/g/<slug>` only. Not in the listing, not in the facet results, not in the ranked
tables — a ranked list where a row's prominence is partly a function of who uploaded artwork is the
first step toward the thing §2 says killed Top Mud Sites.

### 4.1 We fetch it; we do not hot-link it

`<img src="https://their-host/logo.png">` hands every MUIndex reader's IP address, user-agent and
referring URL to a third-party server on every page view. §11 is a section about not doing things
like that to people who did not ask.

So the site fetches the icon itself and serves the bytes from its own origin. Three consequences,
each of which is a real cost stated rather than waved past:

- **This project acquires an HTTP client, which it has never had.** Every network operation in this
  codebase is a raw socket the crawler opens. That is worth noticing rather than slipping in.

  It arrives as a **typed client registered through `IHttpClientFactory`** —
  `services.AddHttpClient<IconFetcher>(…)` — and never as a `new HttpClient()` or a static one. The
  factory is what gives the handler a bounded lifetime, so a DNS record that moves is picked up
  instead of being pinned for the life of the process; on a component whose whole job is to fetch
  URLs strangers control, a socket handler that never re-resolves is the same class of bug as
  §7.2's time-of-check-to-time-of-use gap and would compound it. Configuration goes on the
  registration rather than the call site: a short timeout, no automatic redirects
  (`AllowAutoRedirect = false`, per the bullet below), no cookie container, and a `User-Agent`
  carrying `CrawlerContact.Path` — the same short URL the telnet probe already announces over TTYPE
  and MNES, so an operator reading a web server log and an operator reading a telnet log look up the
  same page.
- **The fetch is of an owner-controlled URL, which is §7.2's exact hazard.** It goes through the same
  gate as every dial: resolve first, refuse unless *every* returned address is globally routable,
  refuse a mixed answer whole rather than picking the good one. The gate is on the resolved address,
  not the name, and its time-of-check-to-time-of-use limitation is as real here as it is there.
  Redirects are not followed — a redirect is a second address the gate did not rule on.
- **The bytes are cached, and the cache is not a fact.** One row per game holding the bytes, the
  content type, the source URL, the fetch time and the ETag. It may be dropped and refilled at any
  time without losing anything, which is what makes it a cache rather than an exception to "nothing
  is ever deleted".

The fetch is scheduled on the crawl cycle, beside the probe, under the same politeness rules — not on
page render, which would make a reader's page load wait on a stranger's web server and would let a
listing page turn into a fan-out of requests to fifty hosts.

### 4.2 What we will and will not serve

- A size ceiling of 256 KB and a dimension ceiling of 512×512, both **refusing** rather than
  truncating or rescaling. We do not resize, re-encode or otherwise rewrite an owner's image: a
  picture quietly altered on the way through is a different fact from the one they published.
- Dimensions and type come from **parsing the header, not from decoding the image**. Each of the four
  formats states its size in the first few dozen bytes, which is all we need — and it means no image
  library, no decoder attack surface reached by an owner-supplied URL, and no licensing question
  about which of them we took. An image whose header we cannot read is one we do not serve.
- `image/png`, `image/jpeg`, `image/gif`, `image/webp`. **SVG is refused**: it is a document that can
  carry script, and serving one from our own origin is a cross-site scripting hole with an image tag
  in front of it.
- The content type we serve is the one we determined from the bytes, never the one the far end
  claimed, with `X-Content-Type-Options: nosniff`.
- **A failed fetch shows nothing and names no cause.** No broken image, no "icon unavailable", no
  entry in the game's public record. That we could not fetch an image is a fact about our afternoon,
  not about the game — §5.4's rule, applied to a picture.

## 5. Schema

One migration, `0013_game_icon.sql`, for the icon cache. Nothing else needs storage: the writable-set
widening is a code change to the registry, and every value it admits is an ordinary `GameField` row
under `FieldSource.Owner` written by the reconciler that already exists.

## 6. Testing

TUnit, in the suites these already live in.

- `FieldRegistryTests` — the writable partition asserted against MSSP's variable list, so a field
  added to the registry without a decision fails the build rather than defaulting to unwritable and
  being noticed by nobody.
- `OwnerEnrichmentPostgresTests` — an override row coexisting with the MSSP row for one field, both
  ages intact, the winner correct; a write to `capability.gmcp.measured` refused out loud and naming
  the field.
- `IdentityMatcherTests` — an owner-written `NAME` matching another game's exactly, and the identity
  score not moving. This is the regression test for §3.1 and it should be written first.
- `SlugMinterTests` — an owner rename applied without grace, the old slug retired and redirecting;
  and a subsequent MSSP name not re-minting over it.
- `OwnerSurfaceTests` — the header slot signed-in and signed-out; the override form rendering the
  MSSP value beside each box.
- Icon: gate-refusal of a private-address URL, an SVG refused, an oversized body refused, a failed
  fetch rendering nothing and writing no field.

## 7. Order of work

One PR, in four commits in this order. The sequence is not a delivery decision — it is a
correctness one, and (2) before (3) especially: the identity fix is a no-behaviour-change refactor
today and a live de-duplication hole the moment the writable set widens.

1. **§1 — reaching your account.** Independent of everything else, and it is the door the rest is
   behind. Header slot, persistent cookie, conditional mediation.
2. **§3.1 — identity reads MSSP rows.** *Before* the writable set widens, not after. Today it is a
   refactor with a test and no behaviour change; the moment §2 lands it is a live de-duplication
   hole, and a hole that exists for one merge window is a merge that cannot be undone.
3. **§2 — the writable set, the three states, and the immediate owner rename.** Carries §3.2's
   scorecard line with it, since that sentence is meaningless until an override can exist.
4. **§4 — the icon.** Last, and separable: it is the only part needing a migration, an HTTP client
   and a new fetch on the crawl cycle, and none of the first three wait on it.

## 8. Not in this round

- Staff overrides of an owner override. `FieldSource.Staff` outranks everything already and has no
  surface; it stays that way until there is a reason.
- Any second writable source, any moderation queue, any review of what an owner typed. An override is
  labelled `owner-declared` on every surface that renders it, which is the whole mechanism: a reader
  can see who said it. Reviewing it would make us the arbiter of a fact we did not measure.
- Icon in the listing, the facets or the rankings. §4.
