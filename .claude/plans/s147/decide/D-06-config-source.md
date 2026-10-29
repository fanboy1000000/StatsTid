# D-6 — QUAL-233 (with QUAL-204): should what the admin editors write change what people are paid?

*Brief, draft 2, 2026-10-28: adjudicated (`.claude/reviews/S147-D-batch1-adjudication.md` § D-6). The owner has not been asked yet. Drafted by `planner` (Opus 5.5); facts verified at `e7478f24`, draft 2's added citations at `b2ece3a1` (no code changed between the two).*

**The question in one sentence.** A GlobalAdmin publishes a change to an agreement's rules in the editor, or an institution sets up its local agreement profile. Should payroll calculate with that change, as the product specification already requires? Or should those editors be declared not to affect pay?

**The question as it will be asked** (settled by the adjudicator, D-6.1; verbatim).

> *Should payroll calculate with the agreement rules a GlobalAdmin publishes in the editor and with each institution's local agreement profile, as the product specification already requires — a stage of its own in S148, with every configured value dated and frozen into the calculation's proof — or should those editors be declared not to affect pay, either for everything or for central agreements only?*

- **(a) Yes — the editors reach pay (recommended)**
- **(b) No — configs are code; the editors become read-only or carry "does not affect pay"**
- **(c) Split — local profiles reach pay, central agreement configs stay code**

All three options are offered. The adjudicator checked option (c), which came from refinement rev 1, and found it verified and rightly included (D-6.4).

**Why it matters.**
- **Edits never reach pay, and nothing says so.** Today the editors' changes reach some screens and booking but never a paid figure, and the editor shows no notice.
- **Screen and payslip can disagree** about the same employee. Skema's daily norm reads the database, while payroll reads a table compiled into the code.
- **A new agreement cannot be paid.** An agreement created in the editor is refused by the engine.
- **It gates three later questions.** Every later question whose answer is a configured value (office hours D-7, local flex parameters D-8, customary days off D-12) reaches pay only through the path this answer picks, so this question comes before them.

**What StatsTid does today** (verified).
- **The engine reads compiled tables.** The Rule Engine looks configs up in tables compiled into the code (`src/RuleEngine/StatsTid.RuleEngine.Api/Rules/RuleRegistry.cs:254`, `:393`, `:446-449` → `…/Config/AgreementConfigProvider.cs:12-29` → `src/SharedKernel/StatsTid.SharedKernel/Config/CentralAgreementConfigs.cs:13`, `:291-315`). It refuses an agreement it does not know (`RuleRegistry.cs:446-447`).
- **The Backend reads the database.** It resolves the database config (falling back to the compiled table), then a position override, then the institution's local profile (`src/Infrastructure/StatsTid.Infrastructure/ConfigResolutionService.cs:115-203`). Skema's daily norm uses this result (`src/Backend/StatsTid.Backend.Api/Services/DailyNormCalculator.cs:154-166`). The local profile it reads is the current one, not the one in force on a given date (`ConfigResolutionService.cs:185`).
- **Payroll registers that service but never calls it** (`src/Integrations/StatsTid.Integrations.Payroll/Program.cs:147`; no other reference under the Payroll project).
- **The replay contract assumes configs are code.** It says "fingerprint what is code" (`ADR-016:169`; `ConfigFingerprint.cs:13-17`). A replay whose configs no longer match their recorded fingerprints is refused by name, `replay-config-changed` (`src/Integrations/StatsTid.Integrations.Payroll/Services/ReplayRefusedException.cs:38`; raised at `PeriodCalculationService.cs:2775-2784`).
- **The one exception:** payroll reads wage-type mappings from the database at export, by date (`…/Payroll/Services/PayrollMappingService.cs:34-42`).
- **A published config has no start date.** `agreement_configs` has a status and a `published_at` time, but no date it applies from (`docker/postgres/init.sql:1328-1384`; QUAL-182). Position overrides have no effective date either (QUAL-182, `docs/operations/quality-finding-register.md:405`).
- **What the spec says:**
  - The database is the single source of truth for agreement configs, and the service layer passes them to the pure engine (`SYSTEM_TARGET.md:205`, `:207`).
  - Local configuration is merged at the service layer (`:177`) and takes effect at re-evaluation, not retroactively (`:178`).
  - ADR-014 (approved) says the same (`ADR-014:29-32`, `:41`).

