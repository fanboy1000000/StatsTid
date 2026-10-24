# Agreement cells C1–C7 re-verified — research verdict (S146 track D4)

**Date:** 2026-10-23. **Method:** a research pass (`general-purpose`, Opus 5.5, web search, 122 tool calls) on the seven agreement cells left open after the verified overtime/flex dossier (`docs/references/overtime-flex-model-research.md`, V1–V12, which this re-uses rather than redoes), then an independent adversarial verification that re-downloaded and re-read every decisive source (Opus 5.5, 92 tool calls). **Status: verified cell by cell.** Where the verification corrects the first pass, the verification governs (Part 1); the first pass is kept verbatim in Part 2. Its verdicts are authoritative for the source-register fields they touch (S146 ruling R-19); the register rows they changed are listed below.

## The cells and the verified verdicts

| Cell | Question | Verified verdict | What StatsTid should encode (direction for the S147 model program) |
|---|---|---|---|
| **C1** | Which public holidays shorten the monthly norm | **PARTLY.** Agreement facts CONFIRMED: Christmas Eve, New Year's Eve and Grundlovsdag are **not** holidays (022-26, remark to § 4 stk. 2); Grundlovsdag earns +25 % from 12:00 and 24 Dec from 14:00 (§ 13); Store Bededag was compensated through holiday pay (2.02 %), not working time. The first pass's code-impact claim is **contradicted**: StatsTid's norm reads no holiday at all (QUAL-222), so Grundlovsdag does not lower the June norm today | Split the holiday table into "lowers the norm" days and timed supplement windows **before** the norm subtracts holidays (else every June becomes wrong). Local customary days off (5 Jun, 24 Dec, 31 Dec, 1 May) are institution-configurable, dated and audited |
| **C2** | Weekend, holiday and unsocial-hours supplements (HK/PROSA) | **CONFIRMED — the encoded table is contradicted.** One +25 % supplement for weekdays 17:00–06:00, weekends, public holidays, Grundlovsdag after 12:00 and 24 Dec after 14:00 (§ 13); ordered weekend/holiday work +50 % on top and outside the monthly hours count (§ 14). No 1.50 night rate, no Saturday/Sunday difference, no 2.0 Sunday rate for general HK/PROSA (the only 100 % is HK Bilag 9, laboratory technicians at sea/fieldwork) | A single 25 % window set plus the § 14 ordered-weekend compensation; QUAL-227 |
| **C3** | The HK pay-ceiling exemption | **CONFIRMED.** HK § 4 stk. 2 (special positions): base pay ≥ 441,037 kr. (31 March 2012 level) → the chef-pay agreement §§ 5–6 apply and the person is exempt from overtime **and** merarbejde pay. PROSA (019-26, which replaced 079-25 for OK26) has no ceiling | Model the exemption for HK special posts; QUAL-223 (watch row) resolved into the program |
| **C4** | Part-timers' hours above their contracted hours | **CONFIRMED as ruled; state implementing text still missing.** The 4 Feb 2026 award FV2025-795 binds the state employer (back to 20 Jan 2000); no state circular implementing it as of 23 Oct 2026 (MedSt's list checked back to Oct 2025); circular 022-26 still says 1:1. (The AC settlement item is no. 16; FV2025-528 is a different, private-sector ruling) | A dated, switchable rule; the default is an owner ruling with legal input (S147) |
| **C5** | AC's norm period | **CONFIRMED (PARTLY).** The AC agreement sets no period ("gennemsnitlig 37 timer ugentlig"); extra work is reported at least quarterly; certain teachers and special/chief consultants are on an annual norm. "Quarterly" for office staff stays MEDIUM confidence. Never weekly | Monthly flex settlement plus quarterly extra-work assessment, to be confirmed by a domain expert (S147) |
| **C6** | Local flex-cap behaviour (sample) | **CONFIRMED** for the spot checks (SDU 2026: +37 / −7.4 h monthly; the AU template: +37 / −10 h, forfeit at month end unless the manager agrees) | Nine parameters configurable per local agreement (Part 2, C6) |
| **C7** | Who chooses time off vs pay | **CONFIRMED (PARTLY).** The employer chooses for HK, PROSA and AC; time off is the primary path; the employee may only agree to postpone time off (≤ 1 year) and convert to saved days at the deadline. OK26 circular 024-26 abolished the separate "save time off" agreement from 1 April 2026 | `DefaultCompensationModel = AFSPADSERING`, `EmployeeCompensationChoice = false` for HK, PROSA and AC (QUAL-221) |

**Where a holiday changes a figure in StatsTid today (verified in code):** `SupplementRule.cs:54-66` pays every hour on 5 June at the 2.0 holiday rate, mapped to `SLS_0440` (the agreement: +25 % from 12:00 only); the send-month check (`ApprovalEndpoints.cs:2106-2150`) expects no entry on 5 June while the norm still counts it; the demo builder (`AllocatedMonthBuilder.cs:83-86`) skips 5 June. Registered as QUAL-227.

