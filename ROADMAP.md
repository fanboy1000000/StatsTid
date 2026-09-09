# StatsTid Roadmap

> **What this is:** the project's living **forward view** — the loose path toward production, plus a
> durable **backlog** of deferred items and a **parking lot** for loose ideas to pick up later. It is
> deliberately low-fidelity: jot things here so they are not lost, flesh them out when they graduate
> into a sprint.
>
> **What this is NOT** (these have maintained homes — do not duplicate them here):
> - The product definition / end state → [SYSTEM_TARGET.md](SYSTEM_TARGET.md)
> - Architecture + technology stack → [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)
> - Decisions (deployment model, glocal principle, correction policy, multi-tenant) →
>   the ADRs, esp. [ADR-024](docs/knowledge-base/decisions/ADR-024-role-within-agreement-modeling.md),
>   [ADR-025](docs/knowledge-base/decisions/ADR-025-multi-tenant-operational-concerns.md)
> - What actually shipped (the completed-sprint ledger) → [docs/sprints/INDEX.md](docs/sprints/INDEX.md)
> - Next-sprint task planning → the individual sprint logs (goal + task decomposition + Open follow-ups)
>
> **How it stays alive (the forcing function — this doc rotted before because it had none):** at each
> sprint close, any deferred follow-up not scheduled for the next sprint is routed into the Backlog
> below; loose ideas land here ad-hoc. Items *leave* when promoted into a sprint. See WORKFLOW.md.
>
> *Reminder (CONVENTIONS.md): "production" here is the design TARGET, not a scheduled launch — this
> is a learning project. "Launch-blocking" = must be right before we would consider going live.*

---

## 1. Path to production (the arc)

Loosely sequenced buckets between where we are (S128) and a system we would consider production-ready.
Not commitments or dates — direction.

1. **Docs & governance cleanup** — *in progress.* Tracked in
   [docs/operations/docs-governance-program.md](docs/operations/docs-governance-program.md)
   (WS1 invariant model ✅; WS3 docs review; WS4 S128 follow-ups; WS5 security sweep; WS6 env).
2. **Security threat-model sweep + remediation** — a systematic STRIDE/OWASP audit of the whole
   attack surface, re-examining prior owner rulings ("known — should be revisited"), then a
   remediation sprint. (WS5; refinement drafted.)
3. **Employment lifecycle time-control** — *RUNNING.* Owner-raised 2026-08-25; refined, ruled, and
   governed by **ADR-040 "Employee timeline & as-of resolution"** (ratified 2026-08-25, S135).
   The four-increment program plan lives in `SPRINT-135.md` §Program Plan (Increment 1 =
   enforcement core, next up); named follow-ups: org/unit membership history, re-hire spells,
   the OQ-4 IMMEDIATE-pro-rating Phase-B question.
4. **Domain completeness** — the pre-launch agreement-correctness program: real domain-expert
   engagement (Phase B) for the ~80 still-unsourced agreement cells
   ([phase-b-handoff-package](docs/references/phase-b-handoff-package.md)).
5. **Production hardening pass** — the "owed work" per CONVENTIONS.md: security disclosure posture,
   secrets/credentials (the dev JWT key + demo passwords), dependency audit, real deployment story.
   A precondition for any go-live.
6. **Go-to-production decision** — owner's call; not on the near horizon.

## 2. Backlog (deferred, awaiting pickup)

Concrete items explicitly deferred, each with its source. Grouped by theme. Promote into a sprint to
action; delete the row when done.

### Security & access control
*(The WS5 sweep RAN round 1, 2026-08-14 — calibration 3/3, findings in
`docs/operations/security-finding-register.md` + `docs/sprints/SPRINT-129.md`. The items below are now
tracked as SEC-NNN rows there; this list is the pickup summary.)*

