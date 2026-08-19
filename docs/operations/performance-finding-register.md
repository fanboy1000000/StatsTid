# Performance finding register (F-series)

**Status**: living document. Created 2026-08-03 (S126) to give the F-series a durable home.

## Why this file exists

The original F1–F6 performance analysis was produced during S125 and **never written to a file**. It
survived only as list positions in `docs/sprints/SPRINT-125.md:667-671` and
`.claude/refinements/REFINEMENT-f1-period-status-n-plus-1.md:295` (the refinement file has since been
removed — `.claude/refinements/` is transient per-sprint scratch, which is itself the point of this
register). F1, F2, F3, F5 and F6 each carried
an inline label in those lines and so remained identifiable; **F4 carried none and its description was
lost.** The owner ruled (2026-08-03) to re-derive it by a fresh sweep rather than guess, and to record
the result durably. This file is that record.

The lesson is the file, not the finding: an analysis that lives in a conversation is one context
window away from being unrecoverable.

---

## The register

| id | Finding | Status |
|----|---------|--------|
| F1 | Period-status projection: per-pending-employee authorization storm (27,001 commands / 13.8s at K=1000) | **DONE** S125 (TASK-12501) — 9 commands / 79ms, flat in K. ⚠ **S131 guard-quality note (2026-08-19)**: the S106 forest/search constant-query-count guards count a REPOSITORY COPY of the endpoints' reads, not the endpoints — a handler-side per-row query (the exact guarded regression class) would not move the count (QUAL-119). |
| F2 | Un-cancelled fetch effects (reported as "StrictMode double-fetch") | **MOSTLY DONE** S126 — 13 hooks + 3 components; ⚠ census MISSED `useApprovalsByMonth` + `useMyReportsByMonth` (both `[year, month]`-keyed, still unguarded). ⚠ **S131 census-classification CORRECTION (2026-08-19)**: `usePositionOverrides` + `useWageTypeMappings` were classified "mount-only fetches (cannot exhibit it) — deliberately left alone", but their `fetchAll` is re-invoked by every mutation AND exposed as `refetch`, so overlapping responses CAN land out of order (QUAL-028, Likely). Same class as the two missed hooks above — fold into the F2 remainder. |
| F3 | Route-level code splitting (entry chunk 594 kB) | **DONE** S125 (TASK-12504) — 209 kB, −61% |
| F4 | *description lost; re-derived below* | RE-DERIVED 2026-08-03 — see below |
| F5 | Flex-balance full-stream replay on the consolidated `employee-{id}` stream | **DONE** S126 — see below |
| F6 | Loading-flash perception | **OPEN — the S126 measurement was RETRACTED** (harness measured full page loads, and its stop condition was true before the route mounted). Chunk sizes valid; durations and the 250 ms threshold are not. |

---

## F4 — re-derivation sweep (2026-08-03)

### Method and scope (recorded so the sweep is reproducible)

Deliberately targeted the two defect families F1 and F5 belong to, since whatever F4 was, it sat
alongside them in one analysis:

1. **Per-item query loops (N+1)** — `foreach` blocks containing awaited calls, across
   `src/Backend/StatsTid.Backend.Api/Endpoints/`.
2. **Unbounded or superlinear reads in request paths** — `SELECT` against append-only/growing tables
   (`events`, `audit_projection`, `approval_periods`), `GetAll*` repository methods, every `COUNT(*)`
   and every `OFFSET` in `src/Infrastructure/` and `src/Backend/`, excluding test, backfill and
   rebuild code.

**Bias check applied throughout**: "invisible in demo data" is the shared signature of F1 and F5 — the
demo world ships zero time registrations and a shallow audit history, so cost that tracks *system age*
or *history depth* cannot show up locally. That, not raw query count, was the discriminator.

### Inventory and disposition — every hit, including the ones dismissed

