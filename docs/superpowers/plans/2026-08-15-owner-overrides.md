# Owner Overrides Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give a verified owner a reachable account and the power to override anything MSSP could
declare about their game — including its listed name and its icon — without letting them touch a
measurement.

**Architecture:** The precedence ladder already ranks `FieldSource.Owner` above `FieldSource.Mssp`
unconditionally, so the read half of this exists. The work is on the write side: a three-state
`OwnerWritable` on `FieldDefinition`, a direct rename path for `NAME`, and an identity matcher taught
to ignore rows a person typed. The icon is a cached server-side fetch through §7.2's address gate,
never a hot-link.

**Tech Stack:** .NET 10, ASP.NET Core, Blazor SSR, Npgsql + Dapper (no EF Core), TUnit on
Microsoft.Testing.Platform, PostgreSQL.

**Spec:** `docs/specs/2026-08-15-owner-overrides-design.md`

## Global Constraints

- **.NET 10**, `TreatWarningsAsErrors=true` solution-wide. A warning fails the build.
- **`dotnet test` does not work.** Run each suite directly:
  `dotnet run -c Release --no-build --project tests/MUI.<Suite>.Tests </dev/null`. Keep the
  `</dev/null>`.
- Five suites: `MUI.Catalog.Tests`, `MUI.Crawl.Tests`, `MUI.Crawler.Tests`, `MUI.Discovery.Tests`,
  `MUI.Web.Tests`.
- **Stage by explicit path. Never `git add -A` or `git commit -a`** — several agents share this repo.
- `.editorconfig`: file-scoped namespaces, 4-space C#, LF endings.
- **An owner may never edit a measurement.** The writable set *is* the field registry's own property;
  it is never restated at a call site.
- **Never record a decision of ours as a measurement of theirs.** A failed icon fetch writes no field
  and names no cause.
- British spelling in prose and comments, matching the codebase (`authorisation`, `recognise`).
- Branch: `feat/owner-overrides`. One PR, four commits, in the order below.

---

### Task 1: An account link in the header, and a session that survives the browser

**Files:**
- Modify: `src/MUI.Web/Components/Layout/MainLayout.razor`
- Modify: `src/MUI.Web/Accounts/Passkeys.cs` (the `/passkey/sign-in` endpoint)
- Modify: `src/MUI.Web/wwwroot/passkey.js`
- Test: `tests/MUI.Web.Tests/OwnerSurfaceTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: nothing later tasks depend on. Independent and first because it is the door the rest is
  behind.

- [ ] **Step 1: Write the failing tests**

Two, in `OwnerSurfaceTests.cs`, following the harness the file already uses:

```csharp
[Test]
public async Task TheHeaderOffersSignInToAReaderWithNoAccount()
{
    var html = await Site.GetStringAsync("/");

    await Assert.That(html).Contains("/account/sign-in");
}

