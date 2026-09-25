# Model-routing governance interlude, 2026-09-24/25 — external lens (Codex) verdicts

Tracked archive of every Codex verdict on the governance chain `fe0bdb1..bf98e30` and on the post-close commit `5e78941`. All were performed on INLINED SOURCE (the Codex sandbox rejected even `Get-Content`); each verdict says so. An earlier attempt at 07:56Z (`codex-routing-review.txt`) returned `verdict: BLOCKED … contents remain unverified` after the sandbox rejected `Get-Content`; it is not a review and is not archived, but it is why every cycle below ran on inlined source. Log files with the echoed bundles stay in the session scratchpad; the verdicts are the evidence.

---

## Cycle 1 — on e8de47b (BLOCKED); extracted from the log

Review performed on **inlined source only**. No commands were run; runtime behavior and historical verification records were not independently checked.

- **BLOCKER — The challenge instruction could make tests preserve a bug instead of detecting it.** `.claude/agents/test-qa.md:8` says to verify expected values against code and “follow the code” when it disagrees. Line 6 explicitly requires expected values from the specification, not observed implementation. The same ambiguity appears in `docs/AGENTS.md:275`. Limit “follow the code” to factual observations about the existing implementation; disagreements about required behavior must be reported for a ruling, without changing specification-derived expectations.

- **WARNING — The new version-check explanation contradicts the actual review checks.** `docs/WORKFLOW.md:349–351` says none of the four layers checks versions, but `.claude/agents/reviewer.md:9` checks a specific version, and `.claude/hooks/sprint-close-guard.ps1:212–225` requires an exact ID. Rewrite this as a gap primarily affecting non-reviewer roles. At `docs/WORKFLOW.md:373`, say the close gate enforces the **configured version**, which remains newest only when maintained.

- **WARNING — A reviewer can accept a successor model that the close gate rejects.** `.claude/agents/reviewer.md:9` permits a successor explicitly named by the Orchestrator; `.claude/hooks/sprint-close-guard.ps1:225` accepts only the hard-coded ID. `docs/WORKFLOW.md:374–375` supplies the coordinated-update procedure, but the reviewer’s independent exception remains misleading. Require that procedure before using a successor.

- **WARNING — The old “most capable” rule still competes with the owner’s family-based choice.** `docs/WORKFLOW.md:305` and `.claude/agents/reviewer.md:3` retain it as an active instruction. Hook wording also retains it at `.claude/hooks/model-routing-guard.ps1:95` and `.claude/hooks/sprint-close-guard.ps1:231`. Replace it with “newest Fable for planning and review”; benchmark leadership is explicitly no longer the selection rule.

- **WARNING — Reporting a contradiction must not become permission to waive a governing rule.** `docs/AGENTS.md:227–228` routes contradictions like declared deviations, while `.claude/agents/backend-infrastructure.md:6` says the Orchestrator rules on every specification departure. Clarify that the Orchestrator may resolve factual brief errors within its authority; invariant conflicts and architectural exceptions still require the owner under `docs/CONVENTIONS.md:15–22`. Require specification departures to remain under “Declared deviations,” even when also reported as brief contradictions.

- **NOTE — Pinning a definition is compatible with this hook, but the hook does not validate the pin.** `.claude/hooks/model-routing-guard.ps1:49–50` reads only the tool’s explicit override, never definition frontmatter. A named-role spawn without an override therefore passes with a full-ID definition. Conversely, a full ID supplied directly as `model` would be rejected for the reviewer at line 94—even the correct Fable ID—and evade alias comparisons for implementation roles at lines 102–113. Under the supplied aliases-only tool contract, those direct full-ID inputs are unsupported; this is not evidence that the documented definition-pin remedy fails.

- **NOTE — The remaining changes substantially capture the rulings, with appropriate verification limits.** `docs/WORKFLOW.md:392–395` explicitly requires stopping for `/model`, and lines 359–361 correctly distinguish accepting a pin from proving it overrides a lagging alias. All eleven supplied non-reviewer definitions receive BRIEF CHALLENGE. Sweep’s tailored instruction preserves “leave untouched” (`.claude/agents/sweep.md:8`); trace remains read-only (`trace.md:7–9`); constraint-validator’s tailored exception avoids forced verdicts without authorizing edits (`constraint-validator.md:7–9`). The supplied text records the historical checks but does not independently prove them.