| Site | Shape | Disposition |
|---|---|---|
| `AuditProjectionRepository.cs:189` | exact `COUNT(*)` over filtered `audit_projection`, **per page request** | **DEFECT — the F4 re-derivation.** See below |
| `AuditProjectionRepository.cs:211` | `LIMIT/OFFSET` deep paging over the same table | **DEFECT (same finding, second mechanism)** |
| `AuditProjectionRepository.cs:104` | unfiltered `COUNT(*)` | Not applicable — no request-path caller; tests + backfill only |
| `ApprovalPeriodRepository.cs:1282`, `:1388` | shared `matched` CTE feeding `COUNT(*)` + page in ONE round-trip | Accepted — already the good pattern, deliberately documented at `:1195-1197`; bounded by org employees, not history |
| `UnitRepository.cs:216` | `LIMIT/OFFSET` | Accepted — bounded by unit count (administrative cardinality) |
| `PostgresEventStore.cs:195` `ReadAllAsync` | `OFFSET/LIMIT`, `maxCount` default 1000 | Accepted — explicitly bounded |
| `PostgresEventStore.cs:88` `ReadStreamAsync` | full-stream replay | Already **F5**; not double-counted |
| `AgreementConfigRepository`, `EntitlementConfigRepository`, `OrganizationRepository`, `PositionOverrideRepository`, `WageTypeMappingRepository` — `GetAllAsync` | unpaged full-table reads | Accepted — config tables, bounded by administrative cardinality (dozens), not by usage or age |
| `BalanceEndpoints.cs:927` | `foreach` over 3 probe anchors with awaited resolves | Accepted — fixed bound of 3, through cached helpers |
| `ReportingLineEndpoints.cs:1466`, `:1440`, `:1415` | per-row loops closing vikar/edge rows | Accepted — write paths, bounded by one user's active rows; one event per row is required by ADR-018 |
| `AdminEndpoints.cs:2050`, `:2733` | per-edge / per-descendant loops | Accepted — bounded by one user's edges / subtree, not by history |
| `SegmentManifestProjectionRebuilder.cs:131`, `:144` | `FROM events` unbounded | Accepted — offline rebuild tool, not a request path |

### The finding

**The audit-log read is the one place where per-request cost grows with SYSTEM AGE rather than with
data size — and it grows two ways at once.**

`GET` audit log (`AuditEndpoints.cs:44`) → `AuditProjectionRepository.QueryByOrgScopeAsync` (`:153`; named `GetPageAsync` when this finding was written):

1. **`SELECT COUNT(*) FROM audit_projection WHERE {visibility AND filters}` on every page request**
   (`:189`). An exact count cannot short-circuit: it must traverse every matching row. `audit_projection`
   is append-only with one row per audited event and **no retention or partitioning**
   (`docs/generated/db-schema.md:1038-1064`), so this is O(matching rows) and rises forever. The
   partial indexes (`idx_audit_projection_target_org_time` etc.) make it an index-only scan rather
   than a heap scan — which lowers the constant and does not change the growth.
2. **`LIMIT @limit OFFSET @offset` (`:211`)** — PostgreSQL walks and discards every skipped row, so
   deep pages are O(offset) on top of the count.

**Why it is invisible today**: exactly F1's and F5's signature. A freshly seeded demo has a few hundred
audit rows, so both terms are microseconds. A Danish state agency running this for three years has
millions, and the audit page is an HR/compliance surface that is *expected* to be paged deeply — the
one access pattern where OFFSET is worst.

**Confidence that this is what F4 was**: moderate, not certain. It is the strongest remaining member
of the F1/F5 family and the only surviving request-path read whose cost tracks system age. It may not
be the original F4. That is recorded rather than smoothed over — the register's value is the current
inventory, not the archaeology.

### Suggested remedy (not yet ruled)

- Replace the exact count with either a **keyset (seek) pagination** scheme on
  `(occurred_at DESC, projection_id DESC)` — which the existing indexes already support and which
  removes both terms at once — or an approximate/《capped》count (`COUNT(*)` over a bounded subquery)
  where an exact total is not a requirement.
