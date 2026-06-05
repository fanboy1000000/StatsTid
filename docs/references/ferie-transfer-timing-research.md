# Ferie Transfer Timing — Deep Research Verdict (S65 OQ-1)

**Date:** 2026-06-06 · **Method:** adversarially-verified deep research (5 search angles, 16 sources fetched, 50 claims extracted, 25 verified by 3-vote adversarial panels → 23 confirmed / 2 killed, 98 agents)
**Question:** Under the Ferielov (2020 samtidighedsferie model), when does the right/decision to transfer untaken vacation legally crystallize — 31 Aug (ferieår end) or 31 Dec (ferieafholdelsesperiode end)? Plus the state-sector særlige feriedage timeline.
**Consumed by:** SPRINT-65 OQ-1 resolution (`boundaryMonth = 12`), ADR-030 transferable-projection annotation (TASK-6505), ROADMAP særlige-feriedage follow-up.

## Verdict

**The legal transfer point is 31 December, not 31 August.** The owner ratified December placement for the Årsoversigt "Kan overføres" figure (2026-06-06), with the recorded caveat that the system's displayed quantity is its Sep-rollover model projection — an accrual-side approximation of the legal transfer event, not the §21 residual-5th-week quantity (which requires the deferred afholdelsesperiode/settlement modeling).

## Confirmed findings (all high-confidence, 3-0 adversarial votes unless noted)

### 1. Transfer crystallizes at 31 December
Ferielov §21 stk.2 verbatim: *"Lønmodtageren og arbejdsgiveren skal skriftligt indgå aftale efter stk. 1 senest den 31. december i ferieafholdelsesperioden."* The employer executes transfers in the 1 Nov–31 Dec window (borger.dk). 31 Aug is the accrual-year end only — definitively NOT the transfer deadline.
**Caveat (verifier-carried):** the statute fixes an *agreement* deadline; the total accrued amount finalizes at 31 Aug, but the residual *transferable* quantity (unused 5th week) can only crystallize near the 31 Dec taking-period end — December is the legally operative transfer event.
Sources: danskelove.dk/ferieloven (§21 stk.2); borger.dk (Overfoersel-af-ferie; loenmodtager-og-ferie); mklaw.dk; danskerhverv.dk; dm.dk; 3f.dk.

### 2. Statutory structure
Ferielov §4: ferieår = 1 Sep–31 Aug (accrual, 2.08 days/month, 25/year). §6 stk.1: ferieafholdelsesperiode = the ferieår + 4 trailing months → 1 Sep–31 Dec of the following calendar year (16 months). In force 2026.
Sources: danskelove.dk/ferieloven (§4, §6 stk.1); borger.dk; mklaw.dk; dm.dk; corroborated by DI, IDA, Azets, PROSA.

### 3. Only the 5th week is voluntarily transferable
§21 stk.1: only ferie beyond 4 weeks may be transferred by agreement (written, employer approval required). Absent an agreement by 31 Dec, the untaken 5th week is auto-paid (by 31 March). The first 4 weeks lapse/are paid out under separate rules.
**Qualification:** §24 feriehindring (sickness, maternity/parental leave) is a distinct pathway — up to 4 weeks transfer automatically without employer approval (FerieKonto notified by 31 Jan). Out of scope for the voluntary-transfer UI figure.
Sources: danskelove.dk/ferieloven (§21 stk.1); borger.dk; danskerhverv.dk.

### 4. State sector defers to the Ferielov for ordinary ferie
PAV verbatim: *"Ferieaftalen er et supplement til ferieloven. Aftalen erstatter således ikke ferieloven…"* Cirkulære 021-24 §3: *"Månedslønnede følger ferieloven med de afvigelser, der er anført i §§ 4-11."* The same 31 Dec anchor applies to state ferie. (Note: the cirkulære's own §15 citation is imprecise — the more-than-25-days-lapses-on-termination rule lives in §19; reproduced faithfully, not a claim error.)
Sources: pav.medst.dk/pav/kapitel-23-ferie; cirkulaere.medst.dk/media/1372/021-24.pdf (§3, §4, §7); danskelove.dk (§26 stk.3).

### 5. Særlige feriedage live on a wholly different timeline (model-vs-law gap)
NOT regulated by the Ferielov. Accrued by **calendar year** (1 Jan–31 Dec, 0.42 days/month ≈ 5/year); taken **1 May (year after accrual)–30 April**; untaken days without a written carryover agreement are paid as a 2½% godtgørelse at period end (30 April). Cirkulære 021-24 §12 stk.2 verbatim: *"Særlige feriedage afvikles i det år, der går fra 1. maj til 30. april (afholdelsesperioden for særlige feriedage), og som følger efter optjeningsåret (kalenderåret)."*
**System impact:** the modeled ResetMonth-9/carryover-0 SPECIAL_HOLIDAY (danish-agreements.md:110) is a deliberate simplification — recorded as a ROADMAP follow-up (owner-ruled 2026-06-06); invisible in S65 (carryover 0 → "–").
Sources: cirkulaere.medst.dk 021-24 (§12 stk.2); statens-adm.dk (særlige feriedage lønsupport, worked example: accrual 2022 → taken 1 May 2023–30 Apr 2024); dm.dk; pav.medst.dk.

## Killed claims (2/25)
- "Transferred vacation may be used until 31 December of the following year" — refuted 1-2 (over-generalized; the usage boundary of transferred days follows the next afholdelsesperiode).
- A source-accessibility meta-claim about retsinformation.dk (not substantive).

## Source-quality caveats
retsinformation.dk (eli/lta/2019/1025, /2021/230) was inaccessible (Cloudflare/JS-SPA); statutory verbatims verified via danskelove.dk (faithful mirror) cross-checked against borger.dk, Dansk Erhverv, DI, 3F, DM.dk. The 021-24 cirkulære PDF was partially machine-unreadable; §12 stk.2 verified via pdftotext extraction with statens-adm.dk as authoritative corroboration. §26 governs payout on termination/death — the controlling transfer provision is §21 (several secondary sources loosely cite §26).

## Open questions deliberately NOT taken up in S65 (deferred to §21/§26 settlement scope)
1. Surfacing the auto-payout consequence (untaken/un-transferred 5th week paid by 31 March) in the year overview.
2. A law-faithful December "transferable" figure (residual 5th week within the afholdelsesperiode) — requires afholdelsesperiode modeling.
3. The §24 feriehindring automatic-transfer pathway.
4. Per-category boundary markers for a future law-faithful særlige-feriedage timeline (30 Apr/1 May falls in neither Aug nor Dec).
