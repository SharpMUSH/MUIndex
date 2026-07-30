# Probe Engine Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the four-layer MU\* probe (spec §6) so that one telnet connection to one game yields one immutable `ProbeResult` — measured handshake capabilities, the connect screen, a structurally-parsed `WHO`, and MSSP — plus a `mui-probe` console that runs it against a real host.

**Architecture:** `MUI.Crawl` owns transport, telnet, parsing and aggregation and references nothing of ours. A probe opens one `TcpTransport`, wraps it in a `NegotiationRecorder` decorator that sniffs `IAC WILL/DO` off the inbound byte stream (layer 1), drives `TelnetNegotiationCore` over it to get framing-stripped text (layers 2 and 3) and MSSP (layer 4), and assembles one `ProbeResult`. Player names exist only inside the probe: what leaves is `PresenceAggregates` — salted hashes and bucket counts. Everything downstream consumes `ProbeResult` and never sees a socket.

**Tech Stack:** .NET 10 (`net10.0`), C# latest, TUnit on Microsoft.Testing.Platform, `TelnetNegotiationCore` 2.7.0, `Microsoft.Extensions.Logging.Abstractions`, `System.Text.Json`.

**Depends on: nothing. This is the first plan; it produces the `ProbeResult` every later plan consumes.**

---

## No external prerequisite — this plan is blocked on nothing

An earlier draft of this plan opened with a **blocking** prerequisite: a shared `SharpMU.Mssp`
package, extracted from SharpMUTerm, that Task 1 was to consume and without which CI could not go
green. **That decision is reversed and the block is gone.** Nothing was ever published —
`https://api.nuget.org/v3-flatcontainer/sharpmu.mssp/index.json` returns `BlobNotFound`, verified —
and the repository that would have produced it is archived. There is no package, there will be no
package, and **no code is shared with SharpMUTerm**.

**MUIndex implements its own crawler end to end.** The MSSP domain types this plan needs are
MUIndex's own, in namespace `MUI.Crawl.Mssp`, written against MUIndex's own tests (Tasks 1 and 2).
The type *names* `MsspData`, `MsspHost`, `MsspHostScope` and `MsspVariables` are the ones the
cross-plan contract already used, so for every later plan this reversal is a changed `using` and
nothing else.

The only external dependency involved is **`TelnetNegotiationCore` 2.7.0** — on nuget.org, and
first-party (see CLAUDE.md), so a gap in it is a PR rather than a workaround. It already parses
MSSP's telnet option 70, which is why **this plan contains no subnegotiation parser and must never
grow one**.

There is nothing to wait for. Start at Task 1.

---

## Global Constraints

These apply to every task in this plan without being repeated.

- **Target framework `net10.0`**, and `TreatWarningsAsErrors` is `true` solution-wide
  (`Directory.Build.props`). A build with a warning is a failed build.
- **Tests are TUnit on Microsoft.Testing.Platform** (`Exe` projects). **`dotnet test` does not
  work** — .NET 10 dropped VSTest. Run each suite directly and keep the `</dev/null`, which
  detaches stdin so the test host does not hang waiting on it:
  ```bash
  dotnet build MUIndex.slnx -c Release
  dotnet run -c Release --no-build --project tests/MUI.Crawl.Tests </dev/null
  ```
- **Assertion idiom:** `await Assert.That(actual).IsEqualTo(expected);` — tests are
  `[Test] public async Task Name()` on a plain `public class`, no attributes on the class.
- **`.editorconfig`:** file-scoped namespaces, 4-space C# indentation, LF line endings, 2-space
  indentation for `csproj`/`props`/`json`/`yml`/`slnx`.
- **`MUI.Catalog` must NEVER reference `MUI.Crawl`.** The writers that consume a probe result must
  not know a socket exists; that one-way arrow is what keeps every downstream behaviour testable
  against a captured `ProbeResult` fixture with no network involved.
- **Never persist player names.** `WHO` is parsed in memory; aggregates use salted hashes with a
  rotating salt, so a unique-player estimate is possible while re-identification across salt epochs
  is not.
- **Parsers never fabricate, and "we did not ask" is not "we could not read it".** A `WHO` that was
  sent and could not be parsed yields `WhoConfidence.Unknown`, never zero — that is spec §5.4's
  hatched cell. A `WHO` that was never sent yields `WhoConfidence.NotAttempted`, which is the enum's
  **zero value**, so a default-constructed `WhoReading` claims nothing. Collapsing those two is what
  left `PresenceWriter` unable to tell §5.4's own named bug case from never having asked.
- **Vocabulary is "reachable", never "uptime"** — schema, API, code and copy alike (spec §5.7).
- **Branch from `main`, open a PR, never commit directly to `main`.**
- **Any new test project goes into `MUIndex.slnx` AND `.github/workflows/ci.yml`**, which runs each
  suite as its own explicit step.
- **The MSSP domain is ours, in `MUI.Crawl.Mssp`; the wire is TelnetNegotiationCore's.** TNC **2.7.0**
  parses option 70's subnegotiation and hands back an ordered name → value-list map
  (`MSSPConfig.Variables`, `MSSPVariableCollection`), canonicalises variable names so `MINIMUM_AGE`
  and `MINIMUM AGE` are one variable (`MSSPVariables.Canonicalize`), knows which names the
  specification defines (`IsOfficial`/`IsKnown`/`Official`), and reads flags and integers
  (`Flag`, `Integer`, `MSSPValue.TryParseFlag`). **None of that is re-implemented here, and there is
  no subnegotiation parser in this plan.** What Tasks 1 and 2 add on top is the four domain readings
  the library does not have — `CRAWL DELAY`'s `-1`, ports validated as ports, `REFERRAL` read as
  crawlable hosts, and an immutable snapshot — plus `MsspPlaintextReply`, the out-of-band
  `MSSP-REQUEST` text protocol, which is not a telnet option and which TNC knows nothing about.
- **Persistence is PostgreSQL 17 with Npgsql + Dapper and plain numbered `.sql` migration files
  applied by a small idempotent runner. No EF Core**, ever. Integration tests use
  `Testcontainers.PostgreSql`.

---

## The one deliberate divergence from SharpMUTerm's crawler: this probe speaks

SharpMUTerm's `TelnetMsspProbe` sends **nothing but telnet negotiation**, and its
`NothingButTelnetNegotiationIsEverSentToTheServer` test asserts that against the raw outbound bytes.
That is the right rule for an MSSP-only crawler, and it is **not** the rule here.

Spec §6.3 requires layer 3: `WHO` / `DOING` at the connect screen, because on Penn, MUX, Rhost and
the TinyMUD family it is a *live* count where MSSP `PLAYERS` is whatever the codebase last cached —
and because §3.1 establishes that a large slice of MSSP is hand-typed text that rots. A directory
whose whole claim is "measured, not asserted" cannot decline to take the live measurement.

So this probe sends, at most, **three lines**, all at an unauthenticated login screen, all documented
public commands that the server answers to anonymous connections by design:

| Line | Why | Sent when |
|---|---|---|
| `WHO` | spec §6.3 | `ProbeOptions.SendWho` (default true) |
| `DOING` | spec §6.3 — the same layer, second spelling | only if `WHO` yielded `WhoConfidence.Unknown` |
| `MSSP-REQUEST` | spec §6.4 — the protocol's own plaintext fallback | only if telnet option 70 yielded nothing |

Why it is still polite: it is one line at a screen the server prints to every anonymous connection;
the client names itself in TTYPE/MTTS with an info URL before it says anything (Task 8); it is
rate-limited by `CRAWL DELAY` upstream (Plan 3); and it stops there. **The probe never logs in, never
sends a password, never sends a command that is not in the table above, and never sends anything at
all after the transcript it asked for.** Task 15 carries `TheProbeSendsNothingButTheThreeDocumentedLines`,
which asserts that against the bytes the scripted server actually received — the same shape of test
SharpMUTerm has, with the allowance made explicit rather than assumed.

---

## Where the contract stands after the addendum

`/tmp/.../CONTRACT-ADDENDUM.md` **supersedes CONTRACT.md wherever they disagree**, and two of its
changes land in this plan rather than downstream:

- **The MSSP model moved here.** `MsspData`, `MsspHost`, `MsspHostScope` and `MsspVariables` keep
  their names and their shape, and change namespace to `MUI.Crawl.Mssp`. The addendum's §2b listing
  is their signature and is used verbatim (Tasks 1 and 2). `MsspSubnegotiationParser` is **deleted
  outright** — TNC parses option 70 — and its plaintext half becomes
  `MsspPlaintextReply.TryParse(string, out MsspData)`, which is the only MSSP *parsing* MUIndex owns.
- **`WhoReading` gains a fourth state, `WhoConfidence.NotAttempted`, fixed at source here** rather
  than worked around in Plan 2. `WhoReading.Unread` is gone; `WhoReading.NotAttempted` and
  `WhoReading.Unreadable` replace it, `WasAttempted` is new, and `HasCount` is corrected (Task 3).
  Plan 2's `PresenceWriter` then reads intent directly instead of inferring it from `MsspVia`.

Everything else in CONTRACT.md is used verbatim. This plan adds the following, which neither document
names, because they specify the seam and not the machinery behind it. Later plans do not consume any
of them except `ProbeResultJson`.

| Added type | Where | Why |
|---|---|---|
| `MUI.Crawl.Mssp.MsspPlaintextReply` | `src/MUI.Crawl/Mssp/` | Named in the addendum, absent from CONTRACT.md. The out-of-band `MSSP-REQUEST` text protocol (spec §6.4) — the half of MSSP that is not a telnet option and so not TNC's. |
| `MUI.Crawl.Telnet.ProbeTelnetSession` | `src/MUI.Crawl/Telnet/` | `ProbeSession`'s contract signature takes a transport factory, so something has to drive `TelnetNegotiationCore` over it. Trimmed from SharpMUTerm's `TelnetSession`. |
| `MUI.Crawl.BoundedTranscript` | `src/MUI.Crawl/` | Layer 2 and layer 3 both need a size-capped text sink. Named for what it is rather than `BannerCollector`, because the WHO transcript uses it too. |
| `MUI.Crawl.Who.AnsiText` | `src/MUI.Crawl/Who/` | The banner is stored ANSI-intact; the parser needs it stripped. One place, so the two never disagree. |
| `MUI.Crawl.Who.ColumnLayout` | `src/MUI.Crawl/Who/` | The structural column detector — the heart of §6.3 and worth its own unit tests. |
| `MUI.Crawl.PresenceAggregateBuilder` | `src/MUI.Crawl/` | Turns a `WhoTable` into `PresenceAggregates`. The contract names both ends and not the hinge. |
| `MUI.Crawl.ProbeResultJson` | `src/MUI.Crawl/` | The fixture format. **Plan 2 consumes this**; its exact output is pinned in Task 17. |

One contract *type* is extended rather than added: `ProbeOptions` gains
`public int MaxCaptureBytes { get; init; } = 64 * 1024;`. Every other member is verbatim. Spec §13
requires surviving an enormous banner, and a cap that is not configurable cannot be tested at a
sane size.

Two behavioural notes that are choices, not transcriptions:

- **The info URL rides the first TTYPE answer** as `MUINDEX (+https://muindex.org/crawler)`.
  Spec §11 asks for TTYPE/MTTS **and** MNES `CLIENT_NAME`; TelnetNegotiationCore 2.7.0 registers no
  NEW-ENVIRON plugin in `AddDefaultMUDProtocols` and exposes no client-side environment send, so
  MNES is not reachable from here. The `(+url)` form is the HTTP `User-Agent` convention, it lands in
  the field a server operator's log actually records, and it costs nothing. A client-side MNES sender
  is a good upstream PR; until then this is the honest half.
- **`handshake_stalled` is produced by `ProbeSession`, not by `FailureClassifier`.** The classifier
  maps exceptions; a server that accepts the connection and then says nothing at all throws nothing.
  That case is spec §13's fourth misbehaviour and §5.3's named cause, and Task 15 owns it.

---

## Prerequisite: TelnetNegotiationCore's MSSP decoding fix

MSSP over telnet option 70 is decoded by the library, and a fix to how it decodes non-ASCII bytes
is landing upstream before this plan is implemented. TelnetNegotiationCore is first-party, so a gap
in it is a PR rather than something MUIndex compensates for. **This plan builds no defence around
it and pins no test to the pre-fix behaviour** — assume the fix has shipped, and if it has not,
raise it upstream rather than working around it here.

---

## File Structure

```
Directory.Packages.props                        TelnetNegotiationCore 2.6.0 → 2.7.0
MUIndex.slnx                                    + src/MUI.Probe.Cli
.github/workflows/ci.yml                        (unchanged: Crawl suite already has a step)

src/MUI.Crawl/
  MUI.Crawl.csproj                              (unchanged: TNC is already referenced)
  Mssp/
    MsspHost.cs             (new)               REFERRAL's host+port, and §7.2's crawlability gate
    MsspVariables.cs        (new)               the variable names our accessors read by
    MsspData.cs             (new)               immutable projection over TNC's collection — §6.4
    MsspPlaintextReply.cs   (new)               the out-of-band MSSP-REQUEST reply — §6.4
  ProbeResult.cs            (modified)          the seam — §6.5, and WhoReading's four states
  ProbeOptions.cs           (new)               ProbeTarget, ProbeOptions, IProbe
  ProbeFailureCauses.cs     (new)               the cause vocabulary crossing the project boundary
  BoundedTranscript.cs      (new)               size-capped text sink, layers 2 and 3
  FailureClassifier.cs      (new)               exception → FailureDetail
  PresenceAggregates.cs     (new)               PresenceAggregates, PresenceBuckets
  Salt.cs                   (new)               ISaltProvider, RotatingSaltProvider, PlayerHash
  PresenceAggregateBuilder.cs (new)             WhoTable → PresenceAggregates
  ProbeResultJson.cs        (new)               fixture format; Plan 2 reads it
  ProbeSession.cs           (new)               one connection, four layers, hard-bounded
  Transport/
    ITransport.cs           (new)               lifted from SharpMUTerm
    ConnectionOptions.cs    (new)               lifted from SharpMUTerm
    TcpTransport.cs         (new)               lifted from SharpMUTerm
    TelnetOptionNames.cs    (new)               option byte → name
    NegotiationRecorder.cs  (new)               layer 1 — §6.1
  Telnet/
    ProbeTelnetSession.cs   (new)               TelnetNegotiationCore over an ITransport
  Who/
    AnsiText.cs             (new)               ANSI stripping
    ColumnLayout.cs         (new)               structural column detection
    WhoParser.cs            (new)               §6.3 — WhoParser, WhoTable, WhoRow

src/MUI.Probe.Cli/
  MUI.Probe.Cli.csproj      (new)               <AssemblyName>mui-probe</AssemblyName>
  Program.cs                (new)

tests/MUI.Crawl.Tests/
  MUI.Crawl.Tests.csproj    (modified)          + Fixtures content
  Support/
    TelnetWire.cs           (new)               IAC constants + MSSP subnegotiation builder
    ScriptedMuServer.cs     (new)               real TcpListener; §13's misbehaviour switches
    WhoCorpus.cs            (new)               the seven-plus real-shaped WHO fixtures
    FixtureLibrary.cs       (new)               loads Fixtures/*.json
  MsspHostTests.cs          (new)               referral parsing + the §7.2 gate
  MsspDataTests.cs          (new)               the four domain readings, and nothing TNC already does
  MsspPlaintextReplyTests.cs (new)              §6.4's out-of-band reply
  WhoReadingTests.cs        (modified)          the four states, and the two that used to be one
  ProbeResultShapeTests.cs  (new)
  TransportTests.cs         (new)
  ScriptedMuServerTests.cs  (new)
  MisbehaviourTests.cs      (new)
  NegotiationRecorderTests.cs (new)
  ProbeTelnetSessionTests.cs (new)
  BannerCaptureTests.cs     (new)
  WhoParserTests.cs         (new)
  WhoParserPropertyTests.cs (new)
  PresenceAggregateTests.cs (new)
  MsspLayerTests.cs         (new)
  FailureClassifierTests.cs (new)
  ProbeSessionTests.cs      (new)
  ProbeResultJsonTests.cs   (new)
  FixtureTests.cs           (new)
  Fixtures/{pennmush,tinymux,rhostmush,evennia,diku}.json (new)
```

---

### Task 1: `MsspHost` — a referral read as a host, and the §7.2 crawlability gate

Spec §6.4 (MSSP is the payload), §7.2 ("referrals are candidate hostnames, not facts"). Contract
addendum §2b.

**This is the security-relevant type in this plan.** A `REFERRAL` list is hand-maintained
configuration on somebody else's server, and following it is how a crawler is aimed. `IsCrawlable`
is the gate: false for loopback, RFC 1918, RFC 6598 carrier-grade NAT, IPv6 unique-local
(`fc00::/7`), link-local — which includes `169.254.169.254`, the cloud metadata address — and
multicast. That is spec §7.2's verify-don't-trust, and it is what stops a stranger's referral list
pointing our crawler at our own network. Plan 3 refuses to follow anything this says no to; nothing
downstream re-derives the judgement.

The second half of the type is duller and just as load-bearing: **normalising equality**, so
`MUD.Example.ORG.`, `mud.example.org` and `mud.example.org:4201` are one entry rather than three, and
`2001:0DB8:0000::0001` and `2001:db8::1` are one address rather than two. Without it a crawl walks
the same server once per spelling its peers happen to use, and cycle detection never closes.

**Prior art read, not copied.** SharpMUTerm's `src/SharpMUTerm.Core/Telnet/Mssp/MsspHost.cs` solves
the same problem and is worth reading before writing this. It is **not** a dependency and its code is
not lifted: this is MUIndex's own type in MUIndex's namespace, and it deliberately handles two cases
that one does not — a bracketed literal with a colon separator (`[2001:db8::1]:4201`), and an
IPv4-mapped IPv6 address (`::ffff:127.0.0.1`), which is a loopback referral wearing IPv6 clothes.

**Files:**
- Create: `src/MUI.Crawl/Mssp/MsspHost.cs`
- Test: `tests/MUI.Crawl.Tests/MsspHostTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `MUI.Crawl.Mssp.MsspHostScope` (`Unresolved`, `Global`, `Loopback`, `Private`,
  `LinkLocal`, `Multicast`); `sealed record MUI.Crawl.Mssp.MsspHost` with `Host`, `Port`, `Scope`,
  `IsIpV6`, `IsCrawlable`, `static MsspHost? Create(string?, int)`,
  `static bool TryParse(string?, out MsspHost?)`, `string ToReferralString()`.
  Task 2's `MsspData.Referrals` is a list of these; Plan 3's `ReferralGraphWriter` gates on
  `IsCrawlable` and keys its cycle detection on the record's equality.

- [ ] **Step 1: Write the failing test**

Create `tests/MUI.Crawl.Tests/MsspHostTests.cs`:

```csharp
using MUI.Crawl.Mssp;

namespace MUI.Crawl.Tests;

/// <summary>
/// <c>REFERRAL</c> read as a host: the format the MSSP specification defines, the spellings real
/// servers use instead, and the addresses a crawler must refuse to be aimed at (spec §7.2).
/// </summary>
public class MsspHostTests
{
    private static MsspHost Parse(string value)
    {
        MsspHost.TryParse(value, out var host);
        return host ?? throw new InvalidOperationException($"'{value}' should have parsed.");
    }

    [Test]
    public async Task TheSpecifiedFormatIsHostSpacePort()
    {
        // "using the host port format … Make sure to separate the host and port with a space rather
        // than : because IPv6 addresses contain colons."
        var host = Parse("mud.example.org 4000");

        await Assert.That(host.Host).IsEqualTo("mud.example.org");
        await Assert.That(host.Port).IsEqualTo(4000);
        await Assert.That(host.Scope).IsEqualTo(MsspHostScope.Unresolved);
        await Assert.That(host.ToReferralString()).IsEqualTo("mud.example.org 4000");
    }

    [Test]
    public async Task AnIpV6ReferralIsCanonicalisedSoTwoSpellingsAreOneAddress()
    {
        // The exact reason the specification chose a space rather than a colon.
        var verbose = Parse("2001:0DB8:0000:0000:0000:0000:0000:0001 4201");
        var compact = Parse("2001:db8::1 4201");

        await Assert.That(verbose.Host).IsEqualTo("2001:db8::1");
        await Assert.That(verbose).IsEqualTo(compact);
        await Assert.That(verbose.IsIpV6).IsTrue();
        await Assert.That(verbose.ToString()).IsEqualTo("[2001:db8::1]:4201");
        await Assert.That(verbose.ToReferralString()).IsEqualTo("2001:db8::1 4201");
    }

    [Test]
    public async Task ABracketedLiteralIsAcceptedWithEitherSeparator()
    {
        // URLs bracket an IPv6 literal, and servers copy their own connection string into REFERRAL.
        // The brackets are punctuation for a colon problem this format does not have, so they go —
        // but while they are there they make the colon form unambiguous, so it is accepted too.
        await Assert.That(Parse("[2001:db8::1] 4201").Host).IsEqualTo("2001:db8::1");
        await Assert.That(Parse("[2001:db8::1]:4201").Host).IsEqualTo("2001:db8::1");
        await Assert.That(Parse("[2001:db8::1]:4201").Port).IsEqualTo(4201);
        await Assert.That(MsspHost.TryParse("[2001:db8::1", out _)).IsFalse();
    }

    [Test]
    public async Task TheColonFormIsToleratedOnlyWhereThereIsExactlyOneColon()
    {
        // Real servers emit host:port despite the specification, and refusing it loses referrals for
        // nothing…
        await Assert.That(Parse("mud.example.org:4000").Port).IsEqualTo(4000);

        // …but an unbracketed string with two or more colons is far more likely to be a bare IPv6
        // address than a host:port pair, and splitting it at a colon would silently rewrite it into a
        // different address. Those are rejected rather than guessed at.
        await Assert.That(MsspHost.TryParse("2001:db8::1", out _)).IsFalse();
        await Assert.That(MsspHost.TryParse("2001:db8::1:4201", out _)).IsFalse();
    }

    [Test]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments("mud.example.org")]
    [Arguments("mud.example.org 0")]
    [Arguments("mud.example.org 65536")]
    [Arguments("mud.example.org -1")]
    [Arguments("mud.example.org notaport")]
    [Arguments("http://mud.example.org/ 4000")]
    [Arguments("a stale line someone typed")]
    public async Task AMalformedReferralIsRejectedRatherThanGuessedAt(string value) =>
        await Assert.That(MsspHost.TryParse(value, out _)).IsFalse();

    [Test]
    public async Task HostNamesAreNormalisedSoOneServerIsOneEntry()
    {
        // Identity is what cycle detection and deduplication are built on. Case, the root label's
        // trailing dot, and the separator a peer happened to use are all spelling.
        var spellings = new[]
        {
            "MUD.Example.ORG 4201",
            "mud.example.org 4201",
            "mud.example.org. 4201",
            "  mud.example.org\t4201  ",
            "mud.example.org:4201",
        };

        await Assert.That(spellings.Select(Parse).ToHashSet().Count).IsEqualTo(1);
    }

    [Test]
    public async Task ADifferentPortOnTheSameHostIsADifferentEntry()
    {
        // One machine hosting two games is ordinary, and merging them would lose one.
        await Assert.That(Parse("mud.example.org 4201")).IsNotEqualTo(Parse("mud.example.org 4202"));
    }

    [Test]
    public async Task ALoopbackReferralIsNotCrawlable()
    {
        await Assert.That(Parse("127.0.0.1 4201").Scope).IsEqualTo(MsspHostScope.Loopback);
        await Assert.That(Parse("::1 4201").Scope).IsEqualTo(MsspHostScope.Loopback);
        await Assert.That(Parse("0.0.0.0 4201").Scope).IsEqualTo(MsspHostScope.Loopback);
        await Assert.That(Parse("127.0.0.1 4201").IsCrawlable).IsFalse();
    }

    [Test]
    public async Task PrivateSpaceIncludingCarrierGradeNatIsNotCrawlable()
    {
        await Assert.That(Parse("10.1.2.3 4201").Scope).IsEqualTo(MsspHostScope.Private);
        await Assert.That(Parse("172.16.0.1 4201").Scope).IsEqualTo(MsspHostScope.Private);
        await Assert.That(Parse("172.31.255.254 4201").Scope).IsEqualTo(MsspHostScope.Private);
        await Assert.That(Parse("192.168.1.1 4201").Scope).IsEqualTo(MsspHostScope.Private);

        // RFC 6598, 100.64.0.0/10 — a carrier-grade NAT range, and not RFC 1918, so a check that
        // only knew the three classic blocks would follow it.
        await Assert.That(Parse("100.64.0.1 4201").Scope).IsEqualTo(MsspHostScope.Private);
        await Assert.That(Parse("100.127.255.254 4201").Scope).IsEqualTo(MsspHostScope.Private);

        // 172.15 and 172.32 are outside RFC 1918 and are ordinary public addresses.
        await Assert.That(Parse("172.15.0.1 4201").Scope).IsEqualTo(MsspHostScope.Global);
        await Assert.That(Parse("172.32.0.1 4201").Scope).IsEqualTo(MsspHostScope.Global);
        await Assert.That(Parse("100.63.0.1 4201").Scope).IsEqualTo(MsspHostScope.Global);
    }

    [Test]
    public async Task IpV6UniqueLocalAndLinkLocalAreNotCrawlable()
    {
        // fc00::/7 is IPv6's RFC 1918, and it is spelled by two leading bytes rather than a prefix
        // a switch on the first octet would catch.
        await Assert.That(Parse("fd00::1 4201").Scope).IsEqualTo(MsspHostScope.Private);
        await Assert.That(Parse("fc00::1 4201").Scope).IsEqualTo(MsspHostScope.Private);
        await Assert.That(Parse("fe80::1 4201").Scope).IsEqualTo(MsspHostScope.LinkLocal);
        await Assert.That(Parse("ff02::1 4201").Scope).IsEqualTo(MsspHostScope.Multicast);
        await Assert.That(Parse("fd00::1 4201").IsCrawlable).IsFalse();
    }

    [Test]
    public async Task TheCloudMetadataAddressIsNotCrawlable()
    {
        // 169.254.169.254 is the one that matters. A referral to it is either a misconfiguration or
        // an attempt to make somebody else's crawler read credentials out of its own instance
        // metadata, and following it would be both.
        var metadata = Parse("169.254.169.254 80");

        await Assert.That(metadata.Scope).IsEqualTo(MsspHostScope.LinkLocal);
        await Assert.That(metadata.IsCrawlable).IsFalse();
    }

    [Test]
    public async Task AnIpV4MappedIpV6AddressIsClassifiedAsTheAddressItActuallyIs()
    {
        // ::ffff:127.0.0.1 is loopback wearing IPv6 clothes, and a classifier that read only the
        // address family would call it globally routable. This is the bypass, and it is one line.
        var mapped = Parse("::ffff:127.0.0.1 4201");

        await Assert.That(mapped.Scope).IsEqualTo(MsspHostScope.Loopback);
        await Assert.That(mapped.IsCrawlable).IsFalse();
        await Assert.That(Parse("::ffff:10.0.0.1 4201").Scope).IsEqualTo(MsspHostScope.Private);
        await Assert.That(Parse("::ffff:198.51.100.7 4201").Scope).IsEqualTo(MsspHostScope.Global);
    }

    [Test]
    public async Task MulticastAndBroadcastAreNotCrawlable()
    {
        await Assert.That(Parse("224.0.0.1 4201").Scope).IsEqualTo(MsspHostScope.Multicast);
        await Assert.That(Parse("255.255.255.255 4201").Scope).IsEqualTo(MsspHostScope.Multicast);
        await Assert.That(Parse("239.1.2.3 4201").IsCrawlable).IsFalse();
    }

    [Test]
    public async Task ANameAndAGloballyRoutableLiteralAreTheOnlyCrawlableThings()
    {
        // Unresolved is crawlable because a name is exactly what DNS is for: refusing it would refuse
        // every real referral. What DNS then answers is checked when the socket is opened, not here.
        var crawlable = new[] { MsspHostScope.Unresolved, MsspHostScope.Global };
        var representative = new Dictionary<MsspHostScope, MsspHost>
        {
            [MsspHostScope.Unresolved] = Parse("mud.example.org 4201"),
            [MsspHostScope.Global] = Parse("198.51.100.7 4201"),
            [MsspHostScope.Loopback] = Parse("127.0.0.1 4201"),
            [MsspHostScope.Private] = Parse("10.0.0.1 4201"),
            [MsspHostScope.LinkLocal] = Parse("169.254.169.254 80"),
            [MsspHostScope.Multicast] = Parse("224.0.0.1 4201"),
        };

        // Every scope the enum has is answered for. A scope added later fails this test rather than
        // quietly defaulting into "crawlable", which is the direction a mistake here goes.
        await Assert.That(representative.Keys).IsEquivalentTo(Enum.GetValues<MsspHostScope>());

        foreach (var (scope, host) in representative)
        {
            await Assert.That(host.Scope).IsEqualTo(scope);
            await Assert.That(host.IsCrawlable).IsEqualTo(crawlable.Contains(scope)).Because($"{scope}");
        }
    }

    [Test]
    public async Task CreateRefusesAPortOutsideOneToSixtyFiveThousandFiveHundredAndThirtyFive()
    {
        await Assert.That(MsspHost.Create("mud.example.org", 0)).IsNull();
        await Assert.That(MsspHost.Create("mud.example.org", -1)).IsNull();
        await Assert.That(MsspHost.Create("mud.example.org", 65536)).IsNull();
        await Assert.That(MsspHost.Create("mud.example.org", 65535)).IsNotNull();
        await Assert.That(MsspHost.Create(null, 4201)).IsNull();
        await Assert.That(MsspHost.Create("   ", 4201)).IsNull();
    }

    [Test]
    public async Task ANameThatCouldNotBeAHostIsRefusedBeforeItReachesAResolver()
    {
        // Deliberately permissive about what a label may contain — underscores and non-ASCII both
        // occur in the wild — and strict only about what a host name can never contain.
        await Assert.That(MsspHost.Create("mud example org", 4201)).IsNull();
        await Assert.That(MsspHost.Create("mud.example.org/status", 4201)).IsNull();
        await Assert.That(MsspHost.Create("user@mud.example.org", 4201)).IsNull();
        await Assert.That(MsspHost.Create(".mud.example.org", 4201)).IsNull();
        await Assert.That(MsspHost.Create("mud..example.org", 4201)).IsNull();
        await Assert.That(MsspHost.Create(new string('a', 254), 4201)).IsNull();
        await Assert.That(MsspHost.Create("_minecraft._tcp.example.org", 4201)).IsNotNull();
    }

    [Test]
    public async Task ToReferralStringRoundTripsThroughTryParse()
    {
        foreach (var value in new[] { "mud.example.org 4201", "198.51.100.7 4000", "2001:db8::1 4201" })
        {
            var host = Parse(value);

            await Assert.That(Parse(host.ToReferralString())).IsEqualTo(host);
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `error CS0234: The type or namespace name 'Mssp' does not exist in the namespace 'MUI.Crawl'`.

- [ ] **Step 3: Write the type**

Create `src/MUI.Crawl/Mssp/MsspHost.cs`:

```csharp
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace MUI.Crawl.Mssp;

/// <summary>
/// What kind of address a host names, as far as can be told without resolving it. The crawler uses
/// this to refuse referrals that point inside a network rather than at a public game server.
/// </summary>
public enum MsspHostScope
{
    /// <summary>A name, not a literal — nothing can be told about it until DNS answers.</summary>
    Unresolved,

    /// <summary>A globally routable IP literal.</summary>
    Global,

    /// <summary>127.0.0.0/8, ::1, or an unspecified address.</summary>
    Loopback,

    /// <summary>RFC 1918, RFC 6598 carrier-grade NAT, or IPv6 unique-local space (fc00::/7).</summary>
    Private,

    /// <summary>169.254.0.0/16 or fe80::/10 — including the cloud metadata address.</summary>
    LinkLocal,

    /// <summary>A multicast group or the broadcast address; never a game server.</summary>
    Multicast,
}

/// <summary>
/// A host and port as MSSP names one: the unit a <c>REFERRAL</c> value carries, and the identity a
/// crawler deduplicates on.
/// <para>
/// The specification is explicit about the wire format: a referral uses "the host port format and
/// array notation … Make sure to separate the host and port with a space rather than <c>:</c>
/// because IPv6 addresses contain colons." <see cref="TryParse"/> implements exactly that, and
/// tolerates the two other spellings real servers actually emit (see its remarks).
/// </para>
/// <para>
/// Equality is over the <em>normalised</em> host and the port, which is what makes deduplication and
/// cycle detection work: <c>MUD.Example.ORG.</c>, <c>mud.example.org</c> and <c>MUD.EXAMPLE.ORG</c>
/// are one host, and <c>2001:0DB8:0000::0001</c> and <c>2001:db8::1</c> are one address. Without that
/// a crawl walks the same server once per spelling its peers happen to use.
/// </para>
/// </summary>
public sealed record MsspHost
{
    private MsspHost(string host, int port, MsspHostScope scope, bool isIpV6)
    {
        Host = host;
        Port = port;
        Scope = scope;
        IsIpV6 = isIpV6;
    }

    /// <summary>The normalised host: lower-cased, trailing dot removed, IP literals in canonical form.</summary>
    public string Host { get; }

    /// <summary>The TCP port, 1–65535.</summary>
    public int Port { get; }

    /// <summary>What kind of address <see cref="Host"/> is, as far as a literal reveals.</summary>
    public MsspHostScope Scope { get; }

    /// <summary>True when this is an IPv6 literal, which display has to bracket.</summary>
    public bool IsIpV6 { get; }

    /// <summary>
    /// True when this host is worth a crawler's time: a name, or a globally routable literal.
    /// <para>
    /// <b>This is spec §7.2's gate and the security-relevant member of this type.</b> A referral into
    /// loopback, RFC 1918 or RFC 6598 space, IPv6 unique-local space, or link-local — which includes
    /// <c>169.254.169.254</c>, the cloud metadata address — is either a misconfiguration or an attempt
    /// to make somebody else's crawler probe a network it could not otherwise reach. Neither is worth
    /// following, and the second is the reason this is not merely a tidiness filter.
    /// </para>
    /// <para>
    /// A name is crawlable because a name is what DNS is for; refusing one would refuse every real
    /// referral. What DNS answers is checked when the socket is opened, not here.
    /// </para>
    /// </summary>
    public bool IsCrawlable => Scope is MsspHostScope.Unresolved or MsspHostScope.Global;

    /// <summary>
    /// Builds a host from an already-separated host and port, normalising the host. Returns null when
    /// the host is empty or could not be a host name, or the port is outside 1–65535.
    /// </summary>
    public static MsspHost? Create(string? host, int port)
    {
        if (string.IsNullOrWhiteSpace(host) || port is < 1 or > 65535)
        {
            return null;
        }

        var trimmed = host.Trim();

        // A bracketed IPv6 literal, [2001:db8::1], as a URL spells it. The brackets are punctuation
        // for a colon problem this format does not have, so they are stripped rather than kept.
        if (trimmed.Length > 2 && trimmed[0] == '[' && trimmed[^1] == ']')
        {
            trimmed = trimmed[1..^1].Trim();
        }

        if (trimmed.Length == 0)
        {
            return null;
        }

        if (IPAddress.TryParse(trimmed, out var address))
        {
            // ToString() is the canonical form: it compresses IPv6 zero runs and strips leading
            // zeroes, so two spellings of one address become one key.
            var literal = address.ToString().ToLowerInvariant();
            return new MsspHost(
                literal,
                port,
                Classify(address),
                address.AddressFamily == AddressFamily.InterNetworkV6);
        }

        // A DNS name. The root label's trailing dot is legal and means the same name, so it goes.
        var name = trimmed.TrimEnd('.').ToLowerInvariant();
        return name.Length != 0 && IsPlausibleName(name)
            ? new MsspHost(name, port, MsspHostScope.Unresolved, false)
            : null;
    }

    /// <summary>
    /// Parses one <c>REFERRAL</c> array element.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three forms, in this order. The specification's own — <c>host port</c>, whitespace-separated,
    /// chosen precisely so an IPv6 literal's colons are unambiguous — is the one parsed without
    /// reservation.
    /// </para>
    /// <para>
    /// A <b>bracketed</b> literal (<c>[2001:db8::1] 4201</c>, <c>[2001:db8::1]:4201</c>) carries its
    /// own delimiter, so whichever separator follows it the split is unambiguous. Servers copy their
    /// own connection strings into <c>REFERRAL</c>, and a connection string is a URL.
    /// </para>
    /// <para>
    /// Bare <c>host:port</c> is accepted, but <b>only when the string contains exactly one colon</b> —
    /// which is to say only when it cannot be an IPv6 literal. Real servers emit it despite the
    /// specification and refusing it loses referrals for no gain; but a string with two or more colons
    /// is far more likely to be a bare IPv6 address than a host:port pair, and guessing wrong there
    /// would silently rewrite one address into a different one. Those are rejected.
    /// </para>
    /// </remarks>
    public static bool TryParse(string? value, [NotNullWhen(true)] out MsspHost? host)
    {
        host = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();

        if (text[0] == '[')
        {
            var close = text.IndexOf(']');
            if (close < 0)
            {
                return false;
            }

            var rest = text[(close + 1)..].Trim().TrimStart(':').Trim();
            if (!int.TryParse(rest, NumberStyles.None, CultureInfo.InvariantCulture, out var bracketedPort))
            {
                return false;
            }

            host = Create(text[..(close + 1)], bracketedPort);
            return host is not null;
        }

        // The specified form. Split at the last run of whitespace, so a (malformed) host containing a
        // space still yields its port rather than being thrown away twice over.
        var separator = text.LastIndexOfAny([' ', '\t']);
        if (separator > 0 &&
            int.TryParse(text[(separator + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var spacedPort))
        {
            host = Create(text[..separator], spacedPort);
            return host is not null;
        }

        // The tolerated form, only where no IPv6 literal could be meant.
        var colon = text.IndexOf(':');
        if (colon > 0 &&
            text.IndexOf(':', colon + 1) < 0 &&
            int.TryParse(text[(colon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var colonPort))
        {
            host = Create(text[..colon], colonPort);
            return host is not null;
        }

        return false;
    }

    /// <summary>The wire form MSSP uses: host, a space, port.</summary>
    public string ToReferralString() => $"{Host} {Port}";

    /// <summary>The display form, bracketing IPv6 literals the way a URL would.</summary>
    public override string ToString() => IsIpV6 ? $"[{Host}]:{Port}" : $"{Host}:{Port}";

    private static MsspHostScope Classify(IPAddress address) =>
        address.AddressFamily == AddressFamily.InterNetworkV6 ? ClassifyV6(address) : ClassifyV4(address);

    private static MsspHostScope ClassifyV6(IPAddress address)
    {
        // An IPv4-mapped address wears IPv6 clothes and is an IPv4 address. Classify what it is, or
        // ::ffff:127.0.0.1 is a loopback referral that reads as globally routable.
        if (address.IsIPv4MappedToIPv6)
        {
            return ClassifyV4(address.MapToIPv4());
        }

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.IPv6Any))
        {
            return MsspHostScope.Loopback;
        }

        if (address.IsIPv6LinkLocal)
        {
            return MsspHostScope.LinkLocal;
        }

        if (address.IsIPv6Multicast)
        {
            return MsspHostScope.Multicast;
        }

        // Unique local addresses, fc00::/7 — the IPv6 analogue of RFC 1918. It is a prefix on the
        // first seven bits, so fc00:: and fd00:: are both inside it.
        var bytes = address.GetAddressBytes();
        return (bytes[0] & 0xFE) == 0xFC ? MsspHostScope.Private : MsspHostScope.Global;
    }

    private static MsspHostScope ClassifyV4(IPAddress address)
    {
        var b = address.GetAddressBytes();
        return b[0] switch
        {
            0 => MsspHostScope.Loopback,                                  // 0.0.0.0/8, "this network"
            10 => MsspHostScope.Private,
            100 when b[1] is >= 64 and <= 127 => MsspHostScope.Private,   // RFC 6598 carrier-grade NAT
            127 => MsspHostScope.Loopback,
            169 when b[1] == 254 => MsspHostScope.LinkLocal,              // includes 169.254.169.254
            172 when b[1] is >= 16 and <= 31 => MsspHostScope.Private,
            192 when b[1] == 168 => MsspHostScope.Private,
            >= 224 => MsspHostScope.Multicast,                            // and 255.255.255.255
            _ => MsspHostScope.Global,
        };
    }

    /// <summary>
    /// A cheap sanity filter on a DNS name, so a value that is plainly not a host — a sentence, a
    /// URL, a control character smuggled through — never reaches a resolver. Deliberately permissive
    /// about which characters a label may hold (underscores and non-ASCII both occur in the wild) and
    /// strict only about what a host name can never contain.
    /// </summary>
    private static bool IsPlausibleName(string name)
    {
        if (name.Length > 253 || name.StartsWith('.') || name.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var character in name)
        {
            if (char.IsWhiteSpace(character) ||
                char.IsControl(character) ||
                character is '/' or '\\' or '@' or ':' or '?' or '#' or '[' or ']')
            {
                return false;
            }
        }

        return true;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Crawl.Tests </dev/null
```
Expected: PASS — 25 new test cases from 17 test methods (the `[Arguments]` one is nine of them),
plus the existing `WhoReadingTests`, no warnings.

- [ ] **Step 5: Commit**

```bash
git add src/MUI.Crawl/Mssp/MsspHost.cs tests/MUI.Crawl.Tests/MsspHostTests.cs
git commit -m "feat(crawl): read a REFERRAL as a host, and refuse the ones pointing inwards

Spec §7.2: a referral is a candidate hostname, not a fact. IsCrawlable is the gate
— false for loopback, RFC 1918, RFC 6598, fc00::/7, link-local (including
169.254.169.254) and multicast — because a REFERRAL list is configuration on
somebody else's server and following it is how a crawler is aimed. IPv4-mapped
IPv6 is classified as the address it actually is, or ::ffff:127.0.0.1 reads as
globally routable.

Equality is over the normalised host and port, so one server is one entry however
its peers spell it, which is what lets a crawl terminate."
```

---

### Task 2: `MsspData` over TelnetNegotiationCore 2.7.0, and the plaintext reply

Spec §6.4 (MSSP is the payload; telnet option 70 with the plaintext `MSSP-REQUEST` fallback).
Contract addendum §2a and §2b.

**Read §2a before writing a line of this.** TelnetNegotiationCore 2.7.0 already provides — verified by
reflection against the shipped assembly — the ordered name → value-list map (`MSSPConfig.Variables`,
`MSSPVariableCollection` with its indexer, `Keys`, `Count`, `TryGetValue`, `ContainsKey`, `Default`,
`Flag`, `Integer`, `OfficialNames`, `UnofficialNames`), the vocabulary rules
(`MSSPVariables.Canonicalize`, `IsOfficial`, `IsKnown`, `Official`) and flag parsing
(`MSSPValue.TryParseFlag`). **None of that is re-implemented here, and there is no subnegotiation
parser in this repository.**

`MsspData` **projects; it does not parse.** It exists for exactly four readings the library does not
have, and the type's own doc comment says so:

1. `CRAWL DELAY`'s `-1` means "use the crawler's default" and resolves to **null**, never to a
   negative interval — a caller combining this with its own default has to be able to tell "no
   preference" from "zero".
2. Ports validated as ports, so a `PORT` of `0`, `99999` or `web` never reaches a connect attempt.
3. `REFERRAL` read as `MsspHost`s (Task 1) — deduplicated, unparseable values dropped silently, the
   raw strings still readable through the indexer.
4. An **immutable** snapshot. `MSSPConfig` is the live negotiation's own mutable state; a
   `ProbeResult` is a fact about one moment and cannot hold something that changes underneath it.

The second half of this task is the half TNC genuinely does not have: `MsspPlaintextReply`. Telnet
option 70 is TNC's job; the out-of-band `MSSP-REQUEST` text protocol (spec §6.4 — tab-separated pairs
delimited by `MSSP-REPLY-START` / `MSSP-REPLY-END`) is not a telnet option at all, and the library
knows nothing about it. It is the only MSSP *parsing* MUIndex owns.

**Files:**
- Modify: `Directory.Packages.props`
- Create: `src/MUI.Crawl/Mssp/MsspVariables.cs`
- Create: `src/MUI.Crawl/Mssp/MsspData.cs`
- Create: `src/MUI.Crawl/Mssp/MsspPlaintextReply.cs`
- Test: `tests/MUI.Crawl.Tests/MsspDataTests.cs`
- Test: `tests/MUI.Crawl.Tests/MsspPlaintextReplyTests.cs`

**Interfaces:**
- Consumes: `MsspHost` (Task 1); `TelnetNegotiationCore.Models.{MSSPConfig, MSSPVariableCollection,
  MSSPVariables, MSSPValue}`.
- Produces: `MUI.Crawl.Mssp.MsspVariables` (the names); `MUI.Crawl.Mssp.MsspData` with `Empty`, the
  three `From` overloads, the dictionary surface, `Default`/`Flag`/`Integer`, the typed accessors,
  `CrawlDelay` and `Referrals`; `MUI.Crawl.Mssp.MsspPlaintextReply.TryParse(string, out MsspData)`.
  Task 3 types `ProbeResult.Mssp` as `MsspData`; Task 8 hands one to `MsspReceived`; Task 13 chooses
  between the two routes; Plans 2 and 3 read the typed accessors and `Referrals`.

- [ ] **Step 1: Raise the pinned TelnetNegotiationCore**

In `Directory.Packages.props`, change the version and say why the number matters:

```xml
    <!--
      Telnet negotiation. The probe engine is this library pointed outward: the handshake it performs
      IS the capability measurement (spec §6.1), so what a server offers is observed rather than
      taken from a game's own MSSP claim.

      2.7.0 is the version the MSSP surface MsspData projects — MSSPVariableCollection,
      MSSPVariables.Canonicalize, MSSPValue.TryParseFlag — was verified against by reflection.
    -->
    <PackageVersion Include="TelnetNegotiationCore" Version="2.7.0" />
```

`src/MUI.Crawl/MUI.Crawl.csproj` already carries `<PackageReference Include="TelnetNegotiationCore" />`
and needs no edit.

- [ ] **Step 2: Write the failing tests**

Create `tests/MUI.Crawl.Tests/MsspDataTests.cs`:

```csharp
using MUI.Crawl.Mssp;

namespace MUI.Crawl.Tests;

/// <summary>
/// The projection, and only the projection. Every test here is about something TelnetNegotiationCore
/// does <em>not</em> do: if a test could be satisfied by handing back the library's own collection,
/// it does not belong in this file.
/// </summary>
public class MsspDataTests
{
    private static MsspData Report(params (string Variable, string[] Values)[] entries) =>
        MsspData.From(entries.Select(entry =>
            new KeyValuePair<string, IReadOnlyList<string>>(entry.Variable, entry.Values)));

    [Test]
    public async Task AnEmptyReportIsOneSharedInstance()
    {
        // ProbeResult defaults to it (Task 3) and the fixtures assert on the reference, so it has to
        // be a singleton rather than a fresh empty each time.
        await Assert.That(MsspData.Empty).IsSameReferenceAs(MsspData.Empty);
        await Assert.That(MsspData.Empty.Count).IsEqualTo(0);
        await Assert.That(MsspData.Empty["NAME"]).IsEmpty();
        await Assert.That(MsspData.Empty.Name).IsNull();
    }

    [Test]
    public async Task EveryValueOfEveryVariableIsKeptInWireOrder()
    {
        // The reason this is a map to a list rather than to a string. MSSP says "multiple values
        // should be ordered from least to most relevant", so a model with one value per variable
        // would silently pick a server's least preferred port and lose REFERRAL entirely.
        var data = Report(("PORT", ["23", "4201"]), ("NAME", ["Corvid Nest"]));

        await Assert.That(data["PORT"]).IsEquivalentTo(new[] { "23", "4201" });
        await Assert.That(data.Keys).IsEquivalentTo(new[] { "PORT", "NAME" });
        await Assert.That(data.Count).IsEqualTo(2);
        await Assert.That(data.ContainsKey("port")).IsTrue();
        await Assert.That(data.TryGetValue("NAME", out var name)).IsTrue();
        await Assert.That(name).IsEquivalentTo(new[] { "Corvid Nest" });
    }

    [Test]
    public async Task ADefaultIsTheLastValueSentBecauseTheSpecificationSaysSo()
    {
        var data = Report(("CODEBASE", ["TinyMUSH 2.2", "PennMUSH 1.8.8"]));

        await Assert.That(data.Default("CODEBASE")).IsEqualTo("PennMUSH 1.8.8");
        await Assert.That(data.Codebase).IsEqualTo("PennMUSH 1.8.8");
        await Assert.That(data.Default("NOT SENT")).IsNull();
    }

    [Test]
    public async Task NameFoldingIsTheLibrarysSoTwoSpellingsAreOneVariable()
    {
        // MSSPVariables.Canonicalize is TelnetNegotiationCore's, deliberately: two copies of a
        // vocabulary drift, and this one reads the wire.
        var data = Report(("MINIMUM_AGE", ["13"]), ("MINIMUM AGE", ["18"]));

        await Assert.That(data.Count).IsEqualTo(1);
        await Assert.That(data["MINIMUM AGE"]).IsEquivalentTo(new[] { "13", "18" });
        await Assert.That(data.Default("minimum_age")).IsEqualTo("18");
    }

    [Test]
    public async Task TheOfficialAndUnofficialSplitIsAlsoTheLibrarys()
    {
        // A crawler records what a server said rather than what a model expected, so an unofficial
        // variable is kept beside the official ones instead of dropped — and MSSP's unofficial half
        // is where several widely deployed variables live.
        var data = Report(("NAME", ["Corvid Nest"]), ("FAVOURITE BIRD", ["Corvid"]));

        await Assert.That(data.OfficialNames).IsEquivalentTo(new[] { "NAME" });
        await Assert.That(data.UnofficialNames).IsEquivalentTo(new[] { "FAVOURITE BIRD" });
        await Assert.That(data["FAVOURITE BIRD"]).IsEquivalentTo(new[] { "Corvid" });
    }

    [Test]
    public async Task PortsAreValidatedAsPorts()
    {
        // Reading number one of the four. A PORT this model hands back is dialled, so a 0, a 99999
        // or a word must never reach a connect attempt.
        var data = Report(("PORT", ["0", "web", "99999", "23", "4201"]));

        await Assert.That(data.Ports).IsEquivalentTo(new[] { 23, 4201 });
        await Assert.That(data.Port).IsEqualTo(4201).Because("the last listed is the most important");
        await Assert.That(Report(("PORT", ["nonsense"])).Port).IsNull();
    }

    [Test]
    public async Task CrawlDelayOfMinusOneIsNoPreferenceAndNeverANegativeInterval()
    {
        // Reading number two, and the one with teeth: the specification gives -1 the meaning "use the
        // crawler's default", and a caller combining this with its own default has to be able to tell
        // "no preference" from "zero". A negative TimeSpan here would schedule a probe in the past.
        await Assert.That(Report(("CRAWL DELAY", ["-1"])).CrawlDelay).IsNull();
        await Assert.That(Report(("CRAWL DELAY", ["0"])).CrawlDelay).IsEqualTo(TimeSpan.Zero);
        await Assert.That(Report(("CRAWL DELAY", ["5"])).CrawlDelay).IsEqualTo(TimeSpan.FromHours(5));
        await Assert.That(Report(("CRAWL DELAY", ["soon"])).CrawlDelay).IsNull();
        await Assert.That(MsspData.Empty.CrawlDelay).IsNull();
    }

    [Test]
    public async Task ReferralsAreParsedDeduplicatedAndTheRawStringsSurvive()
    {
        // Reading number three. One stale line in somebody else's hand-maintained list is not a fault
        // in their report, so it is dropped silently — but the raw values stay readable, because a
        // report page should be able to show what a server actually said.
        var data = Report(("REFERRAL",
        [
            "a.example.org 4000",
            "b.example.org 4000",
            "A.EXAMPLE.ORG 4000",
            "not a referral",
            "2001:db8::9 4201",
        ]));

        await Assert.That(data.Referrals.Select(r => r.ToReferralString()))
            .IsEquivalentTo(new[] { "a.example.org 4000", "b.example.org 4000", "2001:db8::9 4201" });
        await Assert.That(data["REFERRAL"].Count).IsEqualTo(5);
    }

    [Test]
    public async Task AReferralIntoPrivateSpaceIsListedAndIsNotCrawlable()
    {
        // This model reports; it does not decide. Dropping an uncrawlable referral here would hide it
        // from the report that traces a poisoned source (spec §7.2), so it is listed and marked.
        var data = Report(("REFERRAL", ["169.254.169.254 80", "good.example.org 4000"]));

        await Assert.That(data.Referrals.Count).IsEqualTo(2);
        await Assert.That(data.Referrals.Count(r => r.IsCrawlable)).IsEqualTo(1);
        await Assert.That(data.Referrals.Single(r => !r.IsCrawlable).Scope).IsEqualTo(MsspHostScope.LinkLocal);
    }

    [Test]
    public async Task ASnapshotDoesNotChangeUnderneathItsHolder()
    {
        // Reading number four. MSSPConfig is the live negotiation's mutable state; a ProbeResult is a
        // fact about one moment, so what it holds is copied out rather than referenced.
        var values = new List<string> { "17" };
        var data = MsspData.From([new KeyValuePair<string, IReadOnlyList<string>>("PLAYERS", values)]);

        values.Add("999");

        await Assert.That(data.Players).IsEqualTo(17);
        await Assert.That(data["PLAYERS"].Count).IsEqualTo(1);
    }

    [Test]
    public async Task AnIntegerIsNeverNegativeBecauseMinusOneMeansDataNotAvailable()
    {
        // Narrower than the library's own Integer, which returns -1 as-is so a caller can tell "the
        // server cannot count its rooms" from "the server never mentioned rooms". Everything reading
        // this type wants a number it can print or compare, and the raw string is one indexer away.
        await Assert.That(Report(("PLAYERS", ["-1"])).Players).IsNull();
        await Assert.That(Report(("PLAYERS", ["-1"]))["PLAYERS"]).IsEquivalentTo(new[] { "-1" });
        await Assert.That(Report(("PLAYERS", ["0"])).Players).IsEqualTo(0);
        await Assert.That(Report(("PLAYERS", ["many"])).Players).IsNull();
    }

    [Test]
    public async Task FlagsAreReadByTheLibrarysOwnParser()
    {
        await Assert.That(Report(("SSL", ["1"])).Flag("SSL")).IsTrue();
        await Assert.That(Report(("SSL", ["0"])).Flag("SSL")).IsFalse();
        await Assert.That(Report(("SSL", ["maybe"])).Flag("SSL")).IsNull();
        await Assert.That(MsspData.Empty.Flag("SSL")).IsNull();
    }

    [Test]
    public async Task UptimeIsTheUnixTimestampTheServerBootedAt()
    {
        await Assert.That(Report(("UPTIME", ["1735689600"])).Uptime)
            .IsEqualTo(DateTimeOffset.FromUnixTimeSeconds(1735689600));
        await Assert.That(Report(("UPTIME", ["0"])).Uptime).IsNull();
        await Assert.That(Report(("UPTIME", ["yesterday"])).Uptime).IsNull();
    }

    [Test]
    public async Task TheTypedAccessorsAreTheOnesTheCatalogueReads()
    {
        var data = Report(
            ("NAME", ["Corvid Nest"]),
            ("HOSTNAME", ["corvid.example.org"]),
            ("CONTACT", ["admin@corvid.example.org"]),
            ("WEBSITE", ["https://corvid.example.org/"]),
            ("CODEBASE", ["PennMUSH 1.8.8"]),
            ("FAMILY", ["TinyMUSH"]),
            ("CREATED", ["2003"]));

        await Assert.That(data.Name).IsEqualTo("Corvid Nest");
        await Assert.That(data.Hostname).IsEqualTo("corvid.example.org");
        await Assert.That(data.Contact).IsEqualTo("admin@corvid.example.org");
        await Assert.That(data.Website).IsEqualTo("https://corvid.example.org/");
        await Assert.That(data.Codebase).IsEqualTo("PennMUSH 1.8.8");
        await Assert.That(data.Family).IsEqualTo("TinyMUSH");

        // CREATED is half of Plan 3's second-strongest identity signal (§7.3), which is why it is a
        // named variable here and not only a string in the map.
        await Assert.That(data.Created).IsEqualTo("2003");
    }

    [Test]
    public async Task AVariableMentionedWithNoValuesIsKept()
    {
        // "The server mentioned this and said nothing" is a different fact from "the server never
        // mentioned it", and a capability matrix reads the difference.
        var data = Report(("GENRE", []));

        await Assert.That(data.ContainsKey("GENRE")).IsTrue();
        await Assert.That(data["GENRE"]).IsEmpty();
        await Assert.That(data.Default("GENRE")).IsNull();
    }
}
```

Create `tests/MUI.Crawl.Tests/MsspPlaintextReplyTests.cs`:

```csharp
using MUI.Crawl.Mssp;

namespace MUI.Crawl.Tests;

/// <summary>
/// Spec §6.4's out-of-band reply — the half of MSSP that is not a telnet option, and so the only
/// MSSP parsing this repository owns. TelnetNegotiationCore reads option 70 and knows nothing
/// whatsoever about this text protocol.
/// </summary>
public class MsspPlaintextReplyTests
{
    private const string Reply =
        "MSSP-REPLY-START\r\n" +
        "NAME\tCorvid Nest\r\n" +
        "PLAYERS\t17\r\n" +
        "CODEBASE\tPennMUSH 1.8.8\r\n" +
        "MSSP-REPLY-END\r\n";

    [Test]
    public async Task TabSeparatedPairsBetweenTheDelimitersAreRead()
    {
        await Assert.That(MsspPlaintextReply.TryParse(Reply, out var data)).IsTrue();

        await Assert.That(data.Name).IsEqualTo("Corvid Nest");
        await Assert.That(data.Players).IsEqualTo(17);
        await Assert.That(data.Codebase).IsEqualTo("PennMUSH 1.8.8");
        await Assert.That(data.Count).IsEqualTo(3);
    }

    [Test]
    public async Task TheReplyIsFoundEvenWhenTheConnectScreenPrecedesIt()
    {
        // What actually arrives: the tail of the login screen, an echo of the command, then the
        // reply, then a prompt.
        var transcript = "Welcome to Corvid Nest.\r\nMSSP-REQUEST\r\n" + Reply + "By what name? ";

        await Assert.That(MsspPlaintextReply.TryParse(transcript, out var data)).IsTrue();
        await Assert.That(data.Name).IsEqualTo("Corvid Nest");
    }

    [Test]
    public async Task ARepeatedVariableBecomesAnArrayInTheOrderGiven()
    {
        // The plaintext form has no array notation, so a server repeats the variable. It means the
        // same thing as several MSSP_VALs, and PORT and REFERRAL both depend on it.
        const string ports =
            "MSSP-REPLY-START\r\nPORT\t23\r\nPORT\t4201\r\nREFERRAL\ta.example.org 4000\r\nMSSP-REPLY-END\r\n";

        await Assert.That(MsspPlaintextReply.TryParse(ports, out var data)).IsTrue();
        await Assert.That(data.Ports).IsEquivalentTo(new[] { 23, 4201 });
        await Assert.That(data.Port).IsEqualTo(4201);
        await Assert.That(data.Referrals.Single().ToReferralString()).IsEqualTo("a.example.org 4000");
    }

    [Test]
    public async Task AValueMayContainSpacesBecauseOnlyTheFirstTabSeparates()
    {
        const string spaced = "MSSP-REPLY-START\r\nNAME\tThe  Iron  Marches\r\nMSSP-REPLY-END\r\n";

        await Assert.That(MsspPlaintextReply.TryParse(spaced, out var data)).IsTrue();
        await Assert.That(data.Name).IsEqualTo("The  Iron  Marches");
    }

    [Test]
    public async Task AnEmptyValueIsKeptRatherThanDroppingTheVariable()
    {
        const string empty = "MSSP-REPLY-START\r\nWEBSITE\t\r\nMSSP-REPLY-END\r\n";

        await Assert.That(MsspPlaintextReply.TryParse(empty, out var data)).IsTrue();
        await Assert.That(data.ContainsKey("WEBSITE")).IsTrue();
        await Assert.That(data.Default("WEBSITE")).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task ALineWithNoTabIsNotAPair()
    {
        const string chatty =
            "MSSP-REPLY-START\r\n(here you go)\r\nNAME\tCorvid Nest\r\n\r\nMSSP-REPLY-END\r\n";

        await Assert.That(MsspPlaintextReply.TryParse(chatty, out var data)).IsTrue();
        await Assert.That(data.Count).IsEqualTo(1);
    }

    [Test]
    [Arguments("Huh? Type HELP for a list of commands.\r\n")]
    [Arguments("")]
    [Arguments("NAME\tCorvid Nest\r\n")]
    [Arguments("MSSP-REPLY-START\r\nNAME\tCorvid Nest\r\n")]
    [Arguments("MSSP-REPLY-END\r\nNAME\tCorvid Nest\r\n")]
    public async Task WithoutBothDelimitersItIsNotAReplyAndIsNotHalfRead(string text)
    {
        // "Huh?" is the usual answer to MSSP-REQUEST and must never become a report. A reply that
        // started and did not finish is the more interesting case: the transcript was capped or the
        // server hung up mid-report, and recording a partial report as a complete one would write a
        // game's fields away to nothing.
        await Assert.That(MsspPlaintextReply.TryParse(text, out var data)).IsFalse();
        await Assert.That(data).IsSameReferenceAs(MsspData.Empty);
    }

    [Test]
    public async Task TheDelimitersAreMatchedWithoutRegardToCaseOrTrailingSpace()
    {
        const string sloppy = "mssp-reply-start  \r\nNAME\tCorvid Nest\r\n  Mssp-Reply-End\r\n";

        await Assert.That(MsspPlaintextReply.TryParse(sloppy, out var data)).IsTrue();
        await Assert.That(data.Name).IsEqualTo("Corvid Nest");
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `error CS0103: The name 'MsspData' does not exist in the current context`.

- [ ] **Step 4: Write the variable names**

Create `src/MUI.Crawl/Mssp/MsspVariables.cs`:

```csharp
namespace MUI.Crawl.Mssp;

/// <summary>
/// The canonical spellings of the MSSP variables this crawler reads by name — the ones
/// <see cref="MsspData"/>'s typed accessors are built on.
/// </summary>
/// <remarks>
/// <para>
/// Names only. The vocabulary <em>rules</em> — which names are official, and the folding that makes
/// <c>MINIMUM_AGE</c> and <c>MINIMUM AGE</c> one variable — belong to
/// <c>TelnetNegotiationCore.Models.MSSPVariables</c>, which derives them from the same model that
/// reads the wire. Two copies of a vocabulary drift; this one exists only so an accessor can say what
/// it is reading instead of repeating a string literal.
/// </para>
/// <para>
/// Canonical form is the <em>spaced, upper-case</em> spelling, which is what the specification's own
/// tables print and what a server operator recognises from their configuration.
/// See <see href="https://mudhalla.net/tintin/protocols/mssp/">the specification</see>.
/// </para>
/// </remarks>
public static class MsspVariables
{
    // ---- Required ----
    public const string Name = "NAME";
    public const string Players = "PLAYERS";
    public const string Uptime = "UPTIME";

    // ---- Generic ----
    public const string Charset = "CHARSET";
    public const string Codebase = "CODEBASE";
    public const string Contact = "CONTACT";
    public const string CrawlDelay = "CRAWL DELAY";
    public const string Hostname = "HOSTNAME";
    public const string MinimumAge = "MINIMUM AGE";
    public const string Port = "PORT";
    public const string Referral = "REFERRAL";
    public const string Ssl = "SSL";
    public const string Website = "WEBSITE";

    // ---- Categorisation ----
    public const string Family = "FAMILY";
    public const string Genre = "GENRE";
    public const string Status = "STATUS";

    /// <summary>
    /// The year the game was created. Half of Plan 3's second-strongest identity signal (§7.3):
    /// a year rarely changes, so <c>NAME</c> + <c>CREATED</c> survives a host move.
    /// </summary>
    public const string Created = "CREATED";
}
```

- [ ] **Step 5: Write the projection**

Create `src/MUI.Crawl/Mssp/MsspData.cs`:

```csharp
using System.Collections;
using System.Globalization;
using TelnetNegotiationCore.Models;

namespace MUI.Crawl.Mssp;

/// <summary>
/// One server's MSSP report, in the shape this crawler wants it: every variable it sent, with every
/// value, in the order it sent them, plus the domain readings a directory asks for.
/// </summary>
/// <remarks>
/// <para>
/// <b>This projects; it does not parse.</b> The bytes are read by TelnetNegotiationCore, which hands
/// back an ordered name → value-list map (<c>MSSPConfig.Variables</c>), canonicalises the names, and
/// reads flags and integers. There is no subnegotiation parser in this repository and there must
/// never be one.
/// </para>
/// <para>
/// <b>Four readings are the entire reason this type exists</b> over the library's own collection:
/// <c>CRAWL DELAY</c>'s <c>-1</c> read as the specification's "no preference" rather than a negative
/// interval; ports validated as ports; <c>REFERRAL</c> read as <see cref="MsspHost"/>s a crawler can
/// follow, deduplicate and refuse; and an immutable snapshot, because <c>MSSPConfig</c> is a live
/// negotiation's mutable state and a <c>ProbeResult</c> is a fact about one moment.
/// </para>
/// <para>
/// The shape is a map from a canonical name to an <em>ordered list</em>, and that is not incidental.
/// MSSP has two ways to attach several values to one variable — repeating the variable, and repeating
/// <c>MSSP_VAL</c> under one variable — and gives both the same meaning: "multiple values should be
/// ordered from least to most relevant", with the default reported last. A model keeping one value
/// per variable would silently pick a server's <em>least</em> preferred port and would lose
/// <c>REFERRAL</c> entirely, a referral list being nothing but an array.
/// </para>
/// <para>
/// Nothing is discarded on the way in. Variables the specification does not define are kept beside
/// the ones it does (<see cref="UnofficialNames"/>): a crawler's job is to record what a server said
/// rather than what a model expected, and MSSP's unofficial half is where several widely deployed
/// variables live.
/// </para>
/// </remarks>
public sealed class MsspData : IReadOnlyDictionary<string, IReadOnlyList<string>>
{
    private readonly Dictionary<string, IReadOnlyList<string>> _values;
    private readonly List<string> _order;

    private MsspData(Dictionary<string, IReadOnlyList<string>> values, List<string> order)
    {
        _values = values;
        _order = order;
    }

    /// <summary>An empty report — no MSSP, or a server that negotiated it and then said nothing.</summary>
    public static MsspData Empty { get; } = new([], []);

    /// <summary>Variable names in the order the server first mentioned them.</summary>
    public IEnumerable<string> Keys => _order;

    public IEnumerable<IReadOnlyList<string>> Values => _order.Select(name => _values[name]);

    public int Count => _order.Count;

    /// <summary>Every value of <paramref name="variable"/>, in wire order; empty when it was not sent.</summary>
    public IReadOnlyList<string> this[string variable] =>
        _values.TryGetValue(MSSPVariables.Canonicalize(variable), out var values) ? values : [];

    /// <summary>The names in this report the specification defines, in wire order.</summary>
    public IReadOnlyList<string> OfficialNames => _order.Where(MSSPVariables.IsOfficial).ToList();

    /// <summary>The names in this report the specification does not define, in wire order.</summary>
    public IReadOnlyList<string> UnofficialNames => _order.Where(name => !MSSPVariables.IsOfficial(name)).ToList();

    public bool ContainsKey(string variable) => _values.ContainsKey(MSSPVariables.Canonicalize(variable));

    public bool TryGetValue(string variable, out IReadOnlyList<string> values) =>
        _values.TryGetValue(MSSPVariables.Canonicalize(variable), out values!);

    /// <summary>
    /// The <em>default</em> value of <paramref name="variable"/> — the last one sent, per the
    /// specification — or null when the server did not send it.
    /// </summary>
    public string? Default(string variable)
    {
        var values = this[variable];
        return values.Count == 0 ? null : values[^1];
    }

    /// <summary>An MSSP boolean, read by the library's own parser so there is one idea of what one is.</summary>
    public bool? Flag(string variable) =>
        Default(variable) is { } value && MSSPValue.TryParseFlag(value, out var flag) ? flag : null;

    /// <summary>
    /// An MSSP integer, or null when unreported or unparseable.
    /// </summary>
    /// <remarks>
    /// Deliberately narrower than the library's <c>MSSPVariableCollection.Integer</c>, which returns
    /// <c>-1</c> as-is so a caller can tell "the server says it cannot count its rooms" from "the
    /// server never mentioned rooms". Everything reading this type wants a number it can print or
    /// compare, and the raw string is still one indexer away.
    /// </remarks>
    public int? Integer(string variable) =>
        int.TryParse(Default(variable), NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    // ---- The variables the catalogue reads by name ----

    /// <summary>The game's name, or null.</summary>
    public string? Name => Default(MsspVariables.Name);

    /// <summary>Players the server says are logged in, or null. Declared, never measured (spec §3.1).</summary>
    public int? Players => Integer(MsspVariables.Players);

    /// <summary>The Unix timestamp the server booted at, or null.</summary>
    public DateTimeOffset? Uptime =>
        long.TryParse(Default(MsspVariables.Uptime), NumberStyles.None, CultureInfo.InvariantCulture, out var unix)
        && unix > 0
            ? DateTimeOffset.FromUnixTimeSeconds(unix)
            : null;

    /// <summary>The preferred port — the last listed, which the specification calls the most important.</summary>
    public int? Port => Ports.Count == 0 ? null : Ports[^1];

    /// <summary>
    /// Every port the server listed that is actually a port, least to most important. Validated
    /// because a value here is dialled: a <c>0</c>, a <c>99999</c> or a word must never reach a
    /// connect attempt.
    /// </summary>
    public IReadOnlyList<int> Ports => this[MsspVariables.Port]
        .Select(value => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var port) ? port : -1)
        .Where(port => port is > 0 and <= 65535)
        .ToList();

    /// <summary>The hostname the server says it is reachable at, or null.</summary>
    public string? Hostname => Default(MsspVariables.Hostname);

    /// <summary>Contact e-mail, or null.</summary>
    public string? Contact => Default(MsspVariables.Contact);

    /// <summary>Website URL, or null.</summary>
    public string? Website => Default(MsspVariables.Website);

    /// <summary>The current codebase — the last listed, per the specification.</summary>
    public string? Codebase => Default(MsspVariables.Codebase);

    /// <summary>The family — the last listed, which the specification calls the most distant ancestor.</summary>
    public string? Family => Default(MsspVariables.Family);

    /// <summary>The year the game was created, as the server wrote it. Half of §7.3's second signal.</summary>
    public string? Created => Default(MsspVariables.Created);

    /// <summary>
    /// How long the server asks a crawler to leave between visits, or null when it did not say or
    /// asked for the crawler's own default.
    /// </summary>
    /// <remarks>
    /// The specification defines <c>CRAWL DELAY</c> as a "preferred minimum number of hours between
    /// crawls" and gives <c>-1</c> the meaning "use the crawler's default". A negative value therefore
    /// resolves to null rather than to a negative interval — the distinction matters, because a caller
    /// combining this with its own default must be able to tell "no preference" from "zero", and
    /// because a negative interval would schedule the next probe in the past.
    /// </remarks>
    public TimeSpan? CrawlDelay =>
        int.TryParse(Default(MsspVariables.CrawlDelay), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var hours)
        && hours >= 0
            ? TimeSpan.FromHours(hours)
            : null;

    /// <summary>
    /// The peers this server points a crawler at: every parseable <c>REFERRAL</c> value, deduplicated,
    /// in the order given.
    /// </summary>
    /// <remarks>
    /// Values that do not parse are dropped silently rather than surfaced as errors — a referral list
    /// is hand-maintained configuration on somebody else's server, and one stale line in it is not a
    /// fault in the report. Values that parse but are not crawlable are <b>kept</b> and marked
    /// (<see cref="MsspHost.IsCrawlable"/>): this type reports, and Plan 3 decides. The raw strings
    /// remain available through <c>this["REFERRAL"]</c> for anything auditing a poisoned source.
    /// </remarks>
    public IReadOnlyList<MsspHost> Referrals
    {
        get
        {
            var seen = new HashSet<MsspHost>();
            var result = new List<MsspHost>();
            foreach (var value in this[MsspVariables.Referral])
            {
                if (MsspHost.TryParse(value, out var host) && seen.Add(host))
                {
                    result.Add(host);
                }
            }

            return result;
        }
    }

    public IEnumerator<KeyValuePair<string, IReadOnlyList<string>>> GetEnumerator() =>
        _order.Select(name => new KeyValuePair<string, IReadOnlyList<string>>(name, _values[name])).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>The live path: the report TelnetNegotiationCore assembled from the subnegotiation.</summary>
    public static MsspData From(MSSPConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return From(config.Variables);
    }

    /// <summary>The library's collection, projected.</summary>
    public static MsspData From(MSSPVariableCollection variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        return From((IEnumerable<KeyValuePair<string, IReadOnlyList<string>>>)variables);
    }

    /// <summary>
    /// Projects a name → values map into this model, keeping every value of every variable in the
    /// order given — and <b>copying</b> them, so the snapshot cannot change underneath its holder.
    /// </summary>
    /// <remarks>
    /// Names are canonicalised on the way in by the library's <see cref="MSSPVariables.Canonicalize"/>,
    /// so there is one vocabulary in the solution rather than two, which means a source spelling both
    /// <c>MINIMUM_AGE</c> and <c>MINIMUM AGE</c> still yields one variable. A name that canonicalises
    /// to nothing is dropped; a variable with no values is kept, because "the server mentioned this
    /// and said nothing" is a different fact from "the server never mentioned it".
    /// </remarks>
    public static MsspData From(IEnumerable<KeyValuePair<string, IReadOnlyList<string>>> variables)
    {
        ArgumentNullException.ThrowIfNull(variables);

        var values = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var order = new List<string>();

        foreach (var (variable, list) in variables)
        {
            var name = MSSPVariables.Canonicalize(variable);
            if (name.Length == 0)
            {
                continue;
            }

            if (!values.TryGetValue(name, out var accumulated))
            {
                accumulated = [];
                values[name] = accumulated;
                order.Add(name);
            }

            accumulated.AddRange(list);
        }

        return new MsspData(
            values.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)pair.Value, StringComparer.Ordinal),
            order);
    }
}
```

- [ ] **Step 6: Write the plaintext reply reader**

Create `src/MUI.Crawl/Mssp/MsspPlaintextReply.cs`:

```csharp
namespace MUI.Crawl.Mssp;

/// <summary>
/// The plaintext <c>MSSP-REQUEST</c> reply (spec §6.4): tab-separated variable/value pairs between
/// <c>MSSP-REPLY-START</c> and <c>MSSP-REPLY-END</c>, sent as ordinary text at the login screen.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the only MSSP parsing MUIndex owns.</b> Telnet option 70 is
/// TelnetNegotiationCore's — it reads the subnegotiation and hands back a variable collection — but
/// this is not a telnet option at all. It is an out-of-band text protocol the library knows nothing
/// about, and a great many games implement MSSP this way and only this way.
/// </para>
/// <para>
/// <b>Both delimiters are required.</b> A transcript that opened the reply and never closed it was
/// capped (Task 9's cap) or the server hung up mid-report, and recording a partial report as a
/// complete one would write a game's fields away to nothing on the next reconciliation. "Huh?" —
/// the usual answer to a command a game does not have — has neither delimiter and is likewise not a
/// report.
/// </para>
/// </remarks>
public static class MsspPlaintextReply
{
    /// <summary>The line that opens a reply.</summary>
    public const string StartDelimiter = "MSSP-REPLY-START";

    /// <summary>The line that closes one.</summary>
    public const string EndDelimiter = "MSSP-REPLY-END";

    /// <summary>
    /// Reads a reply out of <paramref name="text"/>, which is generally a whole transcript with the
    /// connect screen in front of it and a prompt behind it. Returns false — and
    /// <see cref="MsspData.Empty"/> — when there is no complete reply in it.
    /// </summary>
    public static bool TryParse(string? text, out MsspData data)
    {
        data = MsspData.Empty;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var entries = new List<KeyValuePair<string, IReadOnlyList<string>>>();
        var started = false;
        var finished = false;

        foreach (var raw in text.Split('\n'))
        {
            // Trim carriage returns and spaces, never tabs: the tab is the separator, and a variable
            // sent with an empty value is a fact rather than a malformed line.
            var line = raw.Trim('\r', ' ');

            if (!started)
            {
                started = line.Equals(StartDelimiter, StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (line.Equals(EndDelimiter, StringComparison.OrdinalIgnoreCase))
            {
                finished = true;
                break;
            }

            var tab = line.IndexOf('\t');
            if (tab <= 0)
            {
                // Chrome, a blank line, or a server being chatty inside its own reply.
                continue;
            }

            entries.Add(new KeyValuePair<string, IReadOnlyList<string>>(
                line[..tab].Trim(),
                [line[(tab + 1)..].Trim()]));
        }

        if (!started || !finished)
        {
            return false;
        }

        data = MsspData.From(entries);
        return true;
    }
}
```

- [ ] **Step 7: Run the tests to verify they pass**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Crawl.Tests </dev/null
```
Expected: PASS — 27 new tests (`MsspDataTests` has 15, `MsspPlaintextReplyTests` has 12 counting the
five `[Arguments]` cases), no warnings.

If a test fails inside `MSSPVariables.Canonicalize` or `MSSPValue.TryParseFlag`, check the member
against the pinned package (`~/.nuget/packages/telnetnegotiationcore/2.7.0/`) and assert what the
library actually implements. **Do not answer a gap in it by writing a second parser here** — the
library is first-party (CLAUDE.md), so the answer is a PR upstream and, until it lands, a named
limitation in this plan.

- [ ] **Step 8: Commit**

```bash
git add Directory.Packages.props src/MUI.Crawl/Mssp tests/MUI.Crawl.Tests/MsspDataTests.cs \
        tests/MUI.Crawl.Tests/MsspPlaintextReplyTests.cs
git commit -m "feat(crawl): project TelnetNegotiationCore's MSSP into a snapshot we can reason about

TNC 2.7.0 already reads option 70, folds MINIMUM_AGE into MINIMUM AGE, splits
official from unofficial and parses flags and integers, so none of that is written
here and there is no subnegotiation parser in this repository.

MsspData adds the four readings the library does not have: CRAWL DELAY's -1 is 'no
preference' rather than a negative interval that would schedule the next probe in
the past, ports are validated because a PORT gets dialled, REFERRAL is read as
MsspHosts that can be deduplicated and refused, and the whole thing is an immutable
snapshot because a ProbeResult is a fact about one moment.

MsspPlaintextReply is the half TNC cannot have: MSSP-REQUEST is not a telnet option
but an out-of-band text protocol, and it is the only MSSP parsing we own."
```

---

### Task 3: The seam — `ProbeResult` retyped, and `WhoReading`'s fourth state

Spec §6.5 (the seam), §5.4 (the three states an hour can be in), §6.3 (a claimed owner may assert
"use MSSP `PLAYERS`"). Contract: "Plan 1 produces — `MUI.Crawl`", with addendum §3.

**Two changes, and the second is a correctness fix rather than a retype.** `ProbeResult.Mssp` becomes
`MsspData` (Task 2) instead of `IReadOnlyDictionary<string, string>`, which cannot express an
array-valued variable and so cannot hold `PORT` or `REFERRAL`. And `WhoReading` gains a fourth state.

`WhoReading.Unread` was `new(WhoConfidence.Unknown)`, so record equality made **"we never sent WHO"
equal to "we sent WHO and could not parse the answer"**. Those are different facts with different
consequences: the first writes no presence sample at all, and the second is spec §5.4's own named bug
case — the middle row, a sample with `count = NULL` and `unmeasurable_reason = who_unparseable`, which
renders as a hatched cell rather than as downtime. With one value for both, `PresenceWriter` could not
tell them apart and had to infer intent from `MsspVia`, which is a guess about a different layer.
Fixed here, at source, so Plan 2 reads `Who.WasAttempted` and `Who.Confidence` directly.

`NotAttempted` is deliberately the enum's **zero value**, so a default-constructed reading claims
nothing.

**Files:**
- Modify: `src/MUI.Crawl/ProbeResult.cs`
- Create: `src/MUI.Crawl/ProbeOptions.cs`
- Create: `src/MUI.Crawl/ProbeFailureCauses.cs`
- Modify: `tests/MUI.Crawl.Tests/WhoReadingTests.cs` (it references `WhoReading.Unread` and will not
  compile)
- Test: `tests/MUI.Crawl.Tests/ProbeResultShapeTests.cs`

**Interfaces:**
- Consumes: `MsspData` (Task 2).
- Produces: `ProbeResult` (with `Mssp` typed `MUI.Crawl.Mssp.MsspData`, plus `MsspVia`, `TlsObserved`,
  `Aggregates`), `enum MsspTransport { None, TelnetOption70, PlaintextRequest }`,
  `WhoReading` with `NotAttempted`/`Unreadable`/`WasAttempted`/`HasCount`,
  `enum WhoConfidence { NotAttempted, Unknown, Count, PerPlayer }`, `ProbeFailureCauses` constants,
  `ProbeTarget`, `ProbeOptions` (incl. `MaxCaptureBytes`), `interface IProbe`. Every later task in
  this plan and every later plan depends on these names.

- [ ] **Step 1: Rewrite the reading's own test**

`tests/MUI.Crawl.Tests/WhoReadingTests.cs` exists and uses `WhoReading.Unread`, which is being
deleted. Replace the whole file — the two new tests at the top are the point of the change:

```csharp
using MUI.Crawl;

namespace MUI.Crawl.Tests;

/// <summary>
/// The rule that a WHO parser may not invent a number, and — one level down — that "we never asked"
/// and "we asked and could not read the answer" are two different facts (spec §5.4, §6.3).
/// </summary>
public class WhoReadingTests
{
    [Test]
    public async Task NotAttemptedAndUnreadableAreNotTheSameReading()
    {
        // The whole point of the fourth state, pinned as one assertion. Both carry no count, and
        // record equality used to make them one value — so PresenceWriter could not tell spec §5.4's
        // middle row (probed, unmeasurable: count NULL, unmeasurable_reason = who_unparseable, a
        // hatched cell) from a probe that never asked, which writes no presence sample at all.
        await Assert.That(WhoReading.NotAttempted).IsNotEqualTo(WhoReading.Unreadable);

        await Assert.That(WhoReading.NotAttempted.WasAttempted).IsFalse();
        await Assert.That(WhoReading.Unreadable.WasAttempted).IsTrue();

        await Assert.That(WhoReading.NotAttempted.HasCount).IsFalse();
        await Assert.That(WhoReading.Unreadable.HasCount).IsFalse();
        await Assert.That(WhoReading.Unreadable.Confidence).IsEqualTo(WhoConfidence.Unknown);
    }

    [Test]
    public async Task NotAttemptedIsTheZeroValueSoADefaultReadingClaimsNothing()
    {
        // A reading nobody filled in must not assert that a game was probed and found unreadable.
        await Assert.That(default(WhoConfidence)).IsEqualTo(WhoConfidence.NotAttempted);
        await Assert.That(new WhoReading(default)).IsEqualTo(WhoReading.NotAttempted);
    }

    [Test]
    public async Task ACountAttachedToAReadingThatNeverAskedIsNotACount()
    {
        // HasCount is a question about the confidence, not about whether the field happens to be
        // filled. The old spelling — "not Unknown, and Count is not null" — answers true for the
        // first of these, which is a reading that never asked reporting a count.
        await Assert.That(new WhoReading(WhoConfidence.NotAttempted, Count: 3).HasCount).IsFalse();
        await Assert.That(new WhoReading(WhoConfidence.Unknown, Count: 3).HasCount).IsFalse();
        await Assert.That(new WhoReading(WhoConfidence.Count).HasCount).IsFalse();
    }

    [Test]
    public async Task AnUnreadableResponseCarriesNoCount()
    {
        await Assert.That(WhoReading.Unreadable.HasCount).IsFalse();
        await Assert.That(WhoReading.Unreadable.Count).IsNull();
    }

    [Test]
    public async Task AnEmptyGameIsACountOfZeroAndNotAnAbsentCount()
    {
        var empty = new WhoReading(WhoConfidence.Count, Count: 0);

        await Assert.That(empty.HasCount).IsTrue();
        await Assert.That(empty.Count).IsEqualTo(0);
        await Assert.That(empty.WasAttempted).IsTrue();
    }

    [Test]
    public async Task PerPlayerConfidenceIsWhatUnlocksAggregates()
    {
        var reading = new WhoReading(WhoConfidence.PerPlayer, Count: 7, IdentifiablePlayers: 7);

        await Assert.That(reading.Confidence).IsEqualTo(WhoConfidence.PerPlayer);
        await Assert.That(reading.IdentifiablePlayers).IsEqualTo(7);
        await Assert.That(reading.HasCount).IsTrue();
    }
}
```

- [ ] **Step 2: Write the failing test for the seam**

Create `tests/MUI.Crawl.Tests/ProbeResultShapeTests.cs`:

```csharp
using MUI.Crawl.Mssp;

namespace MUI.Crawl.Tests;

/// <summary>
/// The seam's shape (spec §6.5). Every downstream writer is built against exactly this, so a change
/// here is a change to four other plans.
/// </summary>
public class ProbeResultShapeTests
{
    private static ProbeResult Minimal() => new()
    {
        Host = "corvid.example.org",
        Port = 4201,
        ObservedAt = DateTimeOffset.UnixEpoch,
        Outcome = ProbeOutcome.Answered,
    };

    [Test]
    public async Task AResultWithNoMsspCarriesTheEmptyReportRatherThanNull()
    {
        var result = Minimal();

        await Assert.That(result.Mssp).IsSameReferenceAs(MsspData.Empty);
        await Assert.That(result.MsspVia).IsEqualTo(MsspTransport.None);
        await Assert.That(result.Aggregates).IsNull();
        await Assert.That(result.TlsObserved).IsFalse();
    }

    [Test]
    public async Task AResultNobodyFilledInHasNotAskedAnybodyAnything()
    {
        // The default matters because a failed probe never reaches layer 3 and is constructed from
        // exactly this. It must not claim we asked and could not read the answer.
        var result = Minimal();

        await Assert.That(result.Who).IsEqualTo(WhoReading.NotAttempted);
        await Assert.That(result.Who.WasAttempted).IsFalse();
        await Assert.That(result.Who.Count).IsNull();
    }

    [Test]
    public async Task MsspIsTheDomainModelAndNotAStringDictionary()
    {
        var result = Minimal() with
        {
            Mssp = MsspData.From([
                new KeyValuePair<string, IReadOnlyList<string>>("NAME", ["Corvid Nest"]),
                new KeyValuePair<string, IReadOnlyList<string>>("PLAYERS", ["17"]),
                new KeyValuePair<string, IReadOnlyList<string>>("PORT", ["23", "4201"]),
            ]),
            MsspVia = MsspTransport.TelnetOption70,
        };

        // The properties a plain IReadOnlyDictionary<string,string> could not have offered: typed
        // access, and array-valued variables kept in wire order.
        await Assert.That(result.Mssp.Name).IsEqualTo("Corvid Nest");
        await Assert.That(result.Mssp.Players).IsEqualTo(17);
        await Assert.That(result.Mssp.Ports).IsEquivalentTo(new[] { 23, 4201 });
    }

    [Test]
    public async Task TheFailureVocabularyIsTheOneTheSpecNames()
    {
        // MUI.Crawl cannot reference MUI.Catalog.FailureCause, so the vocabulary crosses the boundary
        // as strings and MUI.Discovery maps them (Plan 2's FailureCauseMap).
        await Assert.That(ProbeFailureCauses.Dns).IsEqualTo("dns");
        await Assert.That(ProbeFailureCauses.Refused).IsEqualTo("refused");
        await Assert.That(ProbeFailureCauses.Tls).IsEqualTo("tls");
        await Assert.That(ProbeFailureCauses.Timeout).IsEqualTo("timeout");
        await Assert.That(ProbeFailureCauses.HandshakeStalled).IsEqualTo("handshake_stalled");
        await Assert.That(ProbeFailureCauses.Unknown).IsEqualTo("unknown");
    }

    [Test]
    public async Task ProbeOptionsBoundEveryPhaseAndNameTheCrawler()
    {
        var options = new ProbeOptions();

        await Assert.That(options.HardTimeout).IsEqualTo(TimeSpan.FromSeconds(45));
        await Assert.That(options.BannerQuietPeriod).IsEqualTo(TimeSpan.FromSeconds(3));
        await Assert.That(options.TerminalTypes).IsEquivalentTo(new[] { "MUINDEX", "MUINDEX-CRAWLER", "MTTS 1" });
        await Assert.That(options.InfoUrl).IsEqualTo("https://muindex.org/crawler");
        await Assert.That(options.MaxCaptureBytes).IsEqualTo(64 * 1024);
        await Assert.That(options.SendWho).IsTrue();
        await Assert.That(options.PlaintextMsspFallback).IsTrue();
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `error CS0117: 'WhoReading' does not contain a definition for 'NotAttempted'`, and
`error CS0246: The type or namespace name 'ProbeOptions' could not be found`.

- [ ] **Step 4: Retype the seam and give `WhoReading` its fourth state**

Rewrite `src/MUI.Crawl/ProbeResult.cs` whole. It becomes:

```csharp
using MUI.Crawl.Mssp;

namespace MUI.Crawl;

/// <summary>
/// Everything one telnet connection to one game yielded. The single output of a probe and the seam
/// the rest of the system is built against (spec §6.5).
/// </summary>
/// <remarks>
/// The four layers below are not a fallback chain. One session produces the handshake and the banner
/// always, <c>WHO</c> usually, and MSSP either wholly or not at all — so a game that answers none of
/// the optional layers still yields measured capability data.
/// </remarks>
public sealed record ProbeResult
{
    public required string Host { get; init; }

    public required int Port { get; init; }

    public required DateTimeOffset ObservedAt { get; init; }

    public required ProbeOutcome Outcome { get; init; }

    /// <summary>
    /// Layer 1 — the telnet options the server actually offered. Measured, not claimed: a game whose
    /// MSSP says <c>GMCP 1</c> may simply be wrong, and the handshake cannot be (spec §6.1).
    /// </summary>
    public IReadOnlySet<string> OfferedOptions { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Layer 2 — the connect screen, ANSI intact. Display asset and codebase fingerprint both.</summary>
    public string? Banner { get; init; }

    /// <summary>
    /// Layer 3 — what <c>WHO</c> or <c>DOING</c> yielded at the login screen, or the fact that we did
    /// not ask. Defaults to <see cref="WhoReading.NotAttempted"/>, which is what a failed probe
    /// carries: it never reached layer 3, and must not claim it read something it could not.
    /// </summary>
    public WhoReading Who { get; init; } = WhoReading.NotAttempted;

    /// <summary>Layer 4 — MSSP, whether by telnet option 70 or the plaintext <c>MSSP-REQUEST</c> fallback.</summary>
    public MsspData Mssp { get; init; } = MsspData.Empty;

    /// <summary>
    /// How layer 4 arrived. Worth recording rather than inferring: a server read only through the
    /// plaintext fallback did not negotiate option 70, and that is a capability fact about it.
    /// </summary>
    public MsspTransport MsspVia { get; init; } = MsspTransport.None;

    /// <summary>Whether TLS was completed on this port. Observed, exactly like the handshake (spec §6.1).</summary>
    public bool TlsObserved { get; init; }

    /// <summary>
    /// What §11 permits to leave the probe when the WHO parser reached per-player confidence: salted
    /// hashes and bucket counts. Null otherwise. <b>Names never appear here and never leave.</b>
    /// </summary>
    public PresenceAggregates? Aggregates { get; init; }

    public FailureDetail? Failure { get; init; }

    public TimeSpan Elapsed { get; init; }
}

/// <summary>
/// How a probe ended. <b>Exactly two members, and both mean the socket was opened</b> — the far end
/// answered, or the exchange failed. There is deliberately no third member.
/// </summary>
/// <remarks>
/// <para>
/// <b>Never add a <c>Refused</c> member, and never construct a <see cref="ProbeResult"/> for a host
/// we declined to dial.</b> Plan 03's <c>HostScopeGuard</c> refuses a target whose name resolves into
/// non-global address space (spec §7.2), and that refusal happens <em>before</em> a
/// <see cref="ProbeResult"/> exists. That ordering is what satisfies §7.2's "a refusal writes no
/// availability sample" <em>structurally</em> rather than by a check somebody has to remember: a
/// refusal cannot reach the writers because there is nothing for it to reach them with.
/// </para>
/// <para>
/// The tempting shortcut is <c>ProbeResult.Failed(ProbeFailureCauses.Refused, …)</c>, and it is wrong
/// twice. <c>Refused</c> means the far end sent an RST — a real measurement of a real host — so a
/// policy refusal wearing that cause is permanently inseparable downstream from a genuine connection
/// refusal. And it would write our own security policy into a game's public reachability history,
/// which is the same class of lie as recording an unparseable WHO as zero players (§5.4), and is
/// exactly what §7.2 forbids.
/// </para>
/// <para>
/// A refusal belongs to whatever owns the dial: Plan 03 counts it as <c>CrawlCycle.Refused</c> and
/// reschedules with ordinary backoff. It is not a probe outcome, because no probe happened.
/// </para>
/// </remarks>
public enum ProbeOutcome
{
    /// <summary>The socket opened and the server said something usable.</summary>
    Answered,

    /// <summary>The exchange failed; <see cref="ProbeResult.Failure"/> says how.</summary>
    Failed,
}

/// <summary>How MSSP was obtained, if it was (spec §6.4).</summary>
public enum MsspTransport
{
    None,

    /// <summary>The telnet option, reached by asking — <c>IAC DO 70</c> (see Task 8).</summary>
    TelnetOption70,

    /// <summary>The plaintext <c>MSSP-REQUEST</c> reply, tab-separated between REPLY-START and REPLY-END.</summary>
    PlaintextRequest,
}

/// <summary>Why a probe failed, in the vocabulary the availability writer stores (spec §5.3).</summary>
public sealed record FailureDetail(string Cause, string? Detail = null);

/// <summary>
/// How much of a <c>WHO</c> response the structural parser could make sense of — and, first of all,
/// whether one was ever asked for.
/// </summary>
/// <remarks>
/// <para>
/// Parsing is structural rather than per-dialect (spec §6.3): find the trailing
/// "<c>N Players logged in</c>" summary, else count rows between the header rule and the footer.
/// Penn, MUX and Rhost all let operators rewrite the DOING header in softcode, so a per-codebase
/// parser would be a maintenance treadmill that still lost to any game that customised it.
/// </para>
/// <para>
/// <b>The two empty readings are not one reading.</b> <see cref="NotAttempted"/> means nobody asked;
/// <see cref="Unreadable"/> means we asked and could not make sense of the answer, which is spec
/// §5.4's middle row and writes a presence sample with a null count. These were a single value once,
/// and a downstream writer could not tell the specification's own named bug case from silence.
/// </para>
/// </remarks>
public sealed record WhoReading(WhoConfidence Confidence, int? Count = null, int? IdentifiablePlayers = null)
{
    /// <summary>We did not ask. Writes no presence sample at all.</summary>
    public static readonly WhoReading NotAttempted = new(WhoConfidence.NotAttempted);

    /// <summary>We asked and could not read the answer. Writes a sample with a null count (spec §5.4).</summary>
    public static readonly WhoReading Unreadable = new(WhoConfidence.Unknown);

    /// <summary>Whether a <c>WHO</c> or <c>DOING</c> was actually sent.</summary>
    public bool WasAttempted => Confidence is not WhoConfidence.NotAttempted;

    /// <summary>
    /// The count is trustworthy. Never synthesised: an unreadable WHO reports
    /// <see cref="WhoConfidence.Unknown"/> and the site falls back to MSSP <c>PLAYERS</c>, labelled
    /// as such. A parser that guessed zero would be indistinguishable from an empty game.
    /// </summary>
    public bool HasCount => Confidence is WhoConfidence.Count or WhoConfidence.PerPlayer && Count is not null;
}

public enum WhoConfidence
{
    /// <summary>
    /// We did not ask. An owner override said to use MSSP <c>PLAYERS</c> (spec §6.3), or
    /// <c>ProbeOptions.SendWho</c> is off, or the probe failed before layer 3.
    /// <b>Deliberately the zero value</b>, so a default-constructed reading claims nothing.
    /// </summary>
    NotAttempted,

    /// <summary>
    /// We asked and could not make sense of the answer. <b>This</b> is the state that writes a
    /// presence sample with <c>count = NULL</c> and <c>unmeasurable_reason = who_unparseable</c> —
    /// spec §5.4's middle row, rendered as a hatched cell rather than as downtime.
    /// </summary>
    Unknown,

    /// <summary>The number of connected players is readable.</summary>
    Count,

    /// <summary>
    /// The name column is positionally identifiable, so anonymised aggregates can be computed.
    /// Names are hashed with a rotating salt and never persisted (spec §11).
    /// </summary>
    PerPlayer,
}
```

- [ ] **Step 5: Add the cause vocabulary**

Create `src/MUI.Crawl/ProbeFailureCauses.cs`:

```csharp
namespace MUI.Crawl;

/// <summary>
/// The exact strings spec §5.3 names. <c>MUI.Crawl</c> cannot use <c>MUI.Catalog.FailureCause</c> —
/// that reference is forbidden — so the vocabulary crosses the boundary as these constants and
/// <c>MUI.Discovery</c> maps them (Plan 2's <c>FailureCauseMap</c>).
/// </summary>
public static class ProbeFailureCauses
{
    public const string Dns = "dns";

    /// <summary>
    /// The far end sent an RST — a real measurement of a real host that actively rejected us.
    /// <para>
    /// <b>This is not the cause for a host we declined to dial.</b> Plan 03's <c>HostScopeGuard</c>
    /// refuses a target whose name resolves into non-global space (spec §7.2), and that refusal must
    /// never be dressed as <c>ProbeResult.Failed(ProbeFailureCauses.Refused, …)</c>: it would make our
    /// own security policy indistinguishable from a game genuinely refusing connections, forever, and
    /// would write that policy into the game's public reachability history. A scope refusal produces
    /// no <see cref="ProbeResult"/> at all — see <see cref="ProbeOutcome"/>.
    /// </para>
    /// </summary>
    public const string Refused = "refused";

    public const string Tls = "tls";
    public const string Timeout = "timeout";

    /// <summary>
    /// The socket opened and the server then said nothing at all. Not thrown by anything, so it is
    /// never produced by <c>FailureClassifier</c>: <c>ProbeSession</c> decides it (Task 15).
    /// </summary>
    public const string HandshakeStalled = "handshake_stalled";

    public const string Unknown = "unknown";
}
```

- [ ] **Step 6: Add the target, the options and the interface**

Create `src/MUI.Crawl/ProbeOptions.cs`:

```csharp
namespace MUI.Crawl;

/// <summary>One address to visit. Plural per game: a game with a TLS port has two of these (spec §5.5).</summary>
public sealed record ProbeTarget
{
    public required string Host { get; init; }

    public required int Port { get; init; }

    public bool UseTls { get; init; }
}

/// <summary>
/// Every bound the probe obeys, and everything it says about itself.
/// </summary>
/// <remarks>
/// Every duration here is a correctness requirement rather than tuning (spec §12): the crawler shares
/// a process with the web tier, so a wedged probe that is not bounded starves request threads.
/// </remarks>
public sealed record ProbeOptions
{
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>The outer bound. Nothing in a probe may outlive this, whatever phase it is in.</summary>
    public TimeSpan HardTimeout { get; init; } = TimeSpan.FromSeconds(45);

    /// <summary>How long the connect screen must be silent before it is considered finished (spec §6.2).</summary>
    public TimeSpan BannerQuietPeriod { get; init; } = TimeSpan.FromSeconds(3);

    public TimeSpan WhoTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan MsspTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// What this crawler answers TTYPE/MTTS with, most specific first (spec §11). The first entry is
    /// the client name and is emitted with <see cref="InfoUrl"/> appended, so an admin reading their
    /// log can find out who we are and how to opt out. <c>MTTS 1</c> is ANSI and nothing else, which
    /// is all a crawler that stores the connect screen actually does.
    /// </summary>
    public IReadOnlyList<string> TerminalTypes { get; init; } = ["MUINDEX", "MUINDEX-CRAWLER", "MTTS 1"];

    public string InfoUrl { get; init; } = "https://muindex.org/crawler";

    /// <summary>
    /// Layer 3. Off makes the probe silent — negotiation only, SharpMUTerm's crawler exactly — and
    /// the result carries <see cref="WhoReading.NotAttempted"/> rather than an unreadable reading.
    /// That is the state an owner override saying "use MSSP <c>PLAYERS</c>" produces (spec §6.3), and
    /// it is why the two are separate values: nobody asked, so nothing failed.
    /// </summary>
    public bool SendWho { get; init; } = true;

    /// <summary>Layer 4's second half: the plaintext <c>MSSP-REQUEST</c> when option 70 yielded nothing.</summary>
    public bool PlaintextMsspFallback { get; init; } = true;

    /// <summary>
    /// The ceiling on any one captured transcript — connect screen or WHO reply.
    /// <para>
    /// Added beyond CONTRACT.md, deliberately: spec §13 requires surviving an enormous banner, and a
    /// cap that is not configurable cannot be tested at a sane size.
    /// </para>
    /// </summary>
    public int MaxCaptureBytes { get; init; } = 64 * 1024;
}

/// <summary>One visit to one address. Faked in tests; the real one opens a socket.</summary>
public interface IProbe
{
    Task<ProbeResult> ProbeAsync(ProbeTarget target, CancellationToken cancellationToken);
}
```

- [ ] **Step 7: Run the tests to verify they pass**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Crawl.Tests </dev/null
```
Expected: PASS — 5 new tests in `ProbeResultShapeTests` and 6 in the rewritten `WhoReadingTests`,
alongside Tasks 1 and 2, no warnings.

- [ ] **Step 8: Commit**

```bash
git add src/MUI.Crawl/ProbeResult.cs src/MUI.Crawl/ProbeOptions.cs \
        src/MUI.Crawl/ProbeFailureCauses.cs tests/MUI.Crawl.Tests/ProbeResultShapeTests.cs \
        tests/MUI.Crawl.Tests/WhoReadingTests.cs
git commit -m "feat(crawl): retype the seam, and split 'we never asked' from 'we could not read it'

ProbeResult carried IReadOnlyDictionary<string,string>, which cannot express an
array-valued MSSP variable — PORT and REFERRAL are both arrays and both are load
bearing — so Mssp becomes MUI.Crawl.Mssp.MsspData.

WhoReading.Unread was new(WhoConfidence.Unknown), so record equality made 'we never
sent WHO' equal to 'we sent WHO and could not parse the answer'. They have
different consequences: the first writes no presence sample, the second writes one
with a null count and unmeasurable_reason = who_unparseable, which is spec §5.4's
own named bug case. WhoConfidence.NotAttempted is now the zero value, Unread is
gone in favour of NotAttempted and Unreadable, WasAttempted is new and HasCount is
corrected — it asked 'not Unknown, and Count is not null', which answers yes for a
reading that never asked.

Adds MsspVia, TlsObserved and Aggregates to the seam, plus the target, the options
and the failure vocabulary the rest of the engine is built against."
```

---

### Task 4: The transport

Spec §6 (one telnet connection per target), §6.1 (TLS observed by completing a handshake or failing to).

Lifted from SharpMUTerm — `src/SharpMUTerm.Core/Transport/{ITransport,ConnectionOptions,TcpTransport}.cs`
— with two deliberate edits: the namespace, and a 15-second default connect timeout instead of 30
(a crawler visiting thousands of hosts cannot wait half a minute on each dead one).

"Lifted" means *copied into this repository and owned here*, not depended upon: both projects are MIT
and share an author, and after this task the file is MUIndex's to change. There is no build-time
relationship between the two repositories and there is not going to be one.

**Files:**
- Create: `src/MUI.Crawl/Transport/ITransport.cs`
- Create: `src/MUI.Crawl/Transport/ConnectionOptions.cs`
- Create: `src/MUI.Crawl/Transport/TcpTransport.cs`
- Test: `tests/MUI.Crawl.Tests/TransportTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `MUI.Crawl.Transport.ITransport` (`IsConnected`, `RemoteDescription`, `ConnectAsync`,
  `SendAsync`, `ReceiveAsync`, `CloseAsync`, `DisposeAsync`), `ConnectionOptions`,
  `sealed class TcpTransport(ConnectionOptions options) : ITransport`. Tasks 7, 8, 15 consume all three.

- [ ] **Step 1: Write the failing test**

Create `tests/MUI.Crawl.Tests/TransportTests.cs`:

```csharp
using System.Net;
using System.Net.Sockets;
using System.Text;
using MUI.Crawl.Transport;

namespace MUI.Crawl.Tests;

/// <summary>
/// The transport against a real loopback socket. In-memory fakes are used everywhere above this
/// line; this is the one place the socket itself is the subject.
/// </summary>
public class TransportTests
{
    [Test]
    public async Task ConnectingReadsWhatTheServerSaysAndReportsTheRemoteEnd()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serving = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            await client.GetStream().WriteAsync(Encoding.ASCII.GetBytes("Welcome.\r\n"));
            await client.GetStream().FlushAsync();
            await Task.Delay(50);
        });

        await using var transport = new TcpTransport(new ConnectionOptions
        {
            Host = "127.0.0.1",
            Port = port,
        });

        await transport.ConnectAsync(CancellationToken.None);

        await Assert.That(transport.IsConnected).IsTrue();
        await Assert.That(transport.RemoteDescription).IsNotNull();

        var buffer = new byte[64];
        var read = await transport.ReceiveAsync(buffer, CancellationToken.None);

        await Assert.That(Encoding.ASCII.GetString(buffer, 0, read)).IsEqualTo("Welcome.\r\n");

        await serving;
        listener.Stop();
    }

    [Test]
    public async Task AClosedRemoteEndIsAReadOfZeroRatherThanAnException()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serving = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            client.Close();
        });

        await using var transport = new TcpTransport(new ConnectionOptions { Host = "127.0.0.1", Port = port });
        await transport.ConnectAsync(CancellationToken.None);
        await serving;

        var buffer = new byte[64];
        var read = await transport.ReceiveAsync(buffer, CancellationToken.None);

        // A hung-up server is an answer, not a fault: the read loop above this has to end, not throw.
        await Assert.That(read).IsEqualTo(0);
        listener.Stop();
    }

    [Test]
    public async Task ARefusedConnectionThrowsTheSocketErrorTheClassifierNeeds()
    {
        // Bind and immediately release, so the port is almost certainly closed.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        await using var transport = new TcpTransport(new ConnectionOptions { Host = "127.0.0.1", Port = port });

        var error = await Assert.ThrowsAsync<SocketException>(() => transport.ConnectAsync(CancellationToken.None));

        await Assert.That(error.SocketErrorCode).IsEqualTo(SocketError.ConnectionRefused);
        await Assert.That(transport.IsConnected).IsFalse();
    }

    [Test]
    public async Task TheDefaultConnectTimeoutIsFifteenSecondsBecauseACrawlerVisitsThousandsOfHosts()
    {
        await Assert.That(new ConnectionOptions { Host = "h", Port = 1 }.ConnectTimeout)
            .IsEqualTo(TimeSpan.FromSeconds(15));
    }

    [Test]
    public async Task TheDescriptionSaysWhetherTlsWasAskedFor()
    {
        await Assert.That(new ConnectionOptions { Host = "h", Port = 4201 }.ToString()).IsEqualTo("telnet://h:4201");
        await Assert.That(new ConnectionOptions { Host = "h", Port = 4202, UseTls = true }.ToString())
            .IsEqualTo("telnets://h:4202");
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `error CS0234: The type or namespace name 'Transport' does not exist in the namespace 'MUI.Crawl'`.

- [ ] **Step 3: Write the interface and the options**

Create `src/MUI.Crawl/Transport/ITransport.cs`:

```csharp
namespace MUI.Crawl.Transport;

/// <summary>
/// A bidirectional byte transport (TCP, optionally TLS). Kept deliberately minimal so it can be
/// faked and decorated; all telnet logic sits above it.
/// </summary>
/// <remarks>
/// Lifted from SharpMUTerm's <c>SharpMUTerm.Core.Transport.ITransport</c>, unchanged but for the
/// namespace. The decorator in <see cref="NegotiationRecorder"/> is why the shape matters: layer 1
/// is measured by wrapping this interface, not by asking the telnet library.
/// </remarks>
public interface ITransport : IAsyncDisposable
{
    /// <summary>True once <see cref="ConnectAsync"/> has completed and the link is open.</summary>
    bool IsConnected { get; }

    /// <summary>A human-readable description of the remote endpoint, once connected.</summary>
    string? RemoteDescription { get; }

    /// <summary>Opens the connection (DNS resolution, TCP, and TLS handshake if configured).</summary>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>Writes bytes to the transport.</summary>
    ValueTask SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads available bytes into <paramref name="buffer"/>. Returns the number of bytes read, or 0
    /// when the remote end has closed the connection.
    /// </summary>
    ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken = default);

    /// <summary>Closes the connection.</summary>
    Task CloseAsync();
}
```

Create `src/MUI.Crawl/Transport/ConnectionOptions.cs`:

```csharp
namespace MUI.Crawl.Transport;

/// <summary>Immutable connection parameters for one visit to one address.</summary>
public sealed class ConnectionOptions
{
    /// <summary>Hostname or IP literal (IPv4 or IPv6).</summary>
    public required string Host { get; init; }

    /// <summary>TCP port.</summary>
    public required int Port { get; init; }

    /// <summary>When true, wrap the socket in TLS via <see cref="System.Net.Security.SslStream"/>.</summary>
    public bool UseTls { get; init; }

    /// <summary>
    /// When true, accept server certificates that fail validation. Self-signed certificates are
    /// common on hobbyist MU* servers, and a crawler measuring <em>whether TLS completes</em> is not
    /// trusting the peer with anything. Off by default all the same.
    /// </summary>
    public bool AllowInvalidCertificates { get; init; }

    /// <summary>SNI / certificate target host. Defaults to <see cref="Host"/> when null.</summary>
    public string? TlsTargetHost { get; init; }

    /// <summary>
    /// Socket connect timeout. Fifteen seconds rather than SharpMUTerm's thirty: a person opening one
    /// world can wait, and a crawler visiting thousands of addresses cannot.
    /// </summary>
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(15);

    public override string ToString() => $"{(UseTls ? "telnets" : "telnet")}://{Host}:{Port}";
}
```

- [ ] **Step 4: Write the TCP transport**

Create `src/MUI.Crawl/Transport/TcpTransport.cs`:

```csharp
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace MUI.Crawl.Transport;

/// <summary>
/// A TCP transport with optional TLS. DNS resolution via <see cref="TcpClient"/> makes it dual-stack
/// (IPv4 and IPv6). When <see cref="ConnectionOptions.UseTls"/> is set, the network stream is wrapped
/// in an <see cref="SslStream"/> and authenticated as a client — which is exactly how spec §6.1's
/// "TLS is observed by completing a handshake or failing to" is measured.
/// </summary>
/// <remarks>Lifted from SharpMUTerm's <c>SharpMUTerm.Core.Transport.TcpTransport</c>.</remarks>
public sealed class TcpTransport(ConnectionOptions options) : ITransport
{
    private readonly ConnectionOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private TcpClient? _client;
    private Stream? _stream;

    public bool IsConnected => _client?.Connected == true;

    public string? RemoteDescription { get; private set; }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_client is not null)
        {
            throw new InvalidOperationException("Transport is already connected.");
        }

        var client = new TcpClient { NoDelay = true };
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_options.ConnectTimeout);
            await client.ConnectAsync(_options.Host, _options.Port, timeoutCts.Token).ConfigureAwait(false);

            Stream stream = client.GetStream();
            if (_options.UseTls)
            {
                stream = await AuthenticateTlsAsync(stream, cancellationToken).ConfigureAwait(false);
            }

            _client = client;
            _stream = stream;
            RemoteDescription = client.Client.RemoteEndPoint?.ToString() ?? _options.ToString();
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private async Task<SslStream> AuthenticateTlsAsync(Stream inner, CancellationToken cancellationToken)
    {
        var ssl = new SslStream(inner, leaveInnerStreamOpen: false, ValidateCertificate);
        var sslOptions = new SslClientAuthenticationOptions
        {
            TargetHost = _options.TlsTargetHost ?? _options.Host,
            EnabledSslProtocols = SslProtocols.None, // let the OS negotiate TLS 1.2/1.3
        };

        await ssl.AuthenticateAsClientAsync(sslOptions, cancellationToken).ConfigureAwait(false);
        return ssl;
    }

    private bool ValidateCertificate(
        object sender,
        X509Certificate? certificate,
        X509Chain? chain,
        SslPolicyErrors errors)
        => errors == SslPolicyErrors.None || _options.AllowInvalidCertificates;

    public async ValueTask SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        var stream = _stream ?? throw new InvalidOperationException("Transport is not connected.");

        // Serialise writes: the negotiation the telnet layer emits and the three lines the probe
        // sends can reach here from different tasks, and overlapping writes corrupt IAC framing.
        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await stream.WriteAsync(data, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var stream = _stream ?? throw new InvalidOperationException("Transport is not connected.");
        try
        {
            return await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException)
        {
            // A broken or closed socket is a clean end of stream. The read loop above ends; it must
            // not throw, because a server hanging up is an ordinary answer.
            return 0;
        }
    }

    public Task CloseAsync()
    {
        try
        {
            _stream?.Dispose();
            _client?.Close();
        }
        catch
        {
            // Best-effort close.
        }

        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync().ConfigureAwait(false);
        _client?.Dispose();
        _client = null;
        _stream = null;
        _sendLock.Dispose();
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Crawl.Tests </dev/null
```
Expected: PASS — 5 new tests.

- [ ] **Step 6: Commit**

```bash
git add src/MUI.Crawl/Transport tests/MUI.Crawl.Tests/TransportTests.cs
git commit -m "feat(crawl): a TCP transport with TLS, lifted from SharpMUTerm

Same interface, same SslStream handling, same treat-a-dead-socket-as-EOF rule.
Two edits: the namespace, and a 15-second connect timeout — a person opening one
world can wait thirty, a crawler visiting thousands of addresses cannot."
```

---

### Task 5: The scripted fake MU\* server

Spec §13 ("a scripted fake MU\* server ... tests the probe engine end to end"). This is the backbone
of every later task, so it comes before anything it tests.

**Why a real `TcpListener` and not SharpMUTerm's in-memory `ScriptedTransport`.** SharpMUTerm's fake
implements `ITransport`, which means `TcpTransport` itself is never executed by any test in that
suite. Here the transport is ours to get right — dual-stack resolution, the TLS wrapper, EOF
handling, cancellation — and §13 asks for deliberate misbehaviour (half-open connections above all)
that has no meaning above the socket. A loopback listener costs a few milliseconds per test and
exercises the whole stack.

**Files:**
- Create: `tests/MUI.Crawl.Tests/Support/TelnetWire.cs`
- Create: `tests/MUI.Crawl.Tests/Support/ScriptedMuServer.cs`
- Test: `tests/MUI.Crawl.Tests/ScriptedMuServerTests.cs`

**Interfaces:**
- Consumes: `MUI.Crawl.Transport.{ITransport, ConnectionOptions, TcpTransport}` (Task 4).
- Produces: `MUI.Crawl.Tests.Support.TelnetWire` (`Iac`, `Sb`, `Se`, `Will`, `Wont`, `Do`, `Dont`,
  `Mssp`, `Ttype`, `Gmcp`, `Subnegotiation(params (string, string[])[])`, `Offer(byte)`,
  `PlaintextMssp(params (string, string)[])`);
  `MUI.Crawl.Tests.Support.ScriptedMuServer` — `Port`, `Greeting`, `Misbehave`,
  `EnormousBannerBytes`, `Listen()`, `RespondingToDo(byte, byte[])`,
  `RespondingToCommand(string, string)`, `Received`, `ReceivedText`, `Commands`,
  `WaitForReceivedAsync(byte[])`, `WaitForCommandAsync(string)`, `DisposeAsync()`;
  and `[Flags] enum Misbehaviour`. Tasks 6–17 all use it.

- [ ] **Step 1: Write the failing test**

Create `tests/MUI.Crawl.Tests/ScriptedMuServerTests.cs`:

```csharp
using System.Text;
using MUI.Crawl.Tests.Support;
using MUI.Crawl.Transport;

namespace MUI.Crawl.Tests;

/// <summary>
/// The harness testing itself. Everything else in this suite trusts these four behaviours, so they
/// are pinned against a real <see cref="TcpTransport"/> rather than assumed.
/// </summary>
public class ScriptedMuServerTests
{
    private static async Task<TcpTransport> ConnectTo(ScriptedMuServer server)
    {
        var transport = new TcpTransport(new ConnectionOptions { Host = "127.0.0.1", Port = server.Port });
        await transport.ConnectAsync(CancellationToken.None);
        return transport;
    }

    private static async Task<string> ReadSomeAsync(ITransport transport, int atLeast)
    {
        var text = new StringBuilder();
        var buffer = new byte[8192];
        while (text.Length < atLeast)
        {
            var read = await transport.ReceiveAsync(buffer, CancellationToken.None);
            if (read == 0)
            {
                break;
            }

            text.Append(Encoding.ASCII.GetString(buffer, 0, read));
        }

        return text.ToString();
    }

    [Test]
    public async Task TheGreetingArrivesOnConnect()
    {
        await using var server = new ScriptedMuServer { Greeting = Encoding.ASCII.GetBytes("Welcome to Corvid.\r\n") };
        server.Listen();

        await using var transport = await ConnectTo(server);

        await Assert.That(await ReadSomeAsync(transport, 20)).IsEqualTo("Welcome to Corvid.\r\n");
    }

    [Test]
    public async Task AnIacDoIsAnsweredWithWhateverWasScripted()
    {
        await using var server = new ScriptedMuServer();
        server.RespondingToDo(TelnetWire.Mssp, TelnetWire.Subnegotiation(("NAME", ["Silent Until Asked"])));
        server.Listen();

        await using var transport = await ConnectTo(server);
        await transport.SendAsync(new byte[] { TelnetWire.Iac, TelnetWire.Do, TelnetWire.Mssp }, CancellationToken.None);

        var buffer = new byte[256];
        var read = await transport.ReceiveAsync(buffer, CancellationToken.None);

        await Assert.That(read).IsGreaterThan(0);
        await Assert.That(buffer[0]).IsEqualTo(TelnetWire.Iac);
        await Assert.That(buffer[1]).IsEqualTo(TelnetWire.Sb);
        await Assert.That(Encoding.ASCII.GetString(buffer, 0, read)).Contains("Silent Until Asked");
    }

    [Test]
    public async Task AWhoReplyIsKeyedOnTheLiteralCommandTheServerReceived()
    {
        await using var server = new ScriptedMuServer();
        server.RespondingToCommand("WHO", "Player Name\r\nAlice\r\n1 Players logged in.\r\n");
        server.RespondingToCommand("DOING", "nobody is doing anything\r\n");
        server.Listen();

        await using var transport = await ConnectTo(server);
        await transport.SendAsync(Encoding.ASCII.GetBytes("WHO\r\n"), CancellationToken.None);

        await Assert.That(await ReadSomeAsync(transport, 40)).Contains("1 Players logged in.");
        await Assert.That(await server.WaitForCommandAsync("WHO")).IsTrue();
        await Assert.That(server.Commands).IsEquivalentTo(new[] { "WHO" });
    }

    [Test]
    public async Task AnUnscriptedCommandIsRecordedAndAnsweredWithNothing()
    {
        // What a crawler sending something it should not have sent looks like from the server's side.
        await using var server = new ScriptedMuServer();
        server.Listen();

        await using var transport = await ConnectTo(server);
        await transport.SendAsync(Encoding.ASCII.GetBytes("connect wizard hunter2\r\n"), CancellationToken.None);

        await Assert.That(await server.WaitForCommandAsync("connect wizard hunter2")).IsTrue();
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `error CS0246: The type or namespace name 'ScriptedMuServer' could not be found`.

- [ ] **Step 3: Write the wire helper**

Create `tests/MUI.Crawl.Tests/Support/TelnetWire.cs`:

```csharp
using System.Text;

namespace MUI.Crawl.Tests.Support;

/// <summary>
/// Telnet and MSSP spelled byte by byte, exactly as the specifications do, so a test asserts against
/// the wire rather than against a model's idea of it.
/// </summary>
public static class TelnetWire
{
    public const byte Iac = 255;
    public const byte Se = 240;
    public const byte Sb = 250;
    public const byte Will = 251;
    public const byte Wont = 252;
    public const byte Do = 253;
    public const byte Dont = 254;

    public const byte Echo = 1;
    public const byte SuppressGoAhead = 3;
    public const byte Ttype = 24;
    public const byte Eor = 25;
    public const byte Naws = 31;
    public const byte NewEnviron = 39;
    public const byte Charset = 42;
    public const byte Msdp = 69;
    public const byte Mssp = 70;
    public const byte Mccp2 = 86;
    public const byte Msp = 90;
    public const byte Mxp = 91;
    public const byte Gmcp = 201;

    public const byte MsspVar = 1;
    public const byte MsspVal = 2;

    public const byte TtypeIs = 0;
    public const byte TtypeSend = 1;

    /// <summary>
    /// <c>IAC SB MSSP MSSP_VAR "x" MSSP_VAL "y" … IAC SE</c>. Each entry is one variable with all of
    /// its values, so array notation — several <c>MSSP_VAL</c> under one <c>MSSP_VAR</c> — is
    /// expressible, which is the whole point of MSSP for a crawler.
    /// </summary>
    public static byte[] Subnegotiation(params (string Variable, string[] Values)[] entries)
    {
        var bytes = new List<byte> { Iac, Sb, Mssp };
        foreach (var (variable, values) in entries)
        {
            bytes.Add(MsspVar);
            bytes.AddRange(Encoding.UTF8.GetBytes(variable));
            foreach (var value in values)
            {
                bytes.Add(MsspVal);
                bytes.AddRange(Encoding.UTF8.GetBytes(value));
            }
        }

        bytes.AddRange([Iac, Se]);
        return [.. bytes];
    }

    /// <summary>A server volunteering an option: <c>IAC WILL &lt;option&gt;</c>.</summary>
    public static byte[] Offer(byte option) => [Iac, Will, option];

    /// <summary>A server asking the client to enable an option: <c>IAC DO &lt;option&gt;</c>.</summary>
    public static byte[] Ask(byte option) => [Iac, Do, option];

    /// <summary>
    /// The plaintext <c>MSSP-REQUEST</c> reply: tab-separated pairs between the delimiters
    /// (spec §6.4). The fallback for servers that implement MSSP without the telnet option.
    /// </summary>
    public static string PlaintextMssp(params (string Variable, string Value)[] entries)
    {
        var text = new StringBuilder("MSSP-REPLY-START\r\n");
        foreach (var (variable, value) in entries)
        {
            text.Append(variable).Append('\t').Append(value).Append("\r\n");
        }

        return text.Append("MSSP-REPLY-END\r\n").ToString();
    }

    /// <summary>A representative report, used by several tests. Arrays where real servers use arrays.</summary>
    public static (string Variable, string[] Values)[] RepresentativeReport(params string[] referrals) =>
    [
        ("NAME", ["Corvid Nest"]),
        ("PLAYERS", ["17"]),
        ("UPTIME", ["1735689600"]),
        ("PORT", ["23", "4201"]),
        ("HOSTNAME", ["corvid.example.org"]),
        ("CODEBASE", ["PennMUSH 1.8.8"]),
        ("CONTACT", ["admin@corvid.example.org"]),
        ("CRAWL DELAY", ["5"]),
        ("WEBSITE", ["https://corvid.example.org/"]),
        ("FAMILY", ["TinyMUSH"]),
        ("GENRE", ["Fantasy"]),
        ("STATUS", ["Live"]),
        ("CREATED", ["2003"]),
        ("REFERRAL", referrals),
    ];
}
```

- [ ] **Step 4: Write the harness**

Create `tests/MUI.Crawl.Tests/Support/ScriptedMuServer.cs`:

```csharp
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace MUI.Crawl.Tests.Support;

/// <summary>
/// Spec §13's deliberate misbehaviour. Flags, because a real bad server is usually several of these
/// at once — the enormous banner that then goes half-open is not a hypothetical.
/// </summary>
[Flags]
public enum Misbehaviour
{
    None = 0,

    /// <summary>
    /// Writes the greeting, then shuts down its sending half and keeps reading for ever. The client's
    /// reads return 0 while its writes keep succeeding — the case where waiting for more output is
    /// waiting for something that can never arrive.
    /// </summary>
    HalfOpen = 1,

    /// <summary>
    /// Opens <c>IAC SB MSSP …</c> and never sends <c>IAC SE</c>. The telnet layer must stay inside the
    /// subnegotiation for ever without emitting garbage as text, and the probe's own deadline must be
    /// what ends the visit.
    /// </summary>
    TruncatedSubnegotiation = 2,

    /// <summary>Megabytes of banner. The connect-screen capture has to be bounded (spec §6.2).</summary>
    EnormousBanner = 4,

    /// <summary>
    /// Accepts the connection and says nothing at all, for ever. Not a failure to connect and not a
    /// refusal — it is spec §5.3's <c>handshake_stalled</c>, and it is the one a naive probe hangs on.
    /// </summary>
    SilentAfterAccept = 8,
}

/// <summary>
/// A scriptable fake MU* server on a real loopback socket (spec §13).
/// </summary>
/// <remarks>
/// <para>
/// A real <see cref="TcpListener"/> rather than SharpMUTerm's in-memory <c>ScriptedTransport</c>: a
/// fake that implements <c>ITransport</c> never executes our own transport, and half-open connections
/// have no meaning above the socket. The cost is a few milliseconds per test.
/// </para>
/// <para>
/// It is a <em>server</em>, not a tape: MSSP is a conversation — the report is only sent once the
/// client has said <c>IAC DO MSSP</c> — and the WHO reply is keyed on the literal command line
/// received, so a probe that sent nothing gets nothing, which is exactly the property Task 15 asserts.
/// </para>
/// </remarks>
public sealed class ScriptedMuServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Lock _gate = new();
    private readonly List<byte> _received = [];
    private readonly List<string> _commands = [];
    private readonly Dictionary<byte, byte[]> _onDo = [];
    private readonly Dictionary<string, string> _onCommand = new(StringComparer.OrdinalIgnoreCase);
    private Task? _accepting;

    public ScriptedMuServer()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
    }

    /// <summary>The bound port. Available before <see cref="Listen"/>, because the listener is already up.</summary>
    public int Port { get; }

    /// <summary>Bytes written the moment a connection is accepted — the server's opening negotiation.</summary>
    public byte[] Greeting { get; init; } = [];

    /// <summary>How long to wait before writing the greeting. Zero unless a test is about the quiet period.</summary>
    public TimeSpan GreetingDelay { get; init; } = TimeSpan.Zero;

    public Misbehaviour Misbehave { get; init; } = Misbehaviour.None;

    /// <summary>Size of <see cref="Misbehaviour.EnormousBanner"/>. A megabyte by default.</summary>
    public int EnormousBannerBytes { get; init; } = 1024 * 1024;

    /// <summary>Everything the client has written, in order — negotiation and data alike.</summary>
    public byte[] Received
    {
        get
        {
            lock (_gate)
            {
                return [.. _received];
            }
        }
    }

    public string ReceivedText => Encoding.UTF8.GetString(Received);

    /// <summary>Every complete line of application data received, in order. The politeness assertion.</summary>
    public IReadOnlyList<string> Commands
    {
        get
        {
            lock (_gate)
            {
                return [.. _commands];
            }
        }
    }

    /// <summary>Starts accepting. Separate from the constructor so <c>init</c> properties can be set first.</summary>
    public ScriptedMuServer Listen()
    {
        _accepting = Task.Run(() => AcceptLoopAsync(_cts.Token), CancellationToken.None);
        return this;
    }

    /// <summary>When the client says <c>IAC DO &lt;option&gt;</c>, write <paramref name="response"/>.</summary>
    public ScriptedMuServer RespondingToDo(byte option, byte[] response)
    {
        lock (_gate)
        {
            _onDo[option] = response;
        }

        return this;
    }

    /// <summary>When the client sends this exact line (case-insensitively), write <paramref name="reply"/>.</summary>
    public ScriptedMuServer RespondingToCommand(string command, string reply)
    {
        lock (_gate)
        {
            _onCommand[command.Trim()] = reply;
        }

        return this;
    }

    /// <summary>Waits up to two seconds for <paramref name="expected"/> to appear in what was received.</summary>
    public async Task<bool> WaitForReceivedAsync(byte[] expected)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var received = Received;
            for (var at = 0; at + expected.Length <= received.Length; at++)
            {
                if (received.AsSpan(at, expected.Length).SequenceEqual(expected))
                {
                    return true;
                }
            }

            await Task.Delay(10).ConfigureAwait(false);
        }

        return false;
    }

    /// <summary>Waits up to two seconds for a complete command line equal to <paramref name="command"/>.</summary>
    public async Task<bool> WaitForCommandAsync(string command)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (Commands.Any(c => string.Equals(c, command, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            await Task.Delay(10).ConfigureAwait(false);
        }

        return false;
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                return;
            }

            _ = Task.Run(() => ServeAsync(client, cancellationToken), CancellationToken.None);
        }
    }

    private async Task ServeAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using var owned = client;
        try
        {
            var stream = client.GetStream();

            if (Misbehave.HasFlag(Misbehaviour.SilentAfterAccept))
            {
                // Accepted, and then nothing — for ever. Only the client's own deadline ends this.
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (GreetingDelay > TimeSpan.Zero)
            {
                await Task.Delay(GreetingDelay, cancellationToken).ConfigureAwait(false);
            }

            if (Misbehave.HasFlag(Misbehaviour.EnormousBanner))
            {
                await WriteAsync(stream, EnormousBanner(), cancellationToken).ConfigureAwait(false);
            }

            if (Greeting.Length > 0)
            {
                await WriteAsync(stream, Greeting, cancellationToken).ConfigureAwait(false);
            }

            if (Misbehave.HasFlag(Misbehaviour.TruncatedSubnegotiation))
            {
                // IAC SB MSSP MSSP_VAR "NAME" MSSP_VAL "Truncated" … and no IAC SE, ever.
                var truncated = new List<byte> { TelnetWire.Iac, TelnetWire.Sb, TelnetWire.Mssp, TelnetWire.MsspVar };
                truncated.AddRange(Encoding.ASCII.GetBytes("NAME"));
                truncated.Add(TelnetWire.MsspVal);
                truncated.AddRange(Encoding.ASCII.GetBytes("Truncated"));
                await WriteAsync(stream, [.. truncated], cancellationToken).ConfigureAwait(false);
            }

            if (Misbehave.HasFlag(Misbehaviour.HalfOpen))
            {
                // We will never speak again. We keep reading, so the client's writes keep succeeding.
                client.Client.Shutdown(SocketShutdown.Send);
            }

            var line = new List<byte>();
            var buffer = new byte[8192];
            while (!cancellationToken.IsCancellationRequested)
            {
                var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    return;
                }

                lock (_gate)
                {
                    _received.AddRange(buffer.AsSpan(0, read).ToArray());
                }

                var responses = React(buffer.AsSpan(0, read), line);
                if (Misbehave.HasFlag(Misbehaviour.HalfOpen))
                {
                    continue; // reading only; the sending half is shut
                }

                foreach (var response in responses)
                {
                    await WriteAsync(stream, response, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException or ObjectDisposedException)
        {
            // A test finishing is how this connection normally ends.
        }
    }

    private byte[] EnormousBanner()
    {
        // Printable, line-broken, and not valid telnet framing anywhere in it — a wall of text, which
        // is what an ASCII-art connect screen from 1997 actually is.
        var chunk = new string('#', 78) + "\r\n";
        var text = new StringBuilder(EnormousBannerBytes + chunk.Length);
        while (text.Length < EnormousBannerBytes)
        {
            text.Append(chunk);
        }

        return Encoding.ASCII.GetBytes(text.ToString());
    }

    /// <summary>
    /// Walks the client's bytes, answering <c>IAC DO</c> from the script and assembling data bytes
    /// into command lines. Deliberately small: it only has to find the sequences a probe emits.
    /// </summary>
    private List<byte[]> React(ReadOnlySpan<byte> bytes, List<byte> line)
    {
        var responses = new List<byte[]>();
        var i = 0;
        while (i < bytes.Length)
        {
            var b = bytes[i];
            if (b != TelnetWire.Iac)
            {
                if (b == (byte)'\n')
                {
                    var text = Encoding.UTF8.GetString([.. line]).TrimEnd('\r');
                    line.Clear();
                    if (text.Length > 0)
                    {
                        lock (_gate)
                        {
                            _commands.Add(text);
                            if (_onCommand.TryGetValue(text.Trim(), out var reply))
                            {
                                responses.Add(Encoding.UTF8.GetBytes(reply));
                            }
                        }
                    }
                }
                else
                {
                    line.Add(b);
                }

                i++;
                continue;
            }

            if (i + 1 >= bytes.Length)
            {
                break;
            }

            var command = bytes[i + 1];
            if (command == TelnetWire.Sb)
            {
                // Skip the whole subnegotiation, honouring IAC IAC inside it.
                i += 2;
                while (i + 1 < bytes.Length && !(bytes[i] == TelnetWire.Iac && bytes[i + 1] == TelnetWire.Se))
                {
                    i += bytes[i] == TelnetWire.Iac && bytes[i + 1] == TelnetWire.Iac ? 2 : 1;
                }

                i += 2;
                continue;
            }

            if (command is >= TelnetWire.Will and <= TelnetWire.Dont)
            {
                if (i + 2 < bytes.Length)
                {
                    if (command == TelnetWire.Do)
                    {
                        lock (_gate)
                        {
                            if (_onDo.TryGetValue(bytes[i + 2], out var response))
                            {
                                responses.Add(response);
                            }
                        }
                    }

                    i += 3;
                    continue;
                }

                break;
            }

            i += 2;
        }

        return responses;
    }

    private static async Task WriteAsync(NetworkStream stream, byte[] bytes, CancellationToken cancellationToken)
    {
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        _listener.Stop();

        if (_accepting is not null)
        {
            try
            {
                await _accepting.ConfigureAwait(false);
            }
            catch
            {
                // Cancellation is how this ends.
            }
        }

        _cts.Dispose();
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Crawl.Tests </dev/null
```
Expected: PASS — 4 new tests.

- [ ] **Step 6: Commit**

```bash
git add tests/MUI.Crawl.Tests/Support tests/MUI.Crawl.Tests/ScriptedMuServerTests.cs
git commit -m "test(crawl): a scripted fake MU* server on a real loopback socket

Spec §13's end-to-end harness. A real TcpListener rather than SharpMUTerm's
in-memory ScriptedTransport, because a fake ITransport never executes our own
transport and half-open connections have no meaning above the socket. Scriptable
with a greeting, IAC DO responders and a WHO reply keyed on the literal command
line, so a probe that sent nothing gets nothing."
```

---

### Task 6: The misbehaviour switches

Spec §13 ("deliberate misbehaviour: half-open connections, truncated subnegotiation, enormous
banners"), plus the fourth case §5.3's `handshake_stalled` exists for.

The switches were written in Task 5; this task proves each one actually misbehaves, against the real
transport. Without this, a later "the probe survives an enormous banner" test could pass because the
banner was never sent.

**Files:**
- Test: `tests/MUI.Crawl.Tests/MisbehaviourTests.cs`

**Interfaces:**
- Consumes: `ScriptedMuServer`, `Misbehaviour`, `TelnetWire` (Task 5); `TcpTransport`,
  `ConnectionOptions` (Task 4).
- Produces: nothing new — a proof that Tasks 9, 13 and 15 are testing what they claim.

- [ ] **Step 1: Write the failing test**

Create `tests/MUI.Crawl.Tests/MisbehaviourTests.cs`:

```csharp
using System.Diagnostics;
using System.Text;
using MUI.Crawl.Tests.Support;
using MUI.Crawl.Transport;

namespace MUI.Crawl.Tests;

/// <summary>
/// Spec §13's deliberate misbehaviour, proven to misbehave. Every later "the probe survives X" test
/// is worthless if X never happened, and a harness that quietly does nothing is the easiest way for
/// a suite to be green about a case it does not exercise.
/// </summary>
public class MisbehaviourTests
{
    private static async Task<TcpTransport> ConnectTo(ScriptedMuServer server)
    {
        var transport = new TcpTransport(new ConnectionOptions { Host = "127.0.0.1", Port = server.Port });
        await transport.ConnectAsync(CancellationToken.None);
        return transport;
    }

    private static async Task<int> DrainAsync(ITransport transport, TimeSpan within)
    {
        var total = 0;
        var buffer = new byte[64 * 1024];
        using var cts = new CancellationTokenSource(within);
        try
        {
            while (true)
            {
                var read = await transport.ReceiveAsync(buffer, cts.Token);
                if (read == 0)
                {
                    return total;
                }

                total += read;
            }
        }
        catch (OperationCanceledException)
        {
            return total;
        }
    }

    [Test]
    public async Task AHalfOpenServerStopsSpeakingWhileStillAcceptingWhatWeWrite()
    {
        await using var server = new ScriptedMuServer
        {
            Greeting = Encoding.ASCII.GetBytes("Half a conversation.\r\n"),
            Misbehave = Misbehaviour.HalfOpen,
        };
        server.Listen();

        await using var transport = await ConnectTo(server);

        // Everything it will ever say, then a clean end of stream — not an error.
        var buffer = new byte[64];
        var first = await transport.ReceiveAsync(buffer, CancellationToken.None);
        await Assert.That(Encoding.ASCII.GetString(buffer, 0, first)).IsEqualTo("Half a conversation.\r\n");
        await Assert.That(await transport.ReceiveAsync(buffer, CancellationToken.None)).IsEqualTo(0);

        // And our writes still land, which is what makes this half-open rather than closed.
        await transport.SendAsync(Encoding.ASCII.GetBytes("WHO\r\n"), CancellationToken.None);
        await Assert.That(await server.WaitForCommandAsync("WHO")).IsTrue();
    }

    [Test]
    public async Task ATruncatedSubnegotiationIsOpenedAndNeverClosed()
    {
        await using var server = new ScriptedMuServer { Misbehave = Misbehaviour.TruncatedSubnegotiation };
        server.Listen();

        await using var transport = await ConnectTo(server);

        var buffer = new byte[256];
        var read = await transport.ReceiveAsync(buffer, CancellationToken.None);
        var received = buffer.AsSpan(0, read).ToArray();

        await Assert.That(received[0]).IsEqualTo(TelnetWire.Iac);
        await Assert.That(received[1]).IsEqualTo(TelnetWire.Sb);
        await Assert.That(received[2]).IsEqualTo(TelnetWire.Mssp);
        await Assert.That(Encoding.ASCII.GetString(received)).Contains("Truncated");

        // No IAC SE anywhere, and none coming.
        var hasTerminator = false;
        for (var i = 0; i + 1 < received.Length; i++)
        {
            hasTerminator |= received[i] == TelnetWire.Iac && received[i + 1] == TelnetWire.Se;
        }

        await Assert.That(hasTerminator).IsFalse();
        await Assert.That(await DrainAsync(transport, TimeSpan.FromMilliseconds(300))).IsEqualTo(0);
    }

    [Test]
    public async Task AnEnormousBannerIsGenuinelyEnormous()
    {
        await using var server = new ScriptedMuServer
        {
            Misbehave = Misbehaviour.EnormousBanner,
            EnormousBannerBytes = 512 * 1024,
        };
        server.Listen();

        await using var transport = await ConnectTo(server);

        var received = await DrainAsync(transport, TimeSpan.FromSeconds(5));

        await Assert.That(received).IsGreaterThanOrEqualTo(512 * 1024);
    }

    [Test]
    public async Task ASilentServerAcceptsTheConnectionAndSaysNothingAtAll()
    {
        await using var server = new ScriptedMuServer { Misbehave = Misbehaviour.SilentAfterAccept };
        server.Listen();

        var stopwatch = Stopwatch.StartNew();
        await using var transport = await ConnectTo(server);

        // Connecting succeeds — this is not "refused", and it is not a DNS failure. It is spec §5.3's
        // handshake_stalled, and the only thing that will ever end it is our own deadline.
        await Assert.That(transport.IsConnected).IsTrue();
        await Assert.That(await DrainAsync(transport, TimeSpan.FromMilliseconds(400))).IsEqualTo(0);
        await Assert.That(stopwatch.Elapsed).IsLessThan(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task MisbehaviourCombines()
    {
        // The enormous banner that then goes half-open — not hypothetical, and the combination is
        // what a bounded capture plus an EOF-tolerant read loop have to survive together.
        await using var server = new ScriptedMuServer
        {
            Misbehave = Misbehaviour.EnormousBanner | Misbehaviour.HalfOpen,
            EnormousBannerBytes = 128 * 1024,
        };
        server.Listen();

        await using var transport = await ConnectTo(server);

        await Assert.That(await DrainAsync(transport, TimeSpan.FromSeconds(5)))
            .IsGreaterThanOrEqualTo(128 * 1024);
    }
}
```

- [ ] **Step 2: Run the tests to verify they pass**

These exercise code written in Task 5, so they should pass on the first run. That is the point of the
task: it is a verification gate, not new behaviour.

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Crawl.Tests </dev/null
```
Expected: PASS — 5 new tests.

- [ ] **Step 3: If any of them fails, fix the harness**

The likely failure is `AnEnormousBannerIsGenuinelyEnormous` timing out, because a single
`WriteAsync` of a megabyte blocks until the client drains it. `DrainAsync` reads concurrently, so it
should not — but if it does on your platform, lower `EnormousBannerBytes` in the test rather than
changing the harness, and note it. Do **not** "fix" it by making the banner smaller in
`ScriptedMuServer`'s default; Task 9 needs a banner that genuinely exceeds `MaxCaptureBytes`.

- [ ] **Step 4: Commit**

```bash
git add tests/MUI.Crawl.Tests/MisbehaviourTests.cs
git commit -m "test(crawl): prove the misbehaviour switches actually misbehave

Half-open really stops speaking while still reading; the truncated subnegotiation
really never sends IAC SE; the enormous banner is really enormous; the silent
server really accepts and says nothing. Every later 'the probe survives X' test is
worthless if X never happened."
```

---

### Task 7: Layer 1 — the handshake capability recorder

Spec §6.1: "What the server offers via `IAC WILL/DO` is *observed*, not claimed."

**Why a transport decorator and not a library callback.** `TelnetNegotiationCore` negotiates all of
this — GMCP, MSDP, MCCP, MXP, EOR, NAWS, CHARSET, TTYPE, MSSP — and exposes **no surface for "what
did the server offer"**. Its callbacks fire when a *negotiation completed successfully* and are
per-protocol (`onGMCPMessage`, `onMSSP`, `onCompressionEnabled`, `onMXPEnabled`), so three kinds of
fact are simply unavailable through them: an option the server offered that we declined or do not
implement (MSP, ZMP, ATCP — no plugin, no callback, but the offer is the measurement); an option
whose negotiation completed with nothing ever sent over it (a server that offers GMCP and never
speaks it still *offers* GMCP); and the distinction between `WILL` and `DO`, which is the difference
between "I can do this" and "please do this". Sniffing the inbound bytes gets all three, costs one
pass over a buffer we are already copying, and cannot be wrong about what arrived. It also survives a
library upgrade adding or removing plugins.

Adding an `OnOptionNegotiated(verb, option)` hook to `TelnetInterpreter` is a good upstream PR —
`TelnetNegotiationCore` is first-party (see CLAUDE.md) — but it is not needed for this and the
decorator is the honest measurement either way.

**Files:**
- Create: `src/MUI.Crawl/Transport/TelnetOptionNames.cs`
- Create: `src/MUI.Crawl/Transport/NegotiationRecorder.cs`
- Test: `tests/MUI.Crawl.Tests/NegotiationRecorderTests.cs`

**Interfaces:**
- Consumes: `ITransport` (Task 4), `TelnetWire`, `ScriptedMuServer` (Task 5).
- Produces: `MUI.Crawl.Transport.TelnetOptionNames.NameOf(byte)`;
  `sealed class NegotiationRecorder(ITransport inner) : ITransport` with
  `IReadOnlySet<string> Offered` and `IReadOnlyList<(byte Verb, byte Option)> Observed`.
  Task 15 wraps the transport in one and copies `Offered` into `ProbeResult.OfferedOptions`.

- [ ] **Step 1: Write the failing test**

Create `tests/MUI.Crawl.Tests/NegotiationRecorderTests.cs`:

```csharp
using System.Text;
using MUI.Crawl.Tests.Support;
using MUI.Crawl.Transport;

namespace MUI.Crawl.Tests;

/// <summary>
/// Layer 1 (spec §6.1). The subject is the byte stream, so the tests feed bytes — including the
/// awkward ones: an option byte that collides with a verb byte inside a subnegotiation payload, and
/// a sequence split across two reads.
/// </summary>
public class NegotiationRecorderTests
{
    private static async Task<NegotiationRecorder> RecorderOver(params byte[][] chunks)
    {
        var recorder = new NegotiationRecorder(new ChunkedTransport(chunks));
        await recorder.ConnectAsync(CancellationToken.None);

        var buffer = new byte[4096];
        while (await recorder.ReceiveAsync(buffer, CancellationToken.None) > 0)
        {
        }

        return recorder;
    }

    [Test]
    public async Task WhatTheServerOffersIsRecordedByName()
    {
        var recorder = await RecorderOver(
        [
            TelnetWire.Iac, TelnetWire.Will, TelnetWire.Gmcp,
            TelnetWire.Iac, TelnetWire.Will, TelnetWire.Mssp,
            TelnetWire.Iac, TelnetWire.Do, TelnetWire.Naws,
            TelnetWire.Iac, TelnetWire.Will, TelnetWire.Mccp2,
        ]);

        await Assert.That(recorder.Offered).IsEquivalentTo(new[] { "GMCP", "MSSP", "NAWS", "MCCP2" });
    }

    [Test]
    public async Task RefusalsAreObservedButAreNotOffers()
    {
        var recorder = await RecorderOver(
        [
            TelnetWire.Iac, TelnetWire.Wont, TelnetWire.Mssp,
            TelnetWire.Iac, TelnetWire.Dont, TelnetWire.Naws,
        ]);

        // A server saying WONT MSSP has told us something worth keeping — but it has not offered it,
        // and a capability matrix built from Offered must not show it.
        await Assert.That(recorder.Offered).IsEmpty();
        await Assert.That(recorder.Observed).IsEquivalentTo(new[]
        {
            (TelnetWire.Wont, TelnetWire.Mssp),
            (TelnetWire.Dont, TelnetWire.Naws),
        });
    }

    [Test]
    public async Task ASubnegotiationPayloadIsNeverMistakenForNegotiation()
    {
        // The MSSP report below contains byte 251 (WILL) and byte 253 (DO) as ordinary payload, and
        // an escaped IAC. A recorder without a state machine reads options out of the middle of it.
        var payload = new List<byte> { TelnetWire.Iac, TelnetWire.Sb, TelnetWire.Mssp, TelnetWire.MsspVar };
        payload.AddRange(Encoding.ASCII.GetBytes("NAME"));
        payload.Add(TelnetWire.MsspVal);
        payload.AddRange([TelnetWire.Will, 42, TelnetWire.Do, 70, TelnetWire.Iac, TelnetWire.Iac]);
        payload.AddRange([TelnetWire.Iac, TelnetWire.Se]);
        payload.AddRange([TelnetWire.Iac, TelnetWire.Will, TelnetWire.Ttype]);

        var recorder = await RecorderOver([.. payload]);

        await Assert.That(recorder.Offered).IsEquivalentTo(new[] { "TTYPE" });
    }

    [Test]
    public async Task ASequenceSplitAcrossReadsIsStillOneSequence()
    {
        // TCP will split anywhere, and a three-byte negotiation across two packets is routine.
        var recorder = await RecorderOver(
            [TelnetWire.Iac],
            [TelnetWire.Will],
            [TelnetWire.Mssp, TelnetWire.Iac, TelnetWire.Will],
            [TelnetWire.Gmcp]);

        await Assert.That(recorder.Offered).IsEquivalentTo(new[] { "MSSP", "GMCP" });
    }

    [Test]
    public async Task DataBytesPassThroughUntouched()
    {
        var greeting = Encoding.ASCII.GetBytes("Welcome.\r\n");
        var recorder = new NegotiationRecorder(new ChunkedTransport([[TelnetWire.Iac, TelnetWire.Will, TelnetWire.Mssp], greeting]));
        await recorder.ConnectAsync(CancellationToken.None);

        var buffer = new byte[64];
        var first = await recorder.ReceiveAsync(buffer, CancellationToken.None);
        var second = await recorder.ReceiveAsync(buffer.AsMemory(first), CancellationToken.None);

        await Assert.That(first).IsEqualTo(3);
        await Assert.That(Encoding.ASCII.GetString(buffer, first, second)).IsEqualTo("Welcome.\r\n");
    }

    [Test]
    public async Task EveryOptionTheSpecNamesHasAName()
    {
        // The names that reach ProbeResult.OfferedOptions and, through it, the capability matrix on
        // every game page. Spelled once, here.
        await Assert.That(TelnetOptionNames.NameOf(1)).IsEqualTo("ECHO");
        await Assert.That(TelnetOptionNames.NameOf(3)).IsEqualTo("SGA");
        await Assert.That(TelnetOptionNames.NameOf(24)).IsEqualTo("TTYPE");
        await Assert.That(TelnetOptionNames.NameOf(25)).IsEqualTo("EOR");
        await Assert.That(TelnetOptionNames.NameOf(31)).IsEqualTo("NAWS");
        await Assert.That(TelnetOptionNames.NameOf(39)).IsEqualTo("NEW-ENVIRON");
        await Assert.That(TelnetOptionNames.NameOf(42)).IsEqualTo("CHARSET");
        await Assert.That(TelnetOptionNames.NameOf(69)).IsEqualTo("MSDP");
        await Assert.That(TelnetOptionNames.NameOf(70)).IsEqualTo("MSSP");
        await Assert.That(TelnetOptionNames.NameOf(85)).IsEqualTo("MCCP1");
        await Assert.That(TelnetOptionNames.NameOf(86)).IsEqualTo("MCCP2");
        await Assert.That(TelnetOptionNames.NameOf(87)).IsEqualTo("MCCP3");
        await Assert.That(TelnetOptionNames.NameOf(90)).IsEqualTo("MSP");
        await Assert.That(TelnetOptionNames.NameOf(91)).IsEqualTo("MXP");
        await Assert.That(TelnetOptionNames.NameOf(93)).IsEqualTo("ZMP");
        await Assert.That(TelnetOptionNames.NameOf(200)).IsEqualTo("ATCP");
        await Assert.That(TelnetOptionNames.NameOf(201)).IsEqualTo("GMCP");

        // An unknown option is still a measurement, and must not be silently dropped.
        await Assert.That(TelnetOptionNames.NameOf(137)).IsEqualTo("OPT-137");
    }

    [Test]
    public async Task TheRecorderIsATransportAndForwardsEverythingElse()
    {
        await using var server = new ScriptedMuServer { Greeting = TelnetWire.Offer(TelnetWire.Mssp) };
        server.Listen();

        await using var recorder = new NegotiationRecorder(
            new TcpTransport(new ConnectionOptions { Host = "127.0.0.1", Port = server.Port }));

        await recorder.ConnectAsync(CancellationToken.None);
        await Assert.That(recorder.IsConnected).IsTrue();
        await Assert.That(recorder.RemoteDescription).IsNotNull();

        var buffer = new byte[16];
        await recorder.ReceiveAsync(buffer, CancellationToken.None);
        await recorder.SendAsync(Encoding.ASCII.GetBytes("WHO\r\n"), CancellationToken.None);

        await Assert.That(await server.WaitForCommandAsync("WHO")).IsTrue();
        await Assert.That(recorder.Offered).IsEquivalentTo(new[] { "MSSP" });
    }

    /// <summary>An <see cref="ITransport"/> that hands back one prepared chunk per read, then EOF.</summary>
    private sealed class ChunkedTransport(params byte[][] chunks) : ITransport
    {
        private int _next;

        public bool IsConnected { get; private set; }

        public string? RemoteDescription => "chunked";

        public Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            IsConnected = true;
            return Task.CompletedTask;
        }

        public ValueTask SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_next >= chunks.Length)
            {
                return ValueTask.FromResult(0);
            }

            var chunk = chunks[_next++];
            chunk.CopyTo(buffer.Span);
            return ValueTask.FromResult(chunk.Length);
        }

        public Task CloseAsync()
        {
            IsConnected = false;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `error CS0246: The type or namespace name 'NegotiationRecorder' could not be found`.

- [ ] **Step 3: Write the option names**

Create `src/MUI.Crawl/Transport/TelnetOptionNames.cs`:

```csharp
namespace MUI.Crawl.Transport;

/// <summary>
/// Telnet option numbers as the names a game page shows. These strings reach
/// <c>ProbeResult.OfferedOptions</c>, the capability matrix and the ecosystem dashboard's protocol
/// adoption curves (spec §9), so they are spelled once, here.
/// </summary>
public static class TelnetOptionNames
{
    /// <summary>
    /// The registered name, or <c>OPT-&lt;n&gt;</c> for anything unrecognised — an option we have no
    /// name for is still a measurement, and dropping it would quietly narrow what the crawler can see.
    /// </summary>
    public static string NameOf(byte option) => option switch
    {
        1 => "ECHO",
        3 => "SGA",
        24 => "TTYPE",
        25 => "EOR",
        31 => "NAWS",
        32 => "TSPEED",
        33 => "FLOWCONTROL",
        34 => "LINEMODE",
        35 => "XDISPLOC",
        36 => "ENVIRON",
        39 => "NEW-ENVIRON",
        42 => "CHARSET",
        69 => "MSDP",
        70 => "MSSP",
        85 => "MCCP1",
        86 => "MCCP2",
        87 => "MCCP3",
        90 => "MSP",
        91 => "MXP",
        93 => "ZMP",
        200 => "ATCP",
        201 => "GMCP",
        _ => $"OPT-{option}",
    };
}
```

- [ ] **Step 4: Write the recorder**

Create `src/MUI.Crawl/Transport/NegotiationRecorder.cs`:

```csharp
namespace MUI.Crawl.Transport;

/// <summary>
/// A transport decorator that measures spec §6.1's layer 1: every <c>IAC WILL/DO/WONT/DONT</c> the
/// server sent, sniffed off the inbound byte stream on its way past.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a decorator and not a library callback.</b> TelnetNegotiationCore negotiates all of these
/// options and exposes no "what did the server offer" surface: its callbacks are per-protocol and
/// fire on a <em>completed</em> negotiation. Three facts are therefore unreachable through them — an
/// option offered that we decline or have no plugin for (MSP, ZMP, ATCP), an option negotiated but
/// never used (a server offering GMCP and never speaking it still offers it), and the difference
/// between <c>WILL</c> and <c>DO</c>. Sniffing the bytes gets all three, costs one pass over a buffer
/// we already hold, and cannot be wrong about what arrived.
/// </para>
/// <para>
/// It reads only. It never rewrites, delays or drops a byte: the telnet layer above must see exactly
/// the stream the server sent.
/// </para>
/// </remarks>
public sealed class NegotiationRecorder(ITransport inner) : ITransport
{
    private const byte Iac = 255;
    private const byte Se = 240;
    private const byte Sb = 250;
    private const byte Will = 251;
    private const byte Do = 253;
    private const byte Dont = 254;

    private readonly ITransport _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly Lock _gate = new();
    private readonly HashSet<string> _offered = new(StringComparer.Ordinal);
    private readonly List<(byte Verb, byte Option)> _observed = [];

    private State _state = State.Data;
    private byte _verb;

    /// <summary>
    /// The options the server said <c>WILL</c> or <c>DO</c> for, by name. A refusal is not an offer,
    /// so <c>WONT</c>/<c>DONT</c> are recorded in <see cref="Observed"/> and not here.
    /// </summary>
    public IReadOnlySet<string> Offered
    {
        get
        {
            lock (_gate)
            {
                return new HashSet<string>(_offered, StringComparer.Ordinal);
            }
        }
    }

    /// <summary>Every negotiation verb and option seen, in wire order, refusals included.</summary>
    public IReadOnlyList<(byte Verb, byte Option)> Observed
    {
        get
        {
            lock (_gate)
            {
                return [.. _observed];
            }
        }
    }

    public bool IsConnected => _inner.IsConnected;

    public string? RemoteDescription => _inner.RemoteDescription;

    public Task ConnectAsync(CancellationToken cancellationToken = default) =>
        _inner.ConnectAsync(cancellationToken);

    public ValueTask SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default) =>
        _inner.SendAsync(data, cancellationToken);

    public async ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await _inner.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
        if (read > 0)
        {
            Record(buffer.Span[..read]);
        }

        return read;
    }

    public Task CloseAsync() => _inner.CloseAsync();

    public ValueTask DisposeAsync() => _inner.DisposeAsync();

    /// <summary>
    /// The state machine. It carries across reads, because TCP splits anywhere and a three-byte
    /// negotiation arriving as three packets is routine; and it tracks subnegotiation bodies, because
    /// an MSSP payload contains bytes 251 and 253 as ordinary data all the time.
    /// </summary>
    private void Record(ReadOnlySpan<byte> bytes)
    {
        lock (_gate)
        {
            foreach (var b in bytes)
            {
                switch (_state)
                {
                    case State.Data:
                        _state = b == Iac ? State.Command : State.Data;
                        break;

                    case State.Command:
                        if (b is >= Will and <= Dont)
                        {
                            _verb = b;
                            _state = State.Option;
                        }
                        else if (b == Sb)
                        {
                            _state = State.SubnegotiationOption;
                        }
                        else
                        {
                            // IAC IAC (escaped data) and every two-byte command: nothing to record.
                            _state = State.Data;
                        }

                        break;

                    case State.Option:
                        _observed.Add((_verb, b));
                        if (_verb is Will or Do) // an offer either way; WONT/DONT are refusals
                        {
                            _offered.Add(TelnetOptionNames.NameOf(b));
                        }

                        _state = State.Data;
                        break;

                    case State.SubnegotiationOption:
                        _state = State.Subnegotiation;
                        break;

                    case State.Subnegotiation:
                        _state = b == Iac ? State.SubnegotiationIac : State.Subnegotiation;
                        break;

                    case State.SubnegotiationIac:
                        // IAC SE ends the body; IAC IAC is an escaped 255 inside it; anything else is
                        // a malformed embedded command, which we ignore and stay inside the body for.
                        _state = b == Se ? State.Data : State.Subnegotiation;
                        break;
                }
            }
        }
    }

    private enum State
    {
        Data,
        Command,
        Option,
        SubnegotiationOption,
        Subnegotiation,
        SubnegotiationIac,
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Crawl.Tests </dev/null
```
Expected: PASS — 7 new tests.

- [ ] **Step 6: Commit**

```bash
git add src/MUI.Crawl/Transport/TelnetOptionNames.cs src/MUI.Crawl/Transport/NegotiationRecorder.cs \
        tests/MUI.Crawl.Tests/NegotiationRecorderTests.cs
git commit -m "feat(crawl): measure layer 1 by sniffing the bytes, not by asking the library

TelnetNegotiationCore's callbacks fire on completed per-protocol negotiations, so
three facts are unreachable through them: an option we decline or have no plugin
for, an option negotiated and never used, and WILL versus DO. A transport
decorator sees all three and cannot be wrong about what arrived (spec §6.1)."
```

---

### Task 8: The telnet layer, the crawler's identity, and asking for MSSP

Spec §6.1 (the handshake is our own library pointed outward), §6.4 (telnet option 70), §11 (the
crawler self-identifies so an admin can find out who we are and how to opt out).

**Reproduced from SharpMUTerm's `TelnetSessionOptions.RequestOptions`, because it is the reason this
task exists.** The MSSP specification says the server *should* send `IAC WILL MSSP` when a client
connects, and TelnetNegotiationCore is built on that reading: its entire opening negotiation is
`IAC WILL NAWS` and nothing else, so MSSP is only ever reached if the server volunteers it. A great
many servers that fully support MSSP volunteer nothing — they answer `IAC DO MSSP` and are otherwise
silent, which is why the protocol's own reference client, TinTin++'s `#session mssp`, **asks rather
than listens**. A crawler that only listens does not see those servers at all and reports them as
having no MSSP. So this probe sends `IAC DO 70` on every connection.

`IAC DO` is negotiation, not traffic: it is the client half of the option handshake.

**Files:**
- Create: `src/MUI.Crawl/Telnet/ProbeTelnetSession.cs`
- Test: `tests/MUI.Crawl.Tests/ProbeTelnetSessionTests.cs`

**Interfaces:**
- Consumes: `ITransport`, `TcpTransport`, `ConnectionOptions` (Task 4); `ProbeOptions` (Task 3);
  `ScriptedMuServer`, `TelnetWire` (Task 5).
- Produces: `MUI.Crawl.Telnet.ProbeTelnetSession` —
  `ProbeTelnetSession(ITransport transport, ProbeOptions options, ILogger? logger = null)`,
  `bool IdentityStated`, `event EventHandler<string>? TextReceived`,
  `event EventHandler<MUI.Crawl.Mssp.MsspData>? MsspReceived`,
  `event EventHandler<Exception?>? Closed`, `Task ConnectAsync(CancellationToken)`,
  `ValueTask SendLineAsync(string, CancellationToken)`, `Task CloseAsync()`, `ValueTask DisposeAsync()`.
  Tasks 9, 13 and 15 all drive it.

- [ ] **Step 1: Write the failing test**

Create `tests/MUI.Crawl.Tests/ProbeTelnetSessionTests.cs`:

```csharp
using System.Text;
using MUI.Crawl.Mssp;
using MUI.Crawl.Telnet;
using MUI.Crawl.Tests.Support;
using MUI.Crawl.Transport;

namespace MUI.Crawl.Tests;

/// <summary>
/// The telnet layer, negotiating for real against a real socket.
/// </summary>
public class ProbeTelnetSessionTests
{
    private static ProbeTelnetSession SessionFor(ScriptedMuServer server, ProbeOptions? options = null) =>
        new(new TcpTransport(new ConnectionOptions { Host = "127.0.0.1", Port = server.Port }),
            options ?? new ProbeOptions());

    private static async Task<string> TextWithin(ProbeTelnetSession session, TimeSpan within, string contains)
    {
        var text = new StringBuilder();
        var seen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.TextReceived += (_, chunk) =>
        {
            lock (text)
            {
                text.Append(chunk);
                if (text.ToString().Contains(contains, StringComparison.Ordinal))
                {
                    seen.TrySetResult();
                }
            }
        };

        await Task.WhenAny(seen.Task, Task.Delay(within));
        lock (text)
        {
            return text.ToString();
        }
    }

    [Test]
    public async Task AServerThatOnlyAnswersWhenAskedIsStillRead()
    {
        // The case that makes a live MSSP server look as if it had none. This server volunteers
        // nothing whatsoever — no greeting, no IAC WILL MSSP — so it is reachable only by asking.
        await using var server = new ScriptedMuServer();
        server.RespondingToDo(TelnetWire.Mssp, TelnetWire.Subnegotiation(("NAME", ["Silent Until Asked"])));
        server.Listen();

        await using var session = SessionFor(server);
        var report = new TaskCompletionSource<MsspData>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.MsspReceived += (_, data) => report.TrySetResult(data);

        await session.ConnectAsync(CancellationToken.None);

        var finished = await Task.WhenAny(report.Task, Task.Delay(TimeSpan.FromSeconds(5)));

        await Assert.That(finished).IsSameReferenceAs(report.Task)
            .Because("a crawler that only listens reports every ask-me-first server as having no MSSP");
        await Assert.That((await report.Task).Name).IsEqualTo("Silent Until Asked");
    }

    [Test]
    public async Task TheProbeAsksForMsspRatherThanWaitingToBeOffered()
    {
        await using var server = new ScriptedMuServer();
        server.Listen();

        await using var session = SessionFor(server);
        await session.ConnectAsync(CancellationToken.None);

        await Assert.That(await server.WaitForReceivedAsync([TelnetWire.Iac, TelnetWire.Do, TelnetWire.Mssp]))
            .IsTrue();
    }

    [Test]
    public async Task TheCrawlerNamesItselfAndSaysWhereToReadAboutIt()
    {
        // MTTS: the server sends IAC SB TTYPE SEND IAC SE and the client answers IAC SB TTYPE IS
        // <name>. A crawler that did not answer with its own name would be logged as whatever the
        // telnet library calls itself, which tells a server operator nothing and offers no opt-out.
        await using var server = new ScriptedMuServer { Greeting = TelnetWire.Ask(TelnetWire.Ttype) };
        server.RespondingToDo(TelnetWire.Mssp, []);
        server.Listen();

        await using var session = SessionFor(server);
        await session.ConnectAsync(CancellationToken.None);

        await Assert.That(await server.WaitForReceivedAsync([TelnetWire.Iac, TelnetWire.Will, TelnetWire.Ttype]))
            .IsTrue();

        await Assert.That(await server.WaitForReceivedAsync(
                Encoding.ASCII.GetBytes("MUINDEX (+https://muindex.org/crawler)")))
            .IsTrue()
            .Because("spec §11: an admin reading their logs must be able to find out who we are");

        await Assert.That(server.ReceivedText).DoesNotContain("TNC");
        await Assert.That(session.IdentityStated).IsTrue();
    }

    [Test]
    public async Task TextArrivesWithTelnetFramingStrippedAndAnsiIntact()
    {
        var banner = new List<byte>(TelnetWire.Offer(TelnetWire.Eor));
        banner.AddRange(Encoding.UTF8.GetBytes("[1;36mCorvid Nest[0m\r\nBy what name? "));

        await using var server = new ScriptedMuServer { Greeting = [.. banner] };
        server.Listen();

        await using var session = SessionFor(server);
        await session.ConnectAsync(CancellationToken.None);

        var text = await TextWithin(session, TimeSpan.FromSeconds(5), "By what name? ");

        // The IAC WILL EOR is gone; the SGR is not. The connect screen is stored ANSI-intact (§6.2).
        await Assert.That(text).Contains("[1;36mCorvid Nest[0m");
        await Assert.That(text).Contains("By what name? ");
        await Assert.That(text).DoesNotContain("ÿ");
    }

    [Test]
    public async Task AnUnterminatedPromptIsDeliveredWithoutWaitingForANewline()
    {
        // A login prompt with no newline after it is the normal case, and a session that only emitted
        // whole lines would report an empty connect screen for it.
        await using var server = new ScriptedMuServer { Greeting = Encoding.ASCII.GetBytes("Enter your name: ") };
        server.Listen();

        await using var session = SessionFor(server);
        await session.ConnectAsync(CancellationToken.None);

        await Assert.That(await TextWithin(session, TimeSpan.FromSeconds(5), "Enter your name: "))
            .IsEqualTo("Enter your name: ");
    }

    [Test]
    public async Task TheLineTheProbeSendsArrivesAtTheServer()
    {
        await using var server = new ScriptedMuServer();
        server.RespondingToCommand("WHO", "Player Name\r\nAlice\r\n1 Players logged in.\r\n");
        server.Listen();

        await using var session = SessionFor(server);
        await session.ConnectAsync(CancellationToken.None);
        await session.SendLineAsync("WHO", CancellationToken.None);

        await Assert.That(await TextWithin(session, TimeSpan.FromSeconds(5), "1 Players logged in."))
            .Contains("Alice");
        await Assert.That(server.Commands).IsEquivalentTo(new[] { "WHO" });
    }

    [Test]
    public async Task AServerHangingUpRaisesClosedRatherThanThrowing()
    {
        await using var server = new ScriptedMuServer { Misbehave = Misbehaviour.HalfOpen };
        server.Listen();

        await using var session = SessionFor(server);
        var closed = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Closed += (_, error) => closed.TrySetResult(error);

        await session.ConnectAsync(CancellationToken.None);

        var finished = await Task.WhenAny(closed.Task, Task.Delay(TimeSpan.FromSeconds(5)));

        await Assert.That(finished).IsSameReferenceAs(closed.Task);
        await Assert.That(await closed.Task).IsNull().Because("a hung-up server is an answer, not a fault");
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `error CS0246: The type or namespace name 'ProbeTelnetSession' could not be found`.

- [ ] **Step 3: Write the telnet session**

Create `src/MUI.Crawl/Telnet/ProbeTelnetSession.cs`:

```csharp
using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MUI.Crawl.Mssp;
using MUI.Crawl.Transport;
using TelnetNegotiationCore.Builders;
using TelnetNegotiationCore.Interpreters;
using TelnetNegotiationCore.Models;
using TelnetNegotiationCore.Protocols;

namespace MUI.Crawl.Telnet;

/// <summary>
/// <see cref="TelnetInterpreter"/> over an <see cref="ITransport"/>, trimmed to what a probe needs:
/// framing-stripped text, MSSP, and the ability to state who we are.
/// </summary>
/// <remarks>
/// Trimmed from SharpMUTerm's <c>TelnetSession</c>, which is the same library driven for an
/// interactive client. Three differences: everything decoded is surfaced as it arrives rather than
/// line by line (a connect screen ends in an unterminated prompt, and waiting for a newline would
/// lose it); <c>IAC DO MSSP</c> goes out on connect (see <see cref="RequestMsspAsync"/>); and there is
/// no keepalive, because this connection is measured in seconds and filler traffic on a stranger's
/// socket would be gratuitous.
/// </remarks>
public sealed class ProbeTelnetSession : IAsyncDisposable
{
    /// <summary>The MSSP telnet option.</summary>
    public const byte MsspOption = 70;

    // TelnetInterpreter.CallbackOnByteAsync is init-only and not exposed by the builder, so it is
    // assigned reflectively after building. SharpMUTerm does the same; a first-class OnByte builder
    // hook is a candidate upstream PR (TelnetNegotiationCore is first-party — see CLAUDE.md).
    private static readonly PropertyInfo ByteCallbackProperty =
        typeof(TelnetInterpreter).GetProperty(nameof(TelnetInterpreter.CallbackOnByteAsync))
        ?? throw new InvalidOperationException("TelnetInterpreter.CallbackOnByteAsync not found.");

    // CurrentEncoding defaults to Encoding.ASCII, and that default is not inert: it decodes every
    // byte handed to the callbacks. On the many MU* servers that never negotiate CHARSET, every byte
    // above 0x7F became '?' — which for a crawler means a mangled connect screen and a mangled banner
    // hash. Seeded with UTF-8, exactly as SharpMUTerm does.
    private static readonly PropertyInfo? InterpreterEncodingProperty =
        typeof(TelnetInterpreter).GetProperty(nameof(TelnetInterpreter.CurrentEncoding)) is { CanWrite: true } p
            ? p
            : null;

    // TerminalTypeProtocol holds the list it answers TTYPE/MTTS with in a private field and offers no
    // builder hook. Its default is ["TNC", "XTERM", "MTTS 3853"] — i.e. every server would be told it
    // was talking to the telnet library. Spec §11 requires better than that.
    private static readonly FieldInfo? TerminalTypesField =
        typeof(TerminalTypeProtocol).GetField("_terminalTypes", BindingFlags.NonPublic | BindingFlags.Instance);

    private readonly ITransport _transport;
    private readonly ProbeOptions _options;
    private readonly ILogger _logger;
    private readonly List<byte> _pending = [];

    private TelnetInterpreter? _interpreter;
    private CancellationTokenSource? _loopCts;
    private Task? _readLoop;
    private int _closed;

    public ProbeTelnetSession(ITransport transport, ProbeOptions options, ILogger? logger = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// True once the telnet layer has been told what to answer TTYPE with. False means this probe
    /// cannot say who it is — which spec §11 makes a reason to abandon rather than continue (Task 15).
    /// </summary>
    public bool IdentityStated { get; private set; }

    /// <summary>Decoded text, telnet framing stripped and ANSI intact, as it arrives.</summary>
    public event EventHandler<string>? TextReceived;

    /// <summary>One report per <c>IAC SB MSSP … IAC SE</c>.</summary>
    public event EventHandler<MsspData>? MsspReceived;

    /// <summary>The read loop ended: null for a clean end of stream, otherwise the fault.</summary>
    public event EventHandler<Exception?>? Closed;

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (_interpreter is not null)
        {
            throw new InvalidOperationException("Session is already connected.");
        }

        // The transport must be open before building: the interpreter emits its opening negotiation
        // during BuildAsync, straight to the transport.
        await _transport.ConnectAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            _interpreter = await BuildInterpreterAsync().ConfigureAwait(false);
            ByteCallbackProperty.SetValue(_interpreter, new Func<byte, Encoding, ValueTask>(OnByteAsync));
            SeedInterpreterEncoding(_interpreter);
            ApplyTerminalTypes(_interpreter);
        }
        catch
        {
            _interpreter = null;
            await _transport.CloseAsync().ConfigureAwait(false);
            throw;
        }

        _loopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _readLoop = Task.Run(() => ReadLoopAsync(_loopCts.Token), CancellationToken.None);

        // After the read loop is running, so a server that answers instantly is heard.
        await RequestMsspAsync(cancellationToken).ConfigureAwait(false);
    }

    private Task<TelnetInterpreter> BuildInterpreterAsync() =>
        new TelnetInterpreterBuilder()
            .UseMode(TelnetInterpreter.TelnetMode.Client)
            .UseLogger(_logger)
            .OnNegotiation(data => _transport.SendAsync(data))
            .OnSubmit(OnSubmitAsync)
            .AddDefaultMUDProtocols(
                onNAWS: static (_, _) => ValueTask.CompletedTask,
                onGMCPMessage: static _ => ValueTask.CompletedTask,
                onMSSP: OnMsspAsync,
                msspConfig: static () => new MSSPConfig(),
                onMSDPMessage: static (_, _) => ValueTask.CompletedTask,
                onPrompt: OnPromptAsync,
                charsetOrder: [Encoding.UTF8, Encoding.Latin1],
                onCompressionEnabled: static (_, _) => ValueTask.CompletedTask,
                onMXPEnabled: static () => ValueTask.CompletedTask)
            .BuildAsync();

    private void SeedInterpreterEncoding(TelnetInterpreter interpreter)
    {
        if (InterpreterEncodingProperty is null)
        {
            _logger.LogWarning(
                "TelnetInterpreter.CurrentEncoding is not writable; payloads will decode as ASCII until CHARSET settles.");
            return;
        }

        InterpreterEncodingProperty.SetValue(interpreter, Encoding.UTF8);
    }

    /// <summary>
    /// Tells the terminal-type plugin what to answer with, and puts the info URL where a server
    /// operator will actually see it (spec §11).
    /// </summary>
    /// <remarks>
    /// The URL rides the first entry — the client name — as <c>MUINDEX (+https://…)</c>, the HTTP
    /// User-Agent convention. Spec §11 also asks for MNES <c>CLIENT_NAME</c>; TelnetNegotiationCore
    /// 2.7.0 registers no NEW-ENVIRON plugin in <c>AddDefaultMUDProtocols</c> and exposes no
    /// client-side environment send, so that half is not reachable from here. A client-side MNES
    /// sender is a good upstream PR.
    /// </remarks>
    private void ApplyTerminalTypes(TelnetInterpreter interpreter)
    {
        if (_options.TerminalTypes is not { Count: > 0 } configured)
        {
            return;
        }

        if (TerminalTypesField is null ||
            interpreter.PluginManager?.GetPlugin<TerminalTypeProtocol>() is not { } plugin)
        {
            _logger.LogWarning(
                "Terminal types are not settable on this TelnetNegotiationCore build; the server would be told the "
                + "library's name instead of ours.");
            return;
        }

        var announced = configured.ToList();
        if (!string.IsNullOrWhiteSpace(_options.InfoUrl))
        {
            announced[0] = $"{announced[0]} (+{_options.InfoUrl})";
        }

        TerminalTypesField.SetValue(plugin, announced.ToImmutableList());
        IdentityStated = true;
    }

    /// <summary>
    /// Sends <c>IAC DO 70</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Waiting does not work.</b> The MSSP specification says a server "should send IAC WILL MSSP"
    /// on connect, and TelnetNegotiationCore is built on that reading: its whole opening negotiation
    /// is <c>IAC WILL NAWS</c> and nothing else, so MSSP is only reached if the server volunteers it.
    /// A great many servers that fully support MSSP volunteer nothing — they answer <c>IAC DO MSSP</c>
    /// and are otherwise silent, which is why the protocol's own reference client (TinTin++'s
    /// <c>#session mssp</c>) asks rather than listens. A crawler that only listens reports those
    /// servers as having no MSSP.
    /// </para>
    /// <para>
    /// Written straight to the transport rather than through the interpreter's send path, because that
    /// escapes <c>IAC</c> as data — right for a command line, wrong for a negotiation. This is the
    /// same door the interpreter's own negotiation output goes through.
    /// </para>
    /// </remarks>
    private ValueTask RequestMsspAsync(CancellationToken cancellationToken) =>
        _transport.SendAsync(new byte[] { 255, 253, MsspOption }, cancellationToken);

    private ValueTask OnByteAsync(byte value, Encoding encoding)
    {
        _pending.Add(value);
        return ValueTask.CompletedTask;
    }

    private ValueTask OnSubmitAsync(byte[] bytes, Encoding? encoding, TelnetInterpreter interpreter)
    {
        // A newline-terminated line. The bytes also passed through OnByteAsync, so clearing _pending
        // here is what keeps the reconstructed stream in order and free of duplicates.
        _pending.Clear();
        Emit(Encoding.UTF8.GetString(bytes) + "\n");
        return ValueTask.CompletedTask;
    }

    private ValueTask OnPromptAsync()
    {
        FlushPending();
        return ValueTask.CompletedTask;
    }

    private ValueTask OnMsspAsync(MSSPConfig config)
    {
        var report = MsspData.From(config.Variables);
        _logger.LogDebug("MSSP: {Count} variables from {Name}.", report.Count, report.Name ?? "an unnamed server");
        MsspReceived?.Invoke(this, report);
        return ValueTask.CompletedTask;
    }

    private void FlushPending()
    {
        if (_pending.Count == 0)
        {
            return;
        }

        var text = Encoding.UTF8.GetString([.. _pending]);
        _pending.Clear();
        Emit(text);
    }

    private void Emit(string text) => TextReceived?.Invoke(this, text);

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        Exception? error = null;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var read = await _transport.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break; // clean end of stream
                }

                await _interpreter!.InterpretByteArrayAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);

                // Whatever did not end in a newline — a login prompt, a partial line, a WHO reply with
                // no trailing terminator. A session that only emitted whole lines would lose it, and
                // the connect screen is mostly made of it.
                FlushPending();
            }
        }
        catch (OperationCanceledException)
        {
            // graceful shutdown
        }
        catch (Exception ex)
        {
            error = ex;
            _logger.LogDebug(ex, "The probe's receive loop faulted.");
        }
        finally
        {
            RaiseClosed(error);
        }
    }

    /// <summary>Sends one line. The three lines a probe may send are listed in the plan's preamble.</summary>
    public ValueTask SendLineAsync(string text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        var interpreter = _interpreter ?? throw new InvalidOperationException("Session is not connected.");
        return interpreter.SendAsync(Encoding.UTF8.GetBytes(text + "\r\n"));
    }

    public async Task CloseAsync()
    {
        if (_loopCts is not null)
        {
            await _loopCts.CancelAsync().ConfigureAwait(false);
        }

        await _transport.CloseAsync().ConfigureAwait(false);

        if (_readLoop is not null)
        {
            try
            {
                await _readLoop.ConfigureAwait(false);
            }
            catch
            {
                // already reported through Closed
            }
        }

        var interpreter = _interpreter;
        _interpreter = null;
        if (interpreter is not null)
        {
            await interpreter.DisposeAsync().ConfigureAwait(false);
        }

        RaiseClosed(null);
    }

    private void RaiseClosed(Exception? error)
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0)
        {
            return;
        }

        Closed?.Invoke(this, error);
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync().ConfigureAwait(false);
        _loopCts?.Dispose();
        await _transport.DisposeAsync().ConfigureAwait(false);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Crawl.Tests </dev/null
```
Expected: PASS — 7 new tests.

If `TheCrawlerNamesItselfAndSaysWhereToReadAboutIt` fails with `IdentityStated` false, the library's
private field was renamed: check `TerminalTypeProtocol` in the pinned 2.7.0 package
(`~/.nuget/packages/telnetnegotiationcore/2.7.0/`) for the field name and update
`TerminalTypesField`. Do not paper over it by dropping the assertion — spec §11 makes stating an
identity a requirement, and Task 15 abandons a probe that cannot.

- [ ] **Step 5: Commit**

```bash
git add src/MUI.Crawl/Telnet tests/MUI.Crawl.Tests/ProbeTelnetSessionTests.cs
git commit -m "feat(crawl): drive telnet, name the crawler, and ask for MSSP

IAC DO 70 on every connection. The MSSP spec says a server 'should' volunteer IAC
WILL MSSP and TelnetNegotiationCore's opening negotiation is IAC WILL NAWS and
nothing else, so a listening-only crawler reports every ask-me-first server as
having no MSSP — which is why TinTin++'s own #session mssp asks. The first TTYPE
answer carries the info URL, so an admin reading their logs can find out who we
are and how to opt out (spec §11)."
```

---

### Task 9: Layer 2 — the connect screen

Spec §6.2 ("display asset and fingerprint both"), §13 (enormous banners).

Everything decoded **before the WHO command goes out**, ANSI intact, capped at
`ProbeOptions.MaxCaptureBytes`, terminated by `ProbeOptions.BannerQuietPeriod` of silence.

**Files:**
- Create: `src/MUI.Crawl/BoundedTranscript.cs`
- Test: `tests/MUI.Crawl.Tests/BannerCaptureTests.cs`

**Interfaces:**
- Consumes: `ProbeOptions` (Task 3); `ProbeTelnetSession` (Task 8); `ScriptedMuServer` (Task 5).
- Produces: `MUI.Crawl.BoundedTranscript` — `BoundedTranscript(int maxBytes)`, `void Append(string)`,
  `string Text`, `bool IsFull`, `bool Truncated`, `int ByteCount`, `DateTimeOffset? LastAppendAt`
  (set by `Append` from the supplied `TimeProvider`), `void Reset()`, and the static
  `Task<string> CollectAsync(ProbeTelnetSession session, BoundedTranscript sink, TimeSpan quietPeriod, TimeSpan cap, TimeProvider time, CancellationToken ct)`.
  Tasks 13 and 15 use both the sink and `CollectAsync`.

- [ ] **Step 1: Write the failing test**

Create `tests/MUI.Crawl.Tests/BannerCaptureTests.cs`:

```csharp
using System.Text;
using MUI.Crawl.Telnet;
using MUI.Crawl.Tests.Support;
using MUI.Crawl.Transport;

namespace MUI.Crawl.Tests;

/// <summary>
/// Layer 2 (spec §6.2): the connect screen, ANSI intact, bounded, ended by silence.
/// </summary>
public class BannerCaptureTests
{
    private static ProbeTelnetSession SessionFor(ScriptedMuServer server, ProbeOptions options) =>
        new(new TcpTransport(new ConnectionOptions { Host = "127.0.0.1", Port = server.Port }), options);

    [Test]
    public async Task ATranscriptKeepsEverythingUpToItsCap()
    {
        var sink = new BoundedTranscript(16);

        sink.Append("0123456789");
        sink.Append("abcdefghij");

        await Assert.That(sink.Text).IsEqualTo("0123456789abcdef");
        await Assert.That(sink.Truncated).IsTrue();
        await Assert.That(sink.IsFull).IsTrue();
        await Assert.That(sink.ByteCount).IsEqualTo(16);
    }

    [Test]
    public async Task ATranscriptThatFitsIsNotMarkedTruncated()
    {
        var sink = new BoundedTranscript(64);
        sink.Append("Welcome to Corvid Nest.\r\n");

        await Assert.That(sink.Truncated).IsFalse();
        await Assert.That(sink.IsFull).IsFalse();
    }

    [Test]
    public async Task TheCapIsCountedInBytesAndNotInCharacters()
    {
        // A banner full of box drawing is three bytes per glyph in UTF-8; a cap counted in chars
        // would be three times the memory it promised, per connection, across a bounded worker pool.
        var sink = new BoundedTranscript(16);
        sink.Append("╔══════╗");   // 8 chars, 22 bytes

        await Assert.That(sink.ByteCount).IsLessThanOrEqualTo(16);
        await Assert.That(sink.Truncated).IsTrue();
    }

    [Test]
    public async Task TheConnectScreenIsCapturedWithAnsiIntactAndEndsWithSilence()
    {
        var banner = "[1;36m╔══════════════╗\r\n║ Corvid Nest  ║\r\n╚══════════════╝[0m\r\nBy what name? ";

        await using var server = new ScriptedMuServer { Greeting = Encoding.UTF8.GetBytes(banner) };
        server.Listen();

        var options = new ProbeOptions { BannerQuietPeriod = TimeSpan.FromMilliseconds(200) };
        await using var session = SessionFor(server, options);
        var sink = new BoundedTranscript(options.MaxCaptureBytes);

        await session.ConnectAsync(CancellationToken.None);
        var text = await BoundedTranscript.CollectAsync(
            session, sink, options.BannerQuietPeriod, TimeSpan.FromSeconds(5), TimeProvider.System, CancellationToken.None);

        await Assert.That(text).Contains("[1;36m");
        await Assert.That(text).Contains("Corvid Nest");
        await Assert.That(text).EndsWith("By what name? ");
    }

    [Test]
    public async Task AnEnormousBannerIsCappedRatherThanSwallowedWhole()
    {
        await using var server = new ScriptedMuServer
        {
            Misbehave = Misbehaviour.EnormousBanner,
            EnormousBannerBytes = 512 * 1024,
        };
        server.Listen();

        var options = new ProbeOptions
        {
            BannerQuietPeriod = TimeSpan.FromMilliseconds(200),
            MaxCaptureBytes = 4096,
        };

        await using var session = SessionFor(server, options);
        var sink = new BoundedTranscript(options.MaxCaptureBytes);

        await session.ConnectAsync(CancellationToken.None);
        var text = await BoundedTranscript.CollectAsync(
            session, sink, options.BannerQuietPeriod, TimeSpan.FromSeconds(10), TimeProvider.System, CancellationToken.None);

        await Assert.That(Encoding.UTF8.GetByteCount(text)).IsLessThanOrEqualTo(4096);
        await Assert.That(sink.Truncated).IsTrue();
    }

    [Test]
    public async Task CollectionEndsAtTheCapEvenIfTheServerNeverGoesQuiet()
    {
        // A server that never stops talking must not be able to hold a worker for ever. The cap here
        // is the phase's own bound; the probe's hard timeout (Task 15) is the outer one.
        await using var server = new ScriptedMuServer
        {
            Misbehave = Misbehaviour.EnormousBanner,
            EnormousBannerBytes = 8 * 1024 * 1024,
        };
        server.Listen();

        var options = new ProbeOptions { BannerQuietPeriod = TimeSpan.FromSeconds(30), MaxCaptureBytes = 2048 };
        await using var session = SessionFor(server, options);
        var sink = new BoundedTranscript(options.MaxCaptureBytes);

        await session.ConnectAsync(CancellationToken.None);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var text = await BoundedTranscript.CollectAsync(
            session, sink, options.BannerQuietPeriod, TimeSpan.FromSeconds(3), TimeProvider.System, cts.Token);

        await Assert.That(Encoding.UTF8.GetByteCount(text)).IsLessThanOrEqualTo(2048);
    }

    [Test]
    public async Task ASilentServerYieldsAnEmptyBannerRatherThanHanging()
    {
        await using var server = new ScriptedMuServer { Misbehave = Misbehaviour.SilentAfterAccept };
        server.Listen();

        var options = new ProbeOptions { BannerQuietPeriod = TimeSpan.FromMilliseconds(200) };
        await using var session = SessionFor(server, options);
        var sink = new BoundedTranscript(options.MaxCaptureBytes);

        await session.ConnectAsync(CancellationToken.None);
        var text = await BoundedTranscript.CollectAsync(
            session, sink, options.BannerQuietPeriod, TimeSpan.FromSeconds(3), TimeProvider.System, CancellationToken.None);

        await Assert.That(text).IsEmpty();
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `error CS0246: The type or namespace name 'BoundedTranscript' could not be found`.

- [ ] **Step 3: Write the transcript sink**

Create `src/MUI.Crawl/BoundedTranscript.cs`:

```csharp
using System.Text;
using MUI.Crawl.Telnet;

namespace MUI.Crawl;

/// <summary>
/// A size-capped text sink for one captured phase: the connect screen (spec §6.2) or a <c>WHO</c>
/// reply (§6.3).
/// </summary>
/// <remarks>
/// The cap is counted in <b>bytes</b>, not characters. A banner made of box drawing is three bytes
/// per glyph in UTF-8, so a character cap would silently promise a third of the memory it delivered —
/// per connection, across a bounded worker pool, in a process that also serves web requests (§12).
/// </remarks>
public sealed class BoundedTranscript(int maxBytes)
{
    private readonly StringBuilder _text = new();
    private readonly int _maxBytes = maxBytes > 0
        ? maxBytes
        : throw new ArgumentOutOfRangeException(nameof(maxBytes), "A transcript cap must be positive.");

    private int _byteCount;

    /// <summary>What has been captured so far, ANSI intact.</summary>
    public string Text => _text.ToString();

    public int ByteCount => _byteCount;

    /// <summary>True once the cap was reached and something was dropped.</summary>
    public bool Truncated { get; private set; }

    public bool IsFull => _byteCount >= _maxBytes;

    /// <summary>When something last arrived, or null while nothing has. Drives the quiet period.</summary>
    public DateTimeOffset? LastAppendAt { get; private set; }

    /// <summary>
    /// Appends what fits and marks the rest dropped. Splitting mid-character is avoided by walking
    /// back to a rune boundary, so the stored banner is always valid text.
    /// </summary>
    public void Append(string chunk, TimeProvider? time = null)
    {
        LastAppendAt = (time ?? TimeProvider.System).GetUtcNow();

        if (string.IsNullOrEmpty(chunk))
        {
            return;
        }

        if (IsFull)
        {
            Truncated = true;
            return;
        }

        var bytes = Encoding.UTF8.GetByteCount(chunk);
        if (_byteCount + bytes <= _maxBytes)
        {
            _text.Append(chunk);
            _byteCount += bytes;
            return;
        }

        // Take the longest prefix that fits, ending on a rune boundary.
        var room = _maxBytes - _byteCount;
        var taken = 0;
        var length = 0;
        foreach (var rune in chunk.EnumerateRunes())
        {
            var runeBytes = rune.Utf8SequenceLength;
            if (taken + runeBytes > room)
            {
                break;
            }

            taken += runeBytes;
            length += rune.Utf16SequenceLength;
        }

        _text.Append(chunk, 0, length);
        _byteCount += taken;
        Truncated = true;
    }

    public void Reset()
    {
        _text.Clear();
        _byteCount = 0;
        Truncated = false;
        LastAppendAt = null;
    }

    /// <summary>
    /// Collects what the session emits until it has been quiet for <paramref name="quietPeriod"/>,
    /// the sink is full, <paramref name="cap"/> has elapsed, or the server hangs up.
    /// </summary>
    /// <remarks>
    /// A quiet period rather than a terminator, because there is no terminator: a connect screen ends
    /// at a login prompt with no newline, and the only thing that reliably says "it has finished" is
    /// the server having stopped talking. Every exit is bounded, and the caller's token is bounded
    /// again by the probe's hard timeout (spec §12).
    /// </remarks>
    public static async Task<string> CollectAsync(
        ProbeTelnetSession session,
        BoundedTranscript sink,
        TimeSpan quietPeriod,
        TimeSpan cap,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var gate = new Lock();
        var closed = false;
        var startedAt = time.GetUtcNow();
        var lastArrival = startedAt;

        void OnText(object? sender, string chunk)
        {
            lock (gate)
            {
                sink.Append(chunk, time);
                lastArrival = time.GetUtcNow();
            }
        }

        void OnClosed(object? sender, Exception? error)
        {
            lock (gate)
            {
                closed = true;
            }
        }

        session.TextReceived += OnText;
        session.Closed += OnClosed;
        try
        {
            while (true)
            {
                lock (gate)
                {
                    var now = time.GetUtcNow();
                    if (closed || sink.IsFull || now - startedAt >= cap || now - lastArrival >= quietPeriod)
                    {
                        return sink.Text;
                    }
                }

                try
                {
                    await Task.Delay(PollInterval(quietPeriod), time, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return sink.Text;
                }
            }
        }
        finally
        {
            session.TextReceived -= OnText;
            session.Closed -= OnClosed;
        }
    }

    /// <summary>
    /// A tenth of the quiet period, clamped: fine enough that the phase ends promptly, coarse enough
    /// that a bounded worker pool is not spending its time waking up.
    /// </summary>
    private static TimeSpan PollInterval(TimeSpan quietPeriod)
    {
        var tenth = quietPeriod / 10;
        return tenth < TimeSpan.FromMilliseconds(20)
            ? TimeSpan.FromMilliseconds(20)
            : tenth > TimeSpan.FromMilliseconds(250)
                ? TimeSpan.FromMilliseconds(250)
                : tenth;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Crawl.Tests </dev/null
```
Expected: PASS — 7 new tests.

- [ ] **Step 5: Commit**

```bash
git add src/MUI.Crawl/BoundedTranscript.cs tests/MUI.Crawl.Tests/BannerCaptureTests.cs
git commit -m "feat(crawl): capture the connect screen, ANSI intact and bounded

Spec §6.2. Ended by a quiet period rather than a terminator, because there isn't
one — a connect screen ends at a login prompt with no newline. Capped in bytes
rather than characters: a box-drawing banner is three bytes a glyph, and a
character cap would promise a third of the memory it delivered."
```

---

### Task 10: Layer 3 — the structural WHO/DOING parser

Spec §6.3, and locked decision §4.7 ("WHO parsing: structural, not dialectal"). The most detailed
task in this plan, because it is the one that decides whether a game's activity heatmap is filled,
hatched or dark (spec §5.4).

**The rule, stated once:** find a trailing `N Players logged in` / `N players` / `Players: N` summary
line; failing that, count the rows between the header and the footer. Report `Count` when the number
is trustworthy, `PerPlayer` when the name column is positionally identifiable, `Unknown` otherwise —
**and never fabricate a zero.** An unreadable WHO must stay distinguishable from an empty game, or
every game whose `DOING` header was customised past our parser renders as permanently dark while
running fine.

**Why the column algorithm is what it is.** Splitting the header on runs of two-or-more spaces is the
obvious approach and it is not enough: PennMUSH's real header is
`Player Name          On For Idle  Doing`, with a **single** space between `On For` and `Idle`, so
the obvious split merges them and Penn — the single most common MUSH codebase — loses its idle
column. Splitting on every single space is worse: it splits `Player Name` into two columns on any
table whose names all happen to be short. The algorithm below therefore splits on ≥2 spaces first and
then *sub-splits* a multi-word header field only where the body agrees: the column before the
candidate boundary must be whitespace in every row, and some row must actually start a value there.
Penn's `Idle` passes both; `Player Name`'s inner space fails the second.

**Files:**
- Create: `src/MUI.Crawl/Who/AnsiText.cs`
- Create: `src/MUI.Crawl/Who/ColumnLayout.cs`
- Create: `src/MUI.Crawl/Who/WhoParser.cs`
- Create: `tests/MUI.Crawl.Tests/Support/WhoCorpus.cs`
- Test: `tests/MUI.Crawl.Tests/WhoParserTests.cs`

**Interfaces:**
- Consumes: `WhoReading`, `WhoConfidence` (Task 3).
- Produces: `MUI.Crawl.Who.WhoParser.Parse(string) → WhoReading`,
  `MUI.Crawl.Who.WhoParser.ParseTable(string) → WhoTable?`,
  `sealed record WhoTable(IReadOnlyList<WhoRow> Rows, int? SummaryCount)`,
  `sealed record WhoRow(string Name, TimeSpan? OnFor, TimeSpan? Idle, string? Doing)`,
  `MUI.Crawl.Who.AnsiText.Strip(string)`,
  `MUI.Crawl.Who.ColumnLayout.{Starts, Fits, Field}`,
  and `MUI.Crawl.Tests.Support.WhoCorpus` (the fixtures, reused by Task 11 and Task 17).
  Task 12 consumes `WhoTable`/`WhoRow`; Task 15 calls `Parse` and `ParseTable`.

- [ ] **Step 1: Write the corpus**

Create `tests/MUI.Crawl.Tests/Support/WhoCorpus.cs`. **The alignment of these strings is the
fixture.** If a test about a *column* fails, fix the spacing in the fixture before suspecting the
parser — real transcripts are column-aligned and a hand-typed one easily is not. Assertions about
counts and names are robust to a column of drift; only the PennMUSH duration assertions are not, and
that layout is the one verified most carefully.

```csharp
namespace MUI.Crawl.Tests.Support;

/// <summary>
/// Real-shaped <c>WHO</c> and <c>DOING</c> responses (spec §13's "corpus of real, softcode-customised
/// DOING headers"). Verbatim shapes, including the trailing spaces and the blank <c>Doing</c> cells,
/// because those are exactly what a structural parser has to survive.
/// </summary>
public static class WhoCorpus
{
    /// <summary>PennMUSH <c>WHO</c>. Note the single space between "On For" and "Idle" in the header.</summary>
    public const string PennMushWho =
        """
        Player Name          On For Idle  Doing
        Alice                 03:12   1m  Testing the grid
        Bran                  00:47   0s
        Cora                 1d 02h  15m  Writing softcode
        3 Players logged in, 27 record, 0 unfindable.
        """;

    /// <summary>
    /// PennMUSH <c>DOING</c> with a softcode-customised header and banner — the case a per-codebase
    /// parser loses to, and the reason §6.3 says "structural, not dialectal".
    /// </summary>
    public const string PennMushDoingCustomised =
        """
        === Nightfall: who is about ===
        Name                 Idle   Doing
        Alice                  1m   Brewing tea
        Bran                   0s   Idle in the library
        2 players.
        """;

    /// <summary>TinyMUX <c>WHO</c>. Empty Doing cells, and a footer that mentions two other numbers.</summary>
    public const string TinyMuxWho =
        """
        Player Name        On For Idle  Doing
        Alice              00:12   0s
        Bran               03:44   2m   Exploring
        Cora               10:01   1h
        3 Players logged in, 26 record, 0 via SSL.
        """;

    /// <summary>RhostMUSH <c>WHO</c>: rule lines above and below the body, and five columns.</summary>
    public const string RhostMushWho =
        """
        Player Name        On For Idle   Cmds   Host
        --------------------------------------------
        Alice              00:34   1m      12   10.0.0.4
        Bran               02:15   0s     301   10.0.0.9
        --------------------------------------------
        2 Players logged in, 14 record, 0 unfindable.
        """;

    /// <summary>Evennia: a box-drawn table, and a summary that says "accounts" rather than "players".</summary>
    public const string EvenniaWho =
        """
        +------------------------------------------+
        | Account    On for   Idle   Room     Cmds |
        +------------------------------------------+
        | Alice      01:12    0m     Cafe     45   |
        | Bran       00:05    2m     Library  7    |
        +------------------------------------------+
        2 unique accounts logged in.
        """;

    /// <summary>
    /// A DIKU-family game, which generally does not answer WHO before login (spec §6.3). This must be
    /// <c>Unknown</c> — emphatically not zero, which would render as a running game with nobody in it.
    /// </summary>
    public const string DikuHuh =
        """
        Huh?!?
        By what name do you wish to be known?
        """;

    /// <summary>
    /// A game whose header has been rewritten past recognition: prose, no table, no summary. The
    /// honest answer is "we could not read it", and spec §5.4 renders that as a hatched cell.
    /// </summary>
    public const string RewrittenPastRecognition =
        """
        The night is quiet and the register is sealed.
        Type CONNECT <name> <password> to join us.
        """;

    /// <summary>
    /// PennMUSH with nobody connected. A measured zero, and a filled cell — the fact that we got in
    /// and nobody was there is real and useful (spec §5.4).
    /// </summary>
    public const string PennMushEmpty =
        """
        Player Name          On For Idle  Doing
        0 Players logged in, 27 record, 0 unfindable.
        """;

    /// <summary>Every fixture, for the tests that assert a property over all of them.</summary>
    public static IReadOnlyList<(string Name, string Text)> All =>
    [
        (nameof(PennMushWho), PennMushWho),
        (nameof(PennMushDoingCustomised), PennMushDoingCustomised),
        (nameof(TinyMuxWho), TinyMuxWho),
        (nameof(RhostMushWho), RhostMushWho),
        (nameof(EvenniaWho), EvenniaWho),
        (nameof(DikuHuh), DikuHuh),
        (nameof(RewrittenPastRecognition), RewrittenPastRecognition),
        (nameof(PennMushEmpty), PennMushEmpty),
    ];
}
```

- [ ] **Step 2: Write the failing test**

Create `tests/MUI.Crawl.Tests/WhoParserTests.cs`:

```csharp
using MUI.Crawl.Tests.Support;
using MUI.Crawl.Who;

namespace MUI.Crawl.Tests;

/// <summary>
/// Spec §6.3. Structural, not dialectal: none of these tests names a codebase in the parser, and the
/// parser has no branch that does either.
/// </summary>
public class WhoParserTests
{
    [Test]
    public async Task PennMushIsReadDownToItsColumns()
    {
        var reading = WhoParser.Parse(WhoCorpus.PennMushWho);

        await Assert.That(reading.Confidence).IsEqualTo(WhoConfidence.PerPlayer);
        await Assert.That(reading.Count).IsEqualTo(3);
        await Assert.That(reading.IdentifiablePlayers).IsEqualTo(3);

        var table = WhoParser.ParseTable(WhoCorpus.PennMushWho)!;

        await Assert.That(table.Rows.Select(r => r.Name)).IsEquivalentTo(new[] { "Alice", "Bran", "Cora" });
        await Assert.That(table.SummaryCount).IsEqualTo(3);

        // The single space between "On For" and "Idle" in Penn's header is the whole reason the
        // column detector sub-splits. Without it these are null and §11's idle histogram is empty.
        await Assert.That(table.Rows[0].Idle).IsEqualTo(TimeSpan.FromMinutes(1));
        await Assert.That(table.Rows[0].OnFor).IsEqualTo(new TimeSpan(3, 12, 0));
        await Assert.That(table.Rows[0].Doing).IsEqualTo("Testing the grid");
        await Assert.That(table.Rows[2].OnFor).IsEqualTo(new TimeSpan(1, 2, 0, 0));
        await Assert.That(table.Rows[2].Idle).IsEqualTo(TimeSpan.FromMinutes(15));
    }

    [Test]
    public async Task ARowWithAnEmptyLastColumnIsStillARow()
    {
        var table = WhoParser.ParseTable(WhoCorpus.PennMushWho)!;

        await Assert.That(table.Rows[1].Name).IsEqualTo("Bran");
        await Assert.That(table.Rows[1].Doing).IsNull();
    }

    [Test]
    public async Task ASoftcodeCustomisedDoingHeaderIsStillRead()
    {
        var reading = WhoParser.Parse(WhoCorpus.PennMushDoingCustomised);

        await Assert.That(reading.Confidence).IsEqualTo(WhoConfidence.PerPlayer);
        await Assert.That(reading.Count).IsEqualTo(2);

        var table = WhoParser.ParseTable(WhoCorpus.PennMushDoingCustomised)!;

        // The decorative banner above the header is not a row, and the header is not a player.
        await Assert.That(table.Rows.Select(r => r.Name)).IsEquivalentTo(new[] { "Alice", "Bran" });
    }

    [Test]
    public async Task TinyMuxIsRead()
    {
        var reading = WhoParser.Parse(WhoCorpus.TinyMuxWho);

        await Assert.That(reading.Confidence).IsEqualTo(WhoConfidence.PerPlayer);
        await Assert.That(reading.Count).IsEqualTo(3);
        await Assert.That(WhoParser.ParseTable(WhoCorpus.TinyMuxWho)!.Rows.Select(r => r.Name))
            .IsEquivalentTo(new[] { "Alice", "Bran", "Cora" });
    }

    [Test]
    public async Task RhostMushIsReadAndItsRuleLinesAreNotRows()
    {
        var reading = WhoParser.Parse(WhoCorpus.RhostMushWho);

        await Assert.That(reading.Confidence).IsEqualTo(WhoConfidence.PerPlayer);
        await Assert.That(reading.Count).IsEqualTo(2);
        await Assert.That(WhoParser.ParseTable(WhoCorpus.RhostMushWho)!.Rows.Select(r => r.Name))
            .IsEquivalentTo(new[] { "Alice", "Bran" });
    }

    [Test]
    public async Task EvenniaSBoxDrawnTableIsRead()
    {
        var reading = WhoParser.Parse(WhoCorpus.EvenniaWho);

        await Assert.That(reading.Confidence).IsEqualTo(WhoConfidence.PerPlayer);
        await Assert.That(reading.Count).IsEqualTo(2);

        // The box characters are chrome, and "accounts" is the same fact as "players".
        await Assert.That(WhoParser.ParseTable(WhoCorpus.EvenniaWho)!.Rows.Select(r => r.Name))
            .IsEquivalentTo(new[] { "Alice", "Bran" });
    }

    [Test]
    public async Task ADikuStyleRefusalIsUnknownAndEmphaticallyNotZero()
    {
        var reading = WhoParser.Parse(WhoCorpus.DikuHuh);

        await Assert.That(reading.Confidence).IsEqualTo(WhoConfidence.Unknown);
        await Assert.That(reading.Count).IsNull();
        await Assert.That(reading.HasCount).IsFalse();
        await Assert.That(WhoParser.ParseTable(WhoCorpus.DikuHuh)).IsNull();
    }

    [Test]
    public async Task AHeaderRewrittenPastRecognitionIsUnknownAndEmphaticallyNotZero()
    {
        var reading = WhoParser.Parse(WhoCorpus.RewrittenPastRecognition);

        // The bug this prevents: a game rendering as permanently dark on the heatmap while running
        // perfectly well, because its unreadable WHO was recorded as "nobody is here".
        await Assert.That(reading.Confidence).IsEqualTo(WhoConfidence.Unknown);
        await Assert.That(reading.Count).IsNull();
    }

    [Test]
    public async Task AMeasuredZeroIsACountAndNotAnAbsence()
    {
        var reading = WhoParser.Parse(WhoCorpus.PennMushEmpty);

        await Assert.That(reading.Confidence).IsEqualTo(WhoConfidence.Count);
        await Assert.That(reading.Count).IsEqualTo(0);
        await Assert.That(reading.HasCount).IsTrue();
    }

    [Test]
    public async Task TheSummaryWinsWhenItDisagreesWithTheRowsAndTheRowsAreStillCounted()
    {
        // A game whose WHO pages its output, or hides staff from the listing. The summary is what the
        // game says about itself and is the count; the rows are what we could see and are what §11's
        // aggregates are computed from. Neither is discarded to make them agree.
        const string paged =
            """
            Player Name          On For Idle  Doing
            Alice                 03:12   1m  Testing
            Bran                  00:47   0s
            9 Players logged in, 27 record, 0 unfindable.
            """;

        var reading = WhoParser.Parse(paged);

        await Assert.That(reading.Count).IsEqualTo(9);
        await Assert.That(reading.IdentifiablePlayers).IsEqualTo(2);
    }

    [Test]
    public async Task ASummaryWithNoTableIsStillACount()
    {
        var reading = WhoParser.Parse("There are 12 players connected.\r\n");

        await Assert.That(reading.Confidence).IsEqualTo(WhoConfidence.Count);
        await Assert.That(reading.Count).IsEqualTo(12);
        await Assert.That(reading.IdentifiablePlayers).IsNull();
    }

    [Test]
    public async Task TheColonSpellingOfTheSummaryIsRead()
    {
        await Assert.That(WhoParser.Parse("Players: 4\r\n").Count).IsEqualTo(4);
    }

    [Test]
    public async Task AnsiIsStrippedBeforeParsingAndDoesNotShiftTheColumns()
    {
        const string coloured =
            "[1;37mPlayer Name          On For Idle  Doing[0m\r\n" +
            "[36mAlice[0m                 03:12   1m  Testing the grid\r\n" +
            "1 Players logged in, 27 record, 0 unfindable.\r\n";

        var table = WhoParser.ParseTable(coloured)!;

        await Assert.That(table.SummaryCount).IsEqualTo(1);
        await Assert.That(table.Rows.Single().Name).IsEqualTo("Alice");
    }

    [Test]
    public async Task AnEmptyOrWhitespaceTranscriptIsUnknown()
    {
        await Assert.That(WhoParser.Parse(string.Empty).Confidence).IsEqualTo(WhoConfidence.Unknown);
        await Assert.That(WhoParser.Parse("   \r\n\r\n").Confidence).IsEqualTo(WhoConfidence.Unknown);
        await Assert.That(WhoParser.ParseTable("   ")).IsNull();
    }

    [Test]
    public async Task TheParserNeverReportsThatNobodyAsked()
    {
        // NotAttempted is a fact about the probe, not about a response, and this is only ever called
        // on a response. A parser that returned it would tell PresenceWriter no WHO was sent when one
        // was — which is the §5.4 confusion the fourth state exists to end, re-introduced one layer up.
        foreach (var (name, text) in WhoCorpus.All)
        {
            await Assert.That(WhoParser.Parse(text).WasAttempted).IsTrue().Because(name);
        }

        await Assert.That(WhoParser.Parse(string.Empty)).IsEqualTo(WhoReading.Unreadable);
    }

    [Test]
    public async Task TheColumnDetectorSubSplitsOnlyWhereTheBodyAgrees()
    {
        // The two cases in one assertion pair. "On For Idle" splits because every row has whitespace
        // before "Idle" and some row starts a value there; "Player Name" does not, because no row
        // starts a value under "Name".
        var starts = ColumnLayout.Starts(
            "Player Name          On For Idle  Doing",
            ["Alice                 03:12   1m  Testing the grid"]);

        await Assert.That(starts).IsEquivalentTo(new[] { 0, 21, 28, 34 });
    }

    [Test]
    public async Task ALineThatDoesNotFitTheColumnsIsNotARow()
    {
        var starts = ColumnLayout.Starts(
            "Player Name          On For Idle  Doing",
            ["Alice                 03:12   1m  Testing the grid"]);

        await Assert.That(ColumnLayout.Fits("Alice                 03:12   1m  Testing", starts)).IsTrue();
        await Assert.That(ColumnLayout.Fits("Type CONNECT <name> <password> to join us.", starts)).IsFalse();
    }
}
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `error CS0246: The type or namespace name 'WhoParser' could not be found`.

- [ ] **Step 4: Write the ANSI stripper**

Create `src/MUI.Crawl/Who/AnsiText.cs`:

```csharp
using System.Text;

namespace MUI.Crawl.Who;

/// <summary>
/// Removes ANSI escape sequences, leaving the text a structural parser can measure columns in.
/// </summary>
/// <remarks>
/// The connect screen is stored <em>with</em> its ANSI (spec §6.2) — it is a display asset. This is
/// the parsing side of the same bytes, and it lives in one place so the two can never disagree about
/// what a line's tenth character is.
/// </remarks>
public static class AnsiText
{
    private const char Escape = '';

    public static string Strip(string text)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains(Escape, StringComparison.Ordinal))
        {
            return text ?? string.Empty;
        }

        var result = new StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] != Escape)
            {
                result.Append(text[i]);
                i++;
                continue;
            }

            if (i + 1 >= text.Length)
            {
                break; // a trailing escape with nothing after it
            }

            switch (text[i + 1])
            {
                case '[':
                    // CSI: parameters and intermediates, then a final byte in @…~.
                    i += 2;
                    while (i < text.Length && text[i] is >= ' ' and < '@')
                    {
                        i++;
                    }

                    if (i < text.Length)
                    {
                        i++; // the final byte
                    }

                    break;

                case ']':
                    // OSC: terminated by BEL or by ESC \.
                    i += 2;
                    while (i < text.Length && text[i] != '\a' && !(text[i] == Escape && i + 1 < text.Length && text[i + 1] == '\\'))
                    {
                        i++;
                    }

                    i += i < text.Length && text[i] == '\a' ? 1 : 2;
                    break;

                default:
                    i += 2; // a two-character escape
                    break;
            }
        }

        return result.ToString();
    }
}
```

- [ ] **Step 5: Write the column detector**

Create `src/MUI.Crawl/Who/ColumnLayout.cs`:

```csharp
namespace MUI.Crawl.Who;

/// <summary>
/// Finds the column boundaries of a fixed-width table from its header and its body.
/// </summary>
/// <remarks>
/// <para>
/// Two rules, and both are needed. Splitting the header on runs of ≥2 spaces is the primary pass, and
/// on its own it merges PennMUSH's <c>On For</c> and <c>Idle</c> — its real header separates them
/// with a <em>single</em> space, so the most common MUSH codebase would lose its idle column.
/// Splitting on every single space instead splits <c>Player Name</c> in two on any table whose names
/// are all short.
/// </para>
/// <para>
/// So a multi-word header field is sub-split only where the body agrees: the column before the
/// candidate boundary must be whitespace in <em>every</em> row, and some row must actually start a
/// value within eight columns of it. Penn's <c>Idle</c> passes both; the space inside
/// <c>Player Name</c> fails the second, because no row starts a value under <c>Name</c>.
/// </para>
/// </remarks>
public static class ColumnLayout
{
    /// <summary>How far past a candidate boundary a row's value may start and still endorse it.</summary>
    private const int EndorsementWindow = 8;

    /// <summary>The starting column of each field, ascending. Empty when the header has no columns.</summary>
    public static IReadOnlyList<int> Starts(string header, IReadOnlyList<string> body)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(body);

        var primary = TokenStarts(header, minimumGap: 2);
        if (primary.Count == 0)
        {
            return [];
        }

        var inner = TokenStarts(header, minimumGap: 1);
        var starts = new List<int>();
        for (var field = 0; field < primary.Count; field++)
        {
            starts.Add(primary[field]);
            var end = field + 1 < primary.Count ? primary[field + 1] : header.Length;

            foreach (var candidate in inner)
            {
                if (candidate > primary[field] && candidate < end && BodyEndorses(candidate, body))
                {
                    starts.Add(candidate);
                }
            }
        }

        starts.Sort();
        return starts;
    }

    /// <summary>
    /// Whether a line is a row of this table: whitespace at every column boundary, and something in
    /// the first field. A footer, a prompt or a line of prose fails this, which is what keeps them
    /// from being counted as players.
    /// </summary>
    public static bool Fits(string line, IReadOnlyList<int> starts)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(starts);

        if (starts.Count == 0 || line.Length == 0)
        {
            return false;
        }

        for (var i = 1; i < starts.Count; i++)
        {
            var boundary = starts[i] - 1;
            if (boundary < line.Length && !char.IsWhiteSpace(line[boundary]))
            {
                return false;
            }
        }

        return Field(line, starts, 0).Trim().Length > 0;
    }

    /// <summary>The text of one field, or empty when the line stops before it.</summary>
    public static string Field(string line, IReadOnlyList<int> starts, int index)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(starts);

        if (index < 0 || index >= starts.Count)
        {
            return string.Empty;
        }

        var start = starts[index];
        if (start >= line.Length)
        {
            return string.Empty;
        }

        var end = index + 1 < starts.Count ? Math.Min(starts[index + 1], line.Length) : line.Length;
        return end <= start ? string.Empty : line[start..end];
    }

    private static bool BodyEndorses(int candidate, IReadOnlyList<string> body)
    {
        if (body.Count == 0 || candidate == 0)
        {
            return false;
        }

        // Nothing may straddle the boundary.
        foreach (var line in body)
        {
            if (candidate - 1 < line.Length && !char.IsWhiteSpace(line[candidate - 1]))
            {
                return false;
            }
        }

        // And something must actually begin here.
        foreach (var line in body)
        {
            var limit = Math.Min(line.Length, candidate + EndorsementWindow);
            for (var column = candidate; column < limit; column++)
            {
                if (!char.IsWhiteSpace(line[column]) && (column == 0 || char.IsWhiteSpace(line[column - 1])))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// The column of every token start, where a "token" is preceded by at least
    /// <paramref name="minimumGap"/> spaces. The start of the line always counts as a boundary.
    /// </summary>
    private static List<int> TokenStarts(string line, int minimumGap)
    {
        var starts = new List<int>();
        var gap = minimumGap;
        for (var i = 0; i < line.Length; i++)
        {
            if (char.IsWhiteSpace(line[i]))
            {
                gap++;
                continue;
            }

            if (gap >= minimumGap)
            {
                starts.Add(i);
            }

            gap = 0;
        }

        return starts;
    }
}
```

- [ ] **Step 6: Write the parser**

Create `src/MUI.Crawl/Who/WhoParser.cs`:

```csharp
using System.Globalization;
using System.Text.RegularExpressions;

namespace MUI.Crawl.Who;

/// <summary>
/// One parsed <c>WHO</c> table. <b>Rows carry names, in memory only</b>, so §11's aggregates can be
/// computed; nothing here is ever stored, and <c>PresenceAggregates</c> is what leaves the probe.
/// </summary>
public sealed record WhoTable(IReadOnlyList<WhoRow> Rows, int? SummaryCount);

/// <summary>One player as the table listed them. Never persisted, never serialised, never logged.</summary>
public sealed record WhoRow(string Name, TimeSpan? OnFor, TimeSpan? Idle, string? Doing);

/// <summary>
/// The structural <c>WHO</c>/<c>DOING</c> parser (spec §6.3). It knows nothing about any codebase and
/// must never learn: Penn, MUX and Rhost all let operators rewrite the header in softcode, so a
/// per-dialect parser is a maintenance treadmill that still loses to any game that customised it.
/// </summary>
/// <remarks>
/// <b>It never fabricates.</b> Every number it reports was either read out of a summary line or is the
/// number of rows it could actually see. An unreadable response is <see cref="WhoConfidence.Unknown"/>
/// with a null count — never zero, because a zero is a filled cell on the activity heatmap and means
/// "we got in and nobody was there" (spec §5.4).
/// </remarks>
public static class WhoParser
{
    /// <summary>How many non-blank lines from the end to search for a summary.</summary>
    private const int SummaryScanLines = 12;

    /// <summary>Characters a horizontal rule may be made of. A rule carries no data and is removed.</summary>
    private const string RuleCharacters = "-=_~*+|#.:";

    private static readonly RegexOptions Options =
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture;

    /// <summary>
    /// The trailing summary, in the shapes real games write it. Structural in the sense that matters:
    /// none of these is codebase-specific, and a game using none of them falls through to row counting.
    /// </summary>
    private static readonly Regex[] SummaryPatterns =
    [
        // "3 Players logged in, 27 record, 0 unfindable." / "2 unique accounts logged in."
        new(@"^(?<n>\d{1,6})\s+(?:unique\s+)?(?:players?|accounts?|characters?|users?|connections?)\s+(?:logged\s*in|connected|online)\b", Options),

        // "Players: 12" / "Connected: 12"
        new(@"^(?:players?|accounts?|characters?|users?|connected)\s*[:=]\s*(?<n>\d{1,6})\b", Options),

        // "12 players." / "3 accounts"
        new(@"^(?<n>\d{1,6})\s+(?:players?|accounts?|characters?|users?)\s*\.?$", Options),

        // "There are 12 players connected."
        new(@"^there\s+(?:are|is)\s+(?<n>\d{1,6})\s+(?:players?|accounts?|characters?|users?)\b", Options),
    ];

    /// <summary>
    /// The reading spec §6.3 defines: <c>PerPlayer</c> when every row yielded a name, <c>Count</c>
    /// when there is a number but no usable name column, <c>Unknown</c> when there is neither.
    /// </summary>
    /// <remarks>
    /// It never returns <see cref="WhoConfidence.NotAttempted"/>. Whether a <c>WHO</c> was sent at all
    /// is <c>ProbeSession</c>'s fact and not a parser's — this is only ever called on a response — so
    /// the answer to an unreadable one is <see cref="WhoReading.Unreadable"/>.
    /// </remarks>
    public static WhoReading Parse(string transcript)
    {
        var table = ParseTable(transcript);
        if (table is null)
        {
            return WhoReading.Unreadable;
        }

        var named = table.Rows.Count(row => row.Name.Length > 0);
        int? count = table.SummaryCount ?? (table.Rows.Count > 0 ? table.Rows.Count : null);
        if (count is null)
        {
            return WhoReading.Unreadable;
        }

        // The summary is what the game says about itself and is the count; the rows are what we could
        // see and are what the aggregates are computed from. When they disagree — a paged WHO, a game
        // that hides staff — neither is discarded to make them agree.
        return table.Rows.Count > 0 && named == table.Rows.Count
            ? new WhoReading(WhoConfidence.PerPlayer, count, named)
            : new WhoReading(WhoConfidence.Count, count);
    }

    /// <summary>The table itself, or null when nothing structural was found.</summary>
    public static WhoTable? ParseTable(string transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript))
        {
            return null;
        }

        var lines = Normalise(transcript);
        if (lines.Count == 0)
        {
            return null;
        }

        var (summary, summaryIndex) = FindSummary(lines);
        var table = FindTable(lines, summaryIndex);
        if (table is null)
        {
            return summary is null ? null : new WhoTable([], summary);
        }

        var rows = ReadRows(lines, table.Value);
        return rows.Count == 0 && summary is null ? null : new WhoTable(rows, summary);
    }

    /// <summary>
    /// ANSI stripped, line endings normalised, trailing whitespace gone, box drawing unwrapped, and
    /// rule lines removed outright — a rule carries no data, and removing it joins a boxed header to
    /// its own body instead of leaving them in two separate blocks.
    /// </summary>
    private static List<string> Normalise(string transcript)
    {
        var lines = new List<string>();
        foreach (var raw in AnsiText.Strip(transcript).Split('\n'))
        {
            var line = Debox(raw.TrimEnd('\r').TrimEnd());
            if (IsRule(line))
            {
                continue;
            }

            lines.Add(line);
        }

        while (lines.Count > 0 && lines[0].Length == 0)
        {
            lines.RemoveAt(0);
        }

        while (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return lines;
    }

    /// <summary>
    /// Strips the outer bars of a box-drawn row. Uniform across the table, so the columns shift by one
    /// together and stay aligned with each other.
    /// </summary>
    private static string Debox(string line)
    {
        if (line.Length < 2 || line[0] is not ('|' or '│'))
        {
            return line;
        }

        var trimmed = line[1..];
        if (trimmed.Length > 0 && trimmed[^1] is '|' or '│')
        {
            trimmed = trimmed[..^1];
        }

        return trimmed.TrimEnd();
    }

    private static bool IsRule(string line)
    {
        var glyphs = 0;
        foreach (var c in line)
        {
            if (char.IsWhiteSpace(c))
            {
                continue;
            }

            if (!RuleCharacters.Contains(c, StringComparison.Ordinal) && c is not ('─' or '═' or '━'))
            {
                return false;
            }

            glyphs++;
        }

        return glyphs >= 4;
    }

    private static (int? Count, int Index) FindSummary(List<string> lines)
    {
        var scanned = 0;
        for (var i = lines.Count - 1; i >= 0 && scanned < SummaryScanLines; i--)
        {
            if (lines[i].Length == 0)
            {
                continue;
            }

            scanned++;
            var candidate = lines[i].TrimStart();
            foreach (var pattern in SummaryPatterns)
            {
                var match = pattern.Match(candidate);
                if (match.Success &&
                    int.TryParse(match.Groups["n"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var count))
                {
                    return (count, i);
                }
            }
        }

        return (null, lines.Count);
    }

    /// <summary>
    /// The first block of consecutive non-blank lines that has a header with at least two columns.
    /// The search stops at <paramref name="limit"/> — the summary line — so a footer can never be
    /// mistaken for a player.
    /// </summary>
    private static (int Header, int BodyEnd, IReadOnlyList<int> Starts)? FindTable(List<string> lines, int limit)
    {
        var index = 0;
        var end = Math.Min(limit, lines.Count);
        while (index < end)
        {
            while (index < end && lines[index].Length == 0)
            {
                index++;
            }

            if (index >= end)
            {
                break;
            }

            var blockStart = index;
            while (index < end && lines[index].Length > 0)
            {
                index++;
            }

            var blockEnd = index;

            // The header is the first line in the block with two or more space-separated columns. A
            // decorative banner above it has one, and an echoed "WHO" has one, so both are skipped.
            for (var header = blockStart; header < blockEnd; header++)
            {
                var body = lines.GetRange(header + 1, blockEnd - header - 1);
                var starts = ColumnLayout.Starts(lines[header], body);
                if (starts.Count < 2)
                {
                    continue;
                }

                var fitting = body.Count(line => ColumnLayout.Fits(line, starts));

                // A block whose lines mostly do not fit its own header is prose that happened to have
                // two columns' worth of spacing, not a table.
                if (body.Count > 0 && fitting * 2 < body.Count)
                {
                    continue;
                }

                return (header, blockEnd, starts);
            }
        }

        return null;
    }

    private static List<WhoRow> ReadRows(List<string> lines, (int Header, int BodyEnd, IReadOnlyList<int> Starts) table)
    {
        var (header, bodyEnd, starts) = table;
        var labels = new List<string>();
        for (var i = 0; i < starts.Count; i++)
        {
            labels.Add(ColumnLayout.Field(lines[header], starts, i).Trim());
        }

        var idle = IndexOfLabel(labels, "idle");
        var onFor = IndexOfLabel(labels, "on for", "onfor", "on-for", "connected", "on");
        var doing = IndexOfLabel(labels, "doing", "description", "status", "room");

        var rows = new List<WhoRow>();
        for (var i = header + 1; i < bodyEnd; i++)
        {
            var line = lines[i];
            if (!ColumnLayout.Fits(line, starts))
            {
                continue;
            }

            var name = FirstToken(ColumnLayout.Field(line, starts, 0));
            var doingText = doing > 0 ? ColumnLayout.Field(line, starts, doing).Trim() : string.Empty;

            rows.Add(new WhoRow(
                name,
                onFor > 0 ? TryParseDuration(ColumnLayout.Field(line, starts, onFor)) : null,
                idle > 0 ? TryParseDuration(ColumnLayout.Field(line, starts, idle)) : null,
                doingText.Length == 0 ? null : doingText));
        }

        return rows;
    }

    /// <summary>The first labelled column at index 1 or above, or -1. Column 0 is always the name.</summary>
    private static int IndexOfLabel(List<string> labels, params string[] wanted)
    {
        for (var i = 1; i < labels.Count; i++)
        {
            foreach (var candidate in wanted)
            {
                if (labels[i].Contains(candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
        }

        return -1;
    }

    private static string FirstToken(string field)
    {
        var trimmed = field.AsSpan().Trim();
        if (trimmed.IsEmpty)
        {
            return string.Empty;
        }

        var space = trimmed.IndexOf(' ');
        return space < 0 ? trimmed.ToString() : trimmed[..space].ToString();
    }

    /// <summary>
    /// The durations MU* servers write: <c>03:12</c>, <c>01:02:33</c>, <c>15m</c>, <c>1h</c>,
    /// <c>2d</c>, <c>1d 02h</c>, <c>0s</c>. Anything else — <c>--</c>, a blank, a word — is null,
    /// which costs a bucket and never invents one.
    /// </summary>
    private static TimeSpan? TryParseDuration(string field)
    {
        var text = field.Trim();
        if (text.Length == 0)
        {
            return null;
        }

        var total = TimeSpan.Zero;
        var read = false;
        foreach (var part in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.Contains(':', StringComparison.Ordinal))
            {
                var pieces = part.Split(':');
                if (pieces.Length is < 2 or > 3)
                {
                    return null;
                }

                var numbers = new int[pieces.Length];
                for (var i = 0; i < pieces.Length; i++)
                {
                    if (!int.TryParse(pieces[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
                    {
                        return null;
                    }
                }

                total += pieces.Length == 2
                    ? new TimeSpan(numbers[0], numbers[1], 0)
                    : new TimeSpan(numbers[0], numbers[1], numbers[2]);
                read = true;
                continue;
            }

            var unit = char.ToLowerInvariant(part[^1]);
            if (!int.TryParse(part[..^1], NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            {
                return null;
            }

            total += unit switch
            {
                's' => TimeSpan.FromSeconds(value),
                'm' => TimeSpan.FromMinutes(value),
                'h' => TimeSpan.FromHours(value),
                'd' => TimeSpan.FromDays(value),
                'w' => TimeSpan.FromDays(value * 7),
                _ => TimeSpan.MinValue,
            };

            if (total < TimeSpan.Zero)
            {
                return null;
            }

            read = true;
        }

        return read ? total : null;
    }
}
```

- [ ] **Step 7: Run the tests to verify they pass**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Crawl.Tests </dev/null
```
Expected: PASS — 17 new tests.

If a *column* assertion fails (`Idle`, `OnFor`, `Doing`, or `ColumnLayout.Starts` returning something
other than `{0, 21, 28, 34}`), the fixture's spacing is wrong, not the parser: count the columns in
`WhoCorpus` and fix the string. If a *count* or *name* assertion fails, the parser is wrong.

- [ ] **Step 8: Commit**

```bash
git add src/MUI.Crawl/Who tests/MUI.Crawl.Tests/Support/WhoCorpus.cs tests/MUI.Crawl.Tests/WhoParserTests.cs
git commit -m "feat(crawl): the structural WHO/DOING parser

Spec §6.3, and it names no codebase. Trailing summary line first, row counting
between header and footer second, Unknown third — and never a fabricated zero,
because an unreadable WHO has to stay distinguishable from an empty game or the
heatmap renders a running game as permanently dark.

The column detector splits on runs of two spaces and then sub-splits a multi-word
header field only where the body agrees. Penn's real header separates 'On For' and
'Idle' with one space, so the obvious split loses the idle column on the most
common MUSH codebase; splitting on every space splits 'Player Name' in two."
```

---

### Task 11: The parser never returns a count it did not read

Spec §13 ("property tests for the structural WHO parser"), CLAUDE.md rule 4 ("parsers never
fabricate").

Task 10 proves the parser reads eight known shapes. This task proves the *negative*: that across
inputs nobody wrote by hand, every number it produces was either read from the text or is a count of
rows it actually saw. That is the property the whole activity heatmap rests on.

**Files:**
- Test: `tests/MUI.Crawl.Tests/WhoParserPropertyTests.cs`

**Interfaces:**
- Consumes: `WhoParser`, `WhoTable` (Task 10); `WhoCorpus` (Task 10).
- Produces: nothing.

- [ ] **Step 1: Write the failing test**

Create `tests/MUI.Crawl.Tests/WhoParserPropertyTests.cs`:

```csharp
using System.Globalization;
using System.Text;
using MUI.Crawl.Tests.Support;
using MUI.Crawl.Who;

namespace MUI.Crawl.Tests;

/// <summary>
/// Spec §13's property tests. Seeded and deterministic — a property test that fails differently every
/// run is a flake, and a flake in the rule "parsers never fabricate" would be turned off within a week.
/// </summary>
public class WhoParserPropertyTests
{
    private const int Cases = 500;

    /// <summary>Letters, punctuation, spaces and newlines. <b>No digits</b>, deliberately.</summary>
    private static string DigitFreeNoise(Random random)
    {
        const string alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ .,:;!?-_|[]()<>*'\"\t";
        var text = new StringBuilder();
        var lines = random.Next(0, 14);
        for (var line = 0; line < lines; line++)
        {
            var length = random.Next(0, 70);
            for (var i = 0; i < length; i++)
            {
                text.Append(alphabet[random.Next(alphabet.Length)]);
            }

            text.Append("\r\n");
        }

        return text.ToString();
    }

    [Test]
    public async Task ATranscriptWithNoDigitsCanOnlyYieldACountItCounted()
    {
        // The core property. With no digits in the input there is no summary line to read, so any
        // count the parser reports must be the number of rows it actually saw — and if it saw none,
        // it must say Unknown rather than zero.
        var random = new Random(20260730);
        for (var i = 0; i < Cases; i++)
        {
            var transcript = DigitFreeNoise(random);
            var reading = WhoParser.Parse(transcript);
            if (reading.Confidence == WhoConfidence.Unknown)
            {
                await Assert.That(reading.Count).IsNull();
                continue;
            }

            var table = WhoParser.ParseTable(transcript);

            await Assert.That(table).IsNotNull();
            await Assert.That(reading.Count).IsEqualTo(table!.Rows.Count)
                .Because($"the parser produced a number nothing in the text said:\n{transcript}");
            await Assert.That(reading.Count).IsGreaterThan(0);
        }
    }

    [Test]
    public async Task EveryReportedCountIsEitherWrittenInTheTextOrIsTheNumberOfRowsRead()
    {
        // The same property over noise that does contain digits: the number must be one the text
        // literally contains, or the row count. Nothing else is a reading; it would be a guess.
        var random = new Random(19951203);
        const string alphabet = "abcdefgh 0123456789ijklmnop .,:|-\t";
        for (var i = 0; i < Cases; i++)
        {
            var text = new StringBuilder();
            var lines = random.Next(0, 12);
            for (var line = 0; line < lines; line++)
            {
                var length = random.Next(0, 60);
                for (var c = 0; c < length; c++)
                {
                    text.Append(alphabet[random.Next(alphabet.Length)]);
                }

                text.Append("\r\n");
            }

            var transcript = text.ToString();
            var reading = WhoParser.Parse(transcript);
            if (reading.Count is not { } count)
            {
                continue;
            }

            var table = WhoParser.ParseTable(transcript)!;
            var written = transcript.Contains(count.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);

            await Assert.That(written || count == table.Rows.Count).IsTrue()
                .Because($"the count {count} was neither written in the text nor counted:\n{transcript}");
        }
    }

    [Test]
    public async Task NoTruncationOfARealTranscriptEverInventsAPlayer()
    {
        // Transcripts arrive over TCP and are capped (Task 9), so the parser sees prefixes of real
        // responses constantly. A prefix may read fewer players than the whole; it may never read more.
        foreach (var (name, text) in WhoCorpus.All)
        {
            var whole = WhoParser.Parse(text);
            if (whole.Count is not { } full)
            {
                continue;
            }

            for (var cut = 1; cut < text.Length; cut++)
            {
                var reading = WhoParser.Parse(text[..cut]);
                var table = WhoParser.ParseTable(text[..cut]);
                if (reading.Count is not { } partial)
                {
                    continue;
                }

                // Either the summary survived the cut (so the count is the game's own number), or the
                // count is rows we saw — and rows we saw cannot exceed the rows that exist.
                var fromSummary = table?.SummaryCount == partial;

                await Assert.That(fromSummary || partial <= full).IsTrue()
                    .Because($"{name} cut at {cut} reported {partial} where the whole reports {full}");
            }
        }
    }

    [Test]
    public async Task ATrailingNewlineOrCarriageReturnNeverChangesTheReading()
    {
        // The same transcript ending three ways, because a socket decides which one we get.
        foreach (var (name, text) in WhoCorpus.All)
        {
            var baseline = WhoParser.Parse(text);

            await Assert.That(WhoParser.Parse(text + "\r\n")).IsEqualTo(baseline).Because(name);
            await Assert.That(WhoParser.Parse(text + "\n")).IsEqualTo(baseline).Because(name);
            await Assert.That(WhoParser.Parse(text.Replace("\n", "\r\n", StringComparison.Ordinal)))
                .IsEqualTo(baseline).Because(name);
        }
    }

    [Test]
    public async Task LeadingChatterBeforeTheTableNeverBecomesAPlayer()
    {
        // What actually arrives: the tail of the connect screen, then the table. None of it is a row.
        const string chatter =
            "Welcome to the game. Please connect or create a character.\r\n" +
            "Type 'help' for instructions.\r\n" +
            "\r\n";

        foreach (var (name, text) in WhoCorpus.All)
        {
            var baseline = WhoParser.Parse(text);
            var withChatter = WhoParser.Parse(chatter + text);

            await Assert.That(withChatter.Count).IsEqualTo(baseline.Count).Because(name);
        }
    }

    [Test]
    public async Task AnEchoedCommandBeforeTheHeaderIsNotAPlayer()
    {
        // Some servers echo what they were sent. "WHO" on its own line is one column, so it can never
        // be a header, and it must not be counted as a row either.
        var reading = WhoParser.Parse("WHO\r\n" + WhoCorpus.PennMushWho);

        await Assert.That(reading.Count).IsEqualTo(3);
        await Assert.That(WhoParser.ParseTable("WHO\r\n" + WhoCorpus.PennMushWho)!.Rows.Select(r => r.Name))
            .IsEquivalentTo(new[] { "Alice", "Bran", "Cora" });
    }
}
```

- [ ] **Step 2: Run the tests**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Crawl.Tests </dev/null
```
Expected: PASS — 6 new tests.

- [ ] **Step 3: If a property fails, fix the parser, not the property**

A failure here is a real fabrication bug and the property is the specification. The two likely
findings, both worth fixing in `WhoParser`:

- **Noise counted as rows.** Random text with two columns' worth of spacing forming a "table". The
  `fitting * 2 < body.Count` guard in `FindTable` is the lever; if it is not enough, require the
  header to have at least one alphabetic character in its first field.
- **A truncated transcript reading more than the whole.** A cut that removes the summary and lets
  the footer line become a row. The fix is in `Fits`, not in the summary search.

- [ ] **Step 4: Commit**

```bash
git add tests/MUI.Crawl.Tests/WhoParserPropertyTests.cs
git commit -m "test(crawl): the WHO parser never returns a count it did not read

Spec §13. Seeded and deterministic, over digit-free noise (any count must be rows
it counted), over digit-bearing noise (any count must be written in the text or be
the row count), over every prefix of every corpus fixture (a truncated transcript
may read fewer players, never more), and over leading chatter and echoed commands."
```

---

### Task 12: Salted-hash aggregates — what §11 permits to leave the probe

Spec §11 ("player names are never persisted ... aggregates use salted hashes with a rotating salt, so
a unique-player estimate is possible while re-identification across salt epochs is not"), §6.3
(per-player confidence is what unlocks them), §5.2 (`PresenceSample.aggregates`).

**Files:**
- Create: `src/MUI.Crawl/Salt.cs`
- Create: `src/MUI.Crawl/PresenceAggregates.cs`
- Create: `src/MUI.Crawl/PresenceAggregateBuilder.cs`
- Test: `tests/MUI.Crawl.Tests/PresenceAggregateTests.cs`

**Interfaces:**
- Consumes: `WhoTable`, `WhoRow` (Task 10).
- Produces: `MUI.Crawl.ISaltProvider` (`(string Epoch, byte[] Salt) Current(DateTimeOffset now)`),
  `sealed class RotatingSaltProvider(byte[] seed, TimeSpan period, TimeProvider? time = null) : ISaltProvider`,
  `static class PlayerHash { static string Of(string name, byte[] salt); }`,
  `sealed record PresenceAggregates(string SaltEpoch, IReadOnlyList<string> PlayerHashes, IReadOnlyList<int> IdleBucketCounts, IReadOnlyList<int> ConnectedBucketCounts)`,
  `static class PresenceBuckets { static IReadOnlyList<TimeSpan> Edges { get; } static int IndexFor(TimeSpan value); }`,
  `static class PresenceAggregateBuilder { static PresenceAggregates? From(WhoTable table, ISaltProvider salt, DateTimeOffset now); }`.
  Task 15 calls `PresenceAggregateBuilder.From`; Plan 2 serialises `PresenceAggregates` into
  `PresenceSample.AggregatesJson`.

- [ ] **Step 1: Write the failing test**

Create `tests/MUI.Crawl.Tests/PresenceAggregateTests.cs`:

```csharp
using System.Text;
using System.Text.Json;
using MUI.Crawl.Who;

namespace MUI.Crawl.Tests;

/// <summary>
/// Spec §11: a unique-player estimate is possible, and re-identification across salt epochs is not.
/// </summary>
public class PresenceAggregateTests
{
    private static readonly byte[] Seed = Encoding.UTF8.GetBytes("a seed that is not the salt");

    private static WhoTable Table() => new(
    [
        new WhoRow("Alice", TimeSpan.FromHours(3), TimeSpan.FromSeconds(30), "Testing"),
        new WhoRow("Bran", TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(8), null),
        new WhoRow("Cora", TimeSpan.FromHours(9), TimeSpan.FromHours(2), "Writing softcode"),
    ], SummaryCount: 3);

    [Test]
    public async Task TheSameNameInTheSameEpochHashesTheSameAndInAnotherEpochDoesNot()
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-07-30T09:00:00Z"));
        var salt = new RotatingSaltProvider(Seed, TimeSpan.FromDays(1), time);

        var (firstEpoch, firstSalt) = salt.Current(time.GetUtcNow());
        var (sameEpoch, sameSalt) = salt.Current(time.GetUtcNow().AddHours(6));

        await Assert.That(sameEpoch).IsEqualTo(firstEpoch);
        await Assert.That(PlayerHash.Of("Alice", sameSalt)).IsEqualTo(PlayerHash.Of("Alice", firstSalt));

        var (nextEpoch, nextSalt) = salt.Current(time.GetUtcNow().AddDays(2));

        await Assert.That(nextEpoch).IsNotEqualTo(firstEpoch);
        await Assert.That(PlayerHash.Of("Alice", nextSalt)).IsNotEqualTo(PlayerHash.Of("Alice", firstSalt))
            .Because("re-identification across salt epochs is exactly what rotation prevents");
    }

    [Test]
    public async Task AHashIsShortOpaqueAndUrlSafeAndDoesNotContainTheName()
    {
        var (_, salt) = new RotatingSaltProvider(Seed, TimeSpan.FromDays(1)).Current(DateTimeOffset.UnixEpoch);

        var hash = PlayerHash.Of("Alice", salt);

        await Assert.That(hash).DoesNotContain("Alice");
        await Assert.That(hash.Length).IsEqualTo(22);                 // 128 bits, base64url, unpadded
        await Assert.That(hash).DoesNotContain("=");
        await Assert.That(hash).DoesNotContain("+");
        await Assert.That(hash).DoesNotContain("/");
    }

    [Test]
    public async Task NamesAreComparedCaseSensitivelyBecauseTheGameDecidesWhatANameIs()
    {
        var (_, salt) = new RotatingSaltProvider(Seed, TimeSpan.FromDays(1)).Current(DateTimeOffset.UnixEpoch);

        // Two different characters on a case-sensitive MUSH. Folding them together would understate
        // the unique-player estimate, and we have no way to know which games fold.
        await Assert.That(PlayerHash.Of("Alice", salt)).IsNotEqualTo(PlayerHash.Of("alice", salt));
    }

    [Test]
    public async Task TheBucketEdgesAreTheOnesTheAggregateIsDefinedOver()
    {
        await Assert.That(PresenceBuckets.Edges).IsEquivalentTo(new[]
        {
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(15),
            TimeSpan.FromHours(1),
            TimeSpan.FromHours(4),
        });

        await Assert.That(PresenceBuckets.IndexFor(TimeSpan.FromSeconds(30))).IsEqualTo(0);
        await Assert.That(PresenceBuckets.IndexFor(TimeSpan.FromMinutes(1))).IsEqualTo(1);
        await Assert.That(PresenceBuckets.IndexFor(TimeSpan.FromMinutes(8))).IsEqualTo(2);
        await Assert.That(PresenceBuckets.IndexFor(TimeSpan.FromMinutes(30))).IsEqualTo(3);
        await Assert.That(PresenceBuckets.IndexFor(TimeSpan.FromHours(2))).IsEqualTo(4);
        await Assert.That(PresenceBuckets.IndexFor(TimeSpan.FromHours(9))).IsEqualTo(5);

        // Not a duration, and never a negative bucket.
        await Assert.That(PresenceBuckets.IndexFor(TimeSpan.FromSeconds(-5))).IsEqualTo(0);
    }

    [Test]
    public async Task AggregatesAreBuiltFromAPerPlayerTable()
    {
        var now = DateTimeOffset.Parse("2026-07-30T09:00:00Z");
        var salt = new RotatingSaltProvider(Seed, TimeSpan.FromDays(1));

        var aggregates = PresenceAggregateBuilder.From(Table(), salt, now)!;

        await Assert.That(aggregates.PlayerHashes.Count).IsEqualTo(3);
        await Assert.That(aggregates.SaltEpoch).IsEqualTo("20260730T000000Z");

        // Idle: 30s → 0, 8m → 2, 2h → 4.
        await Assert.That(aggregates.IdleBucketCounts).IsEquivalentTo(new[] { 1, 0, 1, 0, 1, 0 });

        // Connected: 3h → 4, 2m → 1, 9h → 5.
        await Assert.That(aggregates.ConnectedBucketCounts).IsEquivalentTo(new[] { 0, 1, 0, 0, 1, 1 });
    }

    [Test]
    public async Task ATableWithNoNamesProducesNoAggregatesAtAll()
    {
        // Count-only confidence: there is nothing to hash, and an aggregate of nothing would look like
        // a measured "nobody was idle" on the histogram.
        await Assert.That(PresenceAggregateBuilder.From(new WhoTable([], 12), new RotatingSaltProvider(Seed, TimeSpan.FromDays(1)), DateTimeOffset.UnixEpoch))
            .IsNull();
    }

    [Test]
    public async Task ARowWithNoDurationsContributesAHashAndNoBucket()
    {
        var table = new WhoTable([new WhoRow("Alice", null, null, null)], SummaryCount: 1);

        var aggregates = PresenceAggregateBuilder.From(table, new RotatingSaltProvider(Seed, TimeSpan.FromDays(1)), DateTimeOffset.UnixEpoch)!;

        await Assert.That(aggregates.PlayerHashes.Count).IsEqualTo(1);
        await Assert.That(aggregates.IdleBucketCounts).IsEquivalentTo(new[] { 0, 0, 0, 0, 0, 0 });
        await Assert.That(aggregates.ConnectedBucketCounts).IsEquivalentTo(new[] { 0, 0, 0, 0, 0, 0 });
    }

    [Test]
    public async Task NoPlayerNameSurvivesIntoTheAggregate()
    {
        // The §11 assertion, made against the serialised bytes rather than against a reading of the
        // code: whatever is in here goes to a database column.
        var aggregates = PresenceAggregateBuilder.From(
            Table(), new RotatingSaltProvider(Seed, TimeSpan.FromDays(1)), DateTimeOffset.UnixEpoch)!;

        var json = JsonSerializer.Serialize(aggregates);

        foreach (var name in new[] { "Alice", "Bran", "Cora" })
        {
            await Assert.That(json).DoesNotContain(name);
        }
    }

    /// <summary>A clock a test controls. TUnit has no built-in fake, and this needs three lines.</summary>
    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `error CS0246: The type or namespace name 'RotatingSaltProvider' could not be found`.

- [ ] **Step 3: Write the salt and the hash**

Create `src/MUI.Crawl/Salt.cs`:

```csharp
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace MUI.Crawl;

/// <summary>The salt in force now, and the name of the epoch it belongs to.</summary>
public interface ISaltProvider
{
    (string Epoch, byte[] Salt) Current(DateTimeOffset now);
}

/// <summary>
/// A salt derived per epoch from a long-lived seed (spec §11).
/// </summary>
/// <remarks>
/// <para>
/// Rotation is what makes the unique-player estimate defensible: within one epoch the same player
/// hashes the same, so "how many distinct people were here this week" is answerable; across epochs
/// the hashes are unrelated, so nobody — including us — can follow a player through time.
/// </para>
/// <para>
/// The epoch's salt is <c>HMAC-SHA256(seed, epoch)</c> rather than the seed itself, so the seed never
/// reaches a hash and rotating costs nothing but a clock reading. The rotation period is spec §15.4's
/// open question; it is a constructor argument precisely because the answer is not settled.
/// </para>
/// </remarks>
public sealed class RotatingSaltProvider(byte[] seed, TimeSpan period, TimeProvider? time = null) : ISaltProvider
{
    private readonly byte[] _seed = seed is { Length: > 0 }
        ? seed
        : throw new ArgumentException("A salt seed must not be empty.", nameof(seed));

    private readonly TimeSpan _period = period > TimeSpan.Zero
        ? period
        : throw new ArgumentOutOfRangeException(nameof(period), "A rotation period must be positive.");

    private readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <summary>The epoch containing <paramref name="now"/>, and its salt.</summary>
    public (string Epoch, byte[] Salt) Current(DateTimeOffset now)
    {
        var elapsed = now - DateTimeOffset.UnixEpoch;
        var index = (long)Math.Floor(elapsed / _period);
        var start = DateTimeOffset.UnixEpoch + _period * index;
        var epoch = start.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

        return (epoch, HMACSHA256.HashData(_seed, Encoding.UTF8.GetBytes(epoch)));
    }

    /// <summary>The epoch containing this provider's own idea of now.</summary>
    public (string Epoch, byte[] Salt) Current() => Current(_time.GetUtcNow());
}

/// <summary>
/// A player name reduced to 128 opaque bits (spec §11). <b>The only form of a name that may leave the
/// probe.</b>
/// </summary>
/// <remarks>
/// HMAC-SHA256 rather than a bare hash, so the salt is a key rather than a prefix and a precomputed
/// table over a list of MUSH character names buys nothing. Truncated to 128 bits because collisions
/// at that width are irrelevant to a per-game population estimate and the value is stored per sample.
/// Case-sensitive: <c>Alice</c> and <c>alice</c> may be two characters, and we have no way to know
/// which games fold — folding them would silently understate the estimate.
/// </remarks>
public static class PlayerHash
{
    public static string Of(string name, byte[] salt)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(salt);

        var digest = HMACSHA256.HashData(salt, Encoding.UTF8.GetBytes(name));
        return Base64Url.EncodeToString(digest.AsSpan(0, 16));
    }
}
```

If `System.Buffers.Text.Base64Url` is unavailable on the installed SDK, replace the last line with
`Convert.ToBase64String(digest.AsSpan(0, 16)).TrimEnd('=').Replace('+', '-').Replace('/', '_')` —
same output, one allocation more.

- [ ] **Step 4: Write the aggregate and its buckets**

Create `src/MUI.Crawl/PresenceAggregates.cs`:

```csharp
namespace MUI.Crawl;

/// <summary>
/// What spec §11 permits to leave the probe when the WHO parser reached per-player confidence: hashed
/// identities and two histograms. Stored as JSON in <c>PresenceSample.aggregates</c> (§5.2).
/// </summary>
/// <param name="SaltEpoch">
/// Which salt epoch <paramref name="PlayerHashes"/> belong to. Without it the hashes are unusable:
/// counting distinct hashes across a rotation would count every player twice.
/// </param>
/// <param name="PlayerHashes">One per identifiable player. Never a name, in any encoding, ever.</param>
/// <param name="IdleBucketCounts">Players per idle bucket — six entries, see <see cref="PresenceBuckets"/>.</param>
/// <param name="ConnectedBucketCounts">Players per connected-time bucket — six entries.</param>
public sealed record PresenceAggregates(
    string SaltEpoch,
    IReadOnlyList<string> PlayerHashes,
    IReadOnlyList<int> IdleBucketCounts,
    IReadOnlyList<int> ConnectedBucketCounts);

/// <summary>
/// The histogram buckets, coarse on purpose: fine buckets over a handful of players on a small game
/// are close to publishing the players themselves.
/// </summary>
public static class PresenceBuckets
{
    /// <summary>Five edges, so six buckets: &lt;1m, 1–5m, 5–15m, 15m–1h, 1–4h, ≥4h.</summary>
    public static IReadOnlyList<TimeSpan> Edges { get; } =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(4),
    ];

    /// <summary>The number of buckets — one more than there are edges.</summary>
    public static int Count => Edges.Count + 1;

    /// <summary>The bucket a duration falls in. Anything negative lands in the first one.</summary>
    public static int IndexFor(TimeSpan value)
    {
        for (var i = 0; i < Edges.Count; i++)
        {
            if (value < Edges[i])
            {
                return i;
            }
        }

        return Edges.Count;
    }
}
```

Create `src/MUI.Crawl/PresenceAggregateBuilder.cs`:

```csharp
using MUI.Crawl.Who;

namespace MUI.Crawl;

/// <summary>
/// Turns a parsed <c>WHO</c> table into the only form of it that may be stored (spec §11).
/// </summary>
/// <remarks>
/// This is the boundary. <see cref="WhoTable"/> holds names and exists for the length of one probe;
/// <see cref="PresenceAggregates"/> holds hashes and is what reaches a database. Nothing else in the
/// system takes a <see cref="WhoTable"/>.
/// </remarks>
public static class PresenceAggregateBuilder
{
    /// <summary>
    /// The aggregates for a table with identifiable names, or null when there are none — an aggregate
    /// built from no names would render as a measured "nobody was idle", which is a different claim
    /// from "we could not tell".
    /// </summary>
    public static PresenceAggregates? From(WhoTable table, ISaltProvider salt, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(salt);

        var named = table.Rows.Where(row => row.Name.Length > 0).ToList();
        if (named.Count == 0)
        {
            return null;
        }

        var (epoch, key) = salt.Current(now);
        var hashes = new List<string>(named.Count);
        var idle = new int[PresenceBuckets.Count];
        var connected = new int[PresenceBuckets.Count];

        foreach (var row in named)
        {
            hashes.Add(PlayerHash.Of(row.Name, key));

            // A missing duration contributes no bucket. Defaulting it to zero would invent a player
            // who had just arrived, on every game whose WHO has no idle column at all.
            if (row.Idle is { } idleFor)
            {
                idle[PresenceBuckets.IndexFor(idleFor)]++;
            }

            if (row.OnFor is { } onFor)
            {
                connected[PresenceBuckets.IndexFor(onFor)]++;
            }
        }

        return new PresenceAggregates(epoch, hashes, idle, connected);
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Crawl.Tests </dev/null
```
Expected: PASS — 8 new tests.

- [ ] **Step 6: Commit**

```bash
git add src/MUI.Crawl/Salt.cs src/MUI.Crawl/PresenceAggregates.cs \
        src/MUI.Crawl/PresenceAggregateBuilder.cs tests/MUI.Crawl.Tests/PresenceAggregateTests.cs
git commit -m "feat(crawl): salted rotating hashes, so a name never leaves the probe

Spec §11. Within an epoch a player hashes the same, so 'how many distinct people
were here this week' is answerable; across epochs the hashes are unrelated, so
nobody — us included — can follow a player through time. The epoch salt is
HMAC(seed, epoch) so the seed never reaches a hash, and the assertion that no name
survives is made against the serialised JSON rather than a reading of the code."
```

---

### Task 13: Layer 4 — MSSP, both ways in

Spec §6.4: "Telnet option 70, with the plaintext `MSSP-REQUEST` fallback (tab-separated, delimited by
`MSSP-REPLY-START` / `MSSP-REPLY-END`)."

Task 8 delivered the option-70 half and proved a silent-until-asked server is read. This task adds the
second half and the `MsspVia` distinction, and it parses nothing at all: **option 70's subnegotiation
is TelnetNegotiationCore's** and arrives already projected into an `MsspData` (Task 2), and the
out-of-band `MSSP-REQUEST` reply is read by `MsspPlaintextReply.TryParse` (Task 2). This file decides
*when to ask* and records *how the answer arrived*.

**Files:**
- Create: `src/MUI.Crawl/MsspLayer.cs`
- Test: `tests/MUI.Crawl.Tests/MsspLayerTests.cs`

**Interfaces:**
- Consumes: `ProbeTelnetSession` (Task 8), `BoundedTranscript` (Task 9), `ProbeOptions`,
  `MsspTransport` (Task 3), `MUI.Crawl.Mssp.{MsspData, MsspPlaintextReply}` (Task 2).
- Produces: `MUI.Crawl.MsspLayer` —
  `static Task<(MsspData Data, MsspTransport Via)> ReadAsync(ProbeTelnetSession session, Task<MsspData> negotiated, ProbeOptions options, TimeProvider time, CancellationToken ct)`
  and `const string RequestCommand = "MSSP-REQUEST"`. Task 15 calls `ReadAsync`.

- [ ] **Step 1: Write the failing test**

Create `tests/MUI.Crawl.Tests/MsspLayerTests.cs`:

```csharp
using System.Text;
using MUI.Crawl.Mssp;
using MUI.Crawl.Telnet;
using MUI.Crawl.Tests.Support;
using MUI.Crawl.Transport;

namespace MUI.Crawl.Tests;

/// <summary>
/// Layer 4 (spec §6.4), both routes in and the difference between them.
/// </summary>
public class MsspLayerTests
{
    private static ProbeTelnetSession SessionFor(ScriptedMuServer server, ProbeOptions options) =>
        new(new TcpTransport(new ConnectionOptions { Host = "127.0.0.1", Port = server.Port }), options);

    private static Task<MsspData> Negotiated(ProbeTelnetSession session)
    {
        var report = new TaskCompletionSource<MsspData>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.MsspReceived += (_, data) => report.TrySetResult(data);
        return report.Task;
    }

    [Test]
    public async Task TheTelnetOptionIsPreferredAndIsRecordedAsSuch()
    {
        await using var server = new ScriptedMuServer();
        server.RespondingToDo(TelnetWire.Mssp, TelnetWire.Subnegotiation(TelnetWire.RepresentativeReport()));
        server.Listen();

        var options = new ProbeOptions { MsspTimeout = TimeSpan.FromSeconds(5) };
        await using var session = SessionFor(server, options);
        var negotiated = Negotiated(session);
        await session.ConnectAsync(CancellationToken.None);

        var (data, via) = await MsspLayer.ReadAsync(session, negotiated, options, TimeProvider.System, CancellationToken.None);

        await Assert.That(via).IsEqualTo(MsspTransport.TelnetOption70);
        await Assert.That(data.Name).IsEqualTo("Corvid Nest");
        await Assert.That(data.Players).IsEqualTo(17);
        await Assert.That(data.Ports).IsEquivalentTo(new[] { 23, 4201 });
        await Assert.That(data.CrawlDelay).IsEqualTo(TimeSpan.FromHours(5));

        // And the plaintext command was never needed, so it was never sent.
        await Assert.That(server.Commands).IsEmpty();
    }

    [Test]
    public async Task AServerWithNoTelnetOptionIsAskedInPlainText()
    {
        // Real, and not rare: a game whose MSSP is a login-screen command rather than a telnet option.
        await using var server = new ScriptedMuServer();
        server.RespondingToCommand(
            "MSSP-REQUEST",
            TelnetWire.PlaintextMssp(("NAME", "Plaintext Nest"), ("PLAYERS", "4"), ("CODEBASE", "Evennia")));
        server.Listen();

        var options = new ProbeOptions { MsspTimeout = TimeSpan.FromMilliseconds(400) };
        await using var session = SessionFor(server, options);
        var negotiated = Negotiated(session);
        await session.ConnectAsync(CancellationToken.None);

        var (data, via) = await MsspLayer.ReadAsync(session, negotiated, options, TimeProvider.System, CancellationToken.None);

        await Assert.That(via).IsEqualTo(MsspTransport.PlaintextRequest);
        await Assert.That(data.Name).IsEqualTo("Plaintext Nest");
        await Assert.That(data.Players).IsEqualTo(4);
        await Assert.That(server.Commands).IsEquivalentTo(new[] { "MSSP-REQUEST" });
    }

    [Test]
    public async Task AServerWithNoMsspAtAllIsRecordedAsHavingNoneRatherThanAsAFailure()
    {
        // Most MU* servers. It prints a login banner and never mentions MSSP in either form.
        await using var server = new ScriptedMuServer
        {
            Greeting = Encoding.ASCII.GetBytes("\r\nWelcome to Some MUD.\r\nBy what name? "),
        };
        server.Listen();

        var options = new ProbeOptions { MsspTimeout = TimeSpan.FromMilliseconds(400) };
        await using var session = SessionFor(server, options);
        var negotiated = Negotiated(session);
        await session.ConnectAsync(CancellationToken.None);

        var (data, via) = await MsspLayer.ReadAsync(session, negotiated, options, TimeProvider.System, CancellationToken.None);

        await Assert.That(via).IsEqualTo(MsspTransport.None);
        await Assert.That(data).IsSameReferenceAs(MsspData.Empty);
    }

    [Test]
    public async Task TheFallbackIsNotSentWhenItIsTurnedOff()
    {
        await using var server = new ScriptedMuServer();
        server.RespondingToCommand("MSSP-REQUEST", TelnetWire.PlaintextMssp(("NAME", "Never Asked")));
        server.Listen();

        var options = new ProbeOptions
        {
            MsspTimeout = TimeSpan.FromMilliseconds(300),
            PlaintextMsspFallback = false,
        };

        await using var session = SessionFor(server, options);
        var negotiated = Negotiated(session);
        await session.ConnectAsync(CancellationToken.None);

        var (_, via) = await MsspLayer.ReadAsync(session, negotiated, options, TimeProvider.System, CancellationToken.None);

        await Assert.That(via).IsEqualTo(MsspTransport.None);
        await Assert.That(server.Commands).IsEmpty();
    }

    [Test]
    public async Task AReplyWithNoDelimitersIsNotMsspAndIsNotHalfRead()
    {
        // "Huh?" is the usual answer to MSSP-REQUEST, and it must not become a report.
        await using var server = new ScriptedMuServer();
        server.RespondingToCommand("MSSP-REQUEST", "Huh? Type HELP for a list of commands.\r\n");
        server.Listen();

        var options = new ProbeOptions { MsspTimeout = TimeSpan.FromMilliseconds(400) };
        await using var session = SessionFor(server, options);
        var negotiated = Negotiated(session);
        await session.ConnectAsync(CancellationToken.None);

        var (data, via) = await MsspLayer.ReadAsync(session, negotiated, options, TimeProvider.System, CancellationToken.None);

        await Assert.That(via).IsEqualTo(MsspTransport.None);
        await Assert.That(data.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ATruncatedSubnegotiationYieldsNothingRatherThanHalfAReport()
    {
        await using var server = new ScriptedMuServer { Misbehave = Misbehaviour.TruncatedSubnegotiation };
        server.Listen();

        var options = new ProbeOptions
        {
            MsspTimeout = TimeSpan.FromMilliseconds(400),
            PlaintextMsspFallback = false,
        };

        await using var session = SessionFor(server, options);
        var negotiated = Negotiated(session);
        await session.ConnectAsync(CancellationToken.None);

        var (data, via) = await MsspLayer.ReadAsync(session, negotiated, options, TimeProvider.System, CancellationToken.None);

        await Assert.That(via).IsEqualTo(MsspTransport.None);
        await Assert.That(data.Count).IsEqualTo(0);
    }

}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `error CS0246: The type or namespace name 'MsspLayer' could not be found`.

- [ ] **Step 3: Write the layer**

Create `src/MUI.Crawl/MsspLayer.cs`:

```csharp
using MUI.Crawl.Mssp;
using MUI.Crawl.Telnet;

namespace MUI.Crawl;

/// <summary>
/// Layer 4 (spec §6.4): the telnet option first, then the plaintext <c>MSSP-REQUEST</c> fallback.
/// </summary>
/// <remarks>
/// <para>
/// Neither format is parsed here, and by two different routes. Option 70's subnegotiation is read by
/// TelnetNegotiationCore and reaches this method already projected into an <see cref="MsspData"/>
/// (Task 2); the out-of-band reply is read by <see cref="MsspPlaintextReply.TryParse"/>. This file
/// decides <em>when to ask</em> and <em>what to record about how the answer arrived</em>.
/// </para>
/// <para>
/// The option is preferred because it is negotiation rather than traffic, and because a server that
/// answers it has demonstrated the capability rather than merely having a command by that name. The
/// fallback is one line, sent only when the option produced nothing.
/// </para>
/// </remarks>
public static class MsspLayer
{
    /// <summary>The plaintext request, spelled as the specification spells it.</summary>
    public const string RequestCommand = "MSSP-REQUEST";

    public static async Task<(MsspData Data, MsspTransport Via)> ReadAsync(
        ProbeTelnetSession session,
        Task<MsspData> negotiated,
        ProbeOptions options,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(negotiated);
        ArgumentNullException.ThrowIfNull(options);

        // The option first. It may already have arrived during the banner phase, in which case this
        // returns immediately.
        var deadline = Task.Delay(options.MsspTimeout, time, cancellationToken);
        var finished = await Task.WhenAny(negotiated, deadline).ConfigureAwait(false);
        if (finished == negotiated)
        {
            return (await negotiated.ConfigureAwait(false), MsspTransport.TelnetOption70);
        }

        if (!options.PlaintextMsspFallback)
        {
            return (MsspData.Empty, MsspTransport.None);
        }

        // The fallback: one documented line, at an unauthenticated login screen.
        var transcript = new BoundedTranscript(options.MaxCaptureBytes);
        await session.SendLineAsync(RequestCommand, cancellationToken).ConfigureAwait(false);
        var reply = await BoundedTranscript.CollectAsync(
            session, transcript, options.MsspTimeout, options.MsspTimeout, time, cancellationToken).ConfigureAwait(false);

        // False when there is no REPLY-START/END pair — which is what "Huh?" produces, and what a
        // capped or half-arrived reply produces too. Neither may become a half-read report.
        return MsspPlaintextReply.TryParse(reply, out var parsed)
            ? (parsed, MsspTransport.PlaintextRequest)
            : (MsspData.Empty, MsspTransport.None);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Crawl.Tests </dev/null
```
Expected: PASS — 6 new tests.

If `TheTelnetOptionIsPreferredAndIsRecordedAsSuch` fails on `CrawlDelay`, read `MsspData.CrawlDelay`
(Task 2): the MSSP specification measures `CRAWL DELAY` in **hours**, so `5` is five hours, and Task 2
pins that alongside `-1` meaning "no preference".

- [ ] **Step 5: Commit**

```bash
git add src/MUI.Crawl/MsspLayer.cs tests/MUI.Crawl.Tests/MsspLayerTests.cs
git commit -m "feat(crawl): read MSSP by option 70, then by MSSP-REQUEST

Spec §6.4. Neither format is parsed here — TelnetNegotiationCore reads option 70
and MsspPlaintextReply reads the out-of-band reply — so this file only decides when
to ask and records how the answer arrived.

MsspVia is worth keeping because a server read only through the plaintext
fallback did not negotiate the option at all, which is a fact about the server
worth recording beside the report itself."
```

---

### Task 14: Classifying a failure

Spec §5.3 (`cause ∈ { dns, refused, tls, timeout, handshake_stalled, … }`), §12 ("failures classify
into causes; only a cause change writes an availability transition").

A cause is not cosmetic: `AvailabilityWriter` (Plan 2) opens a new interval **only** when the cause
changes, so a classifier that returned `unknown` for everything would collapse a game's whole outage
history into one undifferentiated row, and one that was unstable would write a transition per probe.

**Files:**
- Create: `src/MUI.Crawl/FailureClassifier.cs`
- Test: `tests/MUI.Crawl.Tests/FailureClassifierTests.cs`

**Interfaces:**
- Consumes: `FailureDetail`, `ProbeFailureCauses` (Task 3).
- Produces: `MUI.Crawl.FailureClassifier.Classify(Exception error) → FailureDetail`. Task 15 calls it;
  Plan 2's `FailureCauseMap` maps its `Cause` strings onto `MUI.Catalog.FailureCause`.

- [ ] **Step 1: Write the failing test**

Create `tests/MUI.Crawl.Tests/FailureClassifierTests.cs`:

```csharp
using System.Net.Sockets;
using System.Security.Authentication;

namespace MUI.Crawl.Tests;

/// <summary>
/// Spec §5.3. A cause is load-bearing: only a change of cause opens a new availability interval, so
/// an unstable classifier writes a transition per probe and a lazy one collapses a game's whole
/// outage history into a single undifferentiated row.
/// </summary>
public class FailureClassifierTests
{
    [Test]
    public async Task ANameThatDoesNotResolveIsDns()
    {
        foreach (var error in new[] { SocketError.HostNotFound, SocketError.NoData, SocketError.TryAgain })
        {
            await Assert.That(FailureClassifier.Classify(new SocketException((int)error)).Cause)
                .IsEqualTo(ProbeFailureCauses.Dns);
        }
    }

    [Test]
    public async Task ARefusedConnectionIsRefused()
    {
        var detail = FailureClassifier.Classify(new SocketException((int)SocketError.ConnectionRefused));

        await Assert.That(detail.Cause).IsEqualTo(ProbeFailureCauses.Refused);
        await Assert.That(detail.Detail).IsEqualTo("ConnectionRefused");
    }

    [Test]
    public async Task AHostThatIsSimplyGoneIsAnUnreachableRatherThanARefusal()
    {
        // A dead machine and a live machine with a closed port are different facts about a game, and
        // flattening them would hide a move behind a shutdown.
        foreach (var error in new[] { SocketError.HostUnreachable, SocketError.NetworkUnreachable })
        {
            await Assert.That(FailureClassifier.Classify(new SocketException((int)error)).Cause)
                .IsEqualTo(ProbeFailureCauses.Unknown);
        }
    }

    [Test]
    public async Task ATimedOutSocketIsATimeout()
    {
        await Assert.That(FailureClassifier.Classify(new SocketException((int)SocketError.TimedOut)).Cause)
            .IsEqualTo(ProbeFailureCauses.Timeout);
        await Assert.That(FailureClassifier.Classify(new TimeoutException()).Cause)
            .IsEqualTo(ProbeFailureCauses.Timeout);
        await Assert.That(FailureClassifier.Classify(new OperationCanceledException()).Cause)
            .IsEqualTo(ProbeFailureCauses.Timeout);
    }

    [Test]
    public async Task ATlsHandshakeThatFailsIsTlsAndNotAGenericIoError()
    {
        // The whole point of probing a TLS port is to find out whether TLS completes (spec §6.1). A
        // certificate failure recorded as "unknown" would make that measurement useless.
        await Assert.That(FailureClassifier.Classify(new AuthenticationException("cert")).Cause)
            .IsEqualTo(ProbeFailureCauses.Tls);
        await Assert.That(FailureClassifier.Classify(new System.Security.Cryptography.CryptographicException()).Cause)
            .IsEqualTo(ProbeFailureCauses.Tls);
    }

    [Test]
    public async Task AWrappedSocketErrorIsClassifiedByWhatItWraps()
    {
        // SslStream and NetworkStream both wrap socket errors in IOException, routinely.
        var wrapped = new IOException("read failed", new SocketException((int)SocketError.ConnectionRefused));

        await Assert.That(FailureClassifier.Classify(wrapped).Cause).IsEqualTo(ProbeFailureCauses.Refused);
    }

    [Test]
    public async Task AnythingElseIsUnknownAndSaysWhatItWas()
    {
        var detail = FailureClassifier.Classify(new InvalidOperationException("something new"));

        await Assert.That(detail.Cause).IsEqualTo(ProbeFailureCauses.Unknown);
        await Assert.That(detail.Detail).IsEqualTo("InvalidOperationException");
    }

    [Test]
    public async Task TheDetailIsOneShortLineWithNoStackTraceAndNoHostName()
    {
        // These strings are stored, rendered and read by people. A stack trace in an availability row
        // is noise, and a host name repeated back into it is redundant with the row's own key.
        var detail = FailureClassifier.Classify(new SocketException((int)SocketError.HostNotFound));

        await Assert.That(detail.Detail).IsEqualTo("HostNotFound");
        await Assert.That(detail.Detail!.Length).IsLessThan(64);
        await Assert.That(detail.Detail).DoesNotContain("\n");
    }

    [Test]
    public async Task HandshakeStalledIsNeverProducedHereBecauseNothingThrowsIt()
    {
        // A server that accepts and then says nothing throws nothing at all. That case belongs to
        // ProbeSession, which knows the connection opened and the deadline passed in silence.
        foreach (var error in new Exception[]
                 {
                     new SocketException((int)SocketError.ConnectionReset),
                     new IOException("stalled"),
                     new TimeoutException(),
                 })
        {
            await Assert.That(FailureClassifier.Classify(error).Cause)
                .IsNotEqualTo(ProbeFailureCauses.HandshakeStalled);
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `error CS0103: The name 'FailureClassifier' does not exist in the current context`.

- [ ] **Step 3: Write the classifier**

Create `src/MUI.Crawl/FailureClassifier.cs`:

```csharp
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;

namespace MUI.Crawl;

/// <summary>
/// An exception reduced to spec §5.3's cause vocabulary.
/// </summary>
/// <remarks>
/// <para>
/// The cause is what decides whether a probe writes an availability transition: only a <em>change</em>
/// of cause opens a new interval, so a hundred consecutive timeouts are one row. A classifier that
/// returned the same answer for everything would collapse a game's outage history into one
/// undifferentiated row; one that flip-flopped would write a transition per probe.
/// </para>
/// <para>
/// It never returns <see cref="ProbeFailureCauses.HandshakeStalled"/>, because nothing throws it: a
/// server that accepts the connection and then says nothing produces no exception at all.
/// <c>ProbeSession</c> decides that case, which is the only place that knows the socket opened.
/// </para>
/// </remarks>
public static class FailureClassifier
{
    public static FailureDetail Classify(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return error switch
        {
            SocketException socket => new FailureDetail(CauseOf(socket.SocketErrorCode), socket.SocketErrorCode.ToString()),

            // SslStream's own failures. Recording these as generic I/O would make the whole TLS
            // measurement (spec §6.1) worthless.
            AuthenticationException => new FailureDetail(ProbeFailureCauses.Tls, nameof(AuthenticationException)),
            CryptographicException => new FailureDetail(ProbeFailureCauses.Tls, nameof(CryptographicException)),

            TimeoutException => new FailureDetail(ProbeFailureCauses.Timeout, nameof(TimeoutException)),
            OperationCanceledException => new FailureDetail(ProbeFailureCauses.Timeout, nameof(OperationCanceledException)),

            // Both stream types wrap socket errors, routinely. Classify by what is underneath.
            IOException { InnerException: { } inner } => Classify(inner),

            _ => new FailureDetail(ProbeFailureCauses.Unknown, error.GetType().Name),
        };
    }

    private static string CauseOf(SocketError error) => error switch
    {
        SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain or SocketError.NoRecovery =>
            ProbeFailureCauses.Dns,

        SocketError.ConnectionRefused => ProbeFailureCauses.Refused,

        SocketError.TimedOut => ProbeFailureCauses.Timeout,

        // Everything else — unreachable networks, resets, aborts — is honestly unknown. Inventing a
        // finer taxonomy here would put words in the network's mouth, and spec §5.3's list is open.
        _ => ProbeFailureCauses.Unknown,
    };
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Crawl.Tests </dev/null
```
Expected: PASS — 9 new tests.

- [ ] **Step 5: Commit**

```bash
git add src/MUI.Crawl/FailureClassifier.cs tests/MUI.Crawl.Tests/FailureClassifierTests.cs
git commit -m "feat(crawl): classify a failure into spec §5.3's causes

Only a change of cause opens a new availability interval, so the classifier's
stability is what makes a hundred consecutive timeouts one row instead of a
hundred. TLS failures are their own cause because the whole point of probing a TLS
port is measuring whether TLS completes. handshake_stalled is deliberately absent:
nothing throws it, and ProbeSession owns that case."
```

---

### Task 15: `ProbeSession` — one connection, four layers, hard-bounded

Spec §6.5 (the seam), §12 (every probe is hard-bounded by timeout **and** `CancellationToken`; because
the crawler runs in-process with the web tier, bounding is a correctness requirement rather than
hygiene), §11 (a probe that cannot state its identity abandons rather than continuing anonymously —
prior art: SharpMUTerm's `TelnetMsspProbe` does exactly this).

**Files:**
- Create: `src/MUI.Crawl/ProbeSession.cs`
- Test: `tests/MUI.Crawl.Tests/ProbeSessionTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 3, 4, 7, 8, 9, 10, 12, 13, 14.
- Produces: `sealed class ProbeSession : IProbe` with the contract's constructor
  `ProbeSession(ProbeOptions options, Func<ConnectionOptions, ITransport>? transportFactory = null, ISaltProvider? salt = null, ILogger? logger = null, TimeProvider? time = null)`.
  Task 16's CLI and Plan 3's `CrawlerService` construct it.

- [ ] **Step 1: Write the failing test**

Create `tests/MUI.Crawl.Tests/ProbeSessionTests.cs`:

```csharp
using System.Diagnostics;
using System.Text;
using MUI.Crawl.Tests.Support;
using MUI.Crawl.Transport;

namespace MUI.Crawl.Tests;

/// <summary>
/// The seam (spec §6.5), end to end against a real socket.
/// </summary>
public class ProbeSessionTests
{
    private static ProbeTarget TargetFor(ScriptedMuServer server) =>
        new() { Host = "127.0.0.1", Port = server.Port };

    private static ProbeOptions Fast => new()
    {
        ConnectTimeout = TimeSpan.FromSeconds(5),
        HardTimeout = TimeSpan.FromSeconds(20),
        BannerQuietPeriod = TimeSpan.FromMilliseconds(200),
        WhoTimeout = TimeSpan.FromSeconds(2),
        MsspTimeout = TimeSpan.FromMilliseconds(500),
    };

    private static ScriptedMuServer RepresentativeGame()
    {
        var server = new ScriptedMuServer
        {
            Greeting = Encoding.UTF8.GetBytes(
                "[1;36mCorvid Nest[0m\r\nA MUSH about crows.\r\nBy what name do you wish to be known? "),
        };

        server.RespondingToDo(TelnetWire.Mssp, TelnetWire.Subnegotiation(TelnetWire.RepresentativeReport()));
        server.RespondingToCommand("WHO", WhoCorpus.PennMushWho + "\r\n");
        return server;
    }

    [Test]
    public async Task AllFourLayersComeBackFromOneConnection()
    {
        await using var server = RepresentativeGame();
        server.Listen();

        var result = await new ProbeSession(Fast).ProbeAsync(TargetFor(server), CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(ProbeOutcome.Answered);

        // Layer 1: observed, not claimed.
        await Assert.That(result.OfferedOptions).Contains("MSSP");

        // Layer 2: ANSI intact.
        await Assert.That(result.Banner).IsNotNull();
        await Assert.That(result.Banner!).Contains("[1;36m");
        await Assert.That(result.Banner!).Contains("A MUSH about crows.");

        // Layer 3: live, and better than MSSP's cached PLAYERS — which says 17 here, and is wrong.
        await Assert.That(result.Who.Confidence).IsEqualTo(WhoConfidence.PerPlayer);
        await Assert.That(result.Who.Count).IsEqualTo(3);

        // Layer 4.
        await Assert.That(result.MsspVia).IsEqualTo(MsspTransport.TelnetOption70);
        await Assert.That(result.Mssp.Name).IsEqualTo("Corvid Nest");

        await Assert.That(result.Host).IsEqualTo("127.0.0.1");
        await Assert.That(result.Port).IsEqualTo(server.Port);
        await Assert.That(result.Elapsed).IsGreaterThan(TimeSpan.Zero);
        await Assert.That(result.Failure).IsNull();
        await Assert.That(result.TlsObserved).IsFalse();
    }

    [Test]
    public async Task TheProbeSendsNothingButTheThreeDocumentedLines()
    {
        // The politeness assertion, against what the server actually received. SharpMUTerm's crawler
        // sends nothing at all; ours sends WHO because spec §6.3 requires the live count. It sends
        // nothing else — no login, no password, no exploration.
        await using var server = RepresentativeGame();
        server.Listen();

        await new ProbeSession(Fast).ProbeAsync(TargetFor(server), CancellationToken.None);

        await Assert.That(server.Commands).IsEquivalentTo(new[] { "WHO" });
    }

    [Test]
    public async Task DoingIsTriedOnlyWhenWhoWasUnreadable()
    {
        await using var server = new ScriptedMuServer();
        server.RespondingToCommand("WHO", "Huh?!?\r\n");
        server.RespondingToCommand("DOING", WhoCorpus.PennMushDoingCustomised + "\r\n");
        server.Listen();

        var result = await new ProbeSession(Fast).ProbeAsync(TargetFor(server), CancellationToken.None);

        await Assert.That(result.Who.Count).IsEqualTo(2);
        await Assert.That(server.Commands).IsEquivalentTo(new[] { "WHO", "DOING" });
    }

    [Test]
    public async Task AProbeThatIsToldNotToSendWhoSendsNothingAtAll()
    {
        await using var server = RepresentativeGame();
        server.Listen();

        var result = await new ProbeSession(Fast with { SendWho = false, PlaintextMsspFallback = false })
            .ProbeAsync(TargetFor(server), CancellationToken.None);

        await Assert.That(server.Commands).IsEmpty();
        await Assert.That(result.Mssp.Name).IsEqualTo("Corvid Nest");

        // Nobody asked, so nothing failed. Half of the pair this and the next test hold apart: a
        // writer reading these two results must be able to tell them from each other.
        await Assert.That(result.Who).IsEqualTo(WhoReading.NotAttempted);
        await Assert.That(result.Who.WasAttempted).IsFalse();
    }

    [Test]
    public async Task AnUnreadableWhoLeavesTheCountAbsentRatherThanZero()
    {
        await using var server = new ScriptedMuServer
        {
            Greeting = Encoding.ASCII.GetBytes("Welcome.\r\n"),
        };
        server.RespondingToCommand("WHO", "Huh?!?\r\n");
        server.RespondingToCommand("DOING", "Huh?!?\r\n");
        server.Listen();

        var result = await new ProbeSession(Fast).ProbeAsync(TargetFor(server), CancellationToken.None);

        // Answered, because we got in — but with no count, which spec §5.4 renders as a hatched cell
        // rather than as an empty one or a zero.
        await Assert.That(result.Outcome).IsEqualTo(ProbeOutcome.Answered);
        await Assert.That(result.Who.Count).IsNull();
        await Assert.That(result.Aggregates).IsNull();

        // The other half of the pair: we asked, twice, and could not read either answer. This is the
        // state that writes a presence sample with a null count and who_unparseable, and it must not
        // be equal to the reading a probe that never asked carries.
        await Assert.That(result.Who).IsEqualTo(WhoReading.Unreadable);
        await Assert.That(result.Who.WasAttempted).IsTrue();
        await Assert.That(result.Who).IsNotEqualTo(WhoReading.NotAttempted);
    }

    [Test]
    public async Task AggregatesAreProducedForAPerPlayerReadingAndCarryNoNames()
    {
        await using var server = RepresentativeGame();
        server.Listen();

        var result = await new ProbeSession(Fast).ProbeAsync(TargetFor(server), CancellationToken.None);

        await Assert.That(result.Aggregates).IsNotNull();
        await Assert.That(result.Aggregates!.PlayerHashes.Count).IsEqualTo(3);

        var serialised = System.Text.Json.JsonSerializer.Serialize(result.Aggregates);
        foreach (var name in new[] { "Alice", "Bran", "Cora" })
        {
            await Assert.That(serialised).DoesNotContain(name);
        }
    }

    [Test]
    public async Task AServerThatAcceptsAndSaysNothingIsHandshakeStalled()
    {
        await using var server = new ScriptedMuServer { Misbehave = Misbehaviour.SilentAfterAccept };
        server.Listen();

        var options = Fast with
        {
            HardTimeout = TimeSpan.FromSeconds(2),
            MsspTimeout = TimeSpan.FromMilliseconds(300),
            WhoTimeout = TimeSpan.FromMilliseconds(300),
        };

        var stopwatch = Stopwatch.StartNew();
        var result = await new ProbeSession(options).ProbeAsync(TargetFor(server), CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(ProbeOutcome.Failed);
        await Assert.That(result.Failure!.Cause).IsEqualTo(ProbeFailureCauses.HandshakeStalled);
        await Assert.That(stopwatch.Elapsed).IsLessThan(TimeSpan.FromSeconds(10));
    }

    [Test]
    public async Task AServerThatNeverStopsTalkingIsBoundedByTheHardTimeout()
    {
        await using var server = new ScriptedMuServer
        {
            Misbehave = Misbehaviour.EnormousBanner,
            EnormousBannerBytes = 32 * 1024 * 1024,
        };
        server.Listen();

        var options = Fast with
        {
            HardTimeout = TimeSpan.FromSeconds(3),
            BannerQuietPeriod = TimeSpan.FromSeconds(30),
            MaxCaptureBytes = 4096,
        };

        var stopwatch = Stopwatch.StartNew();
        var result = await new ProbeSession(options).ProbeAsync(TargetFor(server), CancellationToken.None);

        // Bounding is a correctness requirement: this process also serves web requests (spec §12).
        await Assert.That(stopwatch.Elapsed).IsLessThan(TimeSpan.FromSeconds(15));
        await Assert.That(Encoding.UTF8.GetByteCount(result.Banner ?? string.Empty)).IsLessThanOrEqualTo(4096);
    }

    [Test]
    public async Task ARefusedConnectionIsAFailedResultAndNotAnException()
    {
        await using var server = new ScriptedMuServer();  // bound, never listening
        var target = new ProbeTarget { Host = "127.0.0.1", Port = server.Port };
        await server.DisposeAsync();

        var result = await new ProbeSession(Fast).ProbeAsync(target, CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(ProbeOutcome.Failed);
        await Assert.That(result.Failure!.Cause).IsEqualTo(ProbeFailureCauses.Refused);
        await Assert.That(result.ObservedAt).IsNotEqualTo(default(DateTimeOffset));
    }

    [Test]
    public async Task AnAlreadyCancelledTokenStopsTheProbeRatherThanReportingAFailure()
    {
        // The scheduler shutting down is not a fact about the game, and must never be written as one.
        await using var server = RepresentativeGame();
        server.Listen();

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => new ProbeSession(Fast).ProbeAsync(TargetFor(server), cts.Token));
    }

    [Test]
    public async Task AProbeThatCannotStateItsIdentityAbandons()
    {
        // Spec §11 makes self-identification a requirement, so a telnet layer that could not be told
        // who we are ends the visit rather than continuing anonymously. Prior art: SharpMUTerm's
        // TelnetMsspProbe does exactly this.
        await using var server = RepresentativeGame();
        server.Listen();

        var result = await new ProbeSession(Fast with { TerminalTypes = [] })
            .ProbeAsync(TargetFor(server), CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(ProbeOutcome.Failed);
        await Assert.That(result.Failure!.Detail).IsEqualTo("identity-not-stated");
    }

    [Test]
    public async Task TheTransportFactoryIsHonouredSoTheSessionCanBeDrivenWithoutASocket()
    {
        // Plan 3's scheduler injects one; so does anything that wants a probe without a network.
        await using var server = RepresentativeGame();
        server.Listen();

        var built = 0;
        var probe = new ProbeSession(Fast, connection =>
        {
            built++;
            return new TcpTransport(connection);
        });

        var result = await probe.ProbeAsync(TargetFor(server), CancellationToken.None);

        await Assert.That(built).IsEqualTo(1);
        await Assert.That(result.Outcome).IsEqualTo(ProbeOutcome.Answered);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `error CS0246: The type or namespace name 'ProbeSession' could not be found`.

- [ ] **Step 3: Write the session**

Create `src/MUI.Crawl/ProbeSession.cs`:

```csharp
using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MUI.Crawl.Mssp;
using MUI.Crawl.Telnet;
using MUI.Crawl.Transport;
using MUI.Crawl.Who;

namespace MUI.Crawl;

/// <summary>
/// One visit to one address, yielding one immutable <see cref="ProbeResult"/> (spec §6.5).
/// </summary>
/// <remarks>
/// <para>
/// The four layers are not a fallback chain: layers 1 and 2 always happen, layer 3 usually does, and
/// layer 4 happens wholly or not at all. A game that answers none of the optional ones still yields
/// measured capability data.
/// </para>
/// <para>
/// <b>Everything is bounded, and that is correctness rather than hygiene (spec §12).</b> The crawler
/// runs in-process with the web tier, so a wedged probe starves request threads. Every phase has its
/// own deadline and all of them sit inside <see cref="ProbeOptions.HardTimeout"/>, which sits inside
/// the caller's <see cref="CancellationToken"/>.
/// </para>
/// <para>
/// <b>What it sends:</b> <c>WHO</c>, then <c>DOING</c> only if <c>WHO</c> was unreadable, then
/// <c>MSSP-REQUEST</c> only if telnet option 70 produced nothing. Nothing else, ever. It does not log
/// in, has no name to log in with, and stops the moment it has what it came for.
/// </para>
/// </remarks>
public sealed class ProbeSession : IProbe
{
    private readonly ProbeOptions _options;
    private readonly Func<ConnectionOptions, ITransport> _transportFactory;
    private readonly ISaltProvider _salt;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;

    public ProbeSession(
        ProbeOptions options,
        Func<ConnectionOptions, ITransport>? transportFactory = null,
        ISaltProvider? salt = null,
        ILogger? logger = null,
        TimeProvider? time = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _transportFactory = transportFactory ?? (connection => new TcpTransport(connection));
        _logger = logger ?? NullLogger.Instance;
        _time = time ?? TimeProvider.System;

        // A random seed by default, so a probe used standalone still hashes names rather than
        // storing them. Production passes a persisted seed — hashes only aggregate across probes if
        // the seed survives a restart (spec §11).
        _salt = salt ?? new RotatingSaltProvider(RandomNumberGenerator.GetBytes(32), TimeSpan.FromDays(7), _time);
    }

    public async Task<ProbeResult> ProbeAsync(ProbeTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        var observedAt = _time.GetUtcNow();
        var stopwatch = Stopwatch.StartNew();

        ProbeResult Result(
            ProbeOutcome outcome,
            FailureDetail? failure = null,
            IReadOnlySet<string>? offered = null,
            string? banner = null,
            WhoReading? who = null,
            MsspData? mssp = null,
            MsspTransport via = MsspTransport.None,
            PresenceAggregates? aggregates = null) => new()
        {
            Host = target.Host,
            Port = target.Port,
            ObservedAt = observedAt,
            Outcome = outcome,
            OfferedOptions = offered ?? new HashSet<string>(StringComparer.Ordinal),
            Banner = banner,
            Who = who ?? WhoReading.NotAttempted,
            Mssp = mssp ?? MsspData.Empty,
            MsspVia = via,
            TlsObserved = target.UseTls && outcome == ProbeOutcome.Answered,
            Aggregates = aggregates,
            Failure = failure,
            Elapsed = stopwatch.Elapsed,
        };

        var connection = new ConnectionOptions
        {
            Host = target.Host,
            Port = target.Port,
            UseTls = target.UseTls,

            // A hobbyist MU* server's self-signed certificate is not a reason to record the game as
            // unreachable: what is being measured is whether TLS completes at all (spec §6.1).
            AllowInvalidCertificates = true,
            ConnectTimeout = _options.ConnectTimeout,
        };

        using var deadline = new CancellationTokenSource(_options.HardTimeout, _time);
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);

        var recorder = new NegotiationRecorder(_transportFactory(connection));
        await using var session = new ProbeTelnetSession(recorder, _options, _logger);

        // Subscribed before connecting: a server that volunteers MSSP does it during the handshake,
        // long before layer 4 gets around to asking.
        var negotiated = new TaskCompletionSource<MsspData>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.MsspReceived += (_, data) => negotiated.TrySetResult(data);

        try
        {
            await session.ConnectAsync(bounded.Token).ConfigureAwait(false);

            if (!session.IdentityStated)
            {
                // Identifying honestly is a requirement, not a nicety (spec §11). If the telnet layer
                // could not be told who we are, the visit ends rather than continuing anonymously.
                _logger.LogError("Could not state the crawler's identity to {Host}; abandoning the probe.", target.Host);
                return Result(ProbeOutcome.Failed, new FailureDetail(ProbeFailureCauses.Unknown, "identity-not-stated"));
            }

            // Layer 2 — everything before we say anything (spec §6.2).
            var bannerSink = new BoundedTranscript(_options.MaxCaptureBytes);
            var banner = await BoundedTranscript.CollectAsync(
                session, bannerSink, _options.BannerQuietPeriod, _options.HardTimeout, _time, bounded.Token)
                .ConfigureAwait(false);

            // Layer 3 — WHO, then DOING only if WHO was unreadable (spec §6.3).
            //
            // The starting value is NotAttempted, and it survives when SendWho is off: nobody asked,
            // so nothing failed. That is the state an owner override saying "use MSSP PLAYERS"
            // produces, and Plan 2's PresenceWriter reads it to decide between writing no presence
            // sample at all and writing §5.4's middle row — a sample with a null count and
            // unmeasurable_reason = who_unparseable. Overwriting it with Unreadable here would claim
            // a failure nobody incurred.
            var who = WhoReading.NotAttempted;
            WhoTable? table = null;
            if (_options.SendWho)
            {
                (who, table) = await AskAsync(session, "WHO", bounded.Token).ConfigureAwait(false);
                if (who.Confidence == WhoConfidence.Unknown)
                {
                    (who, table) = await AskAsync(session, "DOING", bounded.Token).ConfigureAwait(false);
                }
            }

            // Layer 4 (spec §6.4).
            var (mssp, via) = await MsspLayer
                .ReadAsync(session, negotiated.Task, _options, _time, bounded.Token)
                .ConfigureAwait(false);

            var offered = recorder.Offered;

            // Nothing whatsoever came back: not a refusal, not a DNS failure, and not an answer.
            // Spec §5.3 has a cause for exactly this, and it is the one case no exception describes.
            if (banner.Length == 0 && offered.Count == 0 && via == MsspTransport.None && !who.HasCount)
            {
                return Result(
                    ProbeOutcome.Failed,
                    new FailureDetail(ProbeFailureCauses.HandshakeStalled, "accepted, then silent"));
            }

            var aggregates = table is not null && who.Confidence == WhoConfidence.PerPlayer
                ? PresenceAggregateBuilder.From(table, _salt, observedAt)
                : null;

            return Result(
                ProbeOutcome.Answered,
                offered: offered,
                banner: banner.Length == 0 ? null : banner,
                who: who,
                mssp: mssp,
                via: via,
                aggregates: aggregates);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller stopping is not a fact about the game and must never be written as one.
            throw;
        }
        catch (Exception ex)
        {
            var failure = ex is OperationCanceledException
                ? new FailureDetail(ProbeFailureCauses.Timeout, "hard timeout")
                : FailureClassifier.Classify(ex);

            _logger.LogDebug(ex, "Probe of {Host}:{Port} failed as {Cause}.", target.Host, target.Port, failure.Cause);
            return Result(ProbeOutcome.Failed, failure);
        }
        finally
        {
            // Leave immediately, whatever happened. Nothing is gained by holding a stranger's socket
            // open past the moment we have what we came for.
            try
            {
                await session.CloseAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Closing the connection to {Host} failed.", target.Host);
            }
        }
    }

    /// <summary>
    /// Sends one line and reads what comes back, bounded by <see cref="ProbeOptions.WhoTimeout"/> and
    /// ended by the same quiet period the connect screen uses.
    /// </summary>
    private async Task<(WhoReading Reading, WhoTable? Table)> AskAsync(
        ProbeTelnetSession session, string command, CancellationToken cancellationToken)
    {
        var sink = new BoundedTranscript(_options.MaxCaptureBytes);
        await session.SendLineAsync(command, cancellationToken).ConfigureAwait(false);

        var transcript = await BoundedTranscript.CollectAsync(
            session, sink, _options.BannerQuietPeriod, _options.WhoTimeout, _time, cancellationToken)
            .ConfigureAwait(false);

        return (WhoParser.Parse(transcript), WhoParser.ParseTable(transcript));
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Crawl.Tests </dev/null
```
Expected: PASS — 12 new tests.

- [ ] **Step 5: Commit**

```bash
git add src/MUI.Crawl/ProbeSession.cs tests/MUI.Crawl.Tests/ProbeSessionTests.cs
git commit -m "feat(crawl): one connection, four layers, one immutable ProbeResult

Spec §6.5. Every phase is bounded and all of them sit inside the hard timeout,
which sits inside the caller's token — the crawler shares a process with the web
tier, so bounding is correctness (§12). A server that accepts and then says
nothing is handshake_stalled, the one case no exception describes. A probe that
cannot state its identity abandons rather than continuing anonymously (§11)."
```

---

### Task 16: `ProbeResultJson` and the `mui-probe` console

Spec §6.5 (`ProbeResult` fixtures "captured from real games" are the primary test surface), §13.
This is the plan's runnable deliverable: a person can point it at a real MUSH and read the answer.

**The fixture JSON shape is fixed here and Plan 2 depends on it.** It is `System.Text.Json` with
camelCase names, enums as strings, indented, and one custom converter for `MsspData` (which has no
public constructor and is an `IReadOnlyDictionary<string, IReadOnlyList<string>>`):

```json
{
  "host": "corvid.example.org",
  "port": 4201,
  "observedAt": "2026-07-30T09:00:00+00:00",
  "outcome": "Answered",
  "offeredOptions": ["GMCP", "MSSP", "TTYPE"],
  "banner": "[1;36mCorvid Nest[0m\r\nBy what name? ",
  "who": { "confidence": "PerPlayer", "count": 3, "identifiablePlayers": 3 },
  "mssp": { "NAME": ["Corvid Nest"], "PORT": ["23", "4201"] },
  "msspVia": "TelnetOption70",
  "tlsObserved": false,
  "aggregates": {
    "saltEpoch": "20260730T000000Z",
    "playerHashes": ["p3Zk…"],
    "idleBucketCounts": [1, 0, 1, 0, 1, 0],
    "connectedBucketCounts": [0, 1, 0, 0, 1, 1]
  },
  "failure": null,
  "elapsed": "00:00:01.8420000"
}
```

**Files:**
- Create: `src/MUI.Crawl/ProbeResultJson.cs`
- Create: `src/MUI.Probe.Cli/MUI.Probe.Cli.csproj`
- Create: `src/MUI.Probe.Cli/Program.cs`
- Modify: `MUIndex.slnx`
- Test: `tests/MUI.Crawl.Tests/ProbeResultJsonTests.cs`

**Interfaces:**
- Consumes: `ProbeResult` and everything it holds (Tasks 3, 10, 12); `ProbeSession` (Task 15).
- Produces: `MUI.Crawl.ProbeResultJson` — `static JsonSerializerOptions Options { get; }`,
  `static string Serialize(ProbeResult result)`, `static ProbeResult Deserialize(string json)`;
  and the `mui-probe` executable. **Plan 2 loads fixtures with `ProbeResultJson.Deserialize`.**

- [ ] **Step 1: Write the failing test**

Create `tests/MUI.Crawl.Tests/ProbeResultJsonTests.cs`:

```csharp
using System.Text.Json;
using MUI.Crawl.Mssp;

namespace MUI.Crawl.Tests;

/// <summary>
/// The fixture format. Plan 2 reads these files, so the shape is a contract and not an implementation
/// detail: a rename here is a break there.
/// </summary>
public class ProbeResultJsonTests
{
    private static ProbeResult Sample() => new()
    {
        Host = "corvid.example.org",
        Port = 4201,
        ObservedAt = DateTimeOffset.Parse("2026-07-30T09:00:00Z"),
        Outcome = ProbeOutcome.Answered,
        OfferedOptions = new HashSet<string>(["GMCP", "MSSP", "TTYPE"], StringComparer.Ordinal),
        Banner = "[1;36mCorvid Nest[0m\r\nBy what name? ",
        Who = new WhoReading(WhoConfidence.PerPlayer, 3, 3),
        Mssp = MsspData.From([
            new KeyValuePair<string, IReadOnlyList<string>>("NAME", ["Corvid Nest"]),
            new KeyValuePair<string, IReadOnlyList<string>>("PORT", ["23", "4201"]),
        ]),
        MsspVia = MsspTransport.TelnetOption70,
        TlsObserved = false,
        Aggregates = new PresenceAggregates("20260730T000000Z", ["p3ZkAAAAAAAAAAAAAAAAAA"], [1, 0, 1, 0, 1, 0], [0, 1, 0, 0, 1, 1]),
        Failure = null,
        Elapsed = TimeSpan.FromMilliseconds(1842),
    };

    [Test]
    public async Task TheDocumentUsesTheNamesPlanTwoWillRead()
    {
        using var document = JsonDocument.Parse(ProbeResultJson.Serialize(Sample()));
        var root = document.RootElement;

        await Assert.That(root.GetProperty("host").GetString()).IsEqualTo("corvid.example.org");
        await Assert.That(root.GetProperty("port").GetInt32()).IsEqualTo(4201);
        await Assert.That(root.GetProperty("outcome").GetString()).IsEqualTo("Answered");
        await Assert.That(root.GetProperty("msspVia").GetString()).IsEqualTo("TelnetOption70");
        await Assert.That(root.GetProperty("who").GetProperty("confidence").GetString()).IsEqualTo("PerPlayer");
        await Assert.That(root.GetProperty("who").GetProperty("count").GetInt32()).IsEqualTo(3);
        await Assert.That(root.GetProperty("elapsed").GetString()).IsEqualTo("00:00:01.8420000");
        await Assert.That(root.GetProperty("failure").ValueKind).IsEqualTo(JsonValueKind.Null);

        // MSSP is an object of arrays, because a variable may legitimately have several values.
        var mssp = root.GetProperty("mssp");
        await Assert.That(mssp.GetProperty("NAME")[0].GetString()).IsEqualTo("Corvid Nest");
        await Assert.That(mssp.GetProperty("PORT").GetArrayLength()).IsEqualTo(2);
        await Assert.That(mssp.GetProperty("PORT")[1].GetString()).IsEqualTo("4201");

        await Assert.That(root.GetProperty("aggregates").GetProperty("saltEpoch").GetString())
            .IsEqualTo("20260730T000000Z");
    }

    [Test]
    public async Task ARoundTripPreservesEveryLayer()
    {
        var restored = ProbeResultJson.Deserialize(ProbeResultJson.Serialize(Sample()));
        var original = Sample();

        await Assert.That(restored.Host).IsEqualTo(original.Host);
        await Assert.That(restored.Port).IsEqualTo(original.Port);
        await Assert.That(restored.ObservedAt).IsEqualTo(original.ObservedAt);
        await Assert.That(restored.Outcome).IsEqualTo(original.Outcome);
        await Assert.That(restored.OfferedOptions).IsEquivalentTo(original.OfferedOptions);
        await Assert.That(restored.Banner).IsEqualTo(original.Banner);
        await Assert.That(restored.Who).IsEqualTo(original.Who);
        await Assert.That(restored.MsspVia).IsEqualTo(original.MsspVia);
        await Assert.That(restored.TlsObserved).IsEqualTo(original.TlsObserved);
        await Assert.That(restored.Elapsed).IsEqualTo(original.Elapsed);
        await Assert.That(restored.Mssp.Name).IsEqualTo("Corvid Nest");
        await Assert.That(restored.Mssp.Ports).IsEquivalentTo(new[] { 23, 4201 });
        await Assert.That(restored.Aggregates!.PlayerHashes).IsEquivalentTo(original.Aggregates!.PlayerHashes);
        await Assert.That(restored.Aggregates.IdleBucketCounts).IsEquivalentTo(original.Aggregates.IdleBucketCounts);
    }

    [Test]
    public async Task AFailedResultRoundTripsWithItsCause()
    {
        var failed = new ProbeResult
        {
            Host = "gone.example.org",
            Port = 4201,
            ObservedAt = DateTimeOffset.UnixEpoch,
            Outcome = ProbeOutcome.Failed,
            Failure = new FailureDetail(ProbeFailureCauses.Dns, "HostNotFound"),
            Elapsed = TimeSpan.FromSeconds(2),
        };

        var restored = ProbeResultJson.Deserialize(ProbeResultJson.Serialize(failed));

        await Assert.That(restored.Outcome).IsEqualTo(ProbeOutcome.Failed);
        await Assert.That(restored.Failure!.Cause).IsEqualTo("dns");
        await Assert.That(restored.Failure.Detail).IsEqualTo("HostNotFound");
        await Assert.That(restored.Mssp).IsSameReferenceAs(MsspData.Empty);

        // A failed probe never reached layer 3, so what it round-trips is "nobody asked" — not the
        // reading that says we asked and could not read the answer.
        await Assert.That(restored.Who).IsEqualTo(WhoReading.NotAttempted);
        await Assert.That(restored.Who.WasAttempted).IsFalse();
    }

    [Test]
    public async Task NoPlayerNameCanReachTheFile()
    {
        // The §11 assertion at the last boundary before disk. Whatever a WhoTable held, this is what
        // is written, and it must be free of names in every field including the banner.
        var result = Sample();
        var json = ProbeResultJson.Serialize(result);

        foreach (var name in new[] { "Alice", "Bran", "Cora" })
        {
            await Assert.That(json).DoesNotContain(name);
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet build MUIndex.slnx -c Release`
Expected: FAIL — `error CS0103: The name 'ProbeResultJson' does not exist in the current context`.

- [ ] **Step 3: Write the serialiser**

Create `src/MUI.Crawl/ProbeResultJson.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using MUI.Crawl.Mssp;

namespace MUI.Crawl;

/// <summary>
/// The on-disk form of a <see cref="ProbeResult"/> — spec §6.5's "fixtures captured from real games
/// exercise every downstream behaviour without a network", and spec §13's corpus.
/// </summary>
/// <remarks>
/// <b>This shape is a contract.</b> Plan 2's writers and Plan 3's tests load these files, so a
/// property rename here breaks them. camelCase names, enums as strings, indented so a captured
/// fixture is reviewable in a diff.
/// </remarks>
public static class ProbeResultJson
{
    public static JsonSerializerOptions Options { get; } = Build();

    public static string Serialize(ProbeResult result) => JsonSerializer.Serialize(result, Options);

    public static ProbeResult Deserialize(string json) =>
        JsonSerializer.Deserialize<ProbeResult>(json, Options)
        ?? throw new JsonException("The document was null rather than a probe result.");

    private static JsonSerializerOptions Build()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            WriteIndented = true,
        };

        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new MsspDataConverter());
        options.Converters.Add(new ReadOnlyStringSetConverter());
        return options;
    }

    /// <summary>
    /// <see cref="MsspData"/> as an object of arrays. It has no public constructor and no setters —
    /// <see cref="MsspData.From"/> is the way in — and a variable may legitimately carry several
    /// values, so an object of strings would silently drop <c>PORT</c>'s and <c>REFERRAL</c>'s tails.
    /// </summary>
    private sealed class MsspDataConverter : JsonConverter<MsspData>
    {
        public override MsspData Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var raw = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(ref reader, options);
            if (raw is null || raw.Count == 0)
            {
                return MsspData.Empty;
            }

            return MsspData.From(raw.Select(pair =>
                new KeyValuePair<string, IReadOnlyList<string>>(pair.Key, pair.Value)));
        }

        public override void Write(Utf8JsonWriter writer, MsspData value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            foreach (var variable in value.Keys)
            {
                // The variable names are the server's own and are written verbatim: the naming policy
                // must not camel-case "CRAWL DELAY" into something no MSSP reader recognises.
                writer.WritePropertyName(variable);
                writer.WriteStartArray();
                foreach (var item in value[variable])
                {
                    writer.WriteStringValue(item);
                }

                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }
    }

    /// <summary>
    /// <see cref="IReadOnlySet{T}"/> of strings. Declared rather than relied upon: the framework's
    /// support for constructing read-only interfaces has moved between releases, and a fixture loader
    /// that throws <see cref="NotSupportedException"/> on some SDKs is not a loader.
    /// </summary>
    private sealed class ReadOnlyStringSetConverter : JsonConverter<IReadOnlySet<string>>
    {
        public override IReadOnlySet<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            new HashSet<string>(
                JsonSerializer.Deserialize<List<string>>(ref reader, options) ?? [],
                StringComparer.Ordinal);

        public override void Write(Utf8JsonWriter writer, IReadOnlySet<string> value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();

            // Sorted, so a captured fixture does not churn in diffs because a hash set reordered.
            foreach (var item in value.OrderBy(x => x, StringComparer.Ordinal))
            {
                writer.WriteStringValue(item);
            }

            writer.WriteEndArray();
        }
    }
}
```

- [ ] **Step 4: Run the serialiser tests**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Crawl.Tests </dev/null
```
Expected: PASS — 4 new tests.

- [ ] **Step 5: Write the console project**

Create `src/MUI.Probe.Cli/MUI.Probe.Cli.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <RootNamespace>MUI.Probe.Cli</RootNamespace>
    <AssemblyName>mui-probe</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\MUI.Crawl\MUI.Crawl.csproj" />
  </ItemGroup>

</Project>
```

Add it to `MUIndex.slnx`, inside the `/src/` folder, in alphabetical order (after `MUI.Discovery`):

```xml
    <Project Path="src/MUI.Probe.Cli/MUI.Probe.Cli.csproj" />
```

Create `src/MUI.Probe.Cli/Program.cs`:

```csharp
using System.Globalization;
using MUI.Crawl;

namespace MUI.Probe.Cli;

/// <summary>
/// One probe, from a terminal. The plan's runnable deliverable and the tool that captures fixtures.
/// </summary>
internal static class Program
{
    private const string Usage = """
        mui-probe — probe one MU* server and print what it answered.

          mui-probe <host> <port> [options]

        Options:
          --tls               connect with TLS
          --json              print the ProbeResult as JSON
          --timeout <s>       hard timeout in seconds (default 45)
          --no-who            send nothing at all; negotiation only
          --capture <path>    write the ProbeResult as a JSON fixture
          -h, --help          this text

        It sends WHO (and DOING only if WHO was unreadable) and MSSP-REQUEST (only if
        telnet option 70 answered nothing), at the login screen, identifying itself in
        TTYPE. It never logs in and never sends anything else.
        """;

    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            Console.WriteLine(Usage);
            return args.Length == 0 ? 2 : 0;
        }

        if (args.Length < 2 || !int.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out var port))
        {
            Console.Error.WriteLine("A host and a port are required. Try --help.");
            return 2;
        }

        var target = new ProbeTarget { Host = args[0], Port = port, UseTls = args.Contains("--tls") };
        var options = new ProbeOptions { SendWho = !args.Contains("--no-who") };

        if (IndexOfValue(args, "--timeout") is { } timeoutText &&
            double.TryParse(timeoutText, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
        {
            options = options with { HardTimeout = TimeSpan.FromSeconds(seconds) };
        }

        // Ctrl+C stops the probe rather than the process mid-write.
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancellation.Cancel();
        };

        ProbeResult result;
        try
        {
            result = await new ProbeSession(options).ProbeAsync(target, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Cancelled.");
            return 130;
        }

        if (IndexOfValue(args, "--capture") is { } path)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(path, ProbeResultJson.Serialize(result), cancellation.Token);
            Console.Error.WriteLine($"Wrote {path}");
        }

        Console.WriteLine(args.Contains("--json") ? ProbeResultJson.Serialize(result) : Describe(result));
        return result.Outcome == ProbeOutcome.Answered ? 0 : 1;
    }

    private static string? IndexOfValue(string[] args, string flag)
    {
        var at = Array.IndexOf(args, flag);
        return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
    }

    /// <summary>
    /// Human-readable, and honest about what was measured versus what was claimed — the same
    /// distinction the site is built on (spec §3.1).
    /// </summary>
    private static string Describe(ProbeResult result)
    {
        var lines = new List<string>
        {
            $"{result.Host}:{result.Port}  {result.Outcome.ToString().ToLowerInvariant()} in {result.Elapsed.TotalSeconds:F2}s",
        };

        if (result.Failure is { } failure)
        {
            lines.Add($"  cause     {failure.Cause}{(failure.Detail is null ? string.Empty : $" ({failure.Detail})")}");
            return string.Join(Environment.NewLine, lines);
        }

        lines.Add($"  offered   {(result.OfferedOptions.Count == 0 ? "(nothing)" : string.Join(", ", result.OfferedOptions.OrderBy(x => x, StringComparer.Ordinal)))}");
        lines.Add($"  tls       {(result.TlsObserved ? "completed" : "no")}");
        lines.Add($"  banner    {(result.Banner is null ? "(none)" : $"{result.Banner.Length} characters")}");

        lines.Add(result.Who.Confidence switch
        {
            WhoConfidence.PerPlayer => $"  who       {result.Who.Count} players, {result.Who.IdentifiablePlayers} identifiable (measured)",
            WhoConfidence.Count => $"  who       {result.Who.Count} players (measured)",
            WhoConfidence.NotAttempted => "  who       not asked",
            _ => "  who       unreadable — no count, which is not the same as zero",
        });

        lines.Add(result.MsspVia switch
        {
            MsspTransport.None => "  mssp      (none)",
            var via => $"  mssp      {result.Mssp.Count} variables via {via}"
                       + (result.Mssp.Name is { } name ? $"; NAME=\"{name}\"" : string.Empty)
                       + (result.Mssp.Players is { } players ? $", PLAYERS={players} (declared)" : string.Empty),
        });

        if (result.Aggregates is { } aggregates)
        {
            lines.Add($"  aggregate {aggregates.PlayerHashes.Count} salted hashes, epoch {aggregates.SaltEpoch}");
        }

        return string.Join(Environment.NewLine, lines);
    }
}
```

- [ ] **Step 6: Build and run it against a real game**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project src/MUI.Probe.Cli -- --help
```
Expected: the usage text.

Then, if you have network access, point it at a real MUSH — `mush.pennmush.org 4201`,
`m.mudportal.com 4000`, or a SharpMUSH you control (spec §13: "SharpMUSH is the first-party
fixture"). Read the output and check it is honest: an unreadable WHO must print
`unreadable — no count, which is not the same as zero`, never `0 players`.

- [ ] **Step 7: Commit**

```bash
git add src/MUI.Crawl/ProbeResultJson.cs src/MUI.Probe.Cli MUIndex.slnx \
        tests/MUI.Crawl.Tests/ProbeResultJsonTests.cs
git commit -m "feat(probe-cli): mui-probe, and the fixture format Plan 2 reads

ProbeResultJson is a contract, not an implementation detail: camelCase, enums as
strings, MSSP as an object of arrays because PORT and REFERRAL are arrays, and the
offered-options set sorted so a captured fixture does not churn in diffs."
```

---

### Task 17: Captured fixtures, and the loader later plans reuse

Spec §13: "`ProbeResult` fixtures captured from real games — PennMUSH, TinyMUX, RhostMUSH, Evennia,
and a DIKU-family game for contrast — exercise every downstream behaviour without a socket."

**Where the five fixtures come from.** Capturing from live third-party games is the eventual source
and cannot be a CI dependency: the games move, go dark, and change their WHO. So they are generated
by running the *real* `ProbeSession` against `ScriptedMuServer` scripted with each codebase's shape,
and checked in. The pipeline is identical to a real capture — same session, same socket, same
serialiser — so a fixture captured from a live game later drops into the same directory with no code
change. `mui-probe --capture` (Task 16) is that path.

**Files:**
- Create: `tests/MUI.Crawl.Tests/Support/FixtureLibrary.cs`
- Create: `tests/MUI.Crawl.Tests/Fixtures/{pennmush,tinymux,rhostmush,evennia,diku}.json` (generated)
- Modify: `tests/MUI.Crawl.Tests/MUI.Crawl.Tests.csproj`
- Test: `tests/MUI.Crawl.Tests/FixtureTests.cs`

**Interfaces:**
- Consumes: `ProbeSession` (Task 15), `ProbeResultJson` (Task 16), `ScriptedMuServer`, `WhoCorpus`.
- Produces: `MUI.Crawl.Tests.Support.FixtureLibrary` — `static IReadOnlyList<string> Names { get; }`,
  `static string PathOf(string name)`, `static ProbeResult Load(string name)`,
  `static Task WriteAllAsync()`; and the five JSON files.
  **Plan 2 and Plan 3 read these files** — see the note at the end of this task for how.

- [ ] **Step 1: Make the fixtures reachable at runtime**

In `tests/MUI.Crawl.Tests/MUI.Crawl.Tests.csproj`, add:

```xml
  <ItemGroup>
    <!-- Captured ProbeResults (spec §13). Copied so a test reads them from the output directory. -->
    <Content Include="Fixtures\*.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
```

- [ ] **Step 2: Write the failing test**

Create `tests/MUI.Crawl.Tests/FixtureTests.cs`:

```csharp
using MUI.Crawl.Tests.Support;

namespace MUI.Crawl.Tests;

/// <summary>
/// Spec §13's captured fixtures. These files are the test surface for every downstream plan, so what
/// is asserted here is what those plans are entitled to assume.
/// </summary>
public class FixtureTests
{
    [Test]
    public async Task EveryFixtureLoads()
    {
        foreach (var name in FixtureLibrary.Names)
        {
            var result = FixtureLibrary.Load(name);

            await Assert.That(result.Host).IsNotNull().Because(name);
            await Assert.That(result.Port).IsGreaterThan(0).Because(name);
            await Assert.That(result.ObservedAt).IsNotEqualTo(default(DateTimeOffset)).Because(name);
        }
    }

    [Test]
    public async Task ThePennMushFixtureCarriesAllFourLayers()
    {
        var result = FixtureLibrary.Load("pennmush");

        await Assert.That(result.Outcome).IsEqualTo(ProbeOutcome.Answered);
        await Assert.That(result.OfferedOptions).Contains("MSSP");
        await Assert.That(result.Banner).IsNotNull();
        await Assert.That(result.Who.Confidence).IsEqualTo(WhoConfidence.PerPlayer);
        await Assert.That(result.Who.Count).IsEqualTo(3);
        await Assert.That(result.Mssp.Codebase).IsEqualTo("PennMUSH 1.8.8");
        await Assert.That(result.MsspVia).IsEqualTo(MsspTransport.TelnetOption70);
        await Assert.That(result.Aggregates!.PlayerHashes.Count).IsEqualTo(3);
    }

    [Test]
    public async Task TheEvenniaFixtureWasReadThroughThePlaintextFallback()
    {
        var result = FixtureLibrary.Load("evennia");

        await Assert.That(result.MsspVia).IsEqualTo(MsspTransport.PlaintextRequest);
        await Assert.That(result.Who.Count).IsEqualTo(2);
    }

    [Test]
    public async Task TheDikuFixtureIsTheContrastCaseAndHasNoCount()
    {
        // Its whole job downstream: a probe that succeeded and could not count, which spec §5.4 says
        // must write a presence sample with a null count rather than nothing and rather than zero.
        var result = FixtureLibrary.Load("diku");

        await Assert.That(result.Outcome).IsEqualTo(ProbeOutcome.Answered);
        await Assert.That(result.Who.Confidence).IsEqualTo(WhoConfidence.Unknown);
        await Assert.That(result.Who.Count).IsNull();

        // Asked and unreadable, not unasked. This distinction is the entire reason Plan 2's
        // PresenceWriter can write §5.4's middle row from this fixture, so it is pinned on disk.
        await Assert.That(result.Who.WasAttempted).IsTrue();
        await Assert.That(result.MsspVia).IsEqualTo(MsspTransport.None);
        await Assert.That(result.Aggregates).IsNull();
    }

    [Test]
    public async Task TheTinyMuxAndRhostFixturesCarryCountsAndCodebases()
    {
        var mux = FixtureLibrary.Load("tinymux");
        await Assert.That(mux.Who.Count).IsEqualTo(3);
        await Assert.That(mux.Mssp.Codebase).IsEqualTo("TinyMUX 2.13");

        // RhostMUSH ships no MSSP implementation at all (spec §3.1), so its fixture must not have one
        // — it is the case that proves the site does not require MSSP to list a game.
        var rhost = FixtureLibrary.Load("rhostmush");
        await Assert.That(rhost.Who.Count).IsEqualTo(2);
        await Assert.That(rhost.MsspVia).IsEqualTo(MsspTransport.None);
        await Assert.That(rhost.Banner).IsNotNull();
    }

    [Test]
    public async Task NoFixtureContainsAPlayerName()
    {
        // The §11 rule checked against the bytes on disk, which is where it finally matters.
        foreach (var name in FixtureLibrary.Names)
        {
            var text = await File.ReadAllTextAsync(FixtureLibrary.PathOf(name));

            foreach (var player in new[] { "Alice", "Bran", "Cora" })
            {
                await Assert.That(text).DoesNotContain(player).Because($"{name} leaked {player}");
            }
        }
    }
}
```

- [ ] **Step 3: Write the library and the generator**

Create `tests/MUI.Crawl.Tests/Support/FixtureLibrary.cs`:

```csharp
using System.Text;
using MUI.Crawl.Transport;

namespace MUI.Crawl.Tests.Support;

/// <summary>
/// The captured <see cref="ProbeResult"/> fixtures (spec §13), and the generator that produces them.
/// </summary>
/// <remarks>
/// <para>
/// They are generated by running the real <see cref="ProbeSession"/> against a
/// <see cref="ScriptedMuServer"/> scripted with each codebase's shape — same session, same socket,
/// same serialiser as a live capture, so a fixture taken from a real game later drops into this
/// directory unchanged. Live third-party games cannot be a CI dependency: they move, go dark, and
/// rewrite their WHO.
/// </para>
/// <para>
/// Regenerate with:
/// <code>MUI_WRITE_FIXTURES=1 dotnet run -c Release --project tests/MUI.Crawl.Tests &lt;/dev/null</code>
/// and commit the diff.
/// </para>
/// </remarks>
public static class FixtureLibrary
{
    public static IReadOnlyList<string> Names { get; } = ["pennmush", "tinymux", "rhostmush", "evennia", "diku"];

    /// <summary>Where a fixture lives at runtime — beside the test assembly, copied by the csproj.</summary>
    public static string PathOf(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", $"{name}.json");

    /// <summary>Where a fixture lives in the repository, for the generator to write to.</summary>
    public static string SourcePathOf(string name) =>
        Path.Combine(RepositoryFixtureDirectory(), $"{name}.json");

    public static ProbeResult Load(string name)
    {
        var path = PathOf(name);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"The fixture '{name}' is missing. Regenerate with "
                + "MUI_WRITE_FIXTURES=1 dotnet run -c Release --project tests/MUI.Crawl.Tests </dev/null",
                path);
        }

        return ProbeResultJson.Deserialize(File.ReadAllText(path));
    }

    /// <summary>Probes every scripted game and writes its result into the repository.</summary>
    public static async Task WriteAllAsync()
    {
        Directory.CreateDirectory(RepositoryFixtureDirectory());

        foreach (var name in Names)
        {
            await using var server = Scripted(name);
            server.Listen();

            var result = await new ProbeSession(CaptureOptions)
                .ProbeAsync(new ProbeTarget { Host = "127.0.0.1", Port = server.Port }, CancellationToken.None);

            // The host and the elapsed time are the two fields a loopback capture gets wrong. Naming
            // the game keeps the fixture readable, and a fixed duration keeps it diff-stable.
            var captured = result with
            {
                Host = $"{name}.example.org",
                Port = 4201,
                ObservedAt = DateTimeOffset.Parse("2026-07-30T09:00:00Z"),
                Elapsed = TimeSpan.FromMilliseconds(1500),
            };

            await File.WriteAllTextAsync(SourcePathOf(name), ProbeResultJson.Serialize(captured));
        }
    }

    private static ProbeOptions CaptureOptions => new()
    {
        ConnectTimeout = TimeSpan.FromSeconds(5),
        HardTimeout = TimeSpan.FromSeconds(20),
        BannerQuietPeriod = TimeSpan.FromMilliseconds(200),
        WhoTimeout = TimeSpan.FromSeconds(2),
        MsspTimeout = TimeSpan.FromMilliseconds(500),
    };

    /// <summary>Each codebase as it actually behaves on the wire (spec §3.1's evidence table).</summary>
    private static ScriptedMuServer Scripted(string name)
    {
        switch (name)
        {
            case "pennmush":
            {
                var server = new ScriptedMuServer
                {
                    Greeting = Encoding.UTF8.GetBytes(
                        "[1;36m  Corvid Nest[0m\r\n  A MUSH about crows.\r\n"
                        + "  Use 'connect <name> <password>' to join us.\r\n\r\nBy what name do you wish to be known? "),
                };

                server.RespondingToDo(TelnetWire.Mssp, TelnetWire.Subnegotiation(
                    ("NAME", ["Corvid Nest"]), ("PLAYERS", ["17"]), ("CODEBASE", ["PennMUSH 1.8.8"]),
                    ("FAMILY", ["TinyMUSH"]), ("PORT", ["4201"]), ("CREATED", ["2003"]),
                    ("WEBSITE", ["https://corvid.example.org/"]), ("CRAWL DELAY", ["5"])));
                server.RespondingToCommand("WHO", WhoCorpus.PennMushWho + "\r\n");
                return server;
            }

            case "tinymux":
            {
                var server = new ScriptedMuServer
                {
                    Greeting = Encoding.UTF8.GetBytes("Welcome to Gravel Court (TinyMUX 2.13)\r\n\r\nlogin: "),
                };

                server.RespondingToDo(TelnetWire.Mssp, TelnetWire.Subnegotiation(
                    ("NAME", ["Gravel Court"]), ("PLAYERS", ["3"]), ("CODEBASE", ["TinyMUX 2.13"]),
                    ("FAMILY", ["TinyMUSH"]), ("PORT", ["2860"]), ("GENRE", ["Social"])));
                server.RespondingToCommand("WHO", WhoCorpus.TinyMuxWho + "\r\n");
                return server;
            }

            case "rhostmush":
            {
                // Rhost ships no MSSP implementation at all (spec §3.1), so this game has none in
                // either form — the case that proves a game does not need MSSP to be listed.
                var server = new ScriptedMuServer
                {
                    Greeting = Encoding.UTF8.GetBytes("=== Ashfall (RhostMUSH 4.0) ===\r\n\r\nEnter your name: "),
                };

                server.RespondingToCommand("WHO", WhoCorpus.RhostMushWho + "\r\n");
                return server;
            }

            case "evennia":
            {
                // Evennia implements MSSP, and this one answers only the plaintext request.
                var server = new ScriptedMuServer
                {
                    Greeting = Encoding.UTF8.GetBytes("Welcome to Tidewrack!\r\nEnter 'help' for help.\r\n\r\n> "),
                };

                server.RespondingToCommand("WHO", WhoCorpus.EvenniaWho + "\r\n");
                server.RespondingToCommand("MSSP-REQUEST", TelnetWire.PlaintextMssp(
                    ("NAME", "Tidewrack"), ("PLAYERS", "2"), ("CODEBASE", "Evennia 4.2"),
                    ("FAMILY", "Custom"), ("PORT", "4000"), ("GENRE", "Fantasy")));
                return server;
            }

            case "diku":
            {
                // The contrast case: DIKU-family games generally do not answer WHO before login.
                var server = new ScriptedMuServer
                {
                    Greeting = Encoding.UTF8.GetBytes(
                        "\r\n  The Iron Marches\r\n\r\nBy what name do you wish to be known? "),
                };

                server.RespondingToCommand("WHO", WhoCorpus.DikuHuh + "\r\n");
                server.RespondingToCommand("DOING", WhoCorpus.DikuHuh + "\r\n");
                return server;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(name), name, "No script for that fixture.");
        }
    }

    /// <summary>
    /// Walks up from the test assembly to the repository's own <c>Fixtures</c> directory. The
    /// generator writes to source; the loader reads the copy beside the assembly.
    /// </summary>
    private static string RepositoryFixtureDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MUI.Crawl.Tests.csproj")))
        {
            directory = directory.Parent;
        }

        return directory is null
            ? throw new DirectoryNotFoundException("Could not find the test project from the output directory.")
            : Path.Combine(directory.FullName, "Fixtures");
    }
}
```

- [ ] **Step 4: Write the generator entry point**

Add to `tests/MUI.Crawl.Tests/FixtureTests.cs`, inside the class:

```csharp
    [Test]
    public async Task RegenerateTheFixtures()
    {
        // Off unless asked. Generating on every run would rewrite five files on every test pass and
        // make the working tree dirty for no reason.
        if (Environment.GetEnvironmentVariable("MUI_WRITE_FIXTURES") != "1")
        {
            return;
        }

        await FixtureLibrary.WriteAllAsync();

        foreach (var name in FixtureLibrary.Names)
        {
            await Assert.That(File.Exists(FixtureLibrary.SourcePathOf(name))).IsTrue().Because(name);
        }
    }
```

- [ ] **Step 5: Generate the fixtures**

Run:
```bash
dotnet build MUIndex.slnx -c Release
MUI_WRITE_FIXTURES=1 dotnet run -c Release --no-build --project tests/MUI.Crawl.Tests </dev/null
```
Expected: `tests/MUI.Crawl.Tests/Fixtures/` now holds five JSON files. Open `pennmush.json` and read
it: it must have `"outcome": "Answered"`, an `offeredOptions` array containing `"MSSP"`, a `banner`
with escaped ANSI, `"who"` with `"confidence": "PerPlayer"` and `"count": 3`, an `"mssp"` object of
arrays, and an `"aggregates"` object whose `playerHashes` are opaque.

The first run's `FixtureTests` will fail (the files did not exist when the run started). That is
expected — the generator and the assertions cannot both be satisfied in one pass.

- [ ] **Step 6: Run the tests to verify they pass**

Run:
```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Crawl.Tests </dev/null
```
Expected: PASS — every suite green, including the six fixture tests.

If `NoFixtureContainsAPlayerName` fails, a name reached the file through the **banner**, not through
the aggregates: check that no scripted greeting in `FixtureLibrary.Scripted` mentions a player, and
fix the script rather than the assertion.

- [ ] **Step 7: Note for Plan 2, in the fixture directory**

Create `tests/MUI.Crawl.Tests/Fixtures/README.md` (the outer fence below is four backticks, because
the file's own content contains fenced blocks):

````markdown
# Captured probe results

Spec §13's fixture corpus: one `ProbeResult` per codebase family, serialised by
`MUI.Crawl.ProbeResultJson`. They exercise every downstream behaviour with no network.

| File | What it is for |
|---|---|
| `pennmush.json` | All four layers: handshake, banner, per-player WHO, MSSP via telnet option 70 |
| `tinymux.json` | The same shape from another codebase |
| `rhostmush.json` | **No MSSP in either form** — Rhost ships none (spec §3.1). A game listed on WHO and banner alone |
| `evennia.json` | MSSP read through the plaintext `MSSP-REQUEST` fallback |
| `diku.json` | Answered, and **no count** — spec §5.4's *probed, unmeasurable* case |

Regenerate:

```bash
MUI_WRITE_FIXTURES=1 dotnet run -c Release --project tests/MUI.Crawl.Tests </dev/null
```

**Later plans:** load these with `MUI.Crawl.ProbeResultJson.Deserialize`, and reach them from another
test project by linking them in that project's `.csproj`:

```xml
<ItemGroup>
  <Content Include="..\..\tests\MUI.Crawl.Tests\Fixtures\*.json"
           Link="Fixtures\%(Filename)%(Extension)" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```
````

- [ ] **Step 8: Commit**

```bash
git add tests/MUI.Crawl.Tests/MUI.Crawl.Tests.csproj tests/MUI.Crawl.Tests/Support/FixtureLibrary.cs \
        tests/MUI.Crawl.Tests/FixtureTests.cs tests/MUI.Crawl.Tests/Fixtures
git commit -m "test(crawl): captured ProbeResult fixtures, and the loader later plans reuse

Spec §13's corpus — PennMUSH, TinyMUX, RhostMUSH, Evennia and a DIKU-family game
for contrast. Generated by running the real ProbeSession against a scripted server
per codebase, so the pipeline is identical to a live capture and a fixture taken
from a real game drops in unchanged. Live third-party games cannot be a CI
dependency: they move, go dark and rewrite their WHO.

Rhost has no MSSP in either form, because Rhost ships none; the DIKU one answered
and could not be counted, which is spec §5.4's probed-but-unmeasurable case and
the single most important fixture for Plan 2's PresenceWriter."
```

---

## Done means

```bash
dotnet build MUIndex.slnx -c Release
dotnet run -c Release --no-build --project tests/MUI.Catalog.Tests </dev/null
dotnet run -c Release --no-build --project tests/MUI.Crawl.Tests </dev/null
dotnet run -c Release --no-build --project tests/MUI.Discovery.Tests </dev/null
dotnet run -c Release --no-build --project tests/MUI.Web.Tests </dev/null
dotnet run -c Release --no-build --project src/MUI.Probe.Cli -- --help
```

All green, no warnings (warnings are errors), and `mui-probe` runs. No new test project was added, so
`.github/workflows/ci.yml` needs no new step — the Crawl suite already has one.

Open the PR. Nothing external gates it: every dependency this plan needs is on nuget.org, so a red CI
here is ours.

---

## Self-review

**1. Spec coverage.**

| Spec section | Task |
|---|---|
| §6 — one connection per target, four layers | 4, 15 |
| §6.1 — handshake as measured capability | 7 (recorder), 8 (negotiation), 15 (into `OfferedOptions`) |
| §6.2 — connect screen, ANSI intact | 9, 15 |
| §6.3 — structural WHO/DOING, never fabricate | 10, 11, 15 |
| §6.3 — an owner may assert "use MSSP `PLAYERS`" | 3 (`NotAttempted`), 15 (`SendWho` off leaves it) |
| §6.4 — MSSP: the model, option 70, and the plaintext fallback | 1, 2 (the model), 8 (option 70), 13 (fallback, `MsspVia`) |
| §6.5 — one immutable `ProbeResult`; fixtures as the test surface | 3, 15, 16, 17 |
| §5.3 — failure causes | 14, 15 |
| §5.4 — the three states an hour can be in | 3 (`NotAttempted` ≠ `Unknown`), 10 (`Unknown` ≠ 0), 15, 17 (`diku.json`) |
| §7.2 — a referral is a candidate, not a fact | 1 (`MsspHost.IsCrawlable`), 2 (`Referrals` lists and marks) |
| §11 — self-identification with an info URL | 8, 15 (abandon if unstated) |
| §11 — names never persisted; salted rotating hashes | 12, 15, 16, 17 |
| §12 — hard timeout **and** `CancellationToken`; bounded | 9, 13, 15 |
| §13 — scripted fake server incl. misbehaviour | 5, 6 |
| §13 — property tests for the WHO parser | 11 |
| §13 — `ProbeResult` fixtures per codebase | 17 |
| Runnable deliverable (`mui-probe`) | 16 |

Not covered, deliberately, and each belongs to a later plan: `CRAWL DELAY` enforcement and per-host
serialisation (§7.7 → Plan 3), referral *following* (§7.1 → Plan 3; this plan produces the gate it
follows through, and Plan 3 must not re-derive the judgement), the advisory lock (§12 → Plan 3),
banner *fingerprinting* (§7.3's `BannerFingerprint` → Plan 3; this plan captures the banner, which is
its input), and everything in §5.1–5.2's storage (Plan 2).

**2. Placeholder scan.** No "TBD", no "add error handling", no "similar to Task N", no "write tests
for the above". Every code step carries the code. Two steps are deliberately conditional rather than
vague — Task 6 Step 3 and Task 11 Step 3 — and both name the exact symptom, the exact lever, and which
of the two sides to change.

**3. Type consistency.** Checked across tasks:
`MsspHost.{Create, TryParse, Scope, IsCrawlable, ToReferralString}` (1) → 2's `Referrals`, and Plan 3's
`ReferralGraphWriter`.
`MsspData.{Empty, From, Default, Flag, Integer, Name, Players, Port, Ports, CrawlDelay, Referrals}`
(2) → 3's seam, 8's `MsspReceived`, 13, 15, 16, 17, and Plans 2 and 3.
`MsspPlaintextReply.TryParse(string, out MsspData)` (2) → 13 only.
`WhoReading.{NotAttempted, Unreadable, WasAttempted, HasCount}` and `WhoConfidence`'s four members
(3) → 10, 11, 15, 16, 17, and Plan 2's `PresenceWriter`.
`ITransport`/`ConnectionOptions`/`TcpTransport` (4) → used by 5, 7, 8, 13, 15.
`NegotiationRecorder.Offered` (7) → `ProbeResult.OfferedOptions` (3) in 15.
`ProbeTelnetSession.{IdentityStated, TextReceived, MsspReceived, Closed, SendLineAsync, CloseAsync}`
(8) → used by 9's `CollectAsync`, 13's `ReadAsync`, 15.
`BoundedTranscript.{Append, Text, IsFull, Truncated, CollectAsync}` (9) → 13, 15.
`WhoParser.{Parse, ParseTable}`, `WhoTable(Rows, SummaryCount)`, `WhoRow(Name, OnFor, Idle, Doing)`
(10) → 11, 12, 15, 17.
`ISaltProvider.Current(DateTimeOffset)`, `PresenceAggregateBuilder.From(WhoTable, ISaltProvider, DateTimeOffset)`
(12) → 15.
`MsspLayer.ReadAsync(session, negotiated, options, time, ct)` (13) → 15.
`FailureClassifier.Classify(Exception) → FailureDetail` (14) → 15.
`ProbeResultJson.{Serialize, Deserialize, Options}` (16) → 17, and Plan 2.
`ProbeOptions.MaxCaptureBytes` is used in 9, 13, 15 and declared in 3.
`ScriptedMuServer.{Listen, RespondingToDo, RespondingToCommand, Commands, Received, ReceivedText, WaitForReceivedAsync, WaitForCommandAsync}`
and `Misbehaviour` (5) → 6, 8, 9, 13, 15, 17. `TelnetWire.{Mssp, Ttype, Iac, Do, Will, Sb, Se, Offer, Ask, Subnegotiation, PlaintextMssp, RepresentativeReport}`
(5) → 7, 8, 13, 15, 17. `WhoCorpus` (10) → 11, 15, 17.

`ProbeTarget.UseTls` is produced in 3 and consumed in 15 — consistent. `MsspTransport` values
`None`/`TelnetOption70`/`PlaintextRequest` are spelled identically in 3, 13, 15, 16, 17.

**4. Deviations and gaps, restated after the reversal.**

| What | Standing |
|---|---|
| **The `SharpMU.Mssp` package** | **Reversed.** Nothing was published and the source repository is archived, so this plan is blocked on nothing and shares no code with SharpMUTerm. Tasks 1 and 2 are the replacement: MUIndex's own MSSP domain in `MUI.Crawl.Mssp`, with the addendum's names kept so no later plan changes more than a `using`. The old "CI stays red until the package ships" note is gone with it. |
| **`TelnetNegotiationCore` 2.6.0 → 2.7.0** | Raised in Task 2. 2.7.0 is what the addendum's §2a surface — `MSSPVariableCollection`, `MSSPVariables.Canonicalize`, `MSSPValue.TryParseFlag` — was verified against by reflection, and it is what Task 2 projects rather than re-implements. |
| **`MsspSubnegotiationParser`** | Deleted from the plan entirely. Option 70 is TNC's, and there is no subnegotiation parser in this repository. Its plaintext half survives as `MsspPlaintextReply` (Task 2), which is the only MSSP parsing MUIndex owns because `MSSP-REQUEST` is not a telnet option. |
| **MNES `CLIENT_NAME`** | Not reachable: TNC registers no NEW-ENVIRON plugin and exposes no client-side environment send, so §11's self-identification is served by TTYPE/MTTS alone, with the info URL riding the client name. A client-side MNES sender is a good upstream PR. |
| **`WhoReading`'s fourth state** | Fixed **at source** in Task 3 rather than worked around downstream. `WhoReading.Unread` collapsed "we never asked" into "we asked and could not read the answer", so Plan 2's `PresenceWriter` could not tell spec §5.4's own named bug case from silence and had to infer intent from `MsspVia` — a guess about a different layer. Plan 2's deviation row for it is struck. |
| **A crawlable *name* is not a resolved name** | **A gap, and it is here rather than fixed here.** `MsspHost.Scope` is `Unresolved` for every DNS name, and `IsCrawlable` says yes, because refusing names would refuse every real referral. So a referral of `internal.example.org` that resolves to `10.0.0.5` passes this gate and would be dialled. Closing it needs a check *after* resolution and *before* the socket is used, which is Plan 3's scheduler or `TcpTransport`, not a type that only ever sees a string. Recorded on `MsspHost` because that is where a reader will look for it. |

**5. The two tests that are the point of this revision.** If a reviewer reads only two assertions, read
`WhoReadingTests.NotAttemptedAndUnreadableAreNotTheSameReading` (Task 3) and
`MsspHostTests.TheCloudMetadataAddressIsNotCrawlable` (Task 1). The first is a correctness fix one
level below where the bug was reported; the second is the whole reason `MsspHost` is not merely a
tidy way to hold a host and a port.