- Keyset pagination changes the API contract (no arbitrary page jumps), so it needs an owner ruling
  before implementation. **Do not do this opportunistically inside unrelated work.**

---

## F5 — resolved (S126)

**The defect**: `employee-{id}` is the CONSOLIDATED stream (ADR-018 D6) and grows with every time
registration, so it is unbounded in employment length. Four call sites answered "what is the latest
flex balance?" four different ways — three by replaying and deserializing the entire stream in memory
(`BalanceEndpoints` `/summary` + year overview, `TimeEndpoints` `/flex-balance`), one by a hand-rolled
inline `DISTINCT ON` with per-field JSON extraction and its own culture-sensitive parse
(`ApprovalEndpoints`).

**The fix**: one rule, two shapes, on `PostgresEventStore` —
`ReadLatestOfTypeAsync<T>` (single stream, via `IEventStore`) and `ReadLatestOfTypePerStreamAsync<T>`
(batch). Both select `data` and go through `EventSerializer`, so the JSON key names and camelCase
convention stay owned by the serializer instead of being restated in the API layer.
`EventSerializer.EventTypeNameOf<T>()` supplies the `event_type` discriminator so a rename cannot
silently produce an always-empty read. **All four consumers repointed** — no exceptions.

**The index fork, resolved with evidence** (the AC required measurement, not reasoning). On a
1,100-event stream whose single `FlexBalanceUpdated` sits at version 5, with 18,001
`FlexBalanceUpdated` rows across 300 streams so the type is NOT selective (as in production):

| | Plan | Rows removed by filter | Buffers |
|---|---|---|---|
| before | `Index Scan Backward` on `(stream_id, stream_version)`, filtering `event_type` | **1095** | **782** |
| after | `Index Scan` on `(stream_id, event_type, stream_version DESC)`, `Index Cond` covers both | 0 | **4** |

So "bounded" was **not** earned by the query rewrite alone — the composite index
`idx_events_stream_type_version` is what makes it O(1). Added to `init.sql` as
`CREATE INDEX IF NOT EXISTS`, which is already the idempotent pattern there and so reaches legacy
databases on re-run without a separate guarded ALTER.

**Behaviour delta, stated rather than claimed away**: the old form deserialized every preceding
event, so a malformed or unregistered `event_type` anywhere on the stream threw; the new form never
reads those rows. Strictly more robust, but a difference — and it is exactly what
`LatestEventReadTests.DeepStream_WithUnreadableEarlierRow_OldFullReplayThrows_BoundedReadSucceeds`
uses as its red/green discriminator, since command count cannot separate the two (1 vs 1) and a
wall-clock threshold would be a flake generator.

## F2 — reclassified and resolved (S126)

**The reported finding was a non-defect.** React 18 double-invokes effects in DEV builds only;
production is unaffected, so "StrictMode double-fetch" is not a performance problem and removing
StrictMode would be strictly negative — it is the detector, not the fault.

**What it was signalling**: effect-driven fetches had no cancellation discipline at all.
`frontend/src` contained **zero** `AbortController` uses, and only `useSearch` (a `cancelled` flag)
and `useYearOverview` (a `latestRequestId` ref) carried any stale-response guard. The reachable defect
is a stale **write**, not a wasted request: when an effect's inputs change mid-flight, two responses
race and the last to ARRIVE wins — which may be for inputs the user has already left. This project has
shipped and fixed that class before (S123 TASK-12301).

**Census discipline**: the scope is not "every effect". It is effects whose dependencies can change
while a request is in flight. Mount-only fetches (`useApprovals`, `useForest`, `usePositionOverrides`,
`useWageTypeMappings`) cannot exhibit it and were deliberately left alone. Two hooks that *looked*
guarded (`useAdmin`, `useSkema`) were not — the grep matched the word *"ignored"* in prose comments.

