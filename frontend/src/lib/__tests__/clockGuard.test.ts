/// <reference types="vite/client" />
//
// S143 / TASK-14302 — the frontend's first clock guard.
//
// WHAT THIS IS, IN PLAIN LANGUAGE. S142 moved every BUSINESS DATE in StatsTid onto the
// Europe/Copenhagen calendar day (ADR-041): a business date is a fact about Danish employment
// law (an agreement's effective date, an entitlement rule's start), and must read the SAME day
// no matter what timezone the browser or server happens to be running in. The backend got a
// guard that fails the build if a business date is ever again derived from the UTC or
// machine-local clock. The frontend — where the ORIGINAL bug was born (`new Date()` reads the
// BROWSER's local clock, which is wrong precisely when it disagrees with Copenhagen) — had no
// such guard at all. Worse, `npm run lint` turned out to be a hand-typed list of 34 files with
// an ESLint config carrying no general rules, so the three files that actually caused the S142
// defect (`SkemaPage.tsx`, `ArsoversigtPage.tsx`, `SkemaGrid.tsx`) were not linted by anything.
// This file is that guard.
//
// WHY A REAL PARSER, NOT A REGEX. A pattern-matcher looking for the text `new Date()` is wrong
// in both directions: it MISSES `new Date` (no parens), `new Date ()` (a space),
// `new /* comment */ Date()`, and a callee wrapped in parentheses, a TS `as` cast, an
// angle-bracket type assertion, `satisfies`, a non-null assertion, or the right side of a comma
// expression — six DIFFERENT ways to spell "this identifier, unchanged, at runtime" (see B below
// — the parser found five more of these than the first version imagined, executed one at a
// time) — all of which are the exact same zero-argument call the JavaScript grammar allows; and
// it FALSE-POSITIVES on `new Date()` sitting inside a string literal, a comment, a plain
// template's text, a regex literal's pattern, or JSX text, none of which execute anything. The
// backend's C# version of this guard (`BusinessDateCalendarGuardTests.cs`) hand-rolled a lexer for
// exactly these reasons and took five review passes to get right — because a hand-rolled lexer
// has to reinvent string/comment/template boundaries a real parser already gets right for free.
// `typescript` is already a devDependency (`package.json:40`), so this guard asks the ACTUAL
// TypeScript parser to build an AST and walks it for one shape: a `new` expression whose callee,
// once any wrapping "transparent" expressions are stripped away, is the identifier `Date`, with
// an empty or absent argument list. That check is immune to every quoting/comment/template hazard
// above simultaneously, because none of them change the AST shape of the code they surround —
// only the wrapper hazards change the SHAPE of the callee itself, which is why they need their
// own unwrap step (`unwrapTransparentWrapper`) rather than falling out of "parse, don't
// pattern-match" for free. That unwrap step needed TWO rounds to get right, which is exactly the
// backend guard's five-review-pass history repeating here — see B below for why that recurrence
// is the nature of this kind of check, not a one-off miss.
//
// WHY `import.meta.glob`, NEVER `node:fs`. `tsconfig.json:18` pins `"types": ["vitest/globals"]`,
// which deliberately excludes Node's ambient types from `src/**` — a `node:fs` import would fail
// to resolve under `tsc`, which is exactly the check `npm run build` runs. Vite's own file-glob
// primitive reads the SAME files without needing Node's `fs` types: the triple-slash reference at
// the top of this file pulls in `vite/client`'s ambient typing for `import.meta.glob` alone, which
// is enough for `tsc` to accept the calls below without touching `tsconfig.json`.
//
// THE LANDING MECHANISM (why this guard does not turn CI red the moment it merges). Seven sites
// in the tree today read the browser's local clock this way, spread across four sibling tasks
// that migrate them concurrently. If this guard simply failed on all seven until every task
// landed, the only ways to keep CI green in between would be `it.skip`, `test.fails`, or an
// improvised allowlist edited by hand — all three are a guard that passes while doing nothing,
// which is what this file exists to prevent. Instead, each task owns exactly one JSON sidecar
// file under `../clockGuardOffenders/`, naming ITS OWN offending FILE and an EXPECTED OCCURRENCE
// COUNT for that file (not a file+line pair — see "why counts, not lines" below). The guard:
//   - fails on any FILE whose actual violation count exceeds its sidecar allowance — whether that
//     file had no allowance at all (a brand-new offender) or already had one (a second offense
//     added to an already-allowed file, B2 below);
//   - fails on any sidecar entry whose file's actual violation count has dropped BELOW its
//     allowance (a "stale" entry) — this is what FORCES a migrating task to delete its own entry
//     in the same commit as its fix, rather than leaving a dead exemption behind;
//   - fails if the sidecar directory does not contain EXACTLY the four pinned filenames below —
//     an eighth offender must either join an existing task's sidecar (visible in that task's own
//     diff) or create a fifth file, which this assertion refuses. There is no third route, and
//     migrating tasks never touch this scanner file to make room for one.
//
// B — MATCHING SYNTAX AGAINST AN ADVERSARY WHO HAS THE WHOLE GRAMMAR (S143 Step-5a, two rounds).
// Round 1 found: `new (Date)()` parenthesises the callee, so a bare `ts.isIdentifier(node.
// expression)` check failed and the violation was invisible outright. Fixed with an unwrap step —
// but scoped to PARENTHESES specifically, which was the mistake: executed against the real parser
// again, round 2 found FIVE MORE spellings with the identical shape (an expression that changes
// how the callee is TYPED or GROUPED, never what it evaluates to): `new (Date as any)()`,
// `new (<any>Date)()`, `new (Date satisfies any)()`, `new (Date!)()`, and `new (0, Date)()` (the
// right operand of a comma expression). This is the SAME class of hole the backend guard this
// mirrors hit five times over five review passes, each closing a spelling the previous one did
// not imagine — not a criticism of either implementation, but the nature of matching syntax
// against a grammar with dozens of ways to wrap the same value. The fix generalises instead of
// re-patching: `unwrapTransparentWrapper` loops over EVERY known wrapper kind until a pass changes
// nothing (so they compose — `new ((Date as any)!)()` unwraps in one call), and is named and
// documented for the SHAPE ("value-preserving wrapper"), not the list of six, precisely so the
// next spelling extends the same loop with the same kind of test rather than prompting a seventh
// bespoke patch. The test suite below has one test per spelling for exactly this reason: the list
// of tests IS the map of "known transparent wrappers so far", as much the deliverable as the loop.
//
// WHY COUNTS, NOT FILE+LINE, AND THE PER-FILE BUDGET THIS DELIBERATELY IS (S143 Step-5a).
// The first version of this guard matched sidecar entries against violations by `file:line`.
// Executing it (not just reading it) found:
//   B2 — because the match key was `file:line`, a SECOND `new Date()` added to an already-allowed
//        line collapsed onto the SAME key as the first: `allowed.has(key)` was still true, so
//        the extra violation was silently exempted. One sidecar entry exempted UNLIMITED
//        violations on that file+line.
//   W  — matching on the exact line number meant an unrelated edit ABOVE an allowed line (adding
//        or removing lines anywhere earlier in the file) shifted that line number, which made the
//        old entry "stale" (no violation at the old line) AND produced a fresh "unlisted" hit (a
//        violation at the new line with no entry naming it) — for a change that touched no clock
//        logic at all. Four sibling tasks editing these files concurrently would routinely trip
//        each other's sidecar entries for reasons having nothing to do with the clock.
// Matching on (file, expected occurrence COUNT) instead of (file, line) fixes both at once: a
// line shift changes no file's violation COUNT, so nothing goes stale and nothing becomes
// unlisted; a second violation in an already-allowed file raises that file's actual count above
// its allowance, which IS now detected as unlisted (closing B2); and a fixed violation lowers the
// count below the allowance, which IS now detected as stale (preserving the "delete your entry
// when you fix it" property). The trade is precision in MATCHING for precision in REPORTING: a
// violation's real line number is still carried on `Violation` and still shown in failure
// messages — nothing here makes a failure message vaguer, only the equality check coarser.
//
// THIS IS A DELIBERATE PER-FILE BUDGET, NOT PER-OCCURRENCE IDENTITY — say so plainly, so nobody
// later reads the coarser key as an oversight. A count-preserving replacement WOULD pass: fix one
// violation in a file and introduce a different one elsewhere in the SAME file in the same
// commit, and the count is unchanged, so neither list reacts. That gap is accepted, deliberately,
// for three reasons: (1) the common accident — a violation moving to a DIFFERENT file, or a
// genuinely new one appearing anywhere — is still caught, because it changes some file's count;
// (2) the pathological case requires deliberately fixing and reintroducing a violation in the
// SAME file in the SAME commit, which is a much narrower target than "any edit near an allowed
// line" (the file+line design's failure mode); and (3) it is bounded by the sprint regardless —
// the close gate requires every sidecar to reach `[]`, so any violation surviving inside this gap
// still fails the moment its owning task's sidecar is expected to be empty. We chose the coarser
// key ON PURPOSE, to stop line shifts producing false failures across four concurrently-editing
// tasks; this paragraph is that trade written down, not a gap nobody noticed.
//
// WHAT "REACH" MEANS AND WHY IT IS ASSERTED SEPARATELY. A scanner that quietly only looked at the
// seven known paths would pass every check above while missing every file written after this
// commit. `import.meta.glob('/src/**/*.{ts,tsx}', …)` cannot do that BY CONSTRUCTION — it is a
// recursive glob, not an enumerated list — but the tests below prove it empirically anyway, along
// three independent axes: a check that every KNOWN offender path is discovered; a global floor
// tied close to the real combined file count (226 at the time this was written); and — after TWO
// rounds of the same lesson as B above — a per-root MINIMUM COUNT for each of several subtree
// prefixes, not mere presence. Round 1 found the global floor (then 100) too loose: excluding all
// of `src/lib/**` (19 files) still leaves 207, comfortably above 100. Raising the floor to 215 and
// adding a PRESENCE check per subtree closed that — but round 2, executed again, found presence
// alone proves only non-emptiness: shrinking `e2e/` from its real 7 files down to only its 2
// known-offender files still satisfies "at least one file under e2e/ is discovered", and the
// global floor cannot see it either, because `e2e/` is tiny relative to `src/` (losing 5 of 7
// barely dents a combined 226). The fix, again, generalises rather than re-patching: `SUBTREE_
// FLOORS` gives every tracked prefix (`src/`, `src/lib/`, `src/pages/`, `src/components/`,
// `src/hooks/`, `e2e/`) its OWN minimum count, so a subtree excluded outright OR collapsed down to
// only its allowed files both fail, on that root's own number, regardless of every other root's
// size. The global floor also closes a specific silent-failure hazard: `import.meta.glob` returns
// `{}` for a root that does not exist or is mistyped — it throws NOTHING — so a typo in the glob
// pattern would otherwise make this file assert "zero offenders" over an empty set and pass while
// scanning nothing at all. This file demonstrates that hazard directly with a second, deliberately
// wrong glob root.
//
// WHAT THE FABRICATED-FILE PROOF DOES NOT SHOW (an honest limitation, raised at Step-5a). Below,
// a previously-unknown nested path is spliced into an in-memory copy of the discovered file map
// and run through the real scanning function, to prove the PARSER does not special-case the seven
// known paths. It does NOT prove that `import.meta.glob` itself would discover a file that did
// not exist when this test module was transformed: that call is resolved once, at transform time,
// against the filesystem as it stood then, and there is no supported way to make a new on-disk
// file retroactively appear in an already-resolved glob result from inside a running test — short
// of writing through `node:fs`, which this file does not do, for the reason given above. The three
// discovery-reach checks in the paragraph above (not this fabricated-file check) are what stand in
// for proving discovery itself is not narrowed; this check is a narrower, complementary proof
// about the parsing stage alone.

