# REFINEMENT — S129: Security threat-model sweep — rev 6

> **TRACKED SNAPSHOT (2026-08-13).** This is a git-tracked mirror of the plan of record, which
> normally lives in the gitignored `.claude/refinements/REFINEMENT-s129-security-sweep.md`. It was
> committed here so an in-flight S129 resume works on a **different device** (the gitignored original
> does not travel via git). It is the converged rev 6 (five dual-lens cycles). If you edit the plan
> further, edit the gitignored original on the working device and re-snapshot here. This snapshot is
> committed AFTER the sweep baseline SHA `e955e13`, so it is still ABSENT from the sweep worktree —
> the calibration isolation is unaffected (same as the register). The 3 pre-registered calibration
> holes are NOT in this file (they live only in the owner-held manifest).

**Rev 6 (cycle-5 closeout):** cycle-5 confirmed rev 5's two substantive closures held (prompt-channel
control CLOSED both lenses; baseline-SHA-pin mechanism CORRECT both lenses, register/SPRINT-129
verified absent at HEAD) and found three ONE-LINE defects, all now fixed + command-verified: the
deploy inventory globs (`**/mock*` matched nothing — mocks are directories → `**/mock*/**`; `.github`
needs `--hidden`) and a leftover copy of the retracted "only in refinement + manifest" sentence in
the calibration section. The corrected `rg` commands were run and return the expected files (12 mock,
3 workflows, 7 Dockerfiles, 2 compose). No design change; the finding stream reached one-line
mechanical nits — converged.

Rev 5 absorbs the Step-4 **cycle-4** (final targeted) verification. **Both lenses confirmed the
cycle-3 blocker — the calibration filesystem leak — is now STRUCTURALLY CLOSED** by the clean-worktree
isolation. The lenses diverged on the residue: the internal Reviewer cleared kickoff (no blocker,
one accuracy fix), while Codex stayed BLOCKED on two narrow points. Rev 5 closes all three, all cheap:
(a) **Codex A — the prompt-authoring channel:** the Orchestrator holds the answers AND writes the
prompts, so "keep prompts general" was unenforced → now fixed per-slice templates (no per-hole slot)
+ verbatim archival + external Codex audit of the issued prompts. (b) **Codex B — the inventory
extraction commands** were shell-dependent → rewritten as runnable `rg -g` invocations. (c)
**internal accuracy:** the rev-4 claim that the six holes live "only in the refinement + manifest"
was false (they also enter the tracked register), so the real closure — the worktree pinned to the
**pre-register baseline SHA**, at which BOTH the untracked trio AND the not-yet-written register are
absent — is now stated explicitly and made a hard AC. Rev 1's core intent survives from the start;
only the mechanisms hardened.

**Absorption lineage:** rev 2 ← cycle-1 (Codex 2B/7W/1N + internal 4B/8W/8N); rev 3 ← cycle-2
(Codex 3B/6W/2N + internal 1B/6W/5N); rev 4 ← cycle-3 (1 convergent BLOCKER + internal 2W/1N);
rev 5 ← cycle-4 (leak CLOSED both lenses; Codex 2 narrow residuals + internal 1 accuracy fix).
The finding stream is converging — 3 blockers → 1 → 0-structural — not diverging.

**What You Asked For**
Adopt threat-modeling (vendored autoresearch security mode) with revisit semantics for prior rulings.

**What You Actually Need** *(unchanged)*
A systematic whole-attack-surface audit under our governance — the modality our diff-scoped reviews
lack — feeding a durable register in which past rulings are re-attacked, not shielded.

**STAKES FRAMING (owner, 2026-08-12) — this is a HOBBY/LEARNING project.** Nothing is deployed;
there is no real payroll, persondata, or PII at risk. CLAUDE.md's "production-grade Danish state
SaaS" is the aspirational design target the exercise role-plays toward, not literal production
stakes. Findings are **craft/quality/learning** value — keeping the codebase honest and testing the
owner's own rulings — NOT active-vulnerability incident risk. Severity labels (Critical/High) are
used in the tool's OWASP sense for prioritization, not as real-world exposure claims. This does not
lower engineering standards (the governance + dual-lens + adversarial verification ARE the point);
it calibrates how urgency is described. **Scope boundary of the stakes framing (rev-3, Codex W +
internal N3):** it lowers *urgency language*, and it is *not* a claim that the codebase contains no
weaknesses — the sweep's own six unruled findings prove otherwise, and the public GitHub Actions +
its real `ANTHROPIC_API_KEY` are a genuinely *active* external surface even while the app is
undeployed. A finding that compromises the Security & access-control invariant is therefore still
**fixed or explicitly escalated to the owner** — never dismissed by invoking "hobby project."