**Fixed**: 13 hooks + 3 component-level sites (`AuditLogView`, `MyPeriods`, `AgreementConfigEditor`),
standardised on the existing in-repo `latestRequestId` ref pattern rather than introducing a third
convention. `AuditLogView` fetches imperatively on paging, so its race is reachable despite `[]`
dependencies — and because its handler also calls `setPage` from the response, a stale landing would
make the pager itself jump backwards. `AgreementConfigEditor` also sets the ETag, so a stale landing
would arm the next `If-Match` with the wrong version — a display race becoming a write failure.

**Deliberately NOT done**: threading `AbortSignal` through the generated `apiClient`. The defect is the
stale write, not the wasted byte, and the client is generated code (PAT-012) — a larger blast radius
for no additional correctness. Revisit only if a wasted in-flight request becomes a measured cost.

**Verification**: `staleResponseGuard.test.ts`, and the guard was **falsified** — with the guard
removed, 3 of its 4 tests fail. The 4th (unmount) passes either way, because React 18 made
setState-after-unmount a silent no-op; it is labelled in-file as a crash guard that does NOT exercise
the stale-response guard, rather than being left to read as evidence.

## F6 — PARTIAL (S126). Read the limitation before acting on this.

F6 is measure-then-decide: the remedy (skeletons vs. delayed spinners vs. keeping the previous view
mounted) is determined by the durations, so choosing one first would be guessing.

### What WAS measured (production build, 2026-08-03, post-F3)

Per-route lazy chunk sizes — the transfer that the Suspense fallback is waiting on:

| Route chunk | raw | gzip |
|---|---|---|
| entry (`index`) | 209.22 kB | 68.08 kB |
| `OrganisationOgMedarbejdere` | 110.00 kB | 30.96 kB |
| `Select` (shared) | 47.61 kB | 16.97 kB |
| `SkemaPage` | 34.46 kB | 11.64 kB |
| `AgreementConfigEditor` | 34.18 kB | 7.24 kB |
| `TeamOversigt` | 26.16 kB | 8.22 kB |
| `ConfigManagement` | 19.76 kB | 6.17 kB |
| every other route chunk | ≤ 10.72 kB | ≤ 3.07 kB |

**What this supports**: apart from `OrganisationOgMedarbejdere` (31 kB gzip), every route chunk is
under ~12 kB gzipped. At those sizes the fetch is tens of milliseconds on any realistic connection —
below the threshold where a spinner helps, and squarely in the range where showing one reads as a
flash of broken UI. That is consistent with F3's deliberate choice not to put a spinner in the route
fallback, and it makes "add spinners" the *least* likely correct remedy.

### ⛔ RETRACTED 2026-08-04 — THE MEASUREMENT BELOW DOES NOT MEASURE WHAT IT CLAIMS

**Do not act on the durations, the 250 ms threshold, or the "round-trips dominate" conclusion.** The
S126 Step-7a external lens found the harness invalid on two counts, both verified:

1. **It never measured an SPA route transition.** `page.goto()` performs a FULL DOCUMENT navigation —
   entry chunk, app boot and login state included — not the client-side route change F6 is about.
2. **The stop condition was already true before the route mounted.** `<main>` permanently contains
   `mainInner` and the Suspense fallback element (`AppLayout.tsx:16-17`, fallback `:27`), so
   `expect(main).not.toBeEmpty()` succeeds while the lazy chunk is still loading. The timer was read
   before the text assertion, so the recorded interval is "shell present", not "route content
   visible".

Consequences: the localhost figures are meaningless for F6; the throttled figures measure full page
loads under throttling, which is why they looked round-trip-dominated — that is a property of a cold
document load, not of a route transition. **The finding that "throttled durations do not track chunk
size" is therefore unsupported**, and so is the 250 ms threshold derived from it.

This is the register's own recurring lesson, committed to the register itself: a measurement that looks
like evidence and is not. The chunk-size table further up IS still valid (it comes from the build
output, not this harness) — but it was never sufficient on its own, which is why the durations were
sought in the first place.