import { describe, it, expect } from 'vitest'
import * as ts from 'typescript'

// ── types ──────────────────────────────────────────────────────────────────────────────────────

/** One entry in an offender sidecar: `file` and `count` name how many zero-argument `new Date()`
 *  calls this guard is currently allowed to find in that file; `task` records which migration
 *  task owns (and will remove) it. Matching is by FILE + COUNT, not file + line — see the header
 *  comment "WHY COUNTS, NOT FILE+LINE" for why. */
interface OffenderEntry {
  file: string
  count: number
  task: string
}

/** One zero-argument `new Date()` call the parser found, wherever it lives. `line` is carried for
 *  legible failure messages only — it plays no part in matching against sidecar entries. */
interface Violation {
  file: string
  line: number
}

// ── the parser (not a pattern-match) ──────────────────────────────────────────────────────────

/** The one file this guard exempts: the single approved source of "what day is it in
 *  Copenhagen?" (`src/lib/copenhagenDate.ts`) legitimately reads the machine clock exactly once,
 *  as the DEFAULT value of its `now` parameter, precisely so every OTHER call site can pass an
 *  explicit instant instead of reading the clock itself. */
const APPROVED_HELPER = 'src/lib/copenhagenDate.ts'

function scriptKindFor(path: string): ts.ScriptKind {
  return path.endsWith('.tsx') ? ts.ScriptKind.TSX : ts.ScriptKind.TS
}

