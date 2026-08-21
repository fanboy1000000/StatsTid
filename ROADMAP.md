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
3. **Domain completeness** — the pre-launch agreement-correctness program: real domain-expert
   engagement (Phase B) for the ~80 still-unsourced agreement cells
   ([phase-b-handoff-package](docs/references/phase-b-handoff-package.md)).
4. **Production hardening pass** — the "owed work" per CONVENTIONS.md: security disclosure posture,
   secrets/credentials (the dev JWT key + demo passwords), dependency audit, real deployment story.
   A precondition for any go-live.
5. **Go-to-production decision** — owner's call; not on the near horizon.

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
- **Demo-seed write-free rerun** — the loader-evidence arm is written but unobserved (no container
  runtime on the dev machine). [S128 FU-C]

### Usability / accessibility
- **Accessibility (WCAG)** — rises from "polish" to a genuine requirement as the target firms toward
  production; not enforced today. [CONVENTIONS.md]

### Tooling / infra / environment
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