**F6 returns to OPEN.** A valid harness must: drive client-side navigation (in-app link clicks or
router navigation, not `goto`), and stop on the ROUTE's own content appearing — e.g. a per-route
testid rendered by the page component — not on `<main>` being non-empty. The spec also needs excluding
from the default Playwright run, which starts the dev server and so contradicts its production-build
requirement.

### Measured durations (2026-08-03) — ⛔ SEE RETRACTION ABOVE

**Method**: production build served by `vite preview` (:3001, using the `preview.proxy` block added to
`vite.config.ts` for this — `server.proxy` does not apply to preview, which is why earlier timings
could only have come from the dev server, where on-demand transform inflates the very interval being
measured). Driven by `frontend/e2e/f6-route-transition-timing.spec.ts` (since deleted along with the
retraction — a future F6 harness starts from the requirements above, not from this spec), timing navigation → first
non-empty `<main>`. Because F3's Suspense fallback is an empty placeholder, that interval IS the blank
period the user sees. Backend: the real 7-service compose stack.

| Route (chunk) | localhost cold | localhost warm | throttled* |
|---|---|---|---|
| SkemaPage (34 kB) | 29 ms | 58 ms | **2754 ms** |
| ArsoversigtPage (8 kB) | 23 ms | 21 ms | 1755 ms |
| MyPeriods (6 kB) | 29 ms | 23 ms | 1754 ms |
| TeamOversigt (26 kB) | 26 ms | 24 ms | 925 ms |
| OrganisationOgMedarbejdere (110 kB) | 26 ms | 22 ms | 1400 ms |
| AuditLogView (5 kB) | 24 ms | 23 ms | 1531 ms |
| AgreementConfigList (7 kB) | 21 ms | 22 ms | 1059 ms |

\* ~400 kbit/s, 400 ms RTT via CDP — a deliberately pessimistic agency-VPN-shaped profile, chosen to
find the CEILING of the blank interval rather than to model a carrier.

### The finding — and it is not the one the chunk sizes suggested

1. **On a fast connection there is no problem at all.** 21–58 ms, with cold and warm
   indistinguishable. Adding a spinner here would CREATE the flash it was meant to cure. F3's choice
   of an empty fallback was correct.
2. **On a slow connection the blank interval is 0.9–2.8 SECONDS** — unambiguously long enough to read
   as a broken page, and this is the population a remedy would exist for.
3. **⚠ The throttled durations do NOT track chunk size.** `TeamOversigt` (26 kB) is the FASTEST at
   925 ms while `MyPeriods` (6 kB) takes 1754 ms and `OrganisationOgMedarbejdere` (110 kB) — 4× the
   next largest chunk — sits mid-pack at 1400 ms. So **the chunk transfer is not the dominant term;
   the number of sequential round-trips is** (400 ms RTT × document → entry chunk → route chunk → the
   page's own API calls). This is the opposite of what the chunk-size table alone implies, and it is
   why the sizes were not allowed to stand as the answer.

### Decision (threshold + remedy)

- **Threshold: 250 ms.** Below it, render nothing — every localhost measurement is 5–10× under this,
  so fast connections never see an indicator.
- **Above it, show a skeleton** in the route fallback. Every throttled measurement is 3.7–11× over,
  so slow connections always get feedback.
- A single *delayed* indicator therefore serves both regimes; a plain spinner serves neither.
- **Do NOT pursue further chunk-size reduction as an F6 remedy** — finding 3 shows it would buy
  little. If the throttled figure needs to come down, the lever is **removing sequential round-trips**
  (e.g. preloading the route chunk on nav hover/intent, or parallelising each page's first API call
  with its chunk fetch), not smaller bundles. That is a separate, larger piece of work and is NOT
  committed here.

**Implementation is deliberately not done.** F6 was scoped as measure-then-decide; the measurement and
the threshold are the deliverable. The skeleton itself is a UI task for a later sprint, now backed by
numbers instead of intuition.

## Standing convention

Any future performance analysis is written HERE as it is produced, with each finding carrying a
one-line description at minimum. A finding that exists only as an identifier in a sprint log is a
finding that will be lost.
