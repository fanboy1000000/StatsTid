# design-sync NOTES — StatsTid UI Kit

Repo-specific gotchas for future syncs of `frontend/src/components/ui/` (20 components) into the **StatsTid UI Kit** claude.ai/design project (`e0905347-e83e-4fe4-8363-ae956da5d5e1`). The existing **"StatsTid Design System"** project (`1591a575-…`) is a SEPARATE hand-built project — do NOT sync into it.

## Build setup (why the config looks the way it does)
- **Synth-entry mode.** The frontend is an APP, not a published library — there is **no `dist/` library entry and no built `.d.ts`**. The converter bundles directly from the source barrel: `--entry ./frontend/src/components/ui/index.ts --node-modules ./frontend/node_modules`. Re-run is the same command (see below).
- **`componentSrcMap` pins all 20** — with no `.d.ts` tree, discovery (`[ZERO_MATCH]`) finds nothing, so every component is pinned to its `src/components/ui/<Name>.tsx`. **Toast is exported as `ToastProvider`** (the barrel exports `ToastProvider` + `useToast`, no `Toast`) → the map key is `ToastProvider`, not `Toast` (else `[BUNDLE_EXPORT]`).
- **`cssEntry: src/styles/tokens.css`** — the converter APPENDS this into `_ds_bundle.css` (the styles.css closure). This ships the 57 token `var(--*)` definitions the component CSS modules reference (without it: `[TOKENS_MISSING]`, unstyled). NOTE: `global.css`/`utilities.css` are NOT shipped — only `tokens.css`. The components are CSS-module-styled so they render correctly; only the app-level body defaults (`global.css body{}`) are absent, which is fine for component cards.
- **`extraFonts`** points at `node_modules/@fontsource/ibm-plex-sans/{400,500,600,700}.css` → 24 `@font-face` rules + 49 woff/woff2 into `fonts/`. `--font-family` = `'IBM Plex Sans'`. Without it: `[FONT_MISSING]` (system-font fallback).
- **`dtsPropsFor` is HAND-WRITTEN** for all 20 (synth-entry can't extract real props — it falls back to `[key: string]: unknown` because the source prop interfaces aren't `export`ed). The bodies were lifted from the real source interfaces. **Re-sync risk:** these are a hand-maintained copy of the source props — if a component's props change, the `.d.ts` won't update automatically; re-verify against source on a re-sync (a proper fix would be a `tsc --emitDeclarationOnly` lib build of the ui-kit + an `index.d.ts` entry).

## Known render warns (triaged — do NOT treat as new on re-sync)
- **Spinner** — intentionally tiny (a CSS spin border); `Sizes`/`InlineWithLabel` cells render small. Legitimate.
- **Tooltip** — Radix, hover/focus-driven; the bubble portals on hover and does NOT render in a static capture. The `Trigger` cell shows the styled trigger only (honest). Not a failure.
- **ToastProvider** — a context provider with NO static visual; toasts fire imperatively via `useToast()`. The `Provider` cell is an honest typographic explanation. Not a failure.
- **Select / DropdownMenu** — the option list / menu is PORTALLED and only mounts on click, so static captures show the closed trigger (Select shows the selected value / placeholder; DropdownMenu shows the trigger). DropdownMenu wraps Radix `Root` with no `open`/`defaultOpen` passthrough → cannot render open statically.
- **Overlay portals** — `Dialog` (Radix) and `Drawer` (scratch `createPortal`, `if(!open) return null`) BOTH portal to `document.body`. Rendered `open={true}` with `cfg.overrides.{Dialog,Drawer} = {cardMode:single, viewport}` so the open panel fills the card. `Drawer` requires `ariaLabel`; `Dialog` open-prop is `open`+`onOpenChange`, `Drawer` is `open`+`onClose`.
- **Grid overflow** — FormField/Input/Label/Textarea/Table render wider than a grid cell → `cardMode:column` (one story per row). DropdownMenu/Dialog/Drawer → `cardMode:single`.

## Component API facts (from source)
- `Checkbox`/`Radio` are NOT native inputs (custom SVG indicator); `checked`+`onChange`+`label`+`id` all required (`onChange` non-optional → no-op in previews). They render their own `<label>` — don't wrap in `Label`.
- `Input`/`Textarea` require `id`; the DS prop is `error?: boolean` (red border). Use `defaultValue` to render filled.
- `FormField` `error` is a STRING (the message); the inner `Input` `error` is a BOOLEAN (the border) — independent. `Select` is Radix with an `options[]` array + `value`/`onValueChange` (not `<option>` children).
- `Table` is `headers: string[]` + raw `<tr>/<td>` children (NO `Table.Row`). `Tabs` is a `tabs[]` array prop, uncontrolled (`defaultValue`). `Card` has a `header?` slot (not compound). None of Card/Table/Tabs are React-compound (`X.Header`).

## Re-sync risks (the watch-list)
- **`dtsPropsFor` drift** (above) — the #1 watch item; hand-maintained vs source.
- **Synth-entry `.d.ts`** — weaker than a real build; if the ui-kit ever gets a lib build (`vite lib` + `vite-plugin-dts`), drop `componentSrcMap`/`dtsPropsFor` and point `--entry` at the built `dist` for proper types.
- **`@fontsource` path** — `extraFonts` reads from `frontend/node_modules/@fontsource/ibm-plex-sans/*.css`; if the font package version/layout changes, re-point.
- **`global.css` not shipped** — deliberate (only `tokens.css` via `cssEntry`); if a future component relies on a `global.css` base style, add it to a combined cssEntry.
- Tooltip/ToastProvider/DropdownMenu-open are interaction/provider-only → permanently trigger-or-explanation cells unless the components grow a static/`defaultOpen` path.

## Re-sync command
```sh
cp -r "<skill-base>"/{package-build.mjs,package-validate.mjs,package-capture.mjs,resync.mjs,lib,storybook} .ds-sync/    # refresh staged scripts
node .ds-sync/resync.mjs --config .design-sync/config.json --node-modules ./frontend/node_modules \
  --entry ./frontend/src/components/ui/index.ts --out ./ds-bundle --remote .design-sync/.cache/remote-sync.json
```
(playwright 1.61.0 pins chromium build 1228, already in `~/.cache/ms-playwright`; install `playwright@1.61.0` into `.ds-sync` for the render check.)

## Drift gate (PAT-011) — refresh the fingerprint after EVERY sync
`tools/check_design_sync.py` is a CI HARD-FAIL (in the `docs-consistency` job) that fires when the design-system source (ui-kit `*.{tsx,module.css}` + `index.ts` + `tokens.css` + this `.design-sync/config.json`) changes vs the committed `.design-sync/.synced-fingerprint.json`. So after the upload completes:
```sh
python tools/check_design_sync.py --update    # rewrite the fingerprint from the current source
git add .design-sync/.synced-fingerprint.json  # commit it WITH the design-system change
```
A ui-kit/token/config change that lands without refreshing + committing the fingerprint fails CI — that's the "code ↔ Claude Design can't drift" enforcement. The gate is LF-normalized (no `.gitattributes`; mixed CRLF/LF working tree) and checks fingerprint==source — it does NOT prove the upload reached claude.ai (CI can't auth), so only run `--update` after a real sync. `--selftest` proves the gate is live. (Optional hygiene: an `eol=lf` `.gitattributes` for the scoped files — deferred to avoid working-tree renormalization churn; the hasher normalization is the real fix.)