**Vetting verdict** *(unchanged from rev 1)*: vendor ONLY `security.md` + `security-checklist.md`
(both clean, MIT); the plugin's session-wide hooks are disqualifying; `--fix` never used.
*(Rev-2 addition, internal W8)*: the vendored skill is **invoke-by-name only** (narrow description,
no auto-trigger wording), grep-gated for hook registration/network calls/`--fix` at vendor time,
and the vendored text itself gets an external-lens review. NOTE: `.claude/skills/` is TRACKED —
the vendored skill will be public; it contains method, not findings — acceptable.

## Findings already in hand (cycle-1 review byproduct — enter the register as origin `swept-unruled`)

The review lenses themselves surfaced, pre-sweep. **These six are documented in THREE places: here
(this gitignored refinement, with file:line); the owner-held calibration manifest (the three chosen
holes); AND — once TASK-B runs — the tracked S129 register + `SPRINT-129.md` adjudication sections
(origin `swept-unruled`). The calibration protocol relies on the clean-worktree enforcement below,
whose load-bearing precondition is that the sweep worktree is pinned to the PRE-REGISTER BASELINE
SHA: at that SHA the gitignored trio is untracked-and-absent AND the register + SPRINT-129 do not yet
exist, so ALL THREE copies are absent from the agents' tree (rev-5 correction — the rev-4 claim that
the six live "only in the refinement + manifest" was false; they also enter the register, and the
real closure is the baseline-SHA pin, not the "only" property; internal cycle-4 catch). Note the
principle the cycle-3 defect taught: "gitignored" is a git-tracking property, not a read barrier — an
agent with `Read`/`Glob` can open a gitignored file, so exclusion must be structural (absent from the
tree), never instruction-only:**
- **`Auth:UseDatabase` fail-open** — `Program.cs` defaults it FALSE; the false branch is a
  hardcoded plaintext credential table incl. `admin01/admin` = GlobalAdmin. Only compose sets it
  true; any deployment missing that env var silently accepts hardcoded credentials. UNRULED.
- **Orchestrator `GET /api/orchestrator/tasks/{id}`** — EmployeeOrAbove with ZERO ownership/scope
  check (IDOR, A01).
- **Orchestrator `/execute`** — the unfloored `ValidateEmployeeAccessAsync` overload (the class
  ruled CRITICAL in SECURITY.md) + forwards the caller's raw Authorization header downstream
  (confused-deputy surface).
- **`POST /api/external/send`** — Authenticated-only, forwards caller-supplied arbitrary JSON to
  the external system. No role floor, no scope.
- **RuleEngine.Api** — 8 endpoints at "Authenticated" only (no DB — the purity invariant, ADR-002;
  the DEP-001 shorthand elsewhere is the calendar-dependency entry — cite ADR-002 for no-DB — so
  org-scope enforcement is structurally impossible there; the boundary needs a different control).
- **Frontend stores bearer tokens in `localStorage`** (`AuthContext.tsx` / `api.ts`) — XSS ⇒ token
  theft; makes the browser part of the AUTH chain, not UX polish.

## TASK-B — the SEC register (corrected inventory + form)