- **★ NEXT REMEDIATION SPRINT (owner-approved 2026-08-14, S130 candidate)** — the fix-next set in
  priority order, all small bounded fixes (round-2 additions folded in):
  1. ~~**SEC-009** self-approval self-guard (keystone)~~ ✅ **DONE (S130, 2026-08-14)** — choke point in
     `IsEffectiveApproverOrUnitLeaderAsync` + `ApprovalSelfGuard` at the 3 decision endpoints + a
     differential test matrix; RES-003 CLOSED. See `SPRINT-130.md`.
  2. ~~**SEC-020** `Auth:UseDatabase` fail-closed~~ ✅ **DONE (S130, 2026-08-14)** — default flipped
     false→true (fail-closed); in-memory dev creds kept behind explicit opt-in (owner ruling a);
     behavioral fail-closed test (admin01/admin→401). See `SPRINT-130.md`.
  3. ~~**SEC-027** per-service s2s identity~~ ✅ **MITIGATED (S130, 2026-08-17)** — the one active
     GlobalAdmin s2s mint lowered to least-privilege Employee; the shared-key capability residual → SEC-036.
  4. ~~**SEC-032** Position-Override → `GlobalAdminOnly`~~ ✅ **DONE (S130, 2026-08-17)** — the 4 write
     endpoints raised to `GlobalAdminOnly` (reads stay LocalAdmin, owner ruling OQ-2). Per-institution
     org-binding redesign declined; SEC-034 (same PUT handler) stays open. See `SPRINT-130.md`.
  5. ~~**SEC-033** server-side range/negativity validation + DB CHECKs on money-adjacent config numbers~~
     ✅ **DONE (S130, 2026-08-17, app-layer)** — value validation added at all 3 write surfaces (owner
     ruling OQ-1(a)/OQ-2: app-layer + non-negativity/domain-sets only). DB CHECKs + fat-finger ceilings
     DEFERRED → pre-production ledger. Surfaced a new adjacent finding **SEC-037** (legacy-migrator
     `local_agreement_profiles` — same validation class, out of scope). See `SPRINT-130.md`.
  6. ~~**SEC-015** env-only signing key~~ ✅ **MITIGATED (S130, 2026-08-17, re-adjudication — no new code)**
     — real production already fails closed without a configured key (all 5 services; pinned since S19). The
     committed well-known dev key is shared across compose + ~89 test files, so rotating it hurts dev/test →
     DEFERRED to the pre-production secrets-hygiene pass (ledger, with SEC-016/017; shared-key capability =
     SEC-036). Owner guidance: defer dev/test-degrading hardening while in development. See `SPRINT-130.md`.
  7. ~~**SEC-023** `external/send` role floor + schema~~ ✅ **DONE (S130, 2026-08-17, thorough)** — floor
     `Authenticated`→`GlobalAdminOnly` (sibling-consistent) + envelope guard (256 KB→413, object-shape→400)
     + a new in-process External test harness (9 tests, Docker-free). Real per-field schema DEFERRED (no
     external contract yet — enforce at `ExternalApiClient.SendAsync` when it exists, since the internal
     outbox-drain also bypasses the endpoint). See `SPRINT-130.md`.
  8. ~~**SEC-021** Orchestrator `tasks/{id}` ownership/scope check~~ ✅ **DONE (S130, 2026-08-18, Option A)** —
     owner ruled the per-task scope check (over the simpler floor-raise) to enable a future non-admin
     "read your own task" flow: `GET /tasks/{id}` scope-checks the subject employee + a claim-based
     GlobalAdmin bypass (fixed a terminated-subject defect), 404 for every denial. The enabled non-admin
     read path has no consumer yet (residual). See `SPRINT-130.md`.
  9. ~~**SEC-019** `claude.yml` `author_association` gate~~ ✅ **DONE (S130, 2026-08-18)** — both Claude
     workflows gated to trusted `author_association` (per-event paths; `issues: assigned` dropped). See `SPRINT-130.md`.
  10. ~~**SEC-028** CI `permissions:` block~~ ✅ **DONE (S130)** — top-level `contents: read` · ~~**SEC-031**
      frontend CSP header~~ 📋 **DEFERRED (owner ruling c) → prod server-header (ledger)** — a meta CSP can't be
      both dev-safe and strict · ~~**SEC-034/035**~~ ✅ **DONE (S130, 2026-08-18)** — SEC-034 reframed by review
      (not a re-key/500 — an audit-infidelity bug: the PUT stamped audit/event with the body's wrong identity;
      fixed with a 409 identity-immutability guard before any emit) + SEC-035 fail-loud supersession-audit
      helper (invariant-protected today; hardened anyway). See `SPRINT-130.md`.

  ✅ **S130 FIX-NEXT BACKLOG COMPLETE** — all 10 items dispositioned (fixed / mitigated / deferred-with-recorded-
  residual). Deferred residuals live in the register's pre-production ledger, tied to the go-serious gate.
  Details + per-item evidence in `SPRINT-129.md`.
