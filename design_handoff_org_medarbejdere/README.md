# Handoff: Organisation & medarbejdere — "Enhedsspor" model

## Overview
An admin tool for managing a government org hierarchy **and** the people inside it, reconciling two overlapping trees that don't nest cleanly:

- **The org-unit tree** — Ministerområde › Organisation › Direktion › Område › Kontor › Team › Enhed
- **The management/reporting tree** — each employee has exactly one *primary leader*; a unit can have several *peer (sideordnede) leaders*; a leader's own leader usually sits one unit up.

This design resolves that tension by making the **org-unit tree the single spine ("Enhedsspor")** and treating reporting as *derived* from it. It was chosen over two alternatives (a reporting-spine model and a dual-tree model). The core UI is a left org-tree + a right detail panel that renders a single recursive, foldable structure tree (units → leaders → their employees → child units), plus full CRUD, search, scope filtering, cross-unit leader exceptions, and temporary leader replacements (vikar).

## About the Design Files
The files in this bundle are **design references created in HTML** — a working prototype showing intended look and behavior, **not production code to copy directly**. The task is to **recreate this design in the target codebase's existing environment** (React, Vue, etc.), using its established component library, state patterns, and data layer. If no frontend environment exists yet, choose an appropriate framework and implement there.

The prototype is built as a "Design Component" — a custom HTML runtime (`support.js`) with a template + a `class Component` logic class. **Do not port the runtime.** Read it for the data model, layout, and behavior, then reimplement idiomatically. All styling is inline; there is no stylesheet to lift.

## Fidelity
**High-fidelity.** Final colors, typography, spacing, copy (Danish), and interactions are all intentional and should be matched closely. Exact values are in the Design Tokens section. The one caveat: this targets a real design system — the **StatsTid UI Kit** (IBM Plex Sans, oes green `#066b43`, **0px border-radius / square corners**, hairline borders). In the prototype, design-system components (Button, Drawer, Dialog, Select, Alert) are mounted from that kit; in your codebase, use that same kit's real components and only hand-style the surrounding layout.

---

## The Data Model (most important section)

Two entity types. This is the heart of the design — get this right and the UI follows.

### Unit
```
{ id, type, name, parentId, leaderIds: [personId, ...] }
```
- `type` ∈ `ministeromrade | organisation | direktion | omrade | kontor | team | enhed` (fixed ordered hierarchy).
- `parentId` → another unit (null only for `ministeromrade`).
- `leaderIds` → ids of people who lead this unit. **A leader must be a person whose own `unitId` is this unit** (a leader is a member of the unit they lead). Multiple ids = multiple *sideordnede* (peer) leaders.

### Person (user)
```
{ id, name, title, email, unitId, leaderId, vikar }
```
- `unitId` → the unit the person belongs to.
- `leaderId` → their **primary leader** (another person), or `null` = øverste leder (apex, no manager).
- `vikar` (optional) → temporary replacement: `{ leaderId, from, to }` (ISO `YYYY-MM-DD`) or `null`. **Vikar is a pure visibility flag — it changes no reporting data.**

### The key derived rules
- **A unit's leaders** = `unit.leaderIds` filtered to people actually in that unit.
- **An employee's column** = grouped under whichever of the unit's peer leaders equals their `leaderId`.
- **"Refererer opad til" (derived upward reference)** = the distinct set of `leader.leaderId` people across a unit's leaders — i.e. the leader-of-leader, which usually lives one unit up. Shown as ONE derived strip, never mixed into the member list.
- **Cross-unit exception** = a member whose `leaderId` is **not** one of their own unit's leaders ("primær leder uden for enheden"). Surfaced with an amber tag + a one-click "Ret" that reassigns them to a leader in their own unit.
- **Leaderless unit** = a unit with members but `leaderIds` empty → amber note + "Tildel leder".
- **Member counts**: a unit's badge count = all people in that unit OR any descendant unit (deep count).

See `org-data.js` for a complete, realistic seed dataset (one ministerområde, three styrelser, multi-leader unit "Vejledning" with Jens + Trine, cross-unit exception "Carl Storm", leaderless team "Kontrol", and a seeded vikar on Jens Kofoed).

---

## Screens / Views

There is **one screen**, a full-height app (`100vh`, no page scroll) with three regions:

