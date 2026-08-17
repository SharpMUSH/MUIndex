# Mockup parity — QA, then the work

An audit of `design/measured-once` against the handoff, and the record of what the pass that
followed it changed. Part one is the pre-edit checklist; part two is what shipped, what diverged
from the drawing and why, and what is still open.

The handoff moved mid-pass. The second bundle (`Mu-index accessibility audit (1).zip`) rewrote the
README, added an i18n review and eight i18n specimens, and changed copy in the redesign screens —
`players` became `connected`, the count cell lost its unit, the column kicker became
`CONNECTED · REACHED`, and "When people are on" became "Connections by hour". **The geometry did not
change**, so every measurement in part one still held; the copy in part two is the second bundle's.

## Method

- Mockup served over HTTP with `showAnnotations` forced to `false`, rendered in Playwright's
  Chromium at 1440 / 1024 / 768 / 430; each screen captured as an element screenshot.
- Site on the fixture (no `MUI_POSTGRES`), so every page carries the demo banner. That banner is
  required by CLAUDE.md and is not a delta.
- Every number is a `getComputedStyle` / `getBoundingClientRect` reading, not an eyeball.
- Dark is the comparison baseline because the mockup is dark; light checked separately.

---

# Part one — the audit

## A. Chrome

| # | Item | Mock | Ours (measured) | Fixed |
|---|---|---|---|---|
| A1 | Bar height | 60px fixed, stretch, no wrap | 54px, centred, `flex-wrap: wrap` | ✅ |
| A2 | Bar padding | `0 20px` | `11px 24px` | ✅ |
| A3 | Bar fill | `--surface-3`, a step **below** the card | `--raised`, a step **above** | ✅ |
| A4 | **Marker shifts the bar** | drawn inside the box | `a.on` added `padding-bottom: 3px`; `games` sat at y=111 on `/games` and y=112 elsewhere | ✅ |
| A5 | Wordmark | Hanken 17px/700 | Cascadia Mono 14px/600 | ✅ |
| A6 | Nav item | 13.5px | 14px | ✅ |
| A7 | Group rule | 1px × 18px, centred | 1px × 23px, stretched | ✅ |
| A8 | `submit` | radius 9, padding `6px 12px` | radius 99, padding `4px 11px` | ✅ |
| A9 | `sign in` | ring **and** tint | ring only | ✅ |
| A10 | Wording | `find` | `find a game` | ✅ |

## B. Home

| # | Item | Mock | Ours | Fixed |
|---|---|---|---|---|
| B1 | Content padding | `30px 24px 26px` | `36px 24px 48px` | ✅ |
| B2 | Rhythm | uniform 22px | 6 / 24 / 24 / 48px + a 33.6px `h2` margin | ✅ |
| B3 | **Dead band under the tiles** | 22px | ~82px | ✅ |
| B4 | Feed column gap | 22px | 24px | ✅ |
| B5 | **H1 wraps at every width** | 26px, one line | 28px with `max-width: 24ch` | ✅ |
| B6 | Lede | 14.5px | 15px | ✅ |
| B7 | Stat-tile value | Hanken 28px/600 | Cascadia Mono 26px/600 | ◑ size only — see §H3 |
| B8 | Feed name weight | 400 | 600 | ✅ |
| B10 | Search field | one control, max-w 520, radius 9, `⌕` | two controls, max-w 560, radius 6, no glyph | ◑ — see §H6 |
| B11 | Placeholder | `search by name, theme, codebase or host` | `search 9 games` | ✅ |
| B12 | Tile box | padding 16, gap 8, shadow | padding `12px 14px`, gap 6, none | ✅ |
| B13 | Crawler strip | always | renders nothing on the fixture | ◑ — verify against a real pulse |
| B14 | **Feed heading colours** | accent / **amber** / accent | all faint | ✅ |
| B15 | **Per-row prose suffix** | none | a mono line on every row | ✅ |
| B16 | **"came back" is a card** | same plain row | accent tint, border, one-shot glow, prose line | ✅ |
| B17 | Last-row hairline | omitted | present | ✅ |
| B18 | Footer | flex, top hairline, faint separators | `<p class="faint">`, underlined, no rule | ✅ |
| B19 | Only `archived` is a link | no tile is | tiles 1–3 inert, tile 4 wraps an `<a>` | ◑ kept — the archive is a real destination |

