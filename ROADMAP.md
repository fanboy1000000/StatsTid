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
*(Most of these are the "revisit register" candidates the WS5 security sweep will re-attack — a past
ruling is re-examined, not shielded.)*
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
  CI-checked; these secondary indexes are not). [WS3 / C4]

## 3. Loose ideas (someday-maybe; low commitment)

Jot half-formed ideas here; move up to the Backlog or a sprint if one earns it.

- **Auto-generate a fresh onboarding guide from the canon** — we deleted the Sprint-15-frozen
  `SYSTEM_DOCUMENTATION.md` (it rotted with no forcing function). If the project ever needs long-form
  onboarding again (e.g. a team joins), *generate* it from the current canon docs rather than
  hand-maintaining a snapshot — a doc that can't drift because it's derived.
- *(add ideas here)*
