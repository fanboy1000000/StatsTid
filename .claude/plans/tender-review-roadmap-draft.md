# Tender review → roadmap: a proposal for the owner

| | |
|---|---|
| **Status** | DRAFT 1, answered and adjudicated 2026-11-13. Where the adjudication differs (E1 item 4, C10's VD 29 line, § 4's blocked table, § 5), the adjudication and `ROADMAP.md` govern. |
| **Source** | `docs/references/tender-requirements-analysis.md`, § "Owner review (2026-11-13)" (`:528-786`), recorded at `837ecb29` |
| **Base commit** | `de3da64b` (local master; S148 wave 3 merged at `40d3e694`). Registers and `ROADMAP.md` read as they stand there |
| **Drafted by** | Planning Drafter (Opus 5.5). Read-only on `docs/**`, `ROADMAP.md`, `SYSTEM_TARGET.md` and code; no builds, no tests |
| **Evidence age** | The gap analysis rated the code at `67809348` (2026-10-26), with S147 notes added. Code facts this draft states in its own words were re-opened at `de3da64b`; line numbers cited to the analysis are the analysis's |

**Words used throughout.**
- **Epic**: a group of requirements delivered as one coherent piece of product, usually over one or more sprints.
- **SLS** (*Statens Lønsystem*): the state's payroll system today. **HR-Løn**: the state's HR-and-payroll system on top of it.
- **SLP**: the new state payroll system the owner named as the replacement for both (EM A22). The owner will supply its specifications.
- **FRAV**: the state's absence-statistics reporting (circular 2022/9247). The owner says it is moving into SLP (EM B1).
- **Navision Stat**: the state's finance system. **GIS**: ØS's standard integration interface to it.
- **ØS** (*Økonomistyrelsen*): the Agency for Public Finance and Management. **Statens SSO**: the state's common single sign-on, run by ØS.
- **ADR**: an architecture decision record. **QUAL / SEC / HRP**: rows in the quality, security and HR follow-up registers.
- **ADR-043 stages**: the build stages of the overtime/flex program (stage 0 and config in S148, stage 1 in S149, then stages 2 and 3).

---

## 1. In plain words: what the review says StatsTid should become

**The owner's review turns StatsTid from a calculation core into a product a state institution could adopt.**
Of the 215 requirements, 128 should be met. Only one of the 128 is met today, 55 are partly met and 72 are not met. The 85 marked "not relevant" are mostly supplier, hosting, support and contract duties (53). The other 32 are features the owner chose to leave out.

**What StatsTid should become.**
- **It plugs into the state's own systems instead of standing alone.**
  - People, their employment facts and their leaving arrive through the HR-system integration.
  - Who holds which role is set in an external user-administration system.
  - Sign-in goes through Statens SSO.
  - Pay and absence statistics go to SLP.
  - Approved project hours go to Navision Stat.
- **It covers the back office around the everyday loop.** HR acts on an employee's behalf from a screen. Employees send requests and leaders approve them. People get notifications. There is general reporting.
- **It adds shift planning** for institutions with shift workers. The owner says the open question is how to do it.
- **It keeps two things of its own.** Its own verified reading of the rules, not a tender's paraphrase (EM A16). And its own role model, with a shift planner and a shift user added (VD 8).
- **It meets the state's control requirements.** These are ØS's guide for systems whose data leads to payments (the owner flagged it as important, EM A38), an audit trail good enough for the National Audit Office, two people on every pay-affecting action, GDPR deletion, and backup.

**What it leaves out.** Excel and PDF export, printing, a feed to the state data warehouse (LDV), migrating data from a predecessor system, self-service passwords, a test environment per institution, uptime and licence counting, and all supplier duties.

**What it means for the path to production.**
- **The core comes first, and nothing in the review changes S148 or S149.** The rule engine, approval and audit stay the foundation, and they are being rebuilt now: the ADR-043 program, with S148 on stage 0 and the config stage, and S149 on the monthly norm.
- **The critical path runs through the owner's inputs, not through build capacity.**
  - 76 of the 128 requirements wait, wholly or partly, on something only the owner can supply: the integration specifications, the master-data list, what "extract" means, and how shift planning should work.
  - The other 52 can be planned now.
- **Several earlier decisions need re-deciding** (§ 3):
  - where role assignments come from;
  - how users authenticate;
  - "SLS" as the named payroll target;
  - the vacation-transfer approval;
  - two personal-data and visibility choices.

  The product definition (`SYSTEM_TARGET.md`) also gains two missing sections: inbound integrations and shift planning.
- **The roadmap gains one arc item and thirteen backlog rows** (§ 5). Five existing passages get a pointer or an added paragraph. Nothing is removed.

---

## 2. The epics

### 2.1 Overview

Sizes are rough, measured in sprints of today's scale:
- **S**: part of a sprint.
- **M**: about one sprint.
- **L**: two to three sprints.
- **XL**: a program of four or more sprints.

| # | Epic | Ids | Size | Waits on an owner input? | Already covered by |
|---|---|---|---|---|---|
| E1 | Working-time rules and configuration: what the tenders add to ADR-043 | 10 | L (≈ M beyond the planned program) | No (two items need research) | Mostly: ROADMAP §1 item 4, S148, ADR-043 stages 1–3 |
| E2 | Vacation: the holiday rules end to end | 10 | L | No (payout codes wait on E9) | Partly: three existing ROADMAP rows |
| E3 | The HR and leader back office | 10 | L | No (search waits on the master-data list) | No (S148 builds cell clearing) |
| E4 | Employee requests and notifications | 5 | L | No | One ROADMAP row (the leaver notification) |
| E5 | Reporting, statistics and data access | 7 | M–L | **Yes:** what "extract" means | No |
| E6 | Shift planning and the calendar | 16 | XL | **Yes:** how | Only ADR-043 stage 2's roster record (designed) |
| E7 | People and access from the state's systems | 4 | L | **Yes:** specifications, master-data list | The ROADMAP Statens SSO row |
| E8 | Integration foundation | 2 | M | **Yes:** built with the first specification | ADR-025 D1 (designed) |
| E9 | Pay and absence reporting to SLP | 8 | L | **Yes:** SLP specifications | ADR-034, HRP-022 (partly) |
| E10 | Navision Stat via GIS | 39 | XL | **Yes:** integration specifications | No |
| E11 | Audit, compliance, data protection and resilience | 9 | L | No (two research passes) | The conformance row; ADR-025 D3 (designed) |
| E12 | Usability, mobile access and the user manual | 8 | L | No | The accessibility row; QUAL-058 |
| | **Total** | **128** | | 76 wait (E5–E10) · 52 do not | |

### 2.2 The epics in detail

Each epic gives the plain-language what and why first, then the record. "Today" uses the gap analysis's evidence, cited as `TRA:<line>` (= `docs/references/tender-requirements-analysis.md`).

---

#### E1 — Working-time rules and configuration: what the tenders add to the ADR-043 program

**Requirement ids (10):** `EM A5` · `EM A8` · `EM A17` · `EM D1` · `VD 10` · `VD 11` · `VD 12` · `VD 20` · `VD 21` · `VD 45`

**What and why.** Both tenders ask StatsTid to support the state's working-time rules "fully" and to let each institution set its own local agreements. Most of that is the overtime/flex program already under way (ADR-043). The tenders add four things the program does not:
- configurable minimum and maximum sizes for a registration;
- VD's flex-capping scheme;
- more absence quotas;
- applying a new agreement version to everyone at once, with a mass recalculation.

The owner's "stick to our own analysis of rules" (EM A16, `TRA:591`) confirms the method. StatsTid's verified research decides what a rule is. An institution sets only the local values ADR-043 D6 names.

**Today.**
- The rule engine is deterministic, but its overtime/flex model is wrong in kind: a weekly norm, every surplus hour treated as overtime, and the wrong supplement table (`TRA:199`, `:206-207`; QUAL-216..222, QUAL-227).
- The editors do not reach pay (QUAL-233, `TRA:200`), and neither do local profiles (QUAL-204, `TRA:208`).
- Registration limits are coded. Only "whole days only" can be set, and only when a quota is created (`TRA:153`, `:176`).
- Quotas exist for care days, senior days and special holidays. There are no *cheffridage* (manager days off), and care days are flat per employee, not per child (`TRA:209`).
- Retroactive change works well per employee but is weak at the overall level. A new agreement version is a code change, and recalculation runs one month at a time, API only (`TRA:205`).
- Flex above the cap is clamped and paid out (`TRA:212`; QUAL-219).

**What it needs.**
1. The rest of the ADR-043 stage map as ruled: stage 1 (the monthly norm, flex by default), stage 2 (decided hours, +50 %, the +25 % supplement), stage 3 (AC claims) (`docs/knowledge-base/decisions/ADR-043-overtime-flex-monthly-norm.md:563-642`).
2. **VD 45 flex capping.** The owner's comment is "needs analysis and evidence" (`TRA:599`). The scheme needs a floor (−10 h), yearly or quarterly cut dates, conversion to *særlige omsorgstimer* (special care hours) at 2:1 up to 74 h, an HR approval step and advance notice (from E4). These are local settings beyond D6's caps. They need a sourced research pass first, and VD's annexes 12–13 are missing (`TRA:113`).
3. **Minimum and maximum hours per registration type as data** (EM A5, VD 10). These sit beside the absence day rules row (`ROADMAP.md:399-430`).
4. **Quotas.**
   - *Cheffridage*.
   - Care days per child, which needs children's data from E7's master-data list.
   - VD's unmatched *fasttidsdage* (`TRA:114`).
   - Adding a new quota type without code (the absence-to-entitlement map is hard-coded, `TRA:209`).
5. **The overall level.**
   - A new agreement version arrives as dated configuration. S148's config stage provides dated configs; the OK26 values are still placeholders (`TRA:205`).
   - A mass recalculation with a screen, for the months a change reaches.
