<!-- Companion record to SPRINT-131.md — the TASK-D/E adjudication index the QUAL register points at. -->
# SPRINT-131 — Adjudication record (TASK-D verdicts → TASK-E register input)

**What this is (plain language).** The S131 quality sweep produced ~170 candidate findings. Every
candidate was then adversarially re-verified by a refute panel (7 batches, each re-deriving the
evidence fresh from the pinned code at `7e4bb1b`), and the surviving High/Critical set was verified a
second time by the external lens (Codex: 19 CONFIRM / 2 ADJUST / 0 REFUTE). What follows is the final,
dual-verified finding set with per-row verdict provenance — the transcription source for
`docs/operations/quality-finding-register.md`. Raw agent reports and panel transcripts are working
papers in the gitignored `.claude/sweeps/` (hygiene, not secrecy — same convention as S129).

**Verification outcomes at a glance:** 6 candidate rows REFUTED with disproofs · ~20 merged ·
severity corrected in both directions (High→Medium ×2 where the tier rubric was applied strictly;
Medium→High ×1 where the panel found a 6th diverged family member the sweep missed) · 2 evidence
components refuted inside surviving rows · 1 sweep test-observation escalated to a **product defect
at Critical** (both lenses) · 3 candidates retired to existing-register cross-references (dedupe rule).
**Final: 140 rows — 1 Critical, 21 High, 118 Medium.**