/**
 * Strips away any number of nested "transparent" wrappers around a callee expression — wrappers
 * that change how TypeScript TYPES an expression, or how the parser groups it, but never change
 * what it EVALUATES to at runtime. Confirmed by dumping the real AST for each shape (Step-5a
 * round 2, after round 1's parentheses-only unwrap was shown to miss five more spellings):
 *   - `ParenthesizedExpression` — `(Date)`;
 *   - `AsExpression` — `Date as any`;
 *   - `TypeAssertionExpression` — `<any>Date` (legal only outside `.tsx`, where `<any>` would be
 *     ambiguous with JSX — irrelevant here, since this function only sees whatever the real
 *     parser already resolved for the file's actual script kind);
 *   - `SatisfiesExpression` — `Date satisfies any`;
 *   - `NonNullExpression` — `Date!`;
 *   - the RIGHT operand of a comma/sequence `BinaryExpression` — `(0, Date)` evaluates its left
 *     operand for effect and discards it, so the expression's value (and this guard's concern)
 *     is entirely the right operand.
 * Loops until a pass changes nothing, because these nest arbitrarily and in combination — not
 * just `new ((Date))()`, but `new ((Date as any)!)()` — see the composed-nesting test below.
 *
 * This is named for the SHAPE ("a value-preserving wrapper"), not today's list of six: the list
 * is exactly the AST node kinds discovered so far to have that shape. A new TypeScript syntax
 * form with the same shape needs one more branch here, following the same pattern — that is the
 * intended extension point. This is also why the test suite below has one test PER SPELLING: the
 * list of tests is the map of "known transparent wrappers", every bit as much the deliverable as
 * the loop itself, so the next person adding a wrapper kind sees exactly what pattern to extend
 * and what to add alongside it.
 */
