# Site design brief — what a design session needs to decide

**Purpose.** This document is the input to a dedicated design session. Work through it, and the
filled-in result is a complete handoff: enough for implementation to begin on the front end without
further design questions.

**Read first:** [`specs/2026-07-30-mu-directory-design.md`](specs/2026-07-30-mu-directory-design.md).
The system design is settled; this brief covers only how it is presented.

---

## 0. Non-negotiable constraints

These come from the product thesis and are not design choices. A proposal that violates one is
wrong regardless of how it looks.

1. **Every displayed fact carries its provenance and its age.** There is no unlabelled data on this
   site. The design must make provenance legible without making every page look like a
   spreadsheet — this is the central visual problem.
2. **Measured and declared must be visually distinguishable at a glance.** "The game says it
   supports GMCP" and "the server offered GMCP during the handshake" are different claims and the
   page must show both when they disagree.
3. **Zero players and unreachable must never render alike.** A gap in the activity heatmap means
   "we could not reach it"; a zero means "we reached it and nobody was on". Conflating them is the
   single worst thing this design could do.
4. **No voting, rating, or ranking-influence affordance may exist anywhere.** Not a star, not an
   upvote, not a "recommend". Rankings are computed.
5. **Nothing is ever deleted.** Archived games keep their page and URL. The design needs a
   dignified, non-alarming treatment for a game that has been dark for three years.
6. **Accessibility is a primary constraint, not a pass at the end.** The MU\* hobby has a
   substantial blind and low-vision population who play these games precisely because they are
   text. A screen-reader-hostile MU\* directory is a failed MU\* directory.

---

## 1. Identity

- **Name.** *MuIndex* is a placeholder. Requirements: says "MU\*" not "MUD" (MUSH/MUCK/MOO players
  read MUD-branding as not-for-them), pronounceable, domain-available.
- **Positioning line.** One sentence that communicates *measured, not asserted*, without jargon.
- **Tone.** Where does it sit between archival/institutional (a register of record) and
  enthusiast/warm (a hobby site)? The data is rigorous; the subject is people's creative
  communities. Both readings are defensible — pick one deliberately.
- **Logo/mark**, favicon, and whether there is any ASCII/terminal motif. Note the trap: terminal
  pastiche is the obvious move for this subject and is therefore also the templated one.

## 2. Visual language

- Type scale, including a monospace pairing — connect screens, WHO output, and code samples all
  need it, and the monospace face must render CP437/box-drawing and be legible at small sizes.
- Colour system, with an explicit answer for **rendering ANSI colour inside the page** (§4.4).
  Games emit 16-colour, 256-colour and truecolor SGR; the site's own palette has to coexist with
  arbitrary colour it does not control.
- Light and dark themes, both first-class.
- Density: this site is inherently data-dense. Decide the default and whether a compact/comfortable
  toggle is worth its complexity.

## 3. Information architecture

- Top-level navigation across the four catalogues (games, clients, codebases, protocols) plus the
  ecosystem dashboard, the liveness feeds, and owner sign-in — given that games dominate by weight.
- URL scheme, including stable permalinks for archived games.
- Search: one global search or per-catalogue? How do facets and free text combine?
- Where the liveness feeds live. They are the differentiator; homepage placement is a real question.

## 4. Signature components

These are the ones that carry the product. Each needs a designed treatment, not a default.

### 4.1 The provenance chip
The atom of the whole design: a value, plus where it came from, plus how old it is, plus whether it
was measured or declared. It appears hundreds of times per page in aggregate. Needs a resting form
that is nearly invisible and an expanded form that is complete. Consider: does age render as a
date, a relative time, or a decay indicator?

### 4.2 The capability matrix
Per game: ~20 protocol capabilities, each in one of four states — measured-present,
measured-absent, declared-only (asserted but not observed), and unknown. The disagreement case is
the interesting one and must not be buried.

### 4.3 The activity heatmap
Day-of-week × hour-of-day, in the *viewer's* timezone (a MUSH's playerbase is globally scattered;
this is the single most useful fact on the page). Must distinguish low activity from no data.
Needs a non-visual equivalent — a screen reader must be able to convey "busiest Tuesday evenings,
quiet weekday mornings" without reading 168 cells.