verdict: BLOCKED  
reviewed-against-commit: e8de47b

---

## Cycle 2 — on 8d35419 (APPROVED-WITH-WARNINGS); extracted from the log

Review performed on **inlined source only**. No commands were run; runtime behavior, release claims, and historical verification records were not independently verified.

Cycle-1 findings:

- **NOTE — RESOLVED: Tests now preserve the specification’s expected results when the implementation is wrong.** `.claude/agents/test-qa.md:8` explicitly requires a failing test rather than adjusting expectations to match code. `docs/AGENTS.md:284–292` separates existing-code facts from required behavior. Implementers still must verify every factual claim against code; `.claude/agents/backend-infrastructure.md:8` retains that instruction explicitly.

- **NOTE — RESOLVED: The documentation now distinguishes checking a configured version from ensuring it remains current.** `docs/WORKFLOW.md:354–358` acknowledges the reviewer’s exact-version checks and identifies the implementation-tier gap. Lines 383–387 correctly describe the close gate as enforcing the maintained configuration, consistent with `.claude/hooks/sprint-close-guard.ps1:212–225`.

- **NOTE — RESOLVED: A prompt can no longer authorize a reviewer version that the close gate rejects.** `.claude/agents/reviewer.md:9` removes the independent successor exception and requires the coordinated update. `docs/WORKFLOW.md:383–387` names the same three update locations. The reviewer and guard currently agree on `claude-fable-5-1`.

- **NOTE — RESOLVED: The supplied active instructions consistently select the newest Fable rather than the benchmark leader.** The replacements appear in `docs/WORKFLOW.md:305`, `.claude/agents/reviewer.md:3`, `.claude/hooks/model-routing-guard.ps1:6,95`, and `.claude/hooks/sprint-close-guard.ps1:231`, plus the supplied settings, hub, and register diffs. “Most capable” survives at `docs/WORKFLOW.md:308–310` only as explicitly retired historical wording. No competing active instruction survives in the supplied text; this is not a repository-wide search.

- **NOTE — RESOLVED: Reporting a contradiction no longer grants authority to waive a specification or invariant.** `docs/AGENTS.md:229–232` reserves architectural exceptions and invariant-related escalation for the owner and preserves “Declared deviations.” `.claude/agents/backend-infrastructure.md:8` explicitly limits the broader Orchestrator wording at line 6. The authority boundary is restored, although its escalation trigger is now overly broad, as noted below.

The two cycle-1 informational findings remain supported:

- **NOTE — Definition pins remain compatible with the spawn guard but are not validated by it.** `.claude/hooks/model-routing-guard.ps1:49–50,93–99` reads explicit overrides and permits named reviewer spawns without one. Its direct-full-ID limitations remain unchanged. `docs/WORKFLOW.md:379–382` now documents the definition-validation gap.

- **NOTE — The existing safeguards and verification limits remain intact.** `docs/WORKFLOW.md:363–368` distinguishes accepting a pin from proving it overrides a lagging alias; lines 400–407 require stopping for the model switch. All eleven non-reviewer definitions contain BRIEF CHALLENGE. Sweep preserves “leave untouched” (`sweep.md:8`); trace remains read-only (`trace.md:7`); constraint-validator retains its reporting exception without edit authority (`constraint-validator.md:7–9`).

Absorption findings:

- **WARNING — Routine corrections may now require unnecessary owner decisions.** `docs/AGENTS.md:230–231` escalates any contradiction that “touches an invariant”; the shared agent block repeats “anything touching an invariant,” for example `.claude/agents/backend-infrastructure.md:8`. This includes an ordinary incorrect factual claim about a calculation or audit write, even when correcting it fully satisfies the specification. `docs/CONVENTIONS.md:15–22` requires owner escalation for unavoidable invariant conflicts and architectural exceptions, not every related factual correction. Narrow the trigger to contradictions requiring an invariant compromise, architectural exception, or owner-level requirement change; retain Orchestrator handling of factual errors within its authority.