- **WS5 sweep round 2 (owner-approved 2026-08-14)** — deeper bodies of the GlobalAdmin config
  endpoints + settlement/reversal internals (round-1 confirmed the floors, not the bodies) + the FULL
  frontend sweep (SEC-025 browser-token-storage redesign folds in here). Persistence/outbox consumers
  + a dependency audit are also round-2 candidates.
- **Pre-production revisit ledger (owner request 2026-08-14 / -17 — see the register's own ledger
  section for the full table):** deliberate hobby-stage / minimal-fix choices that close a finding now
  but leave a residual to reconsider before production —
  - **SEC-020** kept in-memory hardcoded credential table (minimal ruling (a); option (b) = remove it
    entirely is deferred).
  - **SEC-036** (the SEC-027 residual) — the shared-key s2s trust model has **no per-service identity**;
    any key-holder can mint any role and `GlobalAdminOnly` gates on the role claim alone. Fix = require a
    GLOBAL scope on admin gates (b) and/or per-service `aud`/`iss` (c); amend ADR-007. Its own scoped
    task (breaks the opt-in in-memory admin login).
  - Accepted: SEC-016/017 committed dev DB + demo passwords, SEC-018 unauth mock services, SEC-029 root
    containers.
  This is the concrete security half of the "go-serious hardening pass is owed work" theme.

*(prior recon list — now superseded by the SEC register; kept for provenance:)*
- **RES-002 read-gate remainder** — 9 sibling read endpoints still ungated (7 lack a month parameter,
  so they need contract changes, not a one-line guard). [S128 R2]
- **Reopen read-fork** — a leader-reopened month reverts to DRAFT and is withheld from the leader who
  approved it; `PeriodReopened.PreviousStatus` is the candidate discriminator. Owner has not ruled. [S128 R4]
- **Self-approval class + `ORG_SCOPE_FALLBACK` ruling** — the HR/GlobalAdmin fallback and the
  recurring self-approval defect class. [carried since S125; RES-003]
- **`ProjectionBackfillService` unlocked writes** — writes projections outside the advisory lock. [S128 §3.4 exception]
- **JWT 8h expiry, no revocation list** — no runtime invalidation of a minted token. [SECURITY.md]
- **Role/user deactivation windows** — check-then-act gaps across the write paths. [SECURITY.md]
- **S91 secondary-principal binding** — accepted lateral-assignment hole; owed a dedicated pass. [SECURITY.md]
- **`Auth:UseDatabase` fail-open** — defaults false → a hardcoded credential table (`admin01/admin`). [WS5 recon]
- **Unscoped service endpoints** — Orchestrator `tasks/{id}` (IDOR) + `/execute` token-forwarding;
  `/external/send` arbitrary-JSON passthrough; RuleEngine auth-only endpoints. [WS5 recon]
- **Frontend bearer tokens in `localStorage`** — XSS → token theft; makes the browser part of the
  auth chain. [WS5 recon]
- **Tier-probe log noise** — every legitimate leader read logs a spurious "Access denied" WARNING. [S128 FU-A]