function unwrapTransparentWrapper(expression: ts.Expression): ts.Expression {
  let current = expression
  for (;;) {
    if (ts.isParenthesizedExpression(current)) {
      current = current.expression
    } else if (ts.isAsExpression(current)) {
      current = current.expression
    } else if (ts.isTypeAssertionExpression(current)) {
      current = current.expression
    } else if (ts.isSatisfiesExpression(current)) {
      current = current.expression
    } else if (ts.isNonNullExpression(current)) {
      current = current.expression
    } else if (ts.isBinaryExpression(current) && current.operatorToken.kind === ts.SyntaxKind.CommaToken) {
      current = current.right
    } else {
      return current
    }
  }
}

/** True for an AST node that is `new Date(...)` with zero arguments — covers `new Date()`,
 *  `new Date` (no parens at all), `new Date ()` (a space), `new Date(/* comment *\/)`, and any
 *  callee wrapped in one or more "transparent" wrappers (see `unwrapTransparentWrapper` above:
 *  `new (Date)()`, `new (Date as any)()`, `new (<any>Date)()`, `new (Date satisfies any)()`,
 *  `new (Date!)()`, `new (0, Date)()`, and combinations of these), all of which are the same
 *  legal, zero-argument construct. Deliberately does NOT match `new Date(x)` (a legitimate,
 *  explicit instant) or a qualified callee like `new Foo.Date()`. */
function isZeroArgumentNewDate(node: ts.Node): node is ts.NewExpression {
  if (!ts.isNewExpression(node)) return false
  const callee = unwrapTransparentWrapper(node.expression)
  return (
    ts.isIdentifier(callee) &&
    callee.text === 'Date' &&
    (node.arguments === undefined || node.arguments.length === 0)
  )
}

/**
 * Parses one file's source text and returns the 1-based line of every zero-argument `new Date()`
 * it contains, wherever it is nested — inside a template literal hole, a JSX expression
 * container, behind a comment between `new` and `Date`, anywhere the real TypeScript grammar
 * would allow a `new` expression to appear. `ts.forEachChild` walks the REAL AST, so this needs
 * no special-casing for any of those positions: a real parser already knows where code lives.
 *
 * `relativePath` is used only to (a) pick TS vs TSX grammar and (b) exempt the approved helper —
 * it is never read from disk here.
 */
function findViolations(relativePath: string, sourceText: string): number[] {
  if (relativePath === APPROVED_HELPER) return []

  const sourceFile = ts.createSourceFile(
    relativePath,
    sourceText,
    ts.ScriptTarget.Latest,
    /* setParentNodes */ true,
    scriptKindFor(relativePath),
  )

  const lines: number[] = []

  function visit(node: ts.Node): void {
    if (isZeroArgumentNewDate(node)) {
      const { line } = sourceFile.getLineAndCharacterOfPosition(node.getStart(sourceFile))
      lines.push(line + 1) // TS positions are 0-based; report the same 1-based line a human reads.
    }
    ts.forEachChild(node, visit)
  }

  visit(sourceFile)
  return lines
}

function normalizeKey(globKey: string): string {
  return globKey.startsWith('/') ? globKey.slice(1) : globKey
}

/** Scans an already-discovered `{ path: rawSourceText }` map and returns every violation found,
 *  with `file` normalized to a leading-slash-free, forward-slash relative path — the same shape
 *  the offender sidecars use, so the two can be compared by simple string keys. */
function scanFileMap(fileMap: Record<string, string>): Violation[] {
  const violations: Violation[] = []
  for (const [rawPath, content] of Object.entries(fileMap)) {
    const file = normalizeKey(rawPath)
    for (const line of findViolations(file, content)) {
      violations.push({ file, line })
    }
  }
  return violations
}

function countByFile(violations: Violation[]): Map<string, Violation[]> {
  const byFile = new Map<string, Violation[]>()
  for (const v of violations) {
    const list = byFile.get(v.file)
    if (list) list.push(v)
    else byFile.set(v.file, [v])
  }
  return byFile
}

/**
 * Splits real violations against the sidecar-declared allowances, matching by FILE + COUNT (see
 * the header comment "WHY COUNTS, NOT FILE+LINE" for the two bugs and one warning this replaced
 * file+line matching to fix):
 *   - `unlisted`: violations in a file whose actual count EXCEEDS its allowance (zero for a file
 *     with no sidecar entry at all — the guard's core purpose; or the excess above the allowance
 *     for a file that already has one — closing B2). Which specific occurrences are reported as
 *     "the excess" is an arbitrary, sorted-by-line convention for a legible message only — the
 *     guard has no way to know which occurrence is "the original" and which is "the new one",
 *     since they are structurally identical; it only knows the COUNT went up.
 *   - `stale`: sidecar entries whose file's actual count has dropped BELOW the allowance (the fix
 *     landed but the entry was not deleted in the same commit).
 * A migrating task is done exactly when its entries stop appearing in EITHER list — removed from
 * the sidecar, and its file's actual count at zero.
 */
function diffOffenders(
  violations: Violation[],
  allowed: Map<string, OffenderEntry>,
): { unlisted: Violation[]; stale: OffenderEntry[] } {
  const byFile = countByFile(violations)

  const unlisted: Violation[] = []
  for (const [file, vs] of byFile) {
    const allowedCount = allowed.get(file)?.count ?? 0
    if (vs.length > allowedCount) {
      const sorted = [...vs].sort((a, b) => a.line - b.line)
      unlisted.push(...sorted.slice(allowedCount))
    }
  }

  const stale: OffenderEntry[] = []
  for (const entry of allowed.values()) {
    const actualCount = byFile.get(entry.file)?.length ?? 0
    if (actualCount < entry.count) stale.push(entry)
  }

  return { unlisted, stale }
}

