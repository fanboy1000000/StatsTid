# The 11-hour daily rest and the weekly rest day — verified research (S148 research pass, R1–R5)

**Date:** 2026-11-13. **Method:** a research pass (`general-purpose`, Opus 5.5, web search and direct downloads of the primary texts, read on 2026-11-12), then an independent adversarial verification that re-downloaded and re-read every decisive source (Opus 5.5, 2026-11-13), then a Fable 5.2 adjudication of what it means for StatsTid (`.claude/reviews/S148-RESEARCH-rest-24h-adjudication.md`, RR-1 … RR-8). **Status: verified claim by claim.** Where the verification corrects the first pass, **the verification governs** (Part 1); the first pass is kept verbatim in Part 2 as the record of what was claimed. What StatsTid decided on it is [ADR-044](../knowledge-base/decisions/ADR-044-rest-period-model.md).

## In plain words

**The question.** Danish law gives every employee "at least 11 consecutive hours of rest within each period of 24 hours" (arbejdsmiljøloven § 50; BEK nr. 606 af 29/06/2026 § 3). StatsTid's rest check had to know where that 24-hour period starts. Its old answer, to group work by the day it starts and measure only between days, missed real breaches, which both review lenses found in S148.

**The answer.**
- It is not the calendar day, and since 29 December 1996 it is not "24 hours from the start of the normal working day": Parliament removed that anchor on purpose.
- Arbejdstilsynet, which enforces the rule, gives one test, in the same words in 1997, 2016 and 2024: from a point inside working time, look back 24 hours; that stretch must hold 11 unbroken hours of rest.
- In practice, **no work may end more than 13 hours after the last full 11-hour rest ended.** So these are breaches: one shift over 13 hours; a day shift followed by a night shift; a split day over 13 hours from first start to last end (08–12 plus 17–22 breaches at 22:00 on that day). "13 hours on, 11 off" is lawful.
- The state agreement's § 12 lets the employer cut the rest to 8 hours once per 7 days (twice by local agreement, never two days in a row), with equal compensatory rest straight after. Going below 8 hours needs Arbejdstilsynet's dispensation or a wider agreement it has approved.
- Staff who set their own working time, and managers, can be exempt, assessed person by person, never for a whole group (BEK 606 § 6; PAV 19.8).
- The weekly rest day is 35 unbroken hours (24 plus the 11-hour daily rest), with no more than 6 døgn between two (10 working days under the state agreement), and on-call time never counts toward it, although on-call at home counts as daily rest.
- Five questions the sources leave silent go to a qualified expert; a sixth has a safe default.

**What StatsTid did with it (S148).** The owner chose to build the corrected daily check in S148 (owner Q2, 2026-11-13): wave 3b, merged on 2026-11-16 as `2810ebeb` and CI-verified (run `43851473962`). The rule is ADR-044. How often a cut is used and the compensatory rest (QUAL-254), the per-person exemption (QUAL-255) and the weekly rest day (QUAL-256) are registered, not built.

## The questions and the verified verdicts

| # | Question | Verified verdict | Decisive sources | What StatsTid did |
|---|---|---|---|---|
| **R1** | What is "each period of 24 hours" measured from? | **CONFIRMED.** Not the calendar day; not a start-of-day anchor since 29 Dec 1996; Arbejdstilsynet's look-back test. "Every point" and "clip to the window" are inferences, supported by elimination | Commission, OJ C 109/29 (2023); LOV 1196/1996 and the L 59 remarks; VEJ 9666/2024 § 2, MED 3503/1997, VEJ 9576/2016; KEN 9938/2006 | ADR-044 D1: every work end judged on the 24 elapsed hours before it; QUAL-253 fixed |
| **R2** | Split working days | **PARTLY**, sharper than the first pass: lawful if and only if the chain spans 13 hours or less; gaps under 11 hours never reset the chain and never add up (except under home-work derogations); the example breaches on its own day, at 22:00 | Directive Art. 2(2), Art. 17(4)(b); BEK 606 § 33 stk. 4, Bilag 1 nr. 6 | ADR-044 D1: a split day is judged like any other day |
| **R3** | A continuous over-long shift | **CONFIRMED.** A shift over 13 hours is a breach in itself | Commission, OJ C 109/29; KEN 9938/2006; KEN 9298/2025 (a standing 24-hour arrangement needs the organisations' agreement) | ADR-044 D1; the maximum-daily-hours check stays as a backstop |
| **R4** | The weekly rest day | **PARTLY.** 35 continuous hours; at most 6 døgn between two (Arbejdstilsynet), 10 working days under the state agreement § 12 stk. 3; the 7-døgn anchor is defined nowhere; on-call never counts toward it | LBK 442 § 51; BEK 606 § 4; VEJ 9667/2024 § 2; VEJ 9638 § 4; CJEU C-477/21, C-306/16 | Registered, not built: QUAL-256 |
| **R5** | Derogations for the state | **CONFIRMED with corrections.** § 12 allows a cut only, to 8 hours, once per 7 døgn (twice by local agreement, not two in a row), with compensatory rest equal to the cut before the next work; CIR 9807/1983 (central administration, signed by AC) cuts or postpones and is listed as valid, but whether it still works under BEK 606 is unverified; the AC agreement has no rest clause; some staff are exempt under BEK 606 § 6 | Statens arbejdstidsaftale § 12 (CIR1H 9594 = Medst. 022-26; identical in CIR1H 9551); BEK 606 §§ 5–34; CJEU C-151/02 (Jaeger) para. 97; PAV 19.7–19.8 | ADR-044 D5: the 8-hour floor; QUAL-254 (frequency, compensatory rest, postponement); QUAL-255 (§ 6) |

## Corrections the verification made to the first pass

Numbered as in Part 1, § 3, where each is given in full. Part 2 is kept byte for byte; this index marks where each correction applies in it.

| Corr. | In short | Where it applies in Part 2 |
|---|---|---|
| 1 | Only KEN 9938/2006 calls a 24-hour shift a "significant exceedance"; KEN 9298/2025 refused a general dispensation and grades nothing | the summary's bullet "The two missed cases are breaches"; the verdicts table, row R3 (§ R3-2's own text is accurate) |
| 2 | The 1996 change took effect on 29 Dec 1996; November 1997 is the date of Arbejdstilsynet's notice | the summary ("What applies since November 1997"); the heading of § R1-4 ("1997 to now") |
| 3 | The Arbejdstilsynet notices do not cite BEK 324/2002 or LBK 2062/2021; PAV does | "Premise corrections and currency notes", item 3 |
| 4 | The directive-implementation agreement is Bilag 1 of 022-26, not bilag 2 | § R2-1; "What the algorithm must do", item 2; the sources table, row 11 |
| 5 | A source does give the amount of compensatory rest: Jaeger para. 97 (equal to the cut, before the next work) | § R5-4, "How much compensatory rest"; "Searched and NOT FOUND" |
| 6 | CIR 9807's `EndDate 2026-06-24` is a record date, not an expiry; whether the circular still works under BEK 606 is an expert question | § R5-3, "Status" |
| 7 | "8 h instead of 11, 16 h instead of 13" models a cut, not a postponement; CIR 9807 also allows postponement (22 hours of work in a row) | "What the algorithm must do", item 6 |
| 8 | "Rolling" per 7 døgn is an inference; "not 2 døgn in a row" is undefined | "What the algorithm must do", item 6; the verdicts table, row R5 |
| 9 | The split-day example breaches on day 1 at 22:00, without the next morning | the verdicts table, row R2; § R2-4 |
| 10 | "Below 8 hours always needs a dispensation" is the board's shorthand; the routes are BEK 606 §§ 34, 21, 5 and 7 | the summary's last bullet; the verdicts table, row R5; "What the algorithm must do", item 6 |
| 11 | Agreements under BEK 606 §§ 19–22 are not limited to Bilag 1 activities | § R2-2 |
| 12 | Missed: the BEK 606 § 6 exemption, assessed person by person | the verdicts table, row R5; § R5-2, "Who is covered" |
| 13 | Missed: on-call never counts toward the weekly rest day | § R4; "Notes for the design", the weekly check |
| 14 | Missed: AC staff in central administration reach CIR 9807's derogation route | § R5-2, "Who is covered"; § R5-3 |
| 15 | Pinpoints and quotations: page numbers; an omitted "(see Chapter IX)"; the KEN 9938 quote joins two sentences | § R2-1, § R2-2, § R3-2, § R4-5; the sources table |
| 16 | PAV shows "Sidst redigeret 03.12.24" and lags OK26 and BEK 606; PAV kap. 19 does not list CIS 6240/1984 | § R5-3, the last paragraph; the sources table, row 14 |
| 17 | The 1996 bill's "successive 7-day periods" sentence comes from the remark on § 51 stk. 3 (postponement), not a general anchor | § R4-3 |
| 18 | Further corroboration: VEJ 9576/2016 (offshore) states the same test; CJEU C-585/19 (several contracts with one employer) | an addition; no Part 2 passage |
| 19 | 022-26's repeal clause misdates the 2021 circular (5 June, not 5 July); immaterial | context for § R5-2, "Version stability" (not a first-pass error) |

## Open questions (the sources are silent)