### 1. Header (top bar, ~56px, white, 1px bottom border `#e8e6e6`)
- **Left**: 32×32 green (`#066b43`) "St" logo tile; title "Organisation & medarbejdere" (15.5px/600) + subtitle "Enhedsspor — organisationen er rygraden" (11.5px `#6b6b6e`).
- **Left, after title**: **Afgrænsning** dropdown trigger (bordered box, ~210px min) — label "AFGRÆNSNING" (9.5px/700 uppercase `#a7a6a8`) over the current scope summary (13px/600). Opens a 340px popover listing each ministerområde with its organisations as checkboxes (tri-state parent: ✓ all / – some / empty none). "Vælg alle" / "Ryd" / "Anvend". Scope = the set of organisation ids currently in view; the tree and search are filtered to it.
- **Right**: green primary **Søg** button (opens search overlay).

### 2. Left sidebar — org tree (width 330px, white, right border)
- Header label "ORGANISATIONSSTRUKTUR" (11px/700 uppercase `#6b6b6e`).
- Indented expandable tree of **units only** (no people here). Each row: caret (▸/▾) · 8px square color dot (per-type accent) · name (13.5px, 600 when selected) · spacer · deep member-count pill (`#f3f2f2` bg). Selected row: left border 3px `#066b43` + bg `rgba(6,107,67,0.08)`. Indent = 8 + depth·15 px. Ministerområde rows only show organisation children that are in scope.

### 3. Right detail panel (flex-1, bg `#faf9f9`, scrolls; inner max-width 1060px, padding 18px 28px 64px)
Top-to-bottom:

**a. Back/forward** — two ghost buttons "‹ Tilbage" / "Frem ›" (history stack of selected unit ids), disabled at ends.

**b. Breadcrumb path** — `selPath`, the unit ancestry joined by "   ›   " (12.5px `#6b6b6e`).

**c. Title block**
- Row 1: type chip (10/11px uppercase, per-type accent color on tint bg) + unit name `<h1>` (25px/600, ellipsis).
- Row 2 (action row, wraps): **`+ <ChildType>`** (secondary; label is the child unit type, e.g. "+ Kontor", "+ Team"; disabled on leaf `enhed`), **`+ Medarbejder`** (primary; disabled on ministerområde), a 1px vertical divider, **Rediger** (secondary), **Slet** (ghost; hidden/disabled on ministerområde). Use non-breaking or nowrap labels so buttons don't wrap.

**d. "Refererer opad til" strip** (only if derived upward refs exist) — green-tinted band (`#eef4f1`, border `#d6e6dd`); label + clickable person chips (avatar + name + "title · unit"); clicking navigates to that person's unit.