// ── discovery ──────────────────────────────────────────────────────────────────────────────────
//
// `import.meta.glob` patterns must be static string literals — Vite recognises and rewrites this
// exact call shape at transform time, which is also why the "wrong root" demonstration below is a
// SECOND, separately-spelled call rather than a variable substituted into the first.

const SRC_FILES = import.meta.glob<string>('/src/**/*.{ts,tsx}', {
  eager: true,
  query: '?raw',
  import: 'default',
})

const E2E_FILES = import.meta.glob<string>('/e2e/**/*.{ts,tsx}', {
  eager: true,
  query: '?raw',
  import: 'default',
})

// The hazard, demonstrated directly: a root that does not exist resolves to an EMPTY object, not
// an exception. Nothing here ever throws — see the floor assertions in the main fact below, which
// are what actually turns this silence into a failing test.
const MISTYPED_ROOT_FILES = import.meta.glob<string>(
  '/this-root-does-not-exist-zzz/**/*.{ts,tsx}',
  { eager: true, query: '?raw', import: 'default' },
)

interface OffenderSidecarModules {
  [path: string]: OffenderEntry[]
}

const OFFENDER_SIDECARS: OffenderSidecarModules = import.meta.glob<OffenderEntry[]>(
  '/src/lib/clockGuardOffenders/*.json',
  { eager: true, import: 'default' },
)

// ── the pinned set this guard is contractually allowed to consult ────────────────────────────
//
// Exactly these four filenames, one per owning migration task. NOT a numeric counter: a counter
// would force every migrating task to edit this scanner file to raise it, which is precisely the
// concurrent-edit conflict the sidecar split exists to avoid. An eighth offender either joins one
// of these four files (visible in that task's own diff) or creates a fifth — which the assertion
// below refuses. There is no third route.
const PINNED_SIDECAR_FILENAMES = ['14303.json', '14304.json', '14305.json', '14306b.json'] as const

const VALID_TASK_IDS = new Set<string>(['14303', '14304', '14305', '14306b'])

// The seven sites known at the time this guard was written (TASK-14302 spec). Checked against
// DISCOVERY (not the sidecars) so a scanner that quietly narrowed its own glob would still be
// caught even if the sidecars were (wrongly) edited to match it.
const KNOWN_OFFENDER_PATHS = [
  'src/pages/SkemaPage.tsx',
  'src/components/SkemaGrid.tsx',
  'src/pages/approval/TeamOversigt.tsx',
  'src/pages/ArsoversigtPage.tsx',
  'e2e/helpers/dates.ts',
  'e2e/approval.spec.ts',
  'src/pages/delegation/__tests__/DelegationPage.test.tsx',
] as const

// Per-root MINIMUM COUNTS, not mere presence. A first version of this check asked only "does at
// least one file under this prefix appear?", which a subtree collapsing to just its known
// offender files still satisfies — Step-5a round 2 found that shrinking `e2e/` from its real 7
// files down to only its two known-offender files (`e2e/helpers/dates.ts`,
// `e2e/approval.spec.ts`) still passes a presence check, and the global SCAN_FLOOR below cannot
// see it either, because `e2e/` (7 files) is tiny relative to `src/` (219 files) — losing 5 of
// e2e/'s 7 barely dents a combined total of 226. A per-root COUNT closes both gaps at once: it
// catches a subtree excluded outright (the original Step-5a round-1 finding, `src/lib/**`) AND a
// subtree collapsed down to just its allowed files (this round's finding, `e2e/`), because both
// show up as "this root's own count fell below its own floor", independent of every other root's
// size. Floors below are tied close to each root's real count at the time this was written (`src`
// 219, `src/lib` 19, `src/pages` 87, `src/components` 52, `src/hooks` 49, `e2e` 7) with enough
// headroom for ordinary growth/shrink, but tight enough that `e2e/` collapsing to its 2 known
// offenders (floor 5) or `src/lib/` disappearing entirely (floor 15) both fail.
const SUBTREE_FLOORS: Readonly<Record<string, number>> = {
  'src/': 205,
  'src/lib/': 15,
  'src/pages/': 78,
  'src/components/': 45,
  'src/hooks/': 42,
  'e2e/': 5,
}

// Tied close to the real count (226 .ts/.tsx files under src/+e2e/ at the time this was raised —
// 219 under src/, 7 under e2e/), specifically so it BINDS: it must sit above 207, the count left
// after excluding all of `src/lib/**`, or it cannot catch that exact narrowing (Step-5a W). Tying
// it this close means routine file growth will eventually require raising it — an accepted
// maintenance cost, not a defect, per the review that asked for this to be tightened.
const SCAN_FLOOR = 215

// ── the guard itself ──────────────────────────────────────────────────────────────────────────

