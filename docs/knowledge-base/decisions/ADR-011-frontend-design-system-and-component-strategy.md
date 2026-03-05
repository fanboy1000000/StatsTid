# [ADR-011] Frontend design system and component strategy

| Field | Value |
|-------|-------|
| **ID** | ADR-011 |
| **Category** | decision |
| **Status** | approved |
| **Sprint** | Sprint 8 (pre-planning) |
| **Date** | 2026-03-04 |
| **Domains** | Frontend |
| **Tags** | frontend, design-system, shadcn, css-modules, react, accessibility |

## Context
StatsTid is a Danish state sector enterprise SaaS platform. The frontend must feel native to institutional users and comply with accessibility standards. Sprint 8 will expand the minimal Sprint 2 scaffold (5 pages, 4 hooks) to cover all 26 backend API endpoints with role-based navigation, approval workflows, admin panels, and local config management.

A design language and component strategy was needed that balances institutional aesthetics, accessibility, developer control, and implementation speed.

## Decision

### Design Reference
The visual language is inspired by **Det Fælles Designsystem** (designsystem.dk) — the Danish government's shared design system. We use their aesthetic as reference only: layout, spacing, typography hierarchy, component structure, and color palette. We do NOT use their code, CSS classes, or component implementations.

The aesthetic is: strictly utilitarian Scandinavian government design. High trust, high legibility, institutional clarity. No decoration for its own sake. Every element earns its place.

### Design Tokens (from designsystem.dk)

**Typography**: IBM Plex Sans (open source, SIL Open Font License). Fallback: system-ui, sans-serif.

| Level | Size (desktop) | Weight | Line Height |
|-------|---------------|--------|-------------|
| H1 | 48px (32px mobile) | Bold (700) | 56px (40px mobile) |
| H2 | 32px (24px mobile) | Semi-bold (600) | 40px (32px mobile) |
| H3 | 24px (22px mobile) | Semi-bold (600) | 32px (28px mobile) |
| H4 | 20px (18px mobile) | Semi-bold (600) | 28px (24px mobile) |
| H5 | 16px | Semi-bold (600) | 24px |
| H6 | 14px | Medium (500) | 20px |

**Color palette**:

| Token | Hex | Usage |
|-------|-----|-------|
| primary | #0059B3 | Primary actions, links, active states |
| primary-dark | #004993 | Hover states |
| primary-darker | #003972 | Active/pressed states |
| text | #1A1A1A | Body text, headings |
| gray-100 | #F5F5F5 | Page backgrounds, table stripes |
| gray-200 | #DCDCDC | Borders, dividers |
| gray-300 | #BFBFBF | Disabled states |
| gray-400 | #8E8E8E | Placeholder text |
| gray-500 | #707070 | Secondary text |
| gray-600 | #454545 | Focus outlines |
| white | #FFFFFF | Card backgrounds, input backgrounds |
| success | #358000 | Positive states, approved |
| success-light | #DDF7CE | Success backgrounds |
| error | #CC0000 | Error states, rejected |
| error-light | #FFE0E0 | Error backgrounds |
| warning | #FEBB30 | Warning states, pending |
| warning-light | #FFEECC | Warning backgrounds |
| info | #1B86C3 | Informational states |
| info-light | #E2F2FB | Info backgrounds |
| link | #004D99 | Links |
| link-visited | #800080 | Visited links |

**Spacing**: 8px baseline grid (--space-1: 4px, --space-2: 8px, --space-3: 12px, --space-4: 16px, --space-6: 24px, --space-8: 32px, --space-10: 40px, --space-12: 48px).

**Corners**: 0px border-radius (sharp corners, per government aesthetic). Exception: toast notifications may use 2px.

**Borders**: 1px solid, typically gray-200 for structural borders, gray-600 for focus outlines.

### Technology Stack
- **React 18** + TypeScript + Vite
- **ShadCN/ui** (Radix primitives) for interaction-complex components only
- **CSS Modules** for component-scoped styles
- **Design tokens** via CSS custom properties in a shared `tokens.css`
- **IBM Plex Sans** loaded via `@fontsource/ibm-plex-sans`

### Component Strategy

**Build from scratch** (CSS Modules + React) — components that are visually simple and structurally trivial. Full control over designsystem.dk aesthetic:

| Component | Rationale |
|-----------|-----------|
| Button | Simple `<button>` with CSS variants |
| Input | Simple `<input>` with label + error layout |
| Textarea | Simple `<textarea>` |
| Checkbox | Custom-styled `<input type="checkbox">` |
| Radio | Custom-styled `<input type="radio">` |
| Badge | Inline `<span>` with color variant |
| Alert / notification banner | Static feedback block |
| Card | `<div>` with border and padding |
| Label | `<label>` with consistent styling |
| Spinner / loading state | CSS animation |
| Divider | `<hr>` with token-based styling |
| Table | `<table>` with designsystem.dk row striping |
| Navigation / Sidebar | Role-based nav, core to app |
| FormField | Wrapper: label + input + error message + required indicator |

**Use ShadCN as structural base** (restyle completely) — components with non-trivial interaction patterns that benefit from Radix primitives:

| Component | Rationale |
|-----------|-----------|
| Select / Combobox | Complex keyboard navigation via Radix |
| Modal / Dialog | Focus trap handling |
| Tooltip | Positioning logic |
| Popover | Positioning logic |
| Dropdown Menu | Keyboard nav, ARIA roles |
| Toast / notification | Portal rendering, timers |
| Accordion | Collapse/expand state management |
| Tabs | Keyboard nav, ARIA tablist/tab/tabpanel |
| Date picker | Calendar positioning + keyboard nav |

All ShadCN components are restyled to match the designsystem.dk aesthetic — the Radix default styles are fully overridden.

## Rationale
- **Reference-only approach**: designsystem.dk's implementation is vanilla HTML/CSS/JS with DKFDS-specific class names that would fight React's component model. Using the aesthetic without the code gives institutional consistency without framework conflicts.
- **Scratch-built primitives**: Button, Input, Card etc. are trivial to build and benefit most from complete style control. No dependency overhead for simple elements.
- **ShadCN for complex interactions**: Focus traps, keyboard navigation, portal rendering, and ARIA compliance are hard to get right. Radix primitives handle these correctly; we only restyle the visual layer.
- **CSS Modules over Tailwind**: CSS Modules provide component-scoped styles without utility class proliferation. The designsystem.dk aesthetic has very few variants — utility-first frameworks add complexity without proportional benefit for this design language.
- **Design tokens via CSS custom properties**: Enforces consistency across all components (scratch-built and ShadCN-restyled). Single source of truth for colors, spacing, typography.

## Consequences
- All frontend components must consume design tokens from `tokens.css` — no hardcoded colors/spacing
- ShadCN components must be fully restyled before use — default Radix/ShadCN styles must not leak
- IBM Plex Sans must be loaded as a project dependency, not from Google Fonts (data sovereignty)
- Accessibility must meet WCAG 2.1 AA — inherited from designsystem.dk's requirements
- The UX Agent must follow this ADR when creating or modifying any frontend component

## Agent Guidance
- UX Agent: All new components must use design tokens from `tokens.css`. Scratch-built components use CSS Modules. ShadCN components must have all default styles overridden.
- UX Agent: Follow designsystem.dk's spacing (8px grid), typography (IBM Plex Sans), and color palette exactly. Sharp corners (0px radius). Thin borders (1px solid).
- UX Agent: Role-based navigation must respect the 5-role hierarchy (GlobalAdmin > LocalAdmin > LocalHR > LocalLeader > Employee). See ADR-009 for scope model.