- **NOTE — The pin-check explanation unnecessarily excludes other documented checks.** `docs/WORKFLOW.md:381–382` calls self-report “the only check on a pin,” but lines 369–373 also permit transcript inspection, and reviewer pins additionally face the close gate. Say that pins require resolved-model verification through self-report or transcript inspection, with the reviewer’s additional checks retained.

No new blocker was identified.

verdict: APPROVED-WITH-WARNINGS  
reviewed-against-commit: 8d35419

---

## Cycle 3 — on bbf1e3a (APPROVED-WITH-WARNINGS)

Review performed on inlined source only. No commands or tests were run; this verdict covers the supplied bundle, not the repository.

- **WARNING — Ordinary implementation work can be blocked by the new review detector.** `.claude/hooks/model-routing-guard.ps1:133` matches `reviewed-by-model` anywhere in a generic agent’s prompt. A request to document, diagnose, or test that field therefore triggers the review prohibition. The suggested remedy—use `reviewer`—is unsuitable for implementation because that role is read-only. State this false-positive trade-off and direct legitimate implementation work to a named implementation role. A correctly routed `reviewer` spawn is unaffected: it exits through lines 96–102 before this check.

- **WARNING — The new check stops the observed prompt, but does not close the general bypass.** `docs/WORKFLOW.md:356` says the route is “now closed,” yet a generic Opus review prompt that omits `reviewed-by-model` still passes at `.claude/hooks/model-routing-guard.ps1:136`. Describe this as detection of the observed bypass signature, with remaining reliance on routing discipline. The supplied new tests exercise the close guard, not this prompt detector.

- **WARNING — The explanation turns an observed failure into a broader guarantee than the supplied evidence establishes.** `docs/WORKFLOW.md:378`–386 treats a model-selection precedence chain as proof that every unknown pinned ID silently falls through to the session model. Precedence alone does not establish invalid-value handling, and an intervening environment override appears in that same chain. Consequently, “would run on Fable” should be conditional, and “the one cause a pin cannot fix” is unsupported exclusivity. For PM readability, lead with: “Update the client first: the older version could not use the requested model, and pinning did not fix it.” Keep the observed attempts and the inferred mechanism separate.

- **WARNING — The close check accepts more than the documented context-window suffix.** `.claude/hooks/sprint-close-guard.ps1:225` strips any final bracketed text, including `[]`, `[garbage]`, and malformed nested opening brackets. It does preserve rejection of an ordinary wrong model such as `claude-opus-5-5[1m]`; this is not a general wrong-model bypass. Restrict normalization to supported suffixes, such as `\[1m\]$`, and add an invalid-suffix case. Also align `.claude/agents/reviewer.md:9`: its literal self-check currently instructs a reviewer reporting the valid suffixed ID to refuse before reaching the permissive close gate.

