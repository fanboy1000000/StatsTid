# Overtime vs. flex in the Danish state sector — research verdict (S146)

**Date:** 2026-10-23. **Method:** a deep-research first pass (`general-purpose`, Opus 5.5, web search, 56 tool calls), then an independent adversarial verification that re-downloaded and re-read every primary source and tried to break each claim (Opus 5.5, 78 tool calls; claims V1–V12). The S65/S66 discipline. **Status: verified claim by claim.** Where the verification corrects the first pass, the verification governs (Part 1 below; the first pass is kept verbatim in Part 2 as the record of what was claimed). **Owner rulings it informed (2026-10-23, S146):** OQ-1 = (iii), the certain payroll fixes now, the model rebuild designed in S146 and started in S147; OQ-2 = (a), S145's weekly rulings (Q2 = A, Q3 = F1, Q7 (b)) reopened provisionally — in force in code until replaced, no new work on F1 machinery; OQ-3 = (b), flex by default, overtime/merarbejde only for ordered or accepted hours. Re-verification of the remaining open cells is S146 track D4.

## Question

StatsTid's rule engine counts an hour above the week's norm twice: it adds the hour to the flex balance and, in the same week, pays it as overtime (HK/PROSA, 50% then 100% above 37 h / 40 h) or merarbejde (AC, 1:1) (QUAL-189). What do the state sector's primary sources say about where an hour above the norm goes, when it is overtime, and over what period the norm is measured?

## Bottom line (verified)

