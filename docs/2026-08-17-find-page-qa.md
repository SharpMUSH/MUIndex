# Find a game — mockup parity QA

An audit of `/find` on `design/measured-once` against the **third** handoff bundle
(`Mu-index accessibility audit (2).zip` → `design_handoff_find_page/`), which is scoped to this one
page and supersedes the second bundle where they overlap. Same shape as
[`2026-08-17-mockup-parity-qa.md`](2026-08-17-mockup-parity-qa.md): numbered deltas with what the
mock says, what ours does as measured, and a severity; then the conflicts.

**Parts one to four were written before the work; part five records what shipped.** Everything in
part two is now built except where part five says otherwise.

## Method

- Bundle extracted to `/tmp/mui_handoff3/`; a copy of `MU Index Find Page.dc.html` with
  `showAnnotations` defaulted to `false` served over `python3 -m http.server` and rendered in
  Playwright's Chromium at 1440 / 1024 / 768 / 430.
- Ours at `http://127.0.0.1:5199/find`, both themes, same four widths, on the fixture — so every
  page carries the demo banner, which CLAUDE.md requires and which is not a delta. Fixture counts
  are small (6 games); the *shape* of each control is what is compared, never the numbers.
- Every value below is a `getComputedStyle` / `getBoundingClientRect` reading.
- `?plain=1` read as text; querystring behaviour probed with `curl`.

---

# Part one — what the third bundle changed

`_ds/` (tokens, `styles.css`, manifest, adherence config, fonts) and `support.js` are **byte-identical**
to the second bundle — `md5sum` matches on all ten files. The design system did not move. What
changed is the README and one new screen.

| # | Change | Detail |
|---|---|---|
| Δ1 | **Find gets a screen and a review of its own** | Bundle 2 mocked home, games and game detail and gave Find four rows in a copy table. Bundle 3 is a single-page handoff with eight findings (F1–F8), a working prototype, a layout spec and a verification list. Everything below Δ7 is new material, not a restatement. |
| Δ2 | **Both intro paragraphs are now deleted, not rewritten** | Bundle 2 rewrote the lede to `Six optional questions. Every number is a count of games we measured, not an estimate.` and offered `Answers filter; they never rank.` "or cut". Bundle 3 deletes both outright — the live count is meant to make the point by demonstration. **We shipped bundle 2's version of both strings**, so both are now superseded. |
| Δ3 | **`uncounted` is reversed** | Bundle 2: `quiet is fine — we can reach it, and could not count` → **`uncounted` *(match the listing)***. Bundle 3: → **`reachable, but we can't count`**. A direct reversal of the string we shipped, and of a decision recorded in `FindAGame.razor`'s own comment. See §X6. |
| Δ4 | **The TLS gloss grew, and TLS folds into the protocol list** | Bundle 2: `TLS — handshake completed by us`. Bundle 3: `TLS — encrypted, handshake completed by us`, as row three of one uniform list, not a separate control. |
| Δ5 | **The acronym fix hardens** | Bundle 2 offered "add three words each **or** move the set behind an advanced disclosure". Bundle 3 removes the choice: every option reads *name · three-word gloss · count*, one shape per question. |
| Δ6 | **`?plain=1` is still described as absent from `/find`** | True of production in Aug 2026, stale for this branch — we shipped it. The requirement it carries is new though: plain must carry the `unknown` options and the count. |
| Δ7 | **New in bundle 3, with no antecedent in bundle 2** | The intersection count and the sticky panel (F1); the binding-answer "loosen" button (F2); `unknown` as a selectable option carrying its count (F3); grouping declared free text and badging the question `◆ derived` (F4); the long-tail collapse (F5); inverting the dark-games default (F7); the submit-hands-off-to-the-listing model and the shared vocabulary it needs (F8); single-select on every question including the client one; the layout spec — 24/14/24 rhythm, the `<legend>` padding trap, `overflow: clip` vs `hidden`, 310px panel, 40px option chips. |

---

# Part two — the deltas

