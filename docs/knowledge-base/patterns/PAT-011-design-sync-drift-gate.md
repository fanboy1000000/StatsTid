# PAT-011 — Design-sync drift gate (committed fingerprint; code ↔ Claude Design can't drift)

| Field | Value |
|-------|-------|
| **Status** | approved |
| **Sprint** | post-S101 (the /design-sync follow-up — after the first design-system import to claude.ai/design) |
| **Domains** | CI/CD, Frontend, Tooling |
| **Tags** | design-system, claude-design, drift-gate, fingerprint, single-source-of-truth, ci-lint, lf-normalize |

## The pattern
The repo's design system (the ui-kit components + the design tokens) is the single source of truth that BOTH the shipped app (Claude Code, in-repo) AND the claude.ai/design project (synced via the `/design-sync` skill) derive from. The risk: someone changes a component or token in the repo but forgets to re-sync Claude Design → the design tool silently lags the code. A committed **fingerprint** + a **CI gate** make that drift impossible to merge unnoticed.

## How it works
- `tools/check_design_sync.py` hashes the in-scope source (LF-normalized → deterministic across checkout EOL) and, in `--check` (CI default), HARD-FAILS if it no longer matches `.design-sync/.synced-fingerprint.json` (a committed `{relpath: sha256}` map), reporting the added/removed/changed files. `--update` refreshes the fingerprint; `--selftest` proves the gate fires on injected drift (exits 1 by design).
- **Scope = the code↔design parity surface**: `frontend/src/components/ui/*.{tsx,module.css}` + `ui/index.ts` + `frontend/src/styles/tokens.css` + **`.design-sync/config.json`** (the config — esp. the hand-written `dtsPropsFor` — is an uploaded-bundle INPUT; a config-only change drifts the synced `.d.ts` with no source change, so it MUST be in scope). OUT: `global.css`/`utilities.css` (not shipped — no ui module CSS `@import`s them, every `var(--*)` resolves to `tokens.css`), `__tests__`, and Design-only presentation (`previews/*.tsx`, `conventions.md` — they change the Design cards, not the code).
- Wired into the `docs-consistency` CI job (next to `check_docs.py` + PAT-010's `check_endpoint_contracts.py`).

## The refresh loop (load-bearing)
After every `/design-sync` (which rebuilds + uploads to the StatsTid UI Kit project), run `python tools/check_design_sync.py --update` and commit `.design-sync/.synced-fingerprint.json` with the change. A developer who edits the ui-kit therefore MUST re-sync Design to clear CI — that is the "can't drift apart" enforcement. The `.design-sync/` sync inputs (config, NOTES, conventions, previews, fingerprint) are committed so the sync is reproducible from the repo by any machine.

## Honest framing (do NOT over-sell)
CI cannot authenticate to claude.ai, so the gate verifies only that the committed fingerprint MATCHES the current source — it CANNOT prove the upload actually reached claude.ai. The discipline is: run `--update` only as the final step of a real sync. Same process-trust model as PAT-010's contract gate (a contract test can be registered without being meaningful). The gate forces the conscious "I re-synced" action; it is not an upload verifier.

## Why a fingerprint, not the bundle anchor
The uploaded `ds-bundle/_ds_sync.json` (`sourceHashes`) is keyed by bundle-artifact paths (the *converted* output) + needs the gitignored node build to regenerate, and has no `tokens.css` entry (tokens land in `styleSha` over the post-append bundle) — so it can't serve as a CI parity check without re-running the build CI can't do. A standalone Python hasher over the repo source is the correct, simplest design.

## Implementation notes
- **LF-normalize before hashing** — there is no `.gitattributes` enforcing EOL and the working tree is mixed (some `.tsx` CRLF, `tokens.css` LF); a raw byte hash would be machine-dependent and CI would false-fire on the first run. The hasher strips `\r\n`/`\r` → `\n`. (An `eol=lf` `.gitattributes` is optional defense-in-depth; the hasher normalization is the actual fix — do not rely on gitattributes existing.)
- The map-diff catches added/removed files inherently (a new component is a key not in the fingerprint); the stored `count` is a sanity check.