## C. Games

| # | Item | Mock | Ours | Fixed |
|---|---|---|---|---|
| C1 | Header block | padding + **hairline below** | margin-bottom 30px, no hairline | ✅ |
| C2 | Filter column fill | `--surface-3` | none | ✅ |
| C3 | Filter column padding | `20px 14px` | `0 24px 24px 0` | ✅ |
| C5 | What is in the column | three groups + one footer line | search, sort `<select>`, two checkboxes, a `show` button, eight groups, a three-line legend | ✅ |
| C6 | Sort control | `now / typical / peak` in the toolbar | `<select>` + `show` in the column | ✅ |
| C7 | Group header | name left, **badge pill** right | run together inline | ✅ |
| C8 | Group tracking | `0.16em` | `0.14em` | ✅ |
| C9 | Per-group note | one under every header | one line after the third group | ✅ |
| C10 | **Activity is single-choice** | `◉ / ○`, no exclude | `✓ / ☐` with a `−` on every row | ✅ |
| C11 | Selected row | tint + ring + accent count | `.any.on` zeroed both | ✅ |
| C12 | Excluded row | danger tint / ring / strikethrough | dim tint, faint strikethrough | ✅ |
| C14 | **Rows overflow their column** | fits | 253px in a 243px box (223px at 768) | ✅ |
| C15 | Footer note | one line | a 3-row legend + two more lines | ✅ |
| C16 | **Legend wraps right-aligned** | — | both themes | ✅ |
| C18 | Toolbar | `sorted by…` + switch + kicker, hairline | count + kicker + `RANDOM`, no hairline | ✅ |
| C19 | **Age on the wrong line** | count and age share line 1 | age dropped to line 2 | ✅ |
| C20 | **Provenance on rows** | cut | `●`/`◇` pip + `declared` | ✅ |
| C21 | **Age twice per row** | once | in `.meta` and in the right column | ✅ |
| C22 | Count | whole cell accent, weight 400, **no unit** | number accent, `on` dim, weight 600 | ✅ |
| C23 | Claimed | 16px ring with `✓` | a `CLAIMED` word pill | ✅ |
| C24 | Unreadable count | `not counted` | `— count unknown` | ✅ |
| C25 | Break-row copy | `Below: reachable, count unreadable — not zero.` | `FROM HERE …` | ◑ kicker kept; it is a row, not a sentence |
| C26 | Row padding | `13px 20px` | `13px 24px` | ✅ |
| C27 | Footer | a card footer row | a bare link in the results column | ✅ |

## D. Game

| # | Item | Mock | Ours | Fixed |
|---|---|---|---|---|
| D1 | Header padding + hairline | 24px + rule | neither | ✅ |
| D2 | Identity gap | 16px | 24px | ✅ |
| D3 | **Monogram** | render nothing | a `MU` plate | ✅ — see §H1 |
| D4 | Claim line | one sentence + link | a badge and a paragraph three blocks apart | ✅ |
| D5 | Address | a mono line | a full-width bordered box each | ✅ |
| D6 | Count | mono 20px accent + glow over a 9px kicker | `● 15 players on now ● 2w` at 14px | ✅ |
| D7 | **Body is two columns** | one | `.two-up` 702/519 | ✅ |
| D8 | Connect panel | surface-3, radius 9, padding `16px 18px` | `#000`, radius 0, padding 24 | ✅ |
| D9 | Connect `pre` | 11px/1.35 | 13px/1.5 | ✅ |
| D10 | `read as text` | inside the figure | a `<details>` outside it | ◑ inside, still folded — see §H2 |
| D11 | Caption | lowercase mono | uppercase kicker, and it printed the literal string `Page.ConnectScreenCharset` | ✅ (a real bug) |
| D12 | Populated graphic | 24 hourly bars | the 7×24 grid | ✗ kept — see §H4 |
| D14 | Capabilities frame | bordered, foot caption inside | bare table, loose caption | ✅ |
| D15 | Caption copy | `None of the 6 disagree.` | the long "before" wording | ✅ |
| D16 | Footer | three links | `plain text` alone | ✅ |