**e. Struktur section** (the centerpiece — a `<section>` white card, 1px border)
- **Sticky toolbar** (`position:sticky; top:0`, white, subtle shadow): title "Struktur" + count label ("N medarbejdere · M underenheder"); right side has **Vis org. / Skjul org.** (expand-/collapse-all descendant units) and **Skjul medarbejdere / Vis medarbejdere** (global hide of all people). Buttons stay present (disabled, not removed) where not applicable, so the toolbar never changes shape.
- **Recursive node list.** `walkUnit(unitId, depth)` emits a flat ordered list of typed rows; indent = `14 + depth·20 (+extra)` px. Node types:
  - **Med-group header** ("MEDARBEJDERE" + count, foldable per-unit): 11px/700 uppercase on `#fcfcfb`. Collapsing hides that unit's people only (leaders + employees), leaving its child units visible.
  - **Leader row**: caret (collapses that leader's reports) · 30px green avatar (initials) · name (13.5/600) + title · green "LEDER" badge · "N medarb." · "Rediger ›". If absent now (today within `vikar` range): an amber "FRAVÆRENDE" badge by the name and a `#8a6a00` line "Vikar: <name> · <range>". If the person is themselves a stand-in: a blue "Vikar for <leader>" tag (`#1a6a86` on `#e3eef2`). Left border 3px `#066b43`, bg `#fbfdfb`.
  - **Employee row** (nested under their leader, indented +34, left rail 2px `#e9ece9`): 26px grey avatar · name (13/500) + title · "Rediger ›". Cross-unit member instead gets left rail 2px `#f0d98a`, bg `#fffdf5`, an amber "Leder uden for enheden: <name>" tag and an underlined "Ret" action. If a stand-in: blue "Vikar for …" tag.
  - **Note row** (leaderless unit): amber band (`#fef9c3`, border `#f3e8a0`) with the upward-reference summary + "Tildel leder" action.
  - **Child unit row**: caret (expands inline) · color dot · name (14/600) · type chip · leader names (`#a7a6a8`) · deep count pill · "Åbn ›" (makes it the selected unit). Recurses when expanded.

### Overlays
- **Search overlay** — full-screen scrim (`rgba(31,32,33,0.32)`), centered palette (max-width 680px) anchored ~84px from top. Autofocused **uncontrolled** input (see Gotchas). Results split into two collapsible sections, **ENHEDER** and **MEDARBEJDERE**, each: bold black uppercase header (13px/700) on `#f3f2f2` band, a **green fold caret** (▾/▸), and a **green count pill** (white text on `#066b43`). Unit results show name + type chip + full path + deep count; clicking selects the unit. Person results show avatar + name + (Leder badge) + title + full unit path; clicking jumps to their unit AND opens their edit drawer. Idle state (empty query) shows a hint; no-results shows a message. Search spans the whole org but respects Afgrænsning (footer note when scoped). Opens via the Søg button or `/` key; closes on Esc or scrim click.
- **Unit drawer** (StatsTid Drawer) — edit/create a unit: name field; a checkbox list of the unit's own members to designate as (peer) leaders.
- **Person drawer** (StatsTid Drawer) — create/edit a person: Navn (required), Titel, E-mail, **Organisation** (Select, required), **Placering** (unit Select within that org — changing it *moves* the person, required), **Nærmeste leder** (Select, required unless apex). Below it a checkbox **"Vis ledere uden for enheden"** that widens the leader Select from just the unit's own leaders to **every leader in the organisation** (each labeled with their unit) — this is how you set the cross-unit exception; it auto-enables when editing someone whose leader already sits outside their unit. A **"Øverste leder — ingen overordnet"** checkbox (apex). A **"Er leder af <unit>"** promote checkbox. When the person is a leader, a **"Vikar ved fravær"** section: a leader picker ("Ingen vikar" + every leader in the org), and From/To `date` inputs, with a "Fjern vikar" link. Footer: Slet (danger, edit only) / Annuller / Gem. Inline validation with red messages.
- **Delete confirm** (StatsTid Dialog) — guarded: a unit can't be deleted while it has child units or members; a person can't be deleted while they lead others (toast error otherwise).
- **Toast** (StatsTid Alert, fixed top-center) — success/error, auto-dismiss ~2.8s.

---

## Interactions & Behavior
- **Selecting a unit** (sidebar row, "Åbn ›", breadcrumb, search result, derived-ref chip) sets it as the detail subject and pushes onto the back/forward history.
- **Expand/fold**: sidebar tree (per unit), Struktur child units (per unit), per-unit MEDARBEJDERE groups, per-leader report lists, search sections, plus global Vis org./Skjul org. and Skjul/Vis medarbejdere. These are independent pieces of UI state.
- **Reassigning primary leader**: via the person drawer's Nærmeste leder select (optionally widened cross-unit), or the one-click "Ret" on a cross-unit exception (assigns the first leader of the person's own unit).
- **Promote to leader**: the person drawer checkbox adds/removes the person from their unit's `leaderIds` on save; also synced when a person moves units.
- **Vikar**: set in the person drawer; shows as FRAVÆRENDE (only when today ∈ [from,to]) + "Vikar: …" on the leader and "Vikar for …" on the stand-in. No reporting data changes.
- **Search**: opens overlay; live filtering as you type (multi-token AND match on unit names / person name+title+email); respects Afgrænsning.
- **Keyboard**: `/` opens search (when not typing in a field); `Esc` closes search.
- Hover states throughout: rows tint to `#f3f8f5` (people) or `#faf9f9` (units); buttons follow StatsTid kit.

## State Management
Single component state (reimplement with your store/hooks):
- `units` (map id→Unit), `users` (map id→Person) — the editable dataset.
- `selectedId` (current unit), `history` + `histIndex` (back/forward).
- `expanded` (sidebar tree open map), `treeOpen` (Struktur child-unit open map), `medClosed` (per-unit people-group collapsed map), `lCollapse` (per-leader report collapsed map), `showPeople` (global people hide).
- `scope` (array of in-view organisation ids).
- `search`, `searchOpen`, `foldUnits`, `foldPeople` (search overlay).
- `drawer` (`{ kind:'unit'|'person', mode:'add'|'edit', id?, parentId?, childType?, form, errors }`), `confirm` (`{ kind, id, name }`), `toast` (`{ msg, variant }`), `afgOpen` (scope popover), `seq` (id counter for new entities).