### 4.4 The connect-screen renderer
The hero of a game page: arbitrary ANSI art of arbitrary size, from a source we do not control.
Decisions needed on cropping vs scaling vs scrolling, aspect handling, a suppressed-screen
fallback, and what happens when the art is enormous, blank, or ugly. It is the most distinctive
thing on the page and the least controllable.

### 4.5 The uptime/availability strip
90-day availability at a glance, with outage causes on inspection. Prior art exists (status pages)
— decide whether to follow it or diverge.

### 4.6 The liveness feed card
Three feed types — *newly discovered*, *went dark*, *came back*. Each is a small card with a
different emotional register. "Came back after 26 months" is a genuinely delightful item and should
be designed as one.

### 4.7 The facet panel
Faceted search over the MSSP taxonomy plus derived facets. Many facets, most rarely used. Needs
progressive disclosure and a mobile answer.

## 5. Page templates

For each: what it must carry, what is above the fold, and what it degrades to on mobile.

1. Home
2. Game listing / search results
3. **Game page** (the most important page on the site)
4. Archived-game page (same URL, different treatment) — and the archive section that indexes them
5. Ecosystem dashboard
6. Client / codebase / protocol reference pages
7. Orientation and explainer articles
8. Owner dashboard (see §7)
9. Claim flow
10. About / crawler-transparency page — what we probe, how often, how to opt out. This page is a
    trust artifact; treat it as designed, not as boilerplate.

## 6. States and edge cases

Every one of these will occur on real data and needs a designed answer:

- Discovered but unclaimed (the majority of listings at launch).
- Claimed and enriched (the aspirational state — it should visibly reward claiming).
- Dark for a week / a year / three years.
- **Archived** — out of the default listing, still probed, still permanently addressable. Needs a
  treatment that reads as *historical record*, not as *deleted* or *failed*. The archive is a
  browsable section in its own right (spec §7.5), so it needs an entry point in the IA, an
  *include archived* affordance on the listing, and a page treatment stating when the game was last
  reachable and how long it was known live.
- **Just came back** — un-archived by a successful probe after months or years dark. The most
  delightful state on the site; design it as one.
- Player count unknown because WHO was unparseable and MSSP `PLAYERS` is absent.
- Connect screen suppressed by the owner.
- Declared and measured capabilities in conflict.
- Suspected duplicate awaiting review.
- A game with almost no data at all — one endpoint, a name, nothing else.
- A game whose MSSP was last confirmed in 2019 and has been rotting since.

## 7. Owner dashboard

Separate design problem with a different user. Enrichment fields, connect-screen suppression, WHO
override, opt-out, MSSP linter scorecard, badge/embed generation, multi-owner and transfer.

The linter scorecard is the interesting piece: it should feel like a helpful diagnostic, not a
scolding — the owners it most needs to reach are running a hobby game in their spare time.

## 8. Accessibility

Target: WCAG 2.2 AA as a floor, with specific attention to —

- Every chart, heatmap and matrix has a meaningful text equivalent, not an alt-text stub.
- Full keyboard operation of facets, matrix and heatmap.
- Colour is never the sole carrier of a state (measured vs declared, up vs down).
- Rendered ANSI is announced sanely rather than read out as colour codes.
- Sensible behaviour under `prefers-reduced-motion` and at 200% zoom.

Worth deciding explicitly: is there a **plain/text-only mode**? Given the audience and the subject
matter, a genuinely good no-JS, Lynx-legible rendering is both on-brand and a real accessibility
win, but it is a second design surface to maintain.

## 9. Motion and interaction

Sparingly. Decide where motion earns its place — probably live count updates and the liveness
feeds, probably nowhere else — and what the reduced-motion fallback is.

## 10. Deliverable expected back

To hand off cleanly, the session should produce:

- Name, positioning line, and tone decision.
- Type and colour systems, light and dark, with the ANSI-coexistence answer.
- Designed treatments for all seven components in §4.
- Layouts for at least the game page, the listing page, and the home page, at desktop and mobile.
- The full state matrix from §6, resolved.
- Accessibility decisions, including the plain-mode call.
- Anything in the system spec that the design work proves wrong — this brief assumes the system
  design is right, and design frequently discovers that it is not.
