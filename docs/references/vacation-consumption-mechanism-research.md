# Vacation consumption mechanism research — §6 stk.2 / særlige feriedage (S66 / ADR-032)

**Date:** 2026-06-07. **Method:** deep-research + 2 independent adversarial refute-lens verifications (the S63/S65 discipline). **Owner adjudications:** 2026-06-07 — drop the 5÷N conversion engine + `work_days_per_week` field; ADR-031 D6 launch gate satisfied by ADR-032 D1-family landing.

## Question

Does Ferieloven §6 stk.2 (and the state ferieaftale) prescribe that a worker on N days/week consumes `5 ÷ N` feriedage per day off — the mechanism ADR-031 D6 scoped ADR-032/S66 around? And does any such conversion apply to state-sector særlige feriedage?

## Verdict (verified)

**No 5÷N consumption multiplier exists in the state-sector authority — for ordinary ferie OR særlige feriedage.** The statutory and state-sector mechanism is:

1. **§6 stk.2 (LBK nr 230 af 12/02/2021, verified verbatim against the official Lovtidende PDF):** *"Ferie holdes med 5 dage om ugen og på samme måde, som arbejdet tidsmæssigt er tilrettelagt. Arbejdsfrie dage og vagtdage i turnus indgår i ferien med et forholdsmæssigt antal."* — a **week-mirroring/placement rule**: vacation is held 5 days/week mirroring the work pattern; non-working days are counted INTO the vacation proportionally (a 4-day/week worker's 5 vacation weeks contain 5 arbejdsfri dage).
2. **State Ferievejledning (Medst., Dec 2019, 92 pp., read verbatim — medst.dk/media/kp4fffy1/ferievejledning-2019.pdf):**
   - **Example 3.5 (§3.7.2, p.34):** særlige-feriedag consumption fraction = **hours-off ÷ THAT day's scheduled hours** (3h on a 6h day = ½; 3h on a 9h day = ⅓). One whole scheduled workday off = exactly **1** dag, regardless of days/week. *"Særlige feriedage kan holdes samlet, enkeltvis og som brøkdele af dage."*
   - **§8.1 (p.81) + §8.4 (p.84):** <5-day/week employees accrue the same 2,08 + 0,42 dage/month; *"Der gælder ikke særregler … men ved afholdelsen indgår et forholdsmæssigt antal arbejdsfri dage."*
   - **Full-document sweep: no 5÷N-style per-day multiplier anywhere** (the only ÷5 occurrences are unrelated erstatningsferie/feriedifference math; the only proportional scaling is the inverse 6-day-week 5/6 conversion).
3. **The "5÷N per day off" framing** traces to practitioner operationalizations (employment-law firm Selskabsadvokaterne; payroll vendor Zenegy), which DO debit a single day at 5÷N (a 2-day/week worker's lone feriedag = 2,5). The two readings **coincide for whole weeks** and diverge **only for single days taken by <5-day-week workers** — and the state-sector authority never prescribes the multiplier.
4. **2024 successor circular (Skm. cirk. 021-24):** continuity surfaced (same accrual, same brøkdele afholdelse); full body text not read verbatim this pass (`cirkulaere.medst.dk/media/1372/021-24.pdf` if ever needed).

## Verification trail

- **Verifier A (statute + enkeltdag question):** §6 stk.2 text VERIFIED (Lovtidende `lovtidende.dk/api/pdf/241253`); "placement-rule-not-per-day-conversion" dichotomy REFUTED as stated — practitioner sources apply 5÷N to single days (Selskabsadvokaterne: *"en feriedag omregnes til 2,5 (5/2) pr. dag"*, applied to *"alene holder 1 feriedag i en uge"*); "ferie holdes i hele dage" PARTIALLY (guidance-level, not verbatim statute).
- **Verifier B (vejledning):** Example 3.5, §8.1, §8.4 all VERIFIED verbatim; "no 5÷N anywhere in the vejledning/Bilag 1 (§§12–18)" VERIFIED by full read.

## Implementation implication (the ADR-032 ruling)

Under StatsTid's shape-free norm model (per-day norm = `weekly × fraction ÷ 5` on all 5 weekdays — schedule shape unmodeled, ADR-032 accepted limitation), the §6 stk.2 folding is **self-enforcing for week blocks**: any worker taking a week off absents 5 norm-days = 5 feriedage. With the ADR-032 D1 basis (`hours ÷ fullDayHours(e,d)` — exactly the Example-3.5 mechanism), the system is **exactly correct for all 5-day workers at any fraction, and for særlige feriedage per the primary source**. Adding ×(5÷N) on top would over-consume week-blocks (6,25/week for a 4-day worker) under every reading. The residual divergence — a single enkeltdag taken by a <5-day **compressed-schedule** worker (1,0 in-model vs 1,25 under the practitioner reading) — is unprescribed by the state authority and unrepresentable without schedule-shape modeling; documented as part of the shape-unmodeled limitation.

**Consequences:** the `work_days_per_week` field and the 5÷N engine are dropped from S66; ADR-031 D6's launch-blocking premise (over-entitlement "once <5-day schedules are representable") is dissolved — shape stays unrepresentable by design and week-offs are self-correct; the gate is recorded satisfied by ADR-032 D1/D2/D3 landing. SPECIAL_HOLIDAY consumption is flat per the verified source (no conversion) — same D1 basis as VACATION.

## Sources

- LBK nr 230 af 12/02/2021 (Ferieloven): https://www.lovtidende.dk/api/pdf/241253 · https://www.retsinformation.dk/eli/lta/2021/230
- Ferievejledning, Dec 2019 (Medst./CFU): https://medst.dk/media/kp4fffy1/ferievejledning-2019.pdf
- Skm. cirk. 021-24 (aftale om ferie): https://cirkulaere.medst.dk/media/1372/021-24.pdf
- PAV kap. 23: https://pav.medst.dk/pav/kapitel-23-ferie/
- Practitioner 5÷N readings (refute-lens counter-evidence): selskabsadvokaterne.dk "Ferielovens §23 / deltidsansættelse"; Zenegy payroll guidance
