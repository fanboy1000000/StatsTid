# Sprint 117 — Typed API Contract retrofit Pass 4: the settlement bucket + the nullable-$ref escalation FIRED

| Field | Value |
|-------|-------|
| **Sprint** | 117 |
| **Status** | complete |
| **Start Date** | 2026-07-14 |
| **End Date** | 2026-07-15 |
| **Orchestrator Approved** | yes |
| **Build Verified** | yes — backend 0 err; FE tsc 0 + lint 0 + build green |
| **Test Verified** | yes (local, full): 861 unit + **1277 regression (the FINAL clean run: 1277/1277, ZERO sheds** — the pre-fix run was 1272/1273 with 1 pre-existing FAIL-002 shed [`FeriehindringResolutionTests`] isolation-cleared 1/1; the full suite was RE-RUN clean after the Step-7a fix set added 4 cases; the 42 fixed-port tests ran locally vs the compose Postgres) + 6 smoke (live 8-container stack) + 55 demoseed + 589 FE; **pyramid 861u+1277r+6s+55demoseed+589fe = 2788 (+16)**; gates: convention **73 typed / 64 grandfathered / 3 declared body-less** + 0 stale, drift in-sync (102 paths / 131 schemas), regen sha-idempotent (f192e721 proven), endpoint-contract lint + check_docs hard-green |

## Sprint Goal
Retrofit Pass 4 (PAT-012, [[typed-api-contract-program]]): drain **6 of the 7 settlement-bucket ops** (manifest 70→64; typed 67→73) AND **FIRE the numbered nullable-`$ref` escalation (owner-ruled OQ-1, 2026-07-14)**: op 2's `successor` member would be the 3rd nullable-complex residual member, so the sprint builds the designed mechanism — the `ResponseStrictTypesFilter` emits every CLR-nullable COMPLEX response member as **`type: object` + `allOf: [$ref]` + `nullable: true`** (the OAS-3.0.3-legal wrapper; `openapi-typescript@7.13.0` VERIFIED rendering it `T | null` — probe + generator source, Step-4 Codex) + the coordinated `SpecRuntimeMatcher` change — **retro-applied to the 2 existing members (`outgoingVikar`, `activeVikar`) → the nullable-`$ref` residual class CLOSES ENTIRELY (3→0)** and the FE workaround scaffolding deletes. **Op 5 (`/resolve`) takes the flag-and-defer rule's SECOND firing (owner-ruled OQ-2)** — 4 success branches with genuinely different key sets; typing = wire change; a `oneOf` phase is future work. **Op 4 (`reconcile-payout`) joins the declared-bodyless list (3rd member).** The bucket has ZERO FE callers and ZERO hand-written interfaces (a program first — greenfield typing; no FE switch task; the lie tally does not grow).