| # | Question | Needed for S148's build? | Disposition (RR-6) |
|---|---|---|---|
| (f) | What to assume before the first record in the data | Yes | Defaulted: unregistered time is rest (ADR-044 D6) |
| (a) | The 7-døgn anchor, and whether it rolls | No | Expert; blocks QUAL-254 and QUAL-256 |
| (b) | What "døgn" means in "not 2 døgn in a row" (and in VEJ 9638's "det normale arbejdsdøgn") | No | Expert; blocks QUAL-254 |
| (c) | Whether CIR 9807/1983 still works under BEK 606, and how its "arbejdsdøgn" maps onto the look-back | No | Expert; StatsTid models no postponement |
| (d) | Whether compensatory hours come on top of the next 11-hour rest or inside it | No | Expert; if built before an answer, "on top", the stricter reading |
| (e) | How a night shift counts toward § 12 stk. 3's "arbejdsdage" | No | Expert; blocks QUAL-256 |

Questions (a)–(e) belong in the expert hand-off package (`docs/references/phase-b-handoff-package.md`).

## Sources and how this file is built

Every decisive source was re-fetched on 2026-11-13; Part 1, § 5 lists each with its route and status, and Part 2 ends with the first pass's own list, with URLs. Part 1 is `.claude/reviews/S148-RESEARCH-rest-24h-verification.md` from its line 3, and Part 2 is `.claude/reviews/S148-RESEARCH-rest-24h-pass1.md` from its line 3, both verbatim; their own title lines are replaced by the part headings. No anchor marker (RR-8).

---

# Part 1 — The independent verification (governs)

**Date:** 2026-11-13 (every source below was re-downloaded and re-read on this date). **Method:** independent adversarial verification (`general-purpose`, Opus 5.5). Input taken from pass 1 (`research-rest-24h-pass1.md`): only its claims, quotes, identifiers and cited sources. Its reasoning was not relied on. Every decisive source was fetched again from its primary location:
- retsinformation.dk: the PDF route (`api/pdf/<id>`, `eli/lta/<y>/<n>/pdf`), the document API (`POST api/document/eli/...`) for metadata and for documents with no PDF, and the XML export (`eli/.../xml`) for the database dates;
- MedSt's own PDF of circular 022-26 (cirkulaere.medst.dk);
- pav.medst.dk (PAV kap. 19);
- the EU Publications Office (content-negotiated CELEX/OJ copies) for the directive, the Commission's 2023 Interpretative Communication and five CJEU judgments;
- at.dk (the July 2024 news item).

Text was extracted with `pdftotext -enc UTF-8` or an HTML-to-text step. Quotes were compared after whitespace normalisation only.

**Status: verification of pass 1. Where this file corrects pass 1, this file governs.**

---

## Plain-language summary (for the product owner)

**Pass 1 holds up on the big question.** The Danish rule is "at least 11 consecutive hours of rest within each period of 24 hours". It is **not** measured per calendar day. It has **not** been measured from "the start of the normal working day" since Parliament changed the law in December 1996. Arbejdstilsynet, the authority that enforces the rule, gives one test, and its current notice (2024) uses the same words as in 1997: from a moment inside working time, look 24 hours back; that stretch must contain 11 unbroken hours of rest. I found the same test in a third, still-current Arbejdstilsynet text (the 2016 offshore guidance).

**Pass 1's algorithm rule is right.** Put simply: *after the last 11-hour rest ends, no work may go on more than 13 hours later.* An equivalent way to say it: every unbroken run of work, where no gap inside it reaches 11 hours, may span at most 13 hours from its first start to its last end. The Danish texts never say "every moment" outright, and they never say that rest outside the window doesn't count. But every other reading lets a 24-hour shift pass. The appeal board ruled in 2006 that a 24-hour shift is a significant breach, and the European Commission says work may not run more than 13 consecutive hours. So those other readings are ruled out, and only pass 1's reading fits the decided cases.

**What I corrected.** There are 19 corrections; none overturns a verdict. The ones that matter for StatsTid:
- **The split-day example breaches on the day itself.** The day 08:00–12:00 + 17:00–22:00 already breaches at 22:00 (a 14-hour span), whether or not work starts again at 08:00 the next morning. So any split day longer than 13 hours from first start to last end needs a derogation. That is why EU law lists "split days, e.g. cleaning" as a derogation category.
- **Postponing the rest is a different permission from cutting it.** Pass 1's "use 8 h instead of 11 and 16 h instead of 13" models a *cut* to 8 hours. Some derogations instead *postpone* the rest. The 1983 central-administration circular is one: its own remarks say staff could "typically work 22 hours in a row". A postponement needs its own parameter. The state agreement's § 12 is cut-only.
- **The amount of compensatory rest is in a source.** Pass 1 said no source gives it. The EU Court did, in Jaeger (2003) para. 97: the compensatory rest is "a number of consecutive hours corresponding to the reduction applied", taken "before commencing the following period of work". So a cut from 11 to 8 means 3 hours back.
- **The 2025 appeal-board decision does not say "significant exceedance".** It refused a general dispensation because a standing 24-hour-shift arrangement needs an agreement between the employers' and employees' organisations. Only the 2006 decision uses the "significant exceedance" wording.
- **The odd 2026 end date on the 1983 circular means nothing.** It is a database timestamp, not an expiry. The same field carries similar recent dates on documents that are plainly in force, including the 1981 sister circular and the current Act. Retsinformation lists the circular as "Valid". Whether a 1983 agreement written for the pre-1997 rule still works under today's executive order is a question no source answers.
- **Pass 1 missed three things that change the design.**
  - Staff who set their own working time, or who manage, can be **exempt** from the rest rules. The assessment is made for each person, never for a whole group.
  - **On-call time can never count toward the weekly rest day**, even though on-call at home counts as daily rest.
  - The 1983 central-administration agreement is **signed by AC**. That gives AC staff in ministries a derogation route for listed deadline tasks, even though the AC agreement has no rest clause.

---

## 1. Claim-by-claim verification

Verdicts: **CONFIRMED** = the source says what pass 1 says. **CORRECTED** = the substance stands but a detail is wrong (the correction is given). **REFUTED** = the claim is wrong. For pass 1's inferences: **SUPPORTED / CONTRADICTED / SILENT**. "Corr. n" points to the numbered corrections in § 3.

### Currency and identifiers

| # | Pass 1 claim | Verdict | Correction / note | Citation (re-read 2026-11-13) |
|---|---|---|---|---|
| V-01 | BEK nr. 606 af 29/06/2026 is the order; in force 1 July 2026; repeals BEK 324/2002 | **CONFIRMED** | Published 30/06/2026. BEK 324 is `isHistorical: true`, historicalDate 29/06/2026 | BEK 606 § 37 stk. 1–2, PDF p. 4; retsinformation metadata |
| V-02 | LBK nr. 442 af 24/04/2026 is the current Act; § 50 unchanged | **CONFIRMED** | `isHistorical: false`; no later LBK. § 50 stk. 1 is word-for-word LOV 1196/1996 | LBK 442 kap. 9, PDF p. 10 |
| V-03 | BEK 606 § 3 repeats § 50; footnote implements Dir. 93/104 | **CONFIRMED** | Verbatim | BEK 606 § 3 and fn. 1, PDF p. 1 |
| V-04 | AT notices (VEJ 9666/9667/9638) and PAV still cite BEK 324/2002 and LBK 2062/2021 | **CORRECTED** | **PAV does** (19.7, 19.7.1). **The three AT notices do not**: they name the order by title only ("Bekendtgørelse om hvileperiode og fridøgn m.v."). Neither "324" nor "2062" occurs in their PDF or API text. The 2025 KEN and the 2024 at.dk news item do cite BEK 324 (Corr. 3) | VEJ 9666 §8; VEJ 9667 §8; VEJ 9638 §8; PAV 19.7 |

### R1: the anchor of "hver periode på 24 timer"

| # | Pass 1 claim | Verdict | Correction / note | Citation |
|---|---|---|---|---|
| V-05 | Commission 2023: "the Directive does not define this period as being a calendar day …", with fn. 182–184 | **CONFIRMED** verbatim | On C 109/29 as stated. Fn. 183 and 184 both cite Jaeger para. 95 | OJ C 109, 24.3.2023, ch. V.A |
| V-06 | Old § 50: "i hvert døgn, regnet fra normal arbejdstids begyndelse …" | **CONFIRMED** verbatim | — | 1996/1 LSF 59, Bilag |
| V-07 | VEJ 3045/1984: døgn counted from the start of normal working hours, or the actual start if none | **CONFIRMED** verbatim | Status Historisk (since 02/04/1998) | VEJ 3045, "11 timers hvileperiode" |
| V-08 | LSF 59 remark "Til § 50" (three paragraphs) and the minister's tabling speech | **CONFIRMED** verbatim | Tabled 6 Nov 1996 by Jytte Andersen; adopted at third reading 19 Dec 1996 | LSF 59, "Til § 50"; "Skriftlig fremsættelse" |
| V-09 | Enacted unchanged as LOV 1196/1996; "what applies since November 1997" | **CONFIRMED / CORRECTED** | Enacted text confirmed. **In force 29 Dec 1996** (§ 2: the day after publication; published 28/12/1996). November 1997 is the date of AT's notice, not of the rule (Corr. 2) | LOV 1196 § 1 nr. 1, § 2 |
| V-10 | VEJ 9666/2024 section 2: the three-sentence look-back test | **CONFIRMED** verbatim | PDF p. 2; status Gældende | VEJ nr. 9666 af 05/09/2024, § 2 |
| V-11 | MED 3503/1997 has the same three sentences | **CONFIRMED** verbatim | Status Historisk since 09/09/2024 | MED nr. 3503 af 01/11/1997 |
| V-12 | KEN 9938/2006 applies AT-meddelelse 5.01.1/Nov 1997 and reproduces the test | **CONFIRMED** | The rules list names it, and the annex excerpt reproduces the test sentences | KEN nr. 9938 af 04/09/2006 |
| V-13 | The text says "et tidspunkt", not "ethvert tidspunkt" | **CONFIRMED** (wording) | — | VEJ 9666 § 2 |
| V-14 | [I] Apply the test at **every** point of working time | **SUPPORTED** (by elimination) | The text is silent. The two alternatives (only at work starts; "any one point") let a 24-hour shift pass, which KEN 9938/2006 and the Commission's 13-hour statement contradict (see § 2) | KEN 9938; OJ C 109/29 |
| V-15 | [I] Count only the part of a rest block **inside** the window | **SUPPORTED** (by elimination) | Without clipping, the test reduces to "work-end minus rest-end ≤ 24 h", which also passes a 24-hour shift and the split day plus night shift (see § 2) | Same |
| V-16 | Directive Art. 2(1)–(2), Art. 3 | **CONFIRMED** verbatim | PDF p. 3 | Dir. 2003/88/EC |
| V-17 | Jaeger para. 95 (and 94) | **CONFIRMED** verbatim | **Para. 97 was missed** (Corr. 5) | C‑151/02 |
| V-18 | No CJEU judgment fixes the start of the 24-hour period | **CONFIRMED** (NOT FOUND) | Re-read Jaeger, MÁV-START, Maio Marques da Rosa and C‑585/19 (*Academia de Studii Economice*, 17 Mar 2021); searched again. C‑585/19 holds that daily rest applies per worker across several contracts with one employer, which is relevant to StatsTid but says nothing about the anchor | C‑585/19, operative part |
| V-19 | State agreement § 7 nr. 3 remark: "Ved døgn forstås i denne sammenhæng arbejdsdøgnet …", limited to travel | **CONFIRMED** verbatim | PDF p. 9 | CIR1H 9594 § 7 nr. 3 remark |
| V-20 | VEJ 9638 § 3 "det normale arbejdsdøgn" (on-call) | **CONFIRMED** verbatim | PDF p. 3. Not defined in the notice | VEJ nr. 9638 af 05/09/2024 |
| V-21 | BEK 606 § 14 stk. 1 (after travel abroad: "i det sidste døgn forud for genoptagelsen") | **CONFIRMED** verbatim | PDF p. 2 | BEK 606 § 14 |
| V-22 | AT applies the Commission's 2023 Communication (VEJ 9666 §§ 6–7; VEJ 9638 § 3) | **CONFIRMED** | — | as cited |

### R2: split working days

| # | Pass 1 claim | Verdict | Correction / note | Citation |
|---|---|---|---|---|
| V-23 | Rest = all non-working time (Art. 2(2); the state directive-implementation agreement § 2 nr. 2, § 3), annexed as **bilag 2** to CIR1H 9594, PDF pp. 18–19 | **CONFIRMED / CORRECTED** | Text verbatim. **It is Bilag 1** of 022-26 (MedSt's own table of contents: "Bilag 1 Aftale om visse aspekter …", p. 27; "Bilag 2 Aftale om yderligere særlige helligdage i Grønland", p. 33). The § 12 remark's "jf. bilag 2" is a stale cross-reference, already present in 2021. Both §§ are on PDF p. 19 (Corr. 4) | CIR1H 9594, Bilag 1 §§ 2–3; MedSt 022-26 TOC |
| V-24 | Commission: "The 11 hours of daily rest must be consecutive. … under the permitted derogations." | **CONFIRMED** | Pass 1 dropped "(see Chapter IX)" after "derogations" without an ellipsis (Corr. 15) | OJ C 109/29 |
| V-25 | BEK 606 § 33 stk. 4 (summing allowed under home-work derogations) | **CONFIRMED** verbatim | PDF p. 3. [I] "summing not allowed elsewhere": **SUPPORTED** by contrast and by "sammenhængende" in § 3 | BEK 606 § 33 stk. 4 |
| V-26 | Directive Art. 17(4)(b) "periods of work split up over the day, particularly those of cleaning staff" | **CONFIRMED** verbatim | **PDF p. 5**, not p. 6 (Corr. 15) | Dir. 2003/88/EC |
| V-27 | Commission C 109/58: no case-law on Art. 17(4)(b); compensatory rest applies | **CONFIRMED** verbatim | — | OJ C 109/58 |
| V-28 | BEK 606 Bilag 1 nr. 6 (split-day activities, e.g. cleaning) | **CONFIRMED** verbatim | PDF p. 5 | BEK 606 Bilag 1 |
| V-29 | "These activities may deviate only by § 34 dispensation, a § 7 single deviation, or an agreement (§§ 19–22)" | **CORRECTED** | The agreement route (§§ 19–22) is **not** limited to Bilag 1 activities. The Bilag-limited routes are §§ 7, 8 stk. 2, 10, 16 stk. 3, 17 and 34 (Corr. 11) | BEK 606 §§ 7–34 |
| V-30 | PAV 19.2.10 "delt tjeneste" is a pay rule | **CONFIRMED** verbatim | — | PAV 19.2.10 |
| V-31 | No worked split-day example in AT/MedSt texts | **CONFIRMED** (NOT FOUND) | Also absent from VEJ 9576/2016 (offshore) | texts in § 5 |
| V-32 | Brief's example (08–12 + 17–22, then 08:00) fails "under every reading" | **CONFIRMED / CORRECTED** | The verdict stands, but pass 1 places the failure at next-day 08:00. **It already fails at 22:00 on day 1**: span 14 h > 13, and the window [22:00 previous day, 22:00] holds at most 10 h of unbroken rest, however the previous day ended. The next morning is not needed (Corr. 9) | arithmetic on VEJ 9666 § 2 |
| V-33 | Variant ending 21:00 passes | **CONFIRMED** | Span 13 h; rest 21:00–08:00 = 11 h | arithmetic |

### R3: a continuous over-long shift

| # | Pass 1 claim | Verdict | Correction / note | Citation |
|---|---|---|---|---|
| V-34 | Commission: "consecutive working time is limited to 13 hours … cannot carry out work for more than 13 consecutive hours" | **CONFIRMED** verbatim | OJ C 109/29. "(from which the break presented below must be deducted)": the 13 h is a span, breaks included | OJ C 109/29 |
| V-35 | KEN 9938/2006: the whole 24-hour shift is working time; "en betydelig overskridelse af hvileperioden" | **CONFIRMED** | The quote joins Resumé sentence 1 to the reasoning's sentence 2. The Resumé itself is in the past tense ("I denne vurdering indgik, at der var tale om en betydelig overskridelse …"). Context: on-site sleep 23–07 with possible call-outs; the employer had no collective agreement (Corr. 15) | KEN 9938, Resumé and Begrundelse |
| V-36 | KEN 9938: "Hvis man ønsker at gå under 8 timer, kræver det altid en dispensation fra Arbejdstilsynet" | **CONFIRMED** verbatim | The board's 2006 shorthand. Today's routes below 8 h are wider (Corr. 10) | KEN 9938, Begrundelse |
| V-37 | KEN 9298/2025 Resume quote | **CONFIRMED** verbatim | **PDF p. 2** (p. 1 is the print cover) | KEN nr. 9298 af 25/02/2025 |
| V-38 | Headline: the 2006 **and 2025** decisions "call a 24-hour shift a significant exceedance" | **CORRECTED** | Only KEN 9938/2006 uses that wording. KEN 9298/2025 refused a *general* dispensation for 24-hour shifts because a general deviation "forudsætter en aftale mellem arbejdsgivers og arbejdstagers organisationer". It does not grade the exceedance. Pass 1's body text describes it correctly; the headline overstates it (Corr. 1) | KEN 9298, Resume and Begrundelse |
| V-39 | "13 timer" as a daily maximum is absent from Danish primary rest texts | **CONFIRMED** | Zero hits in LBK 442 kap. 9, BEK 606, VEJ 9666/9667/9638, MED 3503. The only "13 timer" in CIR1H 9594 is the travel cap | full-text search |
| V-40 | 16-hour shift (06–22): fails at 11 h; passes once with a valid 8-h reduction | **CONFIRMED** | Work-end minus rest-end = 16 ≤ 16 | arithmetic |

### R4: the weekly rest day (fridøgn)

| # | Pass 1 claim | Verdict | Correction / note | Citation |
|---|---|---|---|---|
| V-41 | § 51 stk. 1 = BEK 606 § 4 stk. 1 | **CONFIRMED** verbatim | — | LBK 442 § 51; BEK 606 § 4 |
| V-42 | VEJ 9667 § 2: "ikke må være mere end 6 døgn mellem 2 fridøgn … 35 timer hvert 7. døgn" | **CONFIRMED** verbatim | PDF p. 2 | VEJ nr. 9667 af 05/09/2024 |
| V-43 | PAV 19.7.2 "(dvs. højst 6 døgn mellem 2 fridøgn)" | **CONFIRMED** verbatim | **Missed in the same paragraph:** "Hverken rådighedstjeneste i hjemmet eller på arbejdsstedet betragtes som hvileperiode i relation til det ugentlige fridøgn" (Corr. 13) | PAV 19.7.2; VEJ 9638 § 4 |
| V-44 | LSF 59 "Til § 51": a postponed fridøgn does not move the start of the next 7-day period; partners may extend to 12 days | **CONFIRMED** verbatim | Context: the sentence sits in the remark on **stk. 3** (postponement for care of people, animals and plants, and preservation of values). It describes that postponement, not a general anchor (Corr. 17) | LSF 59, "Til § 51" |
| V-45 | No statute or order defines the start of the 7-døgn period | **CONFIRMED** (NOT FOUND) | — | LBK 442, BEK 606, VEJ 9667, PAV 19.7.2 |
| V-46 | Directive Art. 5 and Art. 16(a) (14-day reference period) | **CONFIRMED** verbatim | PDF p. 3 | Dir. 2003/88/EC |
| V-47 | Commission C 109/31: "a continuous rest period of 35 hours" | **CONFIRMED** verbatim | C 109/31 also says "The rest period may be provided at any time within each seven-day period", citing Maio paras. 39 and 44 | OJ C 109/31 |
| V-48 | MÁV-START C‑477/21 operative part 1–3 | **CONFIRMED** verbatim | — | C‑477/21, 2 Mar 2023 |
| V-49 | Maio Marques da Rosa C‑306/16 operative part; para. 44; para. 49 (Art. 15 more favourable rules) | **CONFIRMED** verbatim | [I] "Denmark's 6-døgn reading is a stricter national rule": **SUPPORTED** (plausible; no source says it) | C‑306/16 |
| V-50 | CIR1H 9594 § 12 stk. 3 (max 10 arbejdsdage between 2 fridage); PAV 19.7.2 link; BEK 606 § 20 (12 døgn) | **CONFIRMED** verbatim | — | CIR1H 9594 PDF p. 13; BEK 606 § 20 |
| V-51 | § 15 stk. 2 (fridag ≥ 40 h, or ≥ 36 h if it holds a whole calendar day), "PDF p. 14" | **CONFIRMED / CORRECTED** | Text verbatim. **PDF p. 15** (printed page 14) (Corr. 15) | CIR1H 9594 § 15 stk. 2 |
| V-52 | How a night shift counts toward § 12 stk. 3 "arbejdsdage" | **CONFIRMED** (NOT FOUND) | — | — |

### R5: derogations for the state

| # | Pass 1 claim | Verdict | Correction / note | Citation |
|---|---|---|---|---|
| V-53 | § 50 stk. 2; BEK 606 §§ 5, 7, 8, 16 stk. 3, 34 | **CONFIRMED** verbatim | § 16 stk. 3 (on-call: max 10 døgn per calendar month, 45 per year) applies only to Bilag 1 work | BEK 606 pp. 1–4 |
| V-54 | BEK 606 §§ 19, 21, 22, 24, 27 | **CONFIRMED** verbatim | § 19 allows the rest to be "udskydes **eller** nedsættes til 8 timer" (postponed *or* cut) | BEK 606 pp. 2–3 |
| V-55 | BEK 606 § 36 (penalty incl. strict liability; the state can be fined) | **CONFIRMED** verbatim | — | BEK 606 § 36, PDF p. 4 |
| V-56 | State agreement § 12 stk. 1–3 and its remark | **CONFIRMED** verbatim | PDF p. 13. Identical in MedSt's own 022-26 PDF ("Cirkulære af 18. juni 2026 Medst.nr. 022-26 J.nr. 2026-494") | CIR1H 9594 § 12 |
| V-57 | Identical § 12 in CIR1H 9551 (OK21) | **CONFIRMED** | PDF p. 13. CIR1H 9551 is dated and in force 5 July 2021, effective 1 April 2021. Retsinformation lists 9594 as its successor. (022-26's repeal clause calls the 2021 circular "af 5. juni 2021 (Medst. nr. 014-21)", a slip in 022-26, not material; Corr. 19) | CIR1H 9551 |
| V-58 | PAV 19.2.5.2 "Ved 'ugentlig' forstås … 'pr. 7 døgn'" | **CONFIRMED** verbatim | "**Rolling** per 7 døgn" (pass 1's algorithm item 6, tagged [S]) is pass 1's inference. PAV only contrasts it with "pr. arbejdsuge" (Corr. 8) | PAV 19.2.5.2 |
| V-59 | PAV 19.7.1: the employer alone once a week; by local agreement twice, not 2 døgn in a row | **CONFIRMED** verbatim | — | PAV 19.7.1 |
| V-60 | AC agreement CIR1H 9333 has no rest clause; lists Perst. 055-03 | **CONFIRMED** | No hit for hvileperiode, hviletid, fridøgn or arbejdsmiljøl. Bilag 1 item 6 names Perst. 055-03. **Missed:** the CIR 9807 route and the BEK 606 § 6 exemption (Corr. 12, 14) | CIR1H 9333 |
| V-61 | CIR 9807/1983: identifiers, signatories, remarks, §§ 4, 5, 7 | **CONFIRMED** verbatim | CIR nr. 9807 af 08/11/1983, "Udsendt som LP.nr. 148/83". Signatories: StK I, StK II, LC, AC, HK, VSL, DIF and Finansministeriet. § 1 derogates from the *1975-Act numbering* (§ 50 stk. 1, § 53 stk. 1). § 6 refers to the 1980 order | document API, retsinfo/2007/9807 |
| V-62 | CIR 9807 is in force; XML `EndDate 2026-06-24` unexplained | **CONFIRMED / RESOLVED** | `Status: Valid`, `isHistorical: false`. The `EndDate` is tied to the submission record (`REFid="submit_1"`). It is a database timestamp, **not an expiry** (Corr. 6) | XML exports compared in § 5 |
| V-63 | PAV 19.7.1 and 19.7.3 cite CIR 9807 as current | **CONFIRMED** | PAV 19.7.1: "Fmst.cirk. 8/11 1983 …"; 19.7.3: "Eksempelvis …". Also listed in 19.10 | PAV 19.7.1, 19.7.3, 19.10 |
| V-64 | PAV also lists "CIS nr. 6240 af 03/05/1984 (junior doctors)" | **NOT REPRODUCED** | PAV kap. 19 does not mention it. Its 19.10 lists Fmst.cirk. 4/5 1984 on *rådighedstillæg* (LP 083/84), a pay circular (Corr. 16) | PAV 19.10 |
| V-65 | The remark's "22 timer i træk" = 24 − 2 | **CONFIRMED** | § 5 stk. 2: the postponed 11-h rest must end ≤ 9 h into the next working døgn, so it starts ≤ 2 h before the døgn ends | CIR 9807 remarks, § 5 stk. 2 |
| V-66 | BEK 606 § 11 (discussion with the shop steward) | **CONFIRMED** verbatim | — | BEK 606 § 11 |
| V-67 | VEJ 9666 § 5 (an emergency deviation is reported in writing) and § 6 (compensatory rest "i umiddelbar forlængelse af afslutningen af det sidste arbejde") | **CONFIRMED** verbatim | PDF p. 4. Note: VEJ 9667 § 6 still says a compensatory **fridøgn** is given "snarest muligt" | VEJ 9666 §§ 5–6; VEJ 9667 § 6 |
| V-68 | at.dk news 2 July 2024 ("snarest muligt" no longer enough; "Det gælder alle former for fravigelser …") | **CONFIRMED** verbatim | — | at.dk/nyheder/2024/07/regler-om-arbejdstid-og-hvileperiode/ |
| V-69 | Jaeger para. 94 ("must therefore follow on immediately") | **CONFIRMED** verbatim | — | C‑151/02 |
| V-70 | NOT FOUND: a formula for "tilsvarende" compensatory rest | **REFUTED** | **Jaeger para. 97:** "equivalent periods of compensatory rest made up of a number of consecutive hours corresponding to the reduction applied and from which the worker must benefit before commencing the following period of work" (Corr. 5) | C‑151/02 para. 97 |
| V-71 | PAV 19.7.3 (individual agreements need AT approval) | **CONFIRMED** verbatim | — | PAV 19.7.3 |
| V-72 | PAV 19.8 (registration system, 5-year retention, LBK 982/2024) | **CONFIRMED** (PAV text) | PAV 19.8 also: "Det er ikke muligt at undtage hele personalegrupper" (Corr. 12). The Act itself was not re-read | PAV 19.8 |
| V-73 | PAV page carries no version date | **CORRECTED** | The page shows "Sidst redigeret 03.12.24". It still cites LBK 2062/2021, BEK 324/2002 and Skm. cirk. 5/7 2021, so it lags OK26 and BEK 606 (Corr. 16) | PAV kap. 19 footer |

### Pass 1's algorithm inferences ("What the algorithm must do")

| # | Pass 1 claim | Verdict | Note |
|---|---|---|---|
| V-74 | [I] Evaluating at the **end** of each work interval is enough (the window's best rest only shrinks as *t* moves through a work interval) | **CONFIRMED** (re-derived) | For a rest block [s, e] with e ≤ a, its in-window part is e − max(s, t − 24), which cannot grow as t moves forward through [a, b]. Holds under the clipped reading |
| V-75 | [I] Equivalent rule: pass iff **b − e ≤ 13 h** (b = work end, e = end of the latest ≥ 11-h rest) | **CONFIRMED** (re-derived) | Restated: **every maximal run of work whose internal gaps are all < 11 h ("chain") spans ≤ 13 h from first start to last end**. This form is easier to test and to explain |
| V-76 | [I] A forward window anchored at **every** work start gives the same verdicts | **CONFIRMED** (re-derived) | Forward test ⇔ "the next ≥ 11-h rest begins ≤ 13 h after each work start" ⇔ the same chain-span rule. The two can differ only at the edges of the data |
| V-77 | [I] With a valid reduction: 8 h instead of 11 and 16 h instead of 13; this applies to state § 12, BEK §§ 8/16 and CIR 9807 | **CONFIRMED / CORRECTED** | Right for a **cut to 8 h**. **Wrong for a postponement** (*udskydelse*: BEK § 16 stk. 3, § 19; CIR 9807 § 5 stk. 2 → up to 22 h of work in a row). A postponement needs a separate span parameter. State § 12 is cut-only (Corr. 7) |
| V-78 | [I] Data start needs an explicit assumption | **SUPPORTED** (sources silent) | Add: time must be **elapsed** time, not wall-clock. The rule is in hours ("24 timer", "11 … timer"), and PAV 19.9 confirms the daylight-saving døgn are 23 h and 25 h long [I] |

---

## 2. The algorithm cases

**Pass 1's inference under test (reading E):** at every point of working time, the 24-hour look-back window must contain ≥ 11 h of unbroken rest. Only the part inside the window counts. Equivalently: work-end minus last-≥11-h-rest-end ≤ 13 h, or chain span ≤ 13 h.

**The alternative readings.** I tested each case against four alternatives, because the sources say "et tidspunkt" (a point), not "ethvert" (every):
- **S, start only:** the look-back test is applied only at the start of each work period. This is the reading suggested by the first sentence, "fra det tidspunkt, hvorfra arbejdstiden beregnes".
- **U, unclipped:** every point is tested, but a rest block counts in full if it touches the window.
- **C, calendar day:** the longest rest inside each calendar day must be ≥ 11 h.
- **O, the pre-1997 rule:** one fixed anchor per day at the normal start, with a forward window.

(The literal "at least one point anywhere" reading is not tested separately. It passes almost any schedule, since the first minute after a weekend always passes.)

| Case | E (pass 1) | S (start only) | U (unclipped) | C (calendar) | O (pre-1997) | Which source decides | Verdict |
|---|---|---|---|---|---|---|---|
| **(i)** Tue 08–16, Tue 22–Wed 06, Thu 08–16 | **Breach**: the chain from Tue 08:00 (the 6-h gap 16–22 does not end it) reaches 14 h at the first minute of the night shift; at Wed 06:00 the span is 22 h > 13; window holds max 6 h | **Breach** at Tue 22:00: window [Mon 22, Tue 22] holds 10 h (clipped) + 6 h | **Pass** (Mon 16–Tue 08 block touches every window up to Wed 08:00) | Breach (Tue: max 8 h) | Breach (Tue 08–Wed 08: max 6 h) | The **clipping** element decides it. U is ruled out by (ii) | **Breach.** Pass 1 is CONFIRMED. Under an 8-h cut it is still a breach (chain span 22 h > 16) |
| **(ii)** One 24-h continuous shift (12:00 → 12:00) | **Breach**: chain span 24 h > 13; window at end holds 0 h | **Pass** (the start sees ample rest) | **Pass** at the boundary (24 ≤ 24) | **Pass** (12 h rest in each calendar day) | Breach | **KEN 9938/2006** (24-h shift = "betydelig overskridelse af hvileperioden", needs dispensation) and the **Commission** ("cannot carry out work for more than 13 consecutive hours") rule out S, U and C | **Breach.** Pass 1 is CONFIRMED. Still a breach with an 8-h cut (24 > 16). Only an AT dispensation, an AT-approved agreement (§ 21) or the organisations' agreement (KEN 9298/2025) can cover it |
| **(iii)** 13 h work, 11 h rest, repeated (07–20, rest 20–07) | **Lawful**: span 13 ≤ 13; window at 20:00 holds exactly 11 h | Lawful | Lawful | **Breach** (max 7 h per calendar day) | Lawful (anchored 07:00) | **Commission** "limited to 13 hours … followed by at least 11" and fn. 182 rule out C | **Lawful.** Pass 1 is CONFIRMED. The boundary is inclusive ("mindst 11"). 07:00–20:01 breaches |
| **(iv)** 08–12 + 17–22, then 08:00 next day | **Breach at 22:00 on day 1**: chain span 08→22 = 14 h; window [22 prev, 22] holds ≤ 10 h | Breach, **but only at next-day 08:00** (window max 10 h). **Passes if there is no next-day work** | Breach only with next-day work. **Passes without it** | Breach (day 1 max 8 h) | Breach (08–08: max 10 h) | **Every-point vs start-only is decisive** when the next day is free. Directive Art. 17(4)(b) / BEK Bilag 1 nr. 6 list split-day work as a *derogation* category, which only makes sense if a long split day breaches the base rule | **Breach.** Pass 1 is CONFIRMED, but the breach happens on day 1 itself (Corr. 9). The variant ending 21:00 is lawful (span 13 h) |
| **(v)** 08–16, then 08–16 next day | **Lawful**: span 8 h; window holds 16 h | Lawful | Lawful | **Breach** (max 8 h per calendar day), which is absurd | Lawful | Commission fn. 182 rules out C | **Lawful.** Not decisive between E, S, U and O |

**Is "at least one point" versus "every point" decisive?** Yes.
- **For (ii), always.** A start-only (or "any point") test can never catch a single long shift preceded by normal rest.
- **For (iv), when no work follows the next morning.** The 14-hour split day is then caught only by testing every point (in practice, the end of each work interval).
- **For (i), it is clipping that decides,** not every-point versus start-only. Start-only with clipping catches (i) at Tue 22:00. Every-point without clipping misses it.
- **For (iii) and (v), it is not decisive.**

**Why the inference is supported even though no Danish text says it.** No Danish text says "every point" or "only rest inside the window counts". But a test missing either element passes the 24-hour shift. The one decided Danish case on point (KEN 9938/2006) holds that a 24-hour shift breaches the rule, and so does the Commission's 13-hour statement. The 1996 bill also points the same way: the rest must lie *before* the work period, between *each* work period.

**Verdict on the inference:** **STANDS.** The sources support it by elimination, it decides all five cases correctly, and the proof that checking at work-interval ends is enough re-derives cleanly. It carries two limits:
1. **Postponement.** It models the 11-h rule and a cut to 8 h, not postponement derogations (Corr. 7).
2. **Edge rules the sources do not give:** what to assume at the start of the data, elapsed time versus wall-clock time (daylight saving), and employees who are exempt (Corr. 12).

---

## 3. Corrections to pass 1 (numbered)

1. **KEN 9298/2025 does not call a 24-hour shift a "significant exceedance".** Only KEN 9938/2006 does ("betydelig overskridelse af hvileperioden"). KEN 9298 refused a *general* dispensation for 24-hour shifts at a residential home, because a general deviation "forudsætter en aftale mellem arbejdsgivers og arbejdstagers organisationer". It does not grade the exceedance. (Headline and R3 summary; the R3-2 body is accurate.)
2. **The 1996 change took effect on 29 December 1996**, the day after LOV 1196 was published (28/12/1996; § 2). "What applies since November 1997" names the date of AT's notice (MED 3503), not of the rule.
3. **The AT notices do not cite BEK 324/2002 or LBK 2062/2021.** VEJ 9666, 9667 and 9638 name the order by title only. PAV kap. 19 does cite both, as do KEN 9298/2025 and the at.dk July 2024 news item. Pass 1's premise note 3 holds for PAV only.
4. **The state's directive-implementation agreement is Bilag 1 of 022-26** (MedSt TOC: Bilag 1 at p. 27; Bilag 2 = Greenland holidays at p. 33). The § 12 remark's "jf. bilag 2" is a stale cross-reference, also present in the 2021 circular. The cited §§ 2–3 are on PDF p. 19.
5. **A source does give the amount of compensatory rest.** Jaeger (C‑151/02) para. 97: "equivalent periods of compensatory rest made up of a number of consecutive hours corresponding to the reduction applied and from which the worker must benefit before commencing the following period of work". Pass 1's own reading (a cut from 11 to 8 means 3 hours back) is supported. Still open: how those hours combine with the next ordinary 11-h rest (on top of it, or inside it). No source says.
6. **The CIR 9807 "EndDate 2026-06-24" is a database record date, not an expiry.** It is tied to the submission record (`REFid="submit_1"`), and `Status` is `Valid`. In-force documents carry similar recent EndDates:

   | Document | EndDate | Status |
   |---|---|---|
   | CIR 9808/1981 | 2026-06-24 | Valid |
   | VEJ 9666 | 2026-06-23 | Valid |
   | BEK 606 | 2026-06-30 | Valid |
   | CIR1H 9594 | 2026-06-19 | Valid |
   | LBK 442 | 2026-11-04 | Valid |

   **New caveat:** neither BEK 324/2002 nor BEK 606/2026 has a transitional clause preserving older agreements. CIR 9807 is written in the 1975 Act's numbering and the pre-1997 "arbejdsdøgn" model. Its § 7 makes it conditional on the AT director's approval, which the text does not evidence. Its database status and PAV's citation are the only evidence that it is in force. How its § 5 stk. 2 ("afsluttet senest 9 timer inde i det efterfølgende arbejdsdøgn") maps onto today's rolling test is answered by no source. **Expert question.**
7. **"8 h / 16 h" covers a cut, not a postponement.** BEK 606 § 16 stk. 3 and § 19 allow the rest to be "udskydes" (postponed). CIR 9807 § 5 allows "nedsættes til 8 timer **eller** udskydes", and its remarks say this lets staff "arbejde 22 timer i træk". A postponement needs its own span limit (22 h for CIR 9807; for § 16 stk. 3 the rest is given "efter afslutningen af det sidste arbejde"). The state agreement § 12 allows a **cut only**. Pass 1's algorithm item 6 lists CIR 9807 under the 8/16 substitution, which is wrong for its postponement mode.
8. **"Rolling" per 7 døgn is an inference.** PAV 19.2.5.2 says only that "ugentlig" means "pr. 7 døgn", not "pr. arbejdsuge". Whether the 7 døgn roll or run as fixed blocks is not stated. Nor is the meaning of "ikke 2 døgn i træk" (calendar days or 24-hour periods). Pass 1 tagged "rolling" as [S].
9. **The split-day example breaches on day 1 itself.** 08:00–12:00 + 17:00–22:00 has a chain span of 14 h. The window [22:00 previous day, 22:00] holds at most 10 h of unbroken rest, whatever the previous day was. So it breaches at 22:00 even if no work follows the next morning. General consequence: **a split day whose first start to last end exceeds 13 h breaches the rule unless a derogation applies**, which is why Art. 17(4)(b) / Bilag 1 nr. 6 exists.
10. **"Below 8 h always needs Arbejdstilsynet's dispensation"** (KEN 9938/2006) is the board's shorthand, not the full rule. Under BEK 606, rest below 8 h (or postponed beyond the limits) is reachable only by:
    - AT dispensation (§ 34, Bilag 1 work);
    - an agreement for wider deviations, approved by the AT director (§ 21);
    - force majeure, documented in writing (§ 5; Act § 52);
    - a single emergency deviation in Bilag 1 work, reported to AT (§ 7).

    Loading and unloading by casual workers has its own regime (§ 9).
11. **Agreements are not limited to Bilag 1 activities.** In BEK 606 only §§ 7, 8 stk. 2, 10, 16 stk. 3, 17 and 34 are limited to Bilag 1 work. Agreements under §§ 19–22 are not. (R2-2.)
12. **Missed: the exemption for staff who set their own hours (BEK 606 § 6).** §§ 3 and 4 do not apply where working time is not measured or fixed in advance, or can be set by the employees themselves, when they take independent decisions or have management functions. Sources: PAV 19.7 restates it; VEJ 9666 § 7 reads it narrowly through the Commission's 2023 Communication ("akademikere" probably means university teachers and researchers); PAV 19.8 says the employer must assess each employment individually ("Det er ikke muligt at undtage hele personalegrupper"). **For StatsTid:** the rest checker needs a per-employee "exempt" state with a recorded assessment. This bears directly on AC staff and managers.
13. **Missed: on-call time never counts toward the weekly fridøgn.** VEJ 9638 § 4: "Den tid, den ansatte er til rådighed, hvad enten det er på arbejdsstedet eller uden for arbejdsstedet, kan ikke indgå i det ugentlige fridøgn". PAV 19.7.2 says the same. On-call at home *is* daily rest (BEK § 16 stk. 1). So the daily and weekly checks must classify the same on-call hour differently.
14. **Missed: the AC route through CIR 9807.** The 1983 central-administration agreement is signed by AC. It covers "alle ansatte, der henhører under arbejdsmiljøloven" in the central administration and in agencies given similar tasks (§§ 2–3). So for its listed deadline tasks (§ 4), AC staff there have a derogation route although CIR1H 9333 has no rest clause. (This rests on Corr. 6's caveat about whether CIR 9807 still works today.)
15. **Pinpoint and quotation fixes.**
    - CIR1H 9594 § 15 stk. 2 is on PDF p. 15 (printed p. 14).
    - KEN 9298's Resume is on PDF p. 2.
    - Directive Art. 17(4)(b) is on PDF p. 5.
    - The Commission quote on consecutive rest omits "(see Chapter IX)" without marking the omission.
    - The KEN 9938 quote joins a Resumé sentence to a reasoning sentence. The Resumé wording is past tense ("indgik … var tale om").
16. **PAV currency.** The page shows "Sidst redigeret 03.12.24" and lags OK26 and BEK 606. PAV kap. 19 does not list "CIS nr. 6240 af 03/05/1984"; that item was not reproduced.
17. **The 1996 bill's "successive 7-day periods" sentence comes from the remark on § 51 stk. 3** (postponing the fridøgn for care and preservation work). It shows how that postponement works, not a general anchor, which weakens it as evidence of fixed weekly blocks. The R4 verdict (the anchor is not defined) is unchanged.
18. **Additional corroboration found.**
    - AT-vejledning 65.1.6, VEJ nr. 9576 af 22/06/2016 (offshore; still Gældende) § 4.3 repeats the same look-back test ("fra et tidspunkt inden for arbejdstiden regner 24 timer tilbage … 11 timers sammenhængende hvile"). That makes three AT texts with one formula.
    - CJEU C‑585/19 (17 Mar 2021): daily rest applies to a worker's several contracts with one employer *taken as a whole*. Relevant if StatsTid ever models concurrent employments.
19. **Identity of the 2021 circular.** CIR1H 9551 is dated and in force 5 July 2021 (effective 1 April 2021), and retsinformation lists CIR1H 9594 as its successor. 022-26's repeal clause dates the 2021 circular "5. juni 2021 (Medst. nr. 014-21)". This is a slip in 022-26; § 12 is identical in both, so nothing turns on it.

---

## 4. Final verified verdicts (R1–R5)

| # | Question | Pass 1 | **Verified verdict** | What it means for StatsTid |
|---|---|---|---|---|
| **R1** | What is "hver periode på 24 timer" measured from? | CONFIRMED with caveat | **CONFIRMED.** Not the calendar day (Commission, OJ C 109/29). Not a fixed start-of-day anchor since **29 Dec 1996** (LOV 1196; L 59 remarks). AT's test, verbatim in VEJ 9666/2024, MED 3503/1997 and VEJ 9576/2016: from a point within working time, count 24 hours back; the window must contain 11 consecutive hours of rest. The "every point" and "clip to the window" elements are pass 1's inferences, but they are **supported by elimination**: every reading without them passes the 24-hour shift that KEN 9938/2006 and the Commission treat as a breach (Corr. 2, 3) | Check each **chain** (work runs whose internal gaps are all < 11 h): its span from first start to last end must be ≤ 13 h. Never group by calendar day or start day. Never reuse the travel rule's "arbejdsdøgn". Compute in elapsed time |
| **R2** | Split working days | PARTLY | **PARTLY**, now sharper. Lawful iff the chain span ≤ 13 h. Gaps under 11 h are rest but never reset the chain, and they are not summed (except home-work derogations, BEK § 33 stk. 4). The brief's example breaches **on day 1 at 22:00**. Split-day work is a listed derogation category (Art. 17(4)(b); BEK Bilag 1 nr. 6). No official worked example exists (NOT FOUND) (Corr. 9, 11) | A split day over 13 h needs a recorded derogation. PAV 19.2.10's "delt tjeneste" (split duty) is pay only |
| **R3** | A continuous over-long shift | CONFIRMED | **CONFIRMED.** The Commission: no work beyond 13 consecutive hours without a derogation. KEN 9938/2006: a 24-hour shift (on-site sleep counted as work) is a "betydelig overskridelse". KEN 9298/2025: a standing 24-hour-shift arrangement needs an agreement between the employers' and employees' organisations, so a dispensation was refused. "13 timer" appears in no Danish rest text; it follows from 24 − 11 (Corr. 1, 10) | A shift over 13 h is a breach in itself. `MaxDailyHours = 13` is right in value but must be measured as the chain span |
| **R4** | Weekly rest day | PARTLY | **PARTLY.** One fridøgn per 7 døgn, next to a daily rest, normally **35 continuous hours**. Per AT, no more than 6 døgn between two fridøgn. Agreements can stretch the gap to 7 døgn locally (§ 23) or 12 døgn centrally (§ 20); the state agreement has "max 10 arbejdsdage between 2 fridage" (§ 12 stk. 3). The daily rest is additional to the fridøgn and comes before it (MÁV-START). **The 7-døgn anchor is defined nowhere** (NOT FOUND); EU law lets the rest fall anywhere in each 7-day period (Maio). **On-call time never counts toward the fridøgn** (Corr. 13, 17) | A continuous-hours check (≥ 35 h unbroken) with a maximum gap between blocks, not a count of calendar days. On-call hours are excluded from the weekly block even when they count as daily rest. The anchor and whether the period rolls are owner/expert questions |
| **R5** | State derogations | CONFIRMED | **CONFIRMED with corrections.** Statens arbejdstidsaftale § 12 (022-26 = CIR1H 9594; identical in CIR1H 9551): the employer may "undtagelsesvis" **cut** to 8 h once per 7 døgn; by local agreement twice, never 2 døgn in a row. Compensatory rest equal to the cut must follow immediately, before the next work period (BEK § 27; VEJ 9666 § 6; at.dk 2024; **Jaeger para. 97**). CIR 9807/1983 (central administration, deadline tasks; cut **or postpone** up to 22 h of work in a row; 10/month, 45/year) is "Valid" in the database. Its EndDate is a record timestamp, but whether it still works under BEK 606 is unverified. The AC agreement has no rest clause; AC staff may reach CIR 9807, and some may be exempt under BEK § 6. Writing duties: BEK §§ 5, 24; emergency deviations reported to AT (Corr. 4–8, 10, 12, 14) | Model a derogation as a structured permission: **mode** (cut / postpone), floor (8 h) or maximum span, frequency window (per 7 døgn; per calendar month and year), the "not 2 døgn in a row" ban, and the compensatory hours (= the reduction) due before the next work. Add a per-employee **exempt (§ 6)** state with its assessment recorded |

**Open questions (for the owner or a qualified expert; the sources are silent):**
- (a) the 7-døgn anchor, and whether it rolls;
- (b) what "døgn" means in "ikke 2 døgn i træk" and in VEJ 9638's "det normale arbejdsdøgn";
- (c) whether CIR 9807 is still a valid derogation under BEK 606, and how its "arbejdsdøgn" maps onto the rolling test;
- (d) whether compensatory hours are added to the next 11-h rest or form part of it;
- (e) how a night shift counts toward § 12 stk. 3 "arbejdsdage";
- (f) what to assume before the first record in the data.

---

## 5. Sources re-fetched (2026-11-13)

| # | Identifier | Route used | Status seen |
|---|---|---|---|
| 1 | LBK nr. 442 af 24/04/2026 | retsinformation.dk/eli/lta/2026/442/pdf + document API + XML | Valid, not historical |
| 2 | BEK nr. 606 af 29/06/2026 | eli/lta/2026/606/pdf + document API + XML | Valid |
| 3 | BEK nr. 324 af 23/05/2002 | document API + XML | Historic (29/06/2026) |
| 4 | VEJ nr. 9666 af 05/09/2024 (AT 5.01.1-1) | api/pdf/244837 + document API + XML | Gældende |
| 5 | VEJ nr. 9667 af 05/09/2024 (AT 5.01.2-1) | api/pdf/244838 + document API | Gældende |
| 6 | VEJ nr. 9638 af 05/09/2024 (AT 5.01.4-2) | api/pdf/244839 + document API | Gældende |
| 7 | MED nr. 3503 af 01/11/1997 | document API + XML | Historisk (09/09/2024) |
| 8 | VEJ nr. 3045 af 01/01/1984 | document API | Historisk |
| 9 | 1996/1 LSF 59; LOV nr. 1196 af 27/12/1996 | document API (eli/ft/199612K00059; eli/lta/1996/1196) | — |
| 10 | KEN nr. 9938 af 04/09/2006 | document API | Gældende |
| 11 | KEN nr. 9298 af 25/02/2025 | api/pdf/249024 | Gældende |
| 12 | CIR1H nr. 9594 af 18/06/2026 (= Medst. 022-26) | api/pdf/256749; MedSt PDF cirkulaere.medst.dk/media/jhshf3w2/022-26.pdf | Gældende |
| 13 | CIR1H nr. 9551 af 05/07/2021 | api/pdf/224517 + XML | Historisk |
| 14 | CIR nr. 9807 af 08/11/1983 | document API (eli/retsinfo/2007/9807) + XML; CIR 9808/1981 XML for comparison | Valid |
| 15 | CIR1H nr. 9333 af 11/04/2025 (AC) | api/pdf/249233 | Gældende |
| 16 | PAV kap. 19 | pav.medst.dk/pav/kapitel-19-arbejdstidsregler-mv/ | "Sidst redigeret 03.12.24" |
| 17 | Directive 2003/88/EC | publications.europa.eu/resource/celex/32003L0088 (PDF) | — |
| 18 | Commission Interpretative Communication, OJ C 109, 24.3.2023 | publications.europa.eu/resource/oj/JOC_2023_109_R_0001 (PDF) | — |
| 19 | CJEU C‑151/02 Jaeger | publications.europa.eu/resource/celex/62002CJ0151 | paras. 94–97 read |
| 20 | CJEU C‑477/21 MÁV-START | …/celex/62021CJ0477 | operative part; paras. 33–43 |
| 21 | CJEU C‑306/16 Maio Marques da Rosa | …/celex/62016CJ0306 | operative part; paras. 44, 49, 51 |
| 22 | CJEU C‑585/19 Academia de Studii Economice (new) | …/celex/62019CJ0585 | operative part |
| 23 | VEJ nr. 9576 af 22/06/2016, AT-vejledning 65.1.6 (offshore) (new) | api/pdf/183126 | Gældende |
| 24 | at.dk news, 02.07.2024 | at.dk/nyheder/2024/07/regler-om-arbejdstid-og-hvileperiode/ | live |

**Not re-checked (context only in pass 1):** SWD(2023) 40 fn. 63; KEN 9944/2016; FV 2019.0117 and FV 2020-348; DI and ASE pages. **Searched again, still NOT FOUND:**
- a CJEU or Danish ruling fixing the start of the 24-hour period;
- a definition of the 7-døgn anchor;
- an official worked split-day example.

---

# Part 2 — The first pass (verbatim; superseded where Part 1 corrects it)

*The first pass keeps its own headings, among them its heading Part 2 (Claims, quotes and citations), an internal division of the first pass and not Part 2 of this file. Its corrections are indexed in the head of this file; the text below is the first pass byte for byte.*

**Date:** 2026-11-12 (every source below was downloaded and read on this date). **Method:** a research pass (`general-purpose`, Opus 5.5), using web search plus direct downloads of the primary texts. The Danish statute, executive orders, Arbejdstilsynet notices, appeal-board decisions, the 1996 bill and the state circulars came from retsinformation.dk: the PDF route `api/pdf/<id>`, or the document API (`POST api/document/eli/...`) for documents with no PDF. The state's guidance (PAV) came from pav.medst.dk. The EU directive, the Commission's 2023 Interpretative Communication and the CJEU judgments came from the EU Publications Office (publications.europa.eu, content-negotiated OJ/CELEX copies), because EUR-Lex and curia returned a bot challenge to automated fetches. Text was extracted with `pdftotext` or an HTML-to-text step. In the quotes below, line breaks are normalised and nothing else is changed. **Status: pass 1, unverified.** An independent verifier must re-download and re-read every decisive source (listed at the end) before anything here is relied on.

---

## Plain-language summary (for the product owner)

**The question.** Danish law says every employee must get "at least 11 consecutive hours of rest within each period of 24 hours". StatsTid needs to know where that 24-hour period starts. If the checker picks the wrong starting point, it misses real breaches (the reviewers' two cases) or flags lawful schedules.

**The answer the sources give.**
- **It is not the calendar day.** The European Commission says so in terms.
- **It is no longer "24 hours from the start of the normal working day".** That *was* the Danish rule before 1997. Parliament removed it deliberately in 1996, because it let an employer push the rest period behind the next shift.
- **What applies since November 1997.** Arbejdstilsynet, the authority that enforces the rule, has given one test, word for word the same in 1997 and in its current notice from 2024. Pick a moment inside working time, look back 24 hours, and that 24-hour stretch must contain 11 unbroken hours of rest. Read naturally, this is a **rolling, backward-looking window**. In practice it means one thing: **no more than 13 hours may pass between the end of the last 11-hour rest and the end of any piece of work.** The Commission states that 13-hour ceiling directly.
- **The two missed cases are breaches.** The split day followed by a night shift is one, and so is the 24-hour continuous shift. A plain 13-hour day followed by 11 hours off is lawful. A Danish appeal board has twice treated 24-hour shifts as a clear breach that needs a special exemption.
- **The state's own agreement lets the employer cut the rest to 8 hours, once in any 7 days** (twice by local agreement, but not on two days in a row). A cut is lawful only inside those limits, and only if the lost rest is given back immediately after the work. Below 8 hours always needs Arbejdstilsynet's permission.

**What this means for StatsTid.**
- Grouping work by the calendar day a shift starts is not the legal test.
- A per-agreement "derogation allowed" yes/no flag is coarser than the law. The real permission has a floor (8 h), a frequency limit, and a duty to give the rest back.

**Two things the brief assumed have changed.**
- The executive order is now **BEK nr. 606 of 29 June 2026** (in force 1 July 2026), not BEK 324/2002.
- The Act is now **LBK nr. 442 of 24 April 2026**.

The rule's wording is unchanged in both.

---

## The verdicts

| # | Question | Verdict (pass 1) | What it means for StatsTid |
|---|---|---|---|
| **R1** | What is "each period of 24 hours" measured from? | **CONFIRMED, with one wording caveat.** **(a) Not the calendar day.** Commission Interpretative Communication 2023, OJ C 109/29: *"the Directive does not define this period as being a calendar day"*. **(b) Not a fixed start-of-day anchor.** The pre-1997 § 50 counted *"hvert døgn, regnet fra normal arbejdstids begyndelse"* (each døgn, counted from the start of normal working hours). The 1996 bill (L 59) removed this on purpose, so that there must be *"11 timers sammenhængende hvil mellem hver arbejdsperiode inden for en 24 timers periode"* (11 hours' consecutive rest between each work period within a 24-hour period), with the rest *preceding* the work. **(c) The enforcing authority's test** (AT-meddelelse 5.01.1, identical in MED 3503/1997 and VEJ 9666/2024): from a point within working time, count 24 hours back, and that window must hold 11 consecutive hours of rest. **Caveat:** the text says *"et tidspunkt"* (a point), not "every point". Reading it as every point, which makes the window rolling, is the natural reading but is mine. The state agreement's *"arbejdsdøgnet … 24 timer regnet fra den pågældende arbejdsdags begyndelse"* (the working døgn: 24 hours from the start of that working day) is expressly limited to the travel-time rule (*"i denne sammenhæng"*, in this context) | Replace "group by start day, measure gap to next day" with a **look-back window** at every point of working time (see "What the algorithm must do"). The travel rule's arbejdsdøgn must **not** be reused for rest |
| **R2** | Is a split working day lawful, and how is it judged? | **PARTLY.** **Lawful in principle.** The same window test applies, and any time that is not working time is rest (Directive Art. 2(2); the state's directive agreement § 2 nr. 2). Only an **unbroken** 11-hour block satisfies the rule, though: short gaps do not add up. The one place the order lets rests be summed is a home-work derogation (BEK 606 § 33 stk. 4), which implies by contrast that they normally are not. Split-day work ("several shorter work periods during the day") is a listed **derogation** category (Directive Art. 17(4)(b); BEK 606 Bilag 1 nr. 6). **No worked split-day example** was found in any Arbejdstilsynet or MedSt (Medarbejder- og Kompetencestyrelsen, the state's employer agency) text (NOT FOUND). The brief's example 08–12 + 17–22, then 08:00, has a longest rest of 10 h, so it fails under every reading (my arithmetic) | A split day passes if, and only if, the look-back window still holds an unbroken 11 h. The gaps inside the day are rest, but they never count toward the 11 h unless one gap alone is ≥ 11 h. PAV 19.2.10's "delt tjeneste" (split duty) is a **pay** rule (a supplement when the span exceeds 11 h), not a rest rule |
| **R3** | Is a continuous over-long shift a breach in itself? Is 13 h implied? | **CONFIRMED.** Commission 2023 (OJ C 109/29): *"where there is no derogation, the consecutive working time is limited to 13 hours … workers cannot carry out work for more than 13 consecutive hours"*. The Danish appeal board treated 24-hour on-site shifts as *"en betydelig overskridelse af hvileperioden"* (a significant exceedance of the rest period), requiring an Arbejdstilsynet dispensation (KEN 9938/2006) or an agreement between the organisations (KEN 9298/2025). The words "13 hours" do **not** appear in the Danish statute, executive order or AT notices (NOT FOUND in Danish primary text). There it follows by arithmetic, 24 − 11 | A shift (or chain of work with no ≥ 11 h gap) longer than 13 h is a breach in itself, without needing a "next day". StatsTid's `MaxDailyHours = 13` is right in value, but must be measured as **time since the last ≥ 11 h rest ended**, not per calendar day |
| **R4** | The weekly rest day (*fridøgn*) | **PARTLY.** **What is CONFIRMED:** one fridøgn in each 7-døgn period, adjoining a daily rest, so normally **35 consecutive hours** (AT 5.01.2; EU Art. 5; MÁV-START C‑477/21: the daily rest is *additional* to the 24 h and must *precede* it). Arbejdstilsynet reads it as *"ikke må være mere end 6 døgn mellem 2 fridøgn"* (no more than 6 døgn between two rest days). **The anchor is not fixed in any statute.** The 1996 bill describes successive fixed 7-day periods whose start is not moved by a postponed rest day. The CJEU allows the rest anywhere within each seven-day period (Maio Marques da Rosa C‑306/16), so Denmark's reading is stricter. **Calendar days:** no source counts calendar days for the *legal* fridøgn, because it is measured in continuous hours. The state agreement's own rules (§ 12 stk. 3: max 10 *working days* between 2 days off; § 15: a "fridag" of ≥ 40 h, ≥ 36 h if it contains a whole calendar day) do use day counts. How a night shift counts toward "arbejdsdage" there: NOT FOUND | The weekly check is a **continuous-hours** check (≥ 35 h unbroken rest, with no more than 6 døgn between such blocks per AT, or the agreed longer interval). It is not a "count of worked calendar days" check. A shift crossing midnight simply breaks any rest block it overlaps |
| **R5** | Derogations for the state; breach or permitted deviation; who agrees; how recorded | **CONFIRMED.** **Statens arbejdstidsaftale § 12** (Medst. 022-26 = CIR1H 9594/2026; word-for-word the same in OK21's CIR1H 9551): the employer may *"undtagelsesvis"* (exceptionally) cut the rest from 11 to 8 h **once per 7 døgn**, or by local agreement up to **twice per 7 døgn, not two døgn in a row**, with equivalent compensatory rest *"i umiddelbar forlængelse af arbejdet"* (immediately after the work; PAV 19.2.5.2). **Central administration** (CIR 9807/1983, still flagged in force): for listed deadline tasks only, cut to 8 h or postpone, max 10/month and 45/year. **Rule of thumb:** a cut inside a valid derogation is a **permitted deviation**; outside one it is a breach (BEK 606 § 36). Below 8 h always needs Arbejdstilsynet's dispensation. **Recording:** derogation agreements must be in writing (§ 24); local ones documented in writing (§ 24 stk. 2); force majeure documented in writing (§ 5); emergency deviations reported in writing to Arbejdstilsynet; working time must be registered (PAV 19.8 on the Working Time Act) | A boolean "derogation allowed" flag is too coarse (my inference). A faithful model needs: the **floor** (8 h); the **frequency limit** (per rolling 7 døgn for § 12; per month/year for CIR 9807 and on-call); the **"not two døgn in a row"** rule; and a **compensatory-rest check** immediately after. The AC agreement (CIR1H 9333) contains **no** rest clause (NOT FOUND), so § 12's reduction is not visibly available to AC *contract* staff; AC civil servants fall under the state working-time agreement |

### Premise corrections and currency notes
1. **The executive order is BEK nr. 606 af 29/06/2026** (*Bekendtgørelse om hvileperiode og fridøgn m.v.*), in force 1 July 2026 (§ 37). It repeals BEK nr. 324 af 23/05/2002 (§ 37 stk. 2). Its § 3 repeats the Act's wording.
2. **The Act is LBK nr. 442 af 24/04/2026.** Chapter 9, § 50 is unchanged.
3. **Some sources lag those changes.** Arbejdstilsynet's notices (VEJ 9666, 9667, 9638, all of 05/09/2024, still flagged *Gældende*, i.e. in force) and PAV kap. 19 still cite BEK 324/2002 and LBK 2062/2021. The substance relevant here has not changed.
4. **"Arbejdsdøgn" survives in three texts, and none of them defines the anchor of the statutory rest rule today.**
   - The state agreement's travel rule defines it, but only "in this context".
   - CIR 9807/1983 uses it, but was written under the pre-1997 statute, when the døgn *was* counted from the start of normal working hours.
   - AT's on-call notice (VEJ 9638) uses "det normale arbejdsdøgn" for on-call reductions.

---

# Part 2 — Claims, quotes and citations

Citation format: document identifier · issuing body · URL · pinpoint (section/§, PDF page) · read 2026-11-12. "PDF p." is the page index of the downloaded file. For retsinformation's VEJ/KEN/CIR1H printouts, page 1 is a print cover.

## R1 — The anchor of "hver periode på 24 timer"

### R1-1. The statute and the executive order give no anchor
**Arbejdsmiljøloven § 50 stk. 1** (LBK nr. 442 af 24/04/2026, *Bekendtgørelse af lov om arbejdsmiljø*, https://www.retsinformation.dk/eli/lta/2026/442/pdf, Kapitel 9, PDF p. 10):
> *"§ 50. Arbejdstiden skal tilrettelægges således, at de ansatte får en hvileperiode på mindst 11 sammenhængende timer inden for hver periode på 24 timer."*
> → "Working time shall be organised so that employees get a rest period of at least 11 consecutive hours within each period of 24 hours."

**BEK nr. 606 af 29/06/2026 § 3** (Arbejdstilsynet, https://www.retsinformation.dk/eli/lta/2026/606/pdf, PDF p. 1) is word-for-word the same:
> *"§ 3. Arbejdstiden skal tilrettelægges således, at de ansatte får en hvileperiode på mindst 11 sammenhængende timer inden for hver periode på 24 timer."*

Its footnote 1 says the order implements Directive 93/104/EC. **Neither text says where the 24-hour period starts.**

### R1-2. Before 1997 the døgn was anchored at the start of normal working hours
**The old § 50 stk. 1** (as printed in the bill's annex of current wording, 1996/1 LSF 59, *Forslag til lov om ændring af lov om arbejdsmiljø (Hvileperiode og fridøgn)*, tabled 6 Nov 1996 by the Minister of Labour; https://www.retsinformation.dk/eli/ft/199612K00059, "Bilag"):
> *"§ 50. Arbejdstiden skal tilrettelægges således, at der i hvert døgn, regnet fra normal arbejdstids begyndelse, gives den ansatte en sammenhængende hvileperiode på mindst 11 timer."*
> → "Working time shall be organised so that in each døgn, counted from the start of normal working hours, the employee is given a consecutive rest period of at least 11 hours."

**Arbejdstilsynet's 1984 guidance** under that wording (VEJ nr. 3045 af 01/01/1984, *Vejledning om Daglig hvileperiode*, status *Historisk*, https://www.retsinformation.dk/eli/retsinfo/1984/3045, section "11 timers hvileperiode"):
> *"Hovedreglen er, at den ansatte inden for hvert døgn, regnet fra normal arbejdstids begyndelse, skal have en samlet hviletid på mindst 11 timer. Ved normal arbejdstids begyndelse forstås det tidspunkt, der er aftalt mellem arbejdsgiver og arbejdstager eller fastsat ved kollektiv overenskomst. Hvis der for den enkelte arbejdstager ikke gælder nogen normal arbejdstid, fordi arbejdet hver dag begynder på skiftende tidspunkter, regnes døgnet fra arbejdstidens faktiske begyndelse."*
> → "The main rule is that within each døgn, counted from the start of normal working hours, the employee must have a total rest of at least 11 hours. The start of normal working hours means the time agreed between employer and employee or fixed by collective agreement. If no normal working time applies, because work starts at varying times each day, the døgn is counted from the actual start of work."

### R1-3. The 1996 amendment deliberately removed the start-of-day anchor
**Specific remarks to § 50** (1996/1 LSF 59, "Bemærkninger til lovforslagets enkelte bestemmelser – Til § 1, Til nr. 1, Til § 50", same URL):
> *"Den gældende § 50, stk. 1, er formuleret på en anden måde end direktivets artikel 3 og kan formentlig også fortolkes på en måde, som ikke vil være i overensstemmelse med direktivet, idet § 50, stk. 1, regner døgnet fra normal arbejdstids begyndelse. I lovforslaget er ordet »normal« ikke medtaget. Dette indebærer, at der skal være 11 timers sammenhængende hvil mellem hver arbejdsperiode inden for en 24 timers periode.*
> *Som eksempel herpå kan nævnes en arbejdsgiver, som i en periode, hvor arbejdstidens begyndelse ligger fast, undtagelsesvist lader arbejdet påbegynde senere end normalt, hvilket kan have den konsekvens, at der ikke opnås 11 timers sammenhængende hvil mellem pågældende og næste arbejdsperiode men først efter den følgende arbejdsperiode. Dette vil næppe være i overensstemmelse med direktivet, idet det på grundlag af EF-retspraksis vedrørende køre- og hviletidsområdet må forventes, at det kræves, at hvileperioden ligger forud for arbejdsperioden.*
> *For at undgå fortolkningstvivl er den foreslåede bestemmelse formuleret på samme måde som direktivets artikel 3. Ændringen forventes ikke at medføre særlige ændringer i forhold til gældende praksis på arbejdsmarkedet."*
> → "The current § 50(1) is worded differently from Article 3 of the directive and can presumably be read in a way that would not comply with it, since § 50(1) counts the døgn from the start of normal working hours. The bill does not include the word 'normal'. This means there must be 11 hours' consecutive rest between each work period within a 24-hour period.
> An example: an employer who, in a period with a fixed start of work, exceptionally lets work start later than normal. The result can be that 11 hours' consecutive rest is not obtained between that work period and the next, but only after the following one. That would hardly comply with the directive, since EC case law on driving and rest times leads one to expect that the rest period must lie *before* the work period.
> To avoid doubts of interpretation, the provision is worded like Article 3 of the directive. The change is not expected to alter practice on the labour market noticeably."

**The minister's written tabling speech** (same document, "Skriftlig fremsættelse"):
> *"Som en nødvendig tilpasning af arbejdsmiljøloven er ordet »normal« ikke medtaget i lovforslagets § 50 om daglig hvileperiode. Efter gældende regler regnes døgnet fra normal arbejdstids begyndelse, men efter forslaget skal der være 11 timers sammenhængende hvil mellem hver arbejdsperiode indenfor en 24 timers periode."*
> → "As a necessary adjustment, the word 'normal' is not included in the bill's § 50. Under current rules the døgn is counted from the start of normal working hours. Under the bill, there must be 11 hours' consecutive rest between each work period within a 24-hour period."

**Enacted unchanged:** LOV nr. 1196 af 27/12/1996, § 1 nr. 1 (https://www.retsinformation.dk/eli/lta/1996/1196), new § 50: *"…inden for hver periode på 24 timer."*

*Note for the verifier:* the remark says the word "normal" was dropped. In fact the enacted text drops the whole phrase *"i hvert døgn, regnet fra normal arbejdstids begyndelse"* and adopts the directive's wording. Both the remark and the minister's speech state the intended effect: rest **between each work period**, lying **before** the work period.

### R1-4. Arbejdstilsynet's test: count 24 hours back from a point within working time (1997 to now)
**Current notice:** VEJ nr. 9666 af 05/09/2024, *Daglig hvileperiode*, AT-meddelelse 5.01.1-1, "November 1997 – Opdateret september 2024", status *Gældende* (https://www.retsinformation.dk/api/pdf/244837, section 2 "11 timers hvileperiode", PDF p. 2):
> *"Arbejdstiden skal tilrettelægges således, at de ansatte får en hvileperiode på mindst 11 sammenhængende timer inden for hver periode på 24 timer. Dette betyder, at fra det tidspunkt, hvorfra arbejdstiden beregnes, skal der gives 11 sammenhængende timers hvil, når man regner 24 timer tilbage. Det kan beregnes, om forholdene er lovlige, hvis man tegner en tidslinje og fra et tidspunkt inden for arbejdstiden regner 24 timer tilbage, idet der inden for denne periode skal være 11 timers sammenhængende hvil."*
> → "…This means that from the point in time from which working time is reckoned, 11 consecutive hours of rest must have been given when one counts 24 hours back. Whether the arrangement is lawful can be calculated by drawing a timeline and, from a point in time within working time, counting 24 hours back. Within that period there must be 11 hours of consecutive rest."

**The 1997 original, word-for-word the same:** MED nr. 3503 af 01/11/1997, *At-meddelelse nr. 5.01.1, november 1997, erstatter januar 1984 Daglig hvileperiode*, status *Historisk* (https://www.retsinformation.dk/eli/retsinfo/1997/3503, section "11 timers hvileperiode"). It contains the same three sentences.

**Applied by the appeal board:** KEN nr. 9938 af 04/09/2006 (Arbejdsmiljøklagenævnet, *Dispensation fra hviletidsbestemmelserne*, https://www.retsinformation.dk/eli/retsinfo/2006/9938) lists *"AT-meddelelse nr. 5.01.1/November 1997 om daglig hvileperiode"* among the rules applied and reproduces these sentences.

**How I read it, and the caveat.**
- **The test is backward-looking.** The window ends at a moment inside working time and reaches 24 hours back. That matches the bill's "the rest must lie *before* the work period".
- **The text says "et tidspunkt"** (a point), not "ethvert tidspunkt" (every point). The first sentence, *"fra det tidspunkt, hvorfra arbejdstiden beregnes"*, could be read as the start of work. The second sentence, *"fra et tidspunkt inden for arbejdstiden"*, speaks of any point inside working time.
- **Treating every point as a test point gives a rolling window.** That is the natural reading of the second sentence, but it is my inference, not a quotation.
- **"Inden for denne periode" (within that period)** most literally means that only rest lying *inside* the 24-hour window counts. This too is my inference.

### R1-5. The Commission: not the calendar day; a framework of 24-hour periods
**Commission Interpretative Communication on Directive 2003/88/EC** (OJ C 109, 24.3.2023, p. 1; Chapter V "Minimum rest periods", section A "Daily rest", OJ page C 109/29; retrieved from https://publications.europa.eu/resource/oj/JOC_2023_109_R_0001, Accept: application/pdf):
> *"Workers must receive one period of daily rest for each 24-hour period. However, the Directive does not define this period as being a calendar day. Such an approach would de facto amount to imposing a work schedule which would not fit with the usual periods of work (182)."*
> *"However, by establishing a framework of 24-hour periods, the Directive imposes a certain regularity in daily rest periods. In this respect, the Court has ruled that 'in order to ensure the effective protection of the safety and health of the worker provision must as a general rule be made for a period of work regularly to alternate with a rest period' (183). The purpose of daily rest is to allow workers to remove themselves from their working environment for a number of hours. These must be consecutive and 'directly follow a period of work in order to enable him to relax and dispel the fatigue caused by the performance of his duties' (184)."*
>
> Footnote (182): *"This would impose for workers to work between 00.00 and 13.00 and to benefit from their rest period between 13.00 and 24.00 or on the contrary resting from 00.00 until 11.00 and working from 11.00 until 24.00."* Footnotes (183), (184): *"Judgment in case C-151/02, Jaeger, para. 95."*

Arbejdstilsynet's notices say expressly that this Communication (*"EU-Kommissionens fortolkningsmeddelelse om arbejdstidsdirektivet fra 2023"*) is applied when interpreting compensatory rest, the exempted groups, and on-call (VEJ 9666 sections 6–7; VEJ 9638 section 3).

**The directive** (Directive 2003/88/EC, OJ L 299, 18.11.2003, p. 9; https://publications.europa.eu/resource/celex/32003L0088, PDF p. 3):
> Art. 3: *"Member States shall take the measures necessary to ensure that every worker is entitled to a minimum daily rest period of 11 consecutive hours per 24-hour period."*
> Art. 2(1)–(2): *"'working time' means any period during which the worker is working, at the employer's disposal and carrying out his activity or duties, in accordance with national laws and/or practice; 2. 'rest period' means any period which is not working time;"*

**CJEU, Jaeger** (Case C‑151/02, judgment of 9 September 2003, EU:C:2003:437, para. 95; https://publications.europa.eu/resource/celex/62002CJ0151):
> *"In order to ensure the effective protection of the safety and health of the worker provision must as a general rule be made for a period of work regularly to alternate with a rest period. In order to be able to rest effectively, the worker must be able to remove himself from his working environment for a specific number of hours which must not only be consecutive but must also directly follow a period of work in order to enable him to relax and dispel the fatigue caused by the performance of his duties."*

**No CJEU judgment found that fixes the start of the 24-hour period** (NOT FOUND).
- **Where I looked:** the 2023 Communication's daily-rest section, whose only case citation is Jaeger; the Commission's 2023 implementation report and staff working document (COM(2023) 72; SWD(2023) 40, via Council docs ST 7514/23 and ST 7514/23 ADD 1); web searches for CJEU rulings on reckoning the 24-hour period.
- **What the staff working document does say:** footnote 63 lists Denmark among the states requiring "a minimum of 11 consecutive hours" and notes "Germany (after the end of the working day)". It says nothing about Denmark's anchor.

### R1-6. Other Danish uses of "døgn" (quoted so the verifier sees every candidate anchor)
- **The state working-time agreement's travel rule (travel only).** Cirkulære om statens arbejdstidsaftale, CIR1H nr. 9594 af 18/06/2026 (= Medst. 022-26, Finansministeriet/MedSt j.nr. 2026-494), § 7 nr. 3, remark (https://www.retsinformation.dk/api/pdf/256749, PDF p. 9):
  > *"Rejsetiden medregnes fuldt ud for rejser i såvel ind- som udland, men højst med 13 timer pr. døgn. … Ved døgn forstås i denne sammenhæng arbejdsdøgnet, dvs. 24 timer regnet fra den pågældende arbejdsdags begyndelse."*
  > → "…By døgn is meant **in this context** the working døgn, i.e. 24 hours counted from the start of the working day concerned."

  The scope words "i denne sammenhæng" restrict the definition to § 7 nr. 3. Our own dossier (`docs/references/sick-day-credit-and-agreement-texts-research.md:646`) already flagged that the rest rule needs its own definition. This pass confirms that.
- **AT's on-call notice** (VEJ nr. 9638 af 05/09/2024, AT-meddelelse 5.01.4-2, https://www.retsinformation.dk/api/pdf/244839, section 3, PDF p. 3):
  > *"Når den ansatte kaldes til arbejde under rådighedstjeneste uden for arbejdsstedet, kan hvileperioden i det pågældende døgn enten nedsættes til 8 timer eller udskydes, således at der først gives de ansatte 11 timers hvil efter afslutningen af det sidste arbejde. En nedsættelse af hvileperioden kan kun ske, hvis de 8 timers hvileperiode kan ligge i sammenhæng inden for det normale arbejdsdøgn. Hvis hvileperioden i stedet udskydes, og de 11 timers hvil herved strækker sig ind i det efterfølgende døgn, skal den ansatte inden for dette døgn tillige have den sædvanlige hvileperiode."*
  > → "When an employee on call outside the workplace is called in, the rest in that døgn may be cut to 8 hours or postponed, so that 11 hours' rest is given only after the last work ends. A cut is allowed only if the 8 hours can lie together within the **normal working døgn**. If the rest is postponed and runs into the next døgn, the employee must also have the usual rest within that døgn."

  This uses a day-based notion ("det normale arbejdsdøgn") for the on-call exception only. **The sources disagree in vocabulary.** The general test (R1-4) is the look-back window, while this on-call passage carries the older day notion. The passage does not define "det normale arbejdsdøgn".
- **The rule after travel abroad** (BEK 606 § 14 stk. 1, PDF p. 2):
  > *"Ved hjemkomsten fra en tjenesterejse til udlandet eller Færøerne og Grønland skal der ydes de ansatte 11 timers sammenhængende hvil regnet fra hjemkomsten, medmindre de ansatte har haft en tilsvarende hvileperiode i det sidste døgn forud for genoptagelsen af sit sædvanlige arbejde."*
  > → "…unless the employees have had an equivalent rest period **in the last døgn before resuming** their usual work."

  This, too, looks back from the start of work.
- **CIR 9807/1983** uses "arbejdsdøgn" (see R5-3). It predates the 1997 change.

---

## R2 — Split working days

**R2-1. Rest is all non-working time, but only an unbroken 11-hour block counts.**
- **Directive Art. 2(2):** *"'rest period' means any period which is not working time"* (above).
- **The state's directive-implementation agreement,** annexed as bilag 2 to CIR1H 9594/2026 (PDF pp. 18–19), § 2 nr. 2: *"'hviletid' som det tidsrum, der ikke er arbejdstid"* ("rest" = the time that is not working time). Its § 3: *"Den daglige og ugentlige hviletid fremgår af arbejdsmiljølovgivningen samt af de aftaler om hviletid og fridøgn, der indgås i tilknytning hertil."* (Daily and weekly rest follow the Working Environment Act and the rest agreements made under it.)
- **The statutory requirement is "11 *sammenhængende* timer"** (11 consecutive hours; § 50, R1-1). The Commission adds (OJ C 109/29): *"The 11 hours of daily rest must be consecutive. This means that workers should not be interrupted in their rest period unless the Member State has provided otherwise under the permitted derogations."*
- **Summing short rests is allowed in one place only:** a home-work derogation. BEK 606 § 33 stk. 4 (PDF p. 3):
  > *"Hvis summen af de daglige hvileperioder minimum er 11 timer inden for en 24 timers periode ved fravigelse efter § 28, stk. 1, nr. 1 og § 30, skal der ikke ydes tilsvarende kompenserende hvileperioder eller tilsvarende beskyttelse efter stk. 1 og stk. 3."*
  > → "If, under a derogation under § 28(1) no. 1 or § 30, the **sum** of the daily rest periods is at least 11 hours within a 24-hour period, no compensatory rest or protection is required."

  My inference, by contrast: outside these home-work derogations, summing is not allowed.

**R2-2. Split-day work is a listed derogation category.**
- **Directive Art. 17(4)(b)** (PDF p. 6): *"In accordance with paragraph 2 of this Article, derogations may be made from Articles 3 and 5: … (b) in the case of activities involving periods of work split up over the day, particularly those of cleaning staff."*
- **The Commission** (OJ C 109/58): *"This derogation has not been the subject of case-law in the Court. It is limited to derogations from the provisions on daily and weekly rest. … their use is subject to the workers being granted equivalent compensatory rest or, in exceptional cases in which this is not possible for objective reasons, appropriate protection."*
- **BEK 606, Bilag 1 nr. 6** (the activities where §§ 7 and 34 allow deviation or dispensation; PDF p. 5):
  > *"Aktiviteter, der er kendetegnet ved flere kortere arbejdsperioder i løbet af dagen, f.eks. rengøringsarbejde."*
  > → "Activities characterised by several shorter work periods during the day, e.g. cleaning."

  These activities may deviate only by Arbejdstilsynet's dispensation (§ 34), a single emergency deviation (§ 7), or an agreement (§§ 19–22). The listing implies that a split day which cannot give 11 unbroken hours **needs** a derogation (my inference).

**R2-3. The state treats split duty as a pay matter, not a rest rule.** PAV 19.2.10 "Delt tjeneste" (https://pav.medst.dk/pav/kapitel-19-arbejdstidsregler-mv/):
> *"Ved delt tjeneste forstås tjeneste, der på grund af pauser, der ikke medregnes i arbejdstiden, er opdelt i flere dele. For delt tjeneste af en samlet varighed på mere end 11 timer ydes et tillæg pr. time efter § 3, stk. 2, i natpengeaftalen. Ved beregning af den delte tjenestes samlede varighed medregnes de mellemliggende timer, hvor den ansatte har fri."*
> → "Split duty is duty divided into parts by unpaid breaks. For split duty with a total span over 11 hours a per-hour supplement is paid… The intervening free hours count toward the span."

This is a pay rule. It neither permits nor forbids anything under the rest rule.

**R2-4. Worked example: NOT FOUND.**
- **Where I looked:** VEJ 9666/2024; MED 3503/1997; VEJ 3045/1984; VEJ 9638; VEJ 9667; PAV kap. 19; CIR1H 9594/2026; KEN 9944/2016, 9938/2006, 9298/2025; at.dk (its rule pages return 404 and are not in the sitemap; only the July 2024 news item is reachable); the Commission's 2023 Communication; secondary pages (DI, ASE).
- **What was there:** none has a clock-time example of a split day. ASE (secondary, a union-type advice page, published 18 Oct 2026) gives only a non-split example: finish 22:00 Tuesday → earliest start 09:00 Wednesday.

**The brief's example, by arithmetic (my calculation, not a source):** 08:00–12:00 + 17:00–22:00, then 08:00 the next day. The rest blocks are 12:00–17:00 (5 h) and 22:00–08:00 (10 h).
- **Look-back window at 08:00 next day** (08:00–08:00): longest unbroken rest 10 h, so it fails.
- **Any other reading:** it fails too, because no ≥ 11 h block exists anywhere between the two days.
- **Variant:** with the evening block ending at 21:00 instead, the 21:00–08:00 block is 11 h. That passes.

---

## R3 — A continuous over-long shift

**R3-1. The Commission: no more than 13 consecutive hours without a derogation.** OJ C 109/29:
> *"This means that where there is no derogation, the consecutive working time is limited to 13 hours (from which the break presented below must be deducted) and must be followed by at least 11 consecutive hours of rest. Under the 'normal' provisions of the Directive, workers cannot carry out work for more than 13 consecutive hours as this would be contrary to the purpose of this provision. Such regimes are therefore only permitted under the conditions detailed in Chapter IX."*

**R3-2. Danish appeal board, 24-hour on-site shifts.**
- **KEN 9938/2006** (as above, "Resumé" and reasoning):
  > *"Ved afgørelsen lagde nævnet vægt på, at hele 24 timers vagten måtte betragtes som arbejdstid, da de ansatte stod til rådighed i tidsrummet fra kl. 23.00 til 7.00."* … *"I denne vurdering er indgået, at der er tale om en betydelig overskridelse af hvileperioden, ligesom der er tale om en permanent ordning."*
  > → "The board stressed that the whole 24-hour shift had to be treated as working time, since staff were on call from 23:00 to 07:00. … The assessment included that this was a **significant exceedance of the rest period** and a permanent arrangement."

  In the same decision's full text:
  > *"Ifølge arbejdsmiljøloven er udgangspunktet at de ansatte skal have 11 timers hvile inden for 24 timer. Under forskellige betingelser kan man i begrænset omfang gå ned til 8 timers hvile. Hvis man ønsker at gå under 8 timer, kræver det altid en dispensation fra Arbejdstilsynet."*
  > → "…Going below 8 hours always requires a dispensation from Arbejdstilsynet."
- **KEN nr. 9298 af 25/02/2025** (Arbejdsmiljøklagenævnet, https://www.retsinformation.dk/api/pdf/249024, "Resume", PDF p. 1):
  > *"Arbejdsmiljøklagenævnet vurderede, at der ikke var grundlag for at give en generel dispensation fra reglen om, at ansatte skal have en hvileperiode på mindst 11 sammenhængende timer inden for hver periode på 24 timer. Nævnet vurderede, at der var tale om en generel fravigelse fra hviletidsreglerne, som forudsætter en aftale mellem arbejdsgivers og arbejdstagers organisationer."*
  > → "No basis for a general dispensation so that 24-hour shifts could be worked; a general deviation presupposes an agreement between the employers' and employees' organisations."

**R3-3. "13 hours" in Danish primary texts: NOT FOUND.**
- **Where I looked:** the statute, BEK 606, AT notices 5.01.1/2/4-2, PAV kap. 19, CIR1H 9594.
- **The only "13 timer" found** is the travel-time cap (§ 7 nr. 3: travel counts at most 13 h per arbejdsdøgn). That is a different rule and should not be conflated.

**R3-4. A 16-hour shift (my arithmetic).** Take 06:00–22:00 after normal rest. The look-back window at 22:00 (22:00–22:00) holds at most 8 h of rest (22:00–06:00), so it fails at 11 h. With a valid reduction to 8 h (R5) it passes once, provided equivalent compensatory rest follows immediately.

---

## R4 — The weekly rest day (fridøgn)

**R4-1. The statute and the order.** Arbejdsmiljøloven § 51 stk. 1 (LBK 442/2026, PDF p. 10), the same as BEK 606 § 4 stk. 1:
> *"§ 51. Inden for hver periode på 7 døgn skal de ansatte have et ugentligt fridøgn, der skal ligge i umiddelbar tilslutning til en daglig hvileperiode. Det ugentlige fridøgn skal så vidt muligt falde på søndage og så vidt muligt samtidig for alle, der er ansat i virksomheden."*
> → "Within each period of 7 døgn the employees must have a weekly rest day, which must adjoin a daily rest period. It should, as far as possible, fall on Sundays…"

**R4-2. Arbejdstilsynet: 35 hours, no more than 6 døgn between.** VEJ nr. 9667 af 05/09/2024, AT-meddelelse 5.01.2-1, section 2 (https://www.retsinformation.dk/api/pdf/244838, PDF p. 2):
> *"Inden for hver periode på 7 døgn skal den ansatte have et fridøgn. Bestemmelsen skal forstås således, at der ikke må være mere end 6 døgn mellem 2 fridøgn. Fridøgnet skal ligge i umiddelbar tilslutning til en daglig hvileperiode. Da den daglige hvileperiode er på 11 timer, vil den ansatte derfor normalt have krav på en sammenhængende hvileperiode på 35 timer hvert 7. døgn."*
> → "…The provision is to be understood so that there may be **no more than 6 døgn between 2 rest days**. … Since the daily rest is 11 hours, the employee is therefore normally entitled to a **continuous rest of 35 hours every 7th døgn**."

PAV 19.7.2 repeats: *"inden for en periode på 7 døgn skal være et ugentligt fridøgn (dvs. højst 6 døgn mellem 2 fridøgn)"*.

**R4-3. The 1996 bill: successive 7-day periods.** 1996/1 LSF 59, remarks "Til § 51":
> *"En udskydelse af fridøgnet for en enkelt 7-dages periode medfører ikke, at begyndelsen af den følgende 7-dages periode kan udskydes, idet der for hver periode af 7 dage skal ydes et fridøgn, jf. stk. 1. Arbejdsmarkedets parter kan aftale, at perioden mellem 2 hvileperioder udstrækkes i op til 12 dage."*
> → "Postponing the rest day in one 7-day period does not move the start of the following 7-day period, since a rest day must be given for each 7-day period. The social partners may agree to extend the period between 2 rest periods to up to 12 days."

**The sources disagree on the anchor.**
- **The 1996 bill** implies fixed, successive 7-day periods, but does not say where the first one starts.
- **Arbejdstilsynet** phrases it as a maximum gap: no more than 6 døgn between two fridøgn.
- **No statute or order defines the start of the 7-døgn period** (NOT FOUND).

**R4-4. EU law: 24 + 11 = 35 hours; the daily rest is additional and comes first; timing is flexible.**
- **Directive Art. 5** (PDF p. 3): *"Member States shall take the measures necessary to ensure that, per each seven-day period, every worker is entitled to a minimum uninterrupted rest period of 24 hours plus the 11 hours' daily rest referred to in Article 3. If objective, technical or work organisation conditions so justify, a minimum rest period of 24 hours may be applied."* Art. 16(a) allows *"a reference period not exceeding 14 days"*.
- **The Commission** (OJ C 109/31): *"…a rest period of 24 hours plus the 11 hours of daily rest, meaning that this period of 11 hours of daily rest cannot be subtracted from the weekly rest period. This amounts to a continuous rest period of 35 hours."*
- **CJEU, MÁV-START** (Case C‑477/21, judgment of 2 March 2023; https://publications.europa.eu/resource/celex/62021CJ0477, Accept: application/xhtml+xml), operative part:
  > *"1. Article 5 of Directive 2003/88/EC … must be interpreted as meaning that the daily rest period provided for in Article 3 of that directive does not form part of the weekly rest period referred to in Article 5 of that directive, but is additional to it. 2. Articles 3 and 5 of Directive 2003/88 … must be interpreted as meaning that where national legislation provides for a weekly rest period exceeding 35 consecutive hours, the worker must be granted, in addition to that period, the daily rest as guaranteed by Article 3 of that directive. 3. Article 3 of Directive 2003/88 … must be interpreted as meaning that where a worker is granted a weekly rest period, he or she is also entitled to a daily rest period preceding that weekly rest period."*
- **CJEU, Maio Marques da Rosa** (Case C‑306/16, judgment of 9 November 2017, EU:C:2017:844; https://publications.europa.eu/resource/celex/62016CJ0306, Accept: application/xhtml+xml), operative part:
  > *"…must be interpreted as not requiring the minimum uninterrupted weekly rest period of 24 hours to which a worker is entitled to be provided no later than the day following a period of six consecutive working days, but requires that rest period to be provided within each seven-day period."*

  Para. 44: *"…may be provided at any time within each seven-day period…"*. Para. 49: Member States may provide more protection (Art. 15). Denmark's "no more than 6 døgn between" reading (AT) is such a stricter national rule. That last point is my inference.

**R4-5. The state agreement's own day-off rules (agreement level, not the statute).** CIR1H 9594/2026:
- **§ 12 stk. 3** (PDF p. 13): *"Der kan højst være 10 arbejdsdage mellem 2 fridage."* (At most 10 working days between 2 days off.) PAV 19.7.2 treats this as the agreement-level rearrangement of the fridøgn: *"I arbejdstidsaftalen er det således fastsat, at der kan være op til 10 arbejdsdage mellem 2 fridage, jf. aftalens § 12, stk. 3."* BEK 606 § 20 caps agreed rearrangements at 12 døgn between two fridøgn.
- **§ 15 stk. 2** (staff whose working time lies wholly or partly outside normal day hours, kapitel 4; PDF p. 14): *"En fridag skal være på mindst 40 timer. Hvis der indgår et helt kalenderdøgn, anses fridagen dog for givet, hvis den er på mindst 36 timer."* (A day off must be ≥ 40 h, or ≥ 36 h if it contains a whole calendar day.)

**Does a shift chain across several calendar days count as several worked days?**
- **For the statutory fridøgn:** no source counts calendar days. The test is a continuous rest block (24 h + 11 h). A night shift crossing midnight interrupts any block it touches (my inference from R4-1/R4-2).
- **For § 12 stk. 3's "arbejdsdage":** how a night shift is counted is NOT FOUND.

---

## R5 — Derogations: when, who, breach or deviation, how recorded

**R5-1. The statute and order: the categories.**
- **§ 50 stk. 2** (LBK 442/2026): cut to 8 h at a shift change in shift-work undertakings, and in agriculture for up to 30 days a year.
- **BEK 606 § 8** (PDF p. 1): cut to 8 h for annual stock-taking or sales (max 14 times a year), the last 14 days before Christmas Eve in shops and warehouses, and, within Bilag 1 activities, *"mødevirksomhed, kulturelle arrangementer og lignende"* in the evening (max 20 times a year).
- **§ 5:** force majeure (*"Fravigelsen skal dokumenteres skriftligt"*, the deviation must be documented in writing).
- **§ 7:** a single deviation without dispensation, Bilag 1 activities only.
- **§ 16 stk. 3:** on-call call-outs; cut to 8 h or postpone, *"højst ske i 10 døgn i hver kalendermåned og højst i 45 døgn i et kalenderår"* (at most 10 døgn a calendar month and 45 døgn a calendar year).
- **§ 34:** Arbejdstilsynet's dispensation, Bilag 1 activities only.

**Agreements** (BEK 606 kapitel 5, PDF pp. 2–3):
> § 19: *"Vedkommende arbejdsgiverpart og lønmodtagerorganisation kan aftale, at den daglige hvileperiode i et begrænset omfang kan udskydes eller nedsættes til 8 timer."*
> → "The relevant employer party and employee organisation may agree that the daily rest may, to a limited extent, be postponed or reduced to 8 hours."
>
> § 21: *"Vedkommende arbejdsgiverpart og lønmodtagerorganisation kan aftale, at reglerne om hvileperiode og fridøgn fraviges i videre omfang end nævnt i §§ 19 og 20. Sådanne aftaler er kun gyldige, hvis de godkendes af direktøren for Arbejdstilsynet."* (Wider derogations are valid only if approved by the director of Arbejdstilsynet.)
>
> § 22: *"Hvis rammerne for de fravigelser … er fastlagt i en aftale mellem vedkommende arbejdsgiverpart og lønmodtagerorganisation, kan aftale om fravigelse for enkelte arbejdsopgaver eller ved udarbejdelse af en arbejdstidsplan ske lokalt."* (Within a central framework, deviations for single tasks or a rota may be agreed locally.)
>
> § 24: *"Aftaler i henhold til bestemmelserne i dette kapitel er kun gyldige, hvis de er indgået skriftligt. Stk. 2. Aftaler, der indgås lokalt efter bestemmelserne i §§ 22 og 23, skal tillige dokumenteres skriftligt."* (Valid only if in writing; local agreements must also be documented in writing.)
>
> § 27: *"I tilfælde af fravigelse efter §§ 19 og 21-23 skal der ydes tilsvarende kompenserende hvileperioder i umiddelbar forlængelse af arbejdet."* (Equivalent compensatory rest immediately after the work.)

**Breach or deviation** (BEK 606 § 36 stk. 1, PDF p. 4):
> *"Medmindre højere straf er forskyldt … straffes med bøde eller hæfte den, der: 1) Lader arbejde udføre i strid med §§ 3-5, §§ 7-10, §§ 12-17, …"*

Stk. 2 adds strict liability, and stk. 3 says the state itself may be fined. My reading: a cut inside a valid derogation is a lawful deviation (*fravigelse*), and work contrary to § 3 outside one is an offence.

**R5-2. The state agreement § 12, the state-wide derogation.** CIR1H 9594/2026 = Medst. 022-26 (*Cirkulære om statens arbejdstidsaftale*, 18 June 2026; https://www.retsinformation.dk/api/pdf/256749, PDF p. 13):
> *"§ 12. Den daglige hvileperiode efter arbejdsmiljølovgivningen kan undtagelsesvis nedsættes fra 11 til 8 timer 1 gang ugentlig. Stk. 2. Efter lokal aftale kan hvileperioden nedsættes til 8 timer indtil 2 gange ugentlig, dog ikke 2 døgn i træk. Stk. 3. Der kan højst være 10 arbejdsdage mellem 2 fridage."*
> → "The daily rest under the Working Environment Act may **exceptionally** be cut from 11 to 8 hours **once a week**. By **local agreement** it may be cut to 8 hours up to **twice a week, but not on 2 consecutive døgn**. There may be at most 10 working days between 2 days off."

The circular's remark to § 12:
> *"I tilfælde, hvor arbejdsmiljølovens regler om hvileperiode og fridøgn fraviges, skal der ydes tilsvarende kompenserende hvileperioder eller fridøgn. Hvor forholdene undtagelsesvis er af en sådan art, at det ikke er muligt at yde kompenserende hvileperioder eller fridøgn, skal der ydes passende beskyttelse."*

**Version stability:** OK21's CIR1H nr. 9551 af 05/07/2021 (*Historisk*; https://www.retsinformation.dk/api/pdf/224517, PDF p. 13) has the identical § 12, stk. 1–3. No OK-version split.

**MedSt's elaboration, PAV 19.2.5.2** (https://pav.medst.dk/pav/kapitel-19-arbejdstidsregler-mv/):
> *"Ved "ugentlig" forstås i denne forbindelse ikke "pr. arbejdsuge", men "pr. 7 døgn". I tilfælde, hvor arbejdsmiljølovens regler om hvileperiode og fridøgn fraviges, skal der ydes tilsvarende kompenserende hvileperioder i umiddelbar forlængelse af arbejdet eller tilsvarende kompenserende fridøgn."*
> → "'Weekly' here means not 'per working week' but '**per 7 døgn**'. … equivalent compensatory rest **immediately after the work**…"

**Who decides, PAV 19.7.1:**
> *"Endvidere er der i § 12 i statens arbejdstidsaftale åbnet mulighed for, at arbejdsgiver én gang ugentligt kan nedsætte hvileperioden til 8 timer. Efter lokal aftale kan hvileperioden nedsættes til 8 timer indtil 2 gange ugentligt, dog ikke 2 døgn i træk."*

That is, the employer alone for once per 7 døgn, and a local agreement for twice.

**Who is covered.**
- **HK (OAO-S):** covered by default.
- **PROSA:** covered through its organisation agreement's express clause (per our prior dossier, R1-2).
- **AC contract staff:** the AC agreement, CIR1H nr. 9333 af 11/04/2025 (https://www.retsinformation.dk/api/pdf/249233), contains no rest-period clause. A full-text search for "hvileperiode", "hviletid", "fridøgn", "11 timer" and "arbejdsmiljøl" found nothing, and its list of applicable general agreements names the directive-implementation agreement (Perst. 055-03) but not § 12 (NOT FOUND). So for AC *contract* staff the § 12 reduction is not visibly available (my inference from its absence).

**R5-3. The central-administration derogation, CIR 9807/1983.** Cirkulære om aftale om fravigelse af arbejdsmiljølovgivningens regler om hviletid og fridøgn for så vidt angår ansatte i centraladministrationen.
- **Identifiers:** CIR nr. 9807 af 08/11/1983 (LP.nr. 148/83).
- **Status:** retsinformation `isHistorical: false`, ELI metadata `eli:in_force = InForce`. The XML export also shows an `EndDate 2026-06-24`, whose meaning I could not establish; the verifier should check it.
- **Source:** https://www.retsinformation.dk/eli/retsinfo/2007/9807 (fetched via the document API).
- **Signatories:** Finansministeriet, StK I, StK II, LC, AC, HK, VSL and DIF.
- **PAV 19.7.1 and 19.7.3 cite it as current.**

The circular's remarks:
> *"Bestemmelsen i § 5, stk. 2, indebærer, at hvileperioden kan udskydes, således at den begynder 2 timer inden arbejdsdøgnet slutter, hvilket betyder, at en ansat i de typiske tilfælde vil kunne arbejde 22 timer i træk."*
> → "§ 5(2) means the rest may be postponed so that it starts 2 hours before the working døgn ends, which means an employee could typically work 22 hours in a row."
>
> *"Fravigelse kan ske i det i § 5 anførte omfang, idet indgreb i den daglige hvileperiode for den enkelte ansatte højst kan forekomme 10 gange pr. kalendermåned og højst 45 gange pr. kalenderår, og indgreb i det ugentlige fridøgn højst må forekomme 2 gange pr. kalendermåned og højst 15 gange pr. kalenderår."*

The agreement:
> *"§ 5. I forbindelse med de i § 4 nævnte arbejdsopgaver kan den daglige hvileperiode enten nedsættes til 8 timer eller udskydes. Stk. 2. I tilfælde af udskydelse skal hvileperioden udgøre 11 timer og være afsluttet senest 9 timer inde i det efterfølgende arbejdsdøgn. Stk. 3. Indgreb i den daglige hvileperiode kan højst forekomme 10 gange pr. kalendermåned og højst 45 gange pr. kalenderår. …"*
>
> § 4 limits it to deadlines set by legislation, the Folketing, conciliators or other authorities, the preparation for them, international negotiations, pressing collective bargaining, and elections and referendums.
>
> § 7: *"Denne aftale træder i kraft, når den er godkendt af direktøren for arbejdstilsynet."*

The remarks imply the pre-1997 model. Normally the 11 h rest had to finish within the working døgn, which was then counted from the start of normal working hours. The 22-hour figure follows from 24 − 2.

PAV also cites Fmst.cirk. 16/10 1981 (CIR nr. 9808, ministers' drivers) and lists CIS nr. 6240 af 03/05/1984 (junior doctors in central administration). I did not read these.

**R5-4. Recording and procedure.**
- **Discussion before deciding** (BEK 606 § 11): before using § 8 stk. 1 nr. 3–4, § 8 stk. 2, § 10 stk. 2, § 16 or § 17, the employer must discuss with the shop steward (*tillidsrepræsentant*) or the employees.
- **Emergency deviation must be reported** (VEJ 9666 section 5, PDF p. 4): *"Kan dispensation ikke indhentes i rette tid, kan fravigelse ske uden forudgående tilladelse, men fravigelsen kan kun ske for en enkelt hvileperiode, og fravigelsen skal snarest muligt meddeles skriftligt til det lokale arbejdstilsyncenter."* (A deviation without prior permission, for a single rest period only, must be reported in writing to Arbejdstilsynet as soon as possible.)
- **Compensatory rest must follow immediately** (VEJ 9666 section 6): *"…skal der ydes tilsvarende kompenserende hvileperioder i umiddelbar forlængelse af afslutningen af det sidste arbejde."* Arbejdstilsynet's news of 2 July 2024 (https://at.dk/nyheder/2024/07/regler-om-arbejdstid-og-hvileperiode/): *"Arbejdstilsynet har hidtil vejledt om, at den kompenserende hvileperiode skal ydes "snarest muligt". Det vil nu ikke længere være tilstrækkeligt. Fremover skal den kompenserende hvileperiode gives i umiddelbar forlængelse af det arbejde, der udløser retten til kompenserende hvileperiode. Det gælder alle former for fravigelser…"* The CJEU basis is Jaeger para. 94: *"Such rest periods must therefore follow on immediately from the working time which they are supposed to counteract…"*
- **How much compensatory rest:** "tilsvarende" (equivalent). No text found gives a formula, e.g. that a cut from 11 to 8 means 3 hours back (NOT FOUND). That reading is mine.
- **Individual agreements need approval** (PAV 19.7.3): *"For individuelt ansatte kan der tilsvarende mellem den pågældende og arbejdsgiveren indgås aftaler, der fraviger de nævnte regler, men sådanne aftaler skal under alle omstændigheder godkendes af Arbejdstilsynet."*
- **Working time must be registered** (PAV 19.8, on the Working Time Act, LBK 982/2024): employers must operate *"et arbejdstidsregistreringssystem, der gør det muligt at måle den daglige arbejdstid for hver enkelt ansat"*, kept for 5 years. *I did not re-read the Act itself.*

---

# What the algorithm must do (as far as the sources support it)

**Legend.**
- **[S]** = stated in a source (cited above).
- **[I]** = my inference, which needs owner or verifier confirmation.

### The test
1. **[S] The test.** For a moment *t* inside working time, look at the window **[t − 24 h, t]**. It must contain an **unbroken rest of ≥ 11 h** (VEJ 9666 § 2; MED 3503/1997; § 50).
   - **[I]** Apply it at **every** moment of working time, so the window rolls.
   - **[I]** Count only the part of a rest block that lies **inside** the window ("inden for denne periode").
2. **[S] What counts as rest.**
   - Rest = any time that is not working time (Directive Art. 2(2); bilag 2 § 2 nr. 2).
   - Travel beyond the normal commute is **not** rest (BEK 606 § 13).
   - On-call **at** the workplace is not rest (§ 15).
   - On-call **outside** it is rest unless its purpose is defeated, and a call-out interrupts it from the call until the return home or the end of the work (§ 16 stk. 1–2).
   - Short gaps never sum, except under home-work derogations (§ 33 stk. 4).
3. **[I] Where to evaluate (proof sketch).** It is enough to evaluate at the **end of each work interval**. Within one work interval [a, b], sliding *t* forward only adds working time at the front of the window and drops time at the back. So the longest rest inside the window can only shrink, and its minimum over [a, b] is reached at *b*.
4. **[I] An equivalent formulation that is easy to implement.** For each work-interval end *b*, find the latest maximal rest block of ≥ 11 h ending at or before *b*, and call its end *e*. The test passes if and only if **b − e ≤ 13 h**. In words: *no work may end more than 13 hours after the last full 11-hour rest ended*.
   - **Why it is equivalent:** the block contributes min(e, b) − max(s, b − 24) ≥ 11 exactly when b − 24 ≤ e − 11.
   - **Why it matters:** this reproduces the Commission's statement "consecutive working time limited to 13 hours … followed by at least 11 consecutive hours of rest" [S, OJ C 109/29].
5. **[I] A forward window gives the same result, provided every work start is an anchor.** A forward window [a, a + 24] anchored at **every** work start gives the same verdicts as the look-back test. The pre-1997 rule went wrong because it used a **single fixed anchor per day** (the "normal" start), not because it looked forward. That is the defect the 1996 bill removed [S, L 59 remarks].
6. **[I] With a valid reduction** (state § 12, BEK § 8/§ 16, CIR 9807), use 8 h instead of 11 and 16 h instead of 13 for that window.
   - Count the use against the derogation's limit: per 7 døgn, rolling [S: PAV "pr. 7 døgn"], and "not 2 døgn in a row" for § 12 stk. 2; or 10/month and 45/year (CIR 9807, § 16 stk. 3).
   - Check that equivalent compensatory rest starts **immediately** after the work [S: BEK § 27, PAV, AT 2024].
   - Below 8 h is never covered without Arbejdstilsynet's dispensation [S: KEN 9938/2006 text].

### The three cases

| Pattern | Look-back test (AT 5.01.1) [S + I] | Result | Remarks |
|---|---|---|---|
| **(i)** Tue 08–16, Tue 22–Wed 06, Thu 08–16 (Monday ending 16:00 assumed) | At **Wed 06:00**: window Tue 06:00–Wed 06:00 holds rest 06–08 (2 h, clipped) and 16–22 (6 h), so the longest is 6 h. Equivalently, the last ≥ 11 h rest ended Tue 08:00, and 06:00 − 08:00 = **22 h > 13 h** | **Breach** | Still a breach under an 8-h reduction (6 h < 8; 22 h > 16). A *single* anchor at Tue 08:00 also catches it. Grouping by start day and measuring only the gap to Thursday (26 h) misses it, which is the reviewers' finding |
| **(ii)** 12:00 day 1 to 12:00 day 2, continuous | At 12:00 day 2: window holds 0 h of rest; 24 h since the last ≥ 11 h rest ended | **Breach** | A breach even with an 8-h reduction (24 > 16). Matches KEN 9938/2006 and KEN 9298/2025 (24-h shifts need dispensation or an organisation-level agreement) and the Commission's 13-hour ceiling. A **calendar-day** reading would pass it (each calendar day holds 12 h of rest), which is one reason the Commission rejects the calendar day |
| **Lawful 13/11** (07:00–20:00 daily, rest 20:00–07:00) | At 20:00: window 20:00–20:00 holds rest 20:00–07:00 = 11 h; 20:00 − 07:00 = **13 h ≤ 13 h** | **Lawful** | 07:00–20:01 would fail (13 h 01 m > 13 h) [I, arithmetic]. A "longest rest inside the calendar day" reading would *falsely* fail it (00–07 = 7 h, 20–24 = 4 h): Commission footnote (182) |

### Notes for the design
- **[I]** Any "max daily hours = 13" check must measure the **span since the end of the last ≥ 11 h rest**, not the hours inside a calendar day or a start-day group. Split-day gaps under 11 h do not reset the span.
- **[I]** At the start of the data (no earlier entries), the checker needs an explicit assumption: unknown time before the first record is either treated as rest or flagged as "insufficient history". The sources do not address this.
- **[I] The weekly check is a separate continuous-hours check.**
  - Each 7-døgn period must hold an unbroken rest of ≥ 35 h: 24 h, plus the 11 h daily rest preceding it (MÁV-START).
  - Per Arbejdstilsynet, no more than 6 døgn may lie between two such blocks, or the agreed interval: state § 12 stk. 3 gives 10 working days, and BEK § 20 caps agreements at 12 døgn.
  - A calendar-day count of "worked days" is not the statutory test.
- **[I]** "Derogation allowed" should become a **structured permission**: floor, frequency counter and window, a consecutive-day ban, and a compensatory-rest requirement. Each use of it should be recorded so the decision can be audited, which matches the written-documentation duties in BEK §§ 5, 24.

---

# Decisive sources (for re-fetch)

| # | Identifier | Issuer | URL | Pinpoint |
|---|---|---|---|---|
| 1 | LBK nr. 442 af 24/04/2026 (arbejdsmiljøloven), kap. 9 §§ 50–56 | Beskæftigelsesmin. | https://www.retsinformation.dk/eli/lta/2026/442/pdf | PDF pp. 10–11 |
| 2 | BEK nr. 606 af 29/06/2026, Bekendtgørelse om hvileperiode og fridøgn m.v. | Arbejdstilsynet | https://www.retsinformation.dk/eli/lta/2026/606/pdf | §§ 3, 4, 5, 7, 8, 11, 12, 13–16, 19–24, 27, 33 stk. 4, 34, 36, 37; Bilag 1 nr. 6 |
| 3 | VEJ nr. 9666 af 05/09/2024, AT-meddelelse 5.01.1-1 *Daglig hvileperiode* | Arbejdstilsynet | https://www.retsinformation.dk/api/pdf/244837 | section 2 (PDF p. 2); sections 5–6 (PDF p. 4) |
| 4 | MED nr. 3503 af 01/11/1997, AT-meddelelse 5.01.1 (1997) | Arbejdstilsynet | https://www.retsinformation.dk/eli/retsinfo/1997/3503 (document API) | "11 timers hvileperiode" |
| 5 | VEJ nr. 3045 af 01/01/1984, *Vejledning om Daglig hvileperiode* | Arbejdstilsynet | https://www.retsinformation.dk/eli/retsinfo/1984/3045 (document API) | "11 timers hvileperiode" |
| 6 | 1996/1 LSF 59 (L 59, tabled 6 Nov 1996) + LOV nr. 1196 af 27/12/1996 | Arbejdsministeriet / Folketinget | https://www.retsinformation.dk/eli/ft/199612K00059 ; https://www.retsinformation.dk/eli/lta/1996/1196 | Remarks "Til § 50", "Til § 51"; general remarks pt. 2; "Skriftlig fremsættelse"; Bilag (old § 50) |
| 7 | VEJ nr. 9667 af 05/09/2024, AT-meddelelse 5.01.2-1 *Ugentlig fridøgn* | Arbejdstilsynet | https://www.retsinformation.dk/api/pdf/244838 | section 2 (PDF p. 2) |
| 8 | VEJ nr. 9638 af 05/09/2024, AT-meddelelse 5.01.4-2 *Rådighedstjeneste* | Arbejdstilsynet | https://www.retsinformation.dk/api/pdf/244839 | section 3 (PDF p. 3) |
| 9 | KEN nr. 9938 af 04/09/2006 | Arbejdsmiljøklagenævnet | https://www.retsinformation.dk/eli/retsinfo/2006/9938 (document API) | Resumé; reasoning |
| 10 | KEN nr. 9298 af 25/02/2025 | Arbejdsmiljøklagenævnet | https://www.retsinformation.dk/api/pdf/249024 | Resume (PDF p. 1) |
| 11 | CIR1H nr. 9594 af 18/06/2026 (= Medst. 022-26), *Cirkulære om statens arbejdstidsaftale* | Finansministeriet / MedSt | https://www.retsinformation.dk/api/pdf/256749 (MedSt copy: https://cirkulaere.medst.dk/media/jhshf3w2/022-26.pdf) | § 12 + remark (PDF p. 13); § 7 nr. 3 remark (PDF p. 9); § 15 (PDF p. 14); bilag 2 §§ 2–3 (PDF pp. 18–19) |
| 12 | CIR1H nr. 9551 af 05/07/2021 (OK21, *Historisk*) | Finansministeriet | https://www.retsinformation.dk/api/pdf/224517 | § 12 (PDF p. 13) |
| 13 | CIR nr. 9807 af 08/11/1983 (LP.nr. 148/83), central-administration rest derogation | Finansministeriet | https://www.retsinformation.dk/eli/retsinfo/2007/9807 (document API: POST https://www.retsinformation.dk/api/document/eli/retsinfo/2007/9807) | remarks; §§ 4–7 |
| 14 | PAV kap. 19 (19.2.5.2, 19.2.10, 19.7.1–19.7.3, 19.8) | Medarbejder- og Kompetencestyrelsen | https://pav.medst.dk/pav/kapitel-19-arbejdstidsregler-mv/ | as cited (no version date on page) |
| 15 | CIR1H nr. 9333 af 11/04/2025, *Cirkulære om overenskomst for akademikere i staten* | Finansministeriet / MedSt | https://www.retsinformation.dk/api/pdf/249233 | full-text search: no rest clause |
| 16 | Directive 2003/88/EC, OJ L 299, 18.11.2003, p. 9 | EU | https://publications.europa.eu/resource/celex/32003L0088 (Accept: application/pdf) | Arts. 2, 3, 5, 16, 17(2)–(4), 18 |
| 17 | Commission Interpretative Communication on Directive 2003/88/EC, OJ C 109, 24.3.2023 | European Commission | https://publications.europa.eu/resource/oj/JOC_2023_109_R_0001 (Accept: application/pdf) | ch. V.A (C 109/29) incl. fn. 182–184; V.C (C 109/31); IX on Art. 17(4)(b) (C 109/58) |
| 18 | CJEU C‑151/02 *Jaeger*, 9 Sept 2003, EU:C:2003:437 | CJEU | https://publications.europa.eu/resource/celex/62002CJ0151 (Accept: text/html) | paras. 94–95 |
| 19 | CJEU C‑477/21 *MÁV-START*, 2 March 2023 | CJEU | https://publications.europa.eu/resource/celex/62021CJ0477 (Accept: application/xhtml+xml) | paras. 38–44; operative part 1–3 |
| 20 | CJEU C‑306/16 *Maio Marques da Rosa*, 9 Nov 2017, EU:C:2017:844 | CJEU | https://publications.europa.eu/resource/celex/62016CJ0306 (Accept: application/xhtml+xml) | paras. 39–48; operative part |
| 21 | at.dk news, 02.07.2024, *Nye regler om arbejdstid og ændrede regler om hvileperiode pr. 1. juli 2024* | Arbejdstilsynet | https://at.dk/nyheder/2024/07/regler-om-arbejdstid-og-hvileperiode/ | FAQ "kompenserende hvileperiode" |

**Context only (not decisive):**
- Commission SWD(2023) 40, Council doc ST 7514/23 ADD 1 (https://data.consilium.europa.eu/doc/document/ST-7514-2023-ADD-1/en/pdf), section B and footnote 63.
- KEN nr. 9944 af 05/09/2016 (https://www.retsinformation.dk/api/pdf/183969), on-call, not reckoning.
- Arbitration awards FV 2019.0117 (https://arbejdsretten.dk/media/19246/kendelse-af-3-januar-2020-i-fv20190117.pdf) and FV 2020-348 (https://arbejdsretten.dk/media/16922/2020-348.pdf): both quote the rule; neither fixes the anchor.
- **Secondary:** DI (https://www.danskindustri.dk/vi-radgiver-dig/personale/arbejdstid/hviletid-fridogn-og-helbredskontrol/, updated 01.02.26) and ASE (https://www.ase.dk/faa-svar/ansaettelse/arbejdstid/11-timers-reglen, published 18 Oct 2026). Neither defines the anchor.

**Searched and NOT FOUND:**
- a Danish court or arbitration ruling fixing the start of the 24-hour period;
- a CJEU ruling fixing it;
- a worked split-day example in any Arbejdstilsynet or MedSt text;
- "13 timer" as a daily maximum in Danish primary text;
- a definition of the 7-døgn anchor;
- how a night shift counts toward § 12 stk. 3's "arbejdsdage";
- a formula for the amount of "tilsvarende" compensatory rest;
- a rest clause in the AC agreement;
- the current at.dk rule pages (404; content available only via retsinformation).