describe('clock guard — no zero-argument new Date() outside the approved Copenhagen helper', () => {
  it('discovers the whole tree, proves its own reach, and fails on any undeclared or stale offender', () => {
    const discovered: Record<string, string> = { ...SRC_FILES, ...E2E_FILES }
    const discoveredKeys = new Set(Object.keys(discovered).map(normalizeKey))

    // 1) REACH — the floor. If `/src/**` or `/e2e/**` were ever mistyped, `import.meta.glob`
    //    would return {} silently (see MISTYPED_ROOT_FILES below); this is the assertion that
    //    turns that silence into a failure instead of a false-green "zero offenders found". Tied
    //    close to the real count (see SCAN_FLOOR above) so it also catches a whole subtree being
    //    quietly dropped, not only a totally broken root.
    expect(
      discoveredKeys.size,
      `expected at least ${SCAN_FLOOR} discovered .ts/.tsx files under src/+e2e/, found ` +
        `${discoveredKeys.size}. import.meta.glob returns {} for a missing/mistyped root with no ` +
        'exception, so a small number here means the glob root broke, not that the tree shrank.',
    ).toBeGreaterThanOrEqual(SCAN_FLOOR)

    // 2) REACH — contains every known site. Guards against a scanner narrowed to a subset of the
    //    tree (e.g. accidentally globbing only `/src/pages/**`).
    for (const known of KNOWN_OFFENDER_PATHS) {
      expect(discoveredKeys.has(known), `expected discovery to include ${known}`).toBe(true)
    }

    // 3) REACH — a per-root MINIMUM COUNT, not mere presence. This is the check that falsifies
    //    both "an entire subtree was quietly excluded" (round 1) AND "a subtree collapsed down to
    //    only its known-offender files" (round 2 — see SUBTREE_FLOORS above for the e2e/ example
    //    a presence-only check let through), independent of what the combined SCAN_FLOOR sees.
    for (const [prefix, floor] of Object.entries(SUBTREE_FLOORS)) {
      const count = [...discoveredKeys].filter((k) => k.startsWith(prefix)).length
      expect(
        count,
        `expected at least ${floor} discovered files under ${prefix}, found ${count} — a subtree ` +
          'excluded outright, or collapsed down to only its known-offender files, shows up here ' +
          "even though presence-only or the combined floor cannot see it",
      ).toBeGreaterThanOrEqual(floor)
    }

    // 4) The silent-{}-root hazard, demonstrated: a bad root throws nothing …
    const mistypedKeys = Object.keys(MISTYPED_ROOT_FILES)
    expect(mistypedKeys, 'import.meta.glob should return {} for a root that does not exist').toEqual([])
    // … and is exactly what the floor above exists to catch: this count would fail assertion (1)
    // were it ever the real discovery result.
    expect(mistypedKeys.length).toBeLessThan(SCAN_FLOOR)

    // 5) The parsing stage catches a brand-new, nested, previously-unknown PATH when fed one —
    //    constructed here in memory only, never written to disk, never committed, so it can never
    //    itself become a permanent offender needing a sidecar entry of its own. This does NOT
    //    exercise import.meta.glob's own filesystem walk — see the header comment "WHAT THE
    //    FABRICATED-FILE PROOF DOES NOT SHOW" for exactly what it does and does not prove; checks
    //    (1)-(3) above are what stand in for proving discovery itself is not narrowed.
    const fabricatedPath = 'src/pages/__proof__/never/committed/ProofFixture.tsx'
    const fabricated: Record<string, string> = {
      ...discovered,
      [`/${fabricatedPath}`]: [
        'export function ProofFixture() {',
        '  const stamp = new Date()',
        '  return String(stamp)',
        '}',
      ].join('\n'),
    }
    const fabricatedViolations = scanFileMap(fabricated)
    expect(
      fabricatedViolations.some((v) => v.file === fabricatedPath),
      'a zero-argument new Date() in a brand-new nested file should be caught by the real scanner',
    ).toBe(true)

    // 6) SIDECARS — exactly the four pinned filenames, nothing more, nothing less.
    const sidecarFilenames = Object.keys(OFFENDER_SIDECARS)
      .map((p) => p.split('/').pop() ?? p)
      .sort()
    expect(sidecarFilenames).toEqual([...PINNED_SIDECAR_FILENAMES].sort())

    // 7) SIDECARS — every entry, everywhere, is readable, owned by a real task, names a positive
    //    count, and claims a file no OTHER sidecar also claims (two entries for the same file
    //    would make "which allowance applies" ambiguous). A missing or unreadable sidecar
    //    (malformed JSON) throws at module load, above, which fails this test file outright; a
    //    present-but-non-array sidecar is caught here explicitly.
    const allowed = new Map<string, OffenderEntry>()
    for (const [sidecarPath, entries] of Object.entries(OFFENDER_SIDECARS)) {
      expect(Array.isArray(entries), `${sidecarPath} is missing or unreadable`).toBe(true)
      for (const entry of entries) {
        expect(
          VALID_TASK_IDS.has(entry.task),
          `${sidecarPath} names unknown owning task "${entry.task}" for ${entry.file}`,
        ).toBe(true)
        expect(
          Number.isInteger(entry.count) && entry.count > 0,
          `${sidecarPath} names a non-positive or non-integer count (${entry.count}) for ${entry.file}`,
        ).toBe(true)
        expect(
          allowed.has(entry.file),
          `${entry.file} is claimed by more than one sidecar entry — exactly one task may own a file`,
        ).toBe(false)
        allowed.set(entry.file, entry)
      }
    }

    // 8) THE SCAN ITSELF — zero undeclared offenders, zero stale sidecar entries.
    const violations = scanFileMap(discovered)
    const { unlisted, stale } = diffOffenders(violations, allowed)

    expect(
      unlisted,
      unlisted.length === 0
        ? ''
        : 'Undeclared zero-argument new Date() found outside src/lib/copenhagenDate.ts:\n' +
            unlisted.map((v) => `  ${v.file}:${v.line}`).join('\n') +
            '\nEither this reads the Copenhagen business-date helper instead of new Date() ' +
            'directly, or — if it is a genuinely new, still-open migration — add or raise the ' +
            'count on an entry in the owning task\'s sidecar under src/lib/clockGuardOffenders/.',
    ).toEqual([])

    expect(
      stale,
      stale.length === 0
        ? ''
        : 'Stale sidecar entries — the actual violation count in these files has dropped below ' +
            'the allowance, so the entry must be lowered or deleted in the SAME commit as the ' +
            'fix that removed the violation:\n' +
            stale
              .map((e) => `  ${e.file}: allowed ${e.count}, found ${countByFile(violations).get(e.file)?.length ?? 0} (task ${e.task})`)
              .join('\n'),
    ).toEqual([])
  })
})