Severity: **blocker** = the page makes a claim it cannot support, or an affordance is unreachable ·
**major** = the redesign's premise or the site's own visual language is missing ·
**minor** = a measurable gap that reads as polish.

## L — Layout and chrome

The Find page was not touched by either of the two earlier passes. It is the only page left on
**user-agent default form styling**.

| # | Item | Mock | Ours (measured) | Sev |
|---|---|---|---|---|
| L1 | **Fieldsets are unstyled** | `border: 0`, questions separated by a 1px `--border-soft` hairline | `border: 2px groove rgb(107,107,107)` in dark, `rgb(239,239,239)` in light — the UA default, drawn full-bleed at 1394px | major |
| L2 | Fieldset padding | `0 24px 24px` | `4.9px 10.5px 8.75px`, plus a UA `margin: 0 2px` | major |
| L3 | **Vertical rhythm** | 24 rule→heading · 14 heading→options · 24 options→rule (measured on the mock as 24 / 14 / 25-incl-hairline) | **0 / 6.9 / 12.8** | major |
| L4 | Question separation | 1px hairline, full column width | fieldsets are flush: measured gap between consecutive fieldsets is **0** at every width; the groove border is the only separator | major |
| L5 | Legend typography | 16px / 600, `--text` | **14px / 400**, `padding: 0 2px` — a legend, never promoted to a heading | major |
| L6 | Legend padding | `24px 0 14px` on the legend itself (the README's warning: padding on the fieldset does not move it) | `0 2px` | major |
| L7 | No card, no two-column shell | one card, `grid-template-columns: minmax(0, 1fr) minmax(0, 310px)`, `gap: 0`, `align-items: stretch` | single flow column, `form.wizard` is `display: block`, `max-width: none`, spanning the full 1394px gutter-to-gutter | major |
| L8 | Footer row | `all games · random game · plain text` on `--surface-3` with a top hairline | a bare `plain text` link, no rule, no siblings | minor |
| L9 | `app.css` has no rule for this page | — | `fieldset.facets` and `.facet` are styled for the listing panel; **there is no selector for `form.wizard`, its `fieldset`, or its `legend`** anywhere in the 2394 lines | major |

## N — The count panel (the centre of the redesign, entirely absent)

| # | Item | Mock | Ours (measured) | Sev |
|---|---|---|---|---|
| N1 | **Intersection count** | mono 40px / 600, `tabular-nums`, accent, with the noun beside it at 13.5px | **does not exist**. Every number on the page is marginal, which is exactly F1's complaint | blocker |
| N2 | Sticky panel | outer cell carries `--surface-3` + left hairline at full row height (measured 310×1094 at 1440); inner wrapper `position: sticky; top: 20px`, `padding: 24px 22px`, `gap: 14px` | **0 elements with `position: sticky`** inside `main` | blocker |
| N3 | Kicker | `MATCHING ALL ANSWERS`, 9px, `letter-spacing: 1.44px`, uppercase, faint | absent | major |
| N4 | Sub-line | `of 515 known · 2 answers given`, 12px faint | absent | major |
| N5 | Answer chips | one per given answer, radius 99px, each with `aria-label="clear answer to: <question>"` | absent | major |
| N6 | Loosen button | `× drop "Historical" → 4 games` when the count is small; the binding answer is the given answer with the smallest marginal count | absent. See §X2 — the number on it may not be estimated | major |
| N7 | `clear all answers` | plain link under the CTA | absent | minor |
| N8 | Submit | `Show these 19 games` — count in the label — 265×42, 14px/600, accent tint + `--accent-ring` + `--glow-sm`, radius 9 | `Show me the games`, **163×37**, `padding: 8px 14px`, **14px/400**, `rgb(29,33,37)` on dark, radius 9 | major |
| N9 | Live region | count in `aria-live="polite"`, debounced, announced as a sentence | **0 elements with `aria-live`** on the page. See §X3 — with no JS the round-trip replaces the live region rather than needing one | major |
| N10 | The panel's first line aligns with the first question's heading | `padding-top: 24px` on the sticky wrapper matching the legend | n/a — no panel | minor |

## Q — Questions and options

| # | Item | Mock | Ours (measured) | Sev |
|---|---|---|---|---|
| Q1 | **Option control** | `aria-pressed` buttons, `min-height: 40px`, `padding: 8px 14px`, radius 9px, laid out `flex-wrap: wrap; gap: 10px 8px` | native radios in `display: inline` labels; **19px tall**, `padding: 0`, `min-height: 0` — under half the 40px target and well under 44px | major |
| Q2 | **Three questions are `<select>`** | genre, kind and language are option chips like every other question | `<select>` × 3, measured **137×32 at every width including 430**, 13px, radius 6px. Two option shapes in one form, and a 32px target | major |
| Q3 | **`unknown` is not offered** | `unknown (395)` is a real selectable option in genre, kind and language, carrying its count | `FindAGame.razor:249` filters `.Where(v => !v.IsUnknown)`. **The query layer already supports it** — verified `/games?genre=~unknown&plain=1` returns a filtered listing with the row marked selected. The page declines to offer a filter the site can already apply | blocker |
| Q4 | Provenance badge per question | `● measured` / `◇ declared` / `◆ derived grouping` on every legend | none. `FacetWords.Evidence` and `FacetEvidence` already produce these three words for the listing panel | major |
| Q5 | Counts on every question | activity `199 / 229 / 280`, dark `493`; every option except "any" carries a number | activity and dark options carry **no counts at all**; genre/kind/language carry them inside the `<option>` text | major |
| Q6 | **The client question is single-select** | one `aria-pressed` choice of eight, `doesn't matter` first | seven **checkboxes** — multi-select. Also see Q7/Q8 | major |
| Q7 | **TLS appears twice** | one row, `TLS — encrypted, handshake completed by us  74` | the dedicated `FacetKeys.Tls` checkbox *and* a `protocol=TLS` row measured as `TLS— measured in the handshake 1 games` — the second one falls through to the generic gloss. Two controls, two meanings, one acronym | blocker |
| Q8 | **A missing space and a broken plural** | `MSSP — server self-description  171` | measured accessible text: `MSSP— server self-description 5 games`, `MSDP— structured client data 1 games`. Razor eats the whitespace after the `@if/else` block, and the unit is hard-pluralised | major |
| Q9 | Option order | count-descending inside each question, `any` first | same for the selects; protocols are count-descending and capped at 6 (`Take(6)`), so the cap can hide a protocol the reader wants | minor |
| Q10 | Long tail | anything under 3 folds into `3 more genres`, expanded on click | no collapse. See §X5 before building it | minor |
| Q11 | Name field | label stacked **above** a 460px field on `--surface-3` with a `⌕` glyph; kicker `GAME NAME, IF YOU HAVE ONE` 9px; placeholder `name, or part of one` | label sits **beside** the input (label at x=23, input at x=201); field 226px, no glyph; kicker `A NAME, IF YOU HAVE ONE` at 10px; placeholder `part of a game's name`. The real `<label>` the mock asks for is already there | minor |

## C — Copy

| # | Item | Mock | Ours (measured) | Sev |
|---|---|---|---|---|
| C1 | Intro paragraph | deleted | present: `Six optional questions. Every number is a count of games we measured, not an estimate. Skip to the whole listing…` | minor |
| C2 | Trailing paragraph | deleted | present: `Answers filter; they never rank.` | minor |
| C3 | Escape hatch | survives as `games` in the nav and `all games` in the footer, or beside submit — not in a paragraph | inside the intro paragraph, which C1 deletes. **Must be re-placed, not dropped** | major |
| C4 | Uncounted band | `reachable, but we can't count` | `uncounted`, read from `FacetWords.BandWord`. See §X6 — this reverses bundle 2 and a recorded decision | — |
| C5 | Dark question options | `include them` *(default)* / `live games only 493` | `no, only live games` *(checked)* / `yes, show me those too`, no counts | minor |
| C6 | Submit label | carries the count | does not | major |
| C7 | `unknown` wording | one word, `unknown`, in all three questions | `FacetWords` spells absence per facet — `not declared`, `not identified`, `nothing negotiated` — and its doc comment says so deliberately. See §X7 | — |

## S — State, the querystring, and the plain surface

| # | Item | Mock | Ours (measured) | Sev |
|---|---|---|---|---|
| S1 | **`/find` does not read its own querystring** | answers belong in the URL, linkable, surviving reload | `/find?genre=Fantasy` renders with **0 selected options**. The form is a GET at `/games`, so state exists only *after* submit. Any count on `/find` requires it to bind its own answers first | blocker |
| S2 | **`?plain=1` is a different page** | plain carries the same six questions, every option including `unknown`, and the count | plain renders **ten facet groups** (`band`, `seen`, `charset`, `lineage`, `codebase`, `version`, `family`, `genre`, `language`, `protocol`, `tls`) as querystring recipes — not the six questions. It *does* offer `~unknown`, which the rendered page hides, so the two surfaces disagree about what can be asked | blocker |
| S3 | Two vocabularies for one facet across surfaces | one | rendered says `somebody is on now` / `somebody was on this week`; plain says `connected now` / `active this week` for the same three band tokens | major |
| S4 | Intersection count query | a real indexed count per change, never the prototype's multiplied marginals | not built. `IGameQueries.SearchAsync` already returns a filtered `GameListing`, so the count exists — it is one call away once S1 lands | major |
| S5 | Submit hands off to the listing with the answers as facets (F8) | that is the design | **already true** — every control's `name` is a `FacetKeys` constant and the form GETs `/games`. The page's own header comment says why. No work | ✅ |
| S6 | Find and Games share one vocabulary (F8) | one taxonomy nesting inside the other | **partly already true**: `lineage` (derived: MUSH, LPMud) and `family`/`codebase` (declared: PennMUSH, Evennia) both exist and both are facets. Find asks `family`; the mock's *"What kind of game?"* is the `lineage` question. This is a one-key change, not a taxonomy project. See §X4 | major |

## R — Responsive, focus, themes

| # | Item | Mock | Ours (measured) | Sev |
|---|---|---|---|---|
| R1 | Document horizontal scroll | none | **none** at 1440/1024/768/430, both themes | ✅ |
| R2 | Element clipping inside `main` | — | **none** at any width or theme | ✅ |
| R3 | **The protocol row collapses into prose at 430** | one chip per line, each a 40px target | measured at 430: the seven inline labels wrap as running text, so a checkbox ends a line and its own label starts the next — `MSSP— server / self-description 5 games`. The band radios do the same | major |
| R4 | **The mock has no responsive treatment** | — | at 430 the mock keeps `minmax(0,1fr) minmax(0,310px)`: the panel stays 310px, the question column is crushed to **139px**, option chips wrap to 54px tall and the legend to 42px. **The drawing cannot be followed at 430** — the panel must stack above or below the questions, and that decision is ours to make, not the mock's | — |
| R5 | Both themes | mock is dark-only and the README says the palette is not a proposal | ours: `--surface` `rgb(15,17,19)` dark / `rgb(238,241,242)` light, both from the ramp part three of the parity doc set. Not a delta | ✅ |
| R6 | Count and submit visible at the last question | the assertion that makes the redesign true | untestable — no count, no submit outside normal flow. **Add it to the verification list once N2 lands** | — |

---

# Part three — conflicts with CLAUDE.md, or with a decision already recorded

Each of these is a place where doing what the bundle says would break a rule this repository states,
or would reverse something part two/three of the parity doc already settled. None should be actioned
without a decision recorded here first.

## X1 — F7's dark-games default contradicts rule 3

The bundle wants `Include games that have gone dark?` to default to **`include them`**, so the page's
"none of the questions are required" claim becomes true.

CLAUDE.md rule 3: *"Archiving removes a game from the default listing, the rankings and the 'active
today' figure — and from nothing else."* Excluding archived games **is** the listing's default, by
design and by spec §7.6. Inverting it on Find would make the wizard's default disagree with the
listing it submits to, so the same reader gets two different result sets from two doors into one
query — and the drift would be invisible.

**The bundle offers the compatible branch itself:** *"If the product prefers the current default,
then the page must say a filter is already applied — but do not keep both the claim and the
contradiction."* That is the route: keep `no, only live games`, and fix the sentence. Note that C1
deletes the sentence containing the claim, which resolves the contradiction on its own — but only if
the replacement does not reintroduce it.

## X2 — The prototype's count is fabricated, and rule 4 forbids shipping it

`countFor()` in the prototype multiplies marginal ratios (`n = n * (opt.n / TOTAL)` per answer). The
README says "do not ship that; it assumes independence and will be wrong." CLAUDE.md rule 4 makes it
stronger than advice: a number produced by an assumption, rendered in the site's mono accent beside
the word *games*, is a fabricated measurement on the one site whose product is that it does not
fabricate. **Every number in the panel — the count, the sub-line, the figure on the loosen button —
must come from a real query or not be drawn.**

## X3 — The live count as specified needs JavaScript; this site has none

F1's panel is a debounced `aria-live` region updating as answers change. There is no JS anywhere on
this site, by constraint. The no-JS shape of the same idea:

- `/find` binds its own querystring (S1), so each answer is a link or a `submit` back to `/find`.
- The count is computed server-side and rendered statically; the CTA is a link to `/games` with the
  same querystring.
- `aria-live` and the debounce then become unnecessary rather than unimplemented — a page load
  announces its own heading, which is the behaviour the debounce was approximating. The bundle's
  underlying requirements (announce a sentence, not a bare number; do not move focus) survive intact
  and are satisfied differently.

This should be written down as a deliberate divergence, in the shape of §H in the parity doc, rather
than left as "the aria-live work is outstanding".

## X4 — F4's grouping map is mostly already built, and rule 5 governs the rest

The bundle proposes a raw-string → group map (`LPMud, FluffOS, LDMud → LPMud family`) plus a
`◆ derived` badge, with maintainer sign-off first. Two corrections:

1. `MUI.Catalog` already draws this distinction. `lineage` is the derived grouping facet and carries
   `FacetEvidence.Derived`; `family` and `codebase` are the declared strings. The work is to point
   *"What kind of game?"* at `lineage` and render the badge — **not** to author a new mapping table
   in the web layer, which would be a second copy of a vocabulary the catalogue owns (the exact
   failure mode `FindAGame.razor`'s own header comment warns about).
2. Rule 5 — *never record a decision of ours as a measurement of theirs* — is what the `◆ derived`
   badge exists to satisfy, and it is not optional decoration. A grouped option is our judgement
   about their string. The badge is the sentence that says so.

## X5 — "Dragonball is a data error — fix the record, don't render it" collides with rules 3 and 4

`Dragonball (1)` is a theme sitting in the codebase question, and the bundle says to exclude it.
But it is a value **the game declared**. Suppressing a declared string because we judge it
miscategorised is us overriding what a game said about itself, and rule 3 says nothing is ever
deleted. What is permissible:

- Group it under `unknown`/`not identified` *only if* the grouping is badged derived and the game's
  own page still shows the raw string (which F4 already promises).
- Let the long tail (F5) absorb it as one of the "n more" — an aggregation, not a deletion.

What is not permissible is a hard-coded suppression list in the web layer. Same reasoning applies to
"normalize `russian` → `Russian`": case-folding for *display* of a derived group is fine; rewriting
the stored declared value is not.

Additionally, F5's `3 more genres` bucket must expand to real selectable values. If the bucket
itself were submittable, Find would offer a choice `/games` cannot express — the one invariant the
page's header comment names as the first thing that would drift.

## X6 — Δ3 reverses a decision recorded in the code

`FindAGame.razor` carries this, verbatim:

> the listing shortened this to "uncounted" and this page went on saying "reachable, count unknown"

— which is the note explaining why the band word is now read from `FacetWords.BandWord` rather than
spelled twice. Bundle 2 agreed (`uncounted (match the listing)`). Bundle 3 now asks for
`reachable, but we can't count`, which is (a) the drift that was fixed, and (b) a phrase the locked
glossary in bundle 2 does not contain — `uncounted` is a glossary entry with a translator note; the
new phrase is a sentence that would have to be translated freely in nine locales.

**Recommendation: do not take Δ3.** If the copy is genuinely wanted, change the *listing's* word too
and change the glossary entry, in one commit, so the two surfaces never disagree again.

## X7 — One word `unknown`, versus a word per facet

The mock labels every silent bucket `unknown`. `FacetWords`' own doc comment states the opposite as
a rule:

> Every facet spells its own absence, and none of them spells it as a *no* — "not identified" is a
> fact about our reach, "not declared" is a fact about what a game published, and neither is a fact
> about the game lacking the thing.

Genre and language are `not declared` (the game published nothing); codebase is `not identified` (we
could not tell). Collapsing both into `unknown` loses precisely the distinction rule 1 and the locked
glossary exist to protect. **Take F3's substance — make silence a selectable option carrying its
count — and keep our per-facet wording.**

## X8 — Nothing here reverses a §H decision from the earlier passes

Checked against the ten items in `2026-08-17-mockup-parity-qa.md` §H. Bundle 3 touches two of them
and agrees with both: §H8 (provenance stays, drawn differently) is *reinforced* by Q4's per-question
badges, and §H6 (a form needs a firable submit) is unaffected — the wizard's submit is the form's,
and the mock keeps a submit too. No reversal is proposed by this bundle, and none is proposed here.

---

# Part four — shape of the work

Read off the deltas above, not estimated in the abstract.

| Layer | Items | Character |
|---|---|---|
| **CSS** | L1–L9, N2–N5, N7, N10, Q1, R3, and the visual half of N8 | The largest count and the lowest risk. `app.css` has no `form.wizard` block at all, so this is additive: a fieldset reset, the 24/14/24 legend rhythm, the option chip, the two-column grid with the sticky panel (`overflow: clip`, never `hidden`), the footer row. Roughly **40%** of the items. |
| **Markup / Razor** | Q2–Q9, Q11, C1–C7, N8's label, L8, S3, and the plain rewrite in S2 | Mostly small and mostly mechanical, but it includes three real bugs — the missing space, `1 games`, and TLS listed twice with two meanings. `?plain=1` needs the six questions rather than the ten-group facet dump, which is the biggest single piece here. Roughly **35%**. |
| **Query layer** | S1, S2's option parity, S4, S6, N1, N6, Q3, Q10 | Smallest count, all of the risk, and the only part that reaches `MUI.Catalog`. `/find` must bind its own querystring; the intersection count and the drop-the-binding-answer count are two `SearchAsync` calls that must be real (X2); `family` → `lineage` is one key; the long tail needs an aggregation the listing can also express. Roughly **25%**. |

The two blockers that unlock everything else are **S1** (`/find` reads its own answers) and **Q3**
(`unknown` is offered, since the query layer already answers it). N1 depends on S1 and on nothing
else.

## Verify before closing out, once the work is done

Bundle 3's own list, plus what this audit adds:

- With the viewport at the last question, the count and the submit are both in view (R6).
- Rhythm inside each question measures 24 / 14 / 24 (L3).
- Six `fieldset`/`legend` pairs, exactly one option selected per question (Q1, Q6).
- Submitting with JavaScript disabled returns the same result set — trivially true here, and worth
  asserting anyway because it is the constraint the whole design bends around (X3).
- `/find?…` is linkable and survives reload (S1).
- `/find?plain=1` carries the same six questions and the same options including the silent bucket,
  in the same words (S2, S3, X7).
- A combination returning zero shows the loosen button and the number on it comes from a query (N6,
  X2).
- No option is offered that `/games` cannot apply (Q10, X5).

---

# Part five — what shipped, and where it diverges

Measured on the branch, at 1440 / 1024 / 768 / 430 in both themes and at `/de/find`, on the fixture.

## The verification list, run

| Check | Result |
|---|---|
| Count and submit in view with the viewport at the last question (R6) | ✅ at 1440, 1024, 768 and 430, both with and without answers |
| Rhythm 24 / 14 / 24 inside each question (L3) | ✅ exactly, on all six |
| Six labelled groups, exactly one option chosen in each (Q1, Q6) | ✅ six `role="group"` with `aria-labelledby`, one `aria-current="true"` each, every option ≥ 40px |
| Works with scripting off (X3) | ✅ zero `<script>` on the rendered page; every answer an anchor; the one form the page owns is a GET at `/find`. Asserted in `FindAGameTests.NothingOnThisPageNeedsScript` |
| `/find?…` linkable and surviving reload (S1) | ✅ |
| `?plain=1` carries the same six questions, options and words (S2, S3, X7) | ✅ both surfaces are renderers over one `FindScreen` |
| Zero results shows the loosen button, on a counted number (N6, X2) | ✅ |
| No option offered that `/games` cannot apply (Q10, X5) | ✅ every link walked through the listing's own binding in test |
| Document horizontal scroll, and clipping inside `main` (R1, R2) | ✅ none, at four widths × two themes × four querystrings |

## Divergences, each because a rule here forbade the drawing

1. **No `aria-live`, and none needed (X3).** The count is computed server-side and every answer is
   an address, so the debounced live region is unnecessary rather than unimplemented. Zero elements
   with `aria-live`, deliberately.
2. **Headings, not `fieldset`/`legend` (Q1).** A `<legend>` is announced when focus enters a form
   control in its group, and there are no form controls to enter — the options are links. Each
   question is a `role="group"` labelled by a real `<h2>`, which the handoff allows as the
   substitution and which also puts the six in the document outline.
3. **The dark-games default is not inverted (X1).** Kept as the listing's, with the claim that
   nothing is filtered deleted rather than the default changed; both answers carry the count they
   return.
4. **`uncounted` stays (X6, Δ3).** Read from `FacetWords.BandWord`, so Find and the listing cannot
   drift apart again. Taking Δ3 would mean changing the listing and the locked glossary in the same
   commit.
5. **Per-facet absence wording, not one word `unknown` (X7).** The silent bucket is selectable and
   carries its count — F3's substance — spelled `not declared` / `not identified` as `FacetWords`
   requires.
6. **The panel stacks at ≤ 860px, and the drawing has no answer for that (R4).** Below the questions
   because it is below them in the DOM, sticky to the bottom of the viewport, laid out as a wrapping
   row *in document order*. It was briefly a two-column bar with the call to action pinned right and
   spanning three rows, which put it first to the eye and fourth to the keyboard; `scroll-padding-bottom`
   on `html:has(.find-page)` is what stops the pinned bar covering an option that has just taken focus.
7. **The page's own copy is in the message bundle, questions included.** The six questions, the
   answer that un-asks each one and the capability glosses are ids rather than English in C#, and
   the locale is a parameter of `FindScreen.BuildAsync` rather than something applied to what it
   returns — so the rendered page and `?plain=1` cannot come out in different languages. The four
   satellite `.resx` files were untouched when this was written and are not any more: German, Dutch,
   Japanese and Chinese each carry 44 translated `find.*` ids now, so a reader on `/de/find` meets
   the page in German rather than the English fallback this paragraph promised. What still falls
   back to English is whatever has been added since a translation round — currently the `crawler.*`,
   `preview.*`, `feed.plain.*` and `home.plain.*` ids, which are queued for the next one.

## Known, and not fixed here

- **The tab order crosses the column boundary once at ≥ 861px**: six questions top to bottom, then
  up and right into the panel. That is the DOM order a right-hand summary column has, and the
  alternative — the panel first — would hand a keyboard reader "show these 19 games" before the
  first question. Strictly forward within each column, and strictly forward everywhere at ≤ 860px.
- **The plain surface's structural headings elsewhere on the site are still English.** Find's are
  not, because Find is the page in scope; the rest of `PlainText` is a separate pass.