**Needs an owner ruling or a domain expert (for the S147 model refinement):** (1) the order of work: split the holiday table before the norm subtracts holidays; (2) how institutions configure customary days off; (3) the part-timer default until the state's implementing text exists (legal input); (4) PROSA pay groups 3–4 that do not meet the extra-work conditions; (5) the AC office-staff period (quarterly) and the employer-choice rule's basis for HK/PROSA (MedSt's guide; the agreement text is silent).

**Source-register rows updated from these verdicts (2026-10-23):** SR-HK-OK24-009 … SR-HK-OK24-014 (C2: `disputed? = true`, `confidence_level = LOW`); SR-AC-OK24-020 (C5: `LOW`); SR-HK-OK24-020 (C7: `LOW`); SR-AC-OK24-005 (C7: value confirmed by AC § 14 stk. 5, `disputed? = false`, `HIGH`, the citation corrected in `notes`); SR-HK-OK24-021, SR-AC-OK24-006 (C7: value confirmed, noted); SR-PROSA-OK24-001 (C2, C7: inherits, noted).

---

# Part 1 — The independent verification (governs)

## 1. Net verdict for the product owner (plain language)

**The first pass's legal findings hold up.** I re-read every source it used and found the same Danish text
in all of them. I also found a few things it missed (below). Two of its statements are wrong:

1. **The headline June claim is wrong.** The first pass says StatsTid lowers every June norm by 7.4 hours
   because Grundlovsdag (Constitution Day, 5 June) is in the holiday table. A **norm** is the hours a person owes in a period.
   StatsTid's norm never reads the holiday table: no public holiday lowers any norm today. The project
   already has this registered as QUAL-222 (a quality finding in the project's register). So Grundlovsdag has **no** effect on the June norm. In
   June 2024 and June 2026, StatsTid's norm happens to equal the agreement's norm. In June 2025 StatsTid's norm
   is 7.4 h **too high**. The cause is Whit Monday (2. pinsedag, 9 June 2025), not Grundlovsdag.
   **Where Grundlovsdag does change a figure today:**
   - **HK/PROSA supplement pay:** an hour worked on 5 June earns the "holiday" rate of 2.0 for the whole day.
     The agreement pays +25 % from 12:00 only.
   - **The send-month check:** it does not ask for an entry on 5 June.
   - **The demo data:** no time is entered on 5 June.
   **The warning for S147:** once QUAL-222 is fixed and holidays do lower the norm, the June bug the first
   pass describes will *appear* unless Grundlovsdag is first moved out of the norm-lowering list. That order of
   work is the actionable point.
2. **"The OK26 reissue of the PROSA agreement was not found" is wrong.** MedSt (the state employer agency)
   reissued it on 17 June 2026 as 019-26, in force from 1 Apr 2026. It replaces 079-25. I compared the two texts.
   The clauses the cells rely on are unchanged (§ 12 stk. 2, the absence of a pay-ceiling clause, and the 1/1924
   hourly rate). So C3 and C7 stand, but they should cite 019-26.

**One OK26 change the first pass missed.** It bears on C7, who chooses time off or pay. Circular 024-26
(19 Jun 2026) abolished the *Aftale om opsparing af frihed* from 1 April 2026. Under that agreement, since
1 May 2025, an employee could choose to save time off in lieu (afspadsering) and the 6th holiday week
(særlige feriedage). That choice is gone under OK26. The § 11 option to convert untaken overtime time off
into saved days at the deadline still exists. PAV, MedSt's online guide, has not caught up.

**Per cell:** I confirm C2, C3, C4, C5, C6 and C7 as the first pass ruled them, with small corrections. C1 is
right on the agreement facts but wrong on what the error does in StatsTid. Nothing I found reverses a legal verdict.

---

## 2. Verdict table

| Cell | First pass | My verdict | Decisive quote (Danish → English) | Source (URL · §/page) |
|---|---|---|---|---|
| **C1** holidays that shorten the norm | PARTLY | **PARTLY — domain facts CONFIRMED; code-impact claim CONTRADICTED** (see § 5) | *"Helligdage er skærtorsdag, langfredag, påskedag, 2. påskedag, Kristi himmelfartsdag, pinsedag, 2. pinsedag, juledag, 2. juledag og nytårsdag. Juleaftensdag, nytårsaftensdag og grundlovsdag er ikke helligdage."* → "The holidays are Maundy Thursday … New Year's Day. Christmas Eve, New Year's Eve and Constitution Day are not holidays." Also PAV 26.2.8.3: *"Disse dage er som udgangspunkt almindelige arbejdsdage."* → "These days are as a starting point ordinary working days." | 022-26, remarks to § 4 stk. 2, PDF p. 12, https://cirkulaere.medst.dk/media/jhshf3w2/022-26.pdf ; PAV 26.2.8.3, https://pav.medst.dk/pav/kapitel-26-tjenestefrihed-mv/ |
| **C2** supplements (HK/PROSA) | CONTRADICTED (StatsTid's table) | **CONFIRMED** (the table is contradicted by the sources) | § 13: *"For arbejde i tidsrummet fra kl. 17 til kl. 06 eller i weekender, på søgnehelligdage, grundlovsdag efter kl. 12 samt juleaftensdag efter kl. 14 ydes et tillæg svarende til 25 pct. af nettotimelønnen"* → one 25 % supplement for 17–06, weekends, søgnehelligdage (public holidays on a weekday), Grundlovsdag after 12 and Christmas Eve after 14. § 14: *"Arbejde i weekender eller på søgnehelligdage godtgøres med afspadsering af samme varighed med et tillæg på 50 pct. eller med timeløn med et tillæg på 50 pct."* + stk. 2 *"indgår ikke i arbejdstidsopgørelsen"* + stk. 3 *"Der ydes herudover tillæg efter § 13."* → weekend and holiday work: +50 %, kept out of the hour count, plus the 25 % | 022-26 § 13, PDF p. 20; § 14, PDF p. 21; remarks pp. 20–22 (same URL) |
| **C3** HK pay ceiling | CONFIRMED (not encoded) | **CONFIRMED**, refined: the clause sits in § 4 *Særskilte stillinger* (special positions) | *"Stk. 2. Hvis basislønnen udgør 441.037 kr. og derover i årligt grundbeløb, finder cheflønsaftalens §§ 5-6 tilsvarende anvendelse."* Remark: *"Ansatte omfattet af stk. 2 undtages for reglerne om overarbejds- og merarbejdsbetaling"*. § 3 stk. 4: *"Alle basislønninger og tillæg er angivet i årligt grundbeløb pr. 31. marts 2012."* → base pay ≥ 441,037 kr. (2012 level) means the chef-pay agreement §§ 5–6 apply and the person is exempt from overtime **and** merarbejde (extra-work) pay | 085-25 § 4 stk. 2 + remark, PDF p. 11; § 3 stk. 4, PDF p. 10, https://cirkulaere.medst.dk/media/m1hpbtsq/085-25.pdf |
| **C4** part-timers' band | UNRESOLVED (implementing text) / CONFIRMED (law) | **CONFIRMED as ruled.** Status evidence strengthened (see § 3) | *"Medarbejder- og Kompetencestyrelsen skal … anerkende, at det er i strid med direktiv 97/81/EF … at deltidsansatte medarbejdere, for hvem der findes sammenlignelige fuldtidsansatte, efter de i kendelsens bilag A angivne bestemmelser kun skal have overtidshonorering for arbejdstimer, der præsteres ud over den normale arbejdstid for fuldtidsansatte"* + *"den tidsmæssige udstrækning af påstand 1 gælder fra den 20. januar 2000."* → MedSt must accept that paying part-timers overtime only above the full-time norm breaches the part-time directive, back to 20 Jan 2000 | FV2025-795 (4 Feb 2026), operative part, PDF p. 117; Annex A state section (§ 8 stk. 2; HK Bilag 9), PDF pp. 126–127, https://arbejdsretten.dk/media/bork3lzb/fv2025-795.pdf |
| **C5** AC norm period | PARTLY | **CONFIRMED (PARTLY)** | AC § 13 stk. 1: *"Lønnen efter denne overenskomst forudsætter fuldtidsbeskæftigelse svarende til gennemsnitlig 37 timer ugentlig."* § 14 stk. 3: *"Indberetningen skal afgives ved merarbejdets ophør og mindst hvert kvartal."* PAV 19.3.4: *"Arbejdstid som begreb er ikke nærmere fastlagt i overenskomsten."* → pay assumes 37 h a week **on average**; extra work is reported at least quarterly; the agreement does not define a working-time period | CIR1H 9333 § 13, PDF p. 21; § 14 stk. 3, PDF p. 24, https://www.retsinformation.dk/eli/retsinfo/2025/9333/pdf ; PAV 19.3.4, https://pav.medst.dk/pav/kapitel-19-arbejdstidsregler-mv/ |
| **C6** local flex caps (spot check of 2) | Survey | **CONFIRMED** for SDU 2026 and the AU template; AU Foulum figures also confirmed | SDU 3.3: *"Ved den månedlige opgørelse kan en medarbejder ansat på fuldtid højst have 7,4 timer i underskud og 37 timer i overskud."* → at the monthly settlement a full-timer may be at most 7.4 h under and 37 h over. AU § 5 stk. 4: *"Overskudstimer, der ligger ud over det fastsatte maksimum, bortfalder ved opgørelsesperiodens (månedens) udgang og kan ikke udløse kontant betaling, medmindre andet aftales med lederen."* → surplus above the cap is lost at month end and earns no cash unless the manager agrees. AU § 6 stk. 2: minus cap 10 h; AU § 4 stk. 3: *"maksimalt godskrives 13 fleksarbejdstimer pr. arbejdsdag"* (at most 13 flex hours credited per working day) | SDU pp. 2–3, https://sdunet.dk/-/media/sdunet/filer/servicesider/hr/ansaettelsesforhold/flekstidsaftale-for-sdu-2026-underskrevet.pdf ; AU template §§ 4–6, 12, https://medarbejdere.au.dk/fileadmin/www.medarbejdere.au.dk/hr/Arbejdstid/Arbejdstid_-_skemaer/Skabelon_for_flekstidsaftale.pdf |
| **C7** who chooses time off vs pay | PARTLY | **CONFIRMED (PARTLY)**, plus an OK26 note (024-26) | PAV 19.2.7.1: *"godtgøres de overskydende timer efter arbejdsgiverens valg med afspadsering … med timeløn … eller med en kombination heraf."* → the employer chooses. 022-26 remark to § 10: *"medmindre den ansatte vælger at få den tilgodehavende afspadsering konverteret til opsparingsdage"* → the employee's only election is saved days at the deadline. AC § 14 stk. 5: *"ydes så vidt muligt i form af afspadsering"* → time off where possible | PAV 19.2.7.1 (same PAV URL); 022-26 remarks to § 10 stk. 1, PDF p. 18; CIR1H 9333 § 14 stk. 5–6, PDF pp. 24–25 |

---

## 3. Per-cell notes: what I checked, what held, what I added

### C1 — holidays and the norm
- **Holds:** the closed list and the three named exclusions (022-26 p. 12). PAV 19.2.4.1 adds 1 May:
  *"1. maj, grundlovsdag, juleaftensdag og nytårsaftensdag er ikke helligdage."* ("1 May, Constitution Day,
  Christmas Eve and New Year's Eve are not holidays.") PAV 26.2.8.2 says 1 May is time off on request and,
  in MedSt's view, unpaid unless local custom says otherwise. AC § 13 stk. 8, *"Der tilkommer de ansatte
  fridage efter de for tjenestemænd gældende regler"* (AC staff get days off under the civil servants' rules),
  is on PDF p. 22. Store Bededag (General Prayer Day) is still a holiday in Greenland and the Faroes (022-26 p. 12).
- **Store Bededag:** Act 214, § 1 (*"at sikre, at store bededag anses som en almindelig arbejdsdag"*, to
  ensure Store Bededag counts as an ordinary working day) and § 5 (*"Loven træder i kraft den 1. januar 2024"*,
  in force 1 Jan 2024) are confirmed. So is the § 3 supplement of 0.45 % (https://www.retsinformation.dk/eli/lta/2023/214/pdf).
  The ferie circular CIR1H 9298, which retsinformation lists as in force (*Gældende*) on 23 Oct 2026, says:
  *"forhøjelse af den særlige feriegodtgørelse til 2,02 pct. pr. 1. april 2024. Samtidig ophører ret til
  løntillæg efter lov nr. 214"* (the special holiday allowance rises to 2.02 % from 1 Apr 2024, and the Act 214
  supplement ends). It keeps 1.5 % on the Faroes (general remarks p. 1; § 10 and § 10 stk. 5). The combined
  OK26 settlement text still carries *"2,02 pct."* (tl.dk combined OK26 PDF, p. 19 of 47).
- **The scope of FV2016.0107 is narrower than the first pass suggests.** The case concerned AC staff at SKAT
  and Spillemyndigheden only. The umpire found a *"lignende praksis"* (similar practice) on *"langt de fleste
  statslige arbejdspladser"* (most state workplaces). He also found *"at der ikke har været en ensartet praksis
  med hensyn til, om man kan holde fri med eller uden løn"* (no uniform practice on whether the time off was
  paid). He expressly did **not** decide the *"mellemløsning"*, under which a custom may be ended mid-period
  but only takes effect when the agreement period ends (PDF pp. 10–12). So "any day off on these days is a
  custom the employer can withdraw" is a fair reading of one case. It is not a general rule.
- **New corroboration:** the DTS newsletter of 23 April 2025 (DTS is the tax-staff union): *"Grundlovsdag og
  Nytårsaftensdag er nu igen kutymefridage på Skatteministeriets område"* (Constitution Day and New Year's Eve
  are again customary days off in the tax ministry). It adds that the reinstatement was *paid for*, partly by
  postponing supplement payouts (https://www.dts.dk/ref.aspx?id=6431). This supports "local, negotiable,
  configurable per institution". It also shows that the institution at the centre of FV2016.0107 has since
  reinstated two of the days. The Danish Defence page is confirmed as cited: *"Civilt personel ydes
  tjenestefrihed med løn hele den 5. juni"* (civilian staff get paid time off all of 5 June), updated 7 May 2025.
- **What fails:** the impact paragraph ("understates every June norm by 7.4 h … 7.4 h of surplus"). See § 5.

### C2 — supplements
- **Holds, verbatim:**
  - § 13 is the single 25 % window, with a minimum of 32.77 kr. at the 2012 level.
  - The remarks list the windows, and the supplement applies only to ordered or rostered work.
  - The supplement is paid *"uafhængigt af eventuel overarbejdsgodtgørelse … eller … weekendgodtgørelse"*
    (regardless of overtime or the weekend compensation). It also covers staff exempt from overtime under § 8 stk. 3.
  - § 14: +50 %, the hours are kept out of the § 7 count, and § 13 is paid on top. The remarks on p. 22 add:
    *"Der ydes ikke herudover … godtgørelse for over- eller merarbejde"* (no overtime or extra-work pay on top).
  - PAV 19.2.8.1: *"efter arbejdsgiverens valg"* (at the employer's choice).
  - PAV 19.2.9.1: *"1/4 times afspadsering pr. præsteret arbejdstime"* (¼ hour of time off per hour worked).
  - HK Bilag 9 § 1 stk. 3: *"Timeløn med tillæg af 100 pct. for 4. og følgende timer og for tjeneste på søn- og
    helligdage"* (hourly pay + 100 % from the 4th hour and for Sunday and holiday duty). This is narrow: lab
    technicians on research cruises and fieldwork (085-25, PDF p. 63).
  - A full-text search of 085-25, 079-25 and 019-26 for "søndag", "weekend", "ulempe" and "100 pct" finds only Bilag 9.
  - HK's member page says: *"Du skal have et tillæg på 25 procent af timelønnen … efter klokken 17, i weekenden, på
    helligdage, grundlovsdag efter klokken 12 samt juleaftensdag efter klokken 14"* (25 % after 17:00, at
    weekends, on holidays, Grundlovsdag after 12 and Christmas Eve after 14). It adds *"+50 procent for alt
    arbejde på disse dage"* (+50 % for all work on these days). https://www.hk.dk/raadogstoette/arbejdstid/arbejdstid-stat
- **Refinements:**
  1. **Shift workers (chapter 4).** CO II, a central union organisation, has its own evening and night
     agreement. Staff under it get a **25 %** supplement for 17–06 instead of the 37/3 time off (3 hours off per
     37 hours worked in the evening and at night) and night pay (PAV 19.2.9.2). That is still not a 1.50 night rate.
  2. **AC.** "No unsocial-hours supplement" is right in general (PAV 19.3.1.8 and 19.3.4: *"der betales ikke
     ulempegodtgørelse, natpenge mv."*, no unsocial-hours supplement, night pay etc. is paid). But PAV notes
     that AC Bilag 6 has *"særskilt betaling for rådighedsvagt og weekendtjeneste mv. for specifikke områder"*
     (separate pay for on-call and weekend duty in specific areas). AC staying switched off is correct for the
     general AC profile.
  3. **24 December.** StatsTid has no 14:00 flag, so in the HK/PROSA engine 24 Dec afternoon earns only the
     17:00+ evening rate.
- **Contrary evidence searched for:** a state HK/PROSA Sunday 100 % rate or night 50 % rate. I found none.
  Web hits for "100 % on Sundays" are generic FAQ pages about private-sector HK agreements. That fits the first
  pass's guess that 1.5/2.0 came from the private sector, but I did not trace it either.

### C3 — HK pay ceiling
- **Holds, verbatim:** 085-25 § 4 stk. 2 and its remark (p. 11), and § 3 stk. 4 (p. 10). Bilag 6 § 9 stk. 4
  (Skatteforvaltningen pay group 6, Sats I = 441,037 kr.) carries the remark *"De ansatte er således undtaget for
  reglerne om overarbejds- og merarbejdsbetaling efter Statens arbejdstidsaftale"* (these staff are exempt from
  overtime and extra-work pay; PDF p. 54). The chef-pay agreement 059-19 § 5, p. 9, is confirmed. Its stk. 2
  reads in full: *"De ansatte er undtaget fra reglerne om merarbejde for det pågældende ansættelsesforhold."*
  (staff are exempt from the extra-work rules in that employment.) There is one further exception: no one-off
  payment for extra work to staff in posts carrying a top-chief supplement (topcheftillæg). 022-26's remark to
  § 9 (p. 17) cites *"§ 5, stk. 2, i aftale af 3. juli 2019 om chefløn"*. PAV 19.3.3: *"basisløn på 441.037 kr.
  (grundbeløb niveau 31. marts 2012) eller derover"* (base pay of 441,037 kr. or more at the 2012 level).
- **Refinement: the scope.** The threshold sits in § 4, *Særskilte stillinger*. These are posts whose base pay
  is set locally above the top pay groups, capped at pay-grade (lønramme) 36 level. The highest ordinary HK
  base pay is 369,606 kr. (pay group IV, p. 18). So in practice the ceiling can only bite for a § 4 special post
  or Skatteforvaltningen pay group 6. StatsTid can derive it from the position classification as well as from a
  base-pay comparison.
- **PROSA:** the OK26 text **019-26** (Cirkulære af 17. juni 2026, effective 1 Apr 2026,
  https://cirkulaere.medst.dk/media/xfcdpmfq/019-26.pdf) has the same § 12 stk. 2: *"Ansatte i løngruppe 3 og 4
  er omfattet af merarbejdsgodtgørelse, når de opfylder betingelserne herfor i Statens Arbejdstidsaftale."*
  (pay groups 3–4 get extra-work pay when they meet the state agreement's conditions.) It has no ceiling
  clause. Its highest base pay is still 343,393 kr. A diff against 079-25 shows changes only to pension, dates
  and the deletion of one study clause.

### C4 — part-timers
- **Holds:**
  - The operative part (PDF p. 117) and Annex A (PDF pp. 126–127) list *"Statens arbejdstidsaftale … § 8, stk. 2"* and HK Bilag 9.
  - The remedy reasoning is *"ske ved, at deltidsansatte modtager overtidshonorering ved arbejde udført i spændet …"*
    (compliance comes from paying part-timers overtime for the band between their own norm and full time; PDF p. 114).
  - On limitation of back-pay claims, the umpires write *"ikke taget stilling til spørgsmålet om forældelse"*
    (they did not decide it; PDF p. 116).
  - 022-26 § 8 stk. 2 is unchanged (PDF p. 15).
  - PAV 19.3.1.6 still has the full-time-norm text.
  - CFU item 28 is verbatim (CFU, the Centralorganisationernes Fællesudvalg, is the joint committee of the central union organisations).
- **Corrections:**
  1. In the AC settlement the same text is **item 16**, not item 28 (https://medst.dk/media/0rmlepno/forlig-ok26-medst-ac.pdf).
  2. A sibling award of the same date, **FV2025-528** (CO-industri v. DIO I, private industry), exists. It is
     not a state rule and should not be confused with FV2025-795.
  3. HK's page attributes the change to *"EU-domstolen"* (the EU Court). It is in fact the Danish
     labour-arbitration award.
- **Status, strengthened:** the first pass saw only page 1 of MedSt's circular list. I paged through pages 1–8,
  from 16 Oct 2026 back to Oct 2025. There is **no** circular on part-timers' overtime and no amended working-time
  agreement after 022-26 (18 Jun 2026). There is also no HK organisation-agreement reissue. The union "still
  outstanding" signals are older than the first pass implies: DSR's OK26 page shows news only up to April 2026.
  The verdict holds. The "no implementing agreement" finding now rests on the MedSt list, not only on undated union pages.

### C5 — AC norm period
- **Holds:**
  - § 13 stk. 1 and its *"ikke tilsigtet nogen ændring af hidtidig praksis"* remark (no change to existing practice intended).
  - § 14 stk. 2 (*"normalt ikke må være mindre end 4 uger"*, normally at least 4 weeks) and § 14 stk. 3 (*"mindst hvert kvartal"*, at least quarterly).
  - § 14 stk. 5 (time off before the end of the following quarter).
  - Bilag 6: *"Indberetning om merarbejde og fastsættelse af merarbejdets omfang skal ske mindst én gang årligt
    (årsnorm)"* (extra work is reported and settled at least once a year, an annual norm; PDF p. 76).
  - The Djøf guide quote in FV2023-915 (p. 16).
  - FV2023-915 8.4.2: *"Der er enighed om, at disse regler bygger på en årsnorm"* (it is agreed these rules rest
    on an annual norm). This is on **PDF p. 23** (8.4.2 begins on p. 22), and it is common ground between the
    parties, as the first pass says.
  - PAV 19.3.4.4: 20 h a quarter for fuldmægtige (case officers).
  - PAV 19.5.5: monthly flex settlement is typical.
- **OK26:** item 21 of the AC settlement rewrites § 14 stk. 5, 3rd sentence, to *"Afspadsering kan efter aftale
  med den ansatte udskydes i op til 1 år"* (time off may be postponed by up to a year by agreement with the
  employee). Confirmed (V9). PAV 19.3.4.5 still shows the older *"I særlige tilfælde"* (in special cases) wording.
- **Weak contrary signal:** Djøf's current member page no longer mentions a quarterly norm. It says only *"Hvis
  du er ansat i staten har du som udgangspunkt ikke ret til at få kompenseret dit merarbejde"* (in the state you
  have, as a starting point, no right to compensation for extra work). The "quarterly" default therefore rests
  on one Djøf guide quoted in a 2025 award plus the quarterly reporting floor. MEDIUM confidence is right.

### C6 — local flex caps (spot check)
- **SDU 2026:** monthly settlement and approval by the 5th; caps of 37 plus and 7.4 minus; 3.4 special-case
  agreement; 3.6 no cash; 5.2 exit; 2.4.2 ordered work outside flex; in force 1 Apr 2026 (8.1). Signatures
  19–23 Mar 2026 by shop stewards (TRs) for Djøf/DM, Prosa, HK/Stat and others. **All confirmed.**
  Special and chief consultants are excluded.
- **AU template:** coverage excludes managers, special and chief consultants and fuldmægtige (§ 3 stk. 2).
  Monthly settlement. Plus cap 37 *"medmindre andet aftales med lederen"* (unless agreed with the manager).
  Minus cap 10 h. 13 h credited per working day. Forfeit at month end. 160.33 h kontortid (fixed office hours)
  rule. § 12 exit, including *"Et underskud vil kunne give anledning til reduktion i løn"* (a deficit may lead to
  a pay deduction). **All confirmed.**
- **AU Foulum (FV2019.0076):** 100 h plus, 200 h for field and stable staff, agreement of 31 Jan 2014 for
  "TAB og AC-TAB" (technical/administrative staff, including AC). Confirmed.

### C7 — who chooses time off or pay
- **Holds:**
  - PAV 19.2.7.1 and 19.2.8.1 ("efter arbejdsgiverens valg", at the employer's choice).
  - § 10 stk. 1: postponement *"efter aftale med den ansatte"* (by agreement with the employee).
  - The remark on saved days (opsparingsdage) and early payment at the employer's judgement (p. 18).
  - § 11: the employee's election.
  - AC § 14 stk. 5–6 and AC § 15 (*"kan den ansatte i stedet for merarbejdsbetaling vælge"*, the employee may
    choose saved days instead of extra-work pay).
  - HK's page: *"Hvis du har overtid, skal du afspadsere, inden der er gået 3 måneder. Har du ikke mulighed for
    det, har du ret til i stedet at få betaling"* (take time off within 3 months; if you cannot, you have a right to pay).
- **Caveat on the evidence:** for HK/PROSA the "employer's choice" comes from MedSt's own guide (PAV). 022-26
  § 8 itself is silent on who chooses, as the first pass acknowledges. A union could argue otherwise, but I found
  no union text that does. Djøf, for AC case officers, agrees: the manager decides *"om det skal være i form af
  en udbetaling eller som afspadsering"* (whether it is paid or taken as time off).
- **OK26 addition:** 024-26 (https://cirkulaere.medst.dk/media/xewhlj0b/024-26.pdf) abolishes the *Aftale om
  opsparing af frihed* (047-25) from 1 Apr 2026: *"Der kan ikke efter aftalen optjenes ny frihed fra og med den
  1. april 2026"* (no new time can be saved under it from 1 April 2026). Balances saved before then can be taken
  or paid out until 31 Dec 2027. PAV 19.4 still describes the scheme. If StatsTid ever modelled that election, it
  is now dated to 1 May 2025 – 31 Mar 2026. The § 11 conversion of overtime time off into saved days is
  unaffected: 022-26 § 11 is in the 18 Jun 2026 text. The settlement changed only § 11 stk. 2, to a 111-hour
  floor (tl.dk combined OK26 PDF).

---

## 4. Contradicting evidence found (including weaker evidence)

| Cell | Evidence | Weight | Effect |
|---|---|---|---|
| C1 | DTS, 23 Apr 2025: Grundlovsdag and New Year's Eve reinstated as customary days off in the tax ministry, paid for locally | Medium (union newsletter, specific) | Does not contradict "local custom". It **does** show that the FV2016.0107 withdrawal was reversed at that very employer, so "any custom can be withdrawn" is not a stable fact to encode |
| C1 | FV2016.0107: "no uniform practice" on whether the day off was paid; the *mellemløsning* was not decided | Medium (primary) | The first pass overstated the ruling's generality |
| C2 | AC Bilag 6 area-specific weekend-duty payments | Low | AC "disabled" is right only for the general AC profile |
| C4 | FV2025-528 (private industry, same date) | — | Not contradicting; a citation trap |
| C5 | The current Djøf page no longer mentions a quarterly norm | Low | Keeps "quarterly" at MEDIUM |
| C7 | PAV is the employer's guide; the agreement text is neutral | Low–Medium | The employer-choice reading is MedSt's; no union text disputes it |
| C3/C7 | PROSA 019-26 supersedes 079-25 | — | Same text; citation update only |

---

## 5. C1 code-impact finding: where Grundlovsdag changes a figure in StatsTid today

**Short answer:** Grundlovsdag being listed changes **no norm** and **no absence or vacation figure**. It
changes three things:
- **(a)** the HK/PROSA supplement lines and their payroll export;
- **(b)** the month-send completeness check;
- **(c)** the demo data.

The first pass's "understates every June norm by 7.4 h" is **contradicted** by the code.

| Area | Does the holiday list reach it? | Evidence (file:line) | Effect of Grundlovsdag being listed |
|---|---|---|---|
| **Norm (engine)** | **No** | `src/SharedKernel/StatsTid.SharedKernel/Calendar/DayNorm.cs:27-29` (*"public holidays do NOT reduce the norm"*), `:51-56` (weekday test reads only `DayOfWeek`); used at `src/RuleEngine/StatsTid.RuleEngine.Api/Rules/WeeklyTimelineCore.cs:173-175, 209, 300` | None. 5 June is owed like any weekday (7.40 h full-time). |
| **Norm (screens: balance, month approval, Skema day norm)** | **No** | `src/Backend/StatsTid.Backend.Api/Endpoints/BalanceEndpoints.cs:165-180` (comment *"public holidays do not reduce the norm here or in the engine"*); `ApprovalEndpoints.cs:1046-1131` (month norm = weekdays × `DayNorm.ForWeekday`); `Services/DailyNormCalculator.cs:92, 184` | None. |
| **Absence / vacation consumption** | **No** | `src/RuleEngine/StatsTid.RuleEngine.Api/Rules/AbsenceRule.cs:122-126` (`DayNorm.ForWeekday`, no calendar); `docs/knowledge-base/decisions/ADR-032-vacation-consumption-correctness.md:47` (*"vacation booked on e.g. Grundlovsdag resolves a positive norm and consumes"*) | None. (For 5 June this happens to be right: it is a working day. For real søgnehelligdage it is the QUAL-222 gap.) |
| **Supplements → payroll** | **Yes** | `src/RuleEngine/StatsTid.RuleEngine.Api/Rules/SupplementRule.cs:54-66` (holiday branch: every hour of a timed entry on a listed day → `HOLIDAY_SUPPLEMENT` at `config.HolidayRate`, then `return`, so no evening line); `src/SharedKernel/StatsTid.SharedKernel/Calendar/DanishPublicHolidays.cs:49` (Grundlovsdag in the list); rate default `src/SharedKernel/StatsTid.SharedKernel/Models/AgreementRuleConfig.cs:40` (`HolidayRate = 2.0m`); seeds `docker/postgres/init.sql:1468, 1474, 1480` (HK OK24/OK26, PROSA OK24: `holiday_supplement_enabled TRUE`, `holiday_rate 2.0`); export mapping `init.sql:234-235, 304-305` (`HOLIDAY_SUPPLEMENT → SLS_0440`) | **Yes, a payroll-visible figure.** For HK/PROSA, every registered hour on 5 June (08:00–12:00 too) emits a holiday-supplement line at 2.0. The agreement gives +25 % from 12:00 only, and § 14's +50 % does not apply (it is not a søgnehelligdag). AC is unaffected (supplements disabled). |
| **Month-send completeness check** | **Yes** | `src/Backend/StatsTid.Backend.Api/Endpoints/ApprovalEndpoints.cs:2106-2118` (reads `danish_public_holidays`), `:2140-2150` (`if (holidays.Contains(d)) continue;`); rows `docker/postgres/init.sql:383, 396, 413` | 5 June is not an "expected workday", so a month can be sent with 5 June empty. The norm still asks 7.4 h for that day, so an empty 5 June turns into a **silent 7.4 h deficit** that the check does not flag. Per the agreement, 5 June *is* an expected working day. |
| **Demo data** | **Yes** | `tools/StatsTid.DemoSeed/Generation/DanishHolidays.cs:68` (Grundlovsdag), `:76-78` (`IsExpectedWorkday`); `tools/StatsTid.DemoSeed/Generation/AllocatedMonthBuilder.cs:83-86` (skips such days) | Demo employees log nothing on 5 June (or on any listed weekday holiday). Their June balances therefore show a **deficit** of 7.4 h per listed weekday holiday, not a surplus. |
| **Skema / frontend** | **No calendar** | `frontend/src` has no holiday calendar; only config labels `components/config/ProfileEditor.tsx:68, 77` (`holidaySupplementEnabled`, `holidayRate`) | 5 June shows as an ordinary 7.40 h day. |
| `IsWorkingDay` / `CountWorkingDays` | **No production caller** | `DanishPublicHolidays.cs:78-97`; callers only in `tests/StatsTid.Tests.Unit/DanishPublicHolidaysTests.cs` | None. |

**Worked check (full-timer, agreement norm = 7.4 × (calendar days − weekend days − weekday søgnehelligdage)):**
- **June 2024:** 20 weekdays, no søgnehelligdag. Agreement 148.00 h; StatsTid 148.00 h. Equal.
- **June 2025:** 21 weekdays; 2. pinsedag is Monday 9 June. Agreement 148.00 h; StatsTid 155.40 h, so
  StatsTid is **7.4 h too high**. The cause is QUAL-222 (holidays never lower the norm), not Grundlovsdag.
- **June 2026:** 22 weekdays, no søgnehelligdag. Agreement 162.80 h; StatsTid 162.80 h. Equal.

**The forward risk, which is the actionable part.** QUAL-222 (`docs/operations/quality-finding-register.md:446`)
schedules the norm to start subtracting holidays in the S147 model program. If that fix reads today's list
(`DanishPublicHolidays.GetHolidays` or `danish_public_holidays`), it **introduces** exactly the bug the first pass
describes: every June norm on a weekday 5 June would be 7.4 h too low. Two holiday flags are needed first:
- `søgnehelligdag`: lowers the norm and triggers § 14;
- `ulempe-window` with a start time: Grundlovsdag 12:00, 24 Dec 14:00.

Grundlovsdag must be moved to the second flag before the norm change. The same split is needed for the send
check and the demo generator, or they will disagree with the new norm.

Minor data note: `docker/postgres/init.sql:404-415` labels the 1 Jan – 31 Mar 2026 holidays `OK26`, but those
dates fall under OK24 (OK26 starts 1 Apr 2026). It is harmless today because `ApprovalEndpoints` ignores `ok_version`.

---

## 6. What the first pass got wrong or overstated

1. **C1 impact (material):** "understates every June norm by 7.4 h … 7.4 h of surplus they did not earn" is
   false. The norm has no holiday awareness (§ 5). The Grundlovsdag error shows up instead as a 2.0 supplement
   on 5 June for HK/PROSA, a send check that skips 5 June, and demo deficits. It also left out the S147
   ordering risk.
2. **C1 code citations drifted:** `ApprovalEndpoints.cs:2071-2083` is now `:2106-2150`. The claim that the C#
   list "drives `IsWorkingDay`/`CountWorkingDays`" is true but irrelevant: neither has a production caller.
3. **C1 FV2016.0107 scope:** presented as settling the question generally. It decided one AC dispute at SKAT
   and Spillemyndigheden, noted no uniform paid/unpaid practice, left the *mellemløsning* open, and SKAT has
   since reinstated two of the days.
4. **"The OK26 reissue of the HK and PROSA organisation agreements was not found":** wrong for PROSA (019-26,
   17 Jun 2026). It appears correct for HK, since no HK reissue is in MedSt's list up to 16 Oct 2026.
5. **C4:** the AC settlement item is **16**, not "the same item 28". The union status pages are as old as April 2026.
6. **C5:** the FV2023-915 common-ground sentence is on PDF p. 23, not p. 22 (trivial).
7. **C7:** it missed 024-26 (OK26 abolition of the *opsparing af frihed* election).
8. **C2:** "AC has no unsocial-hours supplement" omits AC Bilag 6's area-specific weekend-duty payments (low impact).

---

## 7. Needs a domain expert or an owner ruling

1. **C1 / S147 ordering (owner, engineering):** split the holiday table into `søgnehelligdag` (lowers the
   norm and triggers § 14) and timed ulempe windows (Grundlovsdag 12:00, 24 Dec 14:00) **before** QUAL-222
   makes the norm subtract holidays. Otherwise the fix introduces a June error. The send check and demo
   generator should follow the same split.
2. **C1 local customs (owner + HR expert):** how to model per-institution customary days off (5 Jun, 24 Dec,
   31 Dec, 1 May): as paid absence credited with hours, as closure days, or both. The 2025 SKAT reinstatement
   shows these change over time and are negotiated, so they need effective dates and an audit trail.
3. **C4 (owner, with legal input):** which part-timer variant applies by default while no state implementing
   text exists. The award binds MedSt back to 20 Jan 2000, but 022-26 (18 Jun 2026) still prints the old rule.
   Unchanged from the first pass.
4. **C3 (domain expert):** PROSA pay groups 3–4 who do not meet § 8 stk. 3. The literal reading is that they
   fall back to § 8 overtime. Unchanged from the first pass.
5. **C7 (domain expert, low priority):** whether any union disputes that the employer chooses time off or pay
   for HK/PROSA. The source is PAV; the agreement is silent.

---

## 8. Sources re-fetched for this verification (all on 23 Oct 2026)

022-26 (both URLs) · 085-25 · 079-25 · **019-26 (new)** · **024-26 (new)** · CIR1H 9333 · 059-19 ·
CIR1H 9298 · Act 214/2023 (PDF) · FV2016.0107 · FV2025-795 · **FV2025-528 (new, private sector)** ·
OK26 CFU and AC settlements · **combined OK26 state settlement (tl.dk, new)** · FV2023-915 · FV2019.0076 ·
SDU 2026 · AU template · PAV chapters 19 and 26 · MedSt circular list pages 1–8 · HK state working-time page ·
Danish Defence holiday page · DTS newsletter 9/2025 (**new**) · DSR OK26 state page · Djøf overtime page.
Not re-fetched: DTU guideline, SKAT 2008/2014 (historical C6 rows), DLF page.

---

# Part 2 — The research pass, verbatim (superseded where Part 1 corrects it)

## Plain-language summary (for the product owner)

StatsTid encodes several "agreement cells": single facts such as "which days are holidays" or "what does
Sunday work pay". This pass checked seven of them against the state's own texts: the state
working-time agreement (circular 022-26), the HK and PROSA organisation agreements, the AC agreement,
the state employer's guide (PAV), and labour-court rulings.

| Cell | Question | Verdict | What changes for StatsTid |
|---|---|---|---|
| **C1** | Which public holidays shorten the monthly norm? | **PARTLY** | 10 of the 11 rows are right. **Grundlovsdag (5 June) is wrong**: in the state it is an ordinary working day. It does not shorten the norm and it is not a holiday for pay. It only earns the 25 % unsocial-hours supplement from 12:00. **24 December is missing as a flag**: it earns the supplement from 14:00. 31 December and 1 May are ordinary working days under the central rules. Any day off on 5 June, 24 Dec, 31 Dec or 1 May is a **local custom** that the employer can withdraw (arbitration FV2016.0107). Store Bededag is rightly gone from 2024. The state compensated for it through holiday pay (særlig feriegodtgørelse rose from 1.5 % to 2.02 %), not through working time. |
| **C2** | Evening, night, weekend and holiday supplements | **CONTRADICTED** | The state has **one** unsocial-hours supplement: **+25 %** for ordered work on weekdays 17:00–06:00, at weekends, on public holidays, on Grundlovsdag after 12:00 and on 24 Dec after 14:00. There is **no separate night rate of 1.50**. Ordered weekend or holiday work by a day worker gets **+50 %** (time off or pay, the employer chooses) **plus** the 25 %. Saturday and Sunday are treated **the same**, and the hours are kept out of the monthly hour count. No **Sunday rate of 2.0** exists for general HK or PROSA staff in the state. The only 100 % rate is for laboratory technicians on research cruises or fieldwork. |
| **C3** | The HK pay ceiling | **CONFIRMED** (the rule); StatsTid does not encode it | HK staff whose **base pay** is **441,037 kr. or more a year, counted at 31 March 2012 pay levels**, are exempt from both overtime and merarbejde pay (merarbejde = discretionary extra-work pay). Instead the chef-pay agreement applies: one-off payments (engangsvederlag) and individually negotiated supplements. **PROSA has no such ceiling.** In PROSA, pay groups 3–4 are routed to discretionary merarbejde pay instead. The unsocial-hours and weekend supplements still apply to exempt staff. |
| **C4** | Part-timers' hours above their contracted hours | **UNRESOLVED** for the state's implementing text; the legal position is **CONFIRMED** | The 4 Feb 2026 arbitration ruling (FV2025-795) binds the state employer. Paying part-timers only 1:1 up to full time breaches the EU part-time directive, with effect from 20 Jan 2000. **No state implementing agreement or circular was found as of 23 Oct 2026.** The circular 022-26 (18 Jun 2026) still has the old 1:1 text, unions report the "concrete implementation" still under negotiation, and HK says the rules change "during 2026". StatsTid should make this a dated, switchable rule and get an owner ruling on which variant applies. |
| **C5** | AC's norm period | **PARTLY** | The AC agreement sets **no** norm period, only "an average of 37 hours a week". The default is not fixed centrally. In practice the merarbejde period is the **quarter**: merarbejde must be reported at least quarterly, and fuldmægtige's standby duty (rådighedsforpligtelse) is counted per quarter. Certain teachers have an **annual** norm written into the agreement. Special and chief consultants are on an **annual** norm by the parties' common understanding (FV2023-915). Local flex agreements set their own flex period, usually monthly. **It is never weekly.** |
| **C6** | Local flex caps (a sample) | **Survey (no verdict)** | Of seven state examples, the four current or template ones all settle **monthly**. Plus caps range from 37 to 100 h (200 h for field staff), minus caps from 7.4 to 37 h. Above-cap handling varies: forfeit, a manager-agreed exception, transfer to the overtime account, or (historically) 1:1 cash. Exit handling also varies: take the hours during notice, forfeit them, or (historically) cash at 1/1924. All of these must be **configurable per local agreement**, not per AC/HK/PROSA. |
| **C7** | Who chooses time off or pay | **PARTLY** (V10 confirmed and refined) | For **all three agreements the employer chooses**, and time off is the primary path. The employee has only two choices: to agree to postpone time off by up to a year, and to convert untaken time off into saved days (opsparingsdage) at the deadline instead of cash. Encode `DefaultCompensationModel = AFSPADSERING` (time off) and `EmployeeCompensationChoice = false` for **HK, PROSA and AC**. HK and PROSA are `true` today, which is wrong. |

**Why it matters (C1 and C2 are the payroll-visible ones):** because Grundlovsdag sits in the holiday table, StatsTid
**understates every June norm by 7.4 h** (5 June 2024, 2025 and 2026 were all weekdays). Every June, a full-timer
therefore shows 7.4 h of surplus they did not earn. Separately, the supplement table pays Grundlovsdag work all day at the
"holiday" rate of 2.0, and pays Sunday work at 2.0. The state rules give 1.25 from 12:00 for Grundlovsdag and
1.5 + 0.25 for Sunday.

**Terms used below:**
- **norm / norm period (normperiode):** the hours a person owes in a period. The state default is one calendar month (V1).
- **søgnehelligdag:** a public holiday that does not fall on a Sunday. Only these shorten the norm.
- **ulempetillæg:** the 25 % unsocial-hours supplement (§ 13).
- **weekendgodtgørelse:** the +50 % compensation for ordered weekend or holiday work (§ 14).
- **afspadsering:** time off in lieu.
- **opsparingsdage:** saved days (§ 11).
- **merarbejde:** discretionary or claim-based extra-work pay, as opposed to overtime (overarbejde).
- **PAV:** the state employer's official guide.
- **grundbeløb 31.3.2012:** an amount stated at the March 2012 pay level, which is then indexed.
- **kutyme:** a workplace custom.

---

## C1 — Which public holidays shorten the monthly norm

**What StatsTid encodes.** Table `danish_public_holidays` (`docker/postgres/init.sql:372-418`) and the pure
`DanishPublicHolidays.GetHolidays` (`src/SharedKernel/StatsTid.SharedKernel/Calendar/DanishPublicHolidays.cs:36-62`)
both list Nytårsdag, Skærtorsdag, Langfredag, Påskedag, 2. Påskedag, Kristi Himmelfartsdag, Pinsedag,
2. Pinsedag, **Grundlovsdag**, Juledag and 2. Juledag. Store Bededag appears only for OK versions before OK24. The table is used to
compute expected workdays (`ApprovalEndpoints.cs:2071-2083`). The C# list drives `IsWorkingDay`/`CountWorkingDays`
and the holiday supplement (`SupplementRule.cs:56`). There is no row for 24 Dec, 31 Dec or 1 May.

**What the sources say.**
1. **The list of holidays for the norm (decisive).** The circular's remarks to § 4 stk. 2 give a closed list and
   exclude the three days explicitly:
   > *"Søgnehelligdage er helligdage, der ikke falder på en søndag. Helligdage er skærtorsdag, langfredag, påskedag, 2. påskedag, Kristi himmelfartsdag, pinsedag, 2. pinsedag, juledag, 2. juledag og nytårsdag. Juleaftensdag, nytårsaftensdag og grundlovsdag er ikke helligdage. Store bededag er en helligdag for statens ansatte i Grønland og på Færøerne."*
   > → "Søgnehelligdage are holidays that do not fall on a Sunday. The holidays are Maundy Thursday, Good Friday, Easter Sunday, Easter Monday, Ascension Day, Whit Sunday, Whit Monday, Christmas Day, Boxing Day and New Year's Day. Christmas Eve, New Year's Eve and Constitution Day are not holidays. Store Bededag is a holiday for state staff in Greenland and the Faroes."

   022-26, remarks to § 4 stk. 2, PDF p. 12, https://cirkulaere.medst.dk/media/jhshf3w2/022-26.pdf (same text at the alternative URL https://cirkulaere.medst.dk/media/brziigd2/022-26.pdf; the two PDFs differ only in the signature image).
   PAV 19.2.4.1 says the same and adds 1 May: *"1. maj, grundlovsdag, juleaftensdag og nytårsaftensdag er ikke helligdage."* ("1 May, Constitution Day, Christmas Eve and New Year's Eve are not holidays.")
   https://pav.medst.dk/pav/kapitel-19-arbejdstidsregler-mv/ (read 23 Oct 2026).
   **AC staff follow the same list.** AC § 13 stk. 8: *"Der tilkommer de ansatte fridage efter de for tjenestemænd gældende regler."* ("Employees get days off under the rules for civil servants.") CIR1H 9333, PDF p. 22, https://www.retsinformation.dk/eli/retsinfo/2025/9333/pdf.
2. **24 Dec, 31 Dec and Grundlovsdag are ordinary working days. A day off on them is a local custom.** PAV 26.2.8.3:
   > *"Der er ikke fastsat generelle regler om frihed på grundlovsdag, juleaftensdag og nytårsaftensdag. Disse dage er som udgangspunkt almindelige arbejdsdage. Der kan dog på den enkelte institution være praksis/kutyme for at give frihed med løn på en eller flere af disse dage."*
   > → "There are no general rules on time off on Constitution Day, Christmas Eve and New Year's Eve. These days are as a starting point ordinary working days. An institution may, however, have a practice or custom of giving paid time off on one or more of them."

   https://pav.medst.dk/pav/kapitel-26-tjenestefrihed-mv/ (read 23 Oct 2026).
   **1 May (PAV 26.2.8.2):** staff "på begæring skal have adgang til fritagelse for arbejdet den 1. maj, i den udstrækning tjenesten tillader det" (on request, they get time off on 1 May insofar as the work allows). MedSt's view is that the time off is **unpaid** *"medmindre der på den enkelte institution måtte være praksis/kutyme for andet"* (unless the institution's practice says otherwise).
   **The arbitration that settled the custom question:** FV2016.0107 (23 Jan 2017), AC v. Finance Ministry for SKAT and Spillemyndigheden. The umpire found that paid days off on these four days were a custom on *"langt de fleste statslige arbejdspladser"* (most state workplaces), held by *"noget over halvdelen af alle medarbejdere"* (somewhat over half of all staff). He held that the custom could be withdrawn collectively with *"et fælles passende varsel"* (reasonable common notice), and he acquitted the ministry. After the withdrawal, staff *"må bruge ferie, særlige feriedage eller andre fridage"* (must use holiday or other days off), and SKAT made 24 and 31 Dec closure days. https://medst.dk/media/wezp5yyl/opsigelse-af-arbejdsgiverbetalte-fridage-23012017.pdf, pp. 10–12.
   **Practice today (a state example):** the Danish Defence HR portal gives civilian staff paid time off on all of 5 June and may give it on 1 May and 24 Dec. It gives none on 31 Dec. https://medarbejder.forsvaret.dk/da/fravaer/andet-fravaer/tjenestefrihed-i-forbindelse-med-helligdage/ (an institution's local rule, not a central one).
3. **Grundlovsdag and 24 Dec matter for pay, not for the norm.** The ulempetillæg window includes *"4. Grundlovsdag: Fra kl. 12 til kl. 24 / 5. Juleaftensdag: Fra kl. 14 til kl. 24"* (022-26, remarks to § 13, PDF p. 20; see C2). The § 14 weekend/holiday +50 % applies only to *"weekender eller … søgnehelligdage"* (weekends or søgnehelligdage), so it does **not** apply on Grundlovsdag or 24 Dec.
4. **Store Bededag.** Lov nr. 214 af 6. marts 2023, § 1: *"at sikre, at store bededag anses som en almindelig arbejdsdag"* (to ensure Store Bededag is an ordinary working day). § 5: *"Loven træder i kraft den 1. januar 2024."* (in force 1 Jan 2024). § 3 grants monthly-paid staff a supplement of *"0,45 pct. af årslønnen"* (0.45 % of annual pay). https://www.retsinformation.dk/eli/lta/2023/214. **The state's compensation**, in the circular on the holiday agreement (CIR1H 9298 af 17/05/2024, general remarks, p. 1):
   > *"Ved overenskomstforhandlingerne i 2024 har parterne aftalt en forhøjelse af den særlige feriegodtgørelse til 2,02 pct. pr. 1. april 2024. Samtidig ophører ret til løntillæg efter lov nr. 214 af 6. marts 2023 … for ansatte, der har krav på den forhøjede særlige feriegodtgørelse."*
   > → "At OK24 the parties agreed to raise the special holiday allowance to 2.02 % from 1 April 2024. At the same time, the right to the supplement under Act 214 ends for staff entitled to the raised allowance."

   https://www.retsinformation.dk/eli/retsinfo/2024/9298/pdf (status "Gældende", meaning in force, on 23 Oct 2026). The Faroes keep 1.5 % because Store Bededag is still a holiday there.

**Verdict: PARTLY.**

| Row | Status | Should be |
|---|---|---|
| Nytårsdag, Skærtorsdag, Langfredag, 2. Påskedag, Kristi Himmelfartsdag, 2. Pinsedag, Juledag, 2. Juledag | **Correct** as holidays that shorten the norm on a weekday | Keep. Note that a holiday on a Saturday does not shorten a day worker's norm: § 4 stk. 2 excludes days *"hvor den ansatte i forvejen altid har fri"* (on which the employee is always off anyway). `IsWorkingDay` already drops weekends. |
| Påskedag, Pinsedag | **Harmless** | Always Sundays, so never søgnehelligdage. They matter only for shift workers' § 15 stk. 5 count of Sunday and holiday days off. Keep them, flagged as `helligdag`, not `søgnehelligdag`. |
| **Grundlovsdag** | **Wrong** | Remove it as a norm-reducing holiday and from the "holiday rate". Re-flag it as an *ulempe window from 12:00* (day workers; also the natpenge weekend supplement from 12:00 for shift staff, PAV 19.2.9.2). Any day off is a **local custom per institution** (configurable, withdrawable). |
| 24 Dec (missing) | **Missing flag** | Add as an *ulempe window from 14:00*. It is not a holiday and does not shorten the norm. Any day off or closure day is local. |
| 31 Dec, 1 May (absent) | **Correctly absent** from the norm table | No central flag. 1 May gives unpaid time off on request unless local custom pays it. Model both as local-custom options. |
| Store Bededag | **Correctly absent from 2024** | The legal trigger is the **calendar date 1 Jan 2024** (lov 214 § 5), not the OK version. The code's OK-version key gives the right answer by coincidence: Store Bededag always falls after 1 April, i.e. inside OK24. It would be more robust to key it on the date. The compensation is a **payroll item** (2.02 % særlig feriegodtgørelse from 1 Apr 2024; the 0.45 % lov-214 supplement for 1 Jan–31 Mar 2024), not a time item. |

**How a local paid day off should enter the time account.** It should be recorded as paid absence credited with that day's hours under § 7 nr. 2 (*"Dage med ret til fravær med løn medregnes med det antal timer, den ansatte skulle have arbejdet"*: days with paid absence count as the hours the person should have worked). It should not be a cut to the norm, because the circular does not count these days as holidays. For a full-timer the balance comes out the same either way. Keeping it as an absence keeps the norm faithful to § 4 and makes the custom auditable.

**Confidence: HIGH.** The list, the exclusions and the Store Bededag treatment are all explicit in current primary texts.

**Contrary evidence searched for.** I looked for a central state rule or circular that makes Grundlovsdag, 24 Dec or 31 Dec paid days off for everyone, and found none. PAV says "no general rules", and FV2016.0107 calls the day off a withdrawable custom. I looked for a state rule keeping Store Bededag as a day off, and found it only for Greenland and the Faroes. A 2017 Djøf article and FAQ pages describe the days as customary days off. That is consistent with this verdict; they do not make it a central rule.

---

## C2 — Weekend, holiday and unsocial-hours supplements (HK/PROSA)

**What StatsTid encodes.** From `docs/references/danish-agreements.md` §"Supplement Time Windows" and the
`agreement_configs` seeds (HK/PROSA, both OK24 and OK26):

| Window | Rate |
|---|---|
| Evening 17:00–23:00 | 1.25 |
| Night 23:00–06:00 | 1.50 |
| Saturday | 1.50 |
| Sunday | 2.00 |
| Holiday (incl. Grundlovsdag, per C1) | 2.00 |

AC has these disabled.

**What the sources say (state working-time agreement, chapter 3: staff who normally work day hours).**
1. **One unsocial-hours supplement, 25 %, over one combined window.** § 13 stk. 1:
   > *"For arbejde i tidsrummet fra kl. 17 til kl. 06 eller i weekender, på søgnehelligdage, grundlovsdag efter kl. 12 samt juleaftensdag efter kl. 14 ydes et tillæg svarende til 25 pct. af nettotimelønnen, inkl. faste tillæg, dog mindst 32,77 kr. (grundbeløb 31. marts 2012)."*
   > → "For work 17:00–06:00, at weekends, on søgnehelligdage, on Constitution Day after 12:00 and on Christmas Eve after 14:00, a supplement of 25 % of the net hourly wage, incl. fixed supplements, minimum 32.77 kr. (2012 level)."

   The remarks list the windows: weekdays 17–06 (incl. Monday morning); weekends Saturday 00:00 to Sunday 24:00; søgnehelligdage 00–24; Grundlovsdag 12–24; Juleaftensdag 14–24. The supplement applies only to work *"efter ordre eller godkendt tjenestefordeling"* (ordered or on an approved roster). It is paid *"uafhængigt af eventuel overarbejdsgodtgørelse … eller … weekendgodtgørelse efter § 14"* (regardless of overtime or § 14 weekend compensation). It also covers staff exempt from overtime under § 8 stk. 3. Under § 13 stk. 2 it can be converted to time off by agreement between management and the employee; PAV gives the rate as ¼ hour per hour. 022-26 § 13 and remarks, PDF pp. 20–21. In flex time it is due only if the placement was ordered or necessary: PAV 19.5.3, *"Medmindre andet er aftalt, kan der kun ydes ulempegodtgørelse for arbejde inden for flekstiden, hvis den særlige placering af arbejdstiden har været beordret eller nødvendig…"*.
2. **Weekend and holiday work: +50 %, outside the hour count, plus the 25 %.** § 14:
   > *"Arbejde i weekender eller på søgnehelligdage godtgøres med afspadsering af samme varighed med et tillæg på 50 pct. eller med timeløn med et tillæg på 50 pct. Stk. 2. Arbejdstid, der godtgøres efter stk. 1, indgår ikke i arbejdstidsopgørelsen efter § 7. Stk. 3. Der ydes herudover tillæg efter § 13."*
   > → "Work at weekends or on søgnehelligdage is compensated with equal time off plus 50 %, or hourly pay plus 50 %. Such hours are not counted in the § 7 hour count. The § 13 supplement is paid on top."

   The remarks add that the supplement applies *"uanset om der er tale om almindelig tjeneste, pålagt over- eller merarbejde eller en deltidsansats ekstraarbejde inden for fuldtidsnormen"* (whether ordinary duty, ordered overtime or extra work, or a part-timer's extra hours). No overtime pay is added on top: *"Der ydes ikke herudover … godtgørelse for over- eller merarbejde."* Time off not taken by the end of the **following norm period** is paid in cash. 022-26 § 14 and remarks, PDF p. 21. PAV 19.2.8.1 says the employer chooses the form: *"Arbejdet kan efter arbejdsgiverens valg godtgøres med afspadsering … med timeløn … eller med en kombination"*.
3. **No Sunday premium.** Saturday and Sunday sit in one window, *"lørdag kl. 00 til søndag kl. 24"*, with the same § 14 +50 % and § 13 +25 %. Neither 085-25 (HK) nor 079-25 (PROSA) adds any supplement. A full-text search of both for "søndag", "weekend", "ulempe", "100 pct" and "kl. 17" found only HK Bilag 9.
4. **The only 100 % Sunday/holiday rate for HK (narrow).** HK Bilag 9 (laboratory technicians on research cruises and fieldwork), § 1 stk. 3:
   > *"Timeløn med tillæg af 50 pct. for 1.-3. timer / Timeløn med tillæg af 100 pct. for 4. og følgende timer og for tjeneste på søn- og helligdage."*
   > → "50 % for the 1st–3rd hours; 100 % for the 4th and later hours and for duty on Sundays and holidays."

   Under stk. 4, no other overtime or unsocial-hours pay is given alongside it. 085-25 Bilag 9, PDF p. 63, https://cirkulaere.medst.dk/media/m1hpbtsq/085-25.pdf. The other narrow HK ladders (Greenland call-ins 023-19; the 2006 municipal-reform transfers 069-06) are in V4.
5. **Shift staff (chapter 4) are a different regime.** They earn 3 hours' time off per 37 hours worked 17–06 (§ 17), plus the night-pay agreement's fixed kroner supplements: night pay 17–06, and a weekend supplement from Saturday 14:00 to Monday 06:00 and on holidays, Grundlovsdag after 12:00 and 24 Dec after 14:00 (PAV 19.2.9.2). Whether a person is a chapter-3 or chapter-4 employee is decided locally (PAV 19.2.3.2). **I did not check the kroner rates in the night-pay agreement.**
6. **AC has no unsocial-hours supplement:** *"Akademikeroverenskomsten har ingen regler om ulempegodtgørelse"* (PAV 19.3.1.8). AC's disabled supplements are correct.

**Verdict: CONTRADICTED.** No state source supports a split evening/night rate, a 1.50 night rate, a Sunday rate above Saturday's, or a holiday rate of 2.0 for HK or PROSA day workers.

**What StatsTid should encode (HK/PROSA, chapter-3 day workers):**
- **Ulempetillæg:** a single add-on of **+0.25 × net hourly wage** (min. 32.77 kr. at 2012 level, indexed) per ordered or approved hour in the window: weekday 17:00–06:00, Saturday 00:00 to Sunday 24:00, søgnehelligdag 00–24, Grundlovsdag 12–24, 24 Dec 14–24. It is paid at the first payday after the norm period, and is convertible to ¼ h time off by mutual agreement. In flex time it requires an ordered/necessary flag.
- **Weekendgodtgørelse:** for ordered or approved hours on a Saturday, Sunday or søgnehelligdag, the **hour leaves the norm count** and is compensated at **1.5** (1.5 h time off or 1.5 × hourly pay; the employer chooses). Untaken time off becomes cash after the following norm period. The ulempetillæg is paid on top, so the total value is 1.5 + 0.25. No overtime is added.
- **No Sunday 2.0 rate and no 1.50 night rate.** Collapse "evening" and "night" into one 25 % window. Remove the separate Sunday/holiday rate or set it equal to Saturday's.
- **Semantics check for the S147 refinement.** If the engine applies "1.25" as a multiplier on an hour that is already covered by the monthly salary, it pays the 0.25 add-on correctly. If it applies it to an hour that is also overtime, the right total is 1.5 (overtime) + 0.25 (ulempe), because both apply independently.
- A configurable **chapter-3/chapter-4 switch** per employee, with the chapter-4 regime (37/3 time off and night-pay kroner rates) left for later verification.

**Confidence: HIGH** for the day-worker rules (the agreement text and its remarks are explicit). **MEDIUM** for chapter 4 (kroner rates not checked).

**Contrary evidence searched for.** I looked for any state HK or PROSA text with a Sunday premium or a night rate above 25 %. I searched 085-25 and 079-25 in full text, read PAV 19.2.9 and 19.3.1.8, and checked the narrow ladders in V4. Found: only Bilag 9 (100 % on Sundays and holidays, but only as an overtime rate for cruises and fieldwork) and the night-pay agreement for shift staff (fixed kroner, not multipliers). HK's member page for state staff (https://www.hk.dk/raadogstoette/arbejdstid/arbejdstid-stat) confirms 25 % from 17:00, at weekends, on holidays, Grundlovsdag after 12 and 24 Dec after 14, and "+50 procent for alt arbejde på disse dage" (+50 % for all work on these days) for weekends and holidays. It mentions no Sunday premium. The 1.5/2.0 figures look like private-sector HK rates; that was not traced (cf. dossier Part 2 Q5).

---

## C3 — The HK pay-ceiling exemption

**What StatsTid encodes.** Nothing. There is no pay-ceiling field or check anywhere in the repository; a search
for "441" or a ceiling found only the dossier.

**What the sources say.**
1. **The rule and its basis (HK).** 085-25 § 4 stk. 2 and its remark:
   > *"Stk. 2. Hvis basislønnen udgør 441.037 kr. og derover i årligt grundbeløb, finder cheflønsaftalens §§ 5-6 tilsvarende anvendelse."* Remark: *"Ansatte omfattet af stk. 2 undtages for reglerne om overarbejds- og merarbejdsbetaling efter tjenestemændenes arbejdstidsaftale."*
   > → "If the **base pay** is 441,037 kr. or more in annual base amount, §§ 5–6 of the chef-pay agreement apply. Staff covered by stk. 2 are exempt from overtime **and** merarbejde pay under the civil servants' working-time agreement."

   085-25, PDF p. 11. The level date is in § 3 stk. 4: *"Alle basislønninger og tillæg er angivet i årligt grundbeløb pr. 31. marts 2012."* ("All base pay and supplements are stated in annual base amounts at 31 March 2012.") So the comparison is on **base pay (basisløn) expressed at the 2012 level**. It is **not** total pay, and not today's kroner. The Skatteforvaltningen protocol (Bilag 6 § 9) does the same for pay group 6 (special and chief consultants, Sats I = 441,037 kr.): *"De ansatte er således undtaget for reglerne om overarbejds- og merarbejdsbetaling efter Statens arbejdstidsaftale."* (PDF p. 54). PAV 19.3.3 restates the threshold but says "undtaget fra de almindelige merarbejdsregler" (exempt from the general merarbejde rules). Where PAV's wording differs, the circular governs.
2. **What they get instead.** Chef-pay agreement (Modst. 059-19), § 5:
   > *"Engangsvederlag fastsættes af ledelsen efter forhandling med den enkelte ansatte … Engangsvederlag kan f.eks. ydes som: … 3. Honorering af merarbejde. … Stk. 2. De ansatte er undtaget fra reglerne om merarbejde…"* Remark: *"Bestemmelsen træder i stedet for reglerne om godtgørelse for merarbejde."*
   > → "One-off payments are set by management after negotiation with the individual. They may reward, among other things, extra work. The staff are exempt from the merarbejde rules. This provision replaces the merarbejde compensation rules."

   § 6 covers permanent and temporary supplements negotiated individually. https://pav.medst.dk/media/5q4ngqxn/059-19.pdf, PDF p. 9. That the 2019 agreement is still current is supported by 022-26's remark to § 9 (June 2026), which cites *"§ 5, stk. 2, i aftale af 3. juli 2019 om chefløn"*.
   **What still applies to them:** the § 13 ulempetillæg and § 14 weekendgodtgørelse cover staff exempt under § 8 stk. 3 (022-26, remarks to §§ 13 and 14, PDF pp. 21–22). These staff fall under § 8 stk. 3 nr. 2 ("undtaget … i henhold til anden aftale", exempt under another agreement). PAV requires that the placement was ordered or necessary.
3. **PROSA has no ceiling.** 079-25 contains no § 4 stk. 2 equivalent; a full-text search for "chefløn" found nothing, and the highest base pay is 343,393 kr. in pay group 4. The routing is instead § 12 stk. 2:
   > *"…ansatte i løngruppe 1 og 2 omfattet af reglerne om overarbejdsgodtgørelse … Ansatte i løngruppe 3 og 4 er omfattet af merarbejdsgodtgørelse, når de opfylder betingelserne herfor i Statens Arbejdstidsaftale."*
   > → "Pay groups 1–2: overtime. Pay groups 3–4: extra-work pay **when they meet the state agreement's conditions**."

   079-25, PDF p. 14. Those conditions are § 8 stk. 3 (management duties, self-organised work, or work that cannot be checked), with § 9's **discretionary** merarbejde: *"kan ansættelsesmyndigheden efter drøftelse med den ansatte yde merarbejdsgodtgørelse i form af afspadsering eller betaling"* (after discussion with the employee, the employer **may** give extra-work pay as time off or pay). 022-26 pp. 15–17.
4. **Generic exemptions that also hit HK/PROSA:** § 8 stk. 3 covers (1) staff under the chef-pay agreement, i.e. pay grade (lønramme) 35 and above; (2) staff exempt under another agreement; (3) staff who decide their own working pattern, or whose normal work cannot be checked (022-26 remarks, PDF p. 16).

**Verdict: CONFIRMED** (threshold, basis and level date), refined as follows. The exemption covers **both** overtime and merarbejde. The replacement is the chef-pay agreement's one-off payments and negotiated supplements. It **does not apply to PROSA**.

**What StatsTid should encode:**
- A per-employee (or per-pay-group) `OvertimeRegime` ∈ {`OVERTIME` (§ 8), `DISCRETIONARY_MERARBEJDE` (§ 9), `EXEMPT_CHEFLOEN` (engangsvederlag only)}.
- For HK, derive `EXEMPT_CHEFLOEN` when `basisløn_grundbeløb_2012 ≥ 441,037`. This means storing base pay at the 2012 level, or indexing the threshold with the state pay-regulation index; supplements are excluded. It also applies to Skatteforvaltningen pay group 6.
- For PROSA: pay groups 1–2 → `OVERTIME`; pay groups 3–4 → `DISCRETIONARY_MERARBEJDE` where § 8 stk. 3 is met.
- This fits ADR-024's three-state compensation right (CONTRACTUAL / DISCRETIONARY / NONE).
- Ulempetillæg and weekendgodtgørelse stay switched on for exempt staff, gated on "ordered or necessary".

**Confidence: HIGH** for HK (explicit text). **MEDIUM** for PROSA groups 3–4: the phrase "når de opfylder betingelserne" leaves open what happens to a group 3–4 employee who does **not** meet § 8 stk. 3. Read literally, they stay under the default overtime regime. This needs a domain expert.

**Contrary evidence searched for.** I looked for a PROSA ceiling clause (none in 079-25) and for a later HK change (none in 085-25 of 12 May 2025; the OK26 HK organisation agreement had not been reissued in what I found). I also checked whether PAV's narrower "merarbejdsregler" wording reflects a real difference; the circular remark is explicit and governs.

---

## C4 — Part-timers' hours above their contracted hours

**What StatsTid encodes.** Not examined in depth in this track. `OvertimeRule.cs` compares hours with the week's `DayNorm` sums and pays the excess as `OVERTIME_50`. If those sums are scaled to the part-time fraction, the engine already pays +50 % from the part-timer's own norm. That would match the award's direction but not 022-26 § 8 stk. 2, and it does so without the "ordered" precondition. The S147 refinement should verify this deliberately.

**What the sources say.**
1. **The agreement text (unchanged).** 022-26 § 8 stk. 2: *"Timer, der ikke overstiger arbejdstiden for fuldtidsbeskæftigelse … godtgøres dog kun med afspadsering af samme varighed eller med almindelig timeløn."* ("Hours up to full time get only 1:1.") 022-26, PDF p. 15. That is the circular of **18 Jun 2026**, in force from 1 Apr 2026. PAV 19.3.1.6 (read 23 Oct 2026) still says *"For deltidsansatte er arbejdstidsnormen i forbindelse med overarbejde fuldtidsnormen. Indtil denne er opfyldt, ydes alene almindelig kvotatimeløn"* (for part-timers the overtime norm is the full-time norm; until it is reached, only ordinary pro-rata pay).
2. **The binding ruling.** FV2025-795 (4 Feb 2026), operative part concerning the state:
   > *"Medarbejder- og Kompetencestyrelsen skal … anerkende, at det er i strid med direktiv 97/81/EF … at deltidsansatte medarbejdere, for hvem der findes sammenlignelige fuldtidsansatte, efter de i kendelsens bilag A angivne bestemmelser kun skal have overtidshonorering for arbejdstimer, der præsteres ud over den normale arbejdstid for fuldtidsansatte (påstand 1). … anerkende, at den tidsmæssige udstrækning af påstand 1 gælder fra den 20. januar 2000."*
   > → "MedSt must acknowledge that it breaches the part-time directive that part-timers who have comparable full-timers get overtime pay only for hours above the full-time norm under the provisions in Annex A, and that this applies from 20 Jan 2000."

   Annex A, state section, names *"Statens arbejdstidsaftale … § 8, stk. 2"* and HK's Bilag 9. The remedy reasoning is that compliance *"må … ske ved, at deltidsansatte modtager overtidshonorering ved arbejde udført i spændet mellem deres deltidsnorm og fuldtidsnormen"* (must come from paying part-timers overtime for the band between their own norm and the full-time norm). The award does not decide limitation of back-pay claims. https://arbejdsretten.dk/media/bork3lzb/fv2025-795.pdf, pp. 113–118 and Annex A pp. 126–127.
3. **OK26 state settlement, item 28** (CFU; the same item appears in the AC settlement):
   > *"Der afsættes i alt 12 mio. kr. Midlerne dækker udgifter til en efterfølgende forhandling og aftale om implementering af voldgiftsrettens dom af 4. februar 2026 på statens område. … Forhandlinger optages senest 1. maj 2026. Parterne aftaler senest 1. april 2026 en proces for håndtering af krav om efterbetaling…"*
   > → "12 million kroner for a later negotiation and agreement on implementing the award in the state. Negotiations to start by 1 May 2026. By 1 April 2026 the parties agree a process for back-pay claims."

   https://medst.dk/media/oo1auuiu/ok26-forlig-medst-cfu.pdf (item 28); https://medst.dk/media/0rmlepno/forlig-ok26-medst-ac.pdf.
4. **Status as of 23 Oct 2026: no state implementing text found.**
   - HK's state working-time page: *"vil reglerne om overtidsbetaling for deltidsansatte blive ændret i løbet af 2026 i samarbejde med Medarbejder- og Kompetencestyrelsen"* (the rules will change during 2026 together with MedSt). https://www.hk.dk/raadogstoette/arbejdstid/arbejdstid-stat (undated).
   - Dansk Sygeplejeråd's OK26 state page: *"De endelige forhandlinger om den konkrete udmøntning mangler dog fortsat."* (the final negotiations on concrete implementation are still outstanding). https://dsr.dk/loen-og-arbejdsvilkaar/ok26/ok26-stat-ny-overenskomst/ (undated).
   - Danmarks Lærerforening: a similar state agreement is expected, *"Aftalen er dog ikke på plads endnu"* (not yet in place). https://www.dlf.org/loen-og-vilkaar/arbejdstid/deltidsansatte/.
   - MedSt's news archive, first page (circulars of 29 Sep–16 Oct 2026), lists no part-timer circular.
   - **Not to be confused with:** the municipal implementation (KL; administrationsgrundlag effective 1 Apr 2026, e.g. Albertslund) and the regional one. **These are not state rules.**

**Verdict: UNRESOLVED** as to the state's implementing agreement (none found). **CONFIRMED** as to the legal position. The award binds MedSt, holds that § 8 stk. 2 cannot be applied to part-timers who have comparable full-timers, and runs back to 20 Jan 2000. The formal state text still carries the old rule.

**What StatsTid should encode:**
- A **versioned, dated rule switch** for the part-timer band (own norm → full-time norm):
  - `PartTimeBandRule = LEGACY_1_TO_1`: the 022-26 § 8 stk. 2 text.
  - `PartTimeBandRule = OVERTIME_50_FROM_OWN_NORM`: the award's direction, i.e. +50 % from the part-timer's own norm, still subject to the ordered/necessary precondition (V3).
- Effective-date handling so that an implementing agreement can be loaded when it appears.
- The § 14 weekend +50 % already applies to part-timers' extra hours (022-26 remark to § 14), independent of this switch.
- Back pay is out of scope for the rule engine; the claims process is local and still being agreed.

**Which variant is the default is an owner ruling** (see "Needs a domain expert").

**Confidence:** HIGH that the award binds the state and that 022-26 is unchanged. MEDIUM-HIGH that no implementing agreement exists yet: union pages say so, but they are undated and MedSt's archive was only partly reachable.

**Contrary evidence searched for.** I looked for a state circular or MedSt news item implementing the award (May–Oct 2026), searching cirkulaere.medst.dk and medst.dk; none was found. I checked whether "1 April 2026" reports referred to the state; they refer to KL/RLTN (municipal and regional) or are unspecific. The award itself is unambiguous about MedSt.

---

## C5 — AC's norm period

**What StatsTid encodes.** `agreement_configs` for AC: `norm_period_weeks = 1`, `norm_model = WEEKLY_HOURS` (OK24 and OK26). V12 already holds that no weekly norm exists.

**What the sources say.**
1. **The agreement fixes no period.** AC § 13 stk. 1: *"Lønnen efter denne overenskomst forudsætter fuldtidsbeskæftigelse svarende til gennemsnitlig 37 timer ugentlig."* ("Pay assumes full-time work of an average of 37 hours a week.") Remark: *"Der er med formuleringen af § 13, stk. 1, ikke tilsigtet nogen ændring af hidtidig praksis."* ("No change to existing practice is intended.") CIR1H 9333, PDF p. 21.
2. **The period hooks in the agreement are about merarbejde, and they are quarterly.** § 14 stk. 2: extra work must *"have strakt sig over en periode, der normalt ikke må være mindre end 4 uger"* (normally have lasted at least 4 weeks). § 14 stk. 3: *"Indberetningen skal afgives ved merarbejdets ophør og mindst hvert kvartal."* ("The report must be made when the extra work ends and **at least every quarter**.") § 14 stk. 5: time off *"bør normalt være tildelt … inden udløbet af det kvartal, der ligger umiddelbart efter"* (should be given before the end of the following quarter). CIR1H 9333, PDF pp. 23–24. Fuldmægtige's standby duty is *"indtil 20 timer pr. kvartal"* (up to 20 h a quarter; PAV 19.3.4.4).
3. **Union reading (quarterly), quoted in a state arbitration.** The Djøf guide, quoted in FV2023-915:
   > *"Administrative djøfere arbejder efter en kvartalsnorm. Det betyder, at eventuelt merarbejde opgøres kvartalsvist, jævnfør overenskomstens § 14, stk. 3. Undervisere på gymnasialt niveau, på professionshøjskoler eller erhvervsakademier har en årsnorm, jævnfør overenskomstens bilag 6. … Videnskabelige medarbejdere på universiteter med videre samt special- og chefkonsulenter arbejder efter en årsnorm, men er ikke omfattet af merarbejdsreglerne."*
   > → "Administrative AC staff work to a quarterly norm, i.e. extra work is counted quarterly (§ 14 stk. 3). Teachers at upper-secondary level, university colleges and business academies have an annual norm (Bilag 6). University researchers and special/chief consultants work to an annual norm but are outside the merarbejde rules."

   https://arbejdsretten.dk/media/eounmhsf/fv2023-915.pdf, p. 16.
4. **Annual norm for special consultants (the parties' common ground, accepted by the umpire).** FV2023-915 § 8.4.2: *"Der er enighed om, at disse regler bygger på en årsnorm"* (it is agreed that these rules rest on an annual norm). This refers to the 1971 circular and the 1993 agreement on special consultants' extra work. The umpire therefore applied a 12-month reference period (same PDF, p. 22). The case concerned the 48-hour rule, so the annual-norm statement is common ground, not the point decided.
5. **The annual norm written into the agreement for one group.** AC Bilag 6, teachers at vocational institutions: *"Indberetning om merarbejde og fastsættelse af merarbejdets omfang skal ske mindst én gang årligt (årsnorm)."* ("Extra work is reported and set at least once a year (annual norm).") CIR1H 9333, PDF p. 76 (the same sentence recurs for other teacher groups on PDF pp. 86 and 92). PAV 19.3.4.5 says the same for teachers at business academies and university colleges.
6. **Flex periods are local and usually monthly, also for AC TAP staff.** AC § 13 stk. 5 allows local flex agreements (not in teaching). The remark says carried-over flex hours *"udløser ikke merarbejdsgodtgørelse"* (do not trigger extra-work pay; V6). SDU 2026 covers AC TAP staff (excluding special/chief consultants) with **monthly** settlement. The AU Institute of Animal Science 2014 agreement covered "TAP og AC-TAP" with monthly settlement (see C6). PAV 19.3.1.6 (general, for staff on collective agreements): *"Normperioden er typisk en måned. Der er dog overenskomster/organisationsaftaler, som fx indeholder en 4-ugers-, kvartals- eller årsnorm."* ("The norm period is typically a month, but some agreements have a 4-week, quarterly or annual norm.")
7. **AC special/chief consultants in the state get no merarbejde as such.** PAV 19.3.4.6 lists them under one-off payments (engangsvederlag). Djøf: *"Hvis du er ansat i staten har du som udgangspunkt ikke ret til at få kompenseret dit merarbejde"* (in the state you have, as a starting point, no right to compensation for extra work). https://www.djoef.dk/vilkaar/arbejdstid/overarbejde-og-afspadsering.

**Verdict: PARTLY.** There is **no central default** for AC. What the sources fix:
- the assessment cadence for merarbejde is at most a **quarter** (reporting floor), with extra work normally lasting **≥ 4 weeks**;
- an **annual** norm for the Bilag 6 teacher groups (explicit) and for special/chief consultants (common ground; and they are outside merarbejde anyway);
- **local** flex agreements set the flex settlement period, typically **monthly**.

**Not weekly: HIGH.**

**What StatsTid should encode:**
- Replace `norm_period_weeks = 1` for AC with **two** configurable periods:
  1. the **flex settlement period**, from the local flex agreement (default: calendar month, matching PAV 19.5.5 *"Opgørelsesperioden på administrativt betonede områder vil typisk kunne fastsættes til en måned"*: on administrative units the settlement period will typically be a month);
  2. the **merarbejde assessment period**, default **calendar quarter**, with ≥ 4-week extent, a claim and an employer decision (V9).
- `AC_TEACHING`: annual merarbejde period (Bilag 6).
- Special/chief consultants: annual, with no merarbejde rule (engangsvederlag).
- `AC_RESEARCH`: outside merarbejde unless the work was ordered and both it and the normal work can be checked (§ 14 stk. 1 nr. 2).
- The configuration enum must allow **month, quarter and year** (V1 item 7 already notes `NormPeriodWeeks ∈ {1,2,4,8,12}` cannot express a month).

**Confidence: MEDIUM** for "quarterly" as the administrative default: it is a union derivation from the reporting floor, not a defined norm period. **HIGH** for the annual groups and for "not weekly".

**Contrary evidence searched for.** I looked for a MedSt or PAV statement fixing a monthly or quarterly norm for AC (none; PAV 19.3.4.5 speaks of "den pågældende normperiode", the norm period in question, without defining it) and for a local AC agreement with a weekly period (none). The SKAT 2014 union article says *"Alle medarbejdere indenfor forbundets aftale- og overenskomstområde har månedsnorm"* (all staff in the union's area have a monthly norm). That is DTS's area (fuldmægtige in tax, under the state working-time agreement), **not** the AC agreement, so it does not contradict this verdict.

---

## C6 — Local flex-cap behaviour (a sample of state agreements)

There is no central state flex agreement (PAV 19.5.1). Below are seven state-sector examples: universities and other self-owning institutions under the state agreements, plus SKAT, together with PAV's model. Status and dates are as found. Universities are state-sector employers under the state agreements and MedSt.

| # | Agreement (status) | Who is covered | Settlement period | Plus cap | Minus cap | Above the cap | At leaving | Fixed hours / office hours | Ordered work |
|---|---|---|---|---|---|---|---|---|---|
| 0 | **PAV model** (MedSt guidance, not an agreement; read 23 Oct 2026). PAV 19.5 | — | *"typisk … en måned"* (typically a month) | e.g. 15 | e.g. 10 | *"bortfalder som udgangspunkt … kan … ikke udløse kontant betaling"* (forfeited as a starting point; no cash). A local scheme may allow transfer to the overtime/merarbejde account | not addressed | Core hours (fikstid), e.g. 10–15. Optional **kontortid** (office-hours) model (V8) | kept outside flex (19.5.11), except inside kontortid |
| 1 | **SDU, TAP, in force 1 Apr 2026** (signed 19–23 Mar 2026 by HK/Stat, Prosa, Djøf/DM and others). https://sdunet.dk/-/media/sdunet/filer/servicesider/hr/ansaettelsesforhold/flekstidsaftale-for-sdu-2026-underskrevet.pdf | monthly-paid TAP staff, incl. AC TAP. Excludes special/chief consultants, self-organising TAP managers, flex-job staff | month (employee approves by the 5th) | **37** (pro rata for part-time) | **7.4** | Cap stated in 3.3. 3.4: *"I særlige tilfælde kan leder og medarbejder aftale yderligere timer i over / underskud"* (in special cases manager and employee may agree more), with a plan to rebalance. 3.6: *"Der kan normalt ikke ydes kontant vederlag for opsparede flekstimer."* (no cash normally). Forfeiture is not stated explicitly | 5.2: surplus *"kan ikke forventes udbetalt som kontant vederlag"* (cannot be expected to be paid out), except under a 3.4 agreement | *"ingen regler om fast arbejdstid"* (no fixed hours). Units set opening hours (åbningstid) | 2.4.2: ordered over/merarbejde *"håndteres uden for flekstidsaftalen"* (handled outside flex); it must be ordered by the manager |
| 2 | **DTU, TAP guideline** (approved by the directorate, HSU March 2017; page updated 21 Sep 2026). https://www.inside.dtu.dk/human-resources/under-ansaettelsen/arbejdstid-og-flekstid/retningslinje-om-arbejds-og-flekstid | TAP staff, excl. managers with staff responsibility and flex-job/wage-subsidy staff | month (manager approves) | **37** | **10** | *"Det aftales med nærmeste leder, hvordan timer, der undtagelsesvis ligger over 37 timer"* (handled case by case with the manager). The manager may order time off | *"skal positive flekstimer være afholdt inden for opsigelsesperioden … og udbetales ikke, uanset årsag"* (must be taken in the notice period; never paid out, whatever the reason) | fikstid optional per unit | positive flex is distinct from over/merarbejde; pay only for ordered work |
| 3 | **Aarhus University template** (adopted by the main cooperation committee 20 Mar 2014 per FV2019.0076; current file on AU's site, undated). https://medarbejdere.au.dk/fileadmin/www.medarbejdere.au.dk/hr/Arbejdstid/Arbejdstid_-_skemaer/Skabelon_for_flekstidsaftale.pdf | TAP staff. Excludes managers, special/chief consultants, **fuldmægtige**, fixed-roster staff | month | **37** unless agreed with the manager | **10** (manager may allow more) | § 5 stk. 4: *"bortfalder ved opgørelsesperiodens (månedens) udgang og kan ikke udløse kontant betaling, medmindre andet aftales med lederen"* (forfeited at month end, no cash, unless agreed with the manager). Max **13 h credited per day** | § 12: balance must be evened out. No payout within 37 h unless agreed. *"Et underskud vil kunne give anledning til reduktion i løn"* (a deficit may lead to a pay deduction) | fikstid e.g. 10–15. Optional **kontortid**: overtime only if ordered **and** outside kontortid **and** above 160.33 h a month | outside flex |
| 4 | **AU Institute of Animal Science (Foulum), Jan 2014** (quoted in FV2019.0076; status after 2017 unknown). https://arbejdsretten.dk/media/16941/fv20190076.pdf pp. 6–7 | TAP and **AC-TAP**. Field and stable staff have a special cap | month for the balance; **year-end** cap test | **100** (200 for field and stable staff); at most 100 carried from one year to the next | **37** | § 5 stk. 3: above 100 *"bortfalder ved årsskiftet og kan ikke udløse kontant betaling, medmindre andet aftales med lederen"* (forfeited at the turn of the year, no cash, unless agreed with the manager) | not quoted | kontortid three-condition test | outside flex |
| 5 | **SKAT 2008** (in force 1 Apr 2008; withdrawn end of Sep 2013; **historical**). https://dts.dk/ref.aspx?id=1453 | staff on the scheme, incl. special/chief consultants from 2008 | daily norm; no period stated | **no hard cap**; *"bør aldrig være … mere end 200 timer uden, at der konkret er aftalt"* (should never exceed 200 h without a concrete agreement) | **16** | — | § 14: if service prevented taking the hours, *"kontantydelse … 1/1924 … pr. overskudstime"* (cash at 1/1924 of annual pay per surplus hour). Up to 10 days of care-day leave (omsorgsdage) can move with the employee to another state employer | — | — |
| 6 | **SKAT 2014 flex + ATN** (from 1 Jan 2014; union description, DTS member magazine 8/2013, not the agreement text). https://www.dts.dk/ref.aspx?id=3283 | assistants, pay-group-4 consultants, **fuldmægtige**. Specialist, special and chief consultants are excluded; they get a voluntary "ATN" scheme instead (a 38 h week earning one ATN day every 2 months, forfeited after 6 months) | month | not stated | not stated | — | — | — | **Fuldmægtige:** half of a month's surplus counts as merarbejde against the 20 h/quarter standby duty, the other half goes to flex, until the quarter's 20 h are met |
| 7 | **SKAT 2012 addendum** (historical; V7): 1:1 cash conversion of flex hours by agreement, upheld in FV2015.0028 | — | — | — | — | cash 1:1 by agreement | — | — | — |

**What this means for StatsTid.** These parameters must sit at the **local-agreement level** (institution or unit, effective-dated, audited), not on the AC/HK/PROSA agreement:
1. **Settlement period:** month (all current examples); plus a separate cap-test period (month-end vs. year-end, as at AU Foulum).
2. **Plus cap and minus cap:** pro rata for part-time; separate caps per staff subgroup (field staff 200).
3. **Daily credit cap** (AU: 13 h).
4. **Above-cap action:** one of {forfeit at period end, forfeit at year end, manager-agreed exception (recorded with a rebalancing plan), transfer to the overtime/merarbejde account (PAV 19.5.8), 1:1 cash by agreement (SKAT 2012)}.
5. **Leaving action:** one of {must be taken during notice/forfeit (DTU), no payout unless agreed (SDU, AU), cash at 1/1924 (SKAT 2008), transfer to a new state employer (SKAT 2008)}. **Deficit at leaving:** pay deduction allowed (AU).
6. **Fixed hours (fikstid) and office hours (kontortid):** with PAV's three-condition rule for turning an hour into overtime (V8).
7. **Excluded groups:** special/chief consultants; managers; fuldmægtige with a standby supplement (AU); flex-job staff.
8. **Standby-duty interaction for fuldmægtige** (SKAT's 50/50 split; PAV 19.5.12).
9. **Suspension of the flex scheme** in peak periods (AU § 14; SKAT § 12).

**Confidence: MEDIUM.** The sample is small and university-heavy, and two of the seven are historical. No ministry or agency agreement newer than 2014 was reachable.

**Could not reach.** The AAU institute agreements (intranet hosts would not resolve); the Danish Defence flex page (404); and KU, CBS, RUC, ITU and other agencies (nothing public found).

---

## C7 — Who chooses between time off and pay

**What StatsTid encodes.** `agreement_configs` seeds:

| Agreement | `default_compensation_model` | `employee_compensation_choice` |
|---|---|---|
| AC, AC_RESEARCH, AC_TEACHING | AFSPADSERING | **FALSE** |
| HK | AFSPADSERING | **TRUE** |
| PROSA | AFSPADSERING | **TRUE** |

`EmployeeCompensationChoice = true` makes the choice per period and per employee (S17).

**What the sources say.**
1. **HK and PROSA pay groups 1–2 (state working-time agreement).** § 8 stk. 1 gives time off **or** pay, each +50 %, without saying who chooses. PAV 19.2.7.1: *"…godtgøres de overskydende timer efter arbejdsgiverens valg med afspadsering … med timeløn … eller med en kombination heraf"* (the **employer** chooses; V10). The deadline structure makes time off the primary path. § 10 stk. 1: *"Afspadsering skal gives senest 3 måneder efter opgørelsesperiodens udløb. Afspadseringen kan efter aftale med den ansatte udskydes i op til 1 år…"* (time off within 3 months of period end; it may be postponed by up to 1 year **by agreement with the employee**). Remarks: after the deadline, *"skal der ske betaling ved den følgende lønudbetaling, medmindre den ansatte vælger at få den tilgodehavende afspadsering konverteret til opsparingsdage"* (payment at the next payday unless the **employee chooses** saved days). Also: *"Betaling kan også ske før fristens udløb, hvis ansættelsesmyndigheden vurderer, at afspadsering ikke vil være mulig eller hensigtsmæssig."* (payment can come earlier **if the employer judges** time off impossible or inappropriate). 022-26 §§ 10–11, PDF pp. 17–18.
   - **HK-specific clause:** none. 085-25 § 38 only says HK follows the state agreement, sets the hourly rate at 1/1924 (stk. 4) and adds *"Overtidstillæg ydes efter arbejdstidsreglerne"* (overtime supplement under the working-time rules). A full-text search found no "valg" (choice) or "afspadser" (time off) clause. PDF p. 34.
   - **Union readings agree.** HK: *"Hvis du har overtid, skal du afspadsere, inden der er gået 3 måneder. Har du ikke mulighed for det, har du ret til i stedet at få betaling"* (you must take time off within 3 months; if you cannot, you are entitled to pay instead). DTS on SKAT: *"Det er SKAT, der afgør, hvorvidt honoreringen skal ske som afspadsering eller udbetaling."* ("SKAT decides whether compensation is time off or payment.")
   - **Weekend compensation (§ 14) is also the employer's choice** (PAV 19.2.8.1), with a deadline at the end of the **following norm period**.
   - **Ulempetillæg** is cash, converted to time off only **by agreement** between management and employee (§ 13 stk. 2).
2. **PROSA.** Pay groups 1–2 are as for HK (079-25 § 12 stk. 2). Pay groups 3–4 get § 9 discretionary merarbejde: the employer decides **whether**, **how much** and **in what form** (time off or pay) *"efter drøftelse med den ansatte"* (after discussion with the employee). There is no PROSA-specific choice clause (079-25 full-text search). V10 already contradicts the repo's "PROSA 1:1".
3. **AC.** § 14 stk. 5: *"Godtgørelse for merarbejde ydes så vidt muligt i form af afspadsering af samme varighed … med tillæg af 50 pct."* (time off +50 % **where possible**). stk. 6: *"Såfremt afspadsering ikke kan finde sted"* (if time off cannot take place), pay at 1/1924 + 50 %. CIR1H 9333, PDF p. 24. The employer sets the scope and gives notice of time off (14 days). OK26 changed § 14 stk. 5, 3rd sentence: time off can be postponed by up to 1 year **by agreement with the employee** (V9). PAV 19.3.4.5: *"I stedet for betaling kan den ansatte vælge at få den ikke afviklede afspadsering konverteret til opsparingsdage."* ("Instead of payment, the employee can choose to convert untaken time off into saved days.")

**Verdict: PARTLY** (V10 confirmed and refined):
- AC `false`: **confirmed**.
- HK and PROSA `true`: **contradicted**.
- `AFSPADSERING` as the default: **confirmed** for all three.

**What StatsTid should encode:**

| Agreement / group | `DefaultCompensationModel` | `EmployeeCompensationChoice` | Employee elections to model separately |
|---|---|---|---|
| HK (all pay groups under § 8) | **AFSPADSERING** (employer may switch to pay early, § 10 remark) | **false** | (a) consent to postpone time off by up to 1 year (§ 10 stk. 1); (b) at the 3-month deadline, saved days (opsparingsdage) instead of cash (§ 11) |
| HK above the C3 ceiling | n/a (engangsvederlag) | false | — |
| PROSA pay groups 1–2 | **AFSPADSERING** | **false** | as HK |
| PROSA pay groups 3–4 | employer's decision under § 9 (discretionary; time off or pay) | **false** | — (an ADR-024 DISCRETIONARY right) |
| AC (fuldmægtig and similar) | **AFSPADSERING** ("så vidt muligt", where possible) | **false** | (a) consent to postpone by up to 1 year (OK26 § 14 stk. 5); (b) saved days instead of cash |

A better model than the boolean would be `CompensationChooser = EMPLOYER` plus the two explicit employee elections, each audited. That captures everything the sources give the employee and nothing more.

**Confidence: HIGH** for `EmployeeCompensationChoice = false` in all three: PAV is explicit, the agreements contain no employee-choice clause, and the unions agree. **MEDIUM-HIGH** for `AFSPADSERING` as the HK/PROSA default: § 8's text is neutral, but the deadline structure and the HK guidance make time off the primary path.

**Contrary evidence searched for.** I looked for any clause giving HK or PROSA staff the right to choose pay over time off. I searched 085-25, 079-25 and 022-26 in full text, and read the HK union page and DTS. The only employee choices found are postponement by agreement and conversion to saved days. HK's "ret til … betaling" is a right to payment **when time off is not given by the deadline**, not a free choice.

---

## Needs a domain expert or an owner ruling

1. **C4: which part-timer rule applies now?** The award binds the state from 20 Jan 2000, but 022-26 still says 1:1 and no implementing agreement exists yet. Should StatsTid default to the award (+50 % from the own norm, ordered hours only) or to the circular text until the implementing agreement lands? This is an owner ruling with legal input. Also: how does the "comparable full-timer" condition map to StatsTid's data?
2. **C3: PROSA pay groups 3–4 who do not meet § 8 stk. 3.** Do they fall back to overtime (§ 8) or have no compensation right? The literal reading is overtime; this needs confirmation.
3. **C2: chapter-4 (shift) staff.** If StatsTid is to support them, the night-pay agreement's kroner rates and the 37/3 time-off accrual need their own verification. Who decides chapter 3 versus chapter 4 is local (PAV 19.2.3.2).
4. **C5: the AC administrative default.** "Quarterly" is a union derivation from the reporting floor. A MedSt or HR practitioner should confirm whether ministries actually settle AC hours monthly (via flex) and assess merarbejde quarterly. That is the two-period model proposed above.
5. **C1: local customary days off** (5 June, 24 Dec, 31 Dec, 1 May). How should they be configured per institution? As paid absence credited with hours (proposed), as closure days requiring the employee's own leave, or both? Which institutions still grant them is unknown.
6. **C6: a ministry or agency example newer than 2014.** None was publicly reachable. An HR practitioner sample would firm up the parameter list.

## Sources not reached / limits

- `hr.medst.dk` would not resolve (the ferie circular 021-24 was read instead through retsinformation CIR1H 9298).
- AAU intranet hosts would not resolve.
- The Danish Defence flex page returned 404.
- MedSt news archive pages 2–10 could not be paged (only the latest 10 items were seen).
- The kroner rates in the night-pay agreement were not checked.
- The OK26 reissue of the HK and PROSA organisation agreements was not found. The 2025 texts (085-25, 079-25) were used; no OK26 change to the cells above was reported in the CFU settlement items read.
- Union pages (HK, DSR, DLF, Djøf, DTS) are undated or secondary. They are used only as corroboration or status signals, never as the decisive source.

## Primary sources (downloaded and read as text)

| Source | URL | Used for |
|---|---|---|
| 022-26 Cirkulære om Statens arbejdstidsaftale (18 Jun 2026, effective 1 Apr 2026) | https://cirkulaere.medst.dk/media/jhshf3w2/022-26.pdf | C1–C4, C7 |
| 085-25 HK organisationsaftale (12 May 2025) | https://cirkulaere.medst.dk/media/m1hpbtsq/085-25.pdf | C2, C3, C7 |
| 079-25 PROSA organisationsaftale | https://cirkulaere.medst.dk/media/tnjl2rxn/079-25.pdf | C2, C3, C7 |
| CIR1H 9333 AC overenskomst (11 Apr 2025) | https://www.retsinformation.dk/eli/retsinfo/2025/9333/pdf | C1, C5, C7 |
| 059-19 Cirkulære om aftale om Chefløn | https://pav.medst.dk/media/5q4ngqxn/059-19.pdf | C3 |
| PAV kap. 19 and kap. 26 (read 23 Oct 2026) | https://pav.medst.dk/pav/kapitel-19-arbejdstidsregler-mv/ ; https://pav.medst.dk/pav/kapitel-26-tjenestefrihed-mv/ | C1–C7 |
| CIR1H 9298 Cirkulære om aftale om ferie (17 May 2024) | https://www.retsinformation.dk/eli/retsinfo/2024/9298/pdf | C1 |
| Lov nr. 214 af 6. marts 2023 (Store Bededag) | https://www.retsinformation.dk/eli/lta/2023/214 | C1 |
| FV2016.0107 (23 Jan 2017), customary days off | https://medst.dk/media/wezp5yyl/opsigelse-af-arbejdsgiverbetalte-fridage-23012017.pdf | C1 |
| FV2025-795 (4 Feb 2026), part-timers | https://arbejdsretten.dk/media/bork3lzb/fv2025-795.pdf | C4 |
| OK26 settlements: CFU and AC | https://medst.dk/media/oo1auuiu/ok26-forlig-medst-cfu.pdf ; https://medst.dk/media/0rmlepno/forlig-ok26-medst-ac.pdf | C4 |
| FV2023-915 (7 Jan 2025) | https://arbejdsretten.dk/media/eounmhsf/fv2023-915.pdf | C5 |
| FV2019.0076 (AU Foulum flex agreements) | https://arbejdsretten.dk/media/16941/fv20190076.pdf | C6 |
| SDU 2026, AU template, DTU guideline, SKAT 2008, DTS 2013/14 | URLs in the C6 table | C6 |