- **HK and PROSA (pay groups 1–2): the norm is measured over one calendar month** unless a local agreement says otherwise (state working-time agreement § 4 stk. 2: "Arbejdstiden beregnes for en periode på 1 måned"; PAV 19.2.4.1: how the hours fall across days and weeks "does not matter"). No primary source establishes a weekly norm period for AC, HK or PROSA.
- **Overtime presupposes ordered or necessary work** (circular remark to § 8: "beordret eller nødvendigt"), and is paid at a **flat +50%**; part-timers' hours up to full time are 1:1 (§ 8 stk. 2, under renegotiation after the 4 Feb 2026 arbitration award, OK26 item 28).
- **Under flex, an unordered surplus hour is a flex hour, not overtime** (PAV 19.5.6; AC's own remark to § 13). Ordered work is kept outside the flex account (PAV 19.5.11), except inside fixed office hours in PAV's office-hours model. **One hour is never both.**
- **Flex above the cap is forfeited as a starting point** (PAV 19.5.8); local agreements soften this (manager exceptions; 1:1 cash conversion by agreement).
- **AC has no overtime.** Merarbejde is paid only after a claim and an employer decision, requires larger extent over normally ≥ 4 weeks, at +50% (AC § 14).
- **Time off vs. pay is the employer's choice** for HK (PAV 19.2.7.1); StatsTid's `EmployeeCompensationChoice = true` and PROSA "1:1" are contradicted.
- **Root cause in the repository:** the source-register rows behind the weekly rule (SR-AC-OK24-020, SR-HK-OK24-001, and SR-AC-OK24-005/007/021) cite sections of 043-19 whose content is unrelated (§ 2 = form of employment, § 4 = base pay). The 37/40 h thresholds (SR-HK-OK24-006/007) were never sourced ("pending (Phase B)").

## What this means for StatsTid (as ruled)

StatsTid's overtime model is wrong in kind for all three agreements: it pays overtime automatically on every hour above a weekly norm, in tiers no source supports. The direction the sources allow is (b): flex by default, overtime/merarbejde only for ordered or accepted hours, a monthly norm (with weekday public holidays subtracted) for HK/PROSA, and AC merarbejde as a claim-and-decision workflow. The rebuild's detailed forks (the ordered-marking workflow, the office-hours rule, local flex parameters, AC's period, the part-timer rule) are for the S147 model refinement. Registered findings: see `docs/operations/quality-finding-register.md` (S146 rows).

---

# Part 1 — The independent verification (governs)

## 1. Verdicts

| # | Verdict | Decisive quote (Danish → English) | Source |
|---|---|---|---|
| **V1** | **CONFIRMED** | § 4 stk. 2: *"Arbejdstiden beregnes for en periode på 1 måned. Arbejdstiden for perioden udgør 7,4 timer gange antallet af kalenderdage fratrukket fridage samt eventuelle søgnehelligdage, bortset fra søgnehelligdage, der falder på ugedage, hvor den ansatte i forvejen altid har fri."* → working time is calculated per 1 month: 7.4 h × calendar days, minus days off and weekday public holidays, except a holiday on a weekday the person always has off. Remark: *"Normperioden er 1 måned, medmindre andet er aftalt."* (the norm period is 1 month unless otherwise agreed). **How the month's norm is computed:** for day workers, days off are Saturdays and Sundays; for others, the days off under § 15. The holidays are skærtorsdag, langfredag, påskedag, 2. påskedag, Kristi himmelfartsdag, pinsedag, 2. pinsedag, juledag, 2. juledag and nytårsdag. 24 Dec, 31 Dec and grundlovsdag are not holidays. Part-timers get a pro-rata norm under § 5; PAV's example is 18 × 20/37 × 7.4 = 72 h. PAV 19.2.4.1 adds: *"Det er uden betydning for beregningen, hvordan den ansattes arbejdstid rent faktisk er fordelt på de enkelte dage og uger."* (how the hours fall across days and weeks does not matter). | 022-26 § 4 (pp. 11–12), https://cirkulaere.medst.dk/media/jhshf3w2/022-26.pdf; same text in CIR1H 9551 § 4, https://www.retsinformation.dk/api/pdf/224517; PAV 19.2.4, https://pav.medst.dk/pav/kapitel-19-arbejdstidsregler-mv/ |
| **V2** | **CONFIRMED as text, but stk. 2 is being changed** | § 8 stk. 1: *"…godtgøres timerne med afspadsering af samme varighed med et tillæg på 50 pct. eller med timeløn med et tillæg på 50 pct."* (time off or pay, each +50 %). stk. 2: *"Timer, der ikke overstiger arbejdstiden for fuldtidsbeskæftigelse … godtgøres dog kun med afspadsering af samme varighed eller med almindelig timeløn."* (hours up to full time get only 1:1). **Nuance 1:** the 1/1924 rate comes from a remark that applies *"medmindre andet er aftalt"* (unless otherwise agreed). For HK and PROSA it is set in their own agreements (HK § 38 stk. 4; PROSA § 3 stk. 5): 1/1924 of fixed pay, excluding the standby allowance (*rådighedstillæg*). **Nuance 2, missed by the first pass:** the OK26 state settlement with CFU (the state's central employee organisations), item 28, reads *"Der afsættes i alt 12 mio. kr. … aftale om implementering af voldgiftsrettens dom af 4. februar 2026 på statens område … Forhandlinger optages senest 1. maj 2026."* (12 million kroner are set aside to negotiate how the 4 Feb 2026 arbitration award on part-timers' overtime and extra work is applied in the state). Akademikerne report that in the regional area, from 1 Apr 2026, part-timers working beyond their contracted hours are paid overtime rates like full-timers. No state implementing text was found; 022-26 (18 Jun 2026) still carries stk. 2 unchanged. | 022-26 § 8 (p. 15); https://medst.dk/media/oo1auuiu/ok26-forlig-medst-cfu.pdf item 28; https://dm.dk/media/zabfucm2/ac-redegoerelse-forligene-ved-ok26.pdf pp. 10, 28 |
| **V3** | **CONFIRMED** | *"Det er en forudsætning, at arbejdet har været beordret eller nødvendigt af hensyn til en forsvarlig varetagelse af arbejdsopgaverne."* (a precondition is that the work was ordered or necessary for proper performance). Identical in 022-26 and CIR1H 9551; repeated in PAV 19.2.7.1. | 022-26 remark to § 8 stk. 1–2 (p. 15); CIR1H 9551 |
| **V4** | **PARTLY** | **Holds:** no 37 h/40 h weekly threshold and no 100 % tier in 022-26, in the general HK provisions (085-25 § 38) or in PROSA (079-25). The PROSA 2008 circular, 064-08, says the transfer to the state rules meant *"overgang fra dagsnorm til månedsnorm samt, at overarbejde honoreres med 50 pct."* (a move from a daily to a monthly norm, and overtime paid at 50 %). **Fails:** Bilag 9 is not the only 50→100 % ladder for HK staff in the state. (a) HK/STAT Hovedstaden clerks on temporary call-in to state service in Greenland (023-19): § 2 *"Arbejdstiden er 40 timer ugentlig"* (a 40-hour week), and § 11 stk. 3 and 8 *"Overarbejdet opgøres dag for dag … 1., 2. og 3. overtime: 50 pct. For 4. og efterfølgende … 100 pct."* (overtime counted day by day; hours 1–3 at 50 %, from the 4th at 100 %). A successor, Medst. 011-23, exists but was not opened. (b) Special terms from 2006 for HK staff transferred in the municipal reform (069-06, item 3 d): *"50 pct. for de 3 første timer … For alle øvrige overarbejdstimer … 100 pct."* Its current status was not checked. Both are daily ladders for narrow groups. PAV 19.3.1.6 confirms ladders exist *"i enkelte organisationsaftaler"* (in a few organisation agreements). | https://cirkulaere.medst.dk/media/1027/023-19.pdf; https://cirkulaere.medst.dk/static/Circular/2006/069-06.pdf; https://cirkulaere.medst.dk/static/Circular/2008/064-08.pdf |
| **V5** | **CONFIRMED** | HK § 38: *"Ansatte under denne aftale er omfattet af statens arbejdstidsaftale … Overtidstillæg ydes efter arbejdstidsreglerne."* (covered by the state working-time agreement; overtime supplement under its rules). PROSA § 12 stk. 2: *"…ansatte i løngruppe 1 og 2 omfattet af reglerne om overarbejdsgodtgørelse … Ansatte i løngruppe 3 og 4 er omfattet af merarbejdsgodtgørelse, når de opfylder betingelserne herfor."* (pay groups 1–2: overtime; 3–4: extra-work pay when they meet the conditions). | 085-25 § 38 (p. 33); 079-25 § 12 (pp. 13–14) |
| **V6** | **CONFIRMED** | *"De timer, der ligger ud over den pågældendes gennemsnitlige ugentlige timetal ved flekstidsopgørelsen, udløser ikke overarbejdsbetaling, men overføres time for time til den følgende periode eller henføres til en særlig fleksopsparingskonto."* (surplus hours do not trigger overtime pay; they carry over hour for hour, or go to a flex savings account). AC's own circular says the same for AC, in the remark to § 13: *"Overskydende timer, der i henhold til flekstidsordningen overføres til senere normperioder, udløser ikke merarbejdsgodtgørelse."* | PAV 19.5.6; CIR1H 9333 (AC OK24) remark to § 13 (p. 20), https://www.retsinformation.dk/eli/retsinfo/2025/9333/pdf |
| **V7** | **CONFIRMED; "only" is overstated** | *"…bortfalder som udgangspunkt ved opgørelsesperiodens udgang og kan således ikke udløse kontant betaling eller anden honorering."* (forfeited **as a starting point** at period end; no cash or other compensation). The only exception PAV names is transfer to the overtime/extra-work account. Local agreements also use other exceptions: the Aarhus template allows *"medmindre andet aftales med lederen"* (unless agreed with the manager), and SKAT's 2012 addendum allowed flex hours to be converted to cash **1:1** by agreement (upheld in arbitration FV2015.0028). | PAV 19.5.8; https://arbejdsretten.dk/media/16539/fv20150028.pdf p. 3 |
| **V8** | **CONFIRMED, with a missed nuance** | *"…beordret arbejde, der udføres inden for flekstiden, men som skal holdes uden for flekstidsregnskabet."* (ordered work in flex hours must be kept outside the flex account). But in PAV's own office-hours model (*kontortid*, fixed daily hours per weekday): *"Arbejde inden for kontortiden … indgå i flekstidsregnskabet, uanset om placeringen … er sket efter … konkret pålæg fra ledelsen."* (work inside office hours stays in flex **even if ordered**). An hour leaves flex only if all three hold: ordered or necessary, outside office hours, and above 7 h 24 min that day. | PAV 19.5.11 |
| **V9** | **CONFIRMED** | § 14 stk. 2: *"1. som er pålagt … i henhold til særlig ordre eller har været en forudsætning for den forsvarlige varetagelse af tjenesten, og 2. som har været af større omfang og har strakt sig over en periode, der normalt ikke må være mindre end 4 uger."* (ordered or a precondition for the job, **and** of larger extent, normally at least 4 weeks). stk. 5–6: time off *"med tillæg af 50 pct."*, otherwise pay at 1/1924 + 50 %. **Standby allowance:** Bilag 6 item 9 grants it to fuldmægtige and similar posts that take on *"forpligtigelse til merarbejde på indtil 20 timer pr. kvartal"* (up to 20 h extra work a quarter; 35 h on the old pay system). § 14 stk. 3 then compensates only if total extra work *"væsentligt overstiger"* (substantially exceeds) that. **OK26:** the only § 14 change is stk. 5, 3rd sentence: *"Afspadsering kan efter aftale med den ansatte udskydes i op til 1 år…"* (time off may be postponed up to 1 year by agreement with the employee). The § 15 floor for saved time-off days (*opsparingsdage*) rose from 74 to 111 h. stk. 2 and the 50 % are unchanged. On 23 Oct 2026 retsinformation still lists CIR1H 9333 as current (*"Gældende"*). | CIR1H 9333 § 14 (pp. 22–23); https://medst.dk/media/0rmlepno/forlig-ok26-medst-ac.pdf item 21; Akademikerne's OK26 report p. 7 |
| **V10** | **PAV CONFIRMED; the repo is PARTLY wrong** | PAV 19.2.7.1: *"…godtgøres de overskydende timer efter arbejdsgiverens valg med afspadsering … med timeløn … eller med en kombination heraf."* (**the employer** chooses time off, pay, or a mix). The deadline rule in § 10 stk. 1 and its remark: *"Afspadsering skal gives senest 3 måneder efter opgørelsesperiodens udløb"* (time off within 3 months of the **end of the norm period**). After that, *"skal der ske betaling ved den følgende lønudbetaling, medmindre den ansatte vælger … opsparingsdage"* (payment at the next payday, unless the employee chooses saved days). So the repo's prose ("within 3 months; payment if not arranged") is roughly right, except that the clock starts at period end. But `EmployeeCompensationChoice = true` and SR-HK-OK24-020's "employer may not override" are **contradicted**. Neither 085-25 nor 079-25 contains any employee-choice clause. The employee's only choices are agreeing to defer time off (up to 1 year) and choosing saved days over cash after the deadline. Separately, the repo describes PROSA as "afspadsering or payment 1:1", which is wrong: § 8 gives +50 %. | PAV 19.2.7.1, 19.2.7.3–4; 022-26 § 10 (p. 17) |
| **V11** | **CONFIRMED, with scope caveats and one attribution error** | Umpire: *"…uden at dette indebærer, at opsparede timer, som ikke er afviklet inden normperiodens udløb, anses for overarbejde…"* (saved flex hours not taken by the end of the norm period are not overtime). The state circular *"forudsætter samtidig, at der kun vil kunne være tale om overarbejde, hvis arbejdet har været beordret eller nødvendigt"* (assumes there can only be overtime if the work was ordered or necessary). **Scope:** a trainee (*elev*) at AU Foulum, OAO for 3F against MedSt. It turned on the OAO-S common agreement's § 32 stk. 2 ("elever kan ikke deltage i overarbejde", trainees may not do overtime) and the agricultural workers' organisation agreement, which a local agreement had switched to the state working-time rules. It was a protective-ban question, not a pay claim. It is an indication (*tilkendegivelse*) accepted as a ruling, dated 19 Mar 2020. **Applies to HK/PROSA by reasoning only:** HK sits under the same OAO-S common agreement, and the umpire cites HK's § 38 remark; PROSA reaches the state agreement through its own § 12. **Attribution:** the phrase "der sondres … mellem overarbejde og flekstid" is the employer's argument. The umpire said that view *"harmonerer med"* (is consistent with) § 12; the first pass presented it as the umpire's own words. | https://arbejdsretten.dk/media/16941/fv20190076.pdf pp. 9–11 |
| **V12** | **CONTRADICTED (the repo's claim)** | 043-19 § 2 is **"Ansættelsesform"** (whether someone is hired as a civil servant or on contract): *"Med hensyn til ansættelsesform (tjenestemands- eller overenskomstansættelse) følges retningslinjerne…"*. It says nothing about norms, 37 h, 1924 h or weeks. § 4 is "Basisløn" (base pay), yet the repo cites it for extra work and compensation in SR-AC-OK24-005/007/021. SR-HK-OK24-001 cites the same **AC** § 2 for **HK's** norm. No weekly norm period was found anywhere for AC, HK or PROSA. HK and PROSA use 1 month. AC § 13 says only *"gennemsnitlig 37 timer ugentlig"* (an average), with no period. A Djøf guide quoted as evidence in FV2023-915: *"Administrative djøfere arbejder efter en kvartalsnorm"* (administrative AC staff work to a **quarterly** norm). FV2023-915 also holds that special consultants' extra-work rules *"bygger på en årsnorm"* (rest on an **annual** norm). | https://oes.dk/media/ik0hm2lr/043-19.pdf § 2 (p. 12), § 13 (pp. 26–27); https://arbejdsretten.dk/media/eounmhsf/fv2023-915.pdf pp. 15–16, 22 |

## 2. Evidence that could support the current StatsTid encoding (all weaker)

- **The "no bearing" remark.** 022-26's remark to § 8 stk. 3 says flex schemes *"har ingen betydning for, om en ansat er omfattet af overarbejds- eller af merarbejdsreglerne"*. It sounds like "flex does not affect overtime", but in context it only says flex does not decide which regime someone belongs to (overtime vs. extra work). It says nothing about counting an hour twice.
- **A "40 hours in a single week" sentence (AC only).** The remark to AC § 13: *"Såfremt der indføres en opsparingsordning, udløser arbejde op til 40 timer den enkelte uge ikke krav på godtgørelse … for merarbejde."* This is the only per-week "40 h" text found in the AC/HK/PROSA state sources and a plausible origin of the 40-hour figure. It says the opposite of a 100 % tier: under a local time-saving scheme, up to 40 h in a week earns **no** compensation.
- **Narrow HK ladders.** The Greenland call-in agreement (40-hour week, daily 50/100 %) and the 2006 municipal-reform special terms (daily 50/100 %). Before 1 Oct 2008, HK's organisation agreement § 35 stk. 5–7 capped overtime pay (abolished in 2008) and PROSA used a daily norm. A daily or weekly ladder therefore existed historically or for small groups, never as a 37/40 weekly threshold for general HK/PROSA.
- **Weekly or daily measures that are not norm periods.** Bilag 1 § 5's 48-hour cap is a weekly average over 4 months; it is a ceiling, not an overtime trigger. PAV's office-hours model uses a daily 7 h 24 min filter.
- **Local cash payout of flex exists.** SKAT's flex agreement allowed 1:1 cash conversion by agreement (FV2015.0028). This supports a payout feature, but only by local agreement and only at 1:1. It is not automatic and not overtime.
- **Nothing found** that establishes a weekly norm period for AC/HK/PROSA, a 37/40 weekly threshold, a 100 % tier for general HK or PROSA, or overtime hours also counting in the flex balance. Every local agreement checked (PAV's model, SKAT, AU Foulum 2014, the AU template, SDU 2026) keeps ordered overtime outside flex.

## 3. Net verdict in plain language

**What stands (high confidence):**
- For HK and PROSA pay groups 1–2, the norm is measured over **one calendar month** unless a local agreement says otherwise. Hours are not judged week by week; the state's own guide says how hours fall across weeks "does not matter".
- An hour above the norm becomes overtime only if it was **ordered or necessary**, and then earns a **flat +50 %**.
- Under flex, an unordered surplus hour is a **flex hour**, not overtime. Flex and overtime are **separate accounts**, so one hour can never be both.
- Flex hours above the cap are **forfeited by default**.
- AC has no overtime. Its extra work is paid only after a claim and an employer decision, requires larger extent over normally at least 4 weeks, and pays +50 %.

**What falls or needs correcting:**
- "The only HK ladder is Bilag 9": two narrow exceptions exist.
- The part-timer 1:1 rule is under active renegotiation following the 4 Feb 2026 arbitration award. The first pass missed this, and it matters for OK26-period payroll.
- The arbitration quote is attributed to the umpire when it was the employer's argument.
- "Forfeit, with transfer as the only exception" is softer in practice (manager-agreed exceptions, 1:1 cash conversion).

**What this means for StatsTid.** The S145 weekly settlement rule says the agreement sources were "silent" on the norm period, citing source register row SR-AC-OK24-020. They are not silent, and that row's source (043-19 § 2) is a section on form of employment. So the weekly basis of last sprint's work is not supported by any primary source found. Confidence that HK/PROSA use a monthly norm: high. For AC, the norm period is not fixed in the agreement; union guidance says quarterly for administrative staff and annual for special and chief consultants. Confidence that AC is not weekly: high; confidence about the exact AC period: medium.

## 4. What the first pass got wrong or missed

1. **The citation root cause (missed).** 043-19 § 2 = Ansættelsesform, § 4 = Basisløn. Register rows SR-AC-OK24-020, SR-HK-OK24-001, SR-AC-OK24-005/007/021 all cite sections with unrelated content. `danish-agreements.md:69` inherits this.
2. **Explicit anti-weekly sentence (missed).** PAV 19.2.4.1: distribution across "days and weeks" is irrelevant to the norm calculation.
3. **AC's own flex remark (missed).** The circular remark to § 13 says carried-over flex hours do not trigger extra-work pay; the first pass relied on PAV alone for AC. It also missed the quarterly/annual AC norm (Djøf guide, FV2023-915).
4. **AC worked example is wrong in kind.** StatsTid's example pays AC 7.20 h of extra work for a single week, and `OvertimeRule.cs` pays AC extra work automatically at 1.0x per week. The sources require ≥ 4 weeks, "larger extent", a written claim, quarterly reporting, an employer decision, and +50 %.
5. **"Ordered" is not the whole test.** In PAV's office-hours model, ordered work inside office hours stays in flex. Whether an hour leaves flex depends on the local agreement.
6. **Hours that are neither flex nor overtime.** Ordered weekend and holiday work for day workers (§ 14) gets +50 % and *"indgår ikke i arbejdstidsopgørelsen efter § 7"* (is left out of the hours count entirely). The 25 % unsocial-hours supplement (*ulempetillæg*, § 13) is paid *"uafhængigt af eventuel overarbejdsgodtgørelse"* (regardless of overtime), so an hour can carry it and also be overtime. Inside flex time it is due only if the placement was ordered or necessary (PAV 19.5.3). § 14 gives +50 % on both Saturday and Sunday; StatsTid's Sunday rate of 2.0 deserves its own check.
7. **The norm period can't be configured.** `SYSTEM_TARGET.md` limits `NormPeriodWeeks` to {1, 2, 4, 8, 12}, which cannot express the state default of one calendar month.
8. **The part-timer arbitration** of 4 Feb 2026 and OK26 item 28 (see V2).
9. **OK26 AC changes** answer the first pass's open question 4: § 14 stk. 5, 3rd sentence, and the saved-days floor of 111 h. Extra-work conditions and the 50 % are unchanged.
10. **Groups exempt from the overtime rules** that matter to the routing question:
    - HK staff with base pay of 441,037 kr. or more are exempt from overtime and extra-work rules (085-25 § 4 stk. 2 remark; PAV 19.3.3).
    - PROSA pay groups 3–4 get extra-work pay only when they meet the state agreement's conditions, i.e. fall in the § 8 stk. 3 categories.

---

# Part 2 — The first pass, verbatim (superseded where Part 1 corrects it)

## Bottom line

In the Danish state, an hour worked above the norm goes to the **flex balance** (*flekssaldo*) by default. It becomes **overtime** (*overarbejde*, paid with a supplement) or **extra work** (*merarbejde*) only when management ordered the work (*beordret/pålagt*), or when it was necessary to do the job properly and management accepted it. The two accounts are kept separate: the employer's official guidance says the flex system must be built so that ordered work is "kept outside the flex account". So one hour can never sit in both. An hour can move from flex to overtime only if the workplace's local flex agreement allows it, and then it leaves the flex balance. A 2020 labour-arbitration ruling confirmed that flex surplus is not overtime. Hours above the flex cap are **forfeited by default**, not paid out. StatsTid's current double count is not supported by any source I found.

Terms used below:
- **norm period** (*normperiode*): the period over which the 37-hour average week is measured. The state default is one month.
- **time off in lieu** (*afspadsering*): paid time off given instead of money.
- **local agreement** (*lokalaftale*): an agreement made at the workplace between management and the union representatives.
- **PAV**: the state employer's official guide to the rules.
- **circular remarks** (*cirkulærebemærkninger*): the official explanatory notes issued with each agreement.

## The primary sources

| # | Source | Version |
|---|---|---|
| S1 | Cirkulære om statens arbejdstidsaftale, Medst.nr. 022-26 — https://cirkulaere.medst.dk/media/jhshf3w2/022-26.pdf | 18 Jun 2026, effective 1 Apr 2026 (OK26) |
| S1' | Its predecessor, CIR1H nr 9551 af 05/07/2021 (in force through OK24) — https://www.retsinformation.dk/api/pdf/224517 | §§ 4 and 8, the remarks on *beordret/nødvendigt* and the flextime remark are word-for-word identical to S1 |
| S2 | PAV kap. 19 (Medarbejder- og Kompetencestyrelsen, MedSt) — https://pav.medst.dk/pav/kapitel-19-arbejdstidsregler-mv/ | read 23 Oct 2026; §§ 19.2.7, 19.3.1.6, 19.3.3, 19.3.4.4–6, 19.5, 19.8 |
| S3 | HK organisation agreement, Medst.nr. 085-25, § 38 and Bilag 9 — https://cirkulaere.medst.dk/media/m1hpbtsq/085-25.pdf | 12 May 2025 |
| S4 | PROSA organisation agreement, Medst.nr. 079-25, § 12 — https://cirkulaere.medst.dk/media/tnjl2rxn/079-25.pdf | 1 May 2025 |
| S5 | AC agreement (OK24, 2025 edition without remarks), §§ 13–15 — https://www.fysio.dk/globalassets/documents/raadgivning/overenskomster/overenskomster---staten/endelig-udgave-af-ac-ok-24-uden-cirkularebemarkninger.pdf | OK24 |
| S6 | Labour arbitration FV2019.0076 (OAO for 3F v. MedSt for Aarhus Universitet Foulum) — https://arbejdsretten.dk/media/16941/fv20190076.pdf | indication of 5 Mar 2020, accepted by both parties with the effect of a ruling (*kendelse*) |
| S7 | SDU local flex agreement for technical/administrative staff (TAP), co-signed by HK/Stat and Prosa union reps — https://sdunet.dk/-/media/sdunet/filer/servicesider/hr/ansaettelsesforhold/flekstidsaftale-for-sdu-2026-underskrevet.pdf | effective 1 Apr 2026 |
| S8 | Aarhus University flex agreement template — https://medarbejdere.au.dk/fileadmin/www.medarbejdere.au.dk/hr/Arbejdstid/Arbejdstid_-_skemaer/Skabelon_for_flekstidsaftale.pdf | undated; S6 says it was adopted by the university's main cooperation committee on 20 Mar 2014 |
| S9 | SKAT flex agreement — https://dts.dk/ref.aspx?id=1453 | 2008, historical |

There is **no central state flextime agreement**. PAV 19.5.1: "der er hjemmel til, at der lokalt kan indgås aftale om flekstidsordninger" ("there is authority to agree flextime arrangements locally"). Every flex rule (caps, settlement period, what happens above the cap) is set in a **local agreement**.

## Q1. Under flextime, where do over-norm hours go?

**Answer:** to the flex balance, hour for hour. They do not trigger overtime pay. **Confidence: HIGH.**

- PAV 19.5.6 (S2): *"De timer, der ligger ud over den pågældendes gennemsnitlige ugentlige timetal ved flekstidsopgørelsen, udløser ikke overarbejdsbetaling, men overføres time for time til den følgende periode eller henføres til en særlig fleksopsparingskonto."* — "Hours above the employee's average weekly hours in the flex settlement do not trigger overtime pay; they carry over hour for hour to the next period or go to a special flex savings account."
- SDU 2026, § 2.4.1 (S7): *"Arbejdstimer ud over den arbejdstid, der følger af ansættelsesbeviset og overenskomsten, registreres som flekstimer, medmindre der er tale om pålagt over- eller merarbejde."* — "Hours beyond the contractual working time are registered as flex hours, unless it is ordered overtime or extra work."
- Aarhus template § 5 stk. 2 (S8) says the same: surplus hours do not trigger overtime pay; they carry over to the next month.

## Q2. When does an hour count as overtime for HK Stat and PROSA?

**Answer:** HK office/IT staff and PROSA pay groups 1–2 follow the state working-time agreement. Their rules are:
- Overtime arises only when the **period's** norm is exceeded. The default period is **one month**: 7.4 hours × calendar days, minus days off and public holidays (§ 4 stk. 2).
- The work must have been **ordered, or necessary to do the job properly**.
- Compensation is a **flat 50 %**, as time off (1.5× the hours) or as pay at 1/1924 of annual salary + 50 % (§ 8 stk. 1). PAV 19.2.7.1 says the **employer** chooses which.
- Part-timers' hours up to the full-time norm are paid **1:1, with no supplement** (§ 8 stk. 2).

**Confidence: HIGH.**

- Circular remark to § 8 stk. 1–2 (S1, S1'): *"Det er en forudsætning, at arbejdet har været beordret eller nødvendigt af hensyn til en forsvarlig varetagelse af arbejdsopgaverne."* — "It is a precondition that the work was ordered or necessary for the proper performance of the duties."
- HK § 38 (S3): HK staff are covered by the state working-time agreement, and *"Overtidstillæg ydes efter arbejdstidsreglerne."* ("Overtime supplement is paid under the working-time rules.")
- PROSA § 12 stk. 2 (S4): pay groups 1–2 get overtime under the state agreement; pay groups 3–4 get extra-work pay "when they meet the conditions". That extra-work pay is discretionary (*merarbejdsgodtgørelse*, § 9).

**There is no 37 h / 40 h weekly threshold and no 100 % tier in these rules.** The only 50 %→100 % ladder I found is in HK Bilag 9. It applies to laboratory technicians on research cruises or fieldwork, counted per day beyond 7.4 hours: hours 1–3 at 50 %, the 4th hour onward at 100 %. PAV 19.3.1.6 notes that "i enkelte organisationsaftaler" (in a few organisation agreements) the supplement rises to at most 100 %. **Confidence that general HK/PROSA has a flat 50 %: HIGH.**

## Q3. AC (academics): when is extra work (merarbejde) paid?

**Answer:** the AC agreement has **no overtime at all**. Extra work is compensated only after a case-by-case decision. **Confidence: HIGH.** The AC agreement, § 14 stk. 2 (S5), allows compensation only for official extra work:

> *"1. som er pålagt den ansatte i henhold til særlig ordre eller har været en forudsætning for den forsvarlige varetagelse af tjenesten, og 2. som har været af større omfang og har strakt sig over en periode, der normalt ikke må være mindre end 4 uger."*

In English: (1) work ordered by special order, or a precondition for properly doing the job, **and** (2) of larger extent, lasting normally at least 4 weeks.

The rest of the AC rules:
- **Paperwork (§ 14 stk. 3–4):** the employee writes a statement of the hours; the nearest manager attests it and makes a recommendation; it is filed at least quarterly; the employer then decides how many hours qualify.
- **Rate (§ 14 stk. 5–6):** **+50 %**, as time off where possible, otherwise pay at 1/1924 of salary + 50 %.
- **Exclusions:** research and teaching staff are excluded unless the work was ordered and both it and their normal work are controllable (§ 14 stk. 1; PAV 19.3.4.6). Special and chief consultants are paid through one-off payments (*engangsvederlag*) instead.
- **Flex:** local flex is allowed for AC (§ 13 stk. 5), except in teaching. PAV 19.5.12 adds that flex must not undermine the extra-work duty already paid for by fuldmægtige's standby allowance (*rådighedstillæg*), which is up to 20 hours per quarter (PAV 19.3.4.4).

## Q4. Can the same hour be both flex and overtime?

**Answer: No.** **Confidence: HIGH.** The rules route each hour one way, and there is a defined path for moving an hour from one account to the other.

- PAV 19.5.11 (S2): *"Ordningen bør konstrueres, så det er muligt at skelne mellem på den ene side egentlig flekstid og på den anden side beordret arbejde, der udføres inden for flekstiden, men som skal holdes uden for flekstidsregnskabet."* — "The scheme should be built so that genuine flextime can be told apart from ordered work done within flex hours, which must be kept outside the flex account."
  - PAV's model: ordered work outside office hours (*kontortid*) that exceeds 7 h 24 min per day is "registreres særskilt" (registered separately).
  - Management may offset those hours with time off in the same period. If it doesn't, they are treated as overtime or extra work at the end of the period.
- **Above the cap**, PAV 19.5.8: *"De overskudstimer, der ligger ud over det fastsatte maksimum, bortfalder som udgangspunkt ved opgørelsesperiodens udgang og kan således ikke udløse kontant betaling eller anden honorering."* — "Surplus hours above the set maximum are as a rule forfeited at the end of the settlement period and cannot trigger cash or other compensation."
  - The only exception: if the local agreement allows it, such hours can be transferred to the overtime/extra-work account. They are then treated as ordinary overtime. This is a **move out of flex**, not a second count.
- **Arbitration S6:** the umpire (Lene Pagter Kristensen, a retired Supreme Court judge) held that the agreements "sondres mellem overarbejde og flekstid" (distinguish between overtime and flextime). Hours saved under flex and not taken by the end of the norm period are not overtime. The state circular "forudsætter … at der kun vil kunne være tale om overarbejde, hvis arbejdet har været beordret eller nødvendigt" ("assumes … that there can only be overtime if the work was ordered or necessary").
- **Local practice matches.** The SDU and Aarhus agreements both forfeit or cap hours and pay "normally no cash".
  - Caps: SDU allows +37 / −7.4 hours a month. The Aarhus template allows +37 / −10, with forfeiture above the cap (§ 5 stk. 4). PAV's own example is +15 / −10.

## Q5. What does the employer record when overtime must be ordered?

- **AC: HIGH confidence.** A formal record: the employee's written statement, the manager's attestation, at least quarterly, then the employer's decision on the approved hours (§ 14 stk. 3–4). Approval can come after the work is done; the decision fixes the hours.
- **HK/PROSA: MEDIUM confidence.** The central agreement sets the condition ("ordered or necessary") but no recording format. Local agreements fill the gap:
  - SDU § 2.4.2: "Over- eller merarbejde skal være pålagt af lederen" ("must be ordered by the manager").
  - Aarhus template: the excess must be ordered or unavoidable, **"og at ledelsen er indforstået hermed"** ("and management has agreed to it").
  - PAV 19.5.11: such hours are "registreres særskilt" (registered separately).
- **After-the-fact approval** is plausible under the "necessary" route plus management's acceptance, but I found no source that defines a retroactive-approval procedure.
- **Time registration:** the Working Time Act requires an objective, reliable and accessible system that records daily working time, with records kept for 5 years (PAV 19.8).

## What this means for StatsTid's model

**Closest: (b).** Over-norm hours go to flex by default; only ordered or approved hours become overtime (HK, PROSA groups 1–2) or extra work (AC, PROSA groups 3–4). Option (a), "an hour paid as overtime is excluded from flex", is true but only as a consequence of (b). Used on its own it would still pay overtime automatically on every excess hour, which is wrong. **(c) has no support.** The double count pays an hour as overtime and also lets it be taken as flex time off.

Data the system would need:
1. **An "ordered" marking per registration:** who ordered or approved it, when, and whether before or after the work. These should be audit events. The spec's `OvertimeRequiresPreApproval` flag is a start, but under (b) an unapproved excess hour is **flex, not flagged overtime**.
2. **Two separate ledgers.** Each hour lands in exactly one. A flex→overtime transfer is an explicit, audited event that reduces the flex balance, and is allowed only if the local agreement permits it.
3. **Settlement against the norm period** (default one month, locally variable), not week by week.
4. **Flex parameters at the local-agreement level** (institution or unit), not per AC/HK/PROSA agreement: maximum plus and minus, settlement period, what happens above the cap (forfeit / manager exception / transfer), what happens when someone leaves, and optionally office hours (*kontortid*).
5. **AC extra work as a claim-and-decision workflow,** not an automatic calculation. For fuldmægtige, only extra work well beyond the 20 hours per quarter covered by the standby allowance qualifies.

## Related discrepancies (outside the question; found in passing, need their own check)

- `danish-agreements.md` line 69 says the sources are "silent" on the norm period. They are not: § 4 stk. 2 sets **1 month**, unless agreed otherwise locally.
- The 37 h / 40 h thresholds with 50 % / 100 % tiers do not match state HK/PROSA, which has a flat 50 % plus 1:1 for part-timers up to full time.
- `FLEX_PAYOUT` "auto-payout" contradicts PAV's default of forfeiture.
- The line-86 note "AC merarbejde 1:1" should be checked against § 14 stk. 5–6, which gives +50 %.
- PAV 19.2.7.1 makes time off vs. pay the **employer's** choice. The repo sets HK `EmployeeCompensationChoice=true`.
- OK26 raised the minimum cap on saved time-off days (*opsparingsdage*) from 74 to 111 hours (S1 introduction). This is relevant to correct behaviour across the OK24→OK26 transition. PAV still says 74, so PAV lags the circular.

## Self-check: what was found against these claims

- **The contrary argument was tested and lost.** In S6, OAO/3F argued that any hours above the period norm are overtime "uanset om eleven måtte være omfattet af en lokalt indgået flekstidsaftale" ("regardless of any local flex agreement"). The umpire rejected it. One caveat: that case concerned trainees under the agricultural organisation agreement. The reasoning rests on the state circular's remarks, so it carries over, but the ruling was not about HK or PROSA directly.
- **Payout terms vary locally.** The SKAT 2008 agreement (S9) had no hard cap (200 hours as guidance) and paid leftover flex **in cash at exit** at 1/1924 with no supplement. It still excluded ordered overtime from flex. This shows exit and payout terms are local, not that flex equals overtime.
- **PAV is the employer's interpretation**, not the agreement itself. The decisive points are also confirmed by the circular remarks (S1) and the arbitration (S6).
- **Private-sector sources were excluded.** DI's flex guidance and Copenhagen Municipality documents turned up in searches; they are private-sector and municipal, not state.

## Unsourced or uncertain — questions for the domain expert (Phase B)

1. In practice, can staff on flex get overtime for work that was **necessary but not ordered**, with no manager acknowledgement? Or does it default to flex? How is retroactive approval recorded?
2. How common are the PAV "office hours" model and flex→overtime transfer clauses in real ministry and agency local agreements?
3. How do PROSA groups 3–4 and HK staff above the salary ceiling have discretionary extra-work pay (§ 9) decided?
4. Did OK26 change AC § 14? The OK24 text was used.
5. Where did StatsTid's 37/40 and 50/100 % figures come from? They may come from a private-sector HK agreement; not traced.
6. HK Stat's own member guidance (hk.dk) and the 2000 circular that first gave local flex its legal basis could not be reached (DNS failure).