**The options** (refinement rev 1's D-6 row, as quoted in `.claude/reviews/REFINEMENT-s147-codex-c1.log:348`; current refinement `:510`).
- **(a) Implement ADR-014 on the pay path.** For each part of the month, payroll resolves the config that was in force (central → position → local) and passes it to the engine. The engine stays pure: it receives values and never reads a database. The config is also frozen in the manifest, the record of what a calculation ran with.
  - *Gives up:* size. It is a stage of its own (`REFINEMENT…:518-524`). Its cost (D-6.2):
    - dated configs (QUAL-182), **including dated position overrides**;
    - the Rule Engine's HTTP contract changes: the configs arrive in the request, and the engine's purity is kept (ADR-002);
    - a change to the replay contract, under which `replay-config-changed` changes meaning or retires, because the configs become frozen inputs rather than fingerprinted code.
  - *Gains:* one path for every configured value, and the editors do what they say.
- **(b) "Configs are code."** Amend ADR-014, and make the editors read-only or label them "does not affect pay".
  - *Gives up:* a requirement, §I's self-service without a redeploy, which is the owner's call (BC-4).
  - Local agreements could not vary pay either, so D-7, D-8 and D-12 would have nowhere to put their values.
- **(c) A split:** central configs stay code, while local parameters are data that reaches pay.
  - *Gives up:* §I for central configs (BC-4).
  - It is smaller than (a), but it leaves **two defects standing** (D-6.2):
    - an agreement a GlobalAdmin creates still cannot be paid, because the engine refuses any agreement it has no compiled config for (`RuleRegistry.cs:446-447`);
    - screen and payslip still disagree after a central edit, because there are still two paths for configured values.

**The lean: (a), confirmed by the adjudicator** (D-6.3; refinement `:510`; plan `:285`). The adjudicator's reasons:
- The specification and an approved ADR already say it.
- D-8's local parameters need this path ("resolve at the service layer, freeze in the manifest") anyway.
- Freezing the config into the calculation's proof honours auditability better than fingerprinting code: the record holds the values the calculation actually used.
- The brief's further points stand: (b) and (c) are requirement changes, not design choices; stage 1 can run on the central defaults, so (a) does not delay it; the research changes nothing here.

**What the owner should know** (D-6.5).
- **The editor gains an "applies from" date.** A published change takes effect from a date, so pay can tell which version was in force on any day.
- **A publish never changes an exported month.** A month already sent to payroll keeps the figures it was sent with; exported months are changed only by an explicit correction, never by a silent recalculation (ADR-013, ADR-034, as ADR-043 D5 already relies on).
- **Until the config stage lands, the editors silently do nothing for pay.** A stage-0 label is recorded for the S148 build input (a proposal, not a question): the editors say "affects screens and booking; does not yet affect pay".
- **QUAL-204's open product question is answered by this ruling.** QUAL-204 asked whether a local profile should change pay; whatever the owner picks here answers it.

**What the S148 build gets from each answer.**
- **(a):** a stage of its own, placed before the first stage that needs a local value (`REFINEMENT…:518-524`), preceded by stage 0's label:
  - dated configs and dated position overrides (QUAL-182);
  - payroll resolving each part of the month and passing the configs to the engine in the request (the engine's HTTP contract changes; it stays pure);
  - configs frozen in the manifest, replacing "fingerprint what is code"; `replay-config-changed` changes meaning or retires;
  - editor publishes taking effect from a date;
  - one path for every configured value.
- **(b):** ADR-014 amended; SYSTEM_TARGET §I text owed to the owner; the editors made read-only or labelled; D-7, D-8 and D-12 re-asked as code-only.
- **(c):** local profiles resolved by date and frozen; central configs stay fingerprinted code; SYSTEM_TARGET §I text owed to the owner; the two defects above remain open.

**The ADR-043 text it rewrites.**
- In ADR-043: F-5 (a)'s sentence on where the profile is resolved (`:290-292`), and F-4 (b)'s "local parameter" (`:282-283`).
- Outside ADR-043: ADR-014, confirmed with an implementation note under (a) or amended under (b)/(c). Under (a), also ADR-016 D10's "fingerprint what is code" (`ADR-016:169`) and its fingerprint contract (`:181`).
- QUAL-204's open product question ("should a local profile change pay?") is answered by the same ruling.
- The stage-0 label is listed in `../ADR-043-working.md`, "Provisional items and recorded proposals".

**Waits on.** Nothing.

---
*Revision history.*
- Draft 1 (2026-10-28): first brief.
- Draft 2 (2026-10-28): the adjudication applied (D-6.1–.5). Added "The question as it will be asked" (option (c) confirmed as rightly included, D-6.4); folded D-6.2 into options (a) and (c) and the facts (the engine's HTTP contract, dated position overrides, `replay-config-changed`; (c)'s two standing defects); the lean confirmed with the adjudicator's reasons; "What the owner should know" added, with the stage-0 label recorded for the build input.