## E. Overflow · F. Focus · G. Themes

- **E**: the only real clip was C14. `.listing-grid`'s 24px `scrollWidth` excess was the deliberate
  hover bleed, and the ANSI `pre` is contained by the one region CLAUDE.md lets scroll sideways.
- **E1**: the filter column stayed a fully expanded panel at 430 — ~1,100px to scroll past. Fixed.
- **E3**: rows went ragged at 430, some putting the age beside the count and some below. Fixed.
- **F1**: `.two-up` put the whole left column first in the DOM, so focus jumped from y=2182 back to
  y=1367. Fixed by the single column.
- **F2/F4**: activity contributed three "excluded" tab stops for a facet that cannot be excluded
  from, and the panel had ~59. Fixed.
- **F3**: accessible names read `1 games` / `0 games`. Fixed.
- **G**: `theme-color` was already media-scoped and `?plain=1` was already on all eight pages —
  both polish items were done before this pass.

---

# Part two — what shipped

## Verified, not asserted

| Check | Result |
|---|---|
| Document horizontal scroll — 8 pages × 4 widths × 2 themes | none |
| Element clipping, same matrix | none |
| `nav.scrollWidth <= nav.clientWidth`, 1440 → 360 | holds at every step; bar stays 60px |
| Nav geometry across `/`, `/games`, `/rankings`, `/archive` | byte-identical — the bar cannot shift on arrival |
| Backward focus jumps | none on the game page; the two remaining are column-to-column, which is the layout |
| Tests | 631 Web · 346 Crawl · 267 Discovery · 13 I3 · 499 Catalog · 250 Crawler |

## Internationalization

Only the parts that need no translation pipeline, which is the handoff's own step 1 and half of
step 5:

- **B2** — `NameScript` derives a language and a direction from a name's own codepoints, and
  `GameName` wraps every game-supplied name in `<bdi>`. It answers **only where the script settles
  the answer**: kana ⇒ Japanese, Hangul, Thai, Greek, Hebrew, Arabic. Han without kana, Cyrillic and
  Devanagari get isolation and direction but **no `lang`**, because guessing there would assert
  something about somebody's game that no probe measured — the exact failure S8 describes.
- **S1** — the kicker is a semantic class whose typography is locale-gated: no uppercase and no
  tracking for Arabic, Hebrew, Thai and Devanagari; a size step up and tracking near zero for CJK.
- **S2** — the nav's degradation order, implemented and asserted: labels → submit's border → both
  groups into one `menu` disclosure → submit into the menu → type. Every step loses decoration and
  never a destination, and **every link exists in the document exactly once** (the obvious
  implementation — a second copy inside the menu — puts two `aria-current="page"` markers in one
  page; the tests catch it).
- **S4** — the connect caption states the width in *cells* and says `double-width` where the content
  is East Asian Wide, rather than claiming 80 columns for a 40-glyph screen.
- **S7 / copy** — `players` → `connected` everywhere, the count cell's unit moved into the column
  head (one string per locale instead of 515), and `claimed by its owner` names no person.
- **Tier three** — `translate="no"` on the machine voice: codebase strings, versions, protocol
  acronyms, hostnames, connect-screen output.