6. **Per-institution configuration** (EM D1). S148's config stage makes the local profile reach pay. Per-institution feature flags (ADR-025 D6) are still unbuilt.

**Dependencies.** ADR-043 stages 1–3; E4 (VD 45's advance notice); E7 (children's data); research (VD 45). The civil-servant category that VD 11 names (`TRA:206`) goes with E6's VD 54 research.

**Size.** L overall. Beyond the already planned program, about M.

**Coverage.** Largely covered.
- `ROADMAP.md` §1 item 4 (`:42-69`).
- **S148** (`docs/sprints/SPRINT-148.md:5`):
  - the rest check reads Skema's clock times (QUAL-234), and wave 3b corrects the rule (QUAL-253, `quality-finding-register.md:505`);
  - the config stage delivers EM D1's and VD 20's local values to pay.
- **S149**: stage 1 (`ROADMAP.md:57-60`).
- **Not covered anywhere today:** VD 45's scheme, min/max registration rules, the extra quotas, mass recalculation.

---

#### E2 — Vacation: the holiday rules end to end

**Requirement ids (10):** `EM A9` · `EM A9b` · `EM A10` · `EM A11` · `EM A13` · `VD 13` · `VD 14` · `VD 15` · `VD 16` · `VD 18`

**What and why.** Vacation accrual and the vacation account are among StatsTid's strongest features. What is missing is the last mile, at year end and when someone leaves:
- the automated year-end close is switched off;
- year-end payouts cannot reach payroll;
- the transfer agreement has no manager approval and no screen;
- the forecast covers 12 months, not the 16-month holiday-taking period;
- special holiday days cannot be transferred;
- there is no leaver form.

The owner also asks for documentation of exactly what current legislation is (EM A9, `TRA:619`).

**Today.**
- Accrual is Met (`TRA:242`) and the account is Met since S147 (`TRA:243`).
- **The year-end close** (`TRA:245` item 3; `TRA:256` item e):
  - It is dormant until `Settlement:GoLiveDate` is set, and `docker/docker-compose.yml` does not set it.
  - Every payout line carries a placeholder code (`SLS_TBD_*`) that the export guard refuses (`TRA:256` item a).
- **The §21 transfer agreement:**
  - It is recorded by HR with no manager step (ADR-033 D8, `ADR-033-vacation-settlement-architecture.md:50`).
  - It has no screen. HR's list still says "skærm følger i S141" (a screen follows in S141) (`frontend/src/pages/admin/opfoelgning/FollowUpLists.tsx:672`), but S141 did not build it.
- The 16-month taking period is modelled only for settlement timing (`TRA:247`; ADR-033 D12). Special-day transfer is not modelled (`TRA:241`).
- Advance vacation is an automatic allowance, not a grant (`TRA:246`; a deliberate choice, `TRA:77`).
- **Leavers:**
  - No form or report gathers what the settlement needs.
  - The §26 payout request is API-only (`FollowUpLists.tsx:620`).
  - The §7 deduction is parked (`TRA:249`).

**What it needs.**
1. **A vacation-law reference.** One verified document of the current Ferielov and the state holiday circular, built from the three existing dossiers (`docs/references/vacation-settlement-law-research.md`, `ferie-transfer-timing-research.md`, `vacation-consumption-mechanism-research.md`) and re-verified. This answers the owner's EM A9 comment. S–M.
2. **The §21 transfer as an employee request with the manager's approval documented** (EM A9b, VD 14). This is built on E4's requests, and it changes ADR-033 D8 (§ 3, C8). Transferred days also get a separate balance on screen (`TRA:256` item c).
3. **The 16-month taking period,** modelled so the forecast covers it (EM A11, VD 16). This is domain modelling, not just a screen (`TRA:274`).
4. **Transferring *særlige feriedage*** (Cirkulære § 15 stk. 1). Research first.
5. **A leaver form:** one screen with everything the settlement needs, plus the §26 request screen. The §21 record (item 2) is one of the four write forms owed since S140 (`ROADMAP.md:320-322`).
6. **Switching the close on.** QUAL-133's reversal probe lands first (`ROADMAP.md:761-764`), then the real payout codes (from E9).
7. **Advance vacation.** Proposal: keep the automatic allowance. Add an explicit per-employee grant only if the reference in item 1 finds the law requires one.

**Dependencies.** E4 (requests); E9 (real payout codes); research (items 1 and 4).

**Size.** L.

**Coverage.** Not in S148 or ADR-043. Parts sit in existing rows:
- advance vacation for leavers (`ROADMAP.md:393-396`);
- the §15 stk. 1 go-live precondition (`:761-764`);
- the §21 and §26 write forms (`:320-322`).

---

#### E3 — The HR and leader back office

**Requirement ids (10):** `EM-G1` · `EM A1` · `EM A3` · `EM A4` · `EM A7` · `EM A29` · `EM A30` · `EM A31` · `VD 9` · `VD 32`

**What and why.** HR and local admins can do far more through the API than on screen. Both tenders need HR to act on an employee's behalf: during maternity leave, sickness, or when someone forgot. That means:
- registering and approving from a screen;
- seeing any month's approval status across the organisation;
- seeing anyone's balances;
- searching on master data;
- approving or reopening a group in one go;
- registering absence over a date range;
- adding a comment to a single registration.

**Today.**
- The API lets HR write, send and approve on an employee's behalf (`TRA:143`). The screens show only the logged-in user, and Teamoversigt lists only the people the actor is designated approver or stand-in for (`TRA:143`).
- S147 added one on-behalf screen, for a leaver's final month (`TRA:143`, S147 note).
- HR's status view covers only the last closed month (`TRA:147`). There is no date-range absence (`TRA:143`).
- Search covers name, username and e-mail, active people only (`TRA:159`).
- Bulk approval is a loop of single approvals in the browser, and there is no mass reopen (`TRA:161`). There are no per-registration comments (`TRA:160`).
- The flex figure shown is the last settled month's closing balance (QUAL-224, built in S146; `TRA:154`).

**What it needs.**
- **An "open employee" mode for HR and local admins** in Skema and Årsoversigt, to read, write and send. Every action is audited as on-behalf, and the four-eyes rules apply unchanged.
- **An HR overview with a month picker:** approved and not approved, across the actor's Organisations.
- **Balances per employee** for every modelled type.
- **Date-range absence** registration and removal.
- **Multi-criteria search**, including master data from E7.
- **Server-side mass approve and reopen** for a group HR selects. Each item keeps its four-eyes check, and reopening still stops at export (ADR-034).
- **Comments on a registration.** They are event-sourced and audited. A comment can contain health data, so it falls under GDPR (`ROADMAP.md:658-660`).
- **A running flex figure on any date** (EM A7), once ADR-043 stage 1 makes flex the default.

**Design choices this epic leans on.** A leader sees a month's figures only after it is sent (owner ruling S124, `TRA:74`). A month exported to payroll is never reopened (ADR-034, `TRA:75`). This draft proposes meeting the requirements without reversing either; owner question Q5 asks.

**Dependencies.** E7 (master data for search); ADR-043 stage 1 (the running flex figure). E4 puts requests into the monthly statement (EM A2 is placed there).

**Size.** L.

**Coverage.** None planned. The HR follow-up page (`ROADMAP.md:445-456`) is the base it builds on. S148 already builds cell clearing (TASK-14840/14841; `quality-finding-register.md:504`, QUAL-252).

---

#### E4 — Employee requests and notifications

**Requirement ids (5):** `EM A2` · `EM A14` · `EM A15` · `VD 22` · `VD 24`

**What and why.** StatsTid has no "request" and sends no notification of any kind.
- An employee cannot ask for anything ("transfer my vacation", "pay out instead").
- A leader cannot approve a request with a history.
- Nobody is reminded of a missing registration or an unapproved month.

Both capabilities are new. They are not extensions of something that exists.

**Today.**
- The only approval workflow is the monthly sign-off (`TRA:250`, `:253`).
- There is no mail library anywhere in `src/`: a search for smtp, MailKit and SendGrid finds nothing.
- HR's worklists are pull-only (`TRA:254`). The HR register's second finding is "nothing escalates" (`docs/operations/hr-follow-up-process-register.md:35`).

**What it needs.**
1. **A request.**
   - Employee-initiated and typed: vacation transfer, a payout wish, holiday payout and *ulempetillæg* (the unsocial-hours allowance).
   - Decided by whoever holds the employee's month-approval authority, never on one's own request.
   - With a history, a full answer on rejection (VD 24), and open requests shown in the monthly statement (EM A2).

   ADR-043's overtime marks already ride in month approval (ADR-043 D3, `:190-191`). The request object must not duplicate them.
2. **A notification capability.**
   - An in-app inbox first, then mail through a relay. In development the relay is a mock mail sink, the same pattern as `mock-payroll` (SEC-018).
   - Alert rules HR can configure over the HR register's processes: missing registration, missing sign-off, untaken vacation.
   - A log of every alert generated and sent.
   - Two named triggers rest on concepts StatsTid does not model: "vacation without pay" and "sickness that qualifies for *sygedagpengerefusion*" (`TRA:272`). Each needs a research pass.
3. **An ADR.** Notifications are outbound, asynchronous and must be idempotent, which places them in the external-integration context (`SYSTEM_TARGET.md:504-514`).