**Form (internal W2, N1, N4, N7; rev-3 internal W7):** a **pointer index**, not a restatement —
columns: opaque `SEC-NNN` id, **one-line plain-language "what this hole means"** (rev-3 — subordinate
to the citation, NOT a restatement; CONVENTIONS.md requires governance artifacts carry a
human-readable summary so a PM can rule from the register without chasing every pointer), title,
origin (`ruled-revisit` / `swept-unruled` / `sweep-NEW`), severity, OWASP/STRIDE, status, disposition
date, **citation to the single source of truth** (SECURITY.md line / sprint+ruling-letter / KB id),
and **a citation to this sprint's tracked adjudication record** (see below). Statuses: `NEW /
known—should-be-revisited / re-ratified / overturned / carried—no-new-evidence / accepted(new ruling)
/ fixed(cites S1XX task id — never in-sprint)`. Gets an `anchor-sprint` marker (accepting the
per-sprint freshness obligation), a CLAUDE.md doc-map row, and a SECURITY.md cross-link.

**Durable tracked adjudication record (rev-3 — Codex B1 + internal W6; the Auditability invariant).**
The register row is an INDEX; the *reasoning* must survive a fresh clone. So every adjudicated row
cites a section of the **tracked** `docs/sprints/SPRINT-129.md` that records, in PM-readable prose:
the SEC id, the prior disposition, a summary of the NEW evidence with `src/` file:line + commit
citations, the finder verdict, the refuter verdict(s), any disagreement and how it was resolved, the
owner decision, the owner's rationale, the date, and the remediation pointer. The gitignored
`.claude/reviews/security-sweep-*/` dir holds the *raw* transcripts (refuter prompts + verdicts,
archived verbatim — a named AC input) as SUPPORTING material, never as the sole proof. **Both OQ
rulings (2026-08-12 public/no-redaction; 2026-08-13 bounded browser-auth) are transcribed into the
tracked sprint log too** — this refinement itself is gitignored and cannot be the record of an owner
ruling.

**Revisit rows — the corrected FULL harvest (internal B1/B4, N8; rev 1 swept only sprint logs and
missed the standing security doc):**
1. The SECURITY.md revocation-residual map, ALL owner-accepted rows: role-assignment deactivation
   window; user-deactivation across 3 write paths / 2 lock domains; **JWT 8h expiry with no
   revocation list**; the S91 secondary-principal binding (with its own written follow-up); the
   S98 create/transfer-vs-delete window (non-MAO cases). **Inclusion rule (rev-3, internal N2):**
   these are the standing accepted RESIDUALS. Deliberately excluded: the S83-R2 inactive-manager
   persisted-only revoke corner (rides with its closed parent ruling) and the null-floored leader
   year-overview reads (a design exception, ADR-027 R9c, not a residual) — named here so the next
   census doesn't "rediscover" the exclusion as a gap.
2. The sprint-log holes: RES-002 9-read remainder (S128 R2 deferral); R6 legacy-SUBMITTED
   approvability (S127); the reopen read-fork (S128 R4); the self-approval class + HR/GlobalAdmin
   ORG_SCOPE_FALLBACK ruling (carried since S125); ProjectionBackfillService §3.4 unlocked writes;
   the non-whole-month natural-key probe residual; the S128 FU-A tier-probe log noise.
3. RES-003 item 4 — the in-memory-mirror-of-a-SQL-predicate convention (`PrefetchedAuthorityFacts`
   fails OPEN on omission).
4. The `check-overtime-governance` composed-unproved service↔service hop (WORKFLOW.md's own list).
5. Deployment-config class: compose dev JWT signing key; `statstid_dev` DB password; universal
   demo passwords; the mock services; `.github/workflows/claude*.yml` running with
   `secrets.ANTHROPIC_API_KEY` on comment/PR triggers (public repo). **This class is threat-modeled
   on its own terms (rev-3, Codex W): workflow `permissions:`, fork/comment-trigger behaviour,
   secret availability to untrusted triggers, token scopes, and action-SHA pinning** — an active
   external surface independent of deployment status.
6. The six `swept-unruled` findings above.

## TASK-C — the sweep (corrected scope + orchestration contract)

**Scope round 1 — FIVE API services (rev-3: renamed for accuracy; internal N1 + Codex B3; the
arithmetic "four" undersold the corrected scope and contradicted SECURITY.md's five-service
description).** The HTTP surface of ALL FIVE: Backend.Api (137 endpoint mappings across **25** files
— 24 `Endpoints/*.cs` + `ApiEndpoints.cs`; rev-3 corrected from "27"), Orchestrator, RuleEngine.Api,
Integrations.Payroll (5 endpoints + health), Integrations.External — plus the auth chain
(OrgScopeValidator, DesignatedApproverAuthorizer, ApprovalReadTier, JWT mint/validation), the
service↔service boundaries, and the enumerated deploy surface (compose ×2, mocks ×2, Dockerfiles,
appsettings/launchSettings, the three GH workflows). **Sliced by trust boundary, one slice per
iteration-group**: (i) employee↔leader↔HR↔admin tiers, (ii) service↔service + token forwarding,
(iii) deploy/CI/secrets, (iv) the named revisit targets, **(v) the bounded browser-auth chain —
storage, XSS sinks, proxy/CORS only (rev-3: OQ-2's owner-ruled pass, now a first-class slice with
its own coverage cells; internal W3 — it was resolved in prose but never wired into the execution
contract).** Full frontend sweep = round 2, scheduled with a closure criterion recorded in ROADMAP
(persistence/outbox consumers + dependency audit also round 2).

**Canonical coverage inventory (rev-3 — Codex B3 + internal W4; rev-4 reproducibility — internal W2).**
Before the sweep, the Orchestrator generates a **commit-pinned** inventory (cells) mechanically. The
generation is REPRODUCIBLE per category — each category names a RUNNABLE extraction (ripgrep does its
own glob expansion via `-g`, so patterns are `-g`-quoted, NOT shell-positional globs — rev-5, Codex
cycle-4 B: the rev-4 `src/**/...` positional forms were shell-dependent and did not expand under
ripgrep/PowerShell). The universe is regenerated from the pinned SHA and diffed, rather than resting
on one agent's assertion:
- **Endpoints** — `rg -n 'Map(Get|Post|Put|Delete|Patch)' -g 'src/**/Endpoints/*.cs' -g 'src/**/ApiEndpoints.cs' -g 'src/**/Program.cs'`
  (the same regex also catches the `Program.cs` inline maps; 137/25 for Backend.Api, verified).
- **Middleware / auth config** — `rg -n 'UseAuthentication|UseAuthorization|Use\w+Middleware|AddAuthorization|AddPolicy|RequireAuthorization' -g 'src/**/Program.cs' -g 'src/**/*Auth*.cs'`.
- **Service↔service** — `rg -n 'AddHttpClient|new HttpClient|BaseAddress|IHttpClientFactory' -g 'src/**/*.cs'` + the consumer call sites they resolve to.
- **Deploy** — the file set itself, via `rg --files --hidden -g '<glob>'` (rev-6: `--hidden` is
  REQUIRED — ripgrep skips dot-directories like `.github` by default, so without it the three GH
  workflows silently drop; and mock services are DIRECTORIES so the glob must descend into them —
  both were zero-match bugs in rev 5, caught by both cycle-5 lenses; a *silent-empty* is worse than
  an error because the Codex regen-and-diff backstop would reproduce the same empty result → a
  false-agreement empty diff): `-g 'docker-compose*.yml'` (×2), `-g '**/mock*/**'` (the mock-service
  dirs, ×2), `-g '**/Dockerfile'`, `-g '**/appsettings*.json'`, `-g '**/launchSettings.json'`,
  `-g '.github/workflows/*.yml'` (×3). Each command is run once at generation and its match count
  recorded, so a future zero-match regression is visible, not silent.
**Falsifiability backstop (rev-4):** the EXTERNAL lens (Codex) independently regenerates the inventory
from the same pinned SHA and diffs it against the Orchestrator's — a non-empty diff is a coverage gap
surfaced before the sweep, so the universe is not a single lens's claim. This inventory IS the
coverage-table universe — completeness is measured against it, not against a prose claim. Every cell
ends round 1 in one of two states: **examined** (a ledger row anchors it) or **explicitly
owner-deferred** (a named adjudication: accept-as-round-2 or extend). Silent non-examination is a
contract breach, not a permitted outcome.

**Orchestration contract (Codex B1 cycle-1; rev-3 hardening; rev-4 worktree isolation).** The
Orchestrator runs the loop; **each iteration = one fresh-context read-only agent spawned with a
capability-restricted tool profile — an `Explore`-type / read-only agent with NO Bash/PowerShell/
psql/docker tool, so "static-analysis only" is ENFORCED by the toolset, not merely instructed
(rev-3, internal W5: the machine hosts the owner's live native stack + demo DB; a trusted constraint
becomes a verified one).**

**Worktree isolation (rev-4, the cycle-3 convergent fix — Codex B2 + internal NEW-BLOCKER; rev-5
baseline-SHA precondition made explicit).** The discovery/calibration sweep agents run against a
**clean git worktree checked out from the PRE-REGISTER BASELINE SHA** (the sprint-baseline commit,
BEFORE TASK-B writes the register + `SPRINT-129.md`) — a TRACKED-files-only tree. At that SHA the
answer-bearing artifacts are ALL absent:
- the gitignored/untracked trio — this refinement in `.claude/refinements/`, the owner-held
  calibration manifest, the sweep scratch dir `.claude/reviews/security-sweep-*/` — is untracked, so
  a separate-directory worktree does not contain it (nor any uncommitted main-tree edit);
- the tracked `docs/operations/security-finding-register.md` + `docs/sprints/SPRINT-129.md` — which
  DO carry the full swept-unruled set (origin `swept-unruled`) — **do not yet exist at the baseline
  SHA**, so they are absent too.
An agent cannot `Read` or `Glob` what is not in its tree. **The baseline-SHA pin is load-bearing: if
the worktree were pinned to a later SHA (after the register lands), the tracked-doc path reopens — so
the pin is a hard requirement (AC below), not an implementation detail.** The Orchestrator passes
each agent its slice, the relevant ledger snapshot, the neutral coverage inventory, and (revisit
agents only) the specific target **inside the prompt** — so no agent needs filesystem access to
`.claude/` or to any post-baseline doc at all, and the read-scope exclusion stops being a mere
instruction. Same "enforce, don't instruct" principle as the tool profile. (A stale-clone objection
does not arise: the worktree is created fresh from the pinned SHA per run.)

Each iteration carries: the persona, ONE slice, the prompt-embedded ledger snapshot, and the
instruction to return a candidate ledger row. **No live requests, no demo-credential use, no
psql/docker invocation** — now structural. Cost controls (Codex W): per-iteration output budget,
dedup against the ledger before logging, max findings forwarded to verification per round, and an
OWNER CHECKPOINT before any extension past the planning estimate.

**Ledger integrity + write discipline (rev-3 — Codex W + internal N-fanout).** The gitignored sweep
dir is the single state, and it is protected: **the Orchestrator is the SOLE writer of `results.tsv`
and the SOLE assignor of `SEC-NNN` ids** — fan-out agents RETURN candidate rows, the Orchestrator
dedups across concurrent iterations and appends atomically at merge. Each row carries an **immutable
iteration id** and an **evidence hash**; rows are schema-validated on append; a checkpoint copy is
written each round. On resume, the ledger is re-validated fail-closed — a truncated/malformed/
conflicting file HALTS rather than silently under-counting coverage or calibration.

**Iteration lifecycle / nondeterminism (rev-3 — Codex W).** An iteration resolves to one of:
`accepted` (well-formed, advances its coverage cell), `rejected-malformed`, `retry-of:<id>`,
`timed-out`, or `abandoned-with-owner-ruling`. **Only `accepted` rows advance coverage.** Retries are
capped; a retry that diverges from its original is retained as calibration evidence, not discarded.

**Calibration control — leak-proofed (rev-3 — Codex B2 + internal B1, the convergent BLOCKER; this
is the sprint's one deliberately falsifiable mechanism and rev 2 let it be satisfied by reading the
answer sheet).** The problem: rev 2 withheld 3 holes from the agents' *briefing* only, while the same
holes are documented verbatim (with file:line) in tracked docs the agents statically read —
SECURITY.md's residual map and (per TASK-B, which precedes the sweep) the published register. So a
"rediscovery" could be a lookup. The fix, four parts:
1. **The 3 withheld holes are drawn from the six `swept-unruled` findings** — which are recorded in
   this refinement, the owner-held manifest, AND (once TASK-B runs) the tracked register + SPRINT-129.
   All of those are **absent from the clean worktree** pinned to the pre-register baseline SHA: the
   first two are untracked, and the register + SPRINT-129 do not yet exist at that SHA (the
   worktree-isolation rule above). This is the structural half of the fix: at the baseline SHA the
   answer sheet is physically unreachable, not just off the reading list. (Rev-6: this bullet
   previously repeated the retracted "only in refinement + manifest" claim — corrected to the
   baseline-SHA rationale, matching the header + Findings section; internal cycle-5 catch.)
2. **Pre-registration + a leak-resistant prompt channel (rev-5 — Codex cycle-4 A: the Orchestrator
   holds the manifest AND authors the prompts, so "keep prompts general" as bare discipline is an
   unenforced channel).** The Orchestrator commits the 3 chosen ids + an evidence hash to the
   owner-held manifest BEFORE round 1; scoring reveals them only AFTER round 1 closes. The
   discovery/calibration-slice prompts are a **FIXED per-slice template keyed ONLY on the trust
   boundary** (slice ii = "the service↔service + token-forwarding boundary", etc.) — the template has
   **no per-hole slot** to narrow into, and it is authored ONCE from the slice definition, not
   free-written per iteration. Every calibration-slice prompt actually issued is **archived verbatim**
   in the sweep dir, and the **external lens (Codex) audits the archived prompts post-hoc for
   answer-leakage** (a named AC). So the residual channel is converted from "trust the Orchestrator's
   discipline" to "template-constrained + archived + externally checkable" — the strongest closure
   available while the runner necessarily knows the pre-registered answers.
3. **Provenance rule in the ledger-row schema:** every row declares the sources it consulted; a
   rediscovery COUNTS only when its evidence is **code-anchored** (`src/` file:line) and cites
   **none** of the answer-bearing artifacts — the register / SECURITY.md residual sections / sprint
   logs / **this refinement / the manifest** (rev-4: the refinement + manifest added to the bar).
   Orchestrator-side dedup happens AFTER submission (never by handing agents the answer list).
4. **Slice partition (rev-4, internal W1 — the read-scope exclusion contradicted slice (iv)):** the
   worktree isolation + read-scope exclusion + provenance bar apply to the **discovery/calibration
   slices (i, ii, iii, v)** — they read code + the neutral coverage inventory + prompt-embedded prior
   findings, nothing answer-bearing. The **revisit slice (iv)** is a SEPARATE track: those agents ARE
   handed their specific residual target + its citation (that IS their job — re-attack the known
   hole), and their rows are marked origin `ruled-revisit` and are **INELIGIBLE for the calibration
   rediscovery count** (they were primed with the answer by design). The withheld 3 (swept-unruled)
   are disjoint from the slice-(iv) residual targets AND withheld from every briefing, so no slice-(iv)
   agent is handed a calibration answer.
Bar: **≥2 of 3 rediscovered independently** (by a discovery/calibration-slice agent, code-anchored,
no answer-artifact citation) → method validated. **1 of 3 → does NOT meet the bar (reported, not
hidden).** 0 of 3 → the method has failed and the sprint says so. The miss rate is recorded either way.

**Per-iteration ledger row (Codex B2 cycle-1 — information gain, not iteration count):** hypothesis /
attack vector; trust boundary + files examined (file:line); NEW evidence vs duplicate-of-SEC-NNN;
**sources consulted (the provenance rule above);** severity+mapping if a finding; explicit
no-finding-because with what was ruled out.

## TASK-D — verification (independence made testable; internal B3, Codex W)

Panel per candidate finding: (i) one fresh refuter agent given ONLY the claim + evidence (never the
finder's reasoning), instructed to refute; (ii) the EXTERNAL lens (Codex) as second refuter for
Critical/High. **The refuter PROMPTS + verdicts are archived verbatim (rev-3, internal W6) — the
independence property is only testable if a reader can see the refuter saw only claim+evidence.**
Conflicting verdicts → Orchestrator adjudicates with both transcripts, owner sees the disagreement.
REFUTED findings stay in the ledger as calibration data. Fallback if Codex is unavailable (N6):
HALT-AND-ASK, never single-lens (the standing rule).

**Revisit rows get the SAME refuter protocol (rev-3 — Codex W).** A revisit proposing `re-ratified`
or `overturned` is an adjudication resting on fresh adversarial evidence, so it goes through the
refute panel exactly like a NEW finding — otherwise a known hole could be re-ratified without anyone
independently re-attacking it, defeating the "re-attacked, not shielded" promise. Only
`carried—no-new-evidence` may take the documented no-new-evidence path (no fresh evidence was found,
so there is nothing to refute).

## Agent/write-authority map (internal B3)

| Task | Executor | Writes |
|------|----------|--------|
| A vendor skill | Orchestrator (cross-cutting, CLAUDE.md exception) | `.claude/skills/threat-model-audit/` |
| B register | Orchestrator (docs are Orchestrator-only) | `docs/operations/security-finding-register.md` + `docs/sprints/SPRINT-129.md` adjudication sections + CLAUDE.md row + SECURITY.md link |
| C sweep | capability-restricted read-only agents (fan-out) in a **clean pinned-SHA worktree**, Orchestrator loops + SOLE ledger writer | gitignored `.claude/reviews/security-sweep-*/` only |
| D verify | fresh refuter agents + Codex | same gitignored dir (transcripts archived verbatim) |
| E adjudicate | Owner + Orchestrator records | the register + the tracked SPRINT-129 adjudication sections |

## Open Questions (RESOLVED)

1. **Repo visibility — RESOLVED (owner ruling 2026-08-12).** The repo **stays PUBLIC, with NO
   redaction of the existing docs**; a genuine disclosure cleanup is deferred to IF the project moves
   from hobby toward something serious. **Framing correction (rev-3, Codex W):** the operative content
   of the ruling is an **acceptance of publication risk** — NOT a finding that the code is free of
   weaknesses (the "nothing is vulnerable" phrasing is not used as the sprint's rationale, because it
   is circular against a sweep whose job is to find weaknesses, and the six swept-unruled findings +
   the secret-bearing public workflows contradict it). Consequences: the register's pointer-index
   form is retained as good practice (single source of truth), NOT as secrecy; the gitignored sweep
   dir is hygiene, not confidentiality; a publication threat-model paragraph lands in the register
   documenting this risk acceptance; and any Security-invariant finding is still fixed-or-escalated,
   never waved off by the hobby framing.
2. **Bounded browser-auth pass in round 1 — RESOLVED (owner, 2026-08-13): YES, bounded**
   (storage/XSS-sinks/proxy/CORS only), now wired as slice (v) with its own coverage cells. The full
   frontend sweep is a round-2 candidate, scheduled on a closure criterion recorded in ROADMAP
   (rev-3: "launch-blocking" language recalibrated per CONVENTIONS — the term is retained with its
   design-target meaning, not retired; internal N3).

**Assumptions** *(rev-3)*
1. S129 = audit sprint per the design-only contract precedents (S28/S32/S36/S38/S67): test baseline
   **3269 carried from S128 — NOT re-executed in this audit sprint** (rev-3, Codex N: label it as
   carried, cite S128 CI run `31485462948` / close `3af7291`, and do not let the header imply fresh
   test verification), `Build Verified: N/A`, one-line precedent justification in the header.
2. Step 0b decision recorded explicitly (Security-invariant content in a docs-only sprint — run
   dual-lens 0b on the plan; internal W7). Step-7a artifacts per the close-guard's mechanical
   requirements (`verdict:` + `reviewed-against-commit:` == close parent).
3. Remediation = "the next remediation sprint", not a hardcoded S130 (ROADMAP rolling-detail rule);
   the round-2 closure criterion is written to the ROADMAP backlog by the same rule.
4. **The audit-contract proof is `git status --porcelain` over ALL trees with an EXPLICITLY
   ENUMERATED allowlist (rev-3 — internal W2 + Codex W; rev 2's "`docs/operations/` + sprint doc" was
   broad enough to hide unrelated edits AND too narrow to permit the mandatory close writes):**
   `docs/operations/security-finding-register.md`, `docs/SECURITY.md` (cross-link only),
   `CLAUDE.md` (doc-map row only), `.claude/skills/threat-model-audit/`, `docs/sprints/SPRINT-129.md`,
   `docs/sprints/INDEX.md` (sprint-log inventory — CI-checked by `tools/check_docs.py`),
   `docs/QUALITY.md` (per-sprint grade), and `ROADMAP.md` (round-2 closure criterion + remediation
   backlog row). The close captures the start commit + initial porcelain status; ANY changed path
   outside this set is a contract breach requiring owner sign-off, never silent widening; pre-existing
   owner changes are preserved and reported separately. (Not `git diff src/**` — the FAIL-003
   untracked-files lesson.)
5. Prior art acknowledged (internal N2 cycle-1): the S76 external whole-codebase trace found caller
   sets before; the novelty here is the durable register + revisit semantics + repeatable harness.

**Acceptance Criteria** *(rev-4 — falsifiable)*
- [ ] Vendored skill: line-by-line Orchestrator review + mechanical grep gate (hooks/network/--fix
      = zero hits) + external-lens review of the vendored text + invoke-by-name-only trigger.
- [ ] Register live with the FULL rev-4 inventory (every row cites BOTH its source-of-truth AND its
      tracked SPRINT-129 adjudication section; the one-line plain-language column present); CLAUDE.md
      doc-map row + SECURITY.md cross-link + anchor-sprint marker present.
- [ ] **Coverage completeness:** every cell of the commit-pinned canonical inventory is either
      `examined` (ledger-row-anchored) or `explicitly owner-deferred` — zero silent gaps. The
      inventory is reproducibly generated (per-category extraction named) AND independently
      regenerated + diffed by Codex (empty diff, or the diff resolved). The five-service + slice-(v)
      universe is fully represented in the coverage table.
- [ ] Sweep ledger: every `accepted` row populated with hypothesis/boundary/files/new-evidence-or-
      duplicate/**sources-consulted**; immutable iteration ids; single-writer append; resume
      re-validates fail-closed. (An iteration count is a cost checkpoint, NOT the completeness gate —
      coverage + validated rows are.)
- [ ] **Calibration: the discovery/calibration agents ran in a clean worktree pinned to the
      PRE-REGISTER BASELINE SHA — verified to contain neither the untracked trio (refinement/manifest/
      sweep dir) NOR the register/SPRINT-129 (which postdate that SHA); the calibration-slice prompts
      were fixed per-slice templates, archived verbatim, and externally audited (Codex) for
      answer-leakage; the 3 withheld holes drawn from the swept-unruled set, pre-registered before
      round 1; ≥2 of 3 rediscovered by a discovery-slice agent via code-anchored evidence with no
      answer-artifact citation; revisit-slice findings excluded from the count; the miss rate
      recorded.** 1-of-3 does not meet the bar; 0-of-3 is a reported method failure.
- [ ] Every revisit row: fresh adversarial evidence + the refuter panel (for re-ratified/overturned)
      + owner re-adjudication (re-ratified / overturned / carried—no-new-evidence), dated, with the
      evidence summarized in the tracked SPRINT-129 adjudication section.
- [ ] Every NEW finding: refute-panel verdict (CONFIRMED/REFUTED) before adjudication; Critical/High
      double-refuted (agent + Codex); refuter prompts + verdicts archived verbatim.
- [ ] Next-remediation-sprint proposal drafted from confirmed+overturned, ordered by severity ×
      invariant-impact.
- [ ] Audit contract: `git status --porcelain` clean outside the enumerated allowlist (Assumption 4)
      at close.
- [ ] Sprint mechanics: Step-0b decision recorded; Step-7a dual-lens artifacts; baseline 3269
      carried-not-re-executed, S128 CI run cited.

**Risks** *(rev-4)*: sweep agents must not probe the live local stack (TOOL-PROFILE-enforced, not
just instructed); the answer sheet must be unreachable (WORKTREE-enforced — absent from the clean
tree, not merely off the reading list); duplicate-flood at fan-out (single-writer Orchestrator
dedup); the register as a fourth copy of truth (pointer-index + tracked adjudication record, not raw
restatement); Codex/environment fragility (halt-and-ask); disclosure via tracked skill text
(method-only, accepted); calibration contamination (leak-proofed via worktree isolation + swept-
unruled selection + provenance rule + slice partition); coverage-inventory self-completeness
(reproducible generation + external diff); ledger corruption (schema-validated, checkpointed,
fail-closed resume).

**Readiness: KICKOFF-READY** — the cycle-3 blocker (calibration filesystem leak) is confirmed
STRUCTURALLY CLOSED by both cycle-4 lenses; the rev-5 prompt-channel + baseline-SHA closures are
confirmed by both cycle-5 lenses; the three cycle-5 one-line nits are fixed + command-verified in
rev 6. Finding stream: 3B → 3B → 1B → 0-structural → 3 one-line nits → converged. The remaining
non-blocking observation (the prompt-embedded ledger snapshot as a distinct, pre-existing channel —
internal cycle-5 out-of-scope note) is handled by the provenance bar + dedup and tracked as a sweep-
time discipline, not a plan defect. No open blocker. Kickoff proceeds per the owner's cycle-5
election (auto-kickoff on a clean final verify).