[Test]
public async Task TheHeaderNamesTheOperatorItIsSignedInAs()
{
    var html = await SignedIn().GetStringAsync("/");

    await Assert.That(html).Contains("/account");
    await Assert.That(html).Contains("corvid-admin");
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet run -c Release --project tests/MUI.Web.Tests </dev/null`
Expected: FAIL — the layout renders no `/account` link at all.

- [ ] **Step 3: Add the account slot to the header**

In `MainLayout.razor`, after the `<nav aria-label="Catalogues">` block and inside `<header class="site">`.
It is its own slot rather than a tenth nav item: that list is nine catalogues and a tenth entry would
read as a tenth catalogue.

```razor
<nav class="account" aria-label="Your account">
    @if (HttpContext?.User is { Identity: { IsAuthenticated: true, Name: { Length: > 0 } name } })
    {
        <a href="/account">your games <span class="faint">@name</span></a>
    }
    else
    {
        <a href="/account/sign-in">sign in</a>
    }
</nav>
```

with the cascading parameter in `@code`:

```razor
@code {
    [CascadingParameter]
    private HttpContext? HttpContext { get; set; }
}
```

- [ ] **Step 4: Make the sign-in cookie persistent**

In `Passkeys.cs`, the `/passkey/sign-in` endpoint calls `PasskeySignInAsync(submission.Credential)`
and requests no persistence, so Identity issues a session cookie and `SlidingExpiration` slides
something that dies with the window. Pass the persistence flag the overload takes. **Verify the
overload's actual signature against the installed 10.0 assembly before writing this** — if
`SignInManager.PasskeySignInAsync` takes no `isPersistent`, sign in through it and then re-issue with
`SignInManager.SignInAsync(user, isPersistent: true)`, and leave a comment saying which it was.

Add beside `ConfigureApplicationCookie`:

```csharp
// An operator administers a game server; asking them to redo a WebAuthn ceremony every time they
// close a tab is friction with nothing on the other side of it. §8.2 already argues the account is
// worth almost nothing to steal, so the expiry that matters is this one and not the window.
options.ExpireTimeSpan = TimeSpan.FromDays(30);
```

- [ ] **Step 5: Offer the passkey without a click**

In `passkey.js`, after the form wiring, request conditional mediation on the sign-in page. It
degrades to exactly today's behaviour where unsupported, so it is guarded and never awaited on the
critical path.

```js
  // A returning operator's browser already holds a discoverable credential for this domain
  // (ResidentKeyRequirement is "required" at registration), so it can offer it unprompted rather
  // than waiting for a click into the ceremony. Unsupported browsers just do nothing here.
  const offerSilently = async (form) => {
    if (!(await PublicKeyCredential.isConditionalMediationAvailable?.())) {
      return;
    }

    const optionsJson = await post('/account/passkey/assertion-options');
    const credential = await navigator.credentials.get({
      publicKey: PublicKeyCredential.parseRequestOptionsFromJSON(optionsJson),
      mediation: 'conditional',
    });

    const result = await post('/account/passkey/sign-in', { credential: serialise(credential) });
    window.location.assign(result.redirect);
  };
```

called for the sign-in form only, with failures swallowed — a conditional request that finds no
credential is not an error worth showing anybody.

- [ ] **Step 6: Run the Web suite**

Run: `dotnet run -c Release --project tests/MUI.Web.Tests </dev/null`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/MUI.Web/Components/Layout/MainLayout.razor src/MUI.Web/Accounts/Passkeys.cs \
        src/MUI.Web/wwwroot/passkey.js tests/MUI.Web.Tests/OwnerSurfaceTests.cs
git commit -m "A door with a handle, and a session that outlives the window"
```

---

### Task 2: Identity stops reading what a person typed

**Files:**
- Modify: `src/MUI.Discovery/IdentityMatcher.cs:140-152` (`StoredAsync`)
- Test: `tests/MUI.Discovery.Tests/IdentityMatcherTests.cs`

**Interfaces:**
- Consumes: `FieldSources.IsMeasured(FieldSource)` — the existing single spelling of the
  measured/declared line, in `src/MUI.Catalog/Provenance.cs`.
- Produces: no signature change. `StoredAsync` keeps its shape; only which rows it considers changes.

**This task must land before Task 3.** Today it is a refactor with a test and no behaviour change,
because no owner-writable field has an MSSP counterpart. The moment Task 3 lands it is a live
de-duplication hole, and §7.3 auto-merges above threshold — a bad merge is not undoable.

- [ ] **Step 1: Write the failing test**

```csharp
[Test]
public async Task AnOwnerCannotTypeTheirGameIntoAnotherGamesIdentity()
{
    // A verified owner of one game writes the exact NAME and CREATED another game measured. If
    // identity read the precedence winner, Owner outranks Mssp and this scores MsspNameAndCreated
    // against a game it has nothing to do with — and §7.3 merges above threshold.
    var impostor = await AGame(mssp: new() { ["NAME"] = "Something Else" });
    await Owner(impostor).Declares("NAME", "Corvid Court").And("CREATED", "2004");

    var verdict = await Matcher.ResolveAsync(
        AProbeOf("corvid.example.org", 4201, name: "Corvid Court", created: "2004"), default);

    await Assert.That(verdict).IsTypeOf<IdentityVerdict.Fresh>();
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet run -c Release --project tests/MUI.Discovery.Tests </dev/null`
Expected: FAIL — the verdict is `Merge`, because `FieldPrecedence.Winner` hands back the owner row.

- [ ] **Step 3: Read only what a server said**

Replace the body of `StoredAsync`, and replace its doc comment — the existing one argues *for* the
precedence winner and would now be a comment saying the opposite of the code:

```csharp
    /// <summary>
    /// One value per field, considering only rows that came off a socket or out of a game's own
    /// report — never one a person typed.
    /// </summary>
    /// <remarks>
    /// <b>De-duplication asks which host is which game, and a person's typing is not evidence in that
    /// question</b> however true it happens to be. <see cref="FieldSource.Owner"/> outranks
    /// <see cref="FieldSource.Mssp"/> in <see cref="FieldPrecedence"/> — correctly, for a page, where
    /// an owner's answer about their own game is the one to show — so a matcher reading the precedence
    /// winner would let a verified owner type their game into another game's fingerprint, or out of a
    /// merge with a second address of their own. <see cref="FieldSource.Staff"/> is excluded with it
    /// and for the same reason: it is us typing, and §7.3 weighs servers against servers.
    ///
    /// Within what remains the ladder still applies, so <c>banner_hash</c> and the claim-token beacon
    /// are unaffected and the handshake still outranks the report.
    /// </remarks>
    private async Task<IReadOnlyDictionary<string, string>> StoredAsync(Guid gameId, CancellationToken ct)
    {
        var rows = await fields.ForGameAsync(gameId, ct);

        return rows
            .Where(row => FieldSources.IsMeasured(row.Source) || row.Source is FieldSource.Mssp)
            .GroupBy(row => row.Field, StringComparer.OrdinalIgnoreCase)
            .Select(group => FieldPrecedence.Winner(group))
            .Where(winner => winner is not null)
            .ToDictionary(winner => winner!.Field, winner => winner!.Value, StringComparer.OrdinalIgnoreCase);
    }
```

- [ ] **Step 4: Run the Discovery suite**

Run: `dotnet run -c Release --project tests/MUI.Discovery.Tests </dev/null`
Expected: PASS, including every pre-existing matcher test — this is a no-behaviour-change refactor
until Task 3 lands, and any pre-existing test that breaks is telling you something.

- [ ] **Step 5: Commit**

```bash
git add src/MUI.Discovery/IdentityMatcher.cs tests/MUI.Discovery.Tests/IdentityMatcherTests.cs
git commit -m "Which host is which game is a question about servers"
```

---

### Task 3: An owner may override anything MSSP could declare

**Files:**
- Modify: `src/MUI.Catalog/Fields.cs` (`FieldDefinition`, and a new `OwnerWritable` enum)
- Modify: `src/MUI.Catalog/Persistence/FieldRegistry.cs` (the `Build()` table and the
  `OwnerEnrichable` projection)
- Modify: `src/MUI.Catalog/Persistence/OwnerEnrichment.cs` (`ApplyAsync`'s gate, and a rename hook)
- Modify: `src/MUI.Crawler/SlugMinter.cs` (do not re-mint over an owner name)
- Modify: `src/MUI.Catalog/MsspLint.cs` (the disagreement line)
- Modify: `src/MUI.Web/Components/OwnerPanel.razor` (two field groups)
- Test: `tests/MUI.Catalog.Tests/Persistence/FieldRegistryTests.cs`,
  `tests/MUI.Catalog.Tests/Persistence/OwnerEnrichmentPostgresTests.cs`,
  `tests/MUI.Crawler.Tests/SlugMinterTests.cs`

**Interfaces:**
- Consumes: `FieldDefinition(string Name, TimeSpan ExpectedRefresh, bool OwnerEnrichable)` as it
  stands today; `IGameStore.RenameAsync(Guid, string, string, DateTimeOffset, CancellationToken)`
  returning the retired slug or null; `GameSlug.UniqueAsync(string, Func<string, CancellationToken,
  Task<bool>>, CancellationToken)`.
- Produces:
  - `enum OwnerWritable { No, Enrichment, Override }` in `MUI.Catalog`.
  - `FieldDefinition(string Name, TimeSpan ExpectedRefresh, OwnerWritable OwnerWritable = OwnerWritable.No)`
    — the `bool OwnerEnrichable` parameter is **replaced**, not added to.
  - `FieldRegistry.OwnerEnrichable` and `FieldRegistry.OwnerOverridable`, both
    `IReadOnlyList<FieldDefinition>`, for the two form groups.
  - `OwnerEnrichment.ApplyAsync` unchanged in signature.

- [ ] **Step 1: Write the failing registry test**

Replace `TheOwnerEnrichableFieldsAreTheOnesMsspCannotExpress` with two tests, keeping its argument
and extending it:

```csharp
[Test]
public async Task TheEnrichmentFieldsAreTheOnesMsspCannotExpress()
{
    var enrichment = FieldRegistry.All
        .Where(f => f.OwnerWritable is OwnerWritable.Enrichment)
        .Select(f => f.Name)
        .ToList();

    await Assert.That(enrichment).IsEquivalentTo(
        new[] { "FANDOM", "APPLICATION PROCESS", "RP ENFORCEMENT", "CONSENT TOOLS" });
}

[Test]
public async Task NothingAProbeMeasuresIsWritable()
{
    // The line §8.5 draws, asserted rather than trusted to the table's layout. A capability's
    // measured side is the handshake's answer, and PLAYERS and UPTIME are the staleness anchors —
    // none of the three is a thing an owner may assert.
    var writable = FieldRegistry.All
        .Where(f => f.OwnerWritable is not OwnerWritable.No)
        .Select(f => f.Name)
        .ToList();

    await Assert.That(writable).DoesNotContain("PLAYERS");
    await Assert.That(writable).DoesNotContain("UPTIME");
    await Assert.That(writable.Where(CapabilityFields.IsMeasured)).IsEmpty();
    await Assert.That(writable).DoesNotContain(InternalFields.ConnectScreen);
    await Assert.That(writable).DoesNotContain(InternalFields.BannerHash);
}

[Test]
public async Task TheOverridableFieldsAreTheHandTypedHalfOfMssp()
{
    var overridable = FieldRegistry.All
        .Where(f => f.OwnerWritable is OwnerWritable.Override)
        .Select(f => f.Name)
        .ToList();

    // What a person types into mush.cnf. Not the connection-describing set the codebase fills in
    // for them, where a hand-typed answer and a measured one mean different things.
    await Assert.That(overridable).IsEquivalentTo(new[]
    {
        "NAME", "GENRE", "SUBGENRE", "GAMEPLAY", "GAMESYSTEM", "DESCRIPTION", "STATUS",
        "WEBSITE", "CONTACT", "DISCORD", "ICON", "LANGUAGE", "LOCATION", "MINIMUM AGE",
        "CREATED", "INTERMUD",
    });
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet run -c Release --project tests/MUI.Catalog.Tests </dev/null`
Expected: FAIL — `OwnerWritable` does not exist.

- [ ] **Step 3: Add the three-state property**

In `src/MUI.Catalog/Fields.cs`, replacing the `bool OwnerEnrichable` parameter:

```csharp
/// <summary>
/// Whether a verified owner may write a field, and on what grounds (spec §8.5).
/// </summary>
/// <remarks>
/// Three states rather than a flag because the two writable ones are different offers and the
/// dashboard has to make them differently. <see cref="Enrichment"/> is a field MSSP has no variable
/// for, so the form asks an open question; <see cref="Override"/> is one it does, so the form shows
/// what the game already says and asks whether the owner would rather we showed something else. One
/// state would produce an empty box beside a field we already have an answer for, which reads as an
/// invitation to retype it.
///
/// <b>The distinction is presentational and the authorisation is not.</b> Everything that is not
/// <see cref="No"/> is writable, and that predicate lives in <see cref="OwnerEnrichment"/> alone.
/// </remarks>
public enum OwnerWritable
{
    /// <summary>A measurement, or machinery. Refused out loud.</summary>
    No,

    /// <summary>MSSP has no such variable. The owner is the only possible source.</summary>
    Enrichment,

    /// <summary>MSSP has this variable, and the owner's answer outranks the game's report of it.</summary>
    Override,
}
```

and:

```csharp
public sealed record FieldDefinition(
    string Name,
    TimeSpan ExpectedRefresh,
    OwnerWritable OwnerWritable = OwnerWritable.No);
```

- [ ] **Step 4: Widen the registry table**

In `FieldRegistry.Build()`, change the local `Add` and mark the sixteen:

```csharp
        void Add(string name, TimeSpan window, OwnerWritable writable = OwnerWritable.No) =>
            fields.Add(new FieldDefinition(name, window, writable));
```

`NAME`, `CONTACT`, `WEBSITE`, `DISCORD`, `ICON`, `CREATED`, `LANGUAGE`, `LOCATION`, `MINIMUM AGE`,
`GENRE`, `GAMEPLAY`, `GAMESYSTEM`, `SUBGENRE`, `STATUS`, `INTERMUD`, `DESCRIPTION` take
`OwnerWritable.Override`; the four enrichment fields take `OwnerWritable.Enrichment`. `PLAYERS`,
`UPTIME`, `CRAWL DELAY`, `HOSTNAME`, `PORT`, `CODEBASE`, `IP`, `IPV6`, `CHARSET`, `FAMILY`, the
connect-screen fields and every capability row are left alone.

Add beside the `Override` block the comment carrying the argument, since the next reader's first
question is why this is not a self-report:

```csharp
        // §8.5's ceiling, moved to where its own argument puts it. The rule is that an owner may
        // never edit a MEASUREMENT — and an MSSP report is not one. §5.1: `mssp` is a game filling in
        // a self-description it maintains, and `owner` is a person typing. Same kind of fact, same
        // person, different road. What stays unwritable is everything a probe observed.
```

Replace the projection:

```csharp
    public static IReadOnlyList<FieldDefinition> OwnerEnrichable { get; } =
        [.. All.Where(definition => definition.OwnerWritable is OwnerWritable.Enrichment)];

    public static IReadOnlyList<FieldDefinition> OwnerOverridable { get; } =
        [.. All.Where(definition => definition.OwnerWritable is OwnerWritable.Override)];
```

- [ ] **Step 5: Widen the gate**

In `OwnerEnrichment.ApplyAsync`, one line:

```csharp
            if (registry.Find(edit.Field) is not { OwnerWritable: not OwnerWritable.No })
```

and update the class remarks: the paragraph asserting an owner's `GENRE` "could not overwrite MSSP's
even if one were writable" is now false and must be replaced with the standing rule — that the two
rows still coexist, that the owner's wins on display, and that both are labelled.

- [ ] **Step 6: Run the Catalog suite**

Run: `dotnet run -c Release --project tests/MUI.Catalog.Tests </dev/null`
Expected: PASS.

- [ ] **Step 7: Write the failing rename tests**

In `tests/MUI.Crawler.Tests/SlugMinterTests.cs`:

```csharp
[Test]
public async Task AnOwnerRenameTakesEffectWithoutWaitingOutTheGrace()
{
    // The grace answers "has this settled?", which a deliberate act has already answered.
    var game = await AGame(name: "PennMUSH", mssp: new() { ["NAME"] = "PennMUSH" });
    await Owner(game).Declares("NAME", "Corvid Court");

    var renamed = await Games.ByIdAsync(game.Id);

    await Assert.That(renamed!.Name).IsEqualTo("Corvid Court");
    await Assert.That(renamed.Slug).IsEqualTo("corvid-court");
    await Assert.That(await Slugs.RetiredByAsync("pennmush")).IsEqualTo(game.Id);
}

[Test]
public async Task AnOwnerMayNameTheirGameAfterItsCodebase()
{
    // IsPlaceholder stops an UNEDITED codebase minting a dozen listings called PennMUSH. A name a
    // verified owner typed on purpose is edited by definition, and the operator of the PennMUSH
    // development server is entitled to call it PennMUSH.
    var game = await AGame(name: "mush.pennmush.org:4201");
    await Owner(game).Declares("NAME", "PennMUSH");

    await Assert.That((await Games.ByIdAsync(game.Id))!.Name).IsEqualTo("PennMUSH");
}

[Test]
public async Task TheCrawlerDoesNotRenameAGameBackToWhatItsConfigSays()
{
    var game = await AGame(name: "Corvid Court");
    await Owner(game).Declares("NAME", "Corvid Court");
    await Fields.Upsert(game.Id, "NAME", FieldSource.Mssp, "PennMUSH", ageInDays: 60);

    await Assert.That(await Minter.ConsiderAsync(game.Id, Now)).IsNull();
}
```

- [ ] **Step 8: Run to verify they fail**

Run: `dotnet run -c Release --project tests/MUI.Crawler.Tests </dev/null`
Expected: FAIL — an owner `NAME` write renames nothing, and `ConsiderAsync` happily re-mints.

- [ ] **Step 9: Rename on an owner write**

`game.name` is a denormalised column written only by `SlugMinter`, so the flag alone does nothing for
fourteen days and then acts on a crawl cycle rather than on a save. Give `OwnerEnrichment` an
optional collaborator that performs the rename, so `MUI.Catalog` gains no reference to `MUI.Crawler`
(the one-way arrow in CLAUDE.md) — the interface lives in `MUI.Catalog` and the crawler's minter is
one implementation:

```csharp
/// <summary>Applies a name a verified owner chose, at once (spec §5.7, §8.5).</summary>
/// <remarks>
/// Separate from <see cref="OwnerEnrichment"/> because a rename re-mints a URL and retires the old
/// one, and the writer that keeps §5.7's promise for a measured rename must be the one that keeps it
/// here — a second rename path is a second place for "every slug redirects for ever" to be almost
/// true. Optional on the enrichment service for the same reason <c>ClaimService</c> is optional on
/// <c>CrawlCycle</c>: a deployment with no catalogue behind it should do less, not refuse to run.
/// </remarks>
public interface IOwnerRenames
{
    Task<string?> ApplyAsync(Guid gameId, string name, CancellationToken cancellationToken = default);
}
```

`OwnerEnrichment.ApplyAsync` calls it after a successful reconcile when the edits contained a
non-empty `NAME`. An empty `NAME` withdraws the override and renames nothing now — the next cycle
gives MSSP the name back under the ordinary grace.

In `SlugMinter`, implement it by extracting the mint-and-rename half of `ConsiderAsync` (the
`GameSlug.UniqueAsync` call, `RenameAsync`, the race `catch`, the log line) into a private
`MintAsync(GameRecord, string, DateTimeOffset, CancellationToken)` that both entry points call. Do
not duplicate it.

- [ ] **Step 10: Stop the crawler renaming over an owner**

In `SlugMinter.ConsiderAsync`, after reading `stored` and before computing the name:

```csharp
        // An owner has said what this game is called, and it outranks the report on every surface
        // (§5.1). Re-minting from MSSP here would spend every cycle trying to rename the game back to
        // what its config says — the override winning the page and losing the URL.
        if (stored.Any(row => row.Source is FieldSource.Owner
                && string.Equals(row.Field, IdentityMsspVariables.Name, StringComparison.OrdinalIgnoreCase)
                && row.Value.Length > 0))
        {
            return null;
        }
```

and in the owner path, bypass `MsspDefaults.MeaningfulName` — an owner's name is used as typed.

- [ ] **Step 11: Run Crawler and Catalog suites**

Run both. Expected: PASS.

- [ ] **Step 12: Pin the auto-listing gate (spec §3.3)**

`CatalogueBinder.MayBeListed` reads `result.Mssp` — the live probe, not the store — so it is already
unreachable from an owner row. Nothing to change; add the test that keeps it that way, because the
failure mode is a listing minted from a claim on a game that was never listed, and the only thing
preventing it is which object that method reads:

```csharp
[Test]
public async Task AnOwnerRowCannotListAGameAStrangerProposed()
{
    // §7.2's cost, accepted there: a stranger-proposed address lists itself only when the PROBE
    // carries a meaningful name. An owner writing one into the catalogue is not that.
    var game = await AGame(submitted: true, mssp: []);
    await Owner(game).Declares("NAME", "Corvid Court");

    await Assert.That(await Queries.IsPubliclyListedAsync(game.Id)).IsFalse();
}
```

- [ ] **Step 13: The scorecard's disagreement line**

In `MsspLint`, add a check that compares the MSSP value against an owner row where both exist. It
scores the **report**, not the merged view — an override on file here is not a field their
`mush.cnf` has — and says so once:

> `GENRE` — your MSSP says *Adventure*; you have told us *Fantasy* here. Every other crawler still
> reads the first one.

The existing tone holds: it reads an operator their own MSSP back and never calls it a fault.

- [ ] **Step 14: Two groups on the dashboard**

In `OwnerPanel.razor`, split the single `@foreach (var definition in FieldRegistry.OwnerEnrichable)`
into two sections. The enrichment group keeps today's copy. The override group iterates
`FieldRegistry.OwnerOverridable` and renders, beside each box, what MSSP currently says — which means
the panel needs the game's MSSP rows as a second parameter, `IReadOnlyDictionary<string, GameField>
Reported`, fetched by `Account.razor` in the same read that fetches `Declared`.

Copy for the override group, which has to be honest about what an override is and is not:

```razor
    <h3>What your game reports, and what you would rather we showed</h3>

    <p class="faint">
        Your MSSP is what every crawler reads, and we go on showing it beside anything you put here —
        an answer of yours does not hide one of your game's. Nothing measured can be edited from here:
        not a player count, not a capability, not an hour of reachability. If a line below is wrong in
        your <code>mush.cnf</code>, fixing it there fixes it everywhere.
    </p>
```

- [ ] **Step 15: Run the Web suite, then commit**

```bash
git add src/MUI.Catalog/Fields.cs src/MUI.Catalog/Persistence/FieldRegistry.cs \
        src/MUI.Catalog/Persistence/OwnerEnrichment.cs src/MUI.Catalog/MsspLint.cs \
        src/MUI.Crawler/SlugMinter.cs src/MUI.Web/Components/OwnerPanel.razor \
        src/MUI.Web/Components/Pages/Account.razor tests/
git commit -m "An MSSP report is not a measurement, and the ceiling moves to say so"
```

---

### Task 4: The icon

**Files:**
- Create: `migrations/0013_game_icon.sql`
- Create: `src/MUI.Web/Icons/IconFetcher.cs`, `src/MUI.Web/Icons/ImageHeader.cs`,
  `src/MUI.Web/Icons/IconEndpoint.cs`
- Create: `src/MUI.Catalog/Persistence/NpgsqlIconStore.cs`
- Modify: `src/MUI.Web/SiteComposition.cs` (the typed-client registration)
- Modify: `src/MUI.Web/Components/Pages/Game.razor` (render it)
- Test: `tests/MUI.Web.Tests/IconTests.cs`, `tests/MUI.Catalog.Tests/Persistence/IconStoreTests.cs`

**Interfaces:**
- Consumes: `IHostScopeGuard` / `HostScopeGuard.RuleOnAsync` from `MUI.Discovery` — §7.2's address
  gate, reused rather than re-derived; `CrawlerContact.Path`.
- Produces: `GET /g/{slug}/icon` serving bytes or 404.

- [ ] **Step 1: Write the failing header-parsing tests**

Dimensions and type come from the header, not a decoder: no image library, no decoder attack surface
reached by an owner-supplied URL, and no licensing question about which library we took.

```csharp
[Test]
[Arguments("png", "image/png", 16, 16)]
[Arguments("jpeg", "image/jpeg", 32, 24)]
[Arguments("gif", "image/gif", 8, 8)]
[Arguments("webp", "image/webp", 64, 64)]
public async Task TheHeaderNamesTheTypeAndTheSize(string fixture, string type, int width, int height)
{
    var read = ImageHeader.Read(Fixtures.Bytes(fixture));

    await Assert.That(read!.ContentType).IsEqualTo(type);
    await Assert.That(read.Width).IsEqualTo(width);
    await Assert.That(read.Height).IsEqualTo(height);
}

[Test]
public async Task AnSvgIsNotAnImageWeWillServe()
{
    // A document that can carry script. Serving one from our own origin is a cross-site scripting
    // hole with an image tag in front of it.
    await Assert.That(ImageHeader.Read("<svg xmlns=\"http://www.w3.org/2000/svg\"/>"u8.ToArray()))
        .IsNull();
}

[Test]
public async Task WeDoNotTrustTheTypeTheFarEndClaimed()
{
    // Content-Type said PNG; the bytes are a GIF. The bytes decide, and nosniff goes on the response.
    await Assert.That(ImageHeader.Read(Fixtures.Bytes("gif"))!.ContentType).IsEqualTo("image/gif");
}
```

- [ ] **Step 2: Run to verify they fail, then implement `ImageHeader`**

`ImageHeader.Read(ReadOnlySpan<byte>) → ImageHeader?` with
`record ImageHeader(string ContentType, int Width, int Height)`. Magic bytes plus the size field for
each of the four; anything else returns null.

- [ ] **Step 3: Write the failing fetch tests**

```csharp
[Test]
public async Task AnIconUrlResolvingToAPrivateAddressIsNotFetched()
{
    // §7.2, and the same gate as every dial: resolve first, refuse unless every returned address is
    // globally routable, refuse a mixed answer whole rather than picking the good one.
    await Assert.That(await Fetcher.FetchAsync(AGame, "http://169.254.169.254/logo.png")).IsNull();
    await Assert.That(Http.Requests).IsEmpty();
}

[Test]
public async Task ARedirectIsNotFollowed()
{
    // A redirect is a second address the gate did not rule on.
    Http.Responds(302, location: "http://10.0.0.5/logo.png");

    await Assert.That(await Fetcher.FetchAsync(AGame, "https://corvid.example.org/logo.png")).IsNull();
}

[Test]
public async Task AnOversizedBodyIsRefusedRatherThanTruncated()
{
    Http.Responds(200, "image/png", Bytes(256 * 1024 + 1));

    await Assert.That(await Fetcher.FetchAsync(AGame, "https://corvid.example.org/logo.png")).IsNull();
}

[Test]
public async Task AFailedFetchSaysNothingAboutTheGame()
{
    // §5.4's rule, applied to a picture: that we could not fetch an image is a fact about our
    // afternoon, not about the game. No field, no cause on the page, no broken image.
    Http.Responds(500);

    await Fetcher.FetchAsync(AGame, "https://corvid.example.org/logo.png");

    await Assert.That(await Fields.ForGameAsync(AGame)).IsEmpty();
    await Assert.That(await Site.GetStringAsync($"/g/{Slug}")).DoesNotContain("icon");
}
```

- [ ] **Step 4: Implement the fetcher as a typed client**

Registered through `IHttpClientFactory`, never `new HttpClient()` and never a static one — the
factory is what gives the handler a bounded lifetime, so a DNS record that moves is picked up rather
than pinned for the life of the process. On a component whose whole job is fetching URLs strangers
control, a socket handler that never re-resolves is the same class of bug as §7.2's
time-of-check-to-time-of-use gap and compounds it.

```csharp
services.AddHttpClient<IconFetcher>(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(10);
        client.MaxResponseContentBufferSize = IconFetcher.MaxBytes;
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"mu*index/1.0 (+https://{site}{CrawlerContact.Path})");
    })
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
    });
