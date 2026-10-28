# Sick-day credit and five agreement cells — verified research (S147 research pass, R1–R5)

**Date:** 2026-10-28. **Method:** a research pass (`general-purpose`, Opus 5.5, web search) on five cells the S147 build and rulings round needed, then an independent adversarial verification that re-downloaded and re-read every decisive source (Opus 5.5), then a Fable adjudication of the consequences (`.claude/reviews/RESEARCH-s147-adjudication.md`, RR-1..RR-13). **Status: verified cell by cell.** Where the verification corrects the first pass, **the verification governs** (Part 1); the first pass is kept verbatim in Part 2 as the record of what was claimed.

## The cells and the verified verdicts

| Cell | Question | Verified verdict | What it means for StatsTid |
|---|---|---|---|
| **R1** | Does a paid sick day (and a child's sick day) count toward the norm in state flex, and with what amount? | **PARTLY.** HK and PROSA (Statens arbejdstidsaftale § 7 nr. 2 and its remark; 022-26 and 014-21 are identical): it counts as if worked, credited with the hours **planned** for that day, else 7.4 h × the part-time fraction; never the hours worked. AC-agreement staff: **no central text** (PAV 19.3.4: AC working time "ikke nærmere fastlagt"), so the local flex agreement sets it. A partly-worked sick day = max(worked, planned). 022-26 § 3 lets a local agreement depart from the credit rule | QUAL-231's fix (TASK-14708) credits `SICK_DAY` for every agreement; for AC it is a **labelled default** pending the owner's confirmation (Q-1, at D-8). "Planned hours" = the per-day norm (`DayNorm`); the sick-day credit basis becomes a tenth local flex parameter (D-8) |
| **R2** | May særlige feriedage be taken in fractions of days? | **CONFIRMED.** Aftale om ferie § 13 stk. 2 (Medst. 021-24 / CIR1H 9298): "Afvikling kan ske som enkeltdage eller som brøkdele af dage". Nuance: days the *employer* imposes after 1 January are whole days only (Ferievejledning Dec 2019) | QUAL-235's fix (TASK-14701) keeps `full_day_only = false` |
| **R3** | Are on-call, call-in and travel working time, and at what rate? | **CONFIRMED.** HK/PROSA (§ 7 nr. 3–5, identical OK24/OK26): travel on an official trip counts in full, ≤ 13 h per working døgn; on-call counts **1/3** at home and **3/4** at the workplace; a call-in at < 24 h notice counts **≥ 3 h**. AC-agreement staff: the travel rule only (§ 13 stk. 6); no on-call or call-in rule | QUAL-232's model (D-3): a fraction of hours per (type × agreement), not `OnCallDutyRule`'s rate |
| **R4** | Has the implementing text for the 4 Feb 2026 part-timer award appeared? | **CONFIRMED that it has not.** MedSt's circular list through 068-26 (27 Oct 2026) has none; 022-26 § 8 stk. 2 still prints the 1:1 rule | F-7 (D-10) stays a provisional owner default pending a qualified expert |
| **R5** | The OK24 texts of § 13 and § 14 | **CONFIRMED.** 014-21 (in force 1 Apr 2021 – 31 Mar 2026) §§ 13–14 are word-for-word identical to OK26's 022-26; no amending circular between | One supplement-window set and one § 14 rule, no OK-version split |

**Found in passing, verified:** StatsTid's child's-sick-day quotas (AC 1 / HK 2 / PROSA 3, `danish-agreements.md:148-150`) are wrong. OK24 gives 2 days for all three; from 1 April 2026 (OK26) HK and PROSA get 3 days plus the remaining hours of the day the child falls ill, and AC-agreement staff settled the same, with the circular pending. The per-episode limit is enforced per year in code. Registered as **QUAL-236** (owner Q-2: fixed complete in S148).

**Corrections the verification and the adjudication made to the first pass** (each marked where it applies in Part 2):
1. R1 is PARTLY, not CONFIRMED: the AC basis cited (PAV 19.3.1.6) is a general "kan forekomme" passage under overtime, not an AC rule; 022-26 § 3 allows local deviation.
2. The partial-day arithmetic is max(worked, planned), not "never more than the norm".
3. HK's agreement in force is 087-25 § 38 (085-25 is *Historisk*).
4. AC civil servants already have OK26's child's-sick-day rule through 027-26 (StatsTid models no civil servants).
5. 020-26's remark differs from 021-26 by one sentence.
6. FV2025-795's Annex A names "Statens arbejdstidsaftale § 8, stk. 2", not 022-26 (which postdates the award).
7. The OAO FAQ is dated 24 Feb 2026.
8. MedSt's list pages 1–10 hold 100 entries; read through page 32.
9. FV2026.573's five-year limitation is general law (a private-sector award relied on by public-sector unions).
10. R2's employer-imposed whole-day nuance, and the seniordage context (Medst. 035-24).
11. R3: AC civil servants fall under § 7 nr. 3–5; the EU rest-check caveat (whether home on-call counts as rest is a separate classification).
12. PAV lags OK26 (it still cites the 2021 circular); 024-26 repealed "opsparing af frihed" from 1 April 2026; the PAV footer date is unreliable.
13. The first pass's word counts are tokenisation artefacts; the identity of the texts reproduces.

---

# Part 1 — The independent verification (governs)


| | |
|---|---|
| **Date** | 2026-10-28 |
| **Pass** | Verifier, S146 D4 method. Input taken from the first pass (`.claude/refinements/RESEARCH-s147.md`): only its claims, verdicts and cited sources. Its reasoning was not relied on |
| **Brief** | `.claude/plans/s147/R-RESEARCH.md` |
| **Method** | Every decisive text was downloaded again on 28 Oct 2026 and read as extracted text (`pdftotext`), not through a summariser. Where the first pass says two versions are "word for word the same", I compared them myself: the text was normalised (line-end hyphenation joined, page numbers and running headers removed) and diffed token by token. MedSt's chronological circular list was read on **pages 1–32** (from 068-26 of 27.10.2026 back to 038-21 of 05.11.2021). MedSt's news archive was read on pages 1–9 (back to 22.08.2025). Arbitration award FV2025-795 was re-read, not inherited. Web searches looked for contradicting texts. Each claim got a deliberate attempt to break it: a newer version, a superseded citation, a quote that says less than the claim |
| **Verdict words** | Per cell: **agree / partly / dissent** with the first pass, then the corrected verdict where one is needed (CONFIRMED / CONTRADICTED / PARTLY / UNRESOLVED) |

---

## Net verdict for the product owner

**In one sentence.** The first pass holds up well. All five cells rest on the right primary texts, and every quote I re-read says what the first pass says it does. **R2–R5 hold as written.** **R1 holds for HK and PROSA, but two of its claims are stronger than their sources:** the AC (academics') part, and the arithmetic for a day where someone works and then goes home sick. Several citations also need small fixes.

**What holds, in plain terms.**
- **Sick days under flex (R1), HK and PROSA.** A paid sick day counts as if worked. It is credited with the hours the person was *planned* to work that day. If no hours are fixed for the day, the credit is 7.4 h for a full-timer, pro rata for a part-timer. It is never credited with the hours someone typed in. A paid child's sick day is credited the same way. This text is identical in the OK24 and OK26 versions. (OK24 and OK26 are the agreement rounds in force from April 2024 and from April 2026.)
- **Special holiday days in part-days (R2).** The holiday agreement in force says these days may be taken *"som enkeltdage eller som brøkdele af dage"* (as single days or as fractions of days). ADR-032 is right.
- **On-call, call-in and travel (R3), HK and PROSA.**
  - On-call at home counts as 1/3 of its hours; on-call at the workplace counts as 3/4.
  - A call-in at less than 24 hours' notice counts as at least 3 hours.
  - Travel on an official trip counts in full, up to 13 hours a day.
  - AC staff on the AC agreement get only the travel rule.
- **The part-timer overtime award (R4).** No implementing text has appeared. The newest state circular is 068-26 of 27 Oct 2026, and none of the circulars since the award concerns it. D-10 stays provisional.
- **Evening, weekend and holiday supplements (R5), § 13 and § 14.** The text in force for the whole OK24 period is word-for-word the same as OK26's. So StatsTid can encode them once for both.

**What I corrected, and why it matters.**
1. **AC's sick-day credit has no central rule (R1).** The first pass cites a passage in MedSt's guide (PAV, the employer agency's official reading of the rules) as the AC basis. That passage is a **general description of rules that "can occur"** in agreements that do not follow the state working-time agreement. It is not specific to AC, and it sits under "overtime", which AC staff do not have. The AC agreement itself says working time is "not further defined". **For AC staff on the AC agreement, the local flex agreement sets the credit.** The 7.4 h rule is a sensible default by analogy, but it is a default the owner chooses, not a sourced rule. *AC civil servants* are different: they are covered by the state working-time agreement directly, so the HK/PROSA rule applies to them.
2. **Partial sick day: drop "never more" (R1).** The first pass's reading says a day where someone works, then goes home sick, "totals the day's norm, never more". That is wrong when the hours already worked exceed the day's planned hours. Under the agreement, worked hours always count in full. The defensible reading is: worked hours, plus the *remaining* planned hours if there are any. The day's credit is then the larger of the hours worked and the hours planned. This case stays unsettled by any central text, and a sick-day credit is in any case a rule a local agreement may change (§ 3).
3. **The HK citation was a superseded circular (R1-2).** Circular 085-25 is marked *Historisk* (superseded) on MedSt's site. It was replaced two days later by **087-25 of 14 May 2025**. Its § 38, the provision that puts HK staff under the state working-time agreement, says the same thing. The substance is unchanged, but the citation must be 087-25.
4. **AC civil servants already have the OK26 child's-sick-day rule (R1-8).** The first pass says AC's OK26 child's-sick-day text "is not yet issued". That is true for AC staff *on the AC agreement*. But circular 027-26 already gives **all state civil servants** days 1–3 and the call-home day from 1 April 2026. That includes AC civil servants.
5. **Smaller fixes.**
   - The award's annex names *"Statens arbejdstidsaftale … § 8, stk. 2"*, not "022-26 § 8 stk. 2". Circular 022-26 did not yet exist on 4 Feb 2026.
   - The OAO FAQ is dated 24 Feb 2026, not April 2026.
   - The circular list pages 1–10 hold 100 entries, not 97. This does not affect any verdict.

**What this means for StatsTid.**
- **Sick and child's-sick days under flex.** Credit = the day's planned hours, falling back to 7.4 h × the person's part-time fraction. This is the **central default**. A local flex agreement may override it, for example with office hours set per weekday. Make it a per-local-agreement parameter, with the default switched on.
- **AC staff on the AC agreement.** Apply the same default, but **label it an owner-ruled default**, not a sourced rule.
- **Child's sick days are counted per illness, in calendar days** (*per episode*: each new illness starts a new count).
  - Day 1 is the first *whole* sick day. Days 2 and 3 follow as calendar days, whether or not they are working days.
  - There is **no annual quota** in any text, and the leave is at the employer's discretion.
  - **StatsTid's encoded values are wrong.** It encodes AC 1, HK 2, PROSA 3 (`danish-agreements.md:148-150`). The sources give:
    - **OK24:** 2 days for all three agreements.
    - **OK26, from 1 April 2026:** 3 days plus the remaining hours of the call-home day for HK, PROSA and all civil servants.
    - **AC staff on the AC agreement under OK26:** 3 days plus the call-home day was agreed, in force from 1 April 2026, but the circular is still pending. Treat it as provisional.
- **Special holiday days.** Fractions of days stay legal. A refinement: when the *employer* imposes untaken days after 1 January, the joint guide says it may impose only **whole** days.
- **On-call and call-in fractions (1/3, 3/4, minimum 3 h).** These apply to HK, PROSA and AC civil servants, not to AC staff on the AC agreement.
- **The part-timer award (D-10).** Keep it provisional.
- **Supplements under § 13 and § 14.** Encode them once for OK24 and OK26.

### The verdicts at a glance

| Cell | First pass | Verifier | Corrected verdict | Decisive source (re-read 28 Oct 2026) |
|---|---|---|---|---|
| **R1** sick and child's sick days in flex | CONFIRMED | **partly** | **PARTLY**: CONFIRMED for HK, PROSA and civil servants (incl. AC civil servants); for AC staff on the AC agreement the credit amount is **not set by any central text** (local flex agreement; 7.4 h is a default by analogy). The partial-day arithmetic is corrected (no "never more" cap) | Statens arbejdstidsaftale § 7 nr. 2 + remark, identical in Medst. 014-21 / CIR1H 9551 / Medst. 022-26; § 3 (local deviation); PAV 19.3.4 ("Arbejdstid som begreb er ikke nærmere fastlagt") |
| **R2** særlige feriedage in fractions | CONFIRMED | **agree** | **CONFIRMED** (+ nuance: employer-imposed days are whole days only, per the guide) | Aftale om ferie, Medst. 021-24 / CIR1H 9298 (*Gældende*), § 13 stk. 2; Ferievejledning Dec 2019 § 3.7.1–3.7.2 |
| **R3** on-call, call-in, travel | CONFIRMED | **agree** | **CONFIRMED** (+ AC civil servants are under § 7 too) | 022-26 = 014-21 § 7 nr. 3–5 + remarks; PAV 19.2.6, 19.2.8.1, 19.2.9.1; CIR1H 9333 § 13 stk. 6 |
| **R4** part-timer award implemented? | CONFIRMED (not appeared) | **agree** | **CONFIRMED**: not appeared as of 28 Oct 2026 | MedSt circular list p. 1 (068-26, 27.10.2026) to p. 32; 022-26 § 8 stk. 2; CFU item 28 / AC item 16 |
| **R5** OK24 texts of §§ 13–14 | CONFIRMED | **agree** | **CONFIRMED**: identical to 022-26, zero differing tokens | Medst. 014-21 (CIR1H 9551, *Historisk*, superseded only by CIR1H 9594 = 022-26), §§ 13–14 + remarks |

---

## R1 — Sick days and child's sick days in state flex

**Verifier: PARTLY.** The first pass is right for HK and PROSA, and for civil servants. It is over-sourced for AC staff on the AC agreement. Its reading of the partial-day case is slightly wrong.

### R1-1 (the § 7 nr. 2 rule): **agree**
> *"§ 7. Den præsterede arbejdstid opgøres således: … 2. Dage med ret til fravær med løn medregnes med det antal timer, den ansatte skulle have arbejdet den pågældende dag. Hvis der ikke er fastsat noget timetal for den pågældende dag, medregnes for fuldtidsansatte 7,4 timer og for ansatte på deltid eller plustid et forholdsmæssigt timetal."*
> Remark: *"Fravær med ret til løn er fx sygedage, afspadsering, feriedage og tjenestefrihed med løn."*
> → Paid-absence days count with the hours the employee *should have worked* that day; if no hours are fixed for the day, 7.4 h (full-time) or a proportional number (part-time/plustid). Paid absence includes sick days, time off in lieu, holiday days and paid leave.

*Cirkulære om Statens arbejdstidsaftale*, Cirkulære af 18. juni 2026, Medst.nr. 022-26 (J.nr. 2026-494), § 7 nr. 2 and remark, https://cirkulaere.medst.dk/media/jhshf3w2/022-26.pdf.

**My identity check.** I took § 7 through to "§ 8. Hvis den præsterede", with all its remarks, from three texts:
- 022-26;
- Medst. 014-21 (https://cirkulaere.medst.dk/media/1191/014-21.pdf);
- CIR1H 9551 (https://www.retsinformation.dk/api/pdf/224517).

I normalised each and diffed them token by token. Result: 285 tokens each, **zero differences**. (The first pass's count of 281 words used a different tokenisation. The decisive fact, identity, reproduces.)

### R1-2 (HK and PROSA are under the state working-time agreement): **partly** (citation correction)
- **HK.** Circular 085-25 is marked *Historisk* on MedSt's site (landing page: "Status: Historisk").
  - It was replaced by *Cirkulære om organisationsaftale for kontorfunktionærer, laboranter og It-medarbejdere (HK)*, Cirkulære af 14. maj 2025, **Medst.nr. 087-25**. The landing page reads: "Status: Gældende … Ophæver: 085-25".
  - Its commencement clause: *"Cirkulæret har virkning fra 1. april 2024. Samtidig ophæves Finansministeriets cirkulære af 12. maj 2025 om organisationsaftale for kontorfunktionærer, laboranter og It-medarbejdere (HK) (Medst. nr. 085-25)."* (In effect from 1 April 2024; repeals 085-25.)
  - Its § 38 is the same as 085-25's: *"Ansatte under denne aftale er omfattet af statens arbejdstidsaftale, med de i stk. 2-3 nævnte tilføjelser."* (Staff under this agreement are covered by the state working-time agreement, with the additions in stk. 2–3.) Stk. 2–4 cover lunch breaks and the 1/1924 hourly rate. There is nothing on absence.
  - PDF: https://cirkulaere.medst.dk/media/aprizyyh/087-25.pdf. PAV 19.3.3 also cites "Fmst. cirk. 14/05 2025" as the HK text.
- **PROSA.** Agree. 019-26 (17 Jun 2026) § 12: *"De ansatte er omfattet af den til enhver tid gældende statslige arbejdstidsaftale med de i stk. 2 nævnte tilføjelser."* (Covered by the state working-time agreement in force at any time, with the additions in stk. 2.)
  - Stk. 2 sends pay groups 3–4 to *merarbejde* (extra-work pay) instead of overtime. It does not touch absence.
  - The OK24 text 079-25 (1 May 2025, https://cirkulaere.medst.dk/media/tnjl2rxn/079-25.pdf) has the identical § 12, which I re-read rather than inheriting it.
- **Why PROSA needs that express clause.** 022-26's general remarks say: *"I henhold til fællesoverenskomsten med LC og CO10 … gælder Statens arbejdstidsaftale derimod kun, hvis det udtrykkeligt fremgår af den enkelte organisationsaftale"* (under the LC/CO10 joint agreement the working-time agreement applies only where the organisation agreement says so expressly). PROSA's § 12 says so. HK, under OAO-S, is covered by default.

### R1-3, R1-4 (PAV 19.2.6, 19.5.9, 19.5.13): **agree**
I re-read all three in PAV kap. 19 (https://pav.medst.dk/pav/kapitel-19-arbejdstidsregler-mv/):
- **PAV 19.2.6:** *"Dagene medregnes med det planlagte antal arbejdstimer."* (The days count with the *planned* working hours.)
- **PAV 19.5.9** lists *"Sygedage"* among the absences that must be reported separately into the flex account.
- **PAV 19.5.13:** *"Kontortiden kan bruges som grundlag for at fastsætte den arbejdstid, der skal medregnes for ferie- og sygedage."* (Office hours *can* be used as the basis for the hours counted on holiday and sick days.) Note the word *kan* (can): it is an option for the local agreement, not a rule.

**Addition the first pass understates: the whole rule is a central *default*.**
> 022-26 § 3: *"Aftalens bestemmelser kan fraviges eller suppleres ved lokal aftale inden for de rammer, der er fastsat i arbejdsmiljølovgivningen og aftalen om visse aspekter …"*
> → The agreement's provisions may be departed from or supplemented by local agreement, within the working-environment law and the EU-directive agreement.

PAV 19.5.1 says the same about flex: *"En flekstidsaftale fraviger de centralt aftalte arbejdstidsregler på en række punkter … skal parterne derfor sørge for, at aftalen regulerer alle forhold"* (a flex agreement departs from the central rules on several points, so the parties must make it regulate everything).

**Consequence:** the sick-day credit is a per-local-agreement parameter, defaulting to § 7 nr. 2. P-7 (weekday office hours) is one instance of this, not the only one.

### R1-5 (a credit to performed hours, not a cut to the norm): **agree**
- § 7 opens *"Den præsterede arbejdstid opgøres således"* (performed working time is computed as follows).
- The remark to § 14, which I read in 014-21 and 022-26, says weekend time off in lieu *"medregnes på afviklingstidspunktet i arbejdstidsopgørelsen som anden afspadsering, jf. § 7, nr. 2"* (is counted when taken, like other time off in lieu, under § 7 nr. 2).

The consequence ("a whole sick day leaves the flex balance unchanged") holds when the day's credit equals the day's share of the norm, which is the 7.4 h fallback case. Where a local agreement credits weekday office hours (P-7), the balance moves by the difference.

### R1-6 (AC: no agreement text; the guide fills the gap): **partly**. The basis is weaker than stated.
- **The agreement text: agree.** CIR1H 9333 (*Cirkulære om overenskomst for akademikere i staten*, CIR1H nr 9333 af 11/04/2025, status *Gældende*, printed 28 Oct 2026, https://www.retsinformation.dk/eli/retsinfo/2025/9333/pdf):
  - § 13 stk. 1: *"Lønnen efter denne overenskomst forudsætter fuldtidsbeskæftigelse svarende til gennemsnitlig 37 timer ugentlig."* (Pay assumes full-time work of 37 hours a week on average.)
  - § 13 stk. 5: *"Der kan lokalt indgås aftale om indførelse af flekstidsordninger."* (Flex schemes may be agreed locally.)
  - The remark to stk. 5 lets the norm be departed from within agreed limits, with hours carried forward.
  - § 17: full pay during sickness.
  - Nothing in the agreement credits absence hours.
- **The guidance basis: dissent on weight.** The first pass's AC source is PAV 19.3.1.6. On re-reading:
  - **Its context is general, not AC.** It sits in 19.3.1 *Generelt*, whose introduction reads: *"Nedenfor omtales nogle arbejdstidsregler, som **kan forekomme** på den del af overenskomstområdet, der ikke følger statens arbejdstidsregler."* (Below are described some working-time rules that **can occur** in the part of the agreement area that does not follow the state rules.)
  - **It sits under overtime.** The sentence is placed under the heading *19.3.1.6 Overarbejde*.
  - **AC has no overtime.** PAV 19.3.4.5: *"Akademikeroverenskomsten indeholder ikke regler om overtidsbetaling."* (The AC agreement has no overtime rules.)
  - **AC working time is undefined.** The AC-specific section, PAV 19.3.4, says: *"Arbejdstid som begreb er ikke nærmere fastlagt i overenskomsten. Der er dermed generelt fleksible muligheder for at tilrettelægge arbejdstiden for akademikere."* (Working time as a concept is not defined in the agreement; generally flexible arrangements are possible for academics.)
  - **19.3.4 is silent on the credit.** It says nothing on crediting sick days.
  - **So 19.3.1.6 does not settle it for AC.** It is evidence of general practice, not a statement of the AC rule.
- **What the sources do settle for AC staff in a flex scheme.**
  - **Sick days must be counted.** PAV 19.5 applies to flex for *"overenskomstansatte inden for centralorganisationernes forhandlingsområde"* (contract staff within the central organisations' bargaining area), which includes Akademikerne. Under PAV 19.5.9, sick days must be reported into the flex account.
  - **The amount is not settled.** How many hours a sick day is credited with is left to the local flex agreement (19.5.13 *kan*). No central text fixes it.
- **Corrected sub-verdict for AC staff on the AC agreement: UNRESOLVED by central text.**
  - The local flex agreement governs.
  - The 7.4 h × fraction default is reasonable by analogy with § 7 nr. 2 and is consistent with PAV 19.3.1.6's general description.
  - StatsTid should hold it as an **owner-ruled default**, labelled as such.
- **AC civil servants: agree.** They come under the state working-time agreement directly: 022-26 § 1, *"Aftalen gælder for tjenestemænd i staten"* (the agreement applies to civil servants in the state). Two corroborations:
  - FV2025-795, Annex A, describes the agreement as *"Statens arbejdstidsaftale indgået mellem Skatteministeriet og CO10 …, Lærernes Centralorganisation, **Akademikerne** og Offentligt Ansattes Organisation"*. Akademikerne is a signatory.
  - § 1 stk. 2 excludes only teachers (*"Aftalen gælder ikke for undervisere"*).
- I did not search for a separate AC civil-servant agreement that might modify § 7. None was cited, and none surfaced.

### R1-7 (a child's sick day is paid absence, so it is credited the same way): **agree**
The four sources say the same:
- **029-24** § 14 stk. 2 (OAO-S, 5 Jul 2024, https://cirkulaere.medst.dk/media/1380/029-24.pdf): *"Den ansatte får under tjenestefriheden en løn, der svarer til den løn, som den ansatte ville have fået under sygdom."* (During the leave, pay equals sick pay.)
- **021-26 and 020-26 § 14 stk. 2:** identical.
- **CIR1H 9333 § 18 stk. 6:** identical in substance.
- **The § 7 nr. 2 remark** lists *"tjenestefrihed med løn"* (paid leave) as paid absence.

The leave is granted as *"hel eller delvis tjenestefrihed"* (full or partial leave). A partial child's sick day is therefore credited with the hours of leave actually granted.

### R1-8 (the entitlement by agreement and OK version): **agree**, with two refinements

All rows were re-read in the primary texts:

| | OK24 | OK26 (from 1 Apr 2026) |
|---|---|---|
| **HK** (OAO-S) | Days 1 and 2. 029-24 § 14: *"… hel eller delvis tjenestefrihed til pasning af et sygt barn på barnets 1. og 2. sygedag, når: 1. hensynet til barnets forhold gør dette nødvendigt 2. forholdene på tjenestestedet tillader det 3. barnet er under 18 år og 4. barnet er hjemmeværende"* | Days 1–3, plus the rest of the day. 021-26 § 14 (Cirkulære af 18. juni 2026; the cover reads "2029"; https://cirkulaere.medst.dk/media/dgge2q3l/021-26.pdf): *"… på barnets 1., 2. og 3. sygedag … Bliver barnet sygt i løbet af den ansattes arbejdsdag, kan den ansatte endvidere holde fri med løn i de resterende arbejdstimer den pågældende dag, hvis betingelserne i pkt.1-4 er opfyldt."* General remark 3: *"Fra den 1. april 2026 er muligheden for hel eller delvis tjenestefrihed til pasning af sygt barn udvidet."* |
| **PROSA** (LC/CO10) | Days 1 and 2. 012-25 § 14. The kost.dk copy is a scanned (OCR) print of the MedSt circular. I confirmed independently that 012-25 is the OK24 LC/CO10 text: 020-26's commencement clause repeals *"Finansministeriets cirkulære af 6. februar 2025 om fællesoverenskomst mellem Finansministeriet og Lærernes Centralorganisation og CO10 … (Medst. nr. 012-25)"* | Days 1–3, plus the rest of the day. 020-26 § 14 (18 Jun 2026, https://cirkulaere.medst.dk/media/qimf0r1n/020-26.pdf). **Refinement:** the *provision* matches 021-26, but the remarks differ by one sentence. 020-26 says *"Den 1., 2. og 3. sygedag kan holdes uafhængigt af hinanden"*, while 021-26 (and 027-26) still say *"Den 1. og 2. sygedag kan holdes uafhængigt af hinanden"*. This does not change the entitlement |
| **AC** (AC agreement) | Days 1 and 2. CIR1H 9333 § 18 stk. 6: *"Ansættelsesmyndigheden kan give den ansatte hel eller delvis tjenestefrihed til pasning af et sygt barn på barnets 1. og 2. sygedag, når: …"* | **Agreed, but the circular is pending.** AC settlement (*Resultatet af forhandlingerne mellem finansministeren og Akademikerne*, February 2026, https://medst.dk/media/0rmlepno/forlig-ok26-medst-ac.pdf), item 4: *"Parterne er derfor enige om at give mulighed for fravær i forbindelse med barnets 3. sygedag og på hjemkaldelsesdagen."* General clause: *"Ændringerne har virkning fra den 1. april 2026, medmindre andet fremgår af forhandlingsresultatet."* Item 4 gives no other date. **Status:** no AC agreement circular in MedSt's list through 068-26, and CIR1H 9333 still prints *Gældende* with no "Senere ændringer" (later changes) line |
| **AC civil servants** (refinement) | — | **Covered now.** *Cirkulære om tjenestefrihed af familiemæssige årsager*, Cirkulære af 25. juni 2026, Medst.nr. 027-26, general remark 1 (https://cirkulaere.medst.dk/media/twmplgln/027-26.pdf): *"For tjenestemænd og tjenestemandslignende ansatte gælder tilsvarende, at ansættelsesmyndigheden kan give hel eller delvis tjenestefrihed til pasning af et sygt barn på barnets 1., 2. og 3. sygedag …"* It includes the call-home sentence. The annexed agreement § 9: *"Aftalen har virkning fra 1. april 2026."* This reaches every state civil servant, AC civil servants included |

The day-count rule was re-read in the 021-26 and 020-26 remarks and in 027-26:

> *"Hvis barnet bliver syg, efter den ansatte er mødt på arbejde, vil denne dag udgøre hjemkaldelsesdagen. Barnets 1. sygedag er den første hele sygedag. Den 2. sygedag er den kalenderdag, der ligger i umiddelbar forlængelse af 1. sygedag, og den 3. sygedag er den kalenderdag, der ligger i umiddelbar forlængelse af 2. sygedag, uanset om det er en arbejdsdag eller en fridag."*
> → The day the child falls ill during work is the call-home day. Day 1 is the first *whole* sick day. Days 2 and 3 are the next *calendar* days, working day or not.

### R1-9 (the call-home day arithmetic, an inference): **agree** that it is an inference
- The text grants *"fri med løn i de resterende arbejdstimer den pågældende dag"* (paid time off for the remaining working hours that day).
- Under flex, with no fixed hours, "remaining working hours" is itself undefined. The natural reading is the day's planned hours (or the 7.4 × fraction fallback) minus hours already worked, floored at zero.
- No text states this.

### Partial own-sickness day: **partly**. Unsettled, and the first pass's analogy needs one correction.
- **Agree that no central text splits the day.** § 7 nr. 2 speaks of *"Dage med ret til fravær med løn"* (*days* with paid absence), and nothing addresses a part day.
- **PAV 24.3.1.1 is about something else.** It covers *"Delvis sygemeldte ansattes ret til løn"* (the pay of staff who are *partly* signed off sick, e.g. working half-time during recovery): *"… har derfor ret til sædvanlig løn under det delvise sygefravær"*. Source: https://pav.medst.dk/pav/kapitel-24-sygdom/. It does not address a person going home sick mid-day. It is not evidence either way on the arithmetic.
- **Correction.** The first pass says the day "totals the day's norm, never more". That is wrong. Hours worked count in full under § 7 nr. 1 (*"tiden mellem mødetidspunktet og det tidspunkt, hvor den ansatte kan forlade arbejdsstedet"*, the time from arrival until the person may leave). Nothing caps them.
- **The defensible analogy is:** worked hours + max(0, planned hours − worked hours). That equals **max(worked, planned)**. "Never more than the norm" holds only when the person had worked less than the planned hours.
- **Confidence: MEDIUM, as the first pass said.** It is an analogy, and a local agreement may set its own rule (§ 3).

### Contrary evidence searched for (verifier)
- **Searched for:** a state text crediting registered hours, or zero hours, for a sick day. Found: none.
- **Searched for:** a newer HK, PROSA or joint-agreement text that changes § 14 or the working-time cross-reference. Found: only the 087-25 supersession noted above.
- **Searched for:** an OK26 AC circular. Found: none in the list through 27.10.2026.

---

## R2 — Særlige feriedage in fractions of days (ADR-032 D3)

**Verifier: agree. CONFIRMED.**

### R2-1 (the agreement in force): **agree**
> § 13 stk. 2: *"Afvikling kan ske som enkeltdage eller som brøkdele af dage."*
> → The days may be taken as single days or as fractions of days.

- *Cirkulære om aftale om ferie*, CIR1H nr 9298 af 17/05/2024, printed 28 Oct 2026 with status **(Gældende)** and no later-changes line: https://www.retsinformation.dk/eli/retsinfo/2024/9298/pdf. The same text is in Medst. 021-24: https://cirkulaere.medst.dk/media/1372/021-24.pdf.
- The general remarks: *"vedlagte aftale om ferie af 15. maj 2024 … Aftalen har virkning fra 1. april 2024 og erstatter aftale om ferie af 16. december 2019."* § 20: *"Aftalen har virkning fra 1. april 2024. Samtidig ophæves aftale af 16. december 2019 om ferie."*

### R2-2 (who it covers): **agree**
> § 1: *"Aftalen gælder for tjenestemænd og tjenestemandslignende ansatte samt for ansatte omfattet af overenskomster indgået på den ene side af eller efter bemyndigelse fra Finansministeriet, Medarbejder- og Kompetencestyrelsen, og på den anden side af de underskrivende centralorganisationer …"*

That covers AC, HK and PROSA.

### R2-3 (the 2019 predecessor and ADR-032's quote): **agree**
*Ferievejledning*, December 2019 (MedSt and CFU), https://medst.dk/media/kp4fffy1/ferievejledning-2019.pdf, § 3.7.2:
> *"Særlige feriedage kan holdes samlet, enkeltvis og som brøkdele af dage."*

Then Example 3.5, re-read verbatim: 6-hour days, 3 hours off = ½ day; on a 9-hour Tuesday, 3 hours off = ⅓ day.

The annexed 2019 agreement has the identical § 13 stk. 2. PAV 23.2 (https://pav.medst.dk/pav/kapitel-23-ferie/) still names the December 2019 guide as *the* guide. A web search found no newer MedSt/CFU holiday guide.

### R2-4 (nothing newer replaces it): **agree**
- MedSt's list from 05.11.2021 to 27.10.2026 contains no holiday circular after 021-24.
- Neither OK26 settlement amends § 13. In both, the only mention of "ferie" is in the Fritvalgs Lønkonto item.

### R2-5 (2028 forward watch): **agree**, with one date correction
- **The settlement text.** CFU settlement, item 3 (11 Feb 2026, https://medst.dk/media/oo1auuiu/ok26-forlig-medst-cfu.pdf): *"Parterne er derfor enige om at etablere en Fritvalgs Lønkonto pr. 1. januar 2028, jf. bilag D."* The item continues: *"… afholdelse af fritvalgsdag, seniordage og særlige feriedage, hvor den ansatte kan få udbetalt midler fra sin Fritvalgs Lønkonto, ikke medfører ændring af den aftalte arbejdstidsnorm ved indberetning af løntimer …"*. In English: taking those days, funded from the account, does not change the agreed working-time norm for reporting.
- **The annexes.** They are not in the PDF, which ends with the signatures of 11 Feb 2026.
- **The OAO FAQ** (https://www.djf.dk/wp-content/uploads/2026/04/OAO-FAQ-Fritvalgskonto.pdf) is a secondary source and is **dated 24 February 2026** on its face. The "04" is only the upload path. Its quote is exact: *"De særlige feriedage og fritvalgsdagen kan du holde som timer eller som hele dage. Seniordagene kan kun holdes som hele dage."* It also says *"Alle fridagene holdes med løntræk"* (all the days are taken with a pay deduction).

### Nuance the first pass did not raise (it qualifies R2 but does not contradict it)
The 2019 guide, just before § 3.7.2:
> *"Er dagene ikke afholdt og tidspunktet for afholdelsen ikke fastlagt senest 1. januar, kan ledelsen varsle dagene afholdt efter de regler, der gælder for øvrig ferie. Det betyder, at varslet er 1 måned, og at dagene **kun kan varsles til afholdelse i hele dage**."*
> → If the days are untaken and unscheduled by 1 January, management may impose them on one month's notice under the ordinary holiday rules, **and may impose them only as whole days**.

The 2024 agreement's § 13 stk. 3 carries the same one-month rule without stating "whole days". The guide that PAV names as current gives the whole-day reading. The guide also says: *"Placering af dagene skal afspejle det sædvanlige arbejdsmønster"* (placement must reflect the usual work pattern).

**For StatsTid:** part-days are legal when the employee takes them. An employer-imposed booking after 1 January should be whole days.

---

## R3 — On-call standby, call-in and non-working travel

**Verifier: agree. CONFIRMED.**

### R3-1 (the agreement text): **agree**
§ 7 nr. 3–5 and their remarks were re-read in 022-26. They fall inside my § 7 identity check, so they are identical in 014-21 and CIR1H 9551.

> *"3. Rejsetid i forbindelse med tjenesterejser medregnes, dog højst med 13 timer pr. døgn. 4. Rådighedstjeneste i hjemmet medregnes med 1/3 og rådighedstjeneste på arbejdsstedet med 3/4. 5. Tilkald med mindre end 24 timers varsel medregnes med mindst 3 timer. Det gælder dog ikke, hvis arbejdet ligger i umiddelbar tilknytning til det normale arbejde, eller tilkaldet sker som led i rådighedstjeneste."*
> → Travel counts, max 13 h per døgn. Home on-call counts at 1/3, workplace on-call at 3/4. A call-in at under 24 h notice counts at least 3 h, except when the work adjoins normal hours or the call-in is part of on-call.

The remarks (also re-read):
- *"Ved døgn forstås i denne sammenhæng arbejdsdøgnet, dvs. 24 timer regnet fra den pågældende arbejdsdags begyndelse."* (A døgn here is the working døgn: 24 h from the start of that working day.)
- *"Ved 'tilkald' forstås, at den ansatte skal møde op på arbejdsstedet. Hvis arbejdet udføres fra den ansattes bopæl, er det derfor kun den faktiske arbejdstid, der medregnes."* (A call-in means having to come to the workplace; work from home counts actual time only.)

### R3-2 to R3-5 (PAV elaborations): **agree**
All of these were re-read verbatim in PAV kap. 19:
- **19.2.6:** the extra-travel rule (*merrejsetiden*); Example 5 (14 h) and Example 6 (15 h); on-call *"medregnes kun delvis"* (counts only partly); the advice to make a special agreement for permanent on-call; on-call during trips; *"Hvis den ansatte tilkaldes flere gange inden for en 3-timers periode, medregnes kun 3 timer i alt"* (several call-ins within 3 hours count 3 hours in total).
- **19.2.8.1:** *"En rådighedstjeneste i hjemmet af 9 timers varighed udløser således en godtgørelse på 1/3 af 9 timer = 3 timer med tillæg af 50 pct."* (A 9-hour home on-call at the weekend gives 3 hours plus 50 %.)
- **19.2.9.1:** *"Ulempetillægget efter aftalens § 13 ydes for den faktisk præsterede arbejdstid, uafhængigt af om der er fastsat et maksimum eller et minimum … Der ydes ikke godtgørelse efter aftalens § 13 for rådighedstjeneste i hjemmet. … En rådighedsvagt på 8 timer udløser således ulempetillæg for 6 timer."* (The § 13 supplement is paid on hours actually worked, whatever the travel maximum or call-in minimum; none for home on-call; an 8-hour workplace on-call earns it for 6 hours.)
- **19.2.8.2 and 19.2.9.2 (shift workers):** quotes exact.

### R3-6 (HK and PROSA add nothing): **agree**
I searched the full text of **087-25** (the in-force HK text), 079-25 and 019-26 for *rådighed*, *tilkald*, *rejsetid* and *vagt*. The only hits are *"rådighedstillæg"* or *"rådigheds-/merarbejdstillæg"* (an allowance excluded from the hourly-rate base), plus *"vagtplaner"* in a job description.

### R3-7 (AC): **agree**, with the AC civil-servant addition
- **Travel.** CIR1H 9333 § 13 stk. 6: *"Rejser, der udføres som en del af tjenesten, medregnes i arbejdstiden, dog højest med 13 timer pr. døgn, medmindre andet aftales lokalt."* Stk. 7: *"Tjeneste på det fremmede tjenestested medregnes efter de almindelige regler for opgørelse af arbejdstid."* (Travel counts, at most 13 h per døgn unless agreed locally; duty at the other workplace counts under the general rules.) The AC text does not define døgn; the "working døgn" definition is in the state agreement's remark only.
- **No general on-call or call-in rule.**
  - *"tilkald"* occurs **0** times in CIR1H 9333.
  - PAV 19.3.4, quoted above, says working time is undefined and there is no unsocial-hours pay.
  - Bilag 6 has area-specific payments. Example: *"Der ydes betaling for deltagelse i rådighedsvagt for ansatte i Energistyrelsen i henhold til aftale af 23. september 2008 …"* (payment for on-call for Danish Energy Agency staff under a 2008 agreement).
  - The remark to § 14: *"Ved udøvelsen af skønnet over et udført merarbejde kan der tages hensyn til, at en del af det opgivne bruttotimetal ikke er udført i form af effektiv tjeneste, men som rådighedstjeneste og lign."* (When assessing claimed extra work, the employer may discount hours that were on-call rather than effective duty.)
- **Do not confuse with the *rådighedstillæg*.** PAV 19.3.4.4: *"Merarbejdsforpligtelsen udgør indtil 20 timer pr. kvartal."* This is an allowance for a duty to do up to 20 hours of extra work a quarter. It is not on-call. Agree.
- **Addition: AC civil servants.** Under § 1 of the state working-time agreement (see R1-6), § 7 nr. 3–5, including the on-call fractions and the 3-hour call-in minimum, apply to AC *civil servants*. They do not apply to AC staff on the AC agreement.

### R3-8 (the EU definition is a separate question): **agree**, with a caveat
- **The two texts.**
  - 022-26 Bilag 1 § 2 nr. 1, identical in 014-21 Bilag 1: *"'arbejdstid' som det tidsrum, hvori den ansatte er på arbejde og står til rådighed for arbejdsgiveren, herunder pauser, der medregnes i arbejdstiden"*.
  - PAV 19.7.1: *"Rådighedstjeneste i hjemmet, men ikke på arbejdsstedet, betragtes i denne relation som hviletid med mindre rådighedstjenesten har en sådan karakter, at formålet med hvileperioden forspildes."* (For the 11-hour rest rule, home on-call is rest unless it defeats the purpose of rest.)
- **The caveat.** Whether a given home on-call defeats the purpose of rest is decided case by case under EU case law. The PAV proviso is the state's hook for that. Like the first pass, I did not fetch the case law. StatsTid's rest check therefore needs its own classification flag, as the first pass says. It should not hard-code home on-call as always being rest.

### R3-9 (OK24 vs OK26): **agree**
§ 7 is identical (my diff, R1-1).

---

## R4 — Has the implementing text for the 4 February 2026 part-timer award appeared?

**Verifier: agree. CONFIRMED that it has not appeared as of 28 Oct 2026.**

### R4-1 (MedSt circular list): **agree**, with an immaterial count correction
I read https://cirkulaere.medst.dk/love-regler-og-aftaler/cirkulaerer/?pageNumber=1 through **32** on 28 Oct 2026.
- The newest entry is **068-26**, *Cirkulære om aftale om nyt lønsystem for tjenestemænd og tjenestemandslignende ansatte inden for den tidligere … (TAT)*, published **27.10.2026**.
- Pages 1–10 hold **100** entries, back to 15.05.2025. The first pass said 97.
- **No entry from 05.11.2021 to 27.10.2026 concerns part-timers' overtime or extra work (merarbejde), or the award.** The 2026 working-time circulars are 022-26 and 023-26, plus the teacher texts 026-26, 040-26 and 065-26.

### R4-2 and R4-3 (the old rule is still printed): **agree**
- **022-26 § 8 stk. 2:** *"Timer, der ikke overstiger arbejdstiden for fuldtidsbeskæftigelse, jf. § 4, stk. 2, godtgøres dog kun med afspadsering af samme varighed eller med almindelig timeløn."* (Hours not exceeding full-time hours are compensated only with equal time off or ordinary hourly pay.)
- **022-26's introduction** lists only the 111-hour change and Greenland's Bilag 2. It mentions no part-timer change.
- **PAV 19.2.7.1:** *"For deltidsansatte ydes tillægget på 50 pct. dog kun for de timer, der overstiger den normale arbejdstid ved fuldtidsbeskæftigelse."* (For part-timers the 50 % supplement is paid only above full-time hours.)
- **PAV 19.3.1.6:** *"For deltidsansatte er arbejdstidsnormen i forbindelse med overarbejde fuldtidsnormen."* (For part-timers, the overtime norm is the full-time norm.)
- All four quotes are exact.

### R4-4 (the mandate): **agree**
- **CFU settlement, item 28:** *"Håndtering af faglig voldgiftskendelse om honorering af over/-merarbejde for deltidsansatte. Der afsættes i alt 12 mio. kr. Midlerne dækker udgifter til en efterfølgende forhandling og aftale om implementering af voldgiftsrettens dom af 4. februar 2026 på statens område. … Forhandlinger optages senest 1. maj 2026. Parterne aftaler senest 1. april 2026 en proces for håndtering af krav om efterbetaling …"* (12 million kroner for a later negotiation and agreement implementing the award in the state; negotiations to open by 1 May 2026; a back-pay process to be agreed by 1 April 2026.)
- **AC settlement, item 16:** the identical text.

### R4-5 (the OK26 joint agreements are silent): **agree**
- **021-26 and 020-26:** general remark 3 lists only the child-sickness extension and the 111-hour floor, plus monthly pay from 1 April 2027 in 021-26. Nothing on part-timers.
- **PROSA 019-26 § 13 *Deltid*:** only the rule on offering hours to existing part-timers first.

### R4-6 (MedSt news archive silent): **agree**
- **What I read:** https://medst.dk/nyheder/nyhedsarkiv/?pageNumber=1–9, back to 22.08.2025.
- **What the 2026 items cover:**
  - the settlement (*"Aftale om treårigt overenskomstforlig for medarbejderne i staten indgået"*, 11.02.2026);
  - the organisation negotiations (13.04.2026);
  - *"Statens overenskomster tiltrådt"* (06.05.2026);
  - regulation percentages;
  - the June circulars;
  - a run of organisation agreements from Aug to Oct 2026.
- **None** concerns the award.

### R4-7 (secondary status statements): **agree**
- **DLF** (https://www.dlf.org/loen-og-vilkaar/arbejdstid/deltidsansatte/, undated, cites the 4 Sep 2026 award): *"Danmarks Lærerforening forventer, at der vil blive indgået en lignende aftale for deltidsansatte i staten. Aftalen er dog ikke på plads endnu."* (DLF expects a similar agreement for state part-timers; it is not yet in place.)
- **HK** (https://www.hk.dk/raadogstoette/arbejdstid/arbejdstid-stat): *"… vil reglerne om overtidsbetaling for deltidsansatte blive ændret i løbet af 2026 i samarbejde med Medarbejder- og Kompetencestyrelsen …"* (the part-timer overtime rules will be changed during 2026 together with MedSt). HK's page wrongly calls the award an EU-court ruling. That does not affect the status reading.
- **DM, the AC union** (https://dm.dk/raad-og-svar/arbejdstid/deltid/deltidsansat-du-kan-have-ret-til-efterbetaling-for-over-eller-merarbejde/): describes back-pay claims and names no state agreement.

### R4-8 (FV2026.573 citation trap): **partly**
- **The private-sector reading holds.** Dansk Erhverv's report (https://www.danskerhverv.dk/presse-og-nyheder/nyheder/2026/september/krav-om-overtidsbetaling-til-deltidsansatte-foraldes-efter-5-ar--ikke-10-ar/) says the parties *"under overenskomstforhandlingerne i 2025 aftalt at afvente udfaldet af prøvesagen … på industriens område"* (in the 2025 negotiations, the parties agreed to await the outcome of the test case in manufacturing). 2025 was the private-sector bargaining round. So the award's case is private-sector, as the first pass says.
- **But the five-year limit is general law.** The *conclusion* is the general five-year limitation for wage claims. DLF, a public-sector union, cites the award as settling the question: *"I en faglig voldgift den 4. september 2026 er det slået fast, at efterbetalingskravene som følge af deltidssagerne er underlagt en femårig forældelsesfrist"* (an award of 4 September 2026 established a five-year limitation for the back-pay claims).
- **For the record:** do not cite FV2026.573 as a *state* rule, but the five-year limit is not private-sector-only. Back pay is outside StatsTid's scope either way.

### R4-9 (FV2025-795, re-read rather than inherited): **partly** (citation correction)
- **Agree on the operative part** (https://arbejdsretten.dk/media/bork3lzb/fv2025-795.pdf, *Kendelse afsagt den 4. februar 2026 i Faglig Voldgift FV2025-795*): *"Medarbejder- og Kompetencestyrelsen skal over for CO10 …, Lærernes Centralorganisation (LC), Akademikerne (AC) og Offentligt Ansattes Organisation (OAO) – Det statslige område anerkende, at det er i strid med direktiv 97/81/EF … at deltidsansatte … efter de i kendelsens bilag A angivne bestemmelser kun skal have overtidshonorering for arbejdstimer, der præsteres ud over den normale arbejdstid for fuldtidsansatte"*, *"… gælder fra den 20. januar 2000."* In English: MedSt must recognise, toward the four state cartels, that paying part-timers overtime only above full-time hours breaches the part-time directive, with effect from 20 January 2000.
- **Correction.** Annex A names *"Statens arbejdstidsaftale indgået mellem Skatteministeriet og CO10 …, Lærernes Centralorganisation, Akademikerne og Offentligt Ansattes Organisation (OAO) – Det statslige område, § 8, stk. 2"*. It names **the agreement, not "022-26"**: that circular was issued on 18 Jun 2026, after the award. The provision is the same one now printed in 022-26.
- Annex A also lists **HK's organisation agreement, Bilag 9** (the laboratory technicians' sea and fieldwork protocol).

---

## R5 — The OK24 texts of §§ 13 and 14

**Verifier: agree. CONFIRMED.**

### R5-1 (the document and its validity): **agree**
- **014-21's commencement:** *"Cirkulæret træder i kraft den 5. juli 2021 og har virkning fra den 1. april 2021."*
- **Retsinformation print of 28 Oct 2026:** *"CIR1H nr 9551 af 05/07/2021 (Historisk)"* with *"Senere ændringer til forskriften: CIR1H nr 9594 af 18/06/2026"* and nothing else. The introduction is *"Skatteministeriet og centralorganisationerne har den 10. oktober 2019 indgået vedlagte aftale."*
- **022-26's commencement:** *"Cirkulæret træder i kraft den 18. juni 2026 og har virkning fra den 1. april 2026. Samtidig ophæves Skatteministeriets cirkulære af 5. juni 2021 om statens arbejdstidsaftale (Medst. nr. 014-21)."* The misprint "5. juni" is confirmed.
- **My additional check.** MedSt's chronological list from 05.11.2021 to 27.10.2026, which includes superseded (*Historisk*) circulars, contains **no** circular amending the state working-time agreement other than 022-26. The only other working-time circulars are teacher-specific (023-22, 038-22, 040-22, 061-25, 026-26, 040-26, 065-26).
- So 014-21 governed the whole OK24 period. This rests on the circular chain, not on the scanned OK24 settlement. And because 022-26's §§ 13–14 are identical to 014-21's, any OK24 change to them is excluded in any case.

### R5-2 (the full quote of §§ 13–14 with remarks): **agree**. The quote is exact.
I tokenised the first pass's blockquote and diffed it against my own extraction of 014-21: **zero differing tokens** over the full span, from *"§ 13. For arbejde i tidsrummet fra kl. 17 til kl. 06 …"* to *"… undtaget fra de almindelige regler om overarbejdsbetaling."*

### R5-3 (identity with OK26): **agree**
- I normalised and diffed the span from "§ 13. For arbejde" to "§ 15. Den ansatte har ret" in all three texts.
- **014-21 vs 022-26:** identical.
- **CIR1H 9551 vs 022-26:** identical except one artefact of my normaliser, which joined the suspended hyphen in "over- eller" across a line break. The texts themselves agree.
- The first pass's 616-word count used a different tokenisation. The decisive fact reproduces.

### R5-4 (caveat on the scanned OK24 settlement): **agree**, and the caveat is now moot
Two facts settle it without the scan:
- the circular chain has no amendment between 2021 and 2026;
- §§ 13–14 are unchanged from 2021 to 2026.

---

## Found in passing (claims that bear on StatsTid's encoded rules)

| Item | Verifier | Record |
|---|---|---|
| **P-1** Child's sick days (`danish-agreements.md:148-150` encodes AC 1 / HK 2 / PROSA 3 per episode, "Annual Quota / Reset January") | **agree**, with refinements | **OK24:** AC 2 (CIR1H 9333 § 18 stk. 6), HK 2 (029-24 § 14), PROSA 2 (012-25 § 14). **OK26 from 1 Apr 2026:** HK 3 plus the remaining hours of the call-home day (021-26 § 14); PROSA the same (020-26 § 14); AC civil servants the same (027-26, in effect from 1 Apr 2026); AC staff on the AC agreement agreed (AC settlement item 4, in effect from 1 Apr 2026) with the circular pending. **Rows contradicted:** AC = 1 for both versions; PROSA = 3 for OK24. HK = 2 is right for OK24 and short for OK26. **Further facts:** the texts have **no annual cap** (only a misuse clause, stk. 3); the days are **calendar days from the first whole sick day**, so a day 2 or 3 falling on a weekend is used up without leave; the right is **discretionary** (*"Ansættelsesmyndigheden kan give … når … forholdene på tjenestestedet tillader det"*, the employer may grant it when the workplace allows) |
| **PROSA's agreement family** | **agree** | LC/CO10, not OAO-S. 019-26's data sheet reads *"Fællesoverenskomst mellem Skatteministeriet og Lærernes Centralorganisation og CO10 – Centralorganisationen af 2010 (LC/CO10-fællesoverenskomsten)"*. 079-25's data sheet says the same. Under LC/CO10 the working-time agreement applies only through PROSA's express § 12 (022-26 general remarks; PAV 19.3.1) |
| **P-2** PAV lags OK26 | **agree** | Re-read 28 Oct 2026. PAV 19.2.7.4 still says the maximum *"kan dog ikke fastsættes til mindre end 74 timer"*, where 022-26 has 111. PAV 19.3.1.6 has *"barns 1. og 2. sygedag"*. PAV 19.4 describes *opsparing af frihed* (Fmst. cirk. 31/3 2025) as current, but **024-26** (19 Jun 2026, https://cirkulaere.medst.dk/media/xewhlj0b/024-26.pdf) repeals it from 1 Apr 2026: *"Der kan ikke efter aftalen optjenes ny frihed fra og med den 1. april 2026"* (no new time can be saved after 1 April 2026); balances can be taken until 31 Dec 2027, then are paid out. **Additional evidence:** PAV 19.2.1 and 19.3.1 still cite *"Skm. cirk. 5/7 2021"* (014-21) as the working-time circular. The footer *"Sidst redigeret 03.12.24"* appears on chapters 19, 23 and 24, yet the pages cite 2025 circulars, so the footer date is unreliable. Treat PAV as pre-OK26 |
| **P-3** Citation traps | **agree** | 022-26 dates 014-21 as "5. juni 2021" (it is 5 July). 021-26's cover reads *"Cirkulære af 18. juni 2029"*, while its signature block reads "Den 18. juni 2026" |
| **P-4 / R2-5** The 2028 Fritvalgs Lønkonto | **agree** (date fix) | CFU item 3 is verified (see R2-5); no implementing circular yet. The OAO FAQ is dated **24 Feb 2026**. **Context for "seniordage become whole-days-only":** today's rule, *Cirkulære om aftale om seniorordninger* (Medst. 035-24, *Gældende*, https://cirkulaere.medst.dk/media/yxwgde4i/035-24.pdf), says *"Seniordage skal som udgangspunkt afvikles som hele fridage. Efter aftale … kan afviklingen dog ske i form af halve dage eller som enkeltstående timer."* (Whole days as the starting point; half days or single hours by agreement.) So OAO's 2028 reading would be a narrowing, and it is secondary. StatsTid's hours ÷ day-norm consumption of SENIOR_DAY is legal today only where a part day is agreed |
| **P-5** FV2026.573 | **partly** | See R4-8. It is a private-sector award, but the five-year limitation it applies is general law and public-sector unions rely on it. Not a state rule; outside StatsTid's scope |
| **P-6** The call-home day needs its own representation | **agree** (inference) | The OK26 text grants paid leave for *"de resterende arbejdstimer den pågældende dag"* (the remaining working hours of that day), separate from days 1–3. A whole-day-only model cannot hold it |
| **P-7** Weekday office hours change the credit | **agree**, and broaden it | PAV 19.5.13 (*kan*) is one option. More generally, 022-26 § 3 lets a local agreement depart from § 7 entirely. Model the sick-day credit basis as a per-local-agreement parameter, defaulting to § 7 nr. 2 |
| **V-1** (new) HK citation | correction | 085-25 is *Historisk*. Cite **087-25** (14 May 2025), § 38. Its substance is identical |
| **V-2** (new) AC civil servants and children's sickness | addition | 027-26 already gives all state civil servants days 1–3 plus the call-home day from 1 Apr 2026. If StatsTid ever models AC civil servants, they are on the OK26 rule now, not pending |
| **V-3** (new) Employer-imposed særlige feriedage | addition | Whole days only, per the Dec 2019 guide (R2 nuance) |

## What I could not do
- I did not read EU case law on stand-by time (R3-8 caveat). The first pass did not either.
- I did not look for a separate agreement for AC civil servants that might modify § 7. None surfaced, and § 1 plus FV2025-795's Annex A point the other way.
- I did not read the OK24 CFU settlement scan. It is not needed: R5 is settled by the circular chain and the 2021↔2026 identity.
- I did not read the AC OK26 settlement annexes (bilag H and I on children and parental leave). They are not in the published PDF.

## Sources re-read for this verification (all on 28 Oct 2026)
**Primary.**
- **Working time:** Medst. 022-26; Medst. 014-21; CIR1H 9551; Medst. 024-26.
- **Organisation agreements:** Medst. 087-25 and the 085-25 landing page (HK); Medst. 079-25 and 019-26 (PROSA).
- **Joint agreements:** Medst. 029-24 and 021-26 (OAO-S); Medst. 012-25 (kost.dk scan, confirmed by 020-26's repeal clause) and 020-26 (LC/CO10).
- **AC:** CIR1H 9333.
- **Leave and holidays:** Medst. 027-26; Medst. 021-24; CIR1H 9298; Ferievejledning Dec 2019; Medst. 035-24.
- **OK26 settlements:** CFU (11 Feb 2026) and AC (Feb 2026).
- **Arbitration:** FV2025-795.
- **MedSt guidance:** PAV kap. 19, 23 and 24.
- **MedSt listings:** the circular list, pp. 1–32; the news archive, pp. 1–9.

**Local.** SDU flex agreement 2026, § 2.2. The quote is exact: *"Normtid for en fuldtidsansat er 7,4 timer pr. dag, 37 timer pr. uge. For deltidsansatte fastsættes normtiden forholdsmæssigt."*

**Secondary (status or support only).** DLF part-timer page; HK state working-time page; Dansk Erhverv, 10 Sep 2026; DM part-timer page; OAO FAQ of 24 Feb 2026.


---

# Part 2 — The first pass (verbatim; superseded where Part 1 corrects it)


| | |
|---|---|
| **Date** | 2026-10-28 |
| **Pass** | First pass, S146 D4 method. An independent verifier gets only the numbered claims and quotes below, not this pass's reasoning |
| **Brief** | `.claude/plans/s147/R-RESEARCH.md` |
| **Method** | Every decisive primary text was downloaded on 28 Oct 2026 and read as extracted text (`pdftotext`), not through a summariser. Where a claim is "unchanged between versions", the two texts were compared word by word, with hyphenation and page furniture stripped. MedSt's chronological circular list was read on pages 1–10, from 068-26 (27 Oct 2026) back to 088-25 (15 May 2025). Its news archive was read on pages 1–8, back to 27 Nov 2025. Web searches looked for contrary evidence |
| **Status** | Unverified first pass. Where this pass reuses a finding that S146 had already verified, it says **inherited** |

---

## Summary for the product owner

StatsTid needs five agreement rules settled before it encodes them. All five can be answered from the state's own texts. None of the answers is contradicted.

| Cell | Question, in short | Verdict | The answer in one line | Decisive source |
|---|---|---|---|---|
| **R1** | Does a paid sick day (and a child's sick day) count toward the hours owed in state flex, and with what hours? | **CONFIRMED** | Yes. A paid sick day counts **as if worked**. It is credited with the hours the person **was scheduled to work that day**. Where no hours are fixed for the day, which is the normal flex case, it is credited with **7.4 h for a full-timer, pro rata for a part-timer**. The credit never comes from the hours someone typed. So a whole sick day leaves the flex balance unchanged. A paid child's sick day is credited the same way. The crediting text is word-for-word the same in OK24 and OK26. **What OK26 changed is the child's sick days.** From 1 April 2026, HK and PROSA staff can also take the **3rd** day, plus the rest of the working day on which the child falls ill. AC agreed the same in OK26, but its circular has not been issued yet | Statens arbejdstidsaftale § 7 nr. 2 and its remark (014-21 = 022-26); OAO-S 021-26 and LC/CO10 020-26 § 14 |
| **R2** | Re-verify ADR-032 D3: may særlige feriedage (the 6th holiday week, five special days off) be taken in fractions of days? | **CONFIRMED** | Yes. The current ferie agreement says the days may be taken *"som enkeltdage eller som brøkdele af dage"* (as single days or as fractions of days). ADR-032's quote is the joint guide's wording of the same rule | CIR1H 9298 / Medst. 021-24, § 13 stk. 2 |
| **R3** | Are on-call standby, call-in and non-working travel working time, and at what rate? | **CONFIRMED** | **HK/PROSA:** travel on official trips counts in full, up to 13 h per working day. On-call counts as **1/3 of its hours at home** and **3/4 at the workplace**. A call-in at less than 24 h notice counts as **at least 3 h**. On-call has no separate kroner rate centrally; its "rate" is the fraction. **AC:** travel follows the same 13-h cap. AC has **no general on-call or call-in rule**; on-call pay exists only for named areas in Bilag 6 | Statens arbejdstidsaftale § 7 nr. 3–5 (014-21 = 022-26); AC agreement § 13 stk. 6 |
| **R4** | Has the state's implementing text for the 4 Feb 2026 part-timer award appeared? | **CONFIRMED** (it has **not** appeared) | No. MedSt's list runs up to 068-26 of 27 Oct 2026 and contains no such circular. The working-time circular of 18 Jun 2026 still prints the old 1:1 rule for part-timers up to full time. A union page updated after 4 Sep 2026 says the state agreement is "ikke på plads endnu" (not yet in place). S146's C4 status holds | MedSt circular list (068-26, 27 Oct 2026); 022-26 § 8 stk. 2 |
| **R5** | The OK24 texts of §§ 13 and 14 (the single +25 % supplement and the § 14 weekend compensation) | **CONFIRMED** | The OK24 period ran on circular **014-21** (5 Jul 2021, = CIR1H 9551). Its §§ 13–14 and their remarks are **word-for-word identical** to OK26's 022-26 (616 words compared). S146's 022-26 quotes therefore hold for OK24 periods too, and StatsTid can encode §§ 13–14 once for both | Cirkulære om statens arbejdstidsaftale, Medst. 014-21, §§ 13–14 |

**Terms used.**
- **norm:** the hours a person owes in a period.
- **flex (flekstid):** a local scheme where hours above or below the norm carry over as a balance.
- **præsteret arbejdstid:** the hours counted as performed. Paid absence is added into this figure; it does not reduce the norm.
- **særlige feriedage:** the five special days off a year in the state, the "6th holiday week".
- **rådighedstjeneste / rådighedsvagt:** on-call standby, at home or at the workplace.
- **tilkald:** a call-in, being called in to the workplace.
- **hjemkaldelsesdag:** the day a child falls ill during the parent's working day.
- **OAO-S / LC-CO10:** the two union cartels whose joint agreements (fællesoverenskomster) cover HK and PROSA respectively.
- **PAV:** MedSt's official guide to state personnel rules. It is the employer's reading, not the agreement itself.
- **MedSt:** Medarbejder- og Kompetencestyrelsen, the state employer agency.
- **Circular (cirkulære):** the official publication of an agreement, with remarks. "Medst. nr." and "CIR1H nr." are its two ids: MedSt's own, and Retsinformation's.

---

## R1 — Sick days and child's sick days in state flex

### Plain answer

A sick day with pay is added to the hours counted as **performed**. The amount added is the hours the person was **scheduled** to work that day. Where the day has no fixed hours, as is normal under flex, the amount is **7.4 h for a full-timer and a proportional amount for a part-timer**. It is not the hours registered.

So a full sick day adds exactly that day's norm. The month's surplus or deficit, and with it the flex balance, does not move.

A child's sick day is paid leave of absence (*tjenestefrihed med løn*). It is credited the same way.

The crediting rule is word-for-word the same in OK24 and OK26. The child's-sick-day **entitlement** did change at OK26:
- **OK24:** day 1 and day 2.
- **OK26, from 1 April 2026, for HK and PROSA:** day 1, day 2 and day 3, plus the rest of the working day on which the child falls ill.
- **AC:** agreed the same at OK26, but its implementing circular is not yet published.

For **AC**, the agreement text itself is silent on how hours are credited. There the basis is MedSt's guide (PAV) and the local flex agreement, not agreement text.

### Claims and quotes

**R1-1. The rule, for staff under the state working-time agreement (HK, PROSA).**
> § 7: *"Den præsterede arbejdstid opgøres således: … 2. Dage med ret til fravær med løn medregnes med det antal timer, den ansatte skulle have arbejdet den pågældende dag. Hvis der ikke er fastsat noget timetal for den pågældende dag, medregnes for fuldtidsansatte 7,4 timer og for ansatte på deltid eller plustid et forholdsmæssigt timetal."*
> → "Performed working time is calculated as follows: … 2. Days with a right to paid absence are counted with the number of hours the employee should have worked that day. If no number of hours is fixed for that day, full-timers are counted with 7.4 hours and part-time or plustid staff with a proportional number."
>
> Remark to § 7 nr. 2: *"Fravær med ret til løn er fx sygedage, afspadsering, feriedage og tjenestefrihed med løn."*
> → "Absence with pay is, e.g., sick days, time off in lieu, holiday days and paid leave of absence."

*Source:* Cirkulære om statens arbejdstidsaftale, Cirkulære af 18. juni 2026, Medst.nr. 022-26 (= CIR1H nr 9594 af 18/06/2026), § 7 nr. 2 and remark, PDF p. 14, https://cirkulaere.medst.dk/media/jhshf3w2/022-26.pdf.

**The same text is in the OK24-period circular:** Cirkulære af 5. juli 2021, Medst.nr. 014-21, § 7, PDF p. 14, https://cirkulaere.medst.dk/media/1191/014-21.pdf. On Retsinformation that circular is CIR1H nr 9551 af 05/07/2021, PDF p. 9, https://www.retsinformation.dk/api/pdf/224517.

*Comparison:* the whole of § 7 with all its remarks (281 words) is identical in 014-21, CIR1H 9551 and 022-26.

**R1-2. HK and PROSA are covered by that agreement.**
- HK: *"Ansatte under denne aftale er omfattet af statens arbejdstidsaftale, med de i stk. 2-3 nævnte tilføjelser."* (Staff under this agreement are covered by the state working-time agreement, with the additions in stk. 2–3.) Organisationsaftale for kontorfunktionærer, laboranter og it-medarbejdere (HK), Cirkulære af 12. maj 2025, Medst.nr. 085-25, § 38, PDF p. 34, https://cirkulaere.medst.dk/media/m1hpbtsq/085-25.pdf. Stk. 2–4 add lunch-break rules and the 1/1924 hourly rate. Nothing on absence.
- PROSA: *"De ansatte er omfattet af den til enhver tid gældende statslige arbejdstidsaftale med de i stk. 2 nævnte tilføjelser."* (Covered by the state working-time agreement in force at any time.) Organisationsaftale for it-medarbejdere (PROSA), Cirkulære af 17. juni 2026, Medst.nr. 019-26, § 12, PDF p. 13, https://cirkulaere.medst.dk/media/xfcdpmfq/019-26.pdf. For the OK24 PROSA text (079-25 § 12) this is inherited from S146 V5.

**R1-3. MedSt's reading of the basis: the planned hours, not the registered ones.**
> PAV 19.2.6: *"Dage med ret til fravær med løn medregnes med det antal timer, den ansatte skulle have arbejdet den pågældende dag. Fravær med ret til løn kan fx være sygedage, afspadsering, feriedage og tjenestefrihed med løn. Dagene medregnes med det planlagte antal arbejdstimer. Hvis der ikke er fastsat noget timetal for den pågældende arbejdsdag, medregnes for fuldtidsansatte 7,4 timer og for deltidsansatte og ansatte på plustid et forholdsmæssigt antal timer."*
> → "… The days are counted with the **planned** number of working hours. If no number is fixed for that working day, full-timers are counted with 7.4 hours and part-timers … a proportional number."

*Source:* PAV kap. 19, § 19.2.6, https://pav.medst.dk/pav/kapitel-19-arbejdstidsregler-mv/ (read 28 Oct 2026).

**R1-4. The same rule applied to flex.**
> PAV 19.5.9: *"Registreringssystemerne må suppleres med særskilt indrapportering i de situationer, hvor timer inden for fiks-/flekstiden skal medregnes i den samlede arbejdstidsopgørelse, uden at den ansatte er til stede på arbejdspladsen. Det gælder bl.a. for: … Feriedage/særlige feriedage · Afspadsering · Sygedage · Andet lovligt fravær"*
> → "Registration systems must be supplemented with separate reporting where hours within core/flex time are to be counted in the total working-time account without the employee being present. This applies to, among others: … holiday days, time off in lieu, **sick days**, other lawful absence."
>
> PAV 19.5.13 (*Ferie/sygdom mv.*): *"Kontortiden kan bruges som grundlag for at fastsætte den arbejdstid, der skal medregnes for ferie- og sygedage."*
> → "The office hours (kontortid, fixed hours per weekday) can be used as the basis for setting the working time to be counted for holiday and sick days."

*Source:* same PAV page, §§ 19.5.9 and 19.5.13.

*Local illustration, supporting only.* SDU's 2026 flex agreement, § 2.2 Normtid, reads: *"Ved normtid forstås den ifølge overenskomsterne og ansættelsesforholdet fastsatte arbejdstid, fordelt ligeligt på ugens fem første arbejdsdage. Normtid for en fuldtidsansat er 7,4 timer pr. dag, 37 timer pr. uge. For deltidsansatte fastsættes normtiden forholdsmæssigt."* In English: the norm is spread evenly over the five weekdays, 7.4 h a day for a full-timer, pro rata for a part-timer. This is a local agreement for one institution, not a central rule. PDF p. 3, https://sdunet.dk/-/media/sdunet/filer/servicesider/hr/ansaettelsesforhold/flekstidsaftale-for-sdu-2026-underskrevet.pdf.

**R1-5. It is a credit to performed hours, not a cut to the norm.**
- § 7 opens *"Den præsterede arbejdstid opgøres således"* ("performed working time is calculated as follows"). A sick day therefore enters the *performed* side of the account.
- The norm is set by § 4 stk. 2. It is reduced only by days off and weekday public holidays (inherited, S146 V1: *"7,4 timer gange antallet af kalenderdage fratrukket fridage samt eventuelle søgnehelligdage"*, 7.4 h × calendar days minus days off and weekday public holidays).
- **Consequence:** a whole sick day adds that day's norm hours to the performed hours, and the period's surplus or deficit is unchanged.
- **The same mechanism covers time off in lieu.** The remark to § 14 (R5-2) says such time off *"medregnes på afviklingstidspunktet i arbejdstidsopgørelsen som anden afspadsering, jf. § 7, nr. 2"* (is counted when taken, like other time off in lieu, under § 7 nr. 2).

**R1-6. AC: no agreement text; MedSt's guide and the local flex agreement govern.**
- The AC agreement's working-time chapter has no rule for crediting absence. § 13 stk. 1: *"Lønnen efter denne overenskomst forudsætter fuldtidsbeskæftigelse svarende til gennemsnitlig 37 timer ugentlig."* (Pay assumes full-time work of 37 hours a week on average.) § 13 stk. 5: *"Der kan lokalt indgås aftale om indførelse af flekstidsordninger."* (Flex schemes may be agreed locally.) AC staff keep full pay during sickness (§ 17).
- *Source:* Cirkulære om overenskomst for akademikere i staten, CIR1H nr 9333 af 11/04/2025 (status *Gældende*, in force, printed 28 Oct 2026), §§ 13, 17, PDF pp. 21–22, 27, https://www.retsinformation.dk/eli/retsinfo/2025/9333/pdf.
- **MedSt's guide fills the gap.** PAV 19.3.1 covers agreements outside the state working-time agreement: *"Nedenfor omtales nogle arbejdstidsregler, som kan forekomme på den del af overenskomstområdet, der ikke følger statens arbejdstidsregler. Endvidere gennemgås en række særlige regler for akademikere …"* (some working-time rules that can occur on the part of the agreement area that does not follow the state rules; and special rules for academics). Under that heading, PAV 19.3.1.6 says:
  > *"Ved opgørelse af arbejdstiden medregnes den tjeneste, der i normperioden skulle være udført på sygedage, barns 1. og 2. sygedag, søgnehelligdage, feriedage, særlige feriedage, opsparingsdage, omsorgsdage, afspadseringsdage o.l."*
  > → "In the working-time account, the duty that should have been performed in the norm period on sick days, a child's 1st and 2nd sick day, weekday public holidays, holiday days, special holiday days, saved days, care days, days of time off in lieu and the like is counted."
- PAV 19.5 (flex, R1-4) applies to state flex schemes in general.
- **Strength:** for AC this is MedSt's guidance, not agreement text. AC staff employed as civil servants fall under the state working-time agreement directly (§ 1: *"Aftalen gælder for tjenestemænd i staten"*), so § 7 applies to them.

**R1-7. A child's sick day is paid absence, so it is credited the same way.**
- The agreement text: *"Stk. 2. Den ansatte får under tjenestefriheden en løn, der svarer til den løn, som den ansatte ville have fået under sygdom."* (During the leave the employee is paid what they would have received when sick.)
- It is granted as *"hel eller delvis tjenestefrihed"* (full or partial leave of absence), so a part-day is allowed.
- The § 7 nr. 2 remark lists *"tjenestefrihed med løn"* (paid leave) as paid absence (R1-1), and PAV 19.3.1.6 names *"barns 1. og 2. sygedag"* (R1-6).
- *Sources:* OAO-S fællesoverenskomst 029-24 § 14 stk. 2, PDF p. 17, https://cirkulaere.medst.dk/media/1380/029-24.pdf. The same wording is in 021-26 § 14 stk. 2 and 020-26 § 14 stk. 2 (below), and in AC CIR1H 9333 § 18 stk. 6, PDF p. 28.

**R1-8. The child's-sick-day entitlement by agreement and OK version.**

| | OK24 (to 31 Mar 2026) | OK26 (from 1 Apr 2026) |
|---|---|---|
| **HK** (OAO-S fællesoverenskomst) | Day 1 and day 2. 029-24 § 14 (*Cirkulære af 5. juli 2024*): *"… hel eller delvis tjenestefrihed til pasning af et sygt barn på barnets 1. og 2. sygedag, når: 1. hensynet til barnets forhold gør dette nødvendigt 2. forholdene på tjenestestedet tillader det 3. barnet er under 18 år og 4. barnet er hjemmeværende"* (PDF p. 17) | Days 1, 2 and 3, plus the rest of the day. 021-26 § 14 *Barns sygdom (fælles regelsæt)* (*Cirkulære af 18. juni 2026*; the cover misprints "2029"), PDF p. 17, https://cirkulaere.medst.dk/media/dgge2q3l/021-26.pdf: *"… tjenestefrihed til pasning af et sygt barn på barnets 1., 2. og 3. sygedag … Bliver barnet sygt i løbet af den ansattes arbejdsdag, kan den ansatte endvidere holde fri med løn i de resterende arbejdstimer den pågældende dag, hvis betingelserne i pkt.1-4 er opfyldt."* In English: day 1, 2 and 3; and if the child falls ill during the working day, the employee may also take the remaining working hours that day off with pay. General remark 3 (PDF p. 5): *"Fra den 1. april 2026 er muligheden for hel eller delvis tjenestefrihed til pasning af sygt barn udvidet."* (From 1 April 2026 the option is extended.) |
| **PROSA** (**LC/CO10** fællesoverenskomst, not OAO-S: 019-26's data sheet names *"Fællesoverenskomst mellem Skatteministeriet og Lærernes Centralorganisation og CO10"*) | Day 1 and day 2. Cirkulære af 6. februar 2025, Medst.nr. 012-25, § 14, PDF p. 17. Read from a copy of the MedSt circular hosted at https://kost.dk/sites/default/files/2025-03/OK24%20STAT%20-%20Cirkul%C3%A6re%20om%20F%C3%A6llesoverenskomst%20mellem%20Finansministeriet%20og%20LC%20og%20CO10.pdf | Days 1, 2 and 3, plus the rest of the day. Cirkulære af 18. juni 2026, Medst.nr. 020-26, § 14, PDF p. 18, https://cirkulaere.medst.dk/media/qimf0r1n/020-26.pdf. Same wording as 021-26. General remark 3, PDF p. 5: same sentence as 021-26 |
| **AC** | Day 1 and day 2. CIR1H 9333 § 18 stk. 6: *"Ansættelsesmyndigheden kan give den ansatte hel eller delvis tjenestefrihed til pasning af et sygt barn på barnets 1. og 2. sygedag, når: …"* (PDF p. 27) | **Agreed, but the circular is not yet issued.** AC settlement, item 4: *"Parterne er derfor enige om at give mulighed for fravær i forbindelse med barnets 3. sygedag og på hjemkaldelsesdagen."* (The parties agree to allow absence on the child's 3rd sick day and on the day the child falls ill.) Effect date: *"Ændringerne har virkning fra den 1. april 2026, medmindre andet fremgår af forhandlingsresultatet."* (In force from 1 April 2026 unless the result says otherwise; item 4 says nothing else.) *Resultatet af forhandlingerne mellem finansministeren og Akademikerne*, February 2026, PDF pp. 4–6, https://medst.dk/media/0rmlepno/forlig-ok26-medst-ac.pdf. **Status:** no AC agreement circular appears in MedSt's list up to 27 Oct 2026, and CIR1H 9333 (OK24 text) is still *Gældende* |
| **Cross-check** | — | MedSt's *Cirkulære om tjenestefrihed af familiemæssige årsager*, Cirkulære af 25. juni 2026, Medst.nr. 027-26, general remark 1, PDF p. 5, https://cirkulaere.medst.dk/media/twmplgln/027-26.pdf: *"Reglerne om frihed på barns 1., 2. og 3 sygedag for overenskomstansatte fremgår af fællesoverenskomsterne og Akademikeroverenskomsten."* (For contract staff, the rules on days 1–3 are in the joint agreements and the AC agreement.) The CFU settlement has the same item 4 (PDF p. 6, https://medst.dk/media/oo1auuiu/ok26-forlig-medst-cfu.pdf) |

The day-count rule, from the remarks to 021-26 and 020-26 § 14 (and 027-26):
- *"Hvis barnet bliver syg, efter den ansatte er mødt på arbejde, vil denne dag udgøre hjemkaldelsesdagen. Barnets 1. sygedag er den første hele sygedag. Den 2. sygedag er den kalenderdag, der ligger i umiddelbar forlængelse af 1. sygedag, og den 3. sygedag er den kalenderdag, der ligger i umiddelbar forlængelse af 2. sygedag, uanset om det er en arbejdsdag eller en fridag."*
- In English: the call-home day is separate. Day 1 is the first **whole** sick day. Days 2 and 3 are the following **calendar** days, whether working days or not.

**R1-9. The call-home day: crediting the rest of the day (an inference for the arithmetic).**
- The text grants *"fri med løn i de resterende arbejdstimer den pågældende dag"* (paid time off for the remaining working hours that day).
- Read with § 7: the hours worked count under § 7 nr. 1, and the remaining scheduled hours count as paid absence under § 7 nr. 2. Together the day totals the day's planned hours.
- This is my reading of how the two provisions combine. No text states the sum.

### Not settled by a central text (outside the verdict)

**Own sickness for part of a day** (working in the morning, going home sick).
- No central text splits such a day. PAV 24.3.1.1 says only that a partly sick employee keeps their usual pay (*"har derfor ret til sædvanlig løn under det delvise sygefravær"*), https://pav.medst.dk/pav/kapitel-24-sygdom/.
- **Reading by analogy (MEDIUM confidence):** worked hours under § 7 nr. 1, plus the rest of that day's planned hours as paid absence under § 7 nr. 2. The day then totals the day's norm, never more.
- Local flex agreements may say otherwise. One university agreement found by search says a sick day counts 7.4 h "også … såfremt man er nødsaget til at gå hjem" (also when one has to go home). That document has moved and could not be read, so it is not relied on.

### Contrary evidence searched for

- **Searched for:** any state text crediting the *registered* hours, or zero, for a sick day under flex. **Found:** none.
- HK's state working-time page (undated, secondary) describes working time as including *"alt fravær med løn"* (all paid absence), https://www.hk.dk/raadogstoette/arbejdstid/arbejdstid-stat. That is consistent with the verdict.
- **One local variation:** where a local flex agreement fixes **office hours per weekday** (PAV 19.5.13), a sick day is credited with *that weekday's* office hours, not a flat 7.4 h. For example, if Friday's office hours are shorter, a Friday sick day is credited with fewer hours. This is a local-agreement parameter (see "Found in passing", P-7).

**Verdict: CONFIRMED.** A paid sick day, and a paid child's sick day, is credited to performed hours with the day's planned hours. With no planned hours, as under flex, that is 7.4 h, pro rata for part-timers. The credit is never the registered hours. The source is 022-26 / 014-21 § 7 nr. 2 and its remark, which is agreement text for HK and PROSA and the same in OK24 and OK26. For AC the source is PAV 19.3.1.6 and 19.5.13. The child's-sick-day entitlement differs by OK version (R1-8).

---

## R2 — May særlige feriedage be taken in fractions of days? (re-verification of ADR-032 D3, `ADR-032:37`)

### Plain answer

Yes. The state's holiday agreement has said so expressly since at least 2019, and the agreement in force today, from 1 April 2024, still does. ADR-032 quoted the joint guide's wording (*"samlet, enkeltvis og som brøkdele af dage"*, all at once, singly, or as fractions of days). The agreement's own wording is *"som enkeltdage eller som brøkdele af dage"* (as single days or as fractions of days). Both allow part-days.

How much of a day a part-day uses is set by the guide's Example 3.5: the hours taken off divided by that day's scheduled hours. That matches ADR-032 D1.

### Claims and quotes

**R2-1. The agreement in force.**
> § 13 stk. 2: *"Afvikling kan ske som enkeltdage eller som brøkdele af dage."*
> → "The days may be taken as single days or as fractions of days."

*Source:* Cirkulære om aftale om ferie, Cirkulære af 17. maj 2024, Medst.nr. 021-24, § 13 stk. 2, PDF p. 11, https://cirkulaere.medst.dk/media/1372/021-24.pdf. On Retsinformation it is CIR1H nr 9298 af 17/05/2024, status *Gældende* (in force), printed 28 Oct 2026, PDF p. 6, https://www.retsinformation.dk/eli/retsinfo/2024/9298/pdf.

The general remarks name the agreement: *"vedlagte aftale om ferie af 15. maj 2024 … Aftalen har virkning fra 1. april 2024 og erstatter aftale om ferie af 16. december 2019."* (The holiday agreement of 15 May 2024, in force from 1 April 2024, replaces the agreement of 16 December 2019.) PDF p. 2 (9298).

Chapter 4 (§§ 12–19) is the special-days chapter. § 12 grants 0.42 days a month, *"svarende til 5 dage pr. år"* (equal to 5 days a year).

**R2-2. Who it covers.**
> § 1: *"Aftalen gælder for tjenestemænd og tjenestemandslignende ansatte samt for ansatte omfattet af overenskomster indgået på den ene side af eller efter bemyndigelse fra Finansministeriet, Medarbejder- og Kompetencestyrelsen, og på den anden side af de underskrivende centralorganisationer eller organisationer, der er tilsluttet disse."*

In English, it covers civil servants and staff under agreements made by the Finance Ministry with the signing central organisations. That includes AC, HK and PROSA. PAV 23.1 describes the same scope as *"overenskomstansatte omfattet af CFU's forhandlingsret"* (contract staff under CFU's bargaining right), https://pav.medst.dk/pav/kapitel-23-ferie/.

**R2-3. The predecessor (2019) and ADR-032's own quote.**
- The joint guide, *Ferievejledning* (MedSt and CFU, December 2019), § 3.7.2 *Hvordan kan de afvikles?*, PDF p. 34, https://medst.dk/media/kp4fffy1/ferievejledning-2019.pdf:
  > *"Særlige feriedage kan holdes samlet, enkeltvis og som brøkdele af dage."* followed by *"Eksempel 3.5 En medarbejder har følgende arbejdstid: Mandag, onsdag, torsdag og fredag 6 timer daglig, og tirsdag 9 timer daglig. 3 timers frihed udgør 1/2 særlig feriedag, hvis timerne lægges på en mandag, onsdag, torsdag eller fredag, men derimod 1/3 særlig feriedag, hvis der er tale om en tirsdag."*
  > → Special days may be taken all at once, singly or as fractions of days. Example: on a 6-hour day, 3 hours off = ½ day; on a 9-hour Tuesday, 3 hours off = ⅓ day.
- The 2019 agreement reprinted in the guide's annex has the identical § 13 stk. 2, *"Afvikling kan ske som enkeltdage eller som brøkdele af dage."* (PDF p. 88).
- PAV 23.2 still names the December 2019 guide as the current guide.

**R2-4. Nothing newer replaces it.**
- No ferie circular appears in MedSt's list from 15 May 2025 to 27 Oct 2026.
- Neither OK26 settlement (CFU or AC) amends § 13. A full-text search of both for "ferie" finds only the item on the future Fritvalgs Lønkonto (R2-5).

**R2-5. Forward watch, not a contradiction.**
- OK26 agrees *"at etablere en Fritvalgs Lønkonto pr. 1. januar 2028, jf. bilag D"*, a flexible pay account from 1 January 2028. It refers to *"afholdelse af fritvalgsdag, seniordage og særlige feriedage, hvor den ansatte kan få udbetalt midler fra sin Fritvalgs Lønkonto"* (taking a free-choice day, senior days and special holiday days, where the employee can draw money from the account). CFU settlement item 3, PDF pp. 5–6.
- The annexes (bilag D–G) are not in the published settlement PDF, and no implementing circular has been issued.
- A **secondary** source, OAO's FAQ (April 2026, https://www.djf.dk/wp-content/uploads/2026/04/OAO-FAQ-Fritvalgskonto.pdf), describes that 2028 model as follows: *"De særlige feriedage og fritvalgsdagen kan du holde som timer eller som hele dage. Seniordagene kan kun holdes som hele dage."* (Special days and the free-choice day may be taken as hours or whole days; senior days only as whole days.) It also says the days will then be taken *"med løntræk"* (with a pay deduction), funded from the account. On OAO's reading, fractional taking survives 2028, but the money mechanics change.

### Contrary evidence searched for

- **Searched for:** any state text limiting særlige feriedage to whole or half days. **Found:** none.
- The "whole or half days where possible" wording in 022-26 applies to time off in lieu (afspadsering) under §§ 14 and 16. It does not apply to særlige feriedage.

**Verdict: CONFIRMED.** ADR-032 D3's "brøkdele af dage" claim holds. Source: Aftale om ferie of 15 May 2024 (Medst. 021-24 / CIR1H 9298, in force), § 13 stk. 2; it continues the 2019 agreement and guide (§ 3.7.2, Example 3.5).

---

## R3 — On-call standby, call-in and non-working travel

### Plain answer

**HK and PROSA (the state working-time agreement, § 7):** each of the three counts toward the hours performed, by a fixed rule.

| Item | Counts toward performed hours? | How much | Extra pay on top |
|---|---|---|---|
| **Travel on an official trip (*tjenesterejse*)** | Yes | In full, at most 13 h per working day, a 24-hour window counted from the start of that working day. Travelling straight from home to a place other than the normal workplace counts only the time beyond the normal commute | The 25 % evening/weekend supplement (§ 13) is paid on the **actual** travel hours in its window, not capped at 13 (PAV) |
| **On-call at home (*rådighedstjeneste i hjemmet*)** | Partly | **1/3** of the on-call hours | No § 13 supplement. On a weekend or weekday holiday, the § 14 +50 % applies to the counted third (PAV example: 9 h at home → 3 h + 50 %) |
| **On-call at the workplace** | Partly | **3/4** of the on-call hours | § 13 supplement on the counted hours (PAV: 8 h → 6 h). § 14 on weekends |
| **Call-in at under 24 h notice (*tilkald*)** | Yes | **At least 3 h.** Not when the work is next to normal hours, or when the call-in happens during on-call. Only if the person must come to the workplace; work from home counts the actual time | The § 13 supplement is paid on actual hours, not the 3-hour minimum (PAV) |

"On-call's rate" in the central agreement is therefore the **fraction** (1/3 or 3/4) by which on-call hours enter the month's working-time account. Those hours can then become overtime (+50 %) in the ordinary way. There is no kroner rate in the central text. PAV advises a local agreement (a smaller fraction or a fixed supplement) where on-call is permanent. HK's and PROSA's organisation agreements add nothing.

**AC:**
- **Travel** counts, at most 13 h per day, unless agreed locally.
- **On-call and call-in:** the AC agreement has no general rule. On-call pay exists only for named areas in Bilag 6, and when the employer assesses claimed extra work it may discount hours that were on-call rather than effective work.

**A separate question.** For the EU working-time limits (the 48-hour average and the 11-hour rest rule) a different definition applies, and PAV treats on-call at home as rest for the 11-hour rule unless the on-call is so demanding that the rest is lost. StatsTid's rest check therefore needs its own classification, not § 7's fractions.

### Claims and quotes

**R3-1. The agreement text** (identical in OK24's 014-21 and OK26's 022-26, PDF pp. 14–15 in both; CIR1H 9551 PDF p. 9):
> § 7: *"3. Rejsetid i forbindelse med tjenesterejser medregnes, dog højst med 13 timer pr. døgn. 4. Rådighedstjeneste i hjemmet medregnes med 1/3 og rådighedstjeneste på arbejdsstedet med 3/4. 5. Tilkald med mindre end 24 timers varsel medregnes med mindst 3 timer. Det gælder dog ikke, hvis arbejdet ligger i umiddelbar tilknytning til det normale arbejde, eller tilkaldet sker som led i rådighedstjeneste."*
> → "3. Travel time on official trips is counted, but at most 13 hours per døgn. 4. On-call duty at home is counted at 1/3 and on-call duty at the workplace at 3/4. 5. A call-in at less than 24 hours' notice is counted at a minimum of 3 hours, except where the work is directly connected with normal work or the call-in happens as part of on-call duty."
>
> Remark to nr. 3: *"Rejsetiden medregnes fuldt ud for rejser i såvel ind- som udland, men højst med 13 timer pr. døgn. Begrænsningen på de 13 timer gælder kun for selve rejsetiden. Ved døgn forstås i denne sammenhæng arbejdsdøgnet, dvs. 24 timer regnet fra den pågældende arbejdsdags begyndelse."*
> → Travel counts in full at home and abroad, max 13 h per døgn. The cap applies to the travel time only. A døgn here is the working døgn: 24 hours from the start of that working day.
>
> Remark to nr. 5: *"Ved ”tilkald” forstås, at den ansatte skal møde op på arbejdsstedet. Hvis arbejdet udføres fra den ansattes bopæl, er det derfor kun den faktiske arbejdstid, der medregnes."*
> → A call-in means having to come to the workplace. Work done from home counts only the actual time.

**R3-2. MedSt's elaboration** (PAV 19.2.6, same PAV URL):
- **Travel to another workplace:** *"Ved rejser direkte mellem den ansattes bopæl og andet arbejdssted end det normale medregnes kun den tid, der ligger ud over transporttiden mellem hjemmet og det normale arbejdssted, dvs. merrejsetiden."* (Travelling straight between home and a workplace other than the usual one counts only the time beyond the usual commute.)
- **Worked examples:** Example 5 counts 6 h of work plus 8 h of travel as 14 h. Example 6 counts 2 h of work plus 14 h of travel as 2 + 13 = 15 h.
- **On-call defined:** *"Rådighedstjeneste, dvs. arbejdstid hvor den ansatte ikke udfører almindelig tjeneste, men alene skal stå til rådighed med henblik på tilkald, medregnes kun delvis. … Hvis der er tale om en mere permanent rådighedsordning, bør der overvejes at indgå en særlig aftale om godtgørelse af rådighedstjenesten. Det kan fx aftales, at rådighedstjenesten medregnes med en mindre brøkdel, eller at der ydes et særligt tillæg, i stedet for at tjenesten medregnes i arbejdstiden."* (On-call, where the person only stands ready to be called in, counts only partly. For a more permanent arrangement, a special agreement should be considered: a smaller fraction, or a special supplement instead of counting the hours.)
- **On-call during a trip:** on-call where the person must stay at a place the employer names counts as workplace on-call. Otherwise it counts as home on-call.
- **Repeated call-ins:** *"Hvis den ansatte tilkaldes flere gange inden for en 3-timers periode, medregnes kun 3 timer i alt."* (Several call-ins within a 3-hour period count 3 hours in total.)

**R3-3. Weekend on-call for day workers** (PAV 19.2.8.1):
> *"Tillægget ydes også, hvis der pålægges den ansatte rådighedstjeneste i weekenden. Det gælder, hvad enten der er tale om rådighedstjeneste på arbejdsstedet eller i hjemmet. En rådighedstjeneste i hjemmet af 9 timers varighed udløser således en godtgørelse på 1/3 af 9 timer = 3 timer med tillæg af 50 pct."*
> → The § 14 weekend supplement also applies to on-call at the weekend, at the workplace or at home. A 9-hour home on-call gives 1/3 × 9 = 3 hours plus 50 %.

**R3-4. The § 13 supplement on travel, call-in and on-call** (PAV 19.2.9.1):
> *"Ulempetillægget efter aftalens § 13 ydes for den faktisk præsterede arbejdstid, uafhængigt af om der er fastsat et maksimum eller et minimum for medregning af tjenesten i arbejdstiden, som det fx er tilfældet for rejsetid (maks. 13 timer) og for tilkald (min. 3 timer). Der ydes ikke godtgørelse efter aftalens § 13 for rådighedstjeneste i hjemmet. For rådighedstjeneste på arbejdsstedet ydes godtgørelse på grundlag af det antal timer, der indgår i arbejdstiden. En rådighedsvagt på 8 timer udløser således ulempetillæg for 6 timer."*
> → The 25 % supplement is paid on the hours actually performed, ignoring the 13-hour travel maximum and the 3-hour call-in minimum. Nothing for home on-call. Workplace on-call earns it on the counted hours: an 8-hour on-call earns it for 6 hours.

**R3-5. Shift workers** (chapter 4; outside StatsTid's day-worker profile; noted for completeness):
- PAV 19.2.8.2: *"En fridag anses også for inddraget, hvis der pålægges en ansat rådighedstjeneste."* (A day off counts as withdrawn if on-call is imposed on it.)
- PAV 19.2.9.2: *"Der optjenes 37/3-frihed i forbindelse med rådighedsvagter, i samme omfang som timerne medregnes i arbejdstidsopgørelsen, jf. arbejdstidsaftalens § 7."* (Night time off, 3 hours per 37, accrues on on-call to the extent the hours are counted under § 7.)

**R3-6. HK and PROSA add nothing.**
- A full-text search of 085-25 (HK) and 019-26 (PROSA) for *rådighed*, *tilkald*, *rejsetid* and *vagt* finds only *"rådighedstillæg"*. That is an allowance excluded from the 1/1924 hourly-rate base (HK § 38 stk. 4; PROSA § 3 stk. 5).
- HK Bilag 9 (laboratory technicians at sea or on fieldwork) has its own working-time set, flagged in S146 C2 and not re-read here.

**R3-7. AC.**
- **Travel.** CIR1H 9333 § 13 stk. 6–7 (PDF p. 22):
  > *"Stk. 6. Rejser, der udføres som en del af tjenesten, medregnes i arbejdstiden, dog højest med 13 timer pr. døgn, medmindre andet aftales lokalt. Stk. 7. Tjeneste på det fremmede tjenestested medregnes efter de almindelige regler for opgørelse af arbejdstid."*
  > → Official travel counts, at most 13 hours per døgn unless agreed locally. Duty at the other workplace counts under the general rules.

  PAV 19.3.4.7 adds the same commute rule (*merrejsetiden*).
- **No general on-call or call-in rule.**
  - The AC circular contains the word *"tilkald"* **0** times.
  - PAV 19.3.4: *"Arbejdstid som begreb er ikke nærmere fastlagt i overenskomsten. … der betales ikke ulempegodtgørelse, natpenge mv. Overenskomstens bilag 6 indeholder dog visse bestemmelser om særskilt betaling for rådighedsvagt og weekendtjeneste mv. for specifikke områder."* (Working time is not defined in the agreement; no unsocial-hours or night pay; Bilag 6 has separate on-call and weekend pay for specific areas.)
  - Example from Bilag 6 (PDF p. 68): *"Der ydes betaling for deltagelse i rådighedsvagt for ansatte i Energistyrelsen i henhold til aftale af 23. september 2008 …"* (payment for on-call at the Danish Energy Agency under a 2008 agreement).
  - The remark to § 14 (PDF p. 25): *"Ved udøvelsen af skønnet over et udført merarbejde kan der tages hensyn til, at en del af det opgivne bruttotimetal ikke er udført i form af effektiv tjeneste, men som rådighedstjeneste og lign."* (When assessing claimed extra work, the employer may take into account that part of the hours was on-call rather than effective duty.)
- **Do not confuse with the *rådighedstillæg*.** The AC *rådighedstillæg / rådighedsforpligtelse* of fuldmægtige (case officers) is an allowance for a duty to do **up to 20 h of extra work a quarter** (PAV 19.3.4.4). It is not on-call standby.

**R3-8. The EU working-time definition is a different question.**
- 022-26 Bilag 1 (*Aftale om visse aspekter i forbindelse med tilrettelæggelse af arbejdstiden*), § 2 nr. 1, PDF p. 28. The same text is in 014-21's Bilag 1:
  > *"”arbejdstid” som det tidsrum, hvori den ansatte er på arbejde og står til rådighed for arbejdsgiveren, herunder pauser, der medregnes i arbejdstiden"*
  > → For the directive's purposes, working time is the time at work and at the employer's disposal.
- PAV 19.7.1: *"Rådighedstjeneste i hjemmet, men ikke på arbejdsstedet, betragtes i denne relation som hviletid med mindre rådighedstjenesten har en sådan karakter, at formålet med hvileperioden forspildes. Rejsetid til og fra et andet arbejdssted end det faste betragtes ikke som hvileperiode, i det omfang den overstiger den ansattes normale rejsetid til arbejdsstedet. Tjenstlig rejsetid i øvrigt betragtes ikke som hviletid."*
  → For the 11-hour rest rule, home on-call (not workplace on-call) counts as rest unless it defeats the purpose of rest. Travel to another workplace beyond the normal commute is not rest, and official travel otherwise is not rest.
- I did not research EU case law. This claim rests on the two state texts quoted.

**R3-9. OK24 vs OK26.** § 7 is word-for-word identical (R1-1). CFU's published OK24 demands included widening the call-in concept (*"Udvidelse af tilkaldebegreb i statens arbejdstidsaftale"*, from a CFU demand list, a secondary report). The 2026 text shows it was not adopted.

### Contrary evidence searched for

- **Searched for:**
  - a central state kroner rate for on-call: none;
  - an HK or PROSA on-call clause: none (R3-6);
  - any state text counting home on-call in full: none;
  - an AC call-in rule: none.

**Verdict: CONFIRMED.** For HK and PROSA, statens arbejdstidsaftale § 7 nr. 3–5 and its remarks (identical in 014-21 and 022-26) answer all three items and give on-call's rate as a fraction: 1/3 at home, 3/4 at the workplace. MedSt's PAV 19.2.6, 19.2.8.1 and 19.2.9.1 give the supplements. For AC, § 13 stk. 6 covers travel. The AC agreement has no general on-call or call-in rule; on-call pay exists only for the Bilag 6 areas (PAV 19.3.4).

---

## R4 — Has the implementing text for the 4 February 2026 part-timer award appeared?

### Plain answer

No.
- **What has not appeared:** the state has published no agreement or circular implementing the award. On 4 Feb 2026 a labour arbitration ruled that part-timers must get overtime pay for hours above their **own** contracted hours, not only above full time.
- **What the official text still says:** the working-time circular reissued on 18 June 2026 still prints the old rule (hours up to full time paid 1:1). MedSt's own guide repeats it.
- **The latest dated outside statement:** a teachers'-union page, updated after 4 Sep 2026, says the state agreement is "not yet in place".

D-10 therefore stays a provisional owner default, as S146 ruled.

### Claims and quotes

**R4-1. MedSt's circular list shows no implementing circular** (read 28 Oct 2026, pages 1–10, https://cirkulaere.medst.dk/love-regler-og-aftaler/cirkulaerer/?pageNumber=1 … 10).
- The newest entry is 068-26, *Cirkulære om aftale om nyt lønsystem for tjenestemænd … (TAT)*, published **27.10.2026**. The oldest on page 10 is 088-25 (15.05.2025). 97 entries in all.
- None concerns part-timers' overtime or merarbejde (extra work) or the award.
- The 2026 working-time circulars are:
  - 022-26 (Statens arbejdstidsaftale, 18.06.2026);
  - 023-26 (*visse aspekter …*, 19.06.2026);
  - teacher-specific texts 026-26, 040-26 and 065-26.
- Compared with S146's check of 23 Oct 2026, the only new entry is 068-26.

**R4-2. The working-time text in force still carries the old rule.**
> 022-26 § 8 stk. 2: *"Timer, der ikke overstiger arbejdstiden for fuldtidsbeskæftigelse, jf. § 4, stk. 2, godtgøres dog kun med afspadsering af samme varighed eller med almindelig timeløn."*
> → "Hours not exceeding full-time working time are, however, compensated only with time off of the same length or with ordinary hourly pay."

Cirkulære af 18. juni 2026, Medst.nr. 022-26, PDF p. 15. Its introduction lists the only OK26 changes: *"Ved overenskomstforhandlingerne i 2026 blev det aftalt at ændre i reglerne for opsparing af tilgodehavende afspadsering, så der fremover ikke kan fastsættes et maksimum på mindre end 111 timer."* (At OK26 only the time-off savings maximum changed, to at least 111 h.) It adds Greenland's Bilag 2. PDF p. 5.

**R4-3. MedSt's guide still carries the old rule** (PAV kap. 19, read 28 Oct 2026):
- 19.2.7.1: *"For deltidsansatte ydes tillægget på 50 pct. dog kun for de timer, der overstiger den normale arbejdstid ved fuldtidsbeskæftigelse."* (For part-timers the 50 % supplement is paid only above full-time hours.)
- 19.3.1.6: *"For deltidsansatte er arbejdstidsnormen i forbindelse med overarbejde fuldtidsnormen. Indtil denne er opfyldt, ydes alene almindelig kvotatimeløn for merarbejdet."* (For part-timers the overtime norm is the full-time norm; until it is reached, only ordinary pro-rata pay.)

**R4-4. The mandate to implement**, the same text in both OK26 settlements: CFU item 28, PDF p. 17, https://medst.dk/media/oo1auuiu/ok26-forlig-medst-cfu.pdf; AC item 16, PDF p. 13, https://medst.dk/media/0rmlepno/forlig-ok26-medst-ac.pdf.
> *"Der afsættes i alt 12 mio. kr. Midlerne dækker udgifter til en efterfølgende forhandling og aftale om implementering af voldgiftsrettens dom af 4. februar 2026 på statens område. … Forhandlinger optages senest 1. maj 2026. Parterne aftaler senest 1. april 2026 en proces for håndtering af krav om efterbetaling som følge af resultatet af kendelsen 4. februar 2026."*
> → 12 million kroner for a later negotiation and agreement implementing the award in the state. Negotiations to open by 1 May 2026. By 1 April 2026 the parties agree a process for back-pay claims.

**R4-5. The OK26 joint agreements are silent on it.**
- The OAO-S (021-26) and LC/CO10 (020-26) joint agreements, both of 18 June 2026, list their OK26 changes in general remark 3: wider child-sickness leave, the 111-hour floor, and (OAO-S) monthly pay from 1 April 2027. Nothing on part-timers' overtime.
- PROSA 019-26 § 13 *Deltid* contains only a rule on offering hours to existing part-timers before hiring.

**R4-6. MedSt's news archive is silent too** (https://medst.dk/nyheder/nyhedsarkiv/, pages 1–8, back to 27 Nov 2025).
- Apart from circular announcements, the 2026 items are:
  - 11.02.2026: the OK26 settlement;
  - 13.04.2026: the organisation negotiation results;
  - 06.05.2026: *"Statens overenskomster tiltrådt"* (agreements ratified);
  - 07.05.2026 and 01.06.2026: regulation percentages.
- No item concerns the award or its implementation.

**R4-7. The latest dated outside statements (secondary; status only).**
- **Danmarks Lærerforening** (DLF, the teachers' union), page undated but citing the 4 Sep 2026 limitation award, so updated after that date: *"Danmarks Lærerforening forventer, at der vil blive indgået en lignende aftale for deltidsansatte i staten."* and *"Aftalen er dog ikke på plads endnu."* (DLF expects a similar agreement for state part-timers; it is not yet in place.) https://www.dlf.org/loen-og-vilkaar/arbejdstid/deltidsansatte/
- **HK**, page undated, unchanged since S146: *"vil reglerne om overtidsbetaling for deltidsansatte blive ændret i løbet af 2026 i samarbejde med Medarbejder- og Kompetencestyrelsen"* (the rules will change during 2026 together with MedSt). https://www.hk.dk/raadogstoette/arbejdstid/arbejdstid-stat

**R4-8. A citation trap.**
- A later award, **FV2026.573 of 4 Sep 2026**, held that back-pay claims are subject to the general **five-year** limitation period. Dansk Erhverv (a private employers' organisation) reported it on 10 Sep 2026.
- The report places the test case in *"industriens område"* (manufacturing industry), the private sector, and does not mention the state. https://www.danskerhverv.dk/presse-og-nyheder/nyheder/2026/september/krav-om-overtidsbetaling-til-deltidsansatte-foraldes-efter-5-ar--ikke-10-ar/
- It is not a state rule. I did not read the award itself.

**R4-9. Inherited, not re-read.** The award's operative part binding MedSt (FV2025-795, 4 Feb 2026, back to 20 Jan 2000) and its Annex A naming 022-26 § 8 stk. 2: S146 C4, verified. https://arbejdsretten.dk/media/bork3lzb/fv2025-795.pdf

### Searched and not found

- MedSt's circular list and news archive (R4-1, R4-6).
- Web searches for a state agreement, circular or *administrationsgrundlag* (an employer's administrative basis) on part-timers' overtime dated May–Oct 2026. Only municipal or regional texts (KL, Albertslund) and private-sector items turned up.
- A search summary attributed to MedSt the statement that the state's model "awaits a new government". I could not trace this to any page, so it is **not relied on**.

**Verdict: CONFIRMED** that the implementing text **has not appeared** as of 28 Oct 2026.
- The latest dated primary evidence is MedSt's circular list through 068-26 (27.10.2026).
- The latest dated text in force still carries the old rule: 022-26 of 18 Jun 2026, § 8 stk. 2.
- The latest dated secondary statement is DLF's "ikke på plads endnu", from after 4 Sep 2026.

---

## R5 — The OK24 texts of §§ 13 and 14

### Plain answer

ADR-043 deferred encoding supplements for OK24 periods until the OK24 texts of §§ 13–14 were checked. They are now checked.
- **Which text governed OK24:** the working-time circular in force for the whole OK24 period (1 April 2024 – 31 March 2026) was **014-21 of 5 July 2021**.
- **What its §§ 13–14 say:** the same as OK26's 022-26, word for word, remarks included.
- **What that means for StatsTid:** the single +25 % supplement window and the § 14 weekend +50 % can be encoded once, valid for OK24 and OK26 periods alike.
- The 32.77 kr. minimum is a 2012-level amount that is indexed. StatsTid exports hours, so it does not need the kroner figure.

### Claims and quotes

**R5-1. The document and its validity.**
- **Title and ids:** *Cirkulære om statens arbejdstidsaftale*, Cirkulære af 5. juli 2021, Medst.nr. 014-21, J.nr. 2020-490, https://cirkulaere.medst.dk/media/1191/014-21.pdf. On Retsinformation it is CIR1H nr 9551 af 05/07/2021, https://www.retsinformation.dk/api/pdf/224517.
- **Commencement:** *"Cirkulæret træder i kraft den 5. juli 2021 og har virkning fra den 1. april 2021."* (PDF p. 7.)
- **The annexed agreement** is dated 10 October 2019 (*"Skatteministeriet og centralorganisationerne har den 10. oktober 2019 indgået vedlagte aftale"*). Its § 18 says *"Aftalen har virkning fra 1. april 2018."*
- **The end of its validity:**
  - Retsinformation's print of 28 Oct 2026 shows *"CIR1H nr 9551 af 05/07/2021 (Historisk)"* with one later change only: *"Senere ændringer til forskriften: CIR1H nr 9594 af 18/06/2026"*.
  - That is 022-26, whose commencement clause reads: *"Cirkulæret træder i kraft den 18. juni 2026 og har virkning fra den 1. april 2026. Samtidig ophæves Skatteministeriets cirkulære af 5. juni 2021 om statens arbejdstidsaftale (Medst. nr. 014-21)."* (022-26 PDF p. 8.) Note the misprint "5. juni"; the repealed circular is dated 5 July 2021.
- **Conclusion:** 014-21 was the text in force from 1 April 2021 to 31 March 2026, which covers the whole OK24 period.

**R5-2. §§ 13 and 14 in full, with their remarks.** From 014-21 PDF pp. 20–21 (CIR1H 9551 PDF pp. 14–15). Hyphenation at line ends is removed.

> **Kapitel 3. Særbestemmelser for ansatte, hvis arbejdstid normalt er placeret inden for almindelig dagarbejdstid**
>
> **Ulempegodtgørelse**
>
> **§ 13.** For arbejde i tidsrummet fra kl. 17 til kl. 06 eller i weekender, på søgnehelligdage, grundlovsdag efter kl. 12 samt juleaftensdag efter kl. 14 ydes et tillæg svarende til 25 pct. af nettotimelønnen, inkl. faste tillæg, dog mindst 32,77 kr. (grundbeløb 31. marts 2012). Tillægget træder i stedet for tillæg efter § 1 og § 2 i aftalen om natpenge mv. for tjenestemænd i staten eller tilsvarende godtgørelser efter andre aftaler.
>
> Stk. 2. Ulempegodtgørelsen efter stk. 1 kan efter aftale med ledelsen og den ansatte konverteres til afspadsering.
>
> Stk. 3. Hvis den ansatte er syg, når en planlagt afspadsering begynder, har den pågældende ikke pligt til at påbegynde afspadseringen.
>
> *Cirkulærebemærkninger til § 13:*
> Ulempetillægget ydes for arbejde, der efter ordre eller godkendt tjenestefordeling udføres i følgende tidsrum:
> 1. Hverdage: Fra kl. 17 til kl. 06 (inkl. mandag morgen)
> 2. Weekender: Fra lørdag kl. 00 til søndag kl. 24
> 3. Søgnehelligdage: Fra kl. 00 til kl. 24
> 4. Grundlovsdag: Fra kl. 12 til kl. 24
> 5. Juleaftensdag: Fra kl. 14 til kl. 24.
>
> Der ydes således også tillæg for arbejde i tidsrummet fra kl. 17 til kl. 19 på hverdage.
>
> For ansatte, der er omfattet af aftalen om natpenge mv. for tjenestemænd i staten, træder tillægget i § 13 i stedet for natpengeaftalens tillæg for tjeneste i tidsrummet fra kl. 17 til kl. 06 (natpenge) og tillæg for weekender, helligdage mv.
>
> For ansatte, der er omfattet af aftalen om tillæg for aften- og natarbejde for ansatte inden for CO II’s forhandlingsområde, træder tillægget i § 13 i stedet for tillægget efter den nævnte aftales § 2 samt tillægget for weekender, helligdage mv. efter natpengeaftalens § 2.
>
> Både ansatte, der er omfattet af natpengeaftalen, og ansatte, der er omfattet af den nævnte CO II-aftale, kan således få tillæg for delt tjeneste efter natpengeaftalens § 3 samtidig med ulempetillæg efter arbejdstidsaftalens § 13.
>
> Tillægget efter § 13 udbetales uafhængigt af eventuel overarbejdsgodtgørelse efter de almindelige regler eller eventuel weekendgodtgørelse efter § 14.
>
> Reglerne i § 13 omfatter også ansatte, der efter § 8, stk. 3, er undtaget fra de almindelige regler om overarbejdsgodtgørelse.
>
> Ulempetillæg udbetales, hvis ikke andet er aftalt, ved den førstkommende lønudbetaling efter opgørelsesperiodens (normperiodens) udløb.
>
> **§ 14.** Arbejde i weekender eller på søgnehelligdage godtgøres med afspadsering af samme varighed med et tillæg på 50 pct. eller med timeløn med et tillæg på 50 pct.
>
> Stk. 2. Arbejdstid, der godtgøres efter stk. 1, indgår ikke i arbejdstidsopgørelsen efter § 7.
>
> Stk. 3. Der ydes herudover tillæg efter § 13.
>
> Stk. 4. Hvis den ansatte er syg, når en planlagt afspadsering begynder, har den pågældende ikke pligt til at påbegynde afspadseringen.
>
> *Cirkulærebemærkninger til § 14:*
> Weekendgodtgørelsen ydes for arbejde, der efter ordre eller godkendt tjenestefordeling udføres i tidsrummet fra lørdag kl. 00 til søndag kl. 24 og på søgnehelligdage fra kl. 00 til kl. 24.
>
> Arbejde i weekender eller på søgnehelligdage udløser et tillæg på 50 pct., uanset om der er tale om almindelig tjeneste, pålagt over- eller merarbejde eller en deltidsansats ekstraarbejde inden for fuldtidsnormen.
>
> Godtgørelse i form af afspadsering kan gives såvel før som efter den pågældende weekend eller søgnehelligdag (omlægning af tjenesten).
>
> Afspadsering bør så vidt muligt gives i hele eller halve dage.
>
> Afspadseringen medregnes på afviklingstidspunktet i arbejdstidsopgørelsen som anden afspadsering, jf. § 7, nr. 2.
>
> Hvis afspadsering ikke er sket inden udløbet af den følgende normperiode, ydes kontant godtgørelse ved den efterfølgende lønudbetaling.
>
> Der ydes ikke herudover erstatningsfridage, kompensation for mistede fridage eller godtgørelse for over- eller merarbejde.
>
> Ved arbejde i weekenden ydes både tillæg efter § 13 og weekendgodtgørelse efter § 14.
>
> Reglerne i § 14 omfatter også ansatte, der efter § 8, stk. 3, er undtaget fra de almindelige regler om overarbejdsbetaling.

*English gloss.*

**§ 13:**
- **The supplement:** a single supplement of 25 % of the net hourly wage, including fixed supplements, minimum 32.77 kr. at the 31 March 2012 level.
- **The windows:** weekdays 17:00–06:00 (including Monday morning); Saturday 00:00 to Sunday 24:00; weekday public holidays all day; Constitution Day from 12:00; Christmas Eve from 14:00. The weekday window includes 17:00–19:00.
- **Conditions:** only for ordered or rostered work. Paid regardless of overtime pay or the § 14 compensation. Also due to staff exempt from overtime under § 8 stk. 3. Paid at the first payday after the norm period. Convertible to time off by agreement.

**§ 14:**
- **The compensation:** work at weekends or on weekday public holidays earns equal time off + 50 % or hourly pay + 50 %, for ordered or rostered work.
- **Outside the hour count:** the hours are kept out of the § 7 count. The § 13 supplement is paid on top.
- **Applies to everyone:** it applies to ordinary duty, ordered overtime or extra work, and part-timers' extra hours alike, and also to staff exempt under § 8 stk. 3.
- **Taking the time off:** it may be given before or after the weekend. It should be in whole or half days where possible. It is counted under § 7 nr. 2 when taken. Time off not taken by the end of the next norm period is paid in cash.
- **Nothing else on top:** no overtime pay or other compensation is added.

**R5-3. Identity with OK26.**
- **What was compared:** the text from "§ 13. For arbejde …" to the end of the remarks to § 14, with line-end hyphenation and page furniture removed.
- **Result:** the three texts have 616 words each and are identical. They are 014-21 (OK24), CIR1H 9551 (Retsinformation's version of 014-21) and 022-26 (OK26, PDF pp. 20–21).
- **Consequence:** S146's 022-26 quotes of §§ 13–14 (C2) hold unchanged for OK24.

**R5-4. Caveat on the OK24 settlement itself.**
- The signed OK24 CFU settlement (https://cfu-net.dk/fileadmin/user_upload/ok24/underskrevet_cfu-forlig_ok24.pdf) is a scanned image. I could not extract text from it and had no OCR tool.
- The conclusion therefore rests on the circular chain. Any OK24 change to the working-time agreement would have needed a new or amending circular, and Retsinformation's amendment chain for 9551 shows none before 9594 (2026).
- A union's OK24 summary (secondary, https://dts.dk/ref.aspx?id=6074) lists no working-time change.

**Verdict: CONFIRMED.** §§ 13 and 14 as quoted are the OK24-period text. Source: Cirkulære om statens arbejdstidsaftale, Cirkulære af 5. juli 2021, Medst.nr. 014-21 (CIR1H 9551, version in force 1 Apr 2021 – 31 Mar 2026), §§ 13–14 with remarks, PDF pp. 20–21. It is word-for-word identical to 022-26.

---

## Found in passing (outside the five cells; for the Orchestrator to route)

**P-1. StatsTid's child-sick quotas disagree with the sources.**
- **What StatsTid encodes:** `docs/references/danish-agreements.md:148-150` encodes CHILD_SICK as AC **1** day, HK **2**, PROSA **3**, per episode.
- **What the sources say (R1-8):**
  - **OK24:** AC, HK and PROSA all get **day 1 and day 2** per illness (no annual cap in the text).
  - **OK26, from 1 Apr 2026:** HK and PROSA get **days 1–3 plus the call-home day**. AC agreed the same, but the text is pending.
- **How each row compares:** AC = 1 is contradicted for both versions. PROSA = 3 is contradicted for OK24. HK = 2 is right for OK24 and short for OK26.
- **Two further facts:**
  - The right is at the employer's discretion: *"Ansættelsesmyndigheden kan give …, når … forholdene på tjenestestedet tillader det"* (the employer may grant it when the workplace allows).
  - PROSA sits under the **LC/CO10** joint agreement, not OAO-S, though the two texts are the same on this point.

**P-2. PAV has not been updated for OK26.**
- It still gives a 74-hour floor for saved days (OK26: 111 h), only day 1 and day 2 for a sick child, and the *opsparing af frihed* scheme, abolished from 1 Apr 2026 by 024-26.
- The page footers read *"Sidst redigeret 03.12.24"* (last edited 3 Dec 2024), yet chapter 19 cites 2025 circulars.
- Treat PAV as describing the pre-OK26 state.

**P-3. Citation traps in the OK26 circulars.**
- 022-26 dates the repealed 014-21 as "5. juni 2021"; it is 5 July 2021.
- 021-26's cover reads "Cirkulære af 18. juni 2029"; the body says 18 June 2026.

**P-4. The 2028 Fritvalgs Lønkonto** (OK26 CFU item 3, from 1 Jan 2028) restructures how særlige feriedage and seniordage are paid. On OAO's reading (secondary), seniordage become whole-days-only. This is a future dated rule to watch (R2-5).

**P-5. FV2026.573 (4 Sep 2026, five-year limitation) is private-sector**, reported for manufacturing (R4-8). Do not cite it as a state rule.

**P-6. The call-home day needs its own representation.**
- Under OK26, HK and PROSA get paid leave for the **remaining hours** of the working day on which a child falls ill.
- If StatsTid models child's sick days as whole days, it has no slot for this part-day entitlement.

**P-7. Weekday office hours change the sick-day credit.**
- Where a local flex agreement fixes office hours per weekday, a sick or holiday day is credited with that weekday's office hours (PAV 19.5.13), not a flat 7.4 h.
- This is a candidate per-local-agreement parameter, next to the nine S146 C6 flex parameters.

## What I could not do

- **OK24 CFU settlement:** a scanned PDF, which I could not read (R5-4).
- **One university flex agreement** (AAU, Department of Computer Science): its URL redirects to a staff portal, so I could not read it (R1 residual).
- **OK26 settlement annexes** (CFU and AC, bilag D–I, which hold the detailed Fritvalgs Lønkonto and family-package texts): not in the published settlement PDFs.
- **FV2025-795:** inherited from S146, not re-read.
- **FV2026.573:** not read; only the Dansk Erhverv report.

## Sources read for this pass (all on 28 Oct 2026)

**Primary**
- Medst. 022-26 (CIR1H 9594)
- Medst. 014-21 and CIR1H 9551
- Medst. 085-25 (HK)
- Medst. 019-26 (PROSA)
- OAO-S joint agreements 029-24 and 021-26
- LC/CO10 joint agreements 012-25 (kost.dk copy) and 020-26
- Medst. 027-26
- CIR1H 9333 (AC, OK24 edition, in force)
- OK26 settlements: CFU and AC
- Ferie: Medst. 021-24 and CIR1H 9298
- Ferievejledning, Dec 2019
- PAV chapters 19, 23, 24 and 26 (MedSt's guide)
- MedSt circular list, pages 1–10
- MedSt news archive, pages 1–8

**Local**
- SDU flex agreement 2026

**Secondary (support or status only)**
- OAO FAQ on the Fritvalgs Lønkonto
- DLF part-timer page
- HK state working-time page
- Dansk Erhverv, 10 Sep 2026
- DTS OK24 summary