CRUD operations: create/edit/delete unit; create/edit/delete person; add child unit; move person (change unit); promote/demote leader; set/clear vikar; fix cross-unit exception. All mutate the in-memory maps and re-derive views. In production, back these with your API/data layer.

---

## Design Tokens

**Brand / kit** (StatsTid UI Kit — use the kit's tokens where available):
- Primary (oes green): `#066b43`; tints used: `#e1efe9`, `#eef4f1`, `#f3f8f5`, `#fbfdfb`, `rgba(6,107,67,0.08)`.
- Border radius: **0px everywhere** (square corners — a brand trait).
- Font: **IBM Plex Sans** (`var(--font-family)`).

**Neutrals**: text `#343536` / strong `#1f2021`; secondary `#6b6b6e`; muted `#a7a6a8`; faint `#bdbcbc`/`#cac8c8`. Surfaces: white, `#faf9f9`, `#fcfcfb`, `#f3f2f2`. Borders: `#e8e6e6`, `#f3f2f2`, `#e3e1e1`.

**Status / accent**:
- Amber (warnings, cross-unit, leaderless, absent): bg `#fef9c3` / `#fffdf5` / `#fef3c7`, border `#f3e8a0` / `#f0d98a`, text `#7a5b00` / `#8a6a00`, FRAVÆRENDE badge bg `#c0791a`.
- Blue (vikar-for tag): text `#1a6a86` on bg `#e3eef2`.

**Per-unit-type accent / tint / order** (from `org-data.js` `ACCENT`/`TINT`/`ORD`):
- ministeromrade `#55565a` / `#ececed`; organisation `#066b43` / `#e1efe9`; direktion `#1a6a86` / `#e3eef2`; omrade `#0f766e` / `#e2efed`; kontor `#8a6a00` / `#f4eed8`; team `#5a6b86` / `#eaedf3`; enhed `#86705a` / `#f2ece6`.

**Type scale** (px): h1 25/600; section titles 15/600; row names 13–14.5/500–600; labels 11/700 uppercase (letter-spacing ~0.05–0.08em); meta/help 11–12.5/`#6b6b6e`; chips 9–10/700 uppercase.

**Spacing**: row padding ~6–11px vertical; section padding 11–18px; card/section gap 18px; sidebar indent step 15px; struktur indent step 20px.

**Danish copy** (use verbatim): Afgrænsning, Organisationsstruktur, Struktur, Vis org. / Skjul org., Vis/Skjul medarbejdere, + Medarbejder, Rediger, Slet, Refererer opad til, Primær leder uden for enheden, Ret til leder her / Ret, Tildel leder, Nærmeste leder, Vis ledere uden for enheden, Øverste leder — ingen overordnet, Er leder af …, Vikar ved fravær, Ingen vikar, Fjern vikar, Fraværende, Vikar for …, Åbn ›, Rediger ›, Søg, Enheder, Medarbejdere.

## Assets
None. No images or icon files — avatars are CSS initials, dots/chips are CSS, carets are unicode glyphs (▸ ▾ ‹ › ↑ ⌕ ✓). Fonts come from the StatsTid kit (IBM Plex Sans).

## Files
- `Model A - Enhedsspor.dc.html` — the full prototype (template markup + `class Component` logic). Read the logic class for the exact derivation rules and the template for layout/styling.
- `org-data.js` — the seed dataset + the static maps (`LABEL`, `SHORT`, `CHILD`, `ACCENT`, `TINT`, `ORD`) and the unit-type hierarchy. **Port this dataset and these maps directly** — they encode the domain rules.
- `support.js` — the prototype's custom runtime. **Reference only; do not port.** Explains how the `{{ }}` template + `renderVals()` wiring works if you need to read the prototype closely.

> To view the prototype: it expects the StatsTid UI Kit bundle at `_ds/statstid-ui-kit-<id>/` (not included here — it's the design system you already target). The three files above are the design-specific work.
