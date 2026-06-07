# Vacation-settlement law research — Ferielov §§ + Cirkulære 021-24 (S67 / ADR-033)

**Date:** 2026-06-07. **Method:** deep-research (opus, adversarial — refuted both prior numbering analyses) + **1 independent refute-lens verification — ALL 9 claims VERIFIED, 0 refuted, 0 unverifiable** (the S65/S66 bar). **Status:** the verification independently primary-source-confirmed the items the first pass had as mirror-caveats — **LBK 152/2024 via the retsinformation ELI registry directly**; the cirkulære §§ via a second `pdftotext` extraction of the official 021-24 PDF; the SLS day-count via the official statens-adm.dk vejledning; each Ferielov spine § corroborated by a second source. Verdict trusted and acted on (shipped-doc corrections applied). **Purpose:** settled the S67 numbering dispute (Codex cycle-2 vs the project's S65 research) and gates ADR-033; the shipped-doc re-audit is DONE (see § Shipped-doc corrections — applied).

## Currency (read first)

- **Current consolidated Ferieloven = LBK nr 152 af 20/02/2024** (incorporating lov 1538+1539 af 12/12/2023 — the bagatelgrænse amendments). The §§21–34 numbering is unchanged from LBK 230/2021, so the section numbers are stable across the consolidation.
- **State sector = Cirkulære af 17. maj 2024, Medst.nr. 021-24, "Aftale om Ferie"** (Finansministeriet/Medst.), virkning 1. april 2024 (replaces 089-19/2019). PDF: `cirkulaere.medst.dk/media/1372/021-24.pdf`. §3: månedslønnede "følger ferieloven med de afvigelser, der er anført i §§ 4-11"; the særlige-feriedage regime is §§12–18.

## Verdict table (mechanism → § → source-class → A/B)

The dispute was between **Analysis A** (the project's S65 `ferie-transfer-timing-research.md`: §21 / §26 / §12 stk.2) and **Analysis B** (Codex cycle-2: §22 / §24 / §26 / §15 stk.2 / §17).

| Mechanism | **Verified §** | Verbatim (abridged) | A / B |
|-----------|---------------|---------------------|-------|
| Transfer by **written agreement** (>4 wk → next period; **31 Dec deadline**) | **Ferielov §21** (stk.1 + stk.2) | §21 stk.2: *"…skal skriftligt indgå aftale efter stk. 1 senest den 31. december i ferieafholdelsesperioden."* | **A** (§21). Refutes B's "§22 = agreement-transfer". |
| **Automatic post-period payout** of earned-w/-pay vacation **>4 weeks** | **Ferielov §24** | §24: *"Efter ferieafholdelsesperiodens udløb udbetaler arbejdsgiveren optjent ferie med løn og ferietillæg ud over 4 uger…"* | **B** (§24). (A silent.) |
| **First-4-weeks** untaken fate (forfeiture) | **Ferielov §34** | §34: untaken feriebetaling not paid under §§23-25/§26 stk.1 and not transferred under §§21-22 *"tilfalder Arbejdsmarkedets Feriefond"* (afregning senest 15. nov.). | **Neither** named §34. The first 4 weeks are NOT auto-paid (only >4wk under §24); if not feriehindring-transferred, they forfeit to the Feriefond. |
| **Feriehindring** (sickness/barsel prevents ferie; auto, ≤4 wk) | **Ferielov §22** (transfer) + **§25** (persistent-impediment payout) | §22: *"Er en lønmodtager på grund af særlige forhold afskåret fra at holde optjent betalt ferie … overføres op til 4 ugers årlig betalt ferie til den efterfølgende ferieafholdelsesperiode."* | **B** (§22/§25). |
| **Termination** — pay out earned-untaken | **Ferielov §26** (stk.1) | §26 stk.1: earned-untaken feriegodtgørelse in an ended employment *"udbetales efter anmodning til lønmodtageren…"* | **B** (§26 = termination). |
| **Termination — negative/forskud modregning** (set-off, capped) | **Ferielov §7 stk.1, 2. pkt.** | *"Fratræder lønmodtageren, inden udligning er sket, er arbejdsgiveren berettiget til at modregne med værdien af den afholdte, ikkeudlignede ferie i lønmodtagerens udestående krav på løn og feriebetaling."* (STAR: capped at outstanding pay — no out-of-pocket clawback.) | **Neither** fully captured — it's **§7**, not §26; capped. |
| **State særlige feriedage — default cash godtgørelse** | **Cirkulære 021-24 §15 stk.2** (payout) + **§17** (2½% calc) | §15 stk.2: *"Indgås der ikke aftale i henhold til stk. 1, godtgøres tilgodehavende særlige feriedage kontant til den ansatte ved udløbet af afholdelsesperioden …, jf. § 17."* §17: *"Kontant godtgørelse for særlige feriedage udgør 2 ½ pct. af den ferieberettigende løn i optjeningsåret (kalenderåret)."* | **B** (§15 stk.2/§17). **REFUTES A's "§12 stk.2 = godtgørelse".** |
| **Særlige feriedage — taking window / accrual** | **Cirkulære 021-24 §12** | §12: 0,42 dag/md i optjeningsåret (kalenderåret) = 5/år; §12 stk.2: *"Særlige feriedage afvikles i det år, der går fra 1. maj til 30. april…"* | §12 stk.2 = the **taking window**, NOT the godtgørelse. |
| State divergence | **Cirkulære §3** | Månedslønnede *"følger ferieloven med de afvigelser, der er anført i §§ 4-11."* | **Defers** to Ferielov (with listed deviations); the særlige regime is §§12–18. |
| **SLS input contract** | løndele 5017/5027/5037 (days) | Input = *antal dage* (felt 3 to-pay / felt 4 accrued); SLS auto-applies *"2½ % af den feriegivende løn"*. Ferielønregulering = 5062 (hours). Kroner = manual-correction only (5007). | n/a — **confirms the day-count line boundary (OQ-1a): StatsTid emits days, SLS owns the rate.** |

## Net A-vs-B verdict

- **A is right ONLY on §21** (transfer-by-agreement). **B's spine is substantially correct** (§24 auto-payout, §22/§25 feriehindring, §26 termination, §15 stk.2/§17 godtgørelse) — with two refinements B also got wrong/missed: **§22 is feriehindring only** (the agreement-transfer is §21, not §22), and the **negative-balance modregning is §7 stk.1 (capped), distinct from §26**. **Both missed §34 (Feriefonden forfeiture) and §7 (forskud modregning).**
- **2,02% vs 2½% — do NOT conflate:** cirkulære **§10** raised the general ferietillæg-replacement ("særlig feriegodtgørelse") to **2,02%** at OK-2024; cirkulære **§17** (cash godtgørelse for *unused særlige feriedage*) **remains 2½%**. Two different figures, two different things.

## Shipped-doc corrections required (the re-audit)

- ✅ **STANDS:** ADR-030 D9 / OQ-1 boundaryMonth=12 / "§21 stk.2 = 31 Dec transfer deadline" — **correct, verbatim-confirmed.** The S66 disposition work's anchor is right.
- ✅ **CORRECTED (S67, applied):** the **"Cirkulære 021-24 §12 stk.2 = 2½% godtgørelse"** error → repointed to **§15 stk.2 (default payout) + §17 (2½% calc)** (§12 stk.2 kept where it correctly cited the *taking window*). Sites fixed: **ADR-030 D9** (the model-vs-law-gap bullet + sources + the S66 amendment block); `BalanceEndpoints.cs:859` (code comment); **ROADMAP.md** (S65 follow-up iii); **`ferie-transfer-timing-research.md`** (S65 — §21 stk.2 retained; the godtgørelse §-attribution corrected with an inline note). _(KB INDEX D9 row + danish-agreements.md checked — their §12 stk.2 / godtgørelse references were already either the correct window-citation or §-free; no change needed. Closed sprint logs SPRINT-65 left as historical record.)_
- ✅ **CORRECTED (loose §26):** ADR-030 D9 non-equivalence + amendment "§26 payout/auto-paid" → **§24** (the post-period auto-payout); §26 reserved for termination, with §7 for forskud modregning.

## Caveats / unverified

- Ferielov §§ verbatim from danskelove.dk mirror (retsinformation 403'd) — cross-validated, mirror caveat on exact punctuation. The cirkulære §§ are from the locally-extracted official PDF (airtight).
- §24 stk.2/3 (the ≤5.000 kr bagatelgrænse routing, raised by lov 1538/1539-2023) — existence confirmed, not quoted verbatim; secondary-sourced (DI/Dansk Erhverv).
- Refute-lens verification in flight at authoring; this doc records the deep-research verdict — amend if verification flips a claim.

## Sources

- Ferieloven LBK 152/2024 (danskelove.dk mirror; retsinformation canonical fetch-blocked).
- Cirkulære 021-24 "Aftale om Ferie" — `cirkulaere.medst.dk/media/1372/021-24.pdf` (locally extracted, verbatim).
- STAR (forskudsferie/modregning Q&A); Statens Administration SLS brugervejledninger (løndele 5017/5027/5037/5062/5007/5030).
