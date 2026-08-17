# The untranslated sweep — 2026-08-17

Written after the chrome, the Find page and the heatmap were localized (304 ids, four locales
complete). This is the record of **what is still English when the site is asked for German**, how it
was found, and how it is partitioned for the work that closes it.

## How it was measured

Two passes, because the first over-reports and the second is authoritative.

**Pass one — the pseudo-locale.** `/qps-ploc/…` renders every id through
`Messages.Pseudo`, which wraps a translated string in `⟦ ⟧` and accents its vowels. Any visible text
*without* the brackets came from somewhere other than the message pipeline. 885 raw hits.

That number is mostly noise: game names, hostnames, codebase strings, version numbers, protocol
acronyms and catalogue tokens are **machine voice** and must never be translated (see the class
comment on `Messages`). Filtering the elements that carry them — `.row-name`, `.check-name`,
`.opt-name`, `.feed-name`, `.name`, `.mark`, `.mono`, `.quote`, `<option>`, anything with
`translate="no"` — leaves 776.

**Pass two — German against English.** Render `/X` and `/de/X`, walk both for visible text and for
`aria-label`/`title`/`placeholder`, and report every string that comes back **byte-identical in both
languages**. This is the one that counts: it cannot be fooled by a pseudo-locale sharing English CLDR
data (which is why `Mon`/`Tue` appeared in pass one and is *not* a defect — the day names do come from
`CultureInfo` and are German in German).

**388 strings are identical in English and German.** Disclosures are forced open before walking, so
folded copy is included.

## What is left, by surface

| Surface | Strings | What it is |
|---|---:|---|
| `/g/…` game page | 228 | ~200 of them are one shape: the trend chart's per-day SVG `<title>`. See below. |
| `/about` | 76 | The page is entirely English — headings, the five rules, "what we know we get wrong". |
| `/rankings` | 30 | H1, lede, the three window links, every column header, the `sr-only` caption. |
| `/ecosystem` | 29 | H1, all three section headings and their ledes, the share/denominator notes. |
| `/archive` | 13 | H1, lede, the search fieldset, the badge, the two archive reasons, the placeholder. |
| `/games` | 4 | Only tooltips: the provenance chip and the "and 1 more" protocol overflow. |
| `/find` | 3 | `all games`, `random game`, `plain text` — three links the rebuild missed. |
| `/submit` | 3 | H1 and the two paragraphs. |
| `/account/sign-in` | 2 | H1 and the demo-mode note. |

### The game page's 228 is four messages, not 228

- `{date} — {n} on average, {low}–{high} across {probes} probes`
- `{date} — no measurement`
- `{date} — probed, no count could be read`
- the summary above the chart: *"Typically 25 on, peaking at 48, over 78 of 90 days. Steady across the
  window."*

Plus the ANSI capture's chrome — `read as text`, `as sent by the server`, `16-colour SGR`,
`captured` — and the connect screen's own `<pre>`, which is **correctly** untranslated: it is what the
server sent.

`P.tagline` and `P.lede` also show up in the diff and are **not** defects for the same reason. They
are the game's own description, measured from MSSP.

### Cross-cutting: dates and provenance

Two shapes appear on nearly every page and are counted above under whichever page found them first:

- `<time title>` — `"2w ago, 30 Jul 2026 18:00 UTC"`. The relative part, the month name and the
  ordering are all English. `CultureInfo` already reaches the day names in the heatmap; this is the
  same job for `Dates`/`Moment`.
- The provenance chip's `title` — `"PennMUSH 1.8.8p0 — declared via Mssp, last confirmed 30 Jul 2026"`.
  Note `Mssp` is also mis-cased here: it is an acronym and the enum's `ToString()` is leaking into a
  reader-facing string. Fix the casing with the translation.

## Reference articles are out of scope, deliberately

`/reference` and `/reference/…` are Markdown under `content/reference/*.md`. Translating an article is
a content project with a different unit of work — a translated article is a document somebody owns,
not a string in a bundle — and machine-translating the site's own explanations of MSSP and telnet
would produce exactly the confident-and-wrong prose this project exists not to publish. The reference
index's *chrome* is in scope; the articles are not.

## Partition for the work

Four independent surfaces, no shared files except `Messages.cs`, to which each appends its ids in its
own marked section so a merge is additive:

1. **Static page copy** — `/about`, `/submit`, `/account/sign-in`.
2. **Catalogue surfaces** — `/ecosystem`, `/rankings`, `/archive`, and `/find`'s three stray links.
3. **The trend chart and the game page's remainder** — four message shapes, the chart summary, the
   ANSI capture chrome.
4. **Dates and provenance** — `Dates`/`Moment`/the chip title, cross-cutting, and the `Mssp` casing.

`Messages.resx` is regenerated from `Messages.cs` after the merge rather than hand-edited, so it is
not a conflict surface. The four locale satellites are left alone by every agent and translated in one
round afterwards, as the previous three rounds were.
