# Handoff: Oversigt (Overblik) — Årsoversigt for medarbejder

## Overview
This is a redesign of the **Oversigt** page under **Min tid** in StatsTid (route `tid/oversigt`,
which currently renders a `Placeholder`). The goal was to make a medarbejder's time/absence
information *readily available* and **forward-planning**, not just a current-month snapshot.

The chosen design (**Direction E — Årsoversigt / Årsgrid**) is a year-at-a-glance matrix:
a row of current-balance tiles on top, then a months × categories table that shows accrued vs.
spent and what can be carried into next year. Four alternate explorations (A–D) are included in
the same reference file for context, but **E is the design to build**.

## About the Design Files
The files in `reference/` are **design references created in HTML/React (Babel JSX)** — a
prototype showing the intended look and behavior. They are **not** production code to copy
verbatim. The task is to **recreate Direction E in the StatsTid codebase** using its existing
stack and patterns:

- React 18 + TypeScript, Vite, react-router v6
- CSS Modules + CSS custom properties (tokens live in `frontend/src/styles/tokens.css`)
- The existing UI kit in `frontend/src/components/ui/` (Card, Badge, Button, Table styling, etc.)
- The app shell in `frontend/src/components/layout/` (Header, TopNav, Sidebar, AppLayout)

The prototype re-implements the shell and tokens as plain classes only so it can run standalone.
In the real app you already have these — **reuse them**; don't port the prototype's `kit.css`
or `colors_and_type.css`. Map the prototype's `st-*` classes to the real components/tokens.

> `reference/design-canvas.jsx` is **only** the pan/zoom canvas used to present options
> side-by-side. It is **not** part of the page — ignore it when implementing.

## Fidelity
**High-fidelity.** Colors, typography, spacing, borders, and the table structure are final and
follow the StatsTid design system (square corners, 1px hairline borders, oes-green, IBM Plex
Sans, 8px grid). Recreate it pixel-faithfully using the codebase's tokens and components.

The **numbers are illustrative seed data** (employee "Anna Berg"). Wire the real values from the
balance/rule engine; do not ship the hardcoded arrays.

---

## Screen: Årsoversigt (`tid/oversigt`)

### Purpose
The medarbejder opens this to answer, at a glance: *What are my balances right now, how did the
year accrue/deplete month by month, and what must I act on before year-end (afspadsering, afhold
ferie, brug seniordage) and what can be transferred?*

### Layout (within the standard app shell)
Render inside the existing `AppLayout`: Header (56px) → TopNav (Min tid active) → body with the
240px Sidebar (Registrering, **Oversigt** active) and the scrolling `main` (max-width 1200px,
32px padding). The page content, top to bottom:

1. **Page header row** — flex, space-between, align-items flex-end.
   - Left: `<h1>` **"Årsoversigt 2026"** (26px / 600) and a sub line
     **"Anna Berg · AC · OK24 · Norm: 147 timer"** (14px, `--color-text-secondary`).
     The norm is the employee's monthly norm (see Notes — consider showing the contractual
     37 t/uge instead, since monthly norm actually varies by working days).
   - Right: a **year switcher** — `← 2026 →` (ghost buttons, centered label min-width 64px).
     (NOT a month switcher — this is a whole-year view.)

2. **Current-balance tiles** — a 6-column CSS grid (`gap: 14px`), one tile per balance.
   Each tile: white, 1px border, 0 radius, padding 14px 16px; an uppercase label (11.5px / 500,
   `--color-gray-500`), a big value (25px / 700, tabular-nums) with a small unit, and a sub line
   (12px, secondary). Tiles, in order:
   | Label | Value | Unit | Sub |
   |---|---|---|---|
   | Flex saldo | +22,5 | t | optjent overtid |
   | Ferie | 22 | dage | saldo |
   | Omsorgsdage | 1 | dag | rest |
   | Seniordage | 3 | dage | rest |
   | Sygedage | 4 | dage | i år |
   | Barns sygedag | 1 | dag | rest |

3. **Year matrix** — inside a `Card` (flush body). A fixed-layout table: a 168px label column +
   12 month columns (Jan…Dec). Structure:
   - **Header row:** "2026" (left, label col) then `Jan Feb Mar … Dec` (right-aligned, gray-100
     bg, 2px bottom border). The **current month** column header (Mar / `NOW_I`) is the visual
     cue: `--color-info-light` background, `--color-info` text, a **3px `--color-info` top
     accent**, and a small **"Nu"** tag (9.5px uppercase) above the month name.
   - **Category groups**, each a full-width group header row (uppercase 12px / 600, 2px
     `--color-border-strong` bottom border, 16px top padding) followed by metric rows:
     - **Arbejdstid:** `Arbejdstid`, `Diff. fra norm` (sub-row)
     - **Ferie:** `Saldo (rest)`, `Afholdt`, `Kan overføres` (all sub-rows)
     - **Omsorgsdage:** `Saldo (rest)`, `Afholdt`, `Kan overføres`
     - **Seniordage:** `Saldo (rest)`, `Afholdt`, `Kan overføres`
   - **Cell semantics:**
     - The **current month** column is tinted `--color-info-light` down every data row.
     - **Future months** (after current) are projected/planlagt → text in
       `--color-text-secondary`. Past + current are faktisk → `--color-text`.
     - `Diff. fra norm` is **signed**: positive → `--color-success`, negative → `--color-error`.
     - `Kan overføres` values that are > 0 render in `--color-info` / 600 (e.g. Ferie Dec = 5).
     - Zero / not-applicable cells render an em-dash `–` in `--color-gray-400` (used heavily for
       absence months and for "Kan overføres" outside December).
   - Row hover tints the row `--color-gray-50` (label + non-highlighted number cells).