// ── the diff mechanism, proven in isolation ──────────────────────────────────────────────────
//
// The assertions above can only exercise "unlisted" and "stale" against whatever state the real
// tree happens to be in right now. These facts prove the MECHANISM itself — independent of live
// repo state — with synthetic data, so the guarantee does not rest solely on the tree never
// drifting into a state that would exercise it.

describe('diffOffenders — the mechanism behind "landing without allowlisting"', () => {
  it('reports a violation in a file with no sidecar entry as unlisted', () => {
    const { unlisted, stale } = diffOffenders([{ file: 'src/example.ts', line: 10 }], new Map())
    expect(unlisted).toEqual([{ file: 'src/example.ts', line: 10 }])
    expect(stale).toEqual([])
  })

  it('reports a sidecar entry whose file has fewer actual violations than allowed as stale', () => {
    const entry: OffenderEntry = { file: 'src/example.ts', count: 1, task: '14303' }
    const { unlisted, stale } = diffOffenders([], new Map([[entry.file, entry]]))
    expect(unlisted).toEqual([])
    expect(stale).toEqual([entry])
  })

  it('reports neither when the actual count matches the allowance exactly', () => {
    const entry: OffenderEntry = { file: 'src/example.ts', count: 1, task: '14303' }
    const { unlisted, stale } = diffOffenders(
      [{ file: 'src/example.ts', line: 10 }],
      new Map([[entry.file, entry]]),
    )
    expect(unlisted).toEqual([])
    expect(stale).toEqual([])
  })

  it('is unaffected by an unrelated line shift within an allowed file (the Step-5a W fix)', () => {
    const entry: OffenderEntry = { file: 'src/example.ts', count: 1, task: '14303' }
    // The single violation moved from line 10 to line 55 — an edit anywhere above it in the file —
    // but the FILE's count is still exactly 1, so neither list should react to the shift at all.
    const { unlisted, stale } = diffOffenders(
      [{ file: 'src/example.ts', line: 55 }],
      new Map([[entry.file, entry]]),
    )
    expect(unlisted).toEqual([])
    expect(stale).toEqual([])
  })

  it('reports the excess occurrence as unlisted when a SECOND violation lands in an already-allowed file (closes B2)', () => {
    const entry: OffenderEntry = { file: 'src/example.ts', count: 1, task: '14303' }
    const { unlisted, stale } = diffOffenders(
      [
        { file: 'src/example.ts', line: 10 },
        { file: 'src/example.ts', line: 11 },
      ],
      new Map([[entry.file, entry]]),
    )
    expect(unlisted).toEqual([{ file: 'src/example.ts', line: 11 }])
    expect(stale).toEqual([])
  })
})

// ── the parser, proven against every hazard the spec named ──────────────────────────────────