**Not done, and each needs a pipeline this pass does not have:** locale routing and `hreflang`, ICU
message extraction, the locked ~90-id glossary, the MT banner, the footer language switcher,
localized `?plain=1`, `Intl` date and relative-time formatting, romanized aliases with provenance,
and the Russian CI canary.

## H. Where the drawing was not followed

1. **The game-page monogram is gone** (D3). The brief is explicit — no monogram, no grey square, no
   initial in a circle. CLAUDE.md's icon section says the opposite in passing, but that is a
   description of current behaviour inside a rule about fetching, not a rule about drawing.
   `GamePlate` keeps the implementation behind a `Fallback` parameter nothing passes.
2. **`read as text` is inside the figure but still folded** (D10). The drawing shows only the prose
   lines, open. We cannot produce "only the prose lines": deciding which of a stranger's lines are
   artwork is a judgement about somebody else's screen, and rules 4 and 5 forbid publishing our
   reading of their output as a fact about their game. So the alternative stays the *whole* screen —
   and therefore stays folded, because open it would draw eighty columns of box-drawing twice on one
   page, which is B1's triplication at two copies instead of three.
3. **The stat-tile value stays in mono** (B7). The drawing sets it in the UI sans; the handoff's own
   token rules, two pages earlier, say "mono for anything the machine said — counts, versions,
   ages … the one typographic rule carrying meaning". A count of the hobby is the purest case, so
   the prose won over the component. The size is the drawing's.
4. **The 7×24 grid stays; the 24-bar hour profile was not built** (D12). An hour-averaged bar cannot
   express CLAUDE.md rule 2's three states — counted, probed-but-uncountable, not measured — and
   collapsing the middle one is the bug the file calls the worst this codebase could ship. Everything
   else in that section is the drawing's: the heading, the takeaway sentence, the caption, the
   `details`, the per-day table.
5. **The order switch has five options, not three** (C18). `now / typical / peak` is the drawing;
   `name` and `reached` follow a rule because they read a spelling and a date rather than a count.
   They had a control before this pass, and losing a way to sort five hundred rows alphabetically to
   match a mockup that never had one would trade a feature for a picture.
6. **The search keeps a submit** (B10). The drawing shows a field alone. A form that submits only on
   the return key is one a pointer-only reader cannot fire, so the button stays — as a glyph inside
   the field in the 268px column, and beside it on the home page's 520px one.
7. **The `also show` group is ours** (C5). Archived and adult are inclusion switches the drawing has
   no equivalent for. They were checkboxes waiting on the `show` button that this pass deleted, so
   they became link rows in the panel's own geometry.
8. **The provenance glyph stays on the codebase chip.** S1 says cut provenance from listing rows
   entirely; the same document's protect-list says "provenance and age on every value — change how
   it is drawn, never whether it is there", and CLAUDE.md rule 1 agrees. The count lost its glyph,
   its word and its legend lookup, which is what S1 was actually about; the codebase keeps one
   character and loses its age, which was the duplicate.
9. **`WHAT WE COULD MEASURE` is still deferred.** `ActivityBand` is one exclusive band per game in
   `MUI.Catalog` and the drawing implies orthogonal booleans. Catalog and API change, not CSS.
10. **The About split is still open** — `/about`, `/about/crawler`, `/about/sources`,
    `/about/licence`, plus redirects and four plain surfaces. Its own change.

## Known limits

- The crawler strip cannot be checked against the fixture; it renders nothing when the pulse is
  unknown, which is correct, so its 12px padding and hairlines want one look against a real pulse.
- The sparse "Connections by hour" panel is unexercised here for the same reason — the fixture game
  is populated.
- The nav holds to 360px. At 320 the wordmark and the menu collide; no target width is that narrow.
- `::details-content` carries the two disclosures that are only disclosures at narrow widths. Both
  are guarded by `@supports`, and a browser without it gets a working menu button and a working
  filters disclosure at every width instead of an unreachable panel.