### Key data rule (must stay internally consistent)
**Arbejdstid = Norm + Diff. fra norm**, per month. In the prototype Arbejdstid is *derived* from
the norm and the diff so the columns always reconcile (`arbejdstid = NORM.expected + diffNorm`).
Preserve this when wiring real data — never show an Arbejdstid that doesn't equal norm + diff.

### Transfer / lapse rules expressed in the grid
- **Ferie:** of the remaining balance, up to **5 dage** (1 week) can be transferred to next
  ferieår; the rest must be afviklet before 31. dec. → "Kan overføres" shows 5 in December.
- **Omsorgsdage, Seniordage:** lapse at year-end (cannot be transferred) → "Kan overføres" = 0/–,
  i.e. a planning prompt to use them before 31. dec.

## Interactions & Behavior
The prototype is a **static hi-fi mock**; these are the intended behaviors to implement:
- **Year switcher** (← →): re-loads the matrix + tiles for the selected calendar year.
- **Current-month highlight**: computed from today's date (`NOW_I` = current month index), not
  hardcoded.
- **Month column → drill-in (recommended):** clicking a month should navigate to that month's
  skema (`tid/registrering` for that period). Not in the mock, but the natural affordance.
- **Past vs. future styling**: driven by current month, so it updates over time automatically.
- No animations beyond the existing 0.15s color/border transitions. Respect reduced-motion.

## State Management
- `year` (selected calendar year) → drives a fetch of the year's aggregates.
- Server data per employee per year:
  - `arbejdstid[12]`, `norm[12]` (or a single contractual norm), `diffNorm[12]`
  - per absence category: `saldo[12]`, `afholdt[12]`, `kanOverføres` (year-end figure)
  - current balances for the 6 tiles (flex saldo, ferie saldo, omsorgsdage, seniordage,
    sygedage YTD, barns sygedag rest)
- `nowMonthIndex` derived from the system date (only highlight when viewing the current year).

## Design Tokens (from StatsTid `tokens.css` — use the real ones, listed here for reference)
- **Brand:** `--color-primary #066b43`, hover `#055638`, pressed `#04412b`
- **Text:** `--color-text #343536`, `--color-text-secondary #6b6b6e`
- **Neutrals:** gray-50 `#f7f6f6`, gray-100 `#f3f2f2`, gray-200/border `#e8e6e6`,
  gray-300 `#cac8c8`, gray-400 `#a7a6a8`, gray-500 `#6b6b6e`, gray-600/strong `#55565a`
- **Status:** success `#0f766e`, error `#cc0000`, warning `#8a6a00`,
  info `#1a6a86` + info-light `#ddeaf3` (used for the current-month highlight)
- **Type:** IBM Plex Sans (400/500/600/700); mono = IBM Plex Mono. Body 16px, sub 14px,
  table cells 13px, tile values 25px, h1 26px. Sentence case. Danish, da-DK numbers (comma
  decimal, `t` for timer).
- **Radius:** `0px` everywhere. **Borders:** 1px hairlines. **No shadows, no gradients.**
- **Spacing:** 8px grid; card/main padding 24–32px; grid gaps 14–16px.
- One non-token color in the prototype: `#a7cfbf` (a light oes-green tint) used for "projected"
  bars in Directions B–D's forecast charts — not used in Direction E. Derive from primary if needed.

## Assets
None. No images, no icon library — the design is intentionally icon-free (text + color only),
per the StatsTid system. Directional affordances use `←` / `→` glyphs.

## Files (in `reference/`)
- `Oversigt redesign.html` — entry; loads React/Babel + the stylesheets + the scripts below.
- `oversigt.jsx` — **the design**. `DirectionE` + `YRow` + `Shell` are what you implement; the
  data lives in `BALANCES`, `YMONTHS`/`NOW_I`, `YEAR`, `NORM`. (A–D + `Forecast`/`FlexMeter`/
  `SegBar` etc. are the alternate explorations — reference only.)
- `oversigt.css` — component styles for the new pieces. The `ov-stat*` classes = the balance
  tiles; the `ov-y*` classes = the year matrix. Translate these into CSS Modules + tokens.
- `kit.css`, `colors_and_type.css` — the prototype's standalone port of the StatsTid shell +
  tokens. **Reference only** — use the real `tokens.css` and UI kit instead.
- `design-canvas.jsx` — presentation scaffold only; ignore.

## How to open the reference
Open `reference/Oversigt redesign.html` in a browser. It opens on a pan/zoom canvas with all
five directions; **Direction E (Årsgrid)** is the first artboard — click its expand icon (top-
right of the card) to view it full-screen.
