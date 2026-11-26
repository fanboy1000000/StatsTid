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

Loosely sequenced buckets between where we are (S148 closed, S149 next) and a system we would consider production-ready.
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
   enforcement core, next up); named follow-ups: org/unit membership history (the organisation half dated in S148,
   C-13; unit membership still undated), re-hire spells,
   the OQ-4 IMMEDIATE-pro-rating Phase-B question.
4. **Domain completeness** — the pre-launch agreement-correctness program: real domain-expert
   engagement (Phase B) for the ~80 still-unsourced agreement cells
   ([phase-b-handoff-package](docs/references/phase-b-handoff-package.md)).
   **The overtime/flex model program: ADR-043 ACCEPTED 2026-10-29 (S147); S148 built stage 0 and the config stage; S149 builds stage 1.**
   Two verified research dossiers (`docs/references/overtime-flex-model-research.md`,
   `docs/references/agreement-cells-reverification-research.md`) showed the overtime model wrong in kind; in S147 the
   owner decided all nine of ADR-043's forks and three tender findings (QUAL-232..234), each on the recommended
   option. In plain words: an hour above the month's norm is flex unless a leader ordered or accepted it; only those
   hours become overtime (+50 %) or an AC claim, and they leave flex. It is built in self-contained stages, each with
   its own evaluation-model marker: **stage 0** stops paying what is wrong today (on-call, call-ins and travel refused;
   the child's-sick-day quotas fixed; the rest check reads Skema's clock times); the **config stage** makes the
   editors' values reach pay, dated and frozen; **stage 1** brings the monthly norm and flex by default, the holiday
   calendar split and recorded forfeiture; **stage 2** decided hours, +50 % overtime, the +25 % supplement and § 14,
   and the overtime account; **stage 3** AC extra-work claims. S148's refinement started from the build input
   (`.claude/plans/s147/S148-BUILD-INPUT.md`). **Owner scope ruling 2026-11-09: S148 carries stage 0 and the config stage** (no demo payslip changes, so
   the sprint's acceptance pin is identical demo pay lines; the one arithmetic change, QUAL-236's new credited
   call-home day, has no demo row); stage 1, where every HK and PROSA payslip moves, gets its own sprint (S149), then
   stage 2 and stage 3.
   **S148 built stage 0 and the config stage, and no pinned demo pay line moved** (PIN-1, six employee-months in two pinned worlds; closed 2026-11-26, detail in
   `docs/sprints/SPRINT-148.md`). In plain words: on-call, call-ins and travel can no longer be registered, and payroll
   refuses a stored one rather than paying it as ordinary work (QUAL-202; QUAL-232's interim); the child's sick days
   follow the agreements (QUAL-236); a day's paid absence is worth at most its planned hours minus the hours worked
   (QUAL-237; ADR-043 D2); the 11-hour rest check reads Skema's clock times and applies the law's 24-hour look-back
   (QUAL-253; ADR-044); a Skema cell can be emptied (QUAL-248); and payroll computes only with the published, dated
   agreement configuration, position override and local profile in force on each day, frozen into the month's proof
   (QUAL-233, QUAL-204). S147's six tidy inputs were built in stage 0. The employee's organisation is dated (owner
   Q-1; QUAL-206), and the editors gained an institution's customary days, the local profile's eighteen new
   settings and two HR facts per person, recorded now and read by pay from S149 or stage 2.
   **Next: S149, stage 1** — the monthly norm and flex by default. Every HK and PROSA payslip moves, so S149 re-pins
   PIN-1 at the recorded seeds (47 and 45). **S149's first task (owner ruling 2026-11-26): QUAL-284**, a backdated norm change leaving stored paid absence above the day's new worth: ADR-043 D2's engine-side credit, min(stored, planned − worked), with a quota's consumption clipped to match. **The re-pin should widen PIN-1** (S148 Step 7a, Fable W2): a flex payout, a sick day and a second month with a carried balance, none of which the S148 goldens hold. **Passed on to S149's refinement** (the S148 refinement's "Items ADR-043
   assigned to this refinement, passed on", ruling R-11; ADR-043's S148 notes): QUAL-225's HR-entered opening flex
   balance; how AC's annual-norm variants settle flex; whether HRP-024 ("approve the previous month first") retires
   or is re-stated; the customary-day and HR-facts writers joining the pay-inputs lock and the correction worklist
   with their first reader (S1-6, S1-9); the local profile's eighteen settings frozen with their first reader (§ 2,
   Correctness / domain); and whether the demo's generated hours honour customary days, decided before the re-pin
   (the demo worlds now hold customary days in their measured months). **Passed on to stage 2's refinement:** where
   the overtime movements are stored (D8); a leaver's owed time off and saved days (R-C); saved days, after
   research cell R-A. Still to close when built: QUAL-189, QUAL-216..222, QUAL-226, QUAL-227, QUAL-232 and QUAL-234
   (QUAL-233 and QUAL-237 closed in S148); retires S145's weekly settlement machinery (frozen by OQ-2, with
   QUAL-209/210/212/213).
   The source register's unreliable citations (QUAL-215) make the Phase-B engagement above more urgent.
5. **Production hardening pass** — the "owed work" per CONVENTIONS.md: security disclosure posture,
   secrets/credentials (the dev JWT key + demo passwords), dependency audit, real deployment story.
   **The dependency part no longer waits for this pass** (owner-raised 2026-10-26): see §2 "Tooling / infra /
   environment", *Dependency inventory, security and updates*. The .NET 8 runtime leaves support on 2026-11-10.
   **Its checklist is the conformance register** (owner-raised 2026-10-26; see §2 "Governance / docs",
   *Standards and requirements conformance*): ISO 27001, the tekniske minimumskrav, NIS2, GDPR, Rigsarkivet,
   accessibility.
   A precondition for any go-live. **Includes authentication through Statens SSO** (owner-raised 2026-10-16;
   SAML 2.0 / OIOSAML Service Provider; see §2 "Security & access control"). Since the tender review (2026-11-13) SSO
   is also product scope, as part of epic E7 (item 7); its sign-in half can be built before this pass.
6. **Go-to-production decision** — owner's call; not on the near horizon. (It stays the last step whatever the
   numbering: item 7 runs before it.)
