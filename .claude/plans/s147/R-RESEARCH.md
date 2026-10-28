# R — the S147 research pass: brief for the first pass and for the independent verifier (Orchestrator-run, wave 1)

| | |
|---|---|
| **When** | Wave 1, in parallel with the build (plan § R) |
| **Who** | First pass: `general-purpose`, **`model: opus`** (the guard blocks a bare generic spawn), with web search. Verifier: a **separate** `general-purpose` spawn, **`model: opus`**, which receives only the first pass's claims and quotes, never its reasoning (S146 D4 method) |
| **Writes** | Only `.claude/refinements/RESEARCH-s147.md` (first pass) and `.claude/refinements/RESEARCH-s147-VERIFICATION.md` (verifier). Nothing else, anywhere |
| **Filing** | The Orchestrator files the verified dossier under `docs/references/` with its CLAUDE.md "Historical & research dossiers" row (O-9) |

*The Orchestrator pastes `docs/CONVENTIONS.md` verbatim with this brief.*

## Plain language — why

StatsTid encodes Danish state agreement rules. Five rules the sprint needs have no verified source yet. A wrong rule is the product failing at its one job, so each answer must rest on a quoted primary source (the agreement or circular text itself, a Medarbejder- og Kompetencestyrelsen or Moderniseringsstyrelsen publication, the union's or the employer's published agreement text, a court or arbitration award), with its date and its paragraph. Secondary summaries may point to a source; they never stand in for one. The verifier's job is to break each answer: find a contradicting text, a superseded version, or a quote that says less than the claim.

## The cells

| Cell | Question | What the answer must contain | Feeds |
|---|---|---|---|
| **R1** | In state flex (the flexitime framework under the state working-time agreements for AC, HK and PROSA), does a **paid sick day** count toward the norm (the hours one must work)? Is the credit the **hours registered** or the **day's norm** (e.g. 7.4 hours)? The same for a **child's sick day** (barns 1., 2., 3. sygedag) | The rule; the credit basis; the source paragraph quoted; whether it differs by agreement or OK version (OK24 vs OK26) | T3b (TASK-14708 or TASK-14717), or the negative branch |
| **R2** | **Re-verify** ADR-032 D3's claim that særlige feriedage may be taken in **fractions of days** ("brøkdele af dage", `ADR-032:37`) | CONFIRMED or CONTRADICTED, with the quote (the ferieaftale / cirkulære paragraph) | T2 (AC-235-7); a contradiction makes the fix-up TASK-14701-F |
| **R3** | Are **on-call standby** (rådighedsvagt), **call-in** (tilkald) and **non-working travel** working time under the state agreements; what is on-call's rate | Each item: working time or not, the rate, the paragraph quoted | D-3, option (a) |
| **R4** | Has the implementing text for the **4 February 2026 part-timer award** appeared (the award on part-time employees' overtime/merarbejde threshold, OK26 item 28 per S146's research)? | Yes/no; if yes, its title, date, paragraph and quote; if no, the latest dated statement found | D-10 (F-7), labelled provisional either way |
| **R5** | The **OK24 texts of § 13 and § 14** referred to in ADR-043 (`ADR-043:206-210`): the single +25 % supplement and § 14 weekend compensation | The paragraphs quoted in full, with the document's title, date and version | D-2, D-4; S148 |

## Verdicts

Each cell ends with exactly one of **CONFIRMED / CONTRADICTED / PARTLY / UNRESOLVED**, the quoted primary source (title, date, paragraph, link or document id), and, in the verification file, the verifier's **agree / dissent** with its reason. An UNRESOLVED cell says what was searched and why it did not settle (AC-R).

## Must not

Write anywhere but the two files; edit code or docs; present a secondary source as primary; round a partial quote into a full claim.