describe('findViolations — parsing, not pattern-matching', () => {
  it('catches new Date() with no arguments', () => {
    expect(findViolations('src/x.ts', 'const now = new Date()')).toEqual([1])
  })

  it('catches new Date with no parentheses at all', () => {
    expect(findViolations('src/x.ts', 'const now = new Date')).toEqual([1])
  })

  it('catches new Date () with a space before the parentheses', () => {
    expect(findViolations('src/x.ts', 'const now = new Date ()')).toEqual([1])
  })

  it('catches new Date(/* comment */) — an empty argument list is still zero-argument', () => {
    expect(findViolations('src/x.ts', 'const now = new Date(/* comment */)')).toEqual([1])
  })

  it('catches a comment sitting between the `new` keyword and `Date`', () => {
    expect(findViolations('src/x.ts', 'const now = new /* sneaky */ Date()')).toEqual([1])
  })

  // Every "transparent wrapper" unwrapTransparentWrapper knows about (Step-5a round 1 found
  // parentheses; round 2 executed the parser against the rest of the grammar and found five
  // more). ONE TEST PER SPELLING, deliberately: this list is the map of known wrapper kinds, so
  // the next person adding a new one sees exactly what pattern — and what test — to add alongside
  // it, per the header comment on unwrapTransparentWrapper.
  it('catches new (Date)() — parentheses around the callee do not hide it (B1)', () => {
    expect(findViolations('src/x.ts', 'const now = new (Date)()')).toEqual([1])
  })

  it('catches new ((Date))() — nested parentheses around the callee', () => {
    expect(findViolations('src/x.ts', 'const now = new ((Date))()')).toEqual([1])
  })

  it('catches new (0, Date)() — the right operand of a comma/sequence expression', () => {
    expect(findViolations('src/x.ts', 'const now = new (0, Date)()')).toEqual([1])
  })

  it('catches new (Date as any)() — a TypeScript `as` cast', () => {
    expect(findViolations('src/x.ts', 'const now = new (Date as any)()')).toEqual([1])
  })

  it('catches new (<any>Date)() — an angle-bracket type assertion', () => {
    expect(findViolations('src/x.ts', 'const now = new (<any>Date)()')).toEqual([1])
  })

  it('catches new (Date satisfies any)() — the `satisfies` operator', () => {
    expect(findViolations('src/x.ts', 'const now = new (Date satisfies any)()')).toEqual([1])
  })

  it('catches new (Date!)() — a non-null assertion', () => {
    expect(findViolations('src/x.ts', 'const now = new (Date!)()')).toEqual([1])
  })

  it('catches a callee wrapped in DIFFERENT wrapper kinds nested together, not just one kind repeated', () => {
    expect(findViolations('src/x.ts', 'const now = new ((Date as any)!)()')).toEqual([1])
  })

  it('does NOT catch new Date(x) — an explicit instant is the correct, sanctioned call', () => {
    expect(findViolations('src/x.ts', 'const then = new Date(isoString)')).toEqual([])
  })

  it('does NOT catch new Date(y, m, d) — a multi-argument constructor call', () => {
    expect(findViolations('src/x.ts', 'const then = new Date(2026, 0, 1)')).toEqual([])
  })

  it('does NOT catch new Date() sitting inside an ordinary string literal', () => {
    const source = 'const msg = "not a call: new Date() // still just text"'
    expect(findViolations('src/x.ts', source)).toEqual([])
  })

  it('does NOT catch new Date() sitting inside a line comment', () => {
    const source = ['// TODO: stop calling new Date() here', 'const x = 1'].join('\n')
    expect(findViolations('src/x.ts', source)).toEqual([])
  })

  it('does NOT catch new Date() sitting in a plain template literal with no expression holes', () => {
    // Step-5a round 2: the FIRST version of this fixture described itself as having "no ${} hole
    // at all" — but writing the literal characters `${}` INSIDE a template literal creates an
    // empty-expression hole (a parse error: "Expression expected"), which is exactly the opposite
    // of what this test claims to exercise. The probe that vetted this behaviour passed only
    // because it happened not to hit this; the COMMITTED fixture below is a genuine
    // NoSubstitutionTemplateLiteral — confirmed by dumping its AST — with zero holes of any kind.
    const source = 'const msg = `not a call: new Date() — still just text, no interpolation here`'
    expect(findViolations('src/x.ts', source)).toEqual([])
  })

  it('does NOT catch the literal text new Date() sitting inside a regex literal pattern', () => {
    const source = 'const re = /new Date\\(\\)/'
    expect(findViolations('src/x.ts', source)).toEqual([])
  })

  it('catches new Date() inside a template literal hole, including a NESTED hole', () => {
    // eslint-disable-next-line no-template-curly-in-string -- this is the fixture under test
    const source = 'const s = `outer ${`inner ${new Date()}`}`'
    expect(findViolations('src/x.ts', source)).toEqual([1])
  })

  it('is not confused by a regex literal containing a double slash on the line before an offender', () => {
    const source = ['const re = /https?:\\/\\//', 'const now = new Date()'].join('\n')
    expect(findViolations('src/x.ts', source)).toEqual([2])
  })

  it('catches new Date() inside a JSX expression container', () => {
    const source = 'const el = <div>{new Date()}</div>'
    expect(findViolations('src/x.tsx', source)).toEqual([1])
  })

  it('does NOT catch the literal text "new Date()" sitting in JSX TEXT, not an expression container', () => {
    const source = 'const el = <div>new Date()</div>'
    expect(findViolations('src/x.tsx', source)).toEqual([])
  })

  it('exempts the approved helper by path, even though its content legitimately calls new Date()', () => {
    const source = 'export function copenhagenToday(now: Date = new Date()): string { return "" }'
    expect(findViolations('src/lib/copenhagenDate.ts', source)).toEqual([])
    // The SAME source, under any other path, is exactly the offense this guard exists to catch —
    // proving the exemption is keyed on PATH, not on recognising this particular snippet.
    expect(findViolations('src/lib/someOtherFile.ts', source)).toEqual([1])
  })
})