Refinement: `.claude/refinements/REFINEMENT-retrofit-pass4.md` — **READY; Step-4 closed in 2 cycles** (Codex 0B/1W — the wrapper-form pin + the rendering risk resolved empirically; Reviewer 0B/2W/5N — the vacuous-pass hazard + the structural-null policy flip with the full inverted-pin inventory; cycle 2 both CLEAN; OQ-1(a) survived the Reviewer's honest adversarial challenge). Both OQs owner-ruled.

**Explicit exclusions:** NO `.Accepts`/request-class changes; NO error-shape typing; NO wire-byte change anywhere (the allOf change is SPEC-METADATA only — the serializer is untouched, verified: no serialization path is reachable from the filter); NO rule-path/event/audit change; NO snapshot-embedding record (`VacationSettlementSnapshot`'s 4 `[JsonIgnore]` members stay dormant — PINNED prohibition); NO `oneOf` mechanism (deferred with op 5); every `RequireAuthorization` string byte-identical (all 7 = `"HROrAbove"`); Pass 5+ (config ~35 → employee-facing incl. skema) NOT in this sprint.

## Entropy Scan Findings (Step 0a)
| Check | Result | Detail |
|-------|--------|--------|
| Gate baseline | GREEN | convention 67 typed / 70 grandfathered / 2 declared body-less; drift+freshness in sync at `9ac61ca` (S116 close `a7232bb` CI GREEN `29154180913`) |
| Q0 nullable-complex census | **EXACTLY 1 in the bucket** | op 2's `successor` (`SettlementReversalEndpoints.cs:229-237`): ALWAYS-EMITTED null-or-object `{sequence(int), settlementState, trigger, version(long)}` — functionally identical to the 2 existing residual members → **the escalation trigger fires on typing it** |
| Q1 conditional-ignore | DORMANT | `VacationSettlementSnapshot`'s 4 `[JsonIgnore]` members (TerminationDate/CrystallizationBasis/CrystallizedDays WhenWritingNull; DeferredDisposition WhenWritingDefault) never reach any of the 7 success responses — handlers copy scalar fields only; the no-embedding prohibition is pinned |
| Op 5 polymorphism | flag-and-defer (2nd firing) | 4 success branches, DIFFERENT key sets: DEFER `+hint −forfeitDays`; WAIVED `+claimDispositionDays`; FERIEHINDRING `+feriehindringTransferDays/feriehindringReason/carryoverIn`; FORFEIT the minimal settled shape; 8-field common core (`VacationSettlementEndpoints.cs:668/752/919/1026`) |
| Op 4 request DTO | NONE | route-params + If-Match only (`:1122-1130`) → the declared-bodyless list's 3rd member |
| Sibling census | ops 6+7 BYTE-IDENTICAL | one shared payload construction (`:318-327`) mapped as two ops → ONE `TransferAgreementResponse`, POST `.Produces` 201 / PUT 200 (separate single-status ops — no multi-2xx); everything else near-siblings kept separate |
| Multi-return-site ops | op 1 = 2 sites | empty-scope `{items:[], count:0}` (`:1069`) + populated (`:1113`) — SAME key set; ONE envelope record constructed at BOTH sites (the empty branch via an empty typed list — Step-4 N3) |
| Enum authorities (cited) | 5 sets | `settlementState` {PENDING_REVIEW,SETTLED,REVERSED} (init.sql:2918 CHECK); `successor.trigger` {YEAR_END,TERMINATION} (:2919); `reversalKind` {BARE,SUPERSEDED} (provably-total projection, `SettlementReversalEndpoints.cs:225`); termination `state` — the FULL DB CHECK superset {OPEN,LINE_STAGED,VOIDED_BY_REVERSAL} (init.sql:3480; the endpoint always emits OPEN — enum-fidelity is MEMBERSHIP, the superset is the durable authority, Step-4 N4); transfer `entitlementType` = the guard-forced constant "VACATION". **REFUSED:** `entitlementType` elsewhere (open set); op-5's `reviewDisposition` (rides the deferral) |
| P6 posture | money-free | ADR-033 D1: no in-app kroner; every decimal = a NUMERIC(6,2) DAY-COUNT read/copied verbatim; NO rounding at any serialization site — byte-preservation = copy-fidelity |
| FE surface | **ZERO** | no caller of any of the 7 routes in frontend/src; no hand-written interfaces; the ONLY FE work = the mechanism fallout (below) |
| The FE fallout inventory (mechanism retro-close) | enumerated | delete BOTH `useRoster.ts` Omit types (`RosterRow` :52-58 AND the threading `RosterResponse` :69-74 + the module-header exception prose) + the `activeVikar ?? null` normalization (`useReportingLines.ts:288`); **tsc does NOT force these** — AC-enforced; consumer census bounded (StrukturPanel null/undefined-agnostic; test literals explicit); **WHITELIST: `LifecycleSections.tsx:86`'s `!== undefined` = a component-prop tri-state sentinel, NOT wire scaffolding — do NOT migrate** (no-refetch behavior test-pinned) |
| The inverted-pin inventory (mechanism) | delivered (Step-4 W2) | `SpecRuntimeMatcherTests`: the `StrictSpecJson` fixture's `detail` bare-$ref analogue + 4 tests serving `"detail": null` (:391/:424/:460/:470) + the green-case policy comment (:377-379); `ResponseStrictTypesFilterTests`: `ClrNullableComplexMember_EmitsAsBareRef_AndIsNotRequired` (:238) + the `maybeChild`-excluding required-array expectation (:167-175); `S115ReportingLineSpecRuntimeTests:250` prose (the both-branch wire pins SURVIVE = the live re-proof) |
| Test homes / FAIL-002 | mapped | rich Settlement suites exist; ZERO per-route contract tests; `SettlementEmitterFixture`/`ReconcileEmitterMutualExclusionTests` = the historical contention epicenter — new seeds DISJOINT + avoid the shared emitter fixture; scoped filters only |

## Plan Review (Step 0b)
| Field | Value |
|-------|-------|
| **Trigger** | MANDATORY — the gate-#1 mechanism change (filter + matcher) + P6 surface. **Named gate points: W1 (the vacuous-pass hazard — the recursion-proof RED direction) + W2 (the structural-null policy flip + the pin inventory)** |
| **External Codex** | 2 cycles (2026-07-14): cycle 1 = the filter-path drift (fixed) + 2 refining pins — **the inner-violation RED must be a MISSING-REQUIRED or OUT-OF-SET-ENUM inner member, not a shallow kind mismatch** (a shallow check could pass a partial recursion; required/enum fidelity proves the full `Match()` path executes inside the wrapper) + the UNMODIFIED wording; W1 code-traced REAL (`Deref` :340-353 direct-`$ref`-only; the empty-object walk); **the spec swept: exactly 2 non-required bare-`$ref` properties exist — no false-RED exposure**; gate math + pin citations verified. Cycle 2: **"Clean — plan ready for Step 1."** |
| **Internal Reviewer** | 2 cycles (2026-07-14): cycle 1 = **0 BLOCKER / 1 WARNING (the same path drift — convergent) / 4 NOTE** (the matcher's OWN stale prose added to the rewrite scope; the no-assertion-logic-changes wording; the +2-employee-settlement-POSTs re-homing; the whitelist broadened to all THREE tri-state sentinels). **The blast radius PROVEN bounded by exhaustive committed-spec sweep** (components + paths: the 2 retro-closed members are the ONLY bare-`$ref` properties in the entire spec) **+ checkpoint-1 achievability TRACED live through the S115 both-branch pins under the wrapper semantics** (null branch admitted via `nullable: true`; object branch reaches the same schema). Cycle 2: **all 5 CLOSED, 0 new findings.** |
| **BLOCKERs resolved before Step 1** | **yes — ZERO BLOCKERs both lenses; the two named gate points armed as task criteria (both RED directions incl. the required/enum inner violation; the policy flip + the full pin inventory)** |

---

### TASK-11700 — Backend: the allOf mechanism (filter + matcher halves)
| Field | Value |
|-------|-------|
| **ID** | TASK-11700 |
| **Status** | complete (2026-07-14) — **the escalation FIRED and RESOLVED.** Filter: `WrapAsNullableComplex()` emits `type: object` + `allOf: [$ref]` + `nullable: true` + required (the `Reference` moves INTO the allOf child — a node with `Reference` set serializes as only the `$ref`); the class-doc paragraph rewritten as the FIRED record. Matcher: `Deref` resolves THROUGH the pure wrapper recursively (W1 closed); the structural-null policy flipped (bare-`$ref`-serving-null = RED; arrays/inline objects unchanged); the :29-37 + :315-317 stale prose rewritten. Pin inventory reworked in full (the fixture's `detail` → wrapper; the 4 null-serving tests re-verified; both filter pins inverted; the S115 prose-only update). **The 4 new armed cases: the policy-flip RED; the recursion proofs = MISSING-REQUIRED and OUT-OF-SET-ENUM inner violations THROUGH the wrapper (the Codex pin — not kind checks); the both-branches green.** Filter 9/9; matcher 28/28. **Checkpoint 1: the spec delta = EXACTLY the 2 retro-closed members (18+/2− total); TS `T \| null` NOT optional for both; sha-idempotent ×2; drift green; composed Contracts 119/119 with NO assertion-logic changes** (the S115 both-branch pins = the live re-proof, unchanged). |
| **Agent** | Backend/API (the filter half) + Test & QA surface (the matcher half rides the same agent — the two halves are ONE coordinated change; file-disjoint parallelism is impossible here by design) |
| **Components** | src/Backend/StatsTid.Backend.Api/ResponseStrictTypesFilter.cs (Step-0b path fix — NO OpenApi/ subfolder; its unit tests live at tests/StatsTid.Tests.Unit/OpenApi/ResponseStrictTypesFilterTests.cs), tests/StatsTid.Tests.Regression/Contracts/SpecRuntimeMatcher.cs (+ SpecRuntimeMatcherTests), docs/api/openapi.json + frontend/src/lib/api-types.ts (regen checkpoint 1) |
| **KB Refs** | PAT-012 (the residual section + the escalation trigger), the S115 SuccessContract compatibility contract |

**Description**: **Filter half:** `ResponseStrictTypesFilter` emits every CLR-nullable COMPLEX member as `type: object` + `allOf: [$ref]` + `nullable: true` AND includes it in `required` (replacing the `IsNullableRef` exclusion); the class-doc's ENTIRE nullable-$ref-exception paragraph rewrites as the record of the escalation having FIRED — **AND the MATCHER's own stale prose (Step-0b N-2): `SpecRuntimeMatcher.cs:29-37` (the outgoingVikar exclusion narrative) + the `:315-317` in-code "a null there is permitted (no recursion)" comment both become FALSE after the flip — rewrite both.** **Matcher half:** `SpecRuntimeMatcher` resolves schemas THROUGH the wrapper (the W1 hazard: today `Deref` resolves only direct `$ref`s — the wrapper would walk as an empty `type: object` checking NOTHING; an incomplete matcher half is SILENTLY GREEN across every suite) and TIGHTENS the structural-null policy (W2): a bare-`$ref` member serving null = RED (post-retro-close, truthful nullability always carries the wrapper). Rework the inverted pins per the delivered inventory (fixture → the wrapper form; the 4 null-serving tests; the 2 filter pins; the S115 prose ripple). Regen checkpoint 1: the ONLY schema deltas are the 2 existing members' representations (+ required); the generated TS flips them to `T | null` required. `SuccessContract`'s public surface untouched (the wrapper appears at PROPERTY level only; response roots stay `$ref`s).

**Validation Criteria**:
- [ ] Matcher unit cases green incl. **BOTH RED directions**: a never-null lie on a wrapped member AND an inner-schema violation served THROUGH the wrapper — **the inner violation must exercise the real fidelity path: a MISSING REQUIRED inner member or an OUT-OF-SET enum on an inner member, NOT a shallow kind mismatch** (Step-0b Codex pin — proves recursion reaches the required/enum checks)
- [ ] The structural-null flip: a bare-`$ref` member serving null → RED (unit-pinned); the inverted pins reworked per the inventory; all other matcher/filter tests green (ripples named)
- [ ] Regen checkpoint 1: spec deltas = exactly the 2 retro-closed members (Step-0b verified: they are the ONLY non-required bare-`$ref` properties in the committed spec — no third member, no false-RED exposure); generated TS `T | null` + required for both; sha-idempotent; drift green; composed Contracts green with **NO ASSERTION-LOGIC CHANGES** (the inventory's comment/pin rework — the S115:249-251 prose + the SpecRuntimeMatcherTests inverted pins, both in `Contracts/` — is the NAMED exception; the S115 both-branch wire pins themselves = the live re-proof through the new path, zero assertion edits; Step-0b both-lens wording pin)

---

### TASK-11701 — Backend: the 6-op settlement drain (depends: 11700)
| Field | Value |
|-------|-------|
| **ID** | TASK-11701 |
| **Status** | complete (2026-07-14) — 6 ops typed as exact shape-copies (`Contracts/SettlementResponses.cs`, 7 records; field-mapping tables delivered, op-1 ONE record at BOTH sites); **op 2's `Successor` = the mechanism's first NEW consumer, spec-verified emitting wrapper-form + required (`SettlementSuccessor \| null` in TS)**; op 3's 201 preserved (`Results.Json` + `.Produces<T>(201)`); op 4 typed + the declared-bodyless 3rd member; **op 5 flag-and-defer (the rule's SECOND firing — in-manifest comment citing the 4-branch key divergence)**; ops 6+7 the shared `TransferAgreementResponse` (201/200 per verb). 5 enum sets with cited authorities (op-3 = the FULL DB-CHECK superset with the membership rationale). Manifest 70→64. **Checkpoint 2: convention 73 typed / 64 grandfathered / 3 declared / 0 stale; drift green (102 paths / 131 schemas); sha-idempotent ×2; build 0 err; the 3 existing settlement Docker suites 39/39 UNCHANGED (live byte-identity); unit 861/861.** P6/P7 held: zero auth/logic/decimal hunks; the snapshot prohibition pinned in the Contracts file header. **Declared residual → 11704: the stale `ActiveVikarResponse` doc comment (`ReportingLineAdminResponses.cs:90-94`) — deliberately left to protect the checkpoint-1 exactly-2-members delta.** |
| **Agent** | Backend/API |
| **Components** | VacationSettlementEndpoints.cs, SettlementReversalEndpoints.cs, TerminationPayoutRequestEndpoints.cs, Contracts/ (new SettlementResponses.cs or per-family files), tools/openapi-convention-exempt.txt, tools/openapi-bodyless-declared.txt, regen checkpoint 2 |
| **KB Refs** | PAT-012 (paved road; the S116 declared-bodyless rule; the S115 flag-and-defer rule) |

**Description**: Type ops 1, 2, 3, 4, 6, 7 — exact shape-copies: op 1's envelope (ONE record, BOTH call sites — the empty branch via an empty typed list); **op 2 incl. `successor` as a nullable nested record (`SettlementSuccessor?`) — the mechanism's first NEW consumer**; op 3's 201 preserved (`Results.Json(..., statusCode: 201)` → the record + `.Produces<T>(201)`); op 4 typed + added to `tools/openapi-bodyless-declared.txt` (3rd member); ops 6+7 share ONE `TransferAgreementResponse` (POST 201 / PUT 200). `[AllowedValues]` on the 5 cited sets (op-3's `state` = the full DB-CHECK superset with the membership rationale in a comment). **Op 5: flag-and-defer** — the in-manifest comment block (the rule's second firing; cite the 4-branch key divergence). NO snapshot embedding. Manifest 70→64. Regen checkpoint 2: gate 73 typed / 64 grandfathered / 3 declared; sha-idempotent. NO handler-logic/auth change (every policy string byte-identical); decimal copy-fidelity untouched.

**Validation Criteria**:
- [ ] Build 0 err; convention 73/64/3 + 0 stale; drift + freshness green at checkpoint 2; field-mapping tables (6 ops, every return site incl. op-1's both)
- [ ] P6/P7 diff audit: zero auth/logic hunks; zero decimal-handling changes

---

### TASK-11702 — FE: the mechanism fallout (depends: 11700 checkpoint 1)
| Field | Value |
|-------|-------|
| **ID** | TASK-11702 |
| **Status** | complete (2026-07-14) — BOTH Omit types → direct spec aliases (exported NAMES kept — 5+ consumers import them); the module-header exception prose rewritten as CLOSED; the `?? null` normalization deleted (`activeVikar` verified required `ActiveVikarInfo \| null` at api-types.ts:4638); the hook's public contract unchanged. Consumer sweep: ALL NO-OP (StrukturPanel truthy/`?.`-agnostic; every test literal explicit) — nothing relied on undefined-vs-null. The three whitelisted sentinels verified UNTOUCHED (git-diff-proven). **Declared extra (correct, not improvised): the api-typed-overloads phase pin had already tripped on the checkpoint-2 regen — mechanically updated to 22 POSTs / 14 PUTs / 8 DELETEs** (the resolve POST recorded as admission-excluded by design). **Reconciliation flag for 11704: the old PAT-012 "+2 employee settlement POSTs" = settlement-reversal + termination-payout-request (both `POST /api/admin/employees/…`) — DRAINED THIS SPRINT, so the rewrite records them RESOLVED, not re-homed** (supersedes the Step-0b N-3 re-homing instruction, which assumed they were outside the bucket). Validated: tsc 0; lint 0; vitest 589/589 (0 new tests — type/comment/pin literals only). |
| **Agent** | UX/Frontend |
| **Components** | frontend/src/hooks/useRoster.ts, frontend/src/hooks/useReportingLines.ts (+ affected tests) |
| **KB Refs** | PAT-012 (the residual's FE scaffolding); the refinement's whitelist |

**Description**: Delete the residual's FE workaround scaffolding — BOTH `useRoster.ts` Omit types (`RosterRow`, the threading `RosterResponse`, the module-header exception prose; both collapse to direct spec aliases) + the `activeVikar ?? null` normalization (`useReportingLines.ts:288` — the spec type is now `T | null` directly). **tsc does not force these — this checklist enforces them.** Consumer sweep per the census (StrukturPanel + test literals — expected no-op; migrate honestly if anything relied on `undefined`-vs-`null`). **WHITELIST (broadened, Step-0b N-4): component-prop/local-state tri-state sentinels are NOT wire scaffolding and stay untouched — THREE exist: `LifecycleSections.tsx:86` (test-pinned no-refetch), `ApproverSection.tsx:85-88`, `VikarSection.tsx:76` (all editPerson/; all ride already-`| null` hook/local-state contracts). A literal `!== undefined` sweep must not over-migrate them.** tsc/lint/vitest green.

**Validation Criteria**:
- [ ] Both Omit types + the normalization DELETED; the hooks' public contracts unchanged (`| null` was always the exposed shape); the whitelist untouched
- [ ] tsc 0; lint 0; vitest green (delta counted)

---

### TASK-11703 — Test & QA: per-route assertions for the 6 drained ops (depends: 11700+11701)
| Field | Value |
|-------|-------|
| **ID** | TASK-11703 |
| **Status** | complete (2026-07-14) — `S117SettlementSpecRuntimeTests`, 8 Docker facts / 6 ops (op 5 excluded per the deferral): op-1 BOTH envelope branches (the empty-scope branch reached via a stale pre-S93 `ORG_AND_DESCENDANTS` HR scope — clears the policy, default-denied by the S93-hardened union → the empty set; the run's single first-pass finding); **op-2 BOTH `successor` branches — null admitted via the wrapper's `nullable`, and the HEADLINE: the SUPERSEDED object with `settlementState`/`trigger` INNER ENUM FIDELITY exercised live through the allOf recursion** (the SUPERSEDED state driven through the REAL choreography: end-date PUT → TERMINATION settle → `REVERSE_AND_SUPERSEDE` with the R4 dual precondition); op-3 201-exact + 11 fields + `evidenceNote` both states; op-4 the caller's If-Match flow; ops 6+7 status-per-verb on the shared record with decimal fidelity. **The Docker-level wrapped-RED: a phantom member injected into the INNER `SettlementSuccessor.required` → the matcher throws with `.successor` + the phantom name + `REQUIRED` — the required-fidelity recursion proven against a LIVE response.** Seeds fully disjoint (census: `s117s_*`/`S117S*` vs all six suites' ids; the emitter fixture untouched — zero references). SETTLED states produced by the legal partition itself (input rows only — no settlement row ever inserted). **Validated: scoped 8/8 (26s); composed Contracts 127/127 (119 pre-existing green).** Durability note: the transfer test anchors ferieår 2026 (real-clock §21 guards) — year-bump rot after 2027, the existing S68/S71 anchor class. |
| **Agent** | Test & QA |
| **Components** | tests/StatsTid.Tests.Regression/Contracts/ (new S117 settlement class(es)) |
| **KB Refs** | PAT-012 (gate #1), FAIL-002 (the settlement contention epicenter) |

**Description**: Per-route spec≡runtime Docker assertions for the 6 ops: **op 2 BOTH `successor` branches — null (BARE) AND object (SUPERSEDED) — through the NEW allOf matcher path** (the mechanism's live proof on its first new consumer); op 1 BOTH envelope branches (empty-scope + populated); op 3's 201 asserted exactly; ops 6+7 status-per-verb (201/200) on the shared record; op 4's If-Match flow as the caller composes it; enum fidelity exercised (settlementState/trigger/reversalKind live values). One RED-on-lie proof through a WRAPPED member (complementing 11700's unit RED — the Docker-level recursion proof). **Seeds DISJOINT from every existing Settlement suite AND the `SettlementEmitterFixture`** (the FAIL-002 epicenter — fresh Organisations/employees, own testcontainer DBs); drive settlement states through the REAL machinery (close → PENDING_REVIEW → resolve/reconcile paths), never SQL-faked. Scoped filters only; composed Contracts green alongside.

**Validation Criteria**:
- [ ] All new Docker assertions green (exact counts); both successor branches + both op-1 branches proven; the wrapped-member RED demonstrated
- [ ] Seed disjointness census delivered (no id overlap; emitter fixture untouched)

---

### TASK-11704 — Orchestrator: PAT-012 rewrite + validation + Step 7a + close
| Field | Value |
|-------|-------|
| **ID** | TASK-11704 |
| **Status** | complete (2026-07-15) — PAT-012: the residual section REWRITTEN CLOSED (the escalation FIRED at `Successor`; the wrapper form; the mechanism rule ["an incomplete matcher half is silently green"]; the N2 named-record-not-collection scope qualifier; 0 members); the flag-and-defer firings consolidated (2, with the oneOf-phase weigh-at-3rd guidance); the declared-bodyless 3rd member; scope 73/64/3 with the "+2 employee settlement POSTs" recorded DRAINED (the 11702 reconciliation — supersedes Step-0b N-3); the stale `ActiveVikarResponse` doc comment fixed (the 11701 declared residual). Validation delta table below (the post-fix FULL re-run: 1277/1277 zero-shed). Step 7a converged 2 cycles both lenses (section above). Close via the explicit file set; the `design_handoff_*` dirs left untracked with cause (owner assets). |
| **Agent** | Orchestrator |
| **Components** | PAT-012, sprint log, INDEX, close gates |
| **KB Refs** | PAT-012, FAIL-002/003 |

**Description**: PAT-012: the nullable-`$ref` residual section REWRITTEN CLOSED (the escalation FIRED + RESOLVED; the wrapper form recorded; 0 members; the FE scaffolding gone); the flag-and-defer second firing recorded (op 5, with the 4-branch cause); the declared-bodyless third member; scope status → 73 typed / 64 grandfathered / 3 declared + the Pass-5 buckets (config ~35 → employee-facing incl. skema) — **explicitly RE-HOMING the "+2 employee settlement POSTs" from the old Pass-4 candidate line into the employee-facing bucket (Step-0b N-3 — they are NOT in this sprint's 7-op surface and must not silently vanish from the scope map).** Validation (`sprint-test-validation`). Step 7a dual-lens (named adversarial targets: the mechanism's recursion + policy-flip semantics across all 73 typed ops; wire-byte identity on the 6; the retro-closed members' spec-only delta). Close per the 5 gates; explicit file set; the `design_handoff_*` dirs stay untracked with cause.

**Validation Criteria**:
- [ ] PAT-012 rewritten; delta table; Step 7a converged; close + push + CI green all 7 jobs

---

## External Review (Step 7a)

Dual-lens, both cycle-1 → cycle-2 converged (2026-07-15). Artifacts: `.claude/reviews/SPRINT-117-step7a-{codex,reviewer}.md`.

| Lens | Cycle 1 | Cycle 2 (fix verification) |
|------|---------|---------------------------|
| External Codex (`codex review`, prompt-steered) | **0 BLOCKER / 2 WARNING / 0 NOTE** — both real fail-closed gaps in the NEW matcher path | **"Clean — the matcher fail-closed set is complete"** (+ ran the scoped suite itself: 32/32) |
| Internal Reviewer | **0 BLOCKER / 0 own WARNING** (both external Ws independently CONFIRMED — convergent) **/ 4 NOTE** (N1 → the 5th impurity case; N2 → the PAT-012 scope qualifier; N3/N4 expected/standing) | **CLEAN** — N1's catch traced value-independent; the caught-not-impossible filter posture ratified; the phase pin independently re-derived (44/44 union entries) |

**The fix set (all absorbed before close):** (1) a wrapper WITHOUT `nullable: true` serving null → RED (the wrapper-shaped never-null claim — Codex W1); (2) impure `allOf` → a DISCRIMINATED fail-closed throw in `Deref` (not-an-array / N-element / own-properties / sole-not-`$ref` — Codex W2, closing the vacuous-walk fall-through the Step-0b review had flagged as the hazard class); (3) enum/required stamped on a wrapper node = the 5th impurity (Reviewer N1 — resolution would silently discard it; the filter-side companion guard deliberately NOT added: the matcher catch at the first per-route assertion is mechanical enforcement, and zero complex members carry `[AllowedValues]`). **4 new unit pins; matcher 28→32; composed Contracts 131/131; zero existing tests flipped (read-derived impact analysis first, then confirmed by run).** [[review-lens-complementarity]]: the external lens attacked the mechanism with lying-spec vectors the internal lens's construction-verification pass had cleared — the third sprint running where the two priors catch disjoint classes at the same gate.

**Accepted/documented:** the source-side enum-on-wrapper scenario (caught fail-closed one gate later — documented here, not made impossible); the PAT-012 named-record-not-collection scope qualifier; the standing ROADMAP/QUALITY freshness warnings.

## Phase Plan
- **Phase 1 (sequential, one Backend agent):** TASK-11700 (the mechanism, regen checkpoint 1 + composed-Contracts re-proof) → TASK-11701 (the drain, regen checkpoint 2). The two halves of 11700 are ONE coordinated change — never land the filter half without the matcher half (the W1 silent-green hazard).
- **Phase 2 (parallel):** TASK-11702 (FE fallout — needs checkpoint 1's types) ∥ TASK-11703 (assertions — needs checkpoint 2's spec; file-disjoint: frontend/ vs tests/)
- **Phase 3:** TASK-11704 (docs + validation + close)

**Atomicity pin:** ONE close commit; no push mid-sprint; explicit file set; gates evaluated at close.

## Test Summary (close, 2026-07-15)

| Suite | Previous (S116) | Current | Delta |
|-------|-----------------|---------|-------|
| Unit | 861 | 861 | 0 |
| Regression | 1261 | 1277 | +16 |
| Smoke | 6 | 6 | 0 |
| DemoSeed | 55 | 55 | 0 |
| Frontend | 589 | 589 | 0 |
| **Total** | **2772** | **2788** | **+16** |

Delta composition: +16 regression = the 4 original mechanism unit cases (the armed Step-0b proofs) + the 8 S117 Docker facts + the 4 Step-7a fail-closed pins (matcher 28→32). FE delta 0 (the fallout was deletion-only; the phase-pin update changed literals, not counts). Full-run integrity: the pre-fix full run 1272/1273 (1 pre-existing FAIL-002 shed, `FeriehindringResolutionTests`, isolation-cleared 1/1); after the Step-7a fix set, the FULL suite re-ran **1277/1277 — zero sheds** (the S100/S101 clean-final-run discipline). Composed Contracts 131/131.

## Legal & Payroll Verification
| Check | Status |
|-------|--------|
| Agreement rule compliance | N/A — response typing + spec metadata only; zero rule-path change; the settlement state machines untouched |
| Wage type mapping correctness | N/A — the domain is money-free (ADR-033 D1: day-counts only; SLS owns all monetary math); decimal copy-fidelity byte-preserved |
| Event sourcing / audit | N/A — no event/audit-path change |
| Security (P7) | PASSIVE — no new endpoint; every existing `RequireAuthorization` string byte-identical (all 7 = HROrAbove); diff-audited at close |
| Payroll (P6) | ACTIVE-WATCH — the export-line/emitter surfaces untouched by response typing; the FAIL-002 emitter-fixture avoidance pinned in 11703 |