```

- [ ] **Step 5: The migration**

```sql
-- The cache behind §4 of the owner-overrides design. This is the one table here that may be dropped
-- and refilled without losing anything, because it holds no fact: the fact is the ICON field, which
-- is a URL, and these are bytes we fetched from it. "Nothing is ever deleted" is a rule about a
-- game's record, and a cache is not one.
CREATE TABLE game_icon (
    game_id      uuid PRIMARY KEY REFERENCES game (id) ON DELETE CASCADE,
    source_url   text        NOT NULL,
    content_type text        NOT NULL,
    width        int         NOT NULL,
    height       int         NOT NULL,
    bytes        bytea       NOT NULL,
    etag         text,
    fetched_at   timestamptz NOT NULL
);
```

- [ ] **Step 6: Fetch on the crawl cycle, not on render**

Beside the probe, under the same politeness rules. Not on page render, which would make a reader's
page load wait on a stranger's web server and turn a listing page into a fan-out of requests to fifty
hosts.

- [ ] **Step 7: Serve and render**

`GET /g/{slug}/icon` returns the cached bytes with the content type **we determined**, never the one
the far end claimed, plus `X-Content-Type-Options: nosniff` and a long `Cache-Control`. `Game.razor`
renders it on the game page only — not the listing, not the facets, not the rankings: a ranked list
whose rows are partly prominent by who uploaded artwork is the first step toward the thing §2 says
killed Top Mud Sites. No icon, no element.

- [ ] **Step 8: Run every suite, then commit**

```bash
git add migrations/0013_game_icon.sql src/MUI.Web/Icons/ src/MUI.Catalog/Persistence/NpgsqlIconStore.cs \
        src/MUI.Web/SiteComposition.cs src/MUI.Web/Components/Pages/Game.razor tests/
git commit -m "An icon we fetch ourselves, because hot-linking spends a reader's address"
```

---

### Task 5: The whole thing, green, as one PR

- [ ] **Step 1:** `dotnet build MUIndex.slnx -c Release` — clean, warnings-as-errors.
- [ ] **Step 2:** All five suites, each with `</dev/null`.
- [ ] **Step 3:** Update `CLAUDE.md`'s §8.5 summary if it names the four enrichment fields.
- [ ] **Step 4:** `gh pr create` against `main`.