**Calibration (the sweep's falsifiability control):** 3 withheld code-anchored items. Round-1 score
**1/3** (QC-1 hit; QC-2, QC-3 missed — both misses one method-gap class: round 1 verified that
justifications/usage EXIST, not that they are STILL TRUE). Corrected-method supplemental passes
recovered both misses unseeded; the refute panel confirmed all three evidence chains code-derived
(with one honest qualification: QC-1's headline count is printed in the build artifact — credit rests
on the unprinted matrix/census/triage, independently reproduced). The round-1 score stands on the
record; the corrected methods are validated and fold into the skill at its next revision.
Withheld items, now disclosable: **QC-1** = the exact 137-warning build count + triage (→ the QUAL-069
evidence base); **QC-2** = the stale migration-rationale pragma at `RetroactiveCorrectionService.cs:209`
(→ inside QUAL-077); **QC-3** = the production-unused non-tx repository overload family (→ QUAL-036).

---

## The finding set (QUAL-001 … QUAL-140)

Format: `QUAL-id | Sev(conf) | Dim | class | title — plain meaning | primary loci | disposition | verdict provenance`.
(L)=confidence Likely; GP=gate proposal (owner rules per OQ-3); ⚖ = owner-reserved call.

### Critical (1) ⚖ owner ratifies severity (both lenses recommend Critical)
- **QUAL-001 | C | rule-engine product | Daily-rest check treats a night shift as 29h rest.** The 11-hour
  statutory daily-rest check assumes no shift crosses midnight: a 23:00→02:00 shift yields ~29h computed
  rest instead of 5h (violation missed), and a night callout on a day that also has a day shift is
  invisible to the check entirely (per-day Max over wall-clock times). The shape is the system's
  canonical one — the sibling SupplementRule splits exactly this shape on the same input list — and no
  caller or layer splits/validates. Exposure bounded to the `POST /api/time-entries` surface (the Skema
  path writes hours-only entries the check skips). | `RestPeriodRule.cs:218-278` (:225, :249-254, :256);
  canonical-shape proof `SupplementRule.cs:133-140` + `SupplementRuleTests.cs:70-85`; verbatim pass-through
  `ComplianceEndpoints.cs:89-133` | fix-now (absolute-instant intervals + one shared crossing
  interpretation across all four checks) | R2c product verdict + Codex PR-1 (BQ-1: Critical — "bounded
  exposure reduces blast radius but does not restore the guarantee"). Files under the interim class
  mapping; `rule.miscalculation` recorded as a method-revision proposal. Pairs QUAL-021 (the missing
  test), kept separate per the ruled test-gap boundary.

### High (21) — all dual-verified
- **QUAL-002 | H | D4 product | segment_manifests enum-encoding split.** The live calculation writes
  `boundaryCause` numerically; the projection rebuilder copies the event JSON's string form; the sole
  reader parses only the numeric form — so after any rebuild the projection read path is permanently
  dead, absorbed by a broad catch whose generic message misdirects triage (Codex trim: logged, so not
  fully silent), and the documented recovery restores rows the reader cannot parse. |
  `PeriodCalculationService.cs:112-116,993,1056,1070`; `SegmentManifestProjectionRebuilder.cs:110` |
  fix-now (unify on the string/audit-of-record encoding + re-encode + narrow the catch) | R2b P1 + Codex PR-2.
- **QUAL-003 | H | D4+D7 | Manifest-id audit enrichment has no caller; the ADR-016 audit linkage cannot
  exist.** The only method stamping the calculation manifest-id into the audit trail is never called;
  ADR-016 asserts audit entries carry `manifest_id` (the audit query path). Codex strengthening: the
  Payroll host registers no audit middleware at all — Backend-only. | `PCS.cs:756`; `Backend Program.cs:487`;
  `ADR-016:134,:224` | fix-now | R1 S-05 + Codex AH-1. ⚖ Backend-only-audit: intended design or gap (with QUAL-009).
- **QUAL-004 | H | D4 | Settlement duplicate-key recovery diverges.** Three of four copy-pasted recovery
  blocks were hardened to fail loudly; the YEAR_END copy still fabricates an "already settled" outcome
  from a row that failed to insert — and the hardened fourth claims to "mirror" it. |
  `VacationSettlementService.cs:388-408` vs `:612-618,:750-757,:893-912` | fix-now | R1 S-06 + Codex AH-2.
- **QUAL-005 | H | D4 | Copenhagen business-date helper: 6 copies, one diverges on the statutory
  deadline guard.** The 6th copy (found by the panel; the sweep said "no divergence") feeds the §21
  stk.2 transfer deadline and diverges three ways: fixed +01:00 fallback (wrong under CEST), narrower
  exception catch, no injectable clock. | `VacationSettlementEndpoints.cs:1511-1523,:177-184` + 5 members |
  fix-now (one shared helper + TimeProvider) | R1 S-08 (M→H) + Codex AH-3.
- **QUAL-006 | H | D5 | Payroll idempotency-mark failure swallowed unlogged.** Empty catch on the sole
  writer of the duplicate-prevention marker disarms the guard for that token; a same-token retry
  re-runs the correction — amounts are protected by design, so the cost is a phantom second correction
  event + audit row (auditability + idempotency). | `Payroll Program.cs:399-415,:345`; `RCS.cs:283-318` |
  fix-now | R3 merged (D5 primary, D8 xref) + Codex EH-1.
- **QUAL-007 | H | D5 | Weekly pipeline persists a failed fetch as a completed task.** No status check
  on the Backend fetches; unconditional `Success=true`; helpers null-swallow non-success; the false
  success reaches both the DB and the API response (401 is handled; JSON-body errors are not). |
  `WeeklyCalculationPipeline.cs:67-75,:140,:150-181`; `OrchestratorControlLoop.cs:104-119` | fix-now |
  R3 split (product half; logging half = QUAL-060) + Codex EH-2.
- **QUAL-008 | H | D8 | Correlation id in 1 of 185 log sites.** The system mints and propagates a
  correlation id but writes it into almost no log line, and no ambient mechanism substitutes —
  cross-service diagnosis is timestamp guessing. Census reproduced independently three times. |
  sole emitter `OrgScopeValidator.cs:451-453` | fix-now | D8 + R3 + Codex EH-3.
- **QUAL-009 | H(L) | D8 | Policy-level authorization denials leave no trace.** Denials from the policy
  layer produce no app log (four silent deny arms, no logger — all fail closed) and no audit row (the
  audit middleware runs after authorization, and only the Backend registers it at all). |
  `ScopeAuthorizationHandler.cs:9-67`; `Backend Program.cs:484-487` | fix-now | R3 + Codex EH-4.
  ⚖ SEC-routing candidate (new SEC row).
- **QUAL-010 | H | D7 | Agreement reference states the wrong særlige-feriedage reset month.** The doc
  the Rule Engine Agent is prompted with says September; both sources it names say January — the exact
  pre-S80 defect the code records as "settled ~4 months early and mis-keyed bookings". No mitigating
  note anywhere in the doc (whole-file sweep). | `danish-agreements.md:117` vs
  `DefaultEntitlementConfigs.cs:96` + `init.sql:1758-1763` | fix-now | R4§13 + Codex DH-1. Pairs QUAL-093.
- **QUAL-011 | H | D7 | SECURITY.md asserts a false authority model.** "The unit dimension appears in NO
  authority path" — substantively false since S105 deliberately wired the unit-leader exception edge;
  pinned to a test that exists nowhere and tables/prefixes that were replaced. | `SECURITY.md:134-138`
  vs `UnitAuthorityAbsenceTests.cs:12-20` | fix-now | R4§14 (strengthened) + Codex DH-2.
  ⚖ strongest SEC-routing candidate.
- **QUAL-012 | H | D7 | Legacy-DB runbook pointers resolve to unrelated DDL.** ~11 of 14 per-sprint
  init.sql pointers wrong (one names dropped tables); the "Known Ordering Gap" premise is also false. |
  `legacy-db-upgrade-runbook.md:44-60,:149` | fix-now | R4§15 (strengthened) + Codex DH-3.
- **QUAL-013 | H | D3 | PayrollMappingTests asserts nothing.** The one unit file named for the payroll
  wage-type mapping never touches the shipped resolver — object-initializer echoes plus an in-test
  lookup re-implementation (the rubric's payroll-vacuity anchor). | `PayrollMappingTests.cs:5-59` |
  fix-now | R2a H1 + Codex TH-1.
- **QUAL-014 | H | D3 | Four legacy-migration tests run pasted DDL copies.** They verify their own
  strings, not the shipped init.sql; two copies' own provenance citations have already drifted; the
  marker-extraction fix pattern exists on four siblings (whose four private extractor copies are
  themselves a duplication to consolidate). | S25/S35/AuditProjection/LegacyProfileSchema loci | fix-now |
  R2a H2 (family=4 finalized) + Codex TH-2. Below-floor fold: the S74 END-marker with no BEGIN (init.sql:4290).
- **QUAL-015 | H | D3 | Payroll replay marquees cannot fail on the regression they exist for.** Both
  variants mutate `part_time_fraction`; the shared stub and PCS itself provably never read the field —
  byte-identity compares payloads independent of the mutation; the sibling file documents this exact
  trap and fixed itself. | `EmployeeProfileMarqueeTests.cs:121-212`; `TestFixtures.cs:246-291` | fix-now |
  R2a H3 + Codex TH-3.
- **QUAL-016 | H | D3 | The atomic-outbox suite pins repository contracts, not endpoints (~45 tests / 15
  files).** A stated harness convention generates the family: 14 forced-rollback tests throw before the
  guarded write ("zero rows" true by construction), inline mirrors re-implement endpoint orchestration
  (a pattern whose failure the tree records twice), self-written fixtures asserted back. The correct
  real-route pattern exists (SendAtomicityTests). | member list in the R2b M1 verdict | fix-now (convert;
  retire the convention) | R2b M1 + Codex TH-4 + BQ-2 (systemic promotion endorsed exactly here).
- **QUAL-017 | H | D3 | The Compliance fail-closed test cannot verify what it claims.** One arm returns
  from a catch with no assertion; the other accepts any 5xx — and the harness's unstubbed rule-engine
  hop produces a 5xx regardless of the resolver (static fact; 9 sibling suites stub it). Sole guard of
  the ADR-023 D3 Compliance half. | `EmployeeProfileLifecycleTests.cs:702-755` | fix-now | R2c merged + Codex TH-5.
- **QUAL-018 | H | D3 | Role-revoke authorization has no deny test.** Three 403 branches in the revoke
  endpoint; zero tests exercise any of them. | `AdminEndpoints.cs:2468-2486` | fix-now | R2c + Codex TH-6.
- **QUAL-019 | H | D3 | The production rule-classification registry is pinned by no test.** All
  segmentation tests hand-build classification tuples; flipping a real rule's split-behavior across
  OK-version boundaries leaves the whole suite green (the ADR-016 D4 rejection included). |
  `RuleRegistry.cs:53-93,:63` | fix-now | R2c + Codex TH-7.
- **QUAL-020 | H | D3 | WTM supersession pins assert the test's own SQL.** The headline outbox emission
  and the Case-C reopen are executed by test-local statements; the real emitter and its audit mapper
  are exercised by nothing. | `WageTypeMappingSupersessionTests.cs` + `WageTypeMappingEndpoints.cs:465-525` |
  fix-now (drive the PUT) | R2c merged + Codex TH-8.
- **QUAL-021 | H | D3 | Daily-rest never tested with midnight-crossing shifts** — QUAL-001's missing
  guard (kept separate per the ruled boundary). | `RestPeriodRuleTests.cs:79-139` | fix-now | R2c + Codex TH-9.
- **QUAL-022 | H | D3 | OvertimeGovernanceRule has zero tests; the file named for it is a byte-identical
  copy of another rule's tests (+10 phantom tests).** | `RuleRegistry.cs:85-87`;
  `Sprint17OvertimeGovernanceTests.cs` | fix-now (delete the copy; write governance tests) | R2c NC-1 + Codex NC-1.

### Medium (118) — index
The full per-row detail (loci, class, disposition, verdict provenance, panel corrections of record)
is carried in the register's rows and grouped here by dimension for the sprint record:
- **D1 architecture (4):** QUAL-023 health-page boundary violation (High→Medium by tier rubric) ·
  QUAL-024 compose wiring asymmetry · QUAL-025 SharedKernel domain-logic creep (document — the code's
  placement rationale is sound; the doc's silence is the defect) · QUAL-026 mock hosts outside the
  solution gate (with the precise CI-compose mitigation carried).
- **D4 dead code & duplication (20):** QUAL-027 17 mapper JSON-options copies · QUAL-028(L) hook
  stale-response-guard divergence (F2 census correction) · QUAL-029 guard-vs-cast divergence ·
  QUAL-030 twin 39-field audit serializers · QUAL-031 six unroutable rule entry points ·
  QUAL-032 ExponentialBackoff wholly dead · QUAL-033 three no-implementer interfaces ·
  QUAL-034 compensatory-rest write path has no producer · QUAL-035 five dead repository reads ·
  **QUAL-036 the 33-overload self-connection family across 14 repositories** (production binds the
  in-transaction sibling without exception; ⚖ systemic-High declined — owner may revisit) ·
  QUAL-037 the replay surface has no production caller · QUAL-038 the entire dormant
  RoleConfigOverrideRepository · QUAL-039 the migrator is test-only (SEC-037 annotation) ·
  QUAL-040 the unfloored scope overload (SEC-022 closing fix) · QUAL-041 both audit-log query methods
  dead · QUAL-042 test-only central-config lookups · QUAL-043 ValidateAbsence unused beside its live
  twin · QUAL-044 the ungated balance mutator with no caller · QUAL-045 four test-only members ·
  QUAL-046 WeekGrid.tsx dead (checklist completed).
- **D5 error contracts (13):** QUAL-047…059 — the payroll 403-for-state and three-shape 422s; the
  tree's only ad-hoc 500 (with raw exception text, unlogged); five empty-body 403s; the error-key-less
  422; three status-vocabulary divergences against stated or counted norms (one softened to `document`
  where the divergence is reasoned in-code); the If-Match-less authority writes contradicting their
  class doc; the undeclared four-shape /resolve; the frontend client rendering raw bodies; the
  team-overview degradation swallow (verified NOT fail-open).
- **D8 observability (9):** QUAL-060…068 — parameterless pipeline failure logs; employment data logged
  verbatim on failure paths (⚖ SEC-routing); Information-level failed payroll delivery (document);
  unlogged failed logins (trimmed: audit_log does record the 401+IP); the OK-version Warning family on
  documented-normal input; correlation never forwarded to the RuleEngine hop; the silent DEAD_LETTER
  terminal; the zero-field audit-fallback log; structured templates with no structured sink (document;
  design-target readiness).
- **D6 warning debt (11):** QUAL-069 payroll warn-gate opt-out GP [QC-1 base] · QUAL-070 the fired-trigger
  breadcrumb class · QUAL-071 the opt-out rationale's coverage gap · QUAL-072 inert mock opt-out entries
  GP · QUAL-073 CA2100 ratchet GP · QUAL-074 mocks-into-gate GP · QUAL-075 CA5351 justified by deleted
  MD5 code · QUAL-076 the false all-literals rationale · QUAL-077 the shim caller-topology/stale-migration
  comment cluster [QC-2 locus] · QUAL-078 three bare frontend lint suppressions · QUAL-079 stale
  manifest-stamp citations in two replay tests.
- **D7 doc drift (15 + 2 pre-known):** QUAL-080…094 (ARCHITECTURE folders/inventories; FRONTEND phantom
  components/WeekGrid claim/styles; SECURITY residual-map pointer rot; the SECURITY:126 residual whose
  premise the flat-authority reform retired [⚖ reconcile with SEC-004]; the audit catalog's draft header
  + future-tense validation [the "never constructed" sub-claim was refuted — the event IS emitted];
  DemoSeed README's fixed-at-S85 bug claim; INDEX FAIL-misfiling; DEP-004's deleted endpoints; the
  17-site init.sql-anchor family; ADR-023's self-refuted exposure claim; the Security-Agent scope
  naming a dead folder and omitting src/Auth [⚖ SEC-routing]; the September comment in BalanceEndpoints;
  init.sql's stale self-references) + QUAL-124/125 (the two unrouted docs, filed without sweep credit).
- **D3 test-quality Mediums (27):** QUAL-095…121 — the legacy-Unit SUT-copy family; DemoSeed-never-run
  GP; untested frontend guard deny-branches; the narrowed outbox-restart and dormant-gate rows; the
  projection re-insert echo; the marquee positive-control gap (with the inert-stash disproof); the §15
  silent witness; the order-dependent boundary pin; position-precedence-by-comment; the 1-of-3 read-floor
  pin; the three unwalked nested schemas; the three-way Unknown404 contradiction; the identical-input
  half-timer test; four unguarded Assert.All arms; the drifted WTM schema mirror; the 8-identical
  If-Match tests masking three untested 428 endpoints; the self-performed dual-emission; the pure-function
  "determinism proofs"; ignored-config/boundary-less rest thresholds; unpinned part-time OT arithmetic;
  the non-discriminating carryoverMax pin; the presence-only fallback assertion; the two ambient-DB
  suites (one running an unscoped mutation sweep); the repo-copy perf guards; the self-declared nav
  redirect; the FE fixture-masking class GP.
- **Product/other (2 + 1 open):** QUAL-122 the hard-coded "Maj 2026" period label (document + backlog
  item) · QUAL-123(L) ⚖ the 48h ceiling ignores its configured reference period — domain-semantics
  ruling required BEFORE severity (Phase-B-class question) · plus the D2 set below.
- **D2 complexity (15):** QUAL-126…140 — all Medium, R5-corrected facts govern (counts fixed; three
  unread tails read, lower bounds rose 15-25% with no band change; the scope-loop "missing guard"
  wording corrected — factored-differently-equivalent, verified). **QUAL-133 carries the sprint's
  conditional-severity headline ⚖: the SPECIAL_HOLIDAY export handler omits the under-lock REVERSED
  probe both siblings carry — Medium while two verified gates keep the path dormant, CRITICAL-class the
  moment the §15 stk.1 go-live gate is configured → registered as a NAMED GO-LIVE PRECONDITION.**

### Refuted at TASK-D (recorded so the negative work is visible)
1. "Orchestrator lacks an outbox publisher" — ADR-018 D6 explicitly forbids Orchestrator stream writes
   and gates future events on re-architecting first; the cited authority defeats the claim. (D7 residual:
   ADR-018's D2 option text + :42 schema comment contradict D6 — folded into the D7 fix set.)
2. "Superseded-generation 422-vs-409 inconsistency" — a 1-vs-1 split with an in-code declared rationale;
   no norm exists to diverge from (the D5 evidence rule). → below-floor observation.
3. "Eligibility GET exempted where its spec lies" — the OpenAPI spec declares NO schema for that GET;
   there was no claim to be a lie. → below-floor + the untyped-operation family note.
4. "YearOverview reconciliation compares constants to themselves" — the compared value is
   product-computed from seeded projections; the residual is declared in-file. → below-floor.
5. "Authority-memo latch untested on its second path" — the latch IS tested; the visibility gap is
   documented with a reasoned trade. → below-floor.
6. "Deadline columns never distinguished" — SendCommandBehaviourTests asserts both columns with distinct
   values on the only production path. → below-floor.
Plus: 1 row demoted below-floor (a same-file pointer nit), 1 test row re-routed to its product defect
(the period label), and evidence-component refutations inside surviving rows (the "never-constructed
event" claim; the WTM version-column leg; two false-dichotomy/overstatement trims; the sixth ternary;
counts corrected in five rows).

### External dedupe (rule: already tracked → cross-reference, never a QUAL row)
- Dev JWT key literal (census-exact 94 files) → **SEC-015** pre-production ledger (count updated; the
  28-inline-bootstrap consolidation subnote added). No QUAL row.
- KB Tag/Domain indexes frozen → **ROADMAP backlog [WS3/C4]** (check_docs link-presence-only fact +
  duplicate-SharedKernel rows appended). No QUAL row.
- Scope-denial log-noise class → **SEC-012** re-observation note. Never filed.

### Register-update actions applied at TASK-E (sweep evidence → existing registers)
- **SEC-022 split**: the /execute unfloored-overload half is FIXED (S130 incidental — Orchestrator-verified
  at the baseline `Orchestrator/Program.cs:57,:94-97`); the raw-Authorization-forward half stays OPEN.
  QUAL-040 is the closing fix for the first half.
- **SEC-037**: reachability annotation — the migrator has no invocation path at all (QUAL-039); deleting
  it would close SEC-037 outright.
- **SEC-004** ⚖: QUAL-085 shows the accepted residual's premise (the nested-org model) was retired by
  S92-S95 — owner decides: close, or re-scope the committed follow-up.
- **SEC-012**: S131 re-observed the class; adjacent instance surface named (`OrgScopeValidator.cs:449-455,:70`).
- **F2** (performance register): census-classification correction — two "mount-only" hooks are re-fetched
  by every mutation (QUAL-028).
- **F1**: guard-quality cross-note — the S106 scale guards count a repository copy of the endpoint's
  reads (QUAL-119).

### Candidate NEW SEC rows ⚖ (owner rules; each stands as a QUAL row meanwhile)
SEC-038? policy-denial no-trace + Backend-only audit middleware (QUAL-009/003) · SEC-039? employment
data in failure logs (QUAL-061) · SEC-040? failed-login logging gap (QUAL-063) · SEC-041? `ex.Message`
in a 500 body (inside QUAL-049) · SECURITY.md false authority claim → new SEC row with QUAL-011 as xref.

### Gate-proposal packet (OQ-3 — owner rules each; the audit changed no CI behavior) ⚖
1. QUAL-069 restore the payroll warn-as-error gate (scoped pragma replaces the project-wide opt-out).
2. QUAL-072/074 bring the two mock hosts into a compiled gate (sln or explicit build step).
3. QUAL-073 freeze CA2100 at 109 in Regression and ratchet down.
4. QUAL-096 run the DemoSeed suite in CI (82 tests incl. golden pins are compiled but never executed).
5. QUAL-121 bind FE test fixtures to spec-derived nullability (or per-field spec-runtime pairing).
6. **QUAL-141 (pre-planned, FAIL-006 class): promote `check_docs.py`'s freshness warning to a hard
   failure for `docs/QUALITY.md`** — the doc refroze twice because freshness findings never reach the
   exit code.

### Method-revision proposals (vocabulary stayed closed this sweep; fold into the skill next revision)
`err.serialization-contract-split` · `rule.miscalculation` · a catch-block-keyed pattern scan (the
D5/D8 census blind spot: logger-less files are invisible to a Log*-keyed census; service-layer swallows
sit outside an endpoint-keyed universe) · the two QC-miss remedies as standing method steps
(production-vs-test caller discrimination; suppression-rationale truth verification).

### Coverage residuals (declared, carried to S132 scoping — not silent)
D2's ~30 census-identified-but-unread over-threshold regions (lizard-artifact re-run recommended) ·
the SCD-2 write-path clone family (15 members) unexamined for divergence — flagged rather than assumed
benign, since one "no divergence" verdict was already disproved · D7's declared shortfalls (KB prose
behavioural claims; ~230 unopened src file headers) · the mid-size-unsampled test files (the owner-ruled
OQ-2 residual) · `SkemaEndpoints.cs:1490-1491` stale anchors — **quick check CLOSED (Orchestrator,
2026-08-19): CONFIRMED stale** (":552" is an S73 additive-field comment; ":829" is a parameter line);
same-file recoverable pointers → below-floor, joins the anchor-family fix pass ·
the GoLiveDate comment/config mismatch — **quick check CLOSED: CONFIRMED** (`Settlement:GoLiveDate`
appears in no appsettings/compose at baseline → the close poller is dormant by default, corroborating
QUAL-133's gate; the test comment at `VacationSettlementServiceTests.cs:295` describes a scenario that
cannot occur unconfigured → below-floor test-comment drift · `Directory.Build.props:44-46`'s QUALITY.md
tracking claim (checked at TASK-E: the "Pre-S39 Warning Baseline" section EXISTS but its ledger is the
stale ~19-warning record — corrected in the QUALITY.md re-grade).

### Owner adjudication packet ⚖ (the decisions only the owner can make)
1. **Ratify QUAL-001 at Critical** (both lenses recommend it; the downgrade argument is stated in the row).
2. **Ratify QUAL-133's conditional severity** and adopt the missing REVERSED probe as a named §15 stk.1
   go-live precondition (ROADMAP entry made).
3. **Rule on the systemic-family High criterion** (BQ-2): the panels used "systemic AND prevents
   regression detection" — ratify or restate; affects QUAL-036, QUAL-095, QUAL-110 (all currently Medium).
4. **Rule per gate** on the 6-item gate packet above.
5. **Rule on the 5 candidate SEC rows** and the SEC-004 close-or-rescope question.
6. **Rule on QUAL-123's routing** (48h reference-period semantics → Phase-B domain question).
7. **Approve the S132 remediation shortlist** (proposed in SPRINT-131.md §S132 proposal).