- **WARNING — Two monitoring instructions still disagree with the updated workflow.** `docs/operations/model-routing-register.md:54` counts blocks on reviewer spawns only, whereas `docs/WORKFLOW.md:364` explicitly includes the new generic-spawn block. Update signal 3 so readers following the register count the same events. The register also retains the obsolete `tasks\` transcript location at line 39 despite giving `subagents\` at line 29.

- **WARNING — Deferring the historical review is defensible only if the outstanding review is explicitly carried forward.** `docs/sprints/SPRINT-143.md:673`–674 cites green CI and says the next Step 7a covers the commit. CI does not replace either review lens, and the bundle does not establish that the next review’s scope includes `5e78941`. Under the rule stated in the review request, deferral until before the next close is permissible; treating recording as completion is not. Record a pending external review of that commit before the next close, plus the missing compliant internal review. The actual post-close WORKFLOW clause and the commit’s diff are absent, so its precise applicability cannot be independently checked here.

- **NOTE — The floor-update instructions remain slightly inconsistent.** `.claude/agents/reviewer.md:9` still enumerates three coordinated changes; `docs/WORKFLOW.md:418` correctly requires four, including the harness. Update the embedded enumeration to prevent another partial bump.

verdict: APPROVED-WITH-WARNINGS
reviewed-against-commit: bbf1e3a

---

## Post-close commit 5e78941 (APPROVED-WITH-WARNINGS)

Review performed on **inlined source only**; no commands or tests were run.

- **NOTE — The clock injection addresses the reported CI failure.** `tests/StatsTid.Tests.Regression/Infrastructure/TemporalWriteZeroWidthReopenTests.cs:90–96`: using the seeded date’s fixed clock in `EmployeeProfileRepository` makes the intended deletion interval `[2025-03-12, 2025-03-12)`, assuming the clock and repository behave as described. This restores the scenario rather than weakening its assertion. The visible `Assert.Null` at line 111 can fail when a row still covers that date.

- **WARNING — The supplied diff cannot establish that every regression check detects the defect it is intended to prevent.** `tests/StatsTid.Tests.Regression/Infrastructure/TemporalWriteZeroWidthReopenTests.cs:111,171`: the visible assertions check absence before recreation; the subsequent recreation assertions are omitted. The repository and `FixedTimeProvider` implementations are also absent. Consequently, full verification of non-vacuous reopening checks and clock consumption remains outstanding.

- **NOTE — These tests use a valid Danish date fixture but do not test the Danish calendar boundary.** `tests/StatsTid.Tests.Regression/Infrastructure/TemporalWriteZeroWidthReopenTests.cs:80–82`: UTC midnight falls on the same Copenhagen date for this fixture. A mistaken UTC-date implementation could therefore pass these tests too; separate boundary coverage is needed to establish S142 compliance. “The one instant” is inaccurate: many instants share the same date in both calendars.

- **NOTE — No product behavior or invariant implementation changes in this commit.** `tests/StatsTid.Tests.Regression/Infrastructure/TemporalWriteZeroWidthReopenTests.cs:96,110,171`: the complete supplied diff changes only test setup and comments. No `src/**` changes or changes to audit recording, delivery guarantees, architecture, or access control are present.

verdict: APPROVED-WITH-WARNINGS  
reviewed-against-commit: 5e78941

---

## Cycle 4 — on 2cffd0e (APPROVED-WITH-WARNINGS)

Review performed on **inlined source only**. No commands or tests were run; conclusions cover the supplied bundle.

1. **NOTE — RESOLVED: Implementation work now receives a usable routing instruction.** `.claude/hooks/model-routing-guard.ps1:133–138` acknowledges false positives and directs implementation, tests and documentation to named implementation roles; actual review work goes to `reviewer`.

2. **NOTE — RESOLVED: The workflow now explains the detector’s limits accurately.** `docs/WORKFLOW.md:356–370` describes detection of the observed signature, explicitly acknowledges that prompts omitting the phrase pass, and retains routing discipline as the broader control. Manual checks are reported separately from the close-guard harness; their execution is not independently verified here.

3. **NOTE — RESOLVED: The client-update explanation now separates evidence from inference.** `docs/WORKFLOW.md:378–394` leads with the practical action and observed failed pins, labels the proposed fallback mechanism as untested inference, accounts for the environment override, and makes the Fable outcome conditional. The unsupported exclusivity claim is removed.

4. **NOTE — RESOLVED: Suffix handling preserves the model check and rejects arbitrary bracketed text.** `.claude/hooks/sprint-close-guard.ps1:224–227` removes only a terminal digits-plus-`m` suffix. Empty, arbitrary, nested or repeated brackets leave a nonmatching model string; a wrong base model remains wrong. This validates suffix **shape**, not whether a numeric capacity is supported. `.claude/hooks/test-sprint-close-guard.ps1:309–314` meaningfully tests the negative: the old expression would remove `[garbage]`, whereas T22 requires rejection and a diagnostic containing it. Execution remains unverified. `.claude/agents/reviewer.md:9` now permits the documented context suffix consistently with the gate.

5. **NOTE — RESOLVED: The monitoring instructions now identify the same detector events and transcript location.** `docs/operations/model-routing-register.md:54–56` includes the new generic-spawn detector alongside reviewer blocks, matching `docs/WORKFLOW.md:367–368`. The obsolete transcript folder is corrected at register line 39.

6. **WARNING — NOT RESOLVED: The historical review was completed, but its record still needs reconciliation.** `docs/sprints/SPRINT-143.md:676–687` correctly records the external `APPROVED-WITH-WARNINGS` verdict and explicitly assigns the recreation-assertion check and comment correction to S144 Step 7a. However, the contradictory change classification and incomplete residual scope below prevent full resolution. The newly supplied tests-only evidence also means cycle 3’s requested internal review should not be treated as automatically required.

7. **NOTE — RESOLVED: The reviewer now names every coordinated floor update.** `.claude/agents/reviewer.md:9` lists the reference list, close-guard pin, reviewer self-check and harness fixtures, agreeing with `docs/WORKFLOW.md:425–431`.

Additional absorption findings:

- **WARNING — The sprint record still gives two incompatible descriptions of the same commit.** `docs/sprints/SPRINT-143.md:667` calls `5e78941` a post-close `src/**` change, while line 678 and the supplied historical verdict establish tests-only changes. Correct line 667; the routing deviation remains real without attributing product-code changes to it.

- **WARNING — The follow-up scope does not explicitly carry every outstanding verification point.** `docs/sprints/SPRINT-143.md:682–687` assigns non-vacuity and the comment fix, but omits explicit verification of repository clock consumption and separate Danish-calendar boundary coverage identified by the historical verdict. Name those checks in S144 Step 7a, or cite existing coverage and its disposition.

- **NOTE — The post-close exemption is consistent with the quoted rule, but the governing clause is missing.** `docs/sprints/SPRINT-143.md:679–687` is consistent with a rule limited to external review of post-close `src/**` changes. However, the supplied WORKFLOW excerpt does not contain that clause. Its precise applicability—and the categorical statement that no internal review is owed—cannot be independently confirmed from this bundle.

verdict: APPROVED-WITH-WARNINGS  
reviewed-against-commit: 2cffd0e

---

## Cycle 5 — on b37961e (APPROVED)

Review performed on **inlined source only**. No commands or tests were run.

- **RESOLVED — Change classification:** `docs/sprints/SPRINT-143.md:667–669` now describes `5e78941` as tests-only, consistent with the historical verdict and lines 678–679, while preserving the routing-deviation finding.
- **RESOLVED — S144 follow-up scope:** `docs/sprints/SPRINT-143.md:686–690` explicitly carries recreation-assertion non-vacuity, repository consumption of the injected clock, Danish-calendar boundary coverage, and the comment correction. These checks remain pending; their documented scope is now complete.
- **RESOLVED — Governing WORKFLOW clause:** `docs/sprints/SPRINT-143.md:691–693` accurately cites `docs/WORKFLOW.md:461–463`, which requires the external lens for post-close `src/**` changes. The categorical assertion that no internal review is owed has been removed.

No new defects identified in the supplied diff.

verdict: APPROVED  
reviewed-against-commit: b37961e

---

## Cycle 6 — on 7c96faf (APPROVED-WITH-WARNINGS)

Review performed on **inlined source only**. No commands or tests were run.

- **WARNING — The corrected history still wrongly says the telemetry omitted the Fable reviewer.** `docs/sprints/SPRINT-143.md:670–672`: the supplied telemetry explicitly includes `12:15:50Z | spawn | reviewer | inherit | ALLOW`, immediately before the Opus spawn. Say the earlier review **overlooked** that entry, or identify a filtered extract that excluded it. The owner’s request and Fable verdict support the central correction: a floor review existed, alongside an owner-requested comparison.

- **WARNING — The monitoring rules still repeat the history this change retracts.** `docs/operations/model-routing-register.md:54–56`: signal 3 still describes a “below-floor review of `5e78941`” and counts only detector blocks on **generic** spawns. That conflicts with the corrected S143 row and `docs/WORKFLOW.md:386–387`, which counts detector blocks across the expanded role coverage. Update both the historical description and scope.

- **WARNING — A possible explanation for model selection becomes a stated fact elsewhere.** `docs/WORKFLOW.md:407–413,427,437–438`: reload lag is appropriately introduced as an inference, but subsequent text asserts that the pin “fell back silently,” that the spawn “ran on the previous definition,” and that an unloaded pin fails silently. The evidence establishes the selected model and the API receiving the pinned ID; it does not establish the reload mechanism. Preserve that distinction throughout. For PM readability, lead step zero with three actions: update the client, verify the **running session’s** version, and confirm the spawned model; place the detailed chronology afterward.

- **WARNING — The guard’s header promises an exemption the code no longer gives.** `.claude/hooks/model-routing-guard.ps1:25–27`: a named role without a model override can now be blocked by the prompt detector at line 116. Qualify that exemption. Also list `fork` among the detector exclusions at lines 110–111 and `docs/WORKFLOW.md:370`; line 96 exempts it explicitly.

- **NOTE — The comparison procedure is sound if its output remains separate from the required floor-review artifact.** `docs/WORKFLOW.md:380–385`: `comparison-arm-model: <id>` does not match the supplied detector. Removing all matching reviewer placeholders and floor clauses lets an explicit Opus generic spawn pass. Under the documented close rule, that output cannot satisfy the required `reviewed-by-model:` field; placing it in the required reviewer artifact would block closure rather than make it an accepted floor review. Explicitly instruct keeping the artifacts separate. The close-guard implementation was not supplied, so its parsing cannot be independently verified here.

- **NOTE — The narrower detector makes a reasonable, explicitly limited trade-off.** `.claude/hooks/model-routing-guard.ps1:106–118`: literal model-ID fixtures now pass, while the observed self-granted clause remains blocked across implementation roles. Reviews avoiding these strings still pass, and harmless quotations of the floor clauses still block. The documentation acknowledges signature detection rather than comprehensive enforcement; its false-positive explanation should cover quoted clauses as well as placeholders.

- **NOTE — Some historical details remain assertions rather than independently checkable evidence in this bundle.** `docs/WORKFLOW.md:400–406`; `docs/sprints/SPRINT-143.md:688–692`: the supplied greps show the pinned API model, but omit the full version-error text and edit chronology. The governing “Post-Step-7a coverage” clause and commit ancestry establishing the S144 base are also absent. These claims are not contradicted here, but this review cannot verify them.

verdict: APPROVED-WITH-WARNINGS  
reviewed-against-commit: 7c96faf

---

## Cycle 7 — on ab3145b (APPROVED-WITH-WARNINGS)

Review performed on **inlined source only**. No commands or tests were run.

1. **WARNING 1 — RESOLVED.** `docs/sprints/SPRINT-143.md:670–674` now correctly states that telemetry contains both spawns and attributes the earlier error to overlooking the reviewer entry.

2. **WARNING 2 — RESOLVED.** `docs/operations/model-routing-register.md:54–56` correctly describes the owner-requested comparison and counts detector blocks across roles.

3. **WARNING 3 — NOT RESOLVED.** `docs/WORKFLOW.md:432` still asserts that the pin “fell back silently,” presenting an unverified mechanism as fact. Replace this with the observation: the spawn ran on a model other than the pinned one without reporting an error. The changes at `:418–421` and `:442–443` correctly preserve that distinction. Step zero at `:398–401` now leads with three understandable actions; its PM readability is improved.

4. **WARNING 4 — RESOLVED.** `.claude/hooks/model-routing-guard.ps1:25–28` qualifies the no-override exemption; `:111–112` and `docs/WORKFLOW.md:370–371` explicitly exclude forks from detection.

5. **NOTE 1 — RESOLVED.** `docs/WORKFLOW.md:386–389` explicitly separates comparison output from the required reviewer artifact. Actual close-guard parsing remains unverified because its implementation is not supplied.

6. **NOTE 2 — RESOLVED.** `docs/WORKFLOW.md:374–377` now acknowledges false positives from quoted clauses as well as placeholders. This matches the detector at `.claude/hooks/model-routing-guard.ps1:118`.

7. **NOTE 3 — NOT RESOLVED; evidence limitation remains.** `docs/WORKFLOW.md:407–413` and `docs/sprints/SPRINT-143.md:690–694` retain historical claims whose underlying error transcript, edit chronology, governing coverage clause, and commit ancestry are absent. This is not evidence that those claims are false.

**New NOTE — unsupported attribution of what someone read.** `docs/sprints/SPRINT-143.md:672–673` says the reviewer “read only the second line” and the Orchestrator proceeded “without noticing the first.” The supplied evidence establishes an erroneous conclusion, not precisely what either participant read or noticed. Prefer “the earlier review overlooked the reviewer entry, and the Orchestrator accepted that finding.”

verdict: APPROVED-WITH-WARNINGS  
reviewed-against-commit: ab3145b

---

## Cycle 8 — on b8009f4 (APPROVED-WITH-WARNINGS)

Review performed on **inlined source only**. No commands or tests were run.

- **WARNING 3 — RESOLVED.** `docs/WORKFLOW.md:432–433` now describes the observed model mismatch and absence of an error without asserting a fallback mechanism.
- **S143 attribution NOTE — NOT RESOLVED.** `docs/sprints/SPRINT-143.md:672–674` removes the claim about what participants noticed, but still asserts that the Orchestrator accepted the finding “without checking it against the log or re-reading” its transcript. The supplied evidence does not establish which checks occurred. Use: “the cycle-1 review overlooked the reviewer entry, and the Orchestrator accepted that finding.”
- **Prior NOTE 3 — NOT RESOLVED.** The historical evidence limitation remains at `docs/WORKFLOW.md:407–413`; this diff supplies no underlying evidence to resolve it.

No separate new defect identified; the revised attribution retains the existing evidentiary problem.

verdict: APPROVED-WITH-WARNINGS  
reviewed-against-commit: b8009f4

---

## Cycle 9 — on bf98e30 (APPROVED-WITH-WARNINGS; only the inlined-source evidence NOTE open)

Review performed on **inlined source only**. No commands or tests were run.

- **S143 attribution — RESOLVED.** `docs/sprints/SPRINT-143.md:673–674` explicitly identifies the unchecked acceptance as the Orchestrator’s own account. A first-person admission is sufficient evidence for an attributed self-report; it does not establish independent verification.
- **Prior NOTE 3 remains unresolved**, as recorded in the supplied cycle-8 verdict at `docs/WORKFLOW.md:407–413`; this change does not address that historical evidence limitation.

No new defect identified.

verdict: APPROVED-WITH-WARNINGS  
reviewed-against-commit: bf98e30


---

## Cycle 10 — on 9c951d0 (APPROVED-WITH-WARNINGS)

Review performed on **inlined source only**; no commands or tests were run.

The central correction is supported: acknowledgement is no longer presented as completed work. Items 5–7 are attributed to the floor review; items 8–9 appropriately remain comparison-arm findings requiring verification. The appendix correction and guard cleanup match the supplied evidence.

- **WARNING — The new summary still says the review is pending even though this commit addresses its blocking verdict.** `docs/operations/model-routing-register.md:70` lists internal cycle 3 as “pending” and discusses only the earlier blockers. Record cycle 3’s BLOCKED verdict against `7c96faf`, distinguishing this commit’s corrections from their subsequent verification.

- **WARNING — The older summary retains the explanation the reviewer challenged.** `docs/sprints/SPRINT-143.md:652` still describes the UTC reads as “one Copenhagen day from the routing they drive,” then appends the finding that their date-origin comments are false. Replace the challenged explanation with the verified finding and its outstanding comment correction; appending a qualification leaves the PM with competing accounts.

- **WARNING — The comparison check assumes a clean workspace, but the workflow normally reviews uncommitted work.** `docs/WORKFLOW.md:386` requires clean `git status`, while step 7a at `:38` defaults to uncommitted sprint changes. Require comparison against a recorded pre-review working-tree baseline instead. Also avoid claiming `reviewer` has no write capability: the supplied role description includes `Bash`; absence of dedicated editing tools does not establish filesystem write prevention.

- **NOTE — The suffix evidence rules out a unique routing signature, but does not establish its origin.** `docs/WORKFLOW.md:418` supports saying `[1m]` also appears on an alias-selected model and therefore cannot establish session-model fallback. Calling it a “client artefact” states a causal conclusion the supplied observations alone do not prove.

- **NOTE — The archived evidence remains only partly inspectable in this review.** `docs/sprints/SPRINT-143.md:698–700` cites tracked archive files, whose additions appear in the supplied stat. Their contents were omitted, so faithful preservation of the verdicts remains unverified. The head-to-head figures at `:686–690` likewise have support from the supplied internal report, rather than a directly inlined transcript line 10923.

verdict: APPROVED-WITH-WARNINGS  
reviewed-against-commit: 9c951d0

---

## Cycle 11 — on e0eb34e (APPROVED-WITH-WARNINGS)

Review performed on **inlined source only**; no commands or tests were run.

- **WARNING 1 — RESOLVED.** `docs/operations/model-routing-register.md:70` now records cycle 3’s BLOCKED verdict against `7c96faf`, corrections in `9c951d0`, and verification pending separately.
- **WARNING 2 — RESOLVED.** `docs/sprints/SPRINT-143.md:652` leads with the false-comment finding and required correction. The earlier date-routing explanation is explicitly unconfirmed.
- **WARNING 3 — NOT RESOLVED.** `docs/WORKFLOW.md:386` correctly allows an initially dirty workspace and acknowledges that `Bash` can write. However, identical before/after `git status` output does **not** establish unchanged contents: an already-modified file can be edited again without changing its status. Require a recorded content baseline covering tracked and untracked files, then compare against it.
- **NOTE 1 — RESOLVED.** `docs/WORKFLOW.md:418` limits the suffix conclusion to what the observations support and leaves its origin unknown.
- **NOTE 2 — NOT RESOLVED (evidence limit).** `docs/sprints/SPRINT-143.md:686–690, 698–700` (cycle-10 references): the archive contents and underlying transcript remain absent, so preservation and figures cannot be independently verified.

No additional defect identified in the supplied diff beyond the incomplete workspace-check correction above.

verdict: APPROVED-WITH-WARNINGS  
reviewed-against-commit: e0eb34e

---

## Cycle 12 — on 9936e99 (APPROVED-WITH-WARNINGS)

Review performed on **inlined source only**; no commands or tests were run.

- **WARNING 3 — NOT RESOLVED.** `docs/WORKFLOW.md:386`: the content baseline fixes the already-modified text-file case, but default `git diff HEAD` does not capture binary contents. Different edits to an already-modified binary can produce the same “Binary files … differ” output. Use `git diff --binary HEAD` or checksum tracked files as well.
- **New defect — WARNING.** `docs/WORKFLOW.md:386`: the untracked-file pipeline mishandles filenames containing spaces, quotes, or newlines. Use `git ls-files --others --exclude-standard -z | xargs -0 …`, check command failures, and store baseline artifacts outside the inspected workspace.
- **NOTE 2 — NOT RESOLVED (evidence limit).** `docs/sprints/SPRINT-143.md:686–690, 698–700` (previously cited locations): archive contents and the underlying transcript remain absent, so preservation and figures cannot be independently verified.

verdict: APPROVED-WITH-WARNINGS  
reviewed-against-commit: 9936e99

---

## Cycle 13 — on 4caea8c (APPROVED-WITH-WARNINGS)

Review performed on **inlined source only**; no commands or tests were run.

- **WARNING 3 — RESOLVED.** `docs/WORKFLOW.md:386`: `git diff --binary HEAD` captures binary changes.
- **Untracked-file WARNING — NOT RESOLVED (partially fixed).** `docs/WORKFLOW.md:386`: `-z/-0` fixes filename handling, and `$TEMP` moves baseline artifacts outside the workspace as instructed. However, checking the pipeline’s exit status can miss a failed `git ls-files` because Bash normally reports only the final command’s status. Require `set -o pipefail` or explicitly check each pipeline stage before accepting the baseline.
- **NOTE 2 — NOT RESOLVED (evidence limit).** `docs/sprints/SPRINT-143.md:686–690, 698–700` (previously cited locations): archive contents and the underlying transcript remain absent.

verdict: APPROVED-WITH-WARNINGS  
reviewed-against-commit: 4caea8c

---

## Closure

The external loop was closed after cycle 13 at `4caea8c`: no BLOCKER since cycle 1, the last two cycles at shell-recipe level, and the one standing NOTE an inlined-source evidence limit (the bundle cannot carry the 2026-09-24 transcript or the archive files). The cycle-13 residual (`set -o pipefail`) was applied in `8e0cfab` without a further cycle.