### Correctness / domain
- **Employment lifecycle time-control — RUNNING (ADR-040; program plan `SPRINT-135.md` §Program Plan).**
  **Increment 1 (S136) SHIPPED** (enforcement core, SEC-046 closed). **Increment 2 (S137) SHIPPED** (calculation
  correctness: typed EMPLOYED/NOT_EMPLOYED segments, accrual end-cap, QUAL-147 closed, dated category
  read-side). **Increment 3 (S138) SHIPPED** (temporal editing, ADR-040 D8: dated profile + agreement-code
  writes through one pure router, category EDITABLE and NOT NULL, the HR backdate worklist, the S136
  leaver-send dead-end fixed; future-dating deliberately held for Increment 4 under the "current ≠ live"
  precondition). **Increment 4 is UNCHANGED and NEXT (S141).** S140 deliberately did *not* claim any part of it: the owner ruled
  (OQ-1) that S140 = the fixed-clock conversion + the HR follow-up surface, so the increment's three defining deliverables —
  the termination screen, a dated change with an effective-date picker, and the HR-gated history timeline — all remain owed, and
  none of its acceptance criteria was met. Two items that needed no design were pulled forward into S140 because they were
  one-screen changes: the admin create form's hire date (the S137-owed item) and routing the orphaned overtime page. What S141
  inherits that it did not have before: a working HR follow-up surface to hang the termination screen's "last month sent?" and §26
  items on, and four named write forms (reconcile payout, resolve a flagged settlement, record a §21 agreement, settlement
  reversal) deferred from S140 under owner ruling OQ-4. The **"current ≠ live" precondition still gates future-dating** and is the
  reason the increment was not simply bundled into S140 — it is a read-model change with roughly 200 read sites in its blast
  radius and deserves its own refinement and plan review. Remaining scope (unchanged design inputs: the HR follow-up register's Increment-4 checklist, S139: the landing tiles, the worklist UI with its role-mismatch decision, the termination screen, the admin-create hire date, the "efter frist" tile fix computed from the owner-ratified +2/+5 deadlines, routing the overtime page) · named follow-ups: **org/unit membership history** (ADR-040 D4 tail —
  until then "which org in March?" stays unanswerable) · **re-hire spells** (D1 tail) · **OQ-4** (IMMEDIATE-
  grant pro-rating at mid-year hire → Phase B expert list). **S137-owed items by increment:** Increment 3 — **all three DELIVERED in
  S138**: the retroactive-correction window pin (the Docker pin that exports a windowless month, records a
  mid-month end date, corrects, and asserts the claw-back deltas cover only post-end days with the manifest
  carrying the NOT_EMPLOYED suffix); the `EmploymentWindow.Overlaps/ClipTo/FirstNotEmployedDay` helpers,
  which replaced three hand-rolled window∩range predicates (PAT-027); and the backfill write-once precondition,
  which the NOT-NULL tightening was built on · Increment 4 — the admin CREATE form surfaces the hire date pre-filled
  with today and editable, and the edit path allows backdating (because the S137 "hired today" default
  makes an undated create unable to back-fill pre-creation registrations until the date is corrected) ·
  TASK-2010 (retire the `[Obsolete]` planless shim): S137's PCS ctor coupling guard + window/profile-date
  hydration block are part of its removal scope (the constructor is becoming a policy site — Reviewer NOTE).