**Dependencies.** None blocking. Other epics consume this one: E2 (the transfer request), E1 (VD 45's advance notice), E6 (swap requests), and the leaver notification row.

**Size.** L.

**Coverage.** `ROADMAP.md:475-485` ("Notify the manager to give a leaver's final month its last approval") is its first use case. Not in S148 or ADR-043.

---

#### E5 — Reporting, statistics and data access

**Requirement ids (7):** `EM-G2` · `VD-G2` · `EM A23` · `EM A26` · `EM B31` · `VD 17` · `VD 60`

**What and why.** The owner wants "some general reporting functionality" (EM A23, `TRA:560`) and a way to pull data into tools such as SAS (EM B31). Excel/PDF export, printing, unlimited data-warehouse extracts and the LDV feed are marked not relevant. First, the owner wants to define what an "extract" means (VD 17, `TRA:631`). That definition decides this epic's size (owner question Q2).

**Today.**
- No statistics anywhere (`TRA:139`, `:155`).
- Every read is per employee or per screen. The documented API has 113 paths but no dataset endpoints, and only a person can log in, not a machine (`TRA:319`).
- No project-administrator role (`TRA:158`).
- No vacation-liability overview (`TRA:248`, `:259`).

**What it needs** (if Q2's recommended option is chosen):
- predefined statistics on absence and on project time, for HR, leaders and project administrators within their scope;
- a vacation-liability overview: earned, taken and untaken days for a chosen population;
- dataset endpoints with service credentials (from E8), for tools such as SAS;
- a **project-administrator assignment** (EM A26). Proposal: a scoped capability on one project, not a sixth role, in keeping with "we use our own roles" (VD 8);
- aggregation rules that protect health data, since sickness is GDPR Art. 9 data (`ROADMAP.md:658-660`).

**Dependencies.** Q2; E8 (service credentials); ADR-043 stage 1 or later. Statistics over today's flex model would mislead, so reporting should follow the corrected figures.

**Size.** M (screens and a dataset API) to L (with self-service queries).

**Coverage.** None. ADR-025 D5's cross-tenant reports (`ADR-025-multi-tenant-operational-concerns.md:121-139`) serve the operator, not the institution.

**Note on the review record.** The record's direction line groups VD-G2 with shift planning: "the open question is how (VD-G1, VD-G2)" (`TRA:540`). VD-G2 itself is about "control and statistics, plus payroll-administrative tools", with no shift planning (`TRA:168`). This draft places it here and reads the owner's "how" comment on it (`TRA:573`) as being about reporting. See Brief contradictions.

---

#### E6 — Shift planning and the calendar

**Requirement ids (16):** `VD-G1` · `VD 25` · `VD 46` · `VD 47` · `VD 48` · `VD 49` · `VD 50` · `VD 51` · `VD 52` · `VD 53` · `VD 54` · `VD 55` · `VD 56` · `VD 57` · `VD 59` · `VD 61`

**What and why.** Vejdirektoratet has shift workers. It needs to:
- create and maintain rosters (*vagtplaner*) with shift types: turn-around days that protect rest, on-call shifts, night shifts;
- let employees propose shift swaps, which a leader approves;
- have the system propose a roster from employees' availability;
- protect days off;
- warn the planner before a roster that breaks the rest rules is published;
- turn roster shifts into time registrations;
- keep Outlook calendars current.

None of this exists. There is also a correctness reason, not just convenience. The state's +25 % supplement applies to work "ordered or on an approved duty roster", so a roster is a pay input (`TRA:233`).

**Today.**
- No shift entity, screen or role, and no calendar integration (`TRA:213-226`).
- **Building blocks:**
  - The rest check reads Skema's clock times from S148 wave 3, and wave 3b corrects it to the law's 24-hour test (QUAL-253).
  - On-call time is refused at registration from S148 stage 0 (ADR-043 D11).
  - ADR-043 designs "an order, or a roster span" as stage 2's record of decided hours, with "a roster's recurrence" on it (`ADR-043:187-188`), and the +25 % supplement reads "placement ordered or rostered" (`ADR-043:364-372`).
- ADR-043 explicitly does not decide shift workers (`ADR-043:832`).

**What it needs.** Owner question Q1 decides the shape. If StatsTid builds it:
1. **The roster record.** It extends ADR-043 stage 2's order/roster span with shift templates and recurrence. Pay and planning then read one record.
2. **The planner capability and the shift-user view.** VD 8's comment: added "with the functionality".
3. **Plan-time rest warnings.** The rest rule is reused on planned intervals.
4. **The roster flows into Skema,** with actual hours compared against planned ones (VD 57).
5. **Shift swaps,** as a request type from E4.
6. **Automatic roster proposals from availability** (VD 50, 55). This is the heaviest part, a scheduling solver, and goes last.
7. **Outlook,** through Microsoft Graph: an outbound integration.
8. **Pay rules that read the roster.** Domain research comes first, on the shift-worker chapter that ADR-043 left open (`:832`):
   - VD 25: keep the night supplement during sickness. The owner's comment is "needs thorough analysis".
   - VD 52: short-notice summons (§ 16).
   - VD 54: civil servants' night pay. Civil servants (*tjenestemænd*) are not modelled at all (`TRA:221`).

**Dependencies.** Q1; ADR-043 stage 2 (the decided-hours record); E4 (swaps, notifications); E12 (VD 27's phone access to shift planning); domain research.

**Size.** XL.

**Coverage.** Only ADR-043 stage 2's roster span, which is designed but not built. **Timing matters:** stage 2's refinement designs that record, so Q1 should be answered before it.

---

#### E7 — People and access from the state's systems

**Requirement ids (4):** `EM A6` · `EM A18` · `VD 3` · `VD 33`

**What and why.** The owner's direction:
- users sign in through Statens SSO (EM A19);
- people are created, changed and closed through the HR-system integration (EM A33, VD 33);
- role assignments come from an external user-administration system (EM A34);
- StatsTid keeps its own role model and adds a shift planner and a shift user (VD 8).

Today StatsTid does all of this itself. Three of the four ids in this epic are only its visible tip. Most of the work comes from the direction set by four "not relevant" rows (§ 3, C1, C2, C4).

**Today** (code re-opened at `de3da64b`):
- **Login** is a username and a password checked with BCrypt (`src/Backend/StatsTid.Backend.Api/Endpoints/AuthEndpoints.cs:16`, `:69`). StatsTid then issues its own token, which lives 8 hours with no revocation (SEC-003, `security-finding-register.md:81`). There is no SAML or OIDC code in `src/`.
- **People** are created one per call (`AdminEndpoints.cs:854`). The only bulk import carries manager relations (`ReportingLineEndpoints.cs:898`; JSON, GlobalAdmin only, `TRA:328`).
- **Roles** are granted and revoked inside StatsTid (`AdminEndpoints.cs:3106`, `:3305`), audited with an expiry (`SYSTEM_TARGET.md:155`). There are five roles (`src/SharedKernel/StatsTid.SharedKernel/Security/StatsTidRoles.cs:5-9`).
- **Master data is thin by design.** There is no CPR number, payroll number or children's data (ADR-029; `TRA:78`, `:198`).
- **The frontend has no production web server.** It is absent from `docker/docker-compose.yml` (SEC-031; `TRA:369`).

**What it needs.**
1. **Statens SSO** as a SAML 2.0 Service Provider. `ROADMAP.md:152-206` already sets out what to settle:
   - no role claim arrives;
   - which identity key to use (`uniqueid` with `cvr`);
   - an unknown user is refused;
   - the local login stays for dev and test.

   It needs an authentication ADR.
2. **The HR feed.** People and their employment facts arrive (start and end dates, agreement, part-time fraction, position, organisation, perhaps the line manager, which EM A6 lists). They are written through ADR-040's dated writers, so the backdate worklist and the audit trail fire as for a human edit, and each change records its source.
3. **Role assignments from the user-administration system.** A provisioning interface maps them onto StatsTid's roles and Organisation-set scopes, audited with their source.
4. **Master data per the owner's list** (EM A6). Fields are added only where a requirement uses them. A CPR number would need a DPIA (a data protection impact assessment).
5. **A production web entry:** a serving layer with HTTPS (EM A18). This shares work with the production hardening pass.
6. **Leavers** (VD 33): deactivation comes from the feed. HR still sees and edits a leaver's registrations (on-behalf, E3). Deletion is in E11.

**Dependencies.** The owner's integration specifications, including which system is the HR source: Statens HR or SLP's HR part. The master-data list. The user-administration system's interface. E8; an authentication ADR; owner question Q4.

**Size.** L.

**Coverage.**
- The Statens SSO row (`ROADMAP.md:152-206`) and §1 item 5 (`:77-78`).
- The token rows (`:216-237`).
- SEC-020 and SEC-025 (`security-finding-register.md:118`, `:123`).
- Not in S148.

---

#### E8 — Integration foundation

**Requirement ids (2):** `VD-G4` · `EM B32`

**What and why.** Every connection to a state system needs the same plumbing:
- a way for a machine, not a person, to log in;
- versioned contracts in both directions, inbound as well as outbound;
- transfers that run on a schedule or on demand;
- file or web-service transport;
- a delivery receipt and reconciliation;
- a destination per institution.

StatsTid has an outbound-only integration context with a mock target and no contract. This epic names the shared pieces once, so E7, E9 and E10 do not each build their own.

**Today.**
- The external context is outbound only (`docs/ARCHITECTURE.md:16`, `:130`; `SYSTEM_TARGET.md:504-514`).
- `POST /api/external/send` forwards to a mock (`src/Integrations/StatsTid.Integrations.External/Program.cs:55`), and the code says "the external contract does not exist yet" (`:97`).
- The outbox poller retries, dead-letters and has a circuit breaker (`TRA:320`). No machine credentials exist (`TRA:319`). No scheduled export exists (`TRA:296`, `:338`).
- The payroll lock means "handed off", not "delivered" (ADR-034 D2, `ADR-034-payroll-export-lock.md:22`).
- A per-institution payroll destination is designed but not built (ADR-025 D1, `:26-55`).

**What it needs.**
- **Service-to-service identity** for external callers. This touches the shared-key residual SEC-036 (`ROADMAP.md:145-148`).
- **An inbound-integration context**, which the product definition lacks (§ 3, C5).
- **A scheduler** with per-institution intervals and an on-demand trigger.
- **Delivery receipts** that close ADR-034 D2's "handed off ≠ delivered".
- **Per-institution destinations,** generalising ADR-025 D1 beyond SLS.
- **Event subscriptions or webhooks** for case systems (EM B32).

**Dependencies.** Proposal: design it with the first real specification, not ahead of one. Building plumbing for an unseen interface risks building the wrong thing. Statens SSO's specification is public, so E7's sign-on half may be the first consumer.

**Size.** M.

**Coverage.** ADR-025 D1 (designed); the SEC-036 ledger entry (`ROADMAP.md:145-148`).

---

#### E9 — Pay and absence reporting to SLP

**Requirement ids (8):** `EM A22` · `EM B1` · `EM B2` · `VD 1` · `VD 2` · `VD 28` · `VD 41` · `VD 58`

**What and why.** StatsTid's payroll export is solid, but it talks to a stand-in. The owner says SLS and HR-Løn are being replaced by SLP, and FRAV by a module inside SLP. The specifications come later. Until then StatsTid can build the parts that do not depend on SLP's format, and should leave the rest.

**Today.**
- **The export:**
  - It is gated on an approved month and handles one employee and one month per call (`src/Integrations/StatsTid.Integrations.Payroll/Program.cs:232`).
  - It keeps an idempotent lock record with an audit row (ADR-034).
  - It posts StatsTid's own JSON to `mock-payroll` (`src/Integrations/StatsTid.Integrations.Payroll/Services/PayrollExportService.cs:78`, `:435`).
- The wage-type codes are internal stand-ins (`TRA:290`).
- No screen starts an export: "ingen skærm kalder den endnu" (no screen calls it yet) (`FollowUpLists.tsx:432`). There is no group export, and nothing runs automatically (`TRA:338`).
- Nothing reports to FRAV (`TRA:289`).
- No keying slips (*tastebilag*) are produced. "Vacation without pay" and the holiday-pay adjustment (løndel 5062) are not modelled (`TRA:288`).
- The supplement table is wrong until ADR-043 stage 2 (QUAL-227).

**What it needs.**
- **Now, independent of SLP** (proposal):
  - an HR export screen and a group export over the existing approval gate (HRP-022);
  - a scheduled hand-off of approved months behind the same gate, keyed to the export cutoff at the manager deadline (`SYSTEM_TARGET.md:196`). This is VD 58's "automatic".
- **After the specifications:**
  - SLP's format and transport;
  - the real wage-type catalogue, mapped through the existing dated `wage_type_mappings` table;
  - delivery receipts (E8);
  - FRAV's absence categories and reporting schedule;
  - the three keying-slip categories, if SLP still uses keying slips;
  - "money stays out of StatsTid" (ADR-033 D1) checked against what SLP accepts (§ 3, C3).

**Dependencies.** The SLP specifications (owner, EM-G8, `TRA:640`); E8; ADR-043 stage 2 (correct supplements before they are exported); E2 (settlement payouts).

**Size.** L.

**Coverage.**
- ADR-034; HRP-022 (`hr-follow-up-process-register.md:122`).
- ADR-025 D1 (designed).
- The conformance row names "the payroll interface's requirements toward Statens Lønsystem" (`ROADMAP.md:668-669`).

---

#### E10 — Navision Stat via GIS

**Requirement ids (39):** `EM B3` · `EM B4` · `EM B5` · `EM B6` · `EM B8` · `EM B9` · `EM B10` · `EM B11` · `EM B13` · `EM B14` · `EM B15` · `EM B16` · `EM B17` · `EM B18` · `EM B19` · `EM B20` · `EM B21` · `EM B22` · `EM B23` · `EM B24` · `EM B25` · `EM B26` · `EM B27` · `EM B28` · `EM B29` · `EM B30` · `VD 4` · `VD 39` · `VD 44` · `VD 62` · `VD 63` · `VD 64` · `VD 65` · `VD 66` · `VD 67` · `VD 68` · `VD 69` · `VD 70` · `VD 71`

**What and why.** The state's finance system wants two things:
- **The registration frame comes from Navision.** "Registration frame" means what hours can be registered against: dimensions, aliases, cases and case tasks. The owner says aliases are StatsTid's projects (EM B20, `TRA:661`).
- **Approved project hours go back to Navision.** They are posted to its journals with batch and voucher numbers, with reconciliation, a resend on error, a reversal and a new posting for each correction, and moves of hours between aliases.

This is the largest single block of the review: 39 of the 128 ids. The owner says the posting details need thorough analysis when the integration is specified (EM B9, B11, `TRA:650`, `:652`).

**Today.**
- One flat list of project codes per organisation (`docker/postgres/init.sql:1103`, `UNIQUE (org_id, project_code)`; `TRA:291`, `:294`).
- The leader's monthly approval already covers project hours (`TRA:314`). The event-sourced history gives a correction trail (`TRA:317`).
- Nothing for Navision, GIS or FTP (`TRA:285`, `:293`).

**What it needs.** Two halves, in this order:
- **(a) A multi-dimensional registration frame inside StatsTid,** kept in sync with Navision. It covers import, closing items Navision has closed, editing imported data, and a chart of accounts and *bogføringskreds* (accounting entity) per institution.
- **(b) Posting approved hours back,** with SFTP or FTP transport (EM B5), reconciliation and corrections, kept current with Navision's versions (VD 39).

The GIS guidance has not been researched (`TRA:502`).

**Dependencies.** The integration specifications (owner, EM-G8); E8; GIS research. Proposal: after E9, which proves the foundation on a smaller integration.

**Size.** XL.

**Coverage.** None.

---

#### E11 — Audit, compliance, data protection and resilience

**Requirement ids (9):** `EM A37` · `EM A38` · `EM §8.3` · `VD 35` · `VD 36` · `VD 42` · `VD 43` · `VD 72` · `VD 75`

**What and why.** This is StatsTid's strongest match with the tenders, and the place where the owner flagged ØS's guide for payment-derived systems as important (EM A38, `TRA:714`). The tenders want:
- that guide met and documented;
- an audit trail good enough for Rigsrevisionen (the National Audit Office);
- two people on every pay-affecting action;
- logs delivered on request;
- GDPR deletion;
- backup.

**Today.**
- Every event records its actor, when it happened and when it was stored. Approved months are locked.
- Four-eyes on the month was finished in S147 (SEC-053 and SEC-054 closed; `TRA:412`).
- **Still one person alone can** (`TRA:412`):
  - run corrections (`/recalculate`);
  - change pay-affecting configuration (agreement publish, wage-type mappings);
  - trigger settlement payouts.

  **From S148's config stage, a single administrator's publish changes pay** (§ 3, C7).
- **The trail has gaps:**
  - registrations are not in the audit view (`TRA:375`, `:413`);
  - there is no log export (`TRA:413`);
  - append-only is a convention, not enforced in the database (`TRA:375`).
- GDPR erasure is designed, not built (ADR-025 D3, `:74-102`).
- There is no backup (`TRA:417`).
- The ØS guide was read only at heading level (`TRA:502`). Rigsrevisionen's requirements were not researched (`TRA:520`).

**What it needs.**
1. **The ØS guide mapped clause by clause,** as the first source of the conformance register (`ROADMAP.md:632-699`).
2. **Four-eyes extended to pay-affecting configuration, corrections and settlement payouts.**
3. **The audit trail completed:**
   - registrations in the audit view ("who registered what, when, for which day");
   - an audit-log export;
   - database-level append-only protection.
4. **GDPR erasure** (ADR-025 D3 Part B, with its S70 constraints, `:99-102`) and a retention period, including VD 33's "delete a leaver after a set period".
5. **Backup and a tested restore.** Hosting shares this, with the production hardening pass.
6. **Rigsrevisionen research.**

**Dependencies.** Research (items 1 and 6); §1 item 5 (hosting for backup); E7 (the leaver lifecycle).

**Size.** L.

**Coverage.** The conformance row (`ROADMAP.md:632-699`; it already lists the tenders at `:672-677`); §1 item 5; ADR-025 D3; ADR-026.

---

#### E12 — Usability, mobile access and the user manual

**Requirement ids (8):** `EM A21` · `VD 27` · `EM A32` · `EM A36` · `EM E4` · `VD 83` · `VD 85` · `VD 87`

**What and why.** Both tenders want people to reach StatsTid from a phone (VD on iOS and Android). They also want:
- Danish error messages that say what to do next;
- Danish labels throughout;
- a Danish user manual that matches the running version;
- release notes per version.

**Today.**
- No app or installable web app, and the pages are not designed for phone widths (`TRA:372`, `:410`).
- Raw backend errors reach the screen (QUAL-058, `quality-finding-register.md:236`; `TRA:162`), and some labels are English or written in ASCII (`TRA:166`).
- No user manual (`TRA:396`, `:428`). No versions and no release notes (`TRA:429-430`).

**What it needs.**
- **Responsive pages and an installable web app** (a PWA) first. A native app only if the PWA fails a requirement (proposal). VD 27 also covers shift planning on the phone.
- **The error mapping** (QUAL-058) and Danish server messages.
- **A user manual** maintained with each release, so it never describes another version.
- **Versioned releases with release notes.**

**Dependencies.** E6 (VD 27's shift-planning part); the accessibility row (`ROADMAP.md:504-507`) belongs in the same pass; a PWA needs HTTPS (E7 item 5, the hardening pass).

**Size.** L.

**Coverage.**
- The accessibility row (`ROADMAP.md:504-507`).
- The four-eyes refusal text row (`:508-513`).
- QUAL-058.
- The developer-facing onboarding idea (`:814-817`), which is not a user manual.

---

### 2.3 Placement check

**Method.** A script (`node`, run from the scratchpad against this file and against the owner's table, `TRA:528-786`):
1. extracts every id the owner marked "Should meet";
2. extracts every id listed on a "**Requirement ids (N):**" line in § 2.2;
3. checks that each of the 128 is placed exactly once, that nothing else is placed, and that each epic's declared count N matches its list.

**Result** (run 2026-11-13 on this draft):

```
should-meet ids in owner table: 128 (unique 128)
epics with an id line: 12
  E1: declared 10, listed 10      E7: declared 4, listed 4
  E2: declared 10, listed 10      E8: declared 2, listed 2
  E3: declared 10, listed 10      E9: declared 8, listed 8
  E4: declared 5, listed 5        E10: declared 39, listed 39
  E5: declared 7, listed 7        E11: declared 9, listed 9
  E6: declared 16, listed 16      E12: declared 8, listed 8
ids placed: 128 | distinct: 128
missing: none
placed more than once: none
placed but not should-meet: none
RESULT: PASS — all 128 placed exactly once
```

---

## 3. Conflicts with existing decisions

Each item says:
- what the owner's direction touches;
- what exists today;
- what would need re-deciding, and by whom: an owner ruling, an ADR amendment, or an update to `SYSTEM_TARGET.md`, which needs the owner's approval.

**C1 — Roles from an external user-administration system, while StatsTid keeps its own role model.**
- **The direction.** "Roles will be handled by external user administration system" (EM A34, `TRA:569`). Also "We use our own roles. Shift planner and shift users added with the functionality" (VD 8, `TRA:574`).
- **Today.**
  - StatsTid both defines its roles and assigns them: five roles (`StatsTidRoles.cs:5-9`; `SYSTEM_TARGET.md:16`, `:138`), granted and revoked in its own admin screen (`AdminEndpoints.cs:3106`, `:3305`).
  - Each grant carries an explicit Organisation-set scope (ADR-035 D1, `:18`), audited with who granted it, when and until when (`SYSTEM_TARGET.md:155`).
  - The scopes are copied into the login token (ADR-009, `:13-14`).
  - Approval authority does not come from roles alone. It comes from reporting lines, unit leaders and stand-ins (ADR-038 D4, `:40`).
- **The two statements fit together.** StatsTid keeps the vocabulary: what each role may do. The external system decides who holds which role.
- **To re-decide:**
  - **Where assignments come from.** Statens SSO presents no role claim (`ROADMAP.md:184-185`), so assignments must arrive through a provisioning interface, not through the sign-in.
  - **What happens to StatsTid's grant screen:** read-only, or kept for emergency access only (Q4).
  - **Auditability.** An assignment that arrives from outside must still be recorded with its source and time.
  - **The external system must express StatsTid's scopes,** which are explicit Organisation sets.
  - **Two new roles.** "5-role access control" (`SYSTEM_TARGET.md:16`, `:138`) gains a shift planner and a shift user. A planner is "planner of a unit or a roster", which fits the existing role ladder poorly. Proposal: a scoped assignment, like a unit leader (ADR-038 D3), rather than a sixth and seventh rung.
  - **Whether reporting lines also come from the HR feed.** EM A6's master-data list includes *personaleleder*, the line manager. If they do, the approval edges become fed data (C4).
  - **Who decides:** an ADR amending ADR-035 and ADR-009, and an update to `SYSTEM_TARGET.md` § F.

**C2 — Statens SSO versus today's username and password login.**
- **The direction.** "We will use Statens SSO" (EM A19, `TRA:708`). VD 6 (SSO, a pass/fail requirement) is also marked not relevant, and so is EM A20 (username and password with self-service reset).
- **Today.** A local login checked with BCrypt (`AuthEndpoints.cs:16`, `:69`), StatsTid's own token (8 hours, no revocation: SEC-003), and an opt-in in-memory credential table for development (SEC-020).
- **Read together, the three rows say:** no direct integration with each institution's directory, no self-service password reset, and Statens SSO as the way in.
- **To re-decide:**
  - an authentication ADR. The ROADMAP row says one is needed before any sprint (`ROADMAP.md:204-206`);
  - whether the local login survives in production or becomes development and test only (the owner's standing preference on dev/test ergonomics);
  - the token lifetime and revocation, ruled together with the agreement-code decision already owed (`ROADMAP.md:216-237`);
  - where SSO sits on the path. `ROADMAP.md:77-78` places it inside the production hardening pass. The review makes it product scope (E7). Proposal: a cross-reference, not a move (§ 5, R-2).

**C3 — SLP versus the SLS-shaped export, its codes and "money stays out".**
- **The direction.** "HRLøn and SLS are being replaced by a new system - SLP" (EM A22, `TRA:641`). FRAV moves into SLP (EM B1, `TRA:642`).
- **Today:**
  - The product definition names SLS: "payroll export (SLS or equivalent)" (`SYSTEM_TARGET.md:21`, `:494`). The wage-type sections map to "SLS code" (`:436-442`).
  - ADR-025 D1 designs a per-institution SLS destination (`:26-55`).
  - ADR-033 D1 rests on SLS turning StatsTid's day counts into kroner, verified only for *særlige feriedage* (`ADR-033:24`).
  - The codes are internal `SLS_0xxx` stand-ins, and the settlement codes are placeholders (`TRA:290`).
  - The payload is StatsTid's own JSON to a mock (`PayrollExportService.cs:435`).
- **To re-decide** (most of it only once the specifications arrive):
  - the product definition's wording: "SLS or equivalent" already allows another system, so this is a wording update;
  - whether SLP's catalogue maps onto StatsTid's dated wage-type table as the adaptation point (proposal: yes);
  - whether SLP accepts quantities only. If it needs amounts, ADR-033 D1 ("money stays out") is challenged, and that would go to the owner;
  - ADR-025 D1's destination, generalised to SLP;
  - **which payroll model applies.** VD 58 wants approved registrations that affect fixed pay transferred automatically. VD 2 and EM B2 want a manual export of one-off pay elements. Both are "should meet", and they are two models (`TRA:116`, `:358`). StatsTid also sends ordinary hours today. SLP's specification decides.

**C4 — People data from the HR system versus StatsTid's own HR edits and its data minimisation.**
- **The direction.** The identity lifecycle is handled through the HR-system integration (EM A33, VD 33, `TRA:568`, `:581`). The owner will supply a full master-data list (EM A6, `TRA:589`).
- **Today.**
  - HR edits employment facts inside StatsTid through ADR-040's dated writers, with an effective-date picker, a termination screen and a backdate worklist. Employment dates are HR-scoped (`SYSTEM_TARGET.md:476`).
  - StatsTid deliberately holds little personal data: no CPR number, payroll number or children's data (ADR-029; `TRA:78`).
- **To re-decide:**
  - whether fed facts are read-only in StatsTid or can be overridden there (Q4);
  - that fed changes go through the same dated writers, so the audit trail and the backdate worklist still fire;
  - which new personal fields are justified, field by field, against the owner's list. A CPR number needs a DPIA (the conformance row, `ROADMAP.md:658-660`).
- **Who decides:** an ADR for the inbound people feed; amendments to ADR-040 and ADR-029.

**C5 — Inbound integrations versus an integration model that is outbound only.**
- **Today.** The product definition says "System must support outbound API integrations … Never influence rule evaluation" (`SYSTEM_TARGET.md:504-514`). The architecture's external context is "Outbound integrations" (`docs/ARCHITECTURE.md:16`, `:130`).
- **What the review brings.** The review brings data in: people and roles (E7), Navision's registration frame (E10), and perhaps SLP master data. Fed employment facts do become inputs to rule evaluation (agreement, part-time fraction).
- **To re-decide** (a `SYSTEM_TARGET.md` section and an architecture decision):
  - Inbound data enters only through the same validated, dated, audited writers as a human edit.
  - The integration context never evaluates rules.
  - A failed feed never corrupts the deterministic core.

**C6 — Shift planning versus a product definition that has none.**
- **Today.**
  - `SYSTEM_TARGET.md` has no shift-planning section.
  - ADR-043 designs the roster span (`:187-188`) and the "placement ordered or rostered" fact (`:364-372`), but leaves shift workers undecided (`:832`).
  - VD 54 needs civil servants' night pay. Civil servants are not an employment category today (`TRA:221`).
- **To re-decide:**
  - a new `SYSTEM_TARGET.md` section, once Q1 is answered;
  - stage 2's refinement designs the roster record with shift planning in mind;
  - a research pass on the shift-worker agreement chapter and on civil servants.

**C7 — Four-eyes on pay-affecting configuration, which S148's config stage makes live.**
- **Today.** VD 35 (should meet) and ØS's guide (§ 5.5) want two people on payroll-affecting actions. The gap analysis lists what stays single-signer after S147 (`TRA:412`): corrections, agreement publish, wage-type mappings and settlement payouts.
- **Why it changes now.** Until S148 a publish did not reach pay (QUAL-233), so a one-person publish changed screens, not payslips. S148's config stage connects the editors to pay (ADR-043 D12; the engine switch, `PLAN-s148.md:33`). From then on, one administrator's publish changes pay for everyone on that agreement.
- **What I found in the registers.** I found no SEC or QUAL row for this, only the gap-analysis text. This is a finding to register, not an S148 scope change (see Needs a ruling).
- **To re-decide:** an amendment to ADR-043 D12 or ADR-014: who signs a publish.

**C8 — The §21 vacation transfer: HR-recorded versus the manager's approval documented.**
- **Today.** ADR-033 D8 records the transfer as HR-entered, "no employee+manager signing UI" (`ADR-033:50`; `TRA:76`).
- **The decision.** EM A9b and VD 14, both should meet, want the manager's approval kept in the solution. D8 called its choice "pre-launch depth", so the review supersedes it.
- **Proposal.** Amend D8 so that the transfer becomes an E4 request decided by the manager. This is a proposal, not a question.

**C9 — Two deliberate design choices meet "should meet" rows.**
- **The choices.** A leader sees a month's figures only after it is sent (S124, `TRA:74`; just enforced again by SEC-059, `security-finding-register.md:226`). A month exported to payroll is never reopened; changes go through corrections (ADR-034 D5, `:43`; `SYSTEM_TARGET.md:197`).
- **The rows.** EM A1, EM A4, VD 9 (overviews) and EM A31 and VD 32 (mass reopen) are marked should meet. Their gap texts name these choices as part of the gap (`TRA:142-143`, `:150`).
- **Why it matters.** The requirements can be met without reversing either choice. But if "should meet" was meant to reverse them, E3 changes and an ADR is needed. Owner question Q5.

**C10 — The "not relevant" decisions: which relieve a known gap and which only reshape it.**

| Decision | Effect |
|---|---|
| **EM A16** (the customer configures rules itself), with **VD 26**: "stick to our own analysis of rules" (`TRA:591`) | **Relieves** the "customer sets up agreements, absence types, accounts" gap (`TRA:200`, `:203-204`). Confirms the "glocal" principle: rule interpretation is the same for every institution (ADR-025 D6, `:166-173`). It does **not** close QUAL-233. That defect is being fixed in S148's config stage under ADR-043 D12. VD 20 and EM D1 (local settings) stay should meet. |
| **EM A19** (SSO through each institution's directory), with **VD 6** | **Reshapes, does not relieve:** "We will use Statens SSO" (`TRA:708`). SSO stays wanted (E7, C2). |
| **EM A20** (password login with self-service reset) | **Relieves** the reset flow and admin password reset. The local login becomes a development and test path (C2). |
| **EM A33** (deactivate, see, reopen and delete leavers): "identity lifecycle should be handled together with HR system integration" (`TRA:568`) | **Reshapes:** deactivation comes from the feed (C4). Seeing and editing a leaver's registrations stays wanted (VD 33, same comment, should meet, `TRA:581`). Deletion stays wanted (VD 75, should meet, `TRA:758`). |
| **EM A34**, **VD 34**, **VD 8** (roles and rights) | **Relieve** the StatsTid-side gaps: Local HR cannot grant roles, and there is no "who holds role X" overview (`TRA:164`, `:182`). **Reshape** ADR-035's grants (C1). |
| **EM A12, A24, A25, A35; VD 5, 29, 30, 37, 40** (liability extract, unlimited extracts, Excel/PDF, print, LDV, statistics for approvers) | **Narrow E5.** EM A23, EM B31, VD 17 and VD 60 remain should meet. Note that EM A12 (not relevant) and VD 17 (should meet) are the same vacation-liability need; the owner's VD 17 comment governs. |
| **EM-G6, VD-G7** (migrating data and opening balances from a predecessor system) | **Relieve migration.** They do **not** close QUAL-225 (no explicit opening flex balance, `quality-finding-register.md:477`). Every employee still needs an opening balance on the day the system goes live, which ADR-043 leaves open (`:836-837`). Proposal: keep QUAL-225 as it stands. |
| **EM-G4** (eight institutions, separate licences) | Commercial. One shared multi-tenant deployment (ADR-025) is unchanged, and EM D1 stays should meet. |
| **EM A27, EM A28, VD 31; VD §5.1, §5.6–5.8, §8, §10** | No roadmap change. Rigsarkivet's archiving duty stays in the conformance row (`ROADMAP.md:661-664`). Test environments, rollback and uptime stay hosting matters. |
| **EM B12** (all Navision versions) against **VD 39** (in-contract versions, should meet) | A minor tension. VD 39's narrower wording is what E10 carries. |

---

## 4. A proposed sequence (loose, in the roadmap's style)

**Now: S148** (stage 0 and the config stage; `SPRINT-148.md:5`).
- The tender ids it moves: EM A8, VD 11, VD 12 (the rest check reads Skema's clock times, and wave 3b corrects it to the 24-hour test), and EM D1 and VD 20 (local settings reach pay).
- It confirms EM A16's direction.
- C7 becomes live inside it.

**Next, already ruled:** S149 runs ADR-043 stage 1, the monthly norm. Stages 2 and 3 follow (`ROADMAP.md:57-60`).
- **One input is needed before stage 2's refinement:** owner question Q1, because stage 2 designs the roster record that shift planning would build on.
- **Register C7 before S148 closes,** so its fix can be scheduled with the config work it follows.

**Unblocked, ready to plan between or after the ADR-043 stages** (owner question Q3 picks the first lane):
- **Lane A — the HR back office (E3) with requests and notifications (E4).** The largest everyday gap, and it needs no outside input. E4 needs an ADR. It unlocks E2's manager approval and the leaver notification.
- **Lane B — the software half of compliance (E11):**
  - four-eyes on configuration, corrections and payouts;
  - registrations in the audit view and a log export;
  - the GDPR erasure design (ADR-025 D3 Part B);
  - the ØS guide clause by clause.
- **Lane C — Statens SSO, the sign-in half of E7.** Its specification is public (`ROADMAP.md:152-206`), so it needs only an authentication ADR. Provisioning waits for the specifications.
- **Lane D — vacation end to end (E2),** after E4's requests exist.
- **Lane E — mobile access and error messages (E12),** with accessibility.

**Cheap research passes to schedule early.** Each unblocks a later epic and uses the project's verified-research method:
- the vacation-law reference (EM A9);
- ØS's guide clause by clause, and Rigsrevisionen (E11);
- VD 45's flex capping and *særlige omsorgstimer* (E1);
- VD 25's sick-night supplement, the shift-worker chapter and civil servants' night pay (E6);
- "vacation without pay" and *sygedagpengerefusion* (E4, E9).

**Blocked on the owner's inputs:**

| What is blocked | What it waits on |
|---|---|
| E9 (SLP) and E10 (Navision Stat) | The integration specifications |
| E7's feed half | The specifications and the master-data list |
| E5 (reporting) | The definition of "extract" (Q2) |
| E6 (shift planning) | The "how" (Q1) |
| E8 (integration foundation) | Built with the first specification that arrives |

**Later:**
- E10 Navision Stat, the largest block. Proposal: after E9 has proved the foundation.
- The user manual, close to a go-live decision, so it describes the version that ships.
- Backup with real hosting, in the production hardening pass (§1 item 5).

---

## 5. Proposed edits to `ROADMAP.md`

The Orchestrator applies these after the owner agrees.

**Placement.**
- Rows go into the existing § 2 themes, with one exception. "Integrations with the state's shared systems" has no existing home, so it gets a new § 2 theme.
- The arc item is numbered 7, so the many "§1 item 5" citations in other documents stay valid.
- Both choices are flagged under "Needs a ruling".

**Paths.** Each row points to this draft (`.claude/plans/` is tracked), but carries enough text to stand alone.

**R-1 — § 1, new item 7, appended after item 6 (`ROADMAP.md:79`):**

```markdown
7. **Product scope for a state buyer: the tender review** (owner, 2026-11-13). The owner reviewed 215 requirements from
   two historical state tenders and marked 128 "should meet" (`docs/references/tender-requirements-analysis.md`,
   § "Owner review (2026-11-13)"). They group into twelve epics, each a row in § 2: rules and configuration (E1,
   mostly item 4's program), vacation end to end (E2), the HR and leader back office (E3), requests and notifications
   (E4), reporting (E5), shift planning (E6), people and access from the state's systems (E7), the integration
   foundation (E8), pay and absence reporting to SLP (E9), Navision Stat (E10), audit and compliance (E11), and
   usability, mobile and the user manual (E12). It runs alongside items 4 and 5, not after them; it is numbered last
   only so existing "§1 item 5" citations stay valid. **Waiting on the owner:** the integration specifications (SLP,
   Navision Stat, the HR system, the user-administration system), the master-data list, what "extract" means, and how
   shift planning is to be done. Detail: `.claude/plans/tender-review-roadmap-draft.md`.
```

**R-2 — § 1 item 5, change the last sentence (`ROADMAP.md:77-78`).** Replace
`A precondition for any go-live. **Includes authentication through Statens SSO** (owner-raised 2026-10-16; SAML 2.0 / OIOSAML Service Provider; see §2 "Security & access control").`
with:

```markdown
   A precondition for any go-live. **Includes authentication through Statens SSO** (owner-raised 2026-10-16;
   SAML 2.0 / OIOSAML Service Provider; see §2 "Security & access control"). Since the tender review (2026-11-13) SSO
   is also product scope, as part of epic E7 (item 7); its sign-in half can be built before this pass.
```

**R-3 — § 2 "Security & access control", the ★ Statens SSO row (`ROADMAP.md:152-206`).** Add a final sub-bullet after "Where it fits" (`:204-206`):

```markdown
  - **Tender review (owner, 2026-11-13).** EM A19 ("We will use Statens SSO"), VD 6 and EM A20 are marked not relevant
    as a per-institution directory login and as self-service passwords; Statens SSO is the way in. The local login
    becomes a dev/test path, to be ruled with SEC-020. The sign-in half belongs to epic E7 (§ 1 item 7); provisioning
    users and roles waits for the HR-system and user-administration specifications.
```

**R-4 — § 2 "Security & access control", new row after the SSO row:**

```markdown
- **★ People and roles from the state's systems (tender epic E7; owner, 2026-11-13).** The owner's direction:
  people are created, changed and closed through the HR-system integration (EM A33, VD 33); role assignments come
  from an external user-administration system (EM A34); StatsTid keeps its own role model and adds a shift planner and
  a shift user (VD 8). Today StatsTid does all of it itself (users one per call, roles granted in its own admin
  screen, five roles). **Needs:** an ADR for the inbound people feed, written through ADR-040's dated writers so audit
  and the backdate worklist still fire; a provisioning interface for role assignments onto StatsTid's roles and
  Organisation-set scopes, audited with their source; the master data on the owner's list (EM A6), field by field,
  with a DPIA for any CPR number; a production web entry with HTTPS (EM A18). **Re-decides:** ADR-035 and ADR-009
  (where assignments come from), ADR-040 (who writes employment facts), ADR-029 (which personal data is held),
  `SYSTEM_TARGET.md` § F ("5 roles"). **Waits on:** the integration specifications (including which system is the
  HR source), the master-data list, the user-administration system's interface. Ids: EM A6, EM A18, VD 3, VD 33.
```

**R-5 — § 2 "Security & access control", new row:**

```markdown
- **Four-eyes on pay-affecting configuration, corrections and settlement payouts (tender VD 35, ØS guide § 5.5;
  epic E11).** S147 put two people on every month (SEC-053, SEC-054). Still one person alone: an agreement-config
  publish, a wage-type mapping change, a correction (`/recalculate`) and a settlement payout
  (`docs/references/tender-requirements-analysis.md`, VD 35). **Live from S148's config stage**, which makes a publish
  change pay (ADR-043 D12). Needs a register row and an ADR-043 D12 / ADR-014 amendment naming who signs a publish.
```

**R-6 — § 2 "Correctness / domain", new row:**

```markdown
- **Working-time rules: what the tenders add to ADR-043 (tender epic E1; owner, 2026-11-13).** Most of the tenders'
  working-time requirements are §1 item 4's program. The rest: VD 45's flex capping (a −10 h floor, yearly or
  quarterly cut dates, conversion to særlige omsorgstimer 2:1 up to 74 h, HR approval, advance notice; the owner:
  "needs analysis and evidence"); minimum and maximum hours per registration type as data (EM A5, VD 10, beside the
  absence day rules row); more quotas (cheffridage; care days per child, which needs children's data from E7); a new
  agreement version applied as dated configuration with a mass recalculation screen (EM A17); per-institution
  feature flags (ADR-025 D6). The owner's EM A16 ruling, "stick to our own analysis of rules", keeps StatsTid's
  verified research as the authority. Ids: EM A5, EM A8, EM A17, EM D1, VD 10, VD 11, VD 12, VD 20, VD 21, VD 45.
```

**R-7 — § 2 "Correctness / domain", new row, and a pointer on the advance-vacation row:**

```markdown
- **Vacation: the holiday rules end to end (tender epic E2; owner, 2026-11-13).** Accrual and the account are strong;
  the last mile is missing. **Needs:** a verified vacation-law reference built from the three dossiers (the owner, EM
  A9: "documentation so we know exactly what the current legislation is"); the §21 transfer as an employee request
  with the manager's approval documented (amends ADR-033 D8; needs E4); a separate transferred-days balance on screen;
  the 16-month taking period modelled (ADR-033 D12) so the forecast covers it; transfer of særlige feriedage (research);
  a leaver form and the §26 request screen (the §21 record is one of the four write forms owed since S140); switching the close on, after the §15
  stk.1 precondition below and with real payout codes (E9). Ids: EM A9, EM A9b, EM A10, EM A11, EM A13, VD 13–16, VD 18.
```

Append to the "Forskudsferie (§7 advance-vacation) cap for LEAVERS — site 6" row (`ROADMAP.md:393-396`): ` → part of tender epic E2.`

**R-8 — § 2 "Correctness / domain", new row:**

```markdown
- **★ Shift planning and the calendar (tender epic E6; owner, 2026-11-13: "The important question is how").** Rosters,
  shift types, swaps approved by a leader, rosters proposed from availability, protected days off, rest warnings
  before a roster is published, the roster flowing into Skema, and Outlook kept current (VD 46–57, 59, 61). Not only
  a convenience: the state's +25 % supplement applies to work "ordered or on an approved duty roster", so a roster is a
  pay input. ADR-043 stage 2 designs "an order, or a roster span" and leaves shift workers undecided, so **how shift
  planning is done must be decided before stage 2's refinement.** Research first: VD 25 (night supplement during
  sickness, "needs thorough analysis"), VD 52 (short-notice summons), VD 54 (civil servants' night pay; civil
  servants are not modelled). Re-decides: a new `SYSTEM_TARGET.md` section. Ids: VD-G1, VD 25, VD 46–57, VD 59, VD 61.
```

**R-9 — § 2 "HR operations", new row:**

```markdown
- **★ The HR and leader back office (tender epic E3; owner, 2026-11-13).** HR can do far more through the API than on
  screen. **Needs:** an "open employee" mode for HR and local admins in Skema and Årsoversigt (read, write, send;
  audited as on-behalf; four-eyes unchanged); an approval-status overview with a month picker across the actor's
  Organisations; balances per employee for every modelled type; absence over a date range; search on master data
  (after E7's list); server-side mass approve and reopen for an HR-selected group (reopen still stops at export,
  ADR-034); comments on a registration (health data: GDPR); a running flex figure (after ADR-043 stage 1). The S124
  visibility rule and ADR-034's export lock stay unless the owner rules otherwise. Ids: EM-G1, EM A1, EM A3, EM A4,
  EM A7, EM A29, EM A30, EM A31, VD 9, VD 32.
```

**R-10 — § 2 "HR operations", new row, and a pointer on the leaver-notification row:**

```markdown
- **★ Employee requests and notifications (tender epic E4; owner, 2026-11-13).** Two new capabilities, not extensions.
  A **request**: employee-initiated and typed (vacation transfer, payout wish, holiday payout, ulempetillæg), decided
  by the month-approval authority, never on one's own request, with a history, a full answer on rejection, and open
  requests shown in the monthly statement (ADR-043's overtime marks stay in month approval). **Notifications**: an
  in-app inbox first, then mail through a relay (a mock sink in dev); alert rules HR configures over this register's
  processes; a log of alerts sent. Needs an ADR; two named triggers ("vacation without pay", sickness qualifying for
  sygedagpengerefusion) need research. Ids: EM A2, EM A14, EM A15, VD 22, VD 24.
```

Append to "Notify the manager to give a leaver's final month its last approval" (`ROADMAP.md:475-485`), after "…rather than as a one-off mail.": ` The capability is tender epic E4 (the row above).`

**R-11 — § 2 "HR operations", new row:**

```markdown
- **Reporting, statistics and data access (tender epic E5; owner, 2026-11-13: "We should have some general reporting
  functionality"; "We need to define what extract means").** Excel/PDF, print, unlimited extracts and the LDV feed
  are not relevant. **Waits on** the owner's definition of an extract. Then: predefined statistics on absence and
  project time within each viewer's scope; a vacation-liability overview; dataset endpoints with service credentials
  for tools such as SAS (EM B31); a project-administrator assignment (EM A26) as a scoped capability, not a new role;
  aggregation that protects health data. After ADR-043 stage 1, so statistics show corrected figures. Ids: EM-G2,
  VD-G2, EM A23, EM A26, EM B31, VD 17, VD 60.
```

**R-12 — § 2, new theme after "HR operations" and before "Usability / accessibility":**

```markdown
### Integrations with the state's shared systems (tender review, owner 2026-11-13)

*The owner will provide the integration specifications later (EM-G8). SLP replaces SLS / HR-Løn and FRAV (EM A22,
EM B1). Every row here waits on those specifications except the SLP-independent payroll items in E9.*

- **Integration foundation (tender epic E8).** The plumbing every connection needs: machine (service) credentials
  (touches SEC-036), versioned inbound and outbound contracts, a scheduler with per-institution intervals and an
  on-demand trigger, delivery receipts (closing ADR-034 D2's "handed off ≠ delivered"), per-institution destinations
  (ADR-025 D1, generalised), webhooks for case systems. **Re-decides:** `SYSTEM_TARGET.md` "External Integrations"
  and the architecture's external context, both outbound-only today: inbound data enters only through the same
  dated, audited writers as a human edit, and never evaluates rules. Designed with the first real specification, not
  ahead of it. Ids: VD-G4, EM B32.
- **Pay and absence reporting to SLP (tender epic E9).** The export is solid but posts StatsTid's own JSON to a mock
  with internal codes. **Now, independent of SLP:** an HR export screen and a group export over the approval gate
  (HRP-022); a scheduled hand-off at the export cutoff (VD 58). **After the specifications:** SLP's format and
  transport, the real catalogue through the dated wage-type mappings, FRAV's absence categories and schedule, keying
  slips if SLP still uses them, delivery receipts. **Re-decides:** "SLS" wording in `SYSTEM_TARGET.md`; ADR-025 D1;
  ADR-033 D1 if SLP needs amounts, not quantities; which payroll model (automatic transfer, VD 58, vs one-off
  elements, VD 2 / EM B2). Ids: EM A22, EM B1, EM B2, VD 1, VD 2, VD 28, VD 41, VD 58.
- **Navision Stat via GIS (tender epic E10).** The registration frame (dimensions, aliases = StatsTid's projects per
  the owner, cases, case tasks) imported and kept in sync; approved hours posted back with batch and voucher numbers,
  reconciliation, resend, reversal and repost, moves between aliases; SFTP/FTP. The owner: posting details "need
  thorough analysis when we spec the integration". The largest block (39 ids); proposed after E9. Ids: EM B3–B6,
  EM B8–B11, EM B13–B30, VD 4, VD 39, VD 44, VD 62–71.
```

**R-13 — § 2 "Usability / accessibility", new row:**

```markdown
- **Mobile access, error messages and the user manual (tender epic E12; owner, 2026-11-13).** Responsive pages and an
  installable web app (PWA) first, a native app only if the PWA fails a requirement (EM A21, VD 27, which also wants
  shift planning on the phone); Danish error messages that say what to do (QUAL-058) and Danish labels throughout; a
  Danish user manual and release notes kept in step with each version. The accessibility row above belongs in the
  same pass. Ids: EM A21, VD 27, EM A32, EM A36, EM E4, VD 83, VD 85, VD 87.
```

**R-14 — § 2 "Governance / docs", the ★ Standards and requirements conformance row.** Add to the "What real state buyers ask for" bullet (`ROADMAP.md:672-677`):

```markdown
      **Owner review (2026-11-13):** EM A38 ("This one sounds important"), EM §8.3 and VD 43, ØS's guide for
      payment-derived systems, are "should meet". Proposed as the register's first source, mapped clause by clause
      (the analysis read it at heading level only). Epic E11.
```

**R-15 — § 2 "Governance / docs", new row:**

```markdown
- **Audit trail, data protection and resilience for a state buyer (tender epic E11; owner, 2026-11-13).** The
  strongest match with the tenders; what is missing: registrations in the audit view ("who registered what, when, for
  which day"); an audit-log export to hand to a controller (VD 36); database-level append-only protection; GDPR
  erasure (ADR-025 D3 Part B, with its S70 constraints) and a retention period, including deleting a leaver after a set
  period; backup with a tested restore (VD 72, with hosting); Rigsrevisionen's requirements researched. Four-eyes on
  configuration and corrections is its own row under "Security & access control". Ids: EM A37, EM A38, EM §8.3,
  VD 35, VD 36, VD 42, VD 43, VD 72, VD 75.
```

**Not proposed.** No row is deleted. The leaver-notification row and the advance-vacation row stay, pointing at their epics. The open decisions VD 38 and EM-G8 change no row: EM-G8's comment is carried as the specification dependency in R-1 and R-12.

---

## 6. Questions for the owner (in priority order)

Only questions whose answer changes the roadmap. Each comes with a recommendation.

**Q1 — How should shift planning be done?**
Vejdirektoratet's shift workers need rosters, and the state's +25 % supplement depends on a roster. So a roster is not only a planning aid; it changes pay. The overtime program's stage 2 will design a record for "an order or a roster span". If shift planning is to be built on that record, the decision must come before stage 2's refinement. Otherwise the roster gets designed twice.
- **(a) Build it inside StatsTid, in stages, starting from stage 2's roster record (recommended).**
  - Gains: one record serves pay and planning, so there is never a second source of truth for "this shift was rostered". Rest warnings, Skema pre-fill and the pay rules all read it.
  - Gives up: this is the largest new module (XL). Automatic roster proposals come last.
- **(b) Use an external shift-planning product and import its approved rosters.**
  - Gains: a smaller build.
  - Gives up: another inbound integration, and planners working in a second system. VD 59 ("an integrated part of time registration") would be only partly met.
- **(c) Rosters only.** StatsTid records planned shifts and uses them for pay and rest warnings.
  - Gains: the smallest build.
  - Gives up: swaps, automatic proposals and Outlook (VD 49, 50, 55, 56, 61) stay unmet.

**Q2 — What does "extract" mean?**
You asked for general reporting and for data reachable through an API (EM A23, EM B31, VD 60). You marked Excel/PDF export, printing, unlimited extracts and the data-warehouse feed not relevant. The answer sets the size of the reporting epic.
- **(a) On-screen reports and statistics, plus documented dataset endpoints that tools such as SAS can pull with a service login (recommended).**
  - Gains: this matches every "should meet" and every "not relevant" mark.
- **(b) On-screen reports only.**
  - Gains: the smallest option.
  - Gives up: EM B31 (API extracts) stays unmet.
- **(c) A dataset API only.** Reporting happens in the institution's own tools.
  - Gains: the smallest build inside StatsTid.
  - Gives up: EM A23's "general reporting functionality" would be met only through other tools.

**Q3 — Which lane comes first among the work that needs no outside input?**
About 52 of the 128 requirements can be planned now. The overtime program (stages 1–3) stays the priority either way. The question is what to interleave with it, or put after it.
- **(a) The HR back office with requests and notifications (recommended).**
  - Gains: this is the biggest gap people would notice every day, and it unlocks the vacation manager approval and the leaver reminder.
- **(b) Statens SSO and the compliance software first.**
  - Gains: the platform preconditions for any go-live come early.
  - Gives up: those preconditions are mostly invisible to users, and SSO's provisioning half still waits for specifications.
- **(c) Shift planning right after stage 2** (only if Q1 = a).
  - Gains: it builds on stage 2's record while that record is fresh.
  - Gives up: it is the largest epic, and it delays the back office.

**Q4 — When the HR system and the user-administration system feed StatsTid, are they the only source for what they carry?**
Today HR edits employment facts and roles inside StatsTid, with dated history, a backdate worklist and audit. The answer decides whether those screens stay editors or become views.
- **(a) Yes.** Fed facts are read-only in StatsTid, and corrections are made at the source. StatsTid edits only what no feed carries (recommended).
  - Gains: one source of truth, no edits that contradict each other, a clean audit trail.
  - Gives up: the S141 termination screen and the effective-date picker become views for fed facts, and development needs a stand-in feed.
- **(b) The feed writes, and HR may override in StatsTid with a recorded reason.**
  - Gains: flexibility.
  - Gives up: two sources can disagree. Every feed run must then decide whether an override wins, and someone must reconcile.
- **(c) StatsTid stays the source. The feed only creates new people.**
  - Gains: the least rework.
  - Gives up: this departs from your comment that the lifecycle is handled through the HR integration.

**Q5 — Should the two deliberate design choices stay?**
- **The choices.**
  - A leader sees a month's figures only after the employee sends it. This was your ruling in S124, and S148 just enforced it again on the rest data.
  - A month already sent to payroll is never reopened; later changes go through corrections (ADR-034).
- **Why ask.** EM A1, EM A4, VD 9, EM A31 and VD 32 are "should meet", and their gap texts mention these choices.
- **(a) Keep both (recommended).** Meet the requirements through overviews of sent months, HR's own view of everything, and the corrections path.
  - Gains: no reversal, and this matches the state's guide on locking approved data.
- **(b) Revisit the S124 rule only.** Leaders would see registrations before they are sent.
  - Gives up: this needs a new ADR and reverses SEC-059's fix.
- **(c) Revisit both.**
  - Gives up: reopening after export also needs a payroll reversal model.

---

## Brief contradictions

Each entry gives the claim, then what the source shows.

1. **The claim.** The review record's direction line says "Shift planning is in scope; the open question is how (VD-G1, VD-G2)" (`TRA:540`).
   - **What the source shows.** VD-G2's requirement is "useful facilities for control and statistics, plus payroll-administrative tools", with no shift planning (`TRA:168`). Only VD-G1 names shift planning (`TRA:167`).
   - **What this draft does.** It places VD-G2 in E5 (reporting) and reads the owner's identical "how" comment on it (`TRA:573`) as being about reporting and control tools. The record's line may want a correction (Orchestrator, `docs/**`).
2. **The claim.** The brief lists "the 'not relevant' decisions that relieve a known gap (EM A16, EM A19, EM A33, EM A34)".
   - **EM A16 and EM A34** do relieve gaps.
   - **EM A19 does not.** Its comment, "We will use Statens SSO" (`TRA:708`), redirects the SSO need rather than removing it.
   - **EM A33 relieves only the deactivation trigger.** Its twin VD 33 has the same comment and is "should meet" (`TRA:581`), and GDPR deletion stays wanted through VD 75 (`TRA:758`).
   - § 3 C10 records each.
3. **The claim.** The brief places PLAN-s148's wave table at `:113-124`.
   - **What the source shows.** The wave table is `PLAN-s148.md:116-124`; `:113-115` are file-ownership rows. "What this plan adds" is `:39-54`. This is a trivial location difference; the content matches the brief.

No claim about required behaviour in the brief was found wrong.

## Needs a ruling (the Orchestrator's)

1. **The new § 2 theme.** R-12 adds a theme, "Integrations with the state's shared systems". The brief asked for rows "in its existing sections", and none of them fits integrations.
   - Alternative: one source-grouped section holding all twelve epics. There is precedent at `ROADMAP.md:432` and `:747`.
   - Lean: the new theme only.
2. **Arc item numbered 7.** R-1 is numbered 7 so the existing "§1 item 5" citations in `tender-requirements-analysis.md` and `ROADMAP.md` (`:204`, `:696`) stay valid.
   - Alternative: insert it as item 5 and renumber, then update those citations.
   - Lean: item 7.
3. **C7 should become a register row before S148's config stage closes.** One administrator's agreement publish changes pay once T14b lands (`PLAN-s148.md:33`). I found no SEC or QUAL row for it.
   - Lean: a SEC row, "revisit, not shield", with the fix scheduled in a later sprint. S148's scope is owner-ruled and should not grow.
4. **Stale texts found while reading** (none of them is tender scope):
   - The "Clearing a Skema cell" row (`ROADMAP.md:490-503`) is being built in S148 (TASK-14840/14841; QUAL-252 at `quality-finding-register.md:504` says so).
   - HR's §21 list still tells users "skærm følger i S141" (`FollowUpLists.tsx:672`). S141 did not build that screen; this is a candidate QUAL row.
   - The arc header still reads "where we are (S128)" (`ROADMAP.md:28`).

---

## Revision history

- **Draft 1 (2026-11-13, Planning Drafter, Opus 5.5, at `de3da64b`).** First draft from the owner review (`837ecb29`). Twelve epics; 128 ids placed and script-checked; ten conflicts; a loose sequence; fifteen roadmap edits; five owner questions. Code facts re-opened at `de3da64b`: `StatsTidRoles.cs`, `AuthEndpoints.cs`, `AdminEndpoints.cs`, `ReportingLineEndpoints.cs`, the Payroll and External `Program.cs`, `PayrollExportService.cs`, `FollowUpLists.tsx`, `init.sql`, `docker-compose.yml`. The registers were read after wave 3's merge (SEC-059, QUAL-253).
- **Draft 1, answered and adjudicated (2026-11-13, Planning Drafter, Opus 5.5).** The owner answered the five questions (`docs/sprints/SPRINT-148.md`, `277fb9cb`). The Fable adjudicator ruled RULED-WITH-QUESTIONS (`.claude/reviews/TENDER-roadmap-adjudication.md`). The owner then answered its one question: SEC-060 is built as the first item of the compliance epic (E11), after the HR back office (E3). Only the status line changed here (ruling 9). The body above is kept as drafted; where it differs, the adjudication and `ROADMAP.md` govern. The adjudication's Edits 1–13 were applied to `ROADMAP.md` at `277fb9cb`'s text. Edit 14 and the SEC-060 row are the Orchestrator's.