7. **Product scope for a state buyer: the tender review** (owner, 2026-11-13). The owner reviewed 215 requirements
   from two historical state tenders and marked 128 "should meet" (`docs/references/tender-requirements-analysis.md`,
   § "Owner review (2026-11-13)"). They group into twelve epics, each a row in § 2: rules and configuration (E1,
   mostly item 4's program), vacation end to end (E2), the HR and leader back office (E3), requests and
   notifications (E4), reporting (E5), shift planning (E6), people and access from the state's systems (E7), the
   integration foundation (E8), pay and absence reporting to SLP (E9), Navision Stat (E10), audit and compliance
   (E11), and usability, mobile and the user manual (E12). It runs alongside items 4 and 5, not after them; it is
   numbered 7 only so existing "§1 item 5" citations stay valid.
   **Owner rulings 2026-11-13** (`docs/sprints/SPRINT-148.md`): (1) shift planning is built into StatsTid in stages,
   on ADR-043 stage 2's roster record; (2) an "extract" is an on-screen report or statistic, plus documented dataset
   endpoints a tool such as SAS pulls with a service login; (3) the first lane among the work that needs no outside
   input is the HR back office (E3, then E4), alongside or after item 4's overtime program, which stays the
   priority; (4) the HR-system and user-administration feeds are the source for what they carry, and fed facts are
   read-only in StatsTid; (5) the S124 visibility rule and ADR-034's export lock both stay.
   **Still waiting on the owner:** the integration specifications (SLP, Navision Stat, the HR system, the
   user-administration system) and the master-data list. E7's feed half, E8, E9 and E10 (53 of the 128 ids) wait on
   them, wholly or partly. Detail: `.claude/plans/tender-review-roadmap-draft.md` (draft 1, adjudicated 2026-11-13).

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
- **★ Authentication through Statens SSO** (owner-raised 2026-10-16: *"The system should handle auth through
  statens SSO"*). **Source:** Økonomistyrelsen, *Tilslutning af fagsystem på Statens SSO*, 14-01-26 (4 pp.),
  <https://oes.dk/media/oekeggzj/statens-sso-tilslutning-af-fagsystem-14-01-26.pdf>.
  - **Why.** Statens SSO is the common single sign-on for the state sector, run by Økonomistyrelsen. A state
    institution's employees would sign in to StatsTid with their own institution's login, instead of a
    StatsTid username and password.
  - **What the guide requires of a connected system** (*fagsystem*):
    - **A federation component acting as a SAML 2.0 Service Provider** (SP), which receives and validates the
      authentication from Statens SSO. Any product will do if it supports SAML 2.0 and the OIOSAML
      specification (Digitaliseringsstyrelsen, <https://digst.dk/it-loesninger/standarder/oiosaml-profiler/>).
      The guide names AD FS 2.0–4.0, SimpleSAMLphp, Shibboleth, OIOSAML-based solutions, Ping Identity and
      Safewhere Identify as examples.
    - **A two-way SAML 2.0 metadata exchange.** StatsTid delivers its SP metadata, preferably as a public URL,
      which Statens SSO monitors and refreshes automatically; otherwise as an XML file. Delivery goes through
      *Serviceportalen hos Statens Administration* (category *Øvrige* → *SSO*). StatsTid configures
      Økonomistyrelsen's IdP metadata:
      - test: `https://statens-sso-test.oes.dk/runtime/saml2/metadata.idp`
      - production: `https://statens-sso.oes.dk/runtime/saml2/metadata.idp`

      Metadata contains no personal data and can travel by e-mail or the internet.
    - **Cryptography.** SHA-256 only, for hashing and message signing; HTTPS with TLS 1.2 for transport.
    - **The claims Statens SSO presents:**

      | Claim | Meaning |
      |---|---|
      | `https://modst.dk/sso/claims/cvr` | the institution's CVR number (fixed per institution) |
      | `https://modst.dk/sso/claims/userid` | the user's unique id, typically the e-mail (UPN by agreement); *the claim connected systems use to identify the user* |
      | `https://modst.dk/sso/claims/uniqueid` | the user's id in the institution's directory, typically the AD `objectGuid` |
      | `https://modst.dk/sso/claims/assurancelevel` | `3`: Økonomistyrelsen requires users to come from a secured network |
      | `https://modst.dk/sso/claims/logonmethod` | how the user logged on, e.g. `usernamepassword`, `protectedtransport` |
      | `http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name` | the user's e-mail |
  - **What it means for StatsTid** (to establish at refinement; nothing here is decided):
    - **No role or rights claim arrives.** Statens SSO authenticates; StatsTid keeps authorizing. The roles and
      org scopes in StatsTid's own model stay authoritative (`docs/SECURITY.md`, ADR-009). So the likely shape is:
      the Auth service becomes the SAML SP, validates the assertion, maps the user to a StatsTid user, and mints
      StatsTid's existing JWT, leaving the downstream services unchanged. That is an inference to verify, not a
      design.
    - **Identity keying.** Which claim keys the StatsTid user? `userid` (e-mail) can change; `uniqueid` is stable
      within an institution and is plausibly unique only together with `cvr`.
    - **Provisioning.** Are users pre-provisioned by HR in StatsTid and matched at first login, or created on
      first login? An unknown user must be refused, not given a default role.
    - **Multi-tenancy.** `cvr` maps the login to an institution, and so to StatsTid's org tree (ADR-025).
    - **Enforcement.** Require `assurancelevel` 3; decide whether `logonmethod` matters. Logout, session lifetime
      and the existing 8 h token with no revocation (the rows below) need a ruling together. The guide does not
      cover single logout.
    - **Dev/test ergonomics.** The local login, demo users and Docker test harness must keep working without
      Statens SSO (see the owner's standing preference on dev/test ergonomics). Likely as an explicit non-production
      mode, ruled alongside SEC-020's in-memory credential table.
    - **The library.** A maintained .NET SAML 2.0 SP library that supports the OIOSAML profile. Choosing it is a
      dependency decision for the refinement, checked against the OIOSAML profile's requirements.
    - **Onboarding** is an operational step with Økonomistyrelsen (the metadata exchange through Serviceportalen,
      test before production). It is not code, and it needs a reachable SP metadata URL.
  - **Where it fits.** It belongs to the path to production (§1 item 5) and to SYSTEM_TARGET's integration and
    security requirements. It needs its own refinement (the `refine-requirements` gate) and an ADR for the
    authentication model before any sprint.
  - **Tender review (owner, 2026-11-13).** "We will use Statens SSO" (EM A19). A login through each institution's
    own directory (EM A19, VD 6) and self-service passwords (EM A20) are not relevant. Whether the local login
    survives outside dev/test is ruled in the authentication ADR, with SEC-020. The sign-in half belongs to epic E7
    (§ 1 item 7) and waits on nothing; provisioning users and roles waits for the specifications (the row below).
- **★ People and roles from the state's systems (tender epic E7; owner, 2026-11-13).** The owner's direction:
  people are created, changed and closed through the HR-system integration (EM A33, VD 33); role assignments come
  from an external user-administration system (EM A34); StatsTid keeps its own role model and adds a shift planner
  and a shift user with shift planning (VD 8). **Owner ruling 2026-11-13: the feeds are the source for what they
  carry.** A fed fact is read-only in StatsTid and corrected at the source; StatsTid edits only what no feed
  carries. Until a feed is built, today's editors stay the source and nothing changes. Once it is, the termination
  screen, the effective-date picker and the role-grant screen become views for fed facts, and dev/test needs a
  stand-in feed (the `mock-payroll` pattern). **Needs:** an ADR for the inbound people feed, in which fed changes
  are written through ADR-040's dated writers, each recorded with its source, so the audit trail and the backdate
  worklist fire as for a human edit, and a failed or partial feed run changes nothing; a provisioning interface that
  maps role assignments onto StatsTid's roles and Organisation-set scopes, audited with their source; the master
  data on the owner's list (EM A6), field by field, with a DPIA for any CPR number; a production web entry with
  HTTPS (EM A18). **Re-decides, at this epic's refinement:** ADR-035 and ADR-009 (where assignments come from),
  ADR-040 (who writes employment facts), ADR-029 (which personal data is held), `SYSTEM_TARGET.md` § F.
  **Open until the specifications arrive:** which system is the HR source (Statens HR or SLP's HR part); whether
  the line manager is fed, which would make approval edges fed data; how an administrator gets access when the
  user-administration system cannot supply it. **Waits on:** the integration specifications, the master-data list,
  the user-administration system's interface. Ids: EM A6, EM A18, VD 3, VD 33.
- **Four-eyes on pay-affecting configuration, corrections and settlement payouts (SEC-060; tender VD 35; epic
  E11).** S147 put two different people on every month (SEC-053, SEC-054). One person alone can still publish an
  agreement config, change a wage-type mapping, edit a local profile, run a correction (`/recalculate`) and trigger
  a settlement payout. **From S148's config stage a publish or a profile edit changes pay** (ADR-043 D12); until
  then it changed screens only. Each change is audited, dated and frozen into the month's proof, and a past-dated
  publish lands the paid months it reaches on HR's worklist, so it can be detected but is not prevented. Needs an
  ADR at E11's refinement naming who signs what (amending ADR-014's publish lifecycle, with a note on ADR-043 D12);
  a second signer also means a second administrator in every demo and test flow. Not S148 scope.
  **Owner ruling 2026-11-13: built as the first item of the compliance epic (E11), after the HR back office (E3).**

*(prior recon list — now superseded by the SEC register; kept for provenance:)*
- **RES-002 read-gate remainder** — 9 sibling read endpoints still ungated (7 lack a month parameter,
  so they need contract changes, not a one-line guard). [S128 R2]
- **Reopen read-fork** — a leader-reopened month reverts to DRAFT and is withheld from the leader who
  approved it; `PeriodReopened.PreviousStatus` is the candidate discriminator. Owner has not ruled. [S128 R4]
- **Self-approval class + `ORG_SCOPE_FALLBACK` ruling** — the HR/GlobalAdmin fallback and the
  recurring self-approval defect class. [carried since S125; RES-003] *(S147: two members of the class are closed: SEC-053, the sender who approves (strict four-eyes, owner OQ-1 = (a)), and SEC-056's self part, a leader deciding their own overtime (owner Q-A = (a)). The fallback itself was not ruled on.)*
- **`ProjectionBackfillService` unlocked writes** — writes projections outside the advisory lock. [S128 §3.4 exception]
- **JWT 8h expiry, no revocation list** — no runtime invalidation of a minted token. [SECURITY.md]
- **★ DECISION OWED — the agreement code carried on a login token goes stale when a scheduled change takes
  effect** (owner-deferred 2026-09-11, S141 planning OQ-4; owner: *"this is an important decision"*).
  **The shape.** A login token carries the holder's `agreement_code`, copied at mint time, and lasts 480
  minutes with no revocation. Today that is harmless, because a change to someone's agreement can only be
  dated today or earlier, so a token minted before the change is refreshed by ordinary expiry well inside the
  period the change affects. **S141's future-dating removes that guarantee:** a token minted at 17:00 on the
  day before an effective date carries the superseded code past midnight, for up to 8 hours into a period
  where the records say something else.
  **What must be established BEFORE deciding, and was not established during S141 planning.** The severity
  depends entirely on *what reads the code from the token rather than from the records*, and that trace was
  never done — the Orchestrator's belief that the load-bearing calculations read the records is an inference
  from the surrounding code's pattern, not a verified fact. **Do the trace first; the ruling is not
  meaningful without it.** If only display surfaces read it, this is cosmetic and self-correcting. If any
  authorization or rule-resolution path reads it, it is not.
  **The options, as framed for the owner:** (a) accept and document the window, costing nothing and adding no
  moving parts; (b) shorten the token lifetime, which taxes every login every day to shrink a window that
  opens rarely and closes itself, and which degrades the dev/test workflow this project has deliberately
  protected before; (c) resolve the agreement per request instead of trusting the token, which removes the
  window but pays a read on every request and makes authentication depend on the scheduling machinery.
  **Related:** the row immediately above (no revocation list) is the general case of the same weakness; if
  that is ever picked up, these two should be ruled together rather than separately.
- **Role/user deactivation windows** — check-then-act gaps across the write paths. [SECURITY.md]
- **S91 secondary-principal binding** — accepted lateral-assignment hole; owed a dedicated pass. [SECURITY.md]
- **`Auth:UseDatabase` fail-open** — defaults false → a hardcoded credential table (`admin01/admin`). [WS5 recon]
- **Unscoped service endpoints** — Orchestrator `tasks/{id}` (IDOR) + `/execute` token-forwarding;
  `/external/send` arbitrary-JSON passthrough; RuleEngine auth-only endpoints. [WS5 recon]
- **Frontend bearer tokens in `localStorage`** — XSS → token theft; makes the browser part of the
  auth chain. [WS5 recon]
- **Tier-probe log noise** — every legitimate leader read logs a spurious "Access denied" WARNING. [S128 FU-A]

### Correctness / domain
- **For S1-9 (the customary-days reader), from S148's 14834 stack (rulings R4 and C2-R3):** customary days are recordable since S148, but **a wrong far-future date cannot be undone in the product.** A row started on a wrong future date can only be stopped the day after it starts, and a row stopped at a wrong future date (up to 9999-12-31) blocks earlier dates for that day and scope through the overlap guard; a row stopped at 9999-12-31 can never be reinstated. S1-9 decides a void (or an editor confirmation on far-future dates). The overlap key is form-exact and leaves out `kind` (ruling 8): "every 5 June" and the single date 2026-06-05 can both be in force, as can scope "all" beside "HK", so the S1-9 reader must define precedence.
- **For S1-9 and the first HR-facts reader, from S148's 14834 and 14836 stacks (O-32; rulings 7 and 16 of the wave-6 batches B and C brief adjudication; the HR-facts stack's ruling C2-R2):** (1) a customary day may name a clock time (`FROM_TIME`) for a person with no office hours behind it; the S149 reader defines the fallback. (2) HR facts are dated today or earlier and never before the current record (QUAL-BF-009), so a known future change cannot be pre-recorded and history before the current record cannot be corrected; the first reader adds future dating, with its worklist and lock. (3) A back-dated HR-facts save supersedes everything after its date, so for a person whose agreement changed back and forth, a save dated in an earlier period can overwrite a later period's regime; the first reader needs a per-period history or a warning on a back-dated save.
- **From S148's QUAL-268 stack (owner ruling 2026-11-25, "C later, A now"):** teach the shared timeline writer (`TemporalWriteRouter`) to step over cancelled zero-length rows, so a repair writes one row and an ordinary edit fills a gap in one step; it removes QUAL-269 (a phantom "planned change" after a repair) and QUAL-270 (a gap fill that stops at a cancelled row and takes its category). The router is shared with the agreement-code and organisation timelines, so it needs an ADR-020 D2 amendment and its own task. Until then both stay registered and test-pinned.
- **From S148's QUAL-268 stack (brief adjudication ruling 9):** a per-person deep link from HR's "cannot register" list to the edit drawer (QUAL-266's row).
- **For S149 (stage 1), from S148's wave-6 batch-A adjudication, ruling 9:** the local profile's eighteen new settings (flex periods, caps and actions, office hours, the paid-absence credit basis) are saved, validated and audited since S148 but are **not yet frozen into a month's proof**: `AgreementRuleConfig` has no member for them. They are frozen with their first reader in S149, as R-21 rules for the HR facts. A manifest written before then reads them as "not set". Also for S149: a settings-only save puts paid months on HR's correction worklist although nothing reads the settings in S148 (the 14832 stack's Fable N5; told to the owner at O-17).
- **✅ BUILT IN S146 — part-time staff see their own monthly norm (QUAL-198, TASK-14602, merged `b104c4d7`, CI-verified at CI-W1).** Both screens use `weekdays × DayNorm.ForWeekday(W, fraction at month end)`. Residual inputs divergence registered as QUAL-226 (for the S147 program).
- **Backlog — needs its own refinement: a leaver's post-end-date night-shift hours are paid by no month
  (QUAL-199).** The post-midnight half of a last shift falls on a NOT_EMPLOYED day. It predates S145.
  Whether such hours are payable, and in which month, is a domain question.
- **✅ SHIPPED IN S142 — business dates are the Danish calendar day** (owner ruling 2026-09-14, S141:
  *"Why not update the system's clock to the Danish clock? We will only have Danish users."*).
  Twelve tasks, 64 production sites, the frontend included; instants stayed UTC. Recorded as
  **ADR-041**; closes QUAL-156, QUAL-157 and QUAL-172. Detail in `docs/sprints/SPRINT-142.md`.
  **✅ COMPLETED IN S143 — the client's own clock is gone too.** This entry previously read "Not carried
  over to S143: display and navigation dates are still browser-local." They were, and the phrase
  *"a different wrong calendar, not a lesser instance of this one"* was right about the calendar and
  **wrong about the stakes**. S143's refinement traced what the value feeds: the month a screen opens
  on is sent as the period envelope of `POST /api/skema/{id}/save` **and** `POST /api/approval/send`
  — the second of which *creates* the approval period. They were never display dates; they decide
  **which month a person's hours are filed under**. Owner ruling 2026-09-23: the client reads the
  Copenhagen day from the server once at app start, and the app shell refuses to render without it.
  Recorded as **ADR-042**; fifteen tasks. No frontend production source derives a business date from
  the device clock — not the four sites that seed a view, and not the six that supply STORED dates
  through the approved helper, which a Step-7a BLOCKER caught the sprint wrongly claiming it had
  fixed. An AST guard enforces the *spelling* `new Date()`; the *authority* is enforced by ADR-042
  and by migration, because no syntactic guard can see which clock a correctly-spelled call reaches.
  Detail in `docs/sprints/SPRINT-143.md`.
  The description below is kept as the statement of the problem that was solved.
  **The user-facing defect, which is why this is more than tidiness.** The frontend sends "today" as
  `new Date().toISOString().slice(0, 10)` (`frontend/src/hooks/useEditPerson.ts:44-46`) — the UTC
  calendar day. In Copenhagen at 00:30 on 1 November that returns **31 October**. So a Danish HR user
  working after midnight who records a change "from today" has it stored as effective **yesterday**.
  Not an internal inconsistency between components: a wrong date, written to the record, originating
  on the user's own screen.
  **Why the whole split exists.** The backend's same-day validator was deliberately made to use the
  UTC day *to agree with that frontend call* (`EmployeeProfileEndpoints.cs:84-90`, which says so
  explicitly). Every writer, both `users.*` caches, the login-token mint and S141's new as-of-today
  reads followed. So the two conventions are **not** a considered balance: the Copenhagen day was
  chosen once, deliberately, for Danish employment law (`CopenhagenBusinessDate`, DST-correct, already
  used by the §21 deadline and the settlement boundaries); the UTC day propagated outward from one
  convenience call in the browser.
  **What must NOT move, and why this is not a find-and-replace.** *Instants* — `created_at`,
  `updated_at`, audit timestamps, outbox ordering — stay UTC. That is correct practice and ordering
  depends on it. Only **business dates** move: which day a change takes effect, and which dated row is
  current. The two concepts share the word "today" and are otherwise unrelated.
  **The destination is already built:** `CopenhagenBusinessDate` is DST-correct (CET/CEST resolved
  through the real zone, not a hardcoded offset — the QUAL-005 bug was exactly that), is already wired
  to the `TimeProvider` seam so it stays deterministic under a fixed test clock, and is already the
  convention for the statutory boundaries.
  **Scope:** every write validator, both cache refreshes, the login mint, S141's as-of-today reads,
  the frontend helper, plus DST-boundary pins. **Deliberately NOT folded into S141** — it would touch
  precisely the surface wave 1 changed and both lenses reviewed, invalidating that review and mixing
  two unrelated risks in one change. It also needs its own analysis of which dates are legally Danish
  versus merely "now".
  **Related:** QUAL-172 registers the S141-visible symptom (two clocks disagreeing nightly). S141's
  own ruling — that the new "no row covers today" detector matches **whatever the writers use** — is
  forward-compatible: when the writers move, the detector moves with them, no exception left behind.
- **Employment lifecycle time-control — RUNNING (ADR-040; program plan `SPRINT-135.md` §Program Plan).**
  **Increment 1 (S136) SHIPPED** (enforcement core, SEC-046 closed). **Increment 2 (S137) SHIPPED** (calculation
  correctness: typed EMPLOYED/NOT_EMPLOYED segments, accrual end-cap, QUAL-147 closed, dated category
  read-side). **Increment 3 (S138) SHIPPED** (temporal editing, ADR-040 D8: dated profile + agreement-code
  writes through one pure router, category EDITABLE and NOT NULL, the HR backdate worklist, the S136
  leaver-send dead-end fixed; future-dating deliberately held for Increment 4 under the "current ≠ live"
  precondition). **★ Increment 4 SHIPPED (S141) — all three deliverables, nothing cut.** The termination screen, the
  effective-date picker, and the HR-gated history timeline all landed, along with the precondition that turned out to be nine
  deliverables rather than one: as-of-today reads everywhere, the refusal lifted at **seven** sites (the refinement counted four
  — the three missed were endpoint validators that would have shipped the picker *dead*), the concurrency token moved to the
  employee record, a soft delete that retires **and audits** scheduled rows, a refresh job so a scheduled change is reflected on
  the day it lands, and a detector for employees no record describes. **Plus B0, an owner-raised requirement no review lens had
  asked for:** a scheduled change must be visible wherever a profile is read — six surfaces, three of which needed a new backend
  field the owner ruled in. **Increment 4's own named follow-ups remain open:** org/unit membership history, re-hire spells, and
  the OQ-4 pro-rating Phase-B question. *(Superseded planning text follows, kept for provenance.)* S140 deliberately did *not*
  claim any part of it: the owner ruled
  (OQ-1) that S140 = the fixed-clock conversion + the HR follow-up surface, so the increment's three defining deliverables —
  the termination screen, a dated change with an effective-date picker, and the HR-gated history timeline — all remain owed, and
  none of its acceptance criteria was met. Two items that needed no design were pulled forward into S140 because they were
  one-screen changes: the admin create form's hire date (the S137-owed item) and routing the orphaned overtime page. What S141
  inherits that it did not have before: a working HR follow-up surface to hang the termination screen's "last month sent?" and §26
  items on, and four named write forms (reconcile payout, resolve a flagged settlement, record a §21 agreement, settlement
  reversal) deferred from S140 under owner ruling OQ-4. The **"current ≠ live" precondition still gates future-dating** and is the
  reason the increment was not simply bundled into S140 — it is a read-model change with roughly 200 read sites in its blast
  radius and deserves its own refinement and plan review. Remaining scope (unchanged design inputs: the HR follow-up register's Increment-4 checklist, S139: the landing tiles, the worklist UI with its role-mismatch decision, the termination screen, the admin-create hire date, the "efter frist" tile fix computed from the owner-ratified +2/+5 deadlines, routing the overtime page) · named follow-ups: **org/unit membership history** (ADR-040 D4 tail —
  until then "which org in March?" stays unanswerable) · **re-hire spells** (D1 tail; **S145 design input:** per-spell settlement cutoffs are needed, since today's single cutoff takes the latest-ending window, and ~~`PeriodCalculationService.SegmentIndexForLine`'s out-of-segment fallback must become a refusal; otherwise a week settled on a NOT_EMPLOYED gap day exports an overtime line dated in the gap under the preceding segment~~ ✅ **DONE (S145 Stage B, TASK-14522, R-56)** — a line dated in no EMPLOYED segment now fails the calculation (500, nothing written) instead of being mapped under the preceding segment's codes; the per-spell cutoffs stay owed, since without them such a month is refused rather than paid. Fable 5a of TASK-14511, WARNING 2) · **OQ-4** (IMMEDIATE-
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
- **✅ SHIPPED IN S145 — a month with a mid-month change is paid part by part (QUAL-149, with QUAL-150 and QUAL-186).**
  Payroll first learned to work week by week (Stage A): every ordinary month had been judged against one week's norm
  (QUAL-186), and each part's wage-type key now comes from the dated record (QUAL-150). Stage B then made a month with
  a mid-month change in part-time hours, position, category, or a Monday agreement change payable on the calculating
  routes, each part under its own codes. Overtime, the 37-hour norm and flex became timeline-aware (evaluated once
  across the whole month, each day under its own contract), and payroll hands the planner only the rules it runs
  (ADR-016 D2–D4, S145 amendments); none of the fix options below was taken. One case stays manual, an agreement
  change inside a settled week (QUAL-209), and the raw export routes refuse split months by their own reason *(retired in S147 with that reason: TASK-14716, SEC-054)*.
  Residuals: QUAL-209..213, and the design questions in the next entry. **S146 OQ-2 (2026-10-23):** QUAL-209, QUAL-210, QUAL-212 and QUAL-213 are frozen (no new work on the weekly settlement machinery); the S147 model program (ADR-043) resolves or retires them. QUAL-149 BUILT (all four Stage B evidence runs matched in CI, S145 C-6/C-7).
  Detail in `docs/sprints/SPRINT-145.md`.
  The entry below is kept as the statement of the problem that was solved.
  **AlignedWindow rules at a GENUINE split in the live rule set (QUAL-149; the S64 F4-1(b) gap — re-registered,
  it had fallen out of this file).** A mid-month part-time/position change while employed, or two spells in one
  month, is REFUSED by the planner (ADR-016 D4) in the Payroll host's live wiring → raw 500 at
  `calculate-and-export`. S137's owner ruling cleared hire/leave edges as truncations only. Fix needs a
  DOMAIN decision (how a weekly norm pro-rates across a mid-week fraction change) → reclassify norm/overtime
  as Mergeable with a pro-rating merger, or complete the shrink stub / pre-split contract. Increment 3
  candidate. **Coupled: QUAL-150** — the wage-type-mapping natural key is snapshotted once per plan from the caller
  profile (ADR-020 D1.5), so the moment profile-change splits become plannable, a mid-period POSITION change
  would map the second segment with the old position's lønart; the per-segment dated key must land FIRST
  (Step-5a Codex, S137). [S64 F4-1(b) · S137 TASK-13707 · QUAL-149/150]
  **→ S145 (owner ruling 2026-10-01: "both stages, gated").** The S145 refinement found that every month is
  wrong, not only split ones (**QUAL-186**: payroll judges a month against one week's norm), so S145 first makes
  payroll work week by week (Stage A, with QUAL-150) and only then opens split months (Stage B, QUAL-149). This
  entry was retired at S145 (the ✅ entry above); QUAL-149 reads BUILT, not FIXED, until the Stage B evidence runs
  (C-6/C-7) report.
- **Overtime and norm design questions the S145 refinement surfaced (domain questions, not defects; none is S145
  scope).** (1) **Part-time overtime band:** should a part-timer's hours between their own norm and the full-time
  37 h be plain merarbejde rather than 50% overtime? Today the thresholds scale with the fraction
  (`OvertimeRule.cs:58-59`). (2) **Multi-week norm periods:** overtime ignores `NormPeriodWeeks` (`OvertimeRule.cs:29`),
  and anchoring 4-week blocks needs the unmodelled "planning start day" (`SYSTEM_TARGET.md:163`). (3) **Absences
  and overtime:** overtime reads only time entries while flex credits absences (`AbsenceRule.cs:72-84`), so a
  week of 4 × 9.25 h with a Monday sick day shows no overtime. (4) **The absence credit is a hard-coded
  7.4 × fraction** (`AbsenceRule.cs:13`), not weekly norm ÷ 5, so the two differ if a position override changes
  `WeeklyNormHours`. The defects found alongside these are registered as QUAL-187..191. [S145 refinement rev 3,
  Fable `adjudicator` item 10]
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
  (sites 1–5, 7, 8 opted in). Phase-B-adjacent domain question. [S137 refinement scope cut] → part of tender epic E2.
- **Demo-seed write-free rerun** — the loader-evidence arm is written but unobserved (no container
  runtime on the dev machine). [S128 FU-C]
- **Absence day rules — one kind of absence per day, and absence never pushes a day past its norm (owner
  rulings 2026-10-28; QUAL-237).** Raised in owner UI testing of the Skema "Ferie og fravær" band. Nothing
  today stops two kinds of absence on one day, and the server's norm limit can be bypassed (QUAL-237).
  Owner rulings:
  1. **One absence type per day.** No two different kinds of absence on the same day (e.g. not Ferie and
     Sygdom). Part-day absence stays allowed.
  2. **A second absence type is refused with a warning at the cell**, e.g. "Der er allerede registreret Ferie
     denne dag — fjern den først". It never silently replaces the first.
  3. **Work and absence may share a day, but together never exceed the day's norm.** 4 t arbejde + 3,4 t
     sygdom on a 7,4 t day is fine; 4 + 7,4 is not. A day with no absence keeps today's rules (work alone
     has its 24 t cap, so overtime and flex are unaffected).
  4. **Too much absence falls back to the maximum as you type**, with a short note at the cell ("Højst 3,4 t
     denne dag"), instead of being refused and jumping back to the old value.
  Scheduling: backlog, not the next sprint (owner, 2026-10-28). What building it involves:
  - QUAL-237 first. The server checks only the cells in one save while the grid saves cell by cell, so any
    day rule checked the same way is just as easy to get around. Rules 1 to 3 must judge the day's
    *resulting* state (stored rows plus the posted ones, absence and work time), read inside the
    per-employee lock. The grid copy of the rules is for instant feedback only.
  - It needed **Clearing a Skema cell**, which shipped in S148 (TASK-14840/14841, QUAL-248; the row left the
    Usability list at S148's close): rule 2's warning asks the user to remove the first absence, which is now possible.
  - The grid copy of the rules needs a test comparing it with the server's, over every pair of types
    (same-type pairs and work+absence included), per the S125 mirror lesson.
  - Rule 4 must apply while typing, not when the user leaves the cell. The grid autosaves after 1 s, so a
    check on leaving the cell would come after the too-large value was already sent (the S123 lesson).
  - To confirm when refined: (a) adding *work* to a day already full of absence: proposed to be refused the
    same way as ruling 2; (b) rule 3 means a day with any absence never earns flex (6 t arbejde + 2,4 t ferie
    is refused), so check it against the S147 overtime/flex model (ADR-043) before building; (c) zero-norm
    days (weekends) keep today's behaviour unless ruled otherwise; (d) existing violating days are not
    migrated: three on `admin01` in the local demo DB, none created by the demo seed. Users fix them by
    clearing a cell.
  [owner UI testing 2026-10-28; working notes in `.claude/refinements/REFINEMENT-absence-day-rules.md`
  (gitignored, so this entry carries the rulings)]
  **S148 note (2026-11-25).** Two prerequisites shipped in S148. **QUAL-237 is fixed** (TASK-14804): the Skema save
  judges the day as it will stand, inside the per-employee lock, and a day's credit-class paid absence may not exceed
  its planned hours minus the hours worked (ADR-043 D2), which is rule 3's server half for paid absence.
  **Clearing a Skema cell shipped** (TASK-14840/14841, QUAL-248). Still open: rules 1, 2 and 4, and the grid's own
  copy of the bound. The grid's advisory check counts a day's absence hours only, never its work
  (`frontend/src/components/SkemaGrid.tsx:338-366`), so it gives no warning before the server's bound refuses a save;
  the employee learns from the refusal's message, which since S148 says exactly why (TASK-14810;
  `docs/sprints/SPRINT-148.md:763`, `:826`).
- **Working-time rules: what the tenders add to ADR-043 (tender epic E1; owner, 2026-11-13).** Most of the tenders'
  working-time requirements are §1 item 4's program. The rest: VD 45's flex capping (a −10 h floor, yearly or
  quarterly cut dates, conversion to særlige omsorgstimer 2:1 up to 74 h, HR approval, advance notice; the owner:
  "needs analysis and evidence"); minimum and maximum hours per registration type as data (EM A5, VD 10, beside the
  absence day rules row); more quotas (cheffridage; care days per child, which needs children's data from E7;
  fasttidsdage, which the analysis could not match to a source); a new agreement version applied as dated
  configuration with a mass recalculation screen (EM A17); per-institution feature flags (ADR-025 D6). The owner's
  EM A16 ruling, "stick to our own analysis of rules", keeps StatsTid's verified research as the authority: a
  customer does not define its own rules or absence types (EM A16 and VD 26 are not relevant). Ids: EM A5, EM A8,
  EM A17, EM D1, VD 10, VD 11, VD 12, VD 20, VD 21, VD 45.
- **Vacation: the holiday rules end to end (tender epic E2; owner, 2026-11-13).** Accrual and the account are
  strong; the last mile is missing. **Needs:** a verified vacation-law reference built from the three dossiers (the
  owner, EM A9: "documentation so we know exactly what the current legislation is"); the §21 transfer as an employee
  request with the manager's approval documented (EM A9b, VD 14; amends ADR-033 D8, where HR records it with no
  manager step; needs E4); a separate transferred-days balance on screen; the 16-month taking period modelled
  (ADR-033 D12) so the forecast covers it; transfer of særlige feriedage (research); a leaver form and the §26
  request screen (the §21 record is one of the four write forms owed since S140, and HR's list still says "skærm
  følger i S141", `FollowUpLists.tsx:86`, `:672`); switching the close on, after the §15 stk.1 precondition below
  and with real payout codes (E9). **Open, an owner decision at this epic's refinement:** advance vacation (EM A10,
  VD 15) is an automatic allowance today, not a grant; whether to add an explicit grant is decided after the law
  reference. Ids: EM A9, EM A9b, EM A10, EM A11, EM A13, VD 13–16, VD 18.
- **★ Shift planning and the calendar (tender epic E6). Owner ruling 2026-11-13: built into StatsTid, in stages, on
  ADR-043 stage 2's roster record.** One record serves pay and planning, so "this shift was rostered" never has two
  sources. It is more than a convenience: the state's +25 % supplement applies to work "ordered or on an approved
  duty roster", so a roster is a pay input. **Covers:** rosters and shift types, swaps approved by a leader,
  protected days off, rest warnings before a roster is published, the roster flowing into Skema with planned
  against actual hours, Outlook kept current, and last the automatic roster proposal from availability (the
  heaviest part). **Consequences for §1 item 4's program:** stage 2's refinement designs "an order, or a roster
  span" (ADR-043 D3) as the record shift planning will extend with shift templates and recurrence; it does not
  build shift planning. If stage 2 lands its acceptance half first, shift planning waits for the order half.
  **Research before stage 2's refinement:** the shift-worker chapter ADR-043 left undecided, so the record fits the
  shift types. **Research before the pay rules:** VD 25 (night supplement during sickness; the owner: "needs
  thorough analysis"), VD 52 (short-notice summons), VD 54 (civil servants' night pay; civil servants are not
  modelled). **Needs, at this epic's refinement:** a new `SYSTEM_TARGET.md` section (owner approval), a
  shift-planning ADR, and the shape of the shift planner and shift user (VD 8), decided with E7's roles. **After:**
  stage 2's order half, and E4 (swaps are requests). Ids: VD-G1, VD 25, VD 46–57, VD 59, VD 61.
- **Backlog — one "plausible date" bound at the request edge for every dated admin write (S148, TASK-14821 Step 5a
  cycle 1, Fable NOTE 5; adjudication `S148-TASK-14821-5a-adjudication-c1.md`, R5).** A start or end date in the
  last days of year 9999 overflows the worklist's week arithmetic (`SettlingMondayAfter`, and the end-date span's
  exclusive upper bound) and the request answers 500 with a full rollback: nothing is written and nothing is silent,
  but the error is a crash, not a refusal. It is a class, not one line: every route that takes a date and feeds the
  week or month arithmetic shares it. The clean fix is one shared bound (e.g. years 1900–2200) checked at the edge of
  every dated admin write, answering 422 with a date-free reason (ADR-040 D7). Below the QUAL register's Medium floor
  and not a security finding, so no register row; `(unproven)` that any such request answered 200 before S148.

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
  rule that lives only in the screen — **RULED 2026-09-22, build owed: split the verb**, see below), QUAL-166 (generated contracts under-describing required inputs), QUAL-167 (deactivation
  without an end date accruing overdue months for ever). [S140 TASK-14003/14004/14007/14010]

- **★ RULED, NOT YET BUILT — split the worklist's "Recalculated" outcome in two** (QUAL-165; owner ruling
  2026-09-22, registered S140, deferred from S141, decided at the S142 close).
  **The problem in one sentence:** when a correction lands in an already-exported payroll month, HR closes the
  worklist row by recording what happened — and "Recalculated" currently means two different things at once.
  For months the system marks impossible to recalculate, the screen greys the option out for everyone, but the
  **server never checks that flag**, so a direct API call records "recalculated" against a month the system
  itself says must not be recalculated (demonstrated by an existing test: blocked row, admin, 200).
  **Why it was not simply an authorisation bug.** The verb records what a human did OUTSIDE the tool. An
  operator may genuinely have re-planned that month correctly by hand — in which case recording the truth is
  right, and the screen is discouraging something *usually* wrong rather than forbidding something *always*
  wrong. **The ruling:** "the system recalculated this" and "a human sorted it out" are two different facts
  sharing one word. Split them; then the flag becomes binding for the recalculation verb specifically, because
  with an honest alternative available, refusing the false one costs nobody anything.
  **Build:** a second outcome, the server-side check, a migration (existing "recalculated" rows on blocked
  months are ambiguous by construction — record that, do not guess), and the screen's missing 403 branch on
  resolve. Nothing is at risk meanwhile: the screen still refuses the verb.

- **Notify the manager to give a leaver's final month its last approval** (owner-raised 2026-10-27, with the
  S147 OQ-2 ruling). From S147, a leaver's own manager can still approve their final month for 5 days after the
  last working day; after that only HR can (QUAL-230). Nothing tells the manager that window is open or about to
  close: StatsTid sends no notifications of any kind today (the tender analysis, EM A15 / VD 22,
  `docs/references/tender-requirements-analysis.md`). **Proposed shape:**
  - a notification to the manager when the leaver's final month is ready, asking for the final confirmation;
  - a reminder before the grace period ends;
  - after it, the month is on HR's leaver list (HRP-011, `docs/operations/hr-follow-up-process-register.md`).

  It needs a notification capability that does not exist yet, so it belongs with the HR register's escalation gap
  ("nothing escalates it") rather than as a one-off mail. The capability is tender epic E4 (below).

- **★ The HR and leader back office (tender epic E3). Owner ruling 2026-11-13: the first lane among the work that
  needs no outside input,** alongside or after §1 item 4's overtime program, which stays the priority. HR can do far
  more through the API than on screen. **Needs:** an "open employee" mode for HR and local admins in Skema and
  Årsoversigt (read, write, send; audited as on-behalf; four-eyes unchanged); an approval-status overview with a
  month picker across the actor's Organisations; balances per employee for every modelled type; absence over a date
  range (through Skema's save path, so the absence day rules apply); search on master data (after E7's list);
  server-side mass approve and reopen for an HR-selected group, each month keeping its own four-eyes check;
  comments on a registration (they can hold health data, so GDPR applies and E11's erasure must reach them); a
  running flex figure (after ADR-043 stage 1). **Owner ruling 2026-11-13: both deliberate choices stay.** A leader
  sees a month only after it is sent (S124), and a month sent to payroll is never reopened (ADR-034). The tender
  rows are met through overviews of sent months, HR's own view, and the corrections path; a tender evaluator may
  still score the difference. HRP-022's export screen and group export (the E9 row) are HR screens of the same kind
  and may ride in this lane. Ids: EM-G1, EM A1, EM A3, EM A4, EM A7, EM A29, EM A30, EM A31, VD 9, VD 32.
- **★ Employee requests and notifications (tender epic E4; second half of the first lane, owner 2026-11-13).** Two
  new capabilities, not extensions. A **request**: employee-initiated and typed (vacation transfer, payout wish,
  holiday payout, ulempetillæg), decided by whoever holds the employee's month-approval authority, never on one's
  own request, with a history, a full answer on rejection, and open requests shown in the monthly statement.
  ADR-043's overtime marks stay in month approval, so the request design is refined with stage 2's marks in view.
  **Notifications**: an in-app inbox first, then mail through a relay (a mock sink in dev); alert rules HR
  configures over the HR register's processes; a log of alerts sent. Needs an ADR; two named triggers ("vacation
  without pay", sickness qualifying for sygedagpengerefusion) need research. Unlocks E2's manager approval, the
  leaver notification above and E6's swaps. Ids: EM A2, EM A14, EM A15, VD 22, VD 24.
- **Reporting, statistics and data access (tender epic E5). Owner ruling 2026-11-13: an "extract" is an on-screen
  report or statistic, plus documented dataset endpoints that a tool such as SAS pulls with a service login.** The
  owner (EM A23): "We should have some general reporting functionality." Excel/PDF export, print, unlimited
  self-service extracts and the LDV feed are not relevant. **Needs:** a small set of predefined on-screen
  statistics on absence and on project time; a vacation-liability overview (VD 17: earned, taken and untaken days
  for a chosen population); dataset endpoints; a login for a machine, not a person, scoped to one institution (the
  first piece of E8; touches SEC-036; needs an ADR); a project-administrator assignment (EM A26), proposed as a
  scoped capability, not a new role. **Constraints:** every report and dataset stays inside the viewer's org scope;
  a leader's figures cover sent months only (S124); sickness is health data (GDPR Art. 9), so aggregates need a
  minimum group size and the dataset endpoints need a ruling on what leaves at person level. **After** ADR-043
  stage 1, so the figures are the corrected ones. No longer waits on the owner. Ids: EM-G2, VD-G2, EM A23, EM A26,
  EM B31, VD 17, VD 60.

### Integrations with the state's shared systems (tender review, owner 2026-11-13)

*The owner will provide the integration specifications later (EM-G8). SLP replaces SLS / HR-Løn and FRAV (EM A22,
EM B1). Every row here waits on those specifications, except the machine login E5 needs and E9's export screen.*

- **Integration foundation (tender epic E8).** The plumbing every connection needs: machine (service) credentials
  (touches SEC-036), versioned inbound and outbound contracts, a scheduler with per-institution intervals and an
  on-demand trigger, delivery receipts (closing ADR-034 D2's "handed off ≠ delivered"), per-institution destinations
  (ADR-025 D1, generalised), webhooks for case systems. **Re-decides:** `SYSTEM_TARGET.md` "External Integrations"
  and the architecture's external context, both outbound-only today: inbound data enters only through the same
  dated, audited writers as a human edit, and never evaluates rules. Designed with the first real specification, not
  ahead of it. Ids: VD-G4, EM B32.
- **Pay and absence reporting to SLP (tender epic E9).** The export is solid but posts StatsTid's own JSON to a mock
  with internal codes.
  **Now, independent of SLP:** an HR export screen and a group export over the approval gate (HRP-022), each month
  keeping its approval and four-eyes check; they may ride with the HR back office (E3). **After the
  specifications:** the scheduled hand-off (VD 58), since SLP decides which payroll model applies; SLP's format and
  transport, the real catalogue through the dated wage-type mappings, FRAV's absence categories and schedule, keying
  slips if SLP still uses them, delivery receipts. **Re-decides:** "SLS" wording in `SYSTEM_TARGET.md`; ADR-025 D1;
  ADR-033 D1 if SLP needs amounts, not quantities; which payroll model (automatic transfer, VD 58, vs one-off
  elements, VD 2 / EM B2). Ids: EM A22, EM B1, EM B2, VD 1, VD 2, VD 28, VD 41, VD 58.
- **Navision Stat via GIS (tender epic E10).** The registration frame (dimensions, aliases = StatsTid's projects per
  the owner, cases, case tasks) imported and kept in sync; approved hours posted back with batch and voucher numbers,
  reconciliation, resend, reversal and repost, moves between aliases; SFTP/FTP. The owner: posting details "need
  thorough analysis when we spec the integration". The largest block (39 ids); proposed after E9. Ids: EM B3–B6,
  EM B8–B11, EM B13–B30, VD 4, VD 39, VD 44, VD 62–71.

### Usability / accessibility

- **Backdate worklist: a "show resolved rows" toggle** (S144 Step-0b ruling R7, 2026-09-29). From S144 every resolution records the block set that was in force when the claim was made (event `BlockedBy`, row `resolution_blocked_by`, GET `resolutionBlockedBy`) — but the screen lists open rows only and re-fetches after each resolve, so HR never sees a resolved row and the display of that audit fact was cut as unreachable UI. The honest way to surface it is a toggle that lists resolved rows too (a query flag the API already accepts, `BackdateWorklistEndpoints.cs:71` defaults to open). Small UX task; source: `.claude/plans/PLAN-s144.md` R7.
- **Accessibility (WCAG)** — rises from "polish" to a genuine requirement as the target firms toward
  production; not enforced today. [CONVENTIONS.md] For a public-sector system it is a legal requirement (the
  webtilgængelighedslov: WCAG 2.1 AA and an accessibility statement); it is one row of the conformance register
  (§2 "Governance / docs", owner-raised 2026-10-26).
- **Teamoversigt: say why an approval was refused for segregation of duties** (S147, the TASK-14704 review;
  tidy item 12e). Today a refused approval reads only "Kunne ikke godkende <navn>."
  (`frontend/src/pages/approval/TeamOversigt.tsx:603`), whether a leader tried to approve their own month (SEC-009)
  or an HR user a month they sent on the employee's behalf (SEC-053, since S147). The server's reasons are English,
  so this needs a Danish mapping, as HR's leaver list already has for the four-eyes case
  (`frontend/src/pages/admin/opfoelgning/FollowUpLists.tsx:191`). A small UX task.
- **Child's sick days: two small follow-ups from S148** (TASK-14811 Step 5a, Fable cycle-2 NOTEs; owed at S148's close).
  (1) **The refusal names the wrong day (Low; rare and safe).** When a child's first sick day is posted on the day
  before one already stored, the refusal names the stored day as the one that cannot be registered: saving Monday
  8 June next to a stored Tuesday 9 June reads "Barns 1. sygedag den 09-06-2026 kan ikke registreres: dagen før
  (08-06-2026) er registreret som barns sygdom …" (`SkemaEndpoints.cs:2374`; the call-home twin at `:2384`). A
  wording pass names the posted date when the conflict comes from a row posted before a stored one.
  (2) **The entry order for a call-home day (`(unproven)`; an e2e check).** The call-home day is refused unless the
  day's work is stored or posted in the same save (`kind` `child-sick-call-home-without-work`,
  `SkemaEndpoints.cs:2405`). If the grid ever saves the absence cell before the day's work periods, the user gets
  that refusal and must enter the work first. An e2e fact settles which order the grid sends.
- **Mobile access, error messages and the user manual (tender epic E12; owner, 2026-11-13).** Responsive pages and an
  installable web app (PWA) first, a native app only if the PWA fails a requirement (EM A21, VD 27, which also wants
  shift planning on the phone); Danish error messages that say what to do (QUAL-058) and Danish labels throughout; a
  Danish user manual and release notes kept in step with each version. The accessibility row above belongs in the
  same pass. Ids: EM A21, VD 27, EM A32, EM A36, EM E4, VD 83, VD 85, VD 87.

### Tooling / infra / environment
- **★ Dependency inventory, security and updates** (owner-raised 2026-10-26: *"We need to have an overview of what
  packages we use, make sure they are secure and updated."*).
  - **Why it matters.** Every library and base image the system runs on is code the project did not write. A known
    vulnerability in one of them is a vulnerability in StatsTid, and an unsupported runtime stops receiving security
    fixes at all. Today nobody can say, from one place, which packages are in use, which are out of date, or whether
    any has a published vulnerability.
  - **What exists today (checked 2026-10-26):**
    - **Dependabot has run since S39** (TASK-3903; `.github/dependabot.yml`). It covers NuGet, npm, GitHub Actions and
      the Docker images, and opens update pull requests weekly. Review is manual, and the auto-merge policy S39 deferred
      was never decided.
    - **34 of its pull requests are open and untriaged, the oldest from 2026-05-24.**
      - **Major-version jumps:** the .NET 8 → 10 base images and `Microsoft.Extensions.*`, Npgsql 8 → 10, Testcontainers
        3 → 4, the xunit runner, `react-router-dom` 6 → 7, `@vitejs/plugin-react` 4 → 6, and five GitHub Actions.
      - **Patch updates on security-relevant paths,** e.g. `Microsoft.AspNetCore.Authentication.JwtBearer` 8.0.8 →
        8.0.27, the library that validates every login token. Patch releases on a supported line typically carry security
        fixes.
    - **Dependabot security alerts are turned off for the repository** (GitHub answers "disabled"). **CI runs no
      vulnerability scan**: no `dotnet list package --vulnerable`, no `npm audit`. The only supply-chain-adjacent check
      is gitleaks, which looks for leaked secrets, not vulnerable packages.
    - **No inventory exists.** Each project file pins its own versions; there is no central package management (no
      `Directory.Packages.props`). That is 15 distinct NuGet packages, about 27 declared npm packages plus their
      transitive tree, and the Docker base images.
    - **.NET 8, the runtime every service runs on, leaves Microsoft support on 2026-11-10;** .NET 10 is the current
      long-term-support release. CI already warns that GitHub's Node 20 action runtime is deprecated.
  - **A proposed shape** (for the refinement, not decided):
    1. **Inventory:** a generated `docs/generated/dependencies.md`, built from the project files, `package-lock.json` and
       the Dockerfiles. It lists each package, its version, where it is used, its licence and the latest available version,
       and CI checks it for drift the way it checks `db-schema.md`.
    2. **Security:**
       - turn on Dependabot alerts and security updates (an owner action in the repository settings);
       - add a CI job that fails on high or critical findings from `dotnet list package --vulnerable
         --include-transitive` and `npm audit`;
       - optionally, scan the container images.
    3. **Updates:**
       - triage the 34 open pull requests: patch and minor updates in tested batches, each major update as its own
         planned task;
       - the .NET 8 → 10 move is the largest and most time-bound major update. It touches every service, Dockerfile, the
         CI workflow and the generated contracts;
       - then a standing cadence, e.g. a dependency slot each sprint or month, plus an auto-merge rule for patch updates
         with green CI.
    4. **Central package management** (`Directory.Packages.props`), so each version is set once.
  - **Timing (owner, 2026-10-26):** it can wait. The system is not in production; the item only needs to be on the
    roadmap. The .NET 8 end-of-support date is recorded as a fact for whenever the item is picked up.
- **Backlog — pin the Rule Engine's "fixed, value-free 400 sentences" (S145 TASK-14511 adjudication, declined NOTE 1).** Payroll now
  logs the timeline endpoint's 400 `error` sentence, capped at 200 characters, with the employee id. That is safe only because
  every `TimelineInputException` message is a literal with no interpolation (`WeeklyTimelineCore.cs`, `RuleRegistry.cs`), and that
  rests on convention, not a test. A Rule Engine unit guard asserting each message is in the fixed set would make it durable.
- **Backlog — fold the remaining rule-engine test stubs onto the shared handler (S145 O-10 finding).** S145's O-10 moved four private
  copies of the entitlement-validation stub onto `tests/StatsTid.Tests.Regression/TestSupport/EntitlementValidationStubHandler.cs`.
  Nine further identical private `RuleEngineStubHandler`s remain in the Regression project (`Balance/DatedQuotaAnchorTests`,
  `EmployeeProfile/Adr032RevaluationTests`, `Hosting/PublisherStallReadYourWriteTests`, `Outbox/ProjectionParityTests`,
  `Outbox/SkemaEntitlementEligibilityGuardTests`, `Outbox/SkemaMonthlyAccrualGuardTests`, `Skema/Adr032ConsumptionPinTests`,
  `Skema/SkemaAccrualCapTests`, `Skema/SkemaFullDayOnlyGuardTests`). A mechanical `sweep` task; tests only.
- **Backlog — one copy of the payroll race-test machinery (S147 tidy item 3, from the TASK-14700-T review).**
  `FlexLedgerStaleChainRaceTests.cs:48` copies about 100 lines of `FlexLedgerRaceTests.cs:52`: the `HeldLock`
  helper that holds the ledger lock, and the observation that a waiter is blocked on it. Lift both into the
  flex-ledger harness. Docker-only, so only CI can check it; do it with the next task that edits either race file.
  Tests only.
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
- **`SkemaPage` 7203-pin vitest flake — ✅ RESOLVED (S147): it was a real race, not a flake.** A day panel opened before
  the page had copied the month's data started with no work periods, and an edit there could wipe the day's stored
  times. Registered as QUAL-241 and fixed by TASK-14718 and TASK-14720 (merged `782e2285`, `3ee2a8c9`; the frontend
  job green at CI-W2b and CI-W3); QUAL-241 CLOSED. [S128 FU-D]

### Governance / docs
- **Should the governance hooks fail closed?** (FAIL-009, 2026-11-05; an owner decision, to be put in prose,
  one question at a time). Both guards fail OPEN: if PowerShell cannot start the guard in time (now 60 s) or at
  all, Claude Code lets the action through unchecked — in S147 that happened on 51 of 200 agent spawns. Claude
  Code's `"onFailure": "block"` makes a failed hook block instead. Options: (a) the routing guard only — a blocked
  spawn is visible and retried; (b) both — the close guard runs on every shell command, so on this
  memory-constrained machine a slow start would stop all shell work; (c) neither — rely on the register's
  "log ALLOW N / transcripts M" reconciliation and the close commit's hook record to notice misses. Read
  S148's reconciliation first: it shows whether 60 s alone closes the gap.
  **Owner ruling 2026-11-06: (c) for now — decide at S148's close**, with S148's register row (log vs
  transcripts, and any `hook_cancelled` / `hook_non_blocking_error` in the session transcript) as the evidence;
  if misses remain, choose (a) or (b) then.
- **Keep or revert the Ponytail adoptions — decide at S150's close** (owner "Go", 2026-11-08). Four ideas from
  the Ponytail plugin (MIT; assessed 2026-11-06, not installed) went in before S148 as a trial: the SMALLEST
  COMPLETE CHANGE block for implementers (`docs/AGENTS.md`), comments that state what is true now plus
  `shortcut(<tag>)` comments backed by a register row (PAT-032, CI check 5), and "no concrete case, no BLOCKER or
  WARNING" with a seven-field finding format for both review lenses. The metrics (M1 review cycles per chain, M2
  cycle-1 counts — descriptive only, M3 post-close defects, M4 `(unproven)` NOTEs followed up vs hidden, M5
  required machinery missed because of the block, M6 shortcuts) and the S145–S147 baseline are in
  `docs/operations/model-routing-register.md`. **M3 was narrowed after the refinement was approved** (2026-11-08,
  during the change review): the approved rule also compared CI runs to green, but S145's count was not
  reconstructed from the repository's records (GitHub's run list could supply it), so read literally the approved
  rule could never reach KEEP; and S145–S147's CI reds were test- or lint-side faults, not review misses. CI runs to green are
  still recorded each sprint, descriptive only. **Rule, per part — REVERT if any REVERT condition holds, else KEEP if
  every KEEP condition holds, else MIXED (to the owner with the numbers); an unevaluable condition is false.**
  M1 *rises* if any included cell (stage 5a/7a × lens, both sides reconstructable, ≥ 3 chains each, pooled means)
  exceeds its baseline by more than 0.5 cycle; M3 *worsens* if post-close defects exceed the baseline total (0) by
  more than 1; a metric is evaluated only when both sides are reconstructable. *Review rule:* REVERT if an M4 "hidden" NOTE later proved a
  BLOCKER-class defect, or M1 rises and M3 worsens; KEEP if at least one M1 cell is included and none rises, M3 is
  evaluated and not worse, and no hidden NOTE. *Build block:* REVERT if M5 ≥ 2, or M3 worsens with an M5 case among
  its causes; KEEP if M5 = 0 and M3 is evaluated and not worse. *"Not built / not checked":* drop if empty in every
  trial report. *Shortcuts:* MIXED if none were used or check 5 gave a false positive outside PAT-032's documented
  classes; else KEEP. Whatever the outcome, every `(unproven)` NOTE still open at S150's close that names an
  invariant becomes a register row then, rather than expiring with the trial. The outcome is the input to the
  owner's decision, read for direction over three sprints
  (S148 is a large program sprint); it also feeds the governance-bloat review below, since the trial adds standing
  text to nine agent prompts.
- **★ Standards and requirements conformance — a systematic approach** (owner-raised 2026-10-26: *"What ISO
  standards, tekniske minimumskrav etc. do we need to measure the system against. We need to add a systematic
  approach."*).
  - **Why it matters.** A system for the Danish state is judged against a set of standards, laws and government
    requirements, not only against its own rules. Some apply to the software itself; others apply to the authority
    that runs it, which then expects its supplier's system to make them possible. Today the project has no list of
    which requirements apply, nothing that maps them to the code, and no way to show conformance. The security,
    quality and HR registers each cover one slice; nothing covers the whole.
  - **The candidate sources** (first pass, 2026-10-26; applicability to be verified, see the approach below):
    - **ISO/IEC 27001** (information security management). It has been mandatory for state authorities since 2016,
      and the NIS2 requirements build largely on top of it (Digitaliseringsstyrelsen, answer to Folketinget's DIU
      committee, question 152, August 2025:
      <https://www.ft.dk/samling/20241/almdel/diu/spm/152/svar/2155572/3055963.pdf>). Its Annex A controls (logging,
      access control, secure development, supplier relations) are the natural checklist for the software's own share.
      Related: ISO/IEC 27002 (the control guidance) and ISO/IEC 27701 (privacy), to be judged in the scoping.
    - **The tekniske minimumskrav for statslige myndigheder** (technical minimum requirements). They are mandatory, and
      an authority may not exempt a system on the strength of its own risk assessment. The current set has **29
      requirements, in force from 1 July 2024** (guidance:
      <https://www.sikkerdigital.dk/Media/638550029138627340/Vejledning%20til%20de%2029%20tekniske%20minimumskrav%202024%20-%20juni.pdf>).
      Most concern the authority's own clients, mail, network and websites; the ones that reach a web application need
      picking out.
    - **The NIS2 law** (*lov om foranstaltninger til sikring af et højt cybersikkerhedsniveau*), in force since 1 July
      2025. State authorities must meet its security measures, including **supply-chain security** and **security in
      the acquisition, development and maintenance of IT systems** (§ 6, stk. 1, nr. 4 and 5), which reaches a
      supplier's system directly. Supervision is by Styrelsen for Samfundssikkerhed (Folketinget DIU question 84:
      <https://www.ft.dk/samling/20241/almdel/diu/spm/84/svar/2141245/3025221.pdf>).
    - **GDPR and the Danish databeskyttelseslov.** StatsTid holds employees' working time, absence and sick days, and
      sickness is health data under Article 9. Expect a DPIA (a data protection impact assessment), retention and
      erasure rules, data-subject access, and the audit log's own privacy footprint.
    - **Rigsarkivet** (the national archives). State authorities must notify an IT system before it is put into use,
      under circular 9290 of 2013 (<https://rigsarkivet.dk/wp-content/uploads/2024/09/Vejledning-til-cirkulaere-nr-9290.pdf>).
      A system judged worth preserving must be able to deliver an archiving version under executive order 128/2020.
      That is an export capability the system may need.
    - **Web accessibility:** the Danish webtilgængelighedslov (WCAG 2.1 AA via EN 301 549, plus a published
      accessibility statement). The existing **Accessibility (WCAG)** item under "Usability / accessibility" becomes
      one row of this register.
    - **Authentication through Statens SSO** (OIOSAML; the existing item under "Security & access control"), and the
      payroll interface's requirements toward Statens Lønsystem.
    - **For the scoping to judge:** the fællesoffentlige arkitekturprincipper (the joint public architecture
      principles); ISO 22301 (business continuity); and anything a customer authority adds in its contract.
    - **What real state buyers ask for** (added 2026-10-26): `docs/references/tender-requirements-analysis.md`
      measures two historical tenders (Erhvervsministeriet 2023, Vejdirektoratet 2025) requirement by requirement.
      Beyond the sources above, both name ØS's *Løsningsarkitektur for betalingsafledende systemer* (EM wants
      compliance documented yearly, VD on request), an audit trail that meets Rigsrevisionen's requirements, the
      four-eyes principle (VD ties it to payroll-affecting actions under *Regnskabsbekendtgørelsen*), and FRAV
      absence reporting under circular 2022/9247.
      **Owner review (2026-11-13):** EM A38 ("This one sounds important"), EM §8.3 and VD 43, ØS's guide for
      payment-derived systems, are "should meet". Proposed as the register's first source, mapped clause by clause
      (the analysis read it at heading level only). Epic E11.
  - **A proposed systematic approach** (for the refinement, not decided):
    1. **A conformance register:** `docs/operations/compliance-register.md`, built like the SEC and QUAL registers.
       - **One row per requirement source,** then one row per clause or control that applies.
       - **Each row records:** who it binds (the software, the operating authority, or the hosting); the requirement
         in plain language; status (met, partly met, gap, or not applicable, with the reason); evidence (code, tests,
         docs, CI); owner; and a revisit trigger, such as a new version of the standard or law.
       - **Same semantics as the other registers:** "revisit, not shield".
    2. **Scoping first.** Decide, per source, which clauses fall on the software and which on the organisation. Most
       ISO 27001 and minimumskrav controls are organisational, and the register should not pretend the software can
       meet them alone.
    3. **Research with an independent verifier,** as the S65–S67 and S146 dossiers were done, for each source's
       current text and applicability. A **qualified expert's review** is needed before any conformance claim, as
       with the Phase B agreement experts. Requirements and laws change, so every citation is dated.
    4. **A gap analysis, mandatory sources first:** ISO 27001 Annex A (the software's share), the relevant
       minimumskrav, NIS2 § 6, GDPR (the DPIA first), accessibility, and Rigsarkivet. The gaps become register rows,
       then tasks.
    5. **Keeping it current:** a review at each entropy scan and before any go-live decision. New features declare
       which register rows they touch, the way PII and security concerns are declared today. The production hardening
       pass (§1 item 5) uses the register as its checklist.
  - **Timing (owner, 2026-10-26):** it can wait. The system is not in production; the item only needs to be on the
    roadmap. The order (the scoping and the register skeleton first, or one mandatory source end to end) is decided
    when the item is picked up.
- **Audit trail, data protection and resilience for a state buyer (tender epic E11; owner, 2026-11-13).** The
  strongest match with the tenders; what is missing: registrations in the audit view ("who registered what, when, for
  which day"); an audit-log export to hand to a controller (VD 36); database-level append-only protection; GDPR
  erasure (ADR-025 D3 Part B, with its S70 constraints) and a retention period, including deleting a leaver after a set
  period; backup with a tested restore (VD 72, with hosting); Rigsrevisionen's requirements researched. Four-eyes on
  configuration, corrections and payouts is SEC-060, its own row under "Security & access control". **Owner ruling
  2026-11-13: SEC-060 is built as the first item of this epic, after the HR back office (E3).** Ids: EM A37, EM A38,
  EM §8.3, VD 35, VD 36, VD 42, VD 43, VD 72, VD 75.
- **★ A thorough governance review: is the process getting bloated?** (owner, 2026-10-01: *"I have a
  feeling our governance is getting bloated. We need to do a thorough review."*). A follow-up, raised during
  S145 planning. Evidence measured the day it was raised, so the review starts from numbers, not from the feeling:
  - **S145 before any code.** The refinement needed 5 Step-4 cycles (rev 1 → 3.4, ~16k words). The plan is at
    draft 4 after Step-0b cycle 2 (draft 3 was ~27k words, 1631 lines). There are **59 Fable-seat rulings so far**
    (R1–R59), produced from roughly a dozen reviewer, a dozen adjudicator and seven Codex runs. The evidence design
    plans 52 deliberate mutations across 4 CI evidence runs, plus a mid-sprint throwaway CI push. Step-0b
    cycles 1–2 turned mostly on that evidence machinery, not on the product design.
  - **S144's close** (C-1..C-9) took three days. It needed two evidence runs, a close-guard waiver (a false
    positive in the guard's own query) and a post-close fix.
  - **Standing text agents and the seat must carry:** `docs/WORKFLOW.md` ~7.7k words, `docs/AGENTS.md` ~7.2k,
    `CLAUDE.md` ~1.6k, `docs/CONVENTIONS.md` ~0.9k. The quality register is ~24k words. Recent sprint logs run
    13–23k words each.
  - **Every agent tier inherits the session's effort level** (xhigh), because the agent definitions pin only the
    model. Mechanical and Sonnet-tier work therefore runs at the same effort as review.

  Questions for the review (to answer, not presumed):
  - What does each checkpoint (refinement Step 4, Step 0b, 5α/5a, 7a, post-close) actually catch, against what it
    costs? Mine the routing register and the review-finding history for this.
  - Which checkpoints duplicate each other?
  - Is mutation-at-the-gate evidence proportionate to the risk it covers?
  - Do the sprint logs and registers serve their reader at their current size?
  - Is the cycle cap of 5 being used as a ceiling or as a budget?
  - How many decisions per sprint land on the owner?

  Outcome: open. The review may find the current governance right-sized, or recommend changes; the owner decides.
  Not S145 scope; nothing about governance has been decided.

  **Observations to feed the review (from S145 planning, 2026-10-02; recorded as data, not conclusions).**
  - **Where Step 0b's cycles went.** Step-0b cycles 1–3 all turned on the evidence machinery rather than on the
    design: first the partition of 51 Docker mutations into four CI runs, then a stub too simple to observe the
    replay claims, then a mutation placed where it reached the raw routes.
  - **A lighter alternative exists.** The Fable seat identified one (R66). For tests that can be written against
    today's code, a single CI run *before* the fix lands would show them red for the right reason; that is the
    RED-first method `docs/AGENTS.md:164-165` already describes. Mutations would then be needed only for tests that
    depend on new code: about 22 instead of 51. Whether to use it in future is a question for this review.
  - **S145 only:** the owner chose to keep the current design for this sprint (OQ1, 2026-10-02). That answer
    applies to S145's evidence method and to nothing else.
  - **The plan's size.** S145's plan reached 1913 lines, about two thirds of it per-task briefs plus history and
    bookkeeping. For S145, the Fable seat moved that material into per-task brief files and the sprint log (R66b).
    Whether this should become the norm is for the review.
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