- **"Current ≠ live" read model — the INCREMENT-4 PRECONDITION for future-dating (owner ruling 2026-09-02, ADR-040
  §Amendment 2026-09-02).** Four readers treat the open-ended row as "current": `UserAgreementCodeRepository.
  GetCurrentAsync` (feeds the LOGIN token, `AuthEndpoints:85`, and the settlement's today-agreement), the profile
  GET/ETag (`GetByEmployeeIdWithVersionAsync`), the profile DELETE pre-read, the profile PUT's lock; plus the two
  live caches `users.agreement_code` / `users.employment_category` (~200 read sites). Before any future-dated row
  may exist: those readers become as-of-today readers, and the caches get an explicit strategy (dated reads vs
  derived-at-write with a refresh on the effective date — a scheduler question D8 had waved away). S138 ships
  backdating only; Increment 4 = picker + this precondition. [S138 refinement · ADR-040 Amendment 2026-09-02]
- **AlignedWindow rules at a GENUINE split in the live rule set (QUAL-149; the S64 F4-1(b) gap — re-registered,
  it had fallen out of this file).** A mid-month part-time/position change while employed, or two spells in one
  month, is REFUSED by the planner (ADR-016 D4) in the Payroll host's live wiring → raw 500 at
  `calculate-and-export`. S137's owner ruling cleared hire/leave edges as truncations only. Fix needs a
  DOMAIN decision (how a weekly norm pro-rates across a mid-week fraction change) → reclassify norm/overtime
  as Mergeable with a pro-rating merger, or complete the shrink stub / pre-split contract. Increment 3
  candidate. **Coupled: QUAL-150** — the wage-type-mapping natural key is snapshotted once per plan from the caller
  profile (ADR-020 D1.5), so the moment profile-change splits become plannable, a mid-period POSITION change
  would map the second segment with the old position's lønart; the per-segment dated key must land FIRST
  (Step-5a Codex, S137). [S64 F4-1(b) · S137 TASK-13707 · QUAL-149/150]
- **Gap-fill corrections record the new history but do not re-record the consumption inside it** —
  S138 RULED DEFERRAL, surfaced by the Step-5a Reviewer and recorded here so it is not left living only as a
  code comment (the S125/F4 lesson: a deferral that exists at the point of occurrence is one nobody sweeps
  for). The profile PUT's revaluation resolves each absence day's committed profile on its own connection;
  for router cases E (before the first row) and G (inside a gap) there IS no pre-write row covering those
  dates, so the resolver returns null and every absence in the newly-filled stretch keeps its recorded
  feriedage — narrower than the increment's own goal ("consumption in exactly that interval is re-recorded").
  The recorded values are stale, not wrong, and exposure is small (a gap exists only after a
  delete-then-recreate). Fix shape: build the in-hand profile from the PRECEDING row's unchanged dimensions —
  the same source the writer's category fallback now uses — and pin it; it changes what a correction WRITES,
  which is why it wants its own tests rather than a Step-5a bolt-on. Comment at the site:
  `EmployeeProfileEndpoints.RevalueAbsencesInIntervalAsync` (the `datedProfile is null` branch).
  [S138 Step-5a Reviewer WARNING 3]
- **Forskudsferie (§7 advance-vacation) cap for LEAVERS — site 6.** `SkemaEndpoints` caps VACATION forskud at
  earned-to-`ferieaarEnd` regardless of a recorded leave date (manager approval IS the §7 agreement). Capping at
  the leave date would change bookability — a genuine domain fork, deliberately NOT taken in S137's D9 end-cap
  (sites 1–5, 7, 8 opted in). Phase-B-adjacent domain question. [S137 refinement scope cut]
- **Demo-seed write-free rerun** — the loader-evidence arm is written but unobserved (no container
  runtime on the dev machine). [S128 FU-C]

### HR operations — the follow-up processes (owner-raised 2026-09-03)

- **ANALYSED (S139) → the register.** The owner-raised inventory (2026-09-03, "the worklist records the debt, but nothing
  escalates it") is now `docs/operations/hr-follow-up-process-register.md`: **15 HR/admin hand-offs**, one row each
  (trigger · accountable role · deadline + source · surfacing · aging · resolution + audit · decision-readiness), plus
  gap rows, ops rows and the ruled-out list; both review lenses APPROVED at cycle 3. Headlines: almost nothing is
  visible (2 embedded tiles, 3 API lists, 1 write-only, 9 with no surface), nothing escalates, and only ONE hand-off
  has a stated deadline (§21 transfer, 31 Dec) — the other 14 wait for "by when" to be ruled. The biggest gap sat
  outside every sweep: every approved month reaches payroll only by a manual per-employee export call (HRP-022).
  **Recommendation: shape 3** (one HR landing page, one tile per process, per-process lists behind them). **Owner ruled 2026-09-08 (post-close):** shape 3; the §21 reminder from 1 November (projection-based until the close);
  the month-end + 2 / + 5 deadlines ratified as provisional institutional defaults with the export cutoff at the manager
  deadline (HRP-011/012/022 now decision-ready — four of fifteen); the register's Increment-4 checklist is the design
  input for S140. [S139 TASK-13904]
- **✅ BUILT (S140) — the surface exists.** The register's recommendation is now a product: route `/admin/opfoelgning` under the
  Local-HR tier, one tile per process, each process's list behind it. **Nine new HR-only read operations on eight new paths** back it (the §21 record read rides the existing `/api/vacation-transfer-agreements/{employeeId}` path), all org-scoped
  through the actor's accessible organisations and filtered on the subject's *current* organisation: settlements flagged for manual
  review (including the refused terminations that write no row at all and were previously unfindable), settled terminations awaiting
  a §26 request, the §21 fifth-week list plus the first-ever read of what was recorded, months past either deadline, a leaver's
  final month, approved months never exported, uncovered approvers, and employees who cannot register time. The backdate worklist
  got its first screen. The two ratified deadlines are now derived in one place and actually read, so the organisation page's
  "efter frist" tile stops claiming an aging computation that did not exist (QUAL-163). **Deliberately not built:** the four write
  forms those lists point at (owner ruling OQ-4 — they are S141 items), so the lists say "handled via API today" rather than
  offering a form. Four findings registered from the work: QUAL-164 (a start date reported from two clocks), QUAL-165 (a blocked-row
  rule that lives only in the screen), QUAL-166 (generated contracts under-describing required inputs), QUAL-167 (deactivation
  without an end date accruing overdue months for ever). [S140 TASK-14003/14004/14007/14010]

### Usability / accessibility
- **Accessibility (WCAG)** — rises from "polish" to a genuine requirement as the target firms toward
  production; not enforced today. [CONVENTIONS.md]

### Tooling / infra / environment
- **Fixed clock — ✅ DONE (S140).** Both tranches are now converted. S140 finished `QUAL-154`: the six remaining suites onto one
  constant anchor, three product seams widened plus a fourth found in passing (the cross-org transfer date), four probe legs, and
  the annual 1-September hard failure defused by a fact that calls the real resolver. The lasting lesson is a **method** correction,
  not a count: the project's pinned clock-census regex cannot see `DateOnly.FromDateTime(<local>)`, because the clock read is one
  assignment away — that blind spot hid a business date through two conversion sprints, and the widened pattern (now in QUAL-155's
  reproduce step) immediately found `QUAL-164`. Converting also surfaced **five latent "one operation, two clocks" defects**, one of
  them an auditability defect where a database row and the event meant to reconstruct it took their dates from different clocks.
  [S140 · QUAL-154 FIXED; QUAL-155/156 updated; QUAL-164 new]
- **Fixed clock — the original second-tranche entry, kept for provenance.** S139 FIXED `QUAL-153` for the eight suites on the profile,
  agreement-code and approval-projection paths (shared `FixedTimeProvider`, `WithFixedToday`, the probe, the product
  clock seam). Six more hazardous suites wait for seams the OQ-1 (a) ruling did not name (`DelegationExpiryService.cs:86`
  SQL; the designated-approver authorizer's fallbacks; `SkemaEndpoints.cs:218`; the `ApprovalEndpoints` as-of reads) plus
  a secondary `.Month/.Year/.DayOfWeek` scan over ~65 files the offset proxy classified blind. **Owner ruled 2026-09-08: the FIRST task of S140**, before Increment 4's own work. [S139 · QUAL-154, QUAL-155, QUAL-156]
- **Docker on the dev VDI** — impossible without nested virtualization; an IT ticket, may be declined. [S128 FU-E]
- **SDK/toolchain fragility on the VDI** — SDK 8 vanished once (restored); Python absent (openapi
  gates run CI-only from here). [S128 FU-E]
- **`SkemaPage` 7203-pin vitest flake** — one absorbed CI flake; graduates to a finding on recurrence. [S128 FU-D]

### Governance / docs
- **KB Tag & Domain indexes** — frozen ~S17, omit newer entries (completeness of the main INDEX is
  CI-checked; these secondary indexes are not). [WS3 / C4] *S131 additive facts: the gate gap is
  structural — `check_docs.py:59-76` checks link-PRESENCE only, so table placement/completeness is
  invisible to CI; the domain index also carries two different rows both labelled "SharedKernel".
  (Cross-ref only — tracked here, not a QUAL row.)*

### Quality (S131 audit — CLOSED 2026-08-20; the fix-next remediation is the S132–S134 program)
- **★ S132–S134 — the S131 fix-next set (owner-approved 2026-08-20; re-ruled into a 3-increment PROGRAM
  2026-08-20 — see the Impact Assessment below)**: the Critical daily-rest defect
  (QUAL-001) + **all 27 High rows** (21 as-swept + 6 owner-promoted under the "family alone = High"
  ruling: QUAL-027/036/090/095/110/111) + the **six adopted gate proposals** (QUAL-069/072/073/074/096/121
  + QUAL-141 the doc-freshness hard-fail). QUAL-133's reversal probe lands in S132 (owner ruled fix-now).
  The 112 Medium rows stay register-tracked for themed follow-ups. Full register:
  `docs/operations/quality-finding-register.md`; adjudication + owner rulings:
  `docs/sprints/SPRINT-131-adjudication.md`; per-row provenance:
  `docs/sprints/SPRINT-131-consolidated-findings.md`.
- **5 findings mirrored to the SEC register** (SEC-038…042, owner ruling); **SEC-004 CLOSED** (premise
  retired by the S92–S95 flat-authority reform). Both fold into the S132 work.
- **QUAL-123 → domain-semantics track** (owner-routed): the 48h reference-period question needs
  domain truth (EU Working Time Directive averaging), joining the Phase-B agreement-cell engagement.
- **⚠ NAMED GO-LIVE PRECONDITION (§15 stk.1)**: the SPECIAL_HOLIDAY export handler omits the under-lock
  REVERSED probe its siblings carry (QUAL-133). Medium while the go-live gate is dormant (two verified
  gates); **Critical-class the moment `Settlement:GoLiveDate` is configured** — the probe MUST land
  before that gate opens (the code's own :679-686 note, now register-tracked).
- **Serve the reporting period on the settlement overview** — the admin StrukturPanel renders a
  hard-coded "Maj 2026" label for every period (QUAL-122); the fix depends on serving the real period,
  deferred at the S123-era port. Retitle/re-pin the placeholder test with it.
- **S132 coverage follow-ups**: lizard-artifact re-run over D2's ~30 census-identified unread
  over-threshold regions; the SCD-2 write-path clone family (15 members) divergence check.

#### Impact Assessment — S131 fix-next re-ruled into a program (Tier-2 re-prioritisation, 2026-08-20)

*Trigger:* S131 ruling 9 blessed the whole fix-next set (Critical + 27 High + 6 gates) as a **single**
sprint. The S132 refinement's dual-lens review found a 34-item close-review diff is not adversarially
reviewable and that interleaving live product fixes with the ~45-test outbox test-conversion makes a red
test un-attributable. The owner **re-ruled** the shape to a program (OQ-1 → program). This is a Tier-2
cross-cutting change (2+ sprints), so per WORKFLOW.md:163-172 it is recorded here before execution.

*Affected sprints & how:*
- **S132 (was: the whole set) → the correctness + safety core.** Critical QUAL-001 (+ RED-on-old test
  QUAL-021 + the boundary-threshold leg of QUAL-114); QUAL-133 reversal probe; QUAL-002 encoding;
  QUAL-006/007 (swallow fixes); QUAL-004/005 (diverged families); the ruling-5 SEC items (SEC-039/040/041
  remediations + the SEC-004 verify test); gates QUAL-141 + QUAL-069 (the latter only on a clean payroll
  warning count). **Scope grew** vs. the refinement's first cut: OQ-2 was ruled (b) — day-attribution is
  fixed now, not deferred — so QUAL-001 extends beyond `CheckDailyRest` into `CheckWeeklyRest` +
  `CheckMaxDailyHours`, **gated on a new domain-truth PRE-TASK** (which calendar day post-midnight hours
  belong to; dual-lens, cites Arbejdstidsloven; owner confirms the rule before code).
- **S133 (NEW) — test integrity.** QUAL-013/014/015/016/017/018/019/020/022 + the test-family promotions
  QUAL-095/110/111 + the D4 dead-code batch (QUAL-027/036) + gates QUAL-096 (DemoSeed in CI) + QUAL-121
  (FE fixture-contract binding) + QUAL-072/074/073 (post warning-count-freeze).
- **S134 (NEW) — audit-scope + observability + docs.** QUAL-003 + QUAL-009 IN FULL: OQ-4 was ruled (b) —
  **build the link** — so `AuditLoggingMiddleware` is registered in the Payroll/calc host and the ADR-016
  D10 `segment_manifests`⋈`audit_log` join works end-to-end **as written (ADR-016 NOT amended)**; +
  observability QUAL-008; + the doc-fix pass (QUAL-010/011/012/093 + the enumerated D7 rows + QUAL-090) +
  SEC-038/042.

*Splits/merges/adds/removes:* no previously-planned sprint is merged or dropped; S133 and S134 are new
increments carved from the S132 set. No task is removed — every ratified High/Critical/gate is assigned to
exactly one increment (re-swept by the internal Reviewer at refinement cycle 3: Critical + 27 High [6 S132
+ 12 S133 + 7 S134 + 2 correlation/audit] + 6 gates all placed).

*Phase-range impact:* none — this is a remediation program riding on top of the roadmap phases, not a
re-sequencing of a numbered phase. QUAL-123 stays routed to the domain-semantics track (unchanged);
day-attribution (OQ-2b) joins that same track as its OWN item for the domain-truth analysis, then its code
lands in S132.

*Coverage-tracker projection:* the fix-next burn-down is now three sprint closes (S132→S133→S134) instead
of one; each increment closes on its own dual-lens Step-7a with an independently reviewable diff.

## 3. Loose ideas (someday-maybe; low commitment)

Jot half-formed ideas here; move up to the Backlog or a sprint if one earns it.

- **Auto-generate a fresh onboarding guide from the canon** — we deleted the Sprint-15-frozen
  `SYSTEM_DOCUMENTATION.md` (it rotted with no forcing function). If the project ever needs long-form
  onboarding again (e.g. a team joins), *generate* it from the current canon docs rather than
  hand-maintaining a snapshot — a doc that can't drift because it's derived.
- *(add ideas here)*
