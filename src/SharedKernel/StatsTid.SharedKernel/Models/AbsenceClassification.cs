namespace StatsTid.SharedKernel.Models;

/// <summary>
/// How one absence day moves the flex balance. Every code in <see cref="AbsenceTypes"/> has
/// exactly one effect, recorded in <see cref="AbsenceClassification"/>.
/// </summary>
public enum AbsenceNormEffect
{
    /// <summary>
    /// The absence's hours count as performed working time: they are credited toward the norm,
    /// so a whole day of it leaves flex unchanged (paid absence; Statens arbejdstidsaftale
    /// § 7 nr. 2).
    /// </summary>
    Credit,

    /// <summary>
    /// The absence's hours lower the norm instead (unpaid leave): the day is not owed, and it is
    /// not credited either.
    /// </summary>
    Reduction,

    /// <summary>
    /// The absence moves neither worked time nor the norm. No code is neutral since S147
    /// (RR-4); the value keeps the classification total should a future code need it.
    /// </summary>
    Neutral,
}

/// <summary>
/// S147 / TASK-14708 (QUAL-231, T3a with T3b folded in) — the ONE classification of absence
/// codes: each code in the <see cref="AbsenceTypes"/> vocabulary is exactly one of credit,
/// reduction or neutral, here and nowhere else.
///
/// <para>
/// <b>Why it lives in SharedKernel.</b> Two readers need the same set. The Rule Engine's
/// <c>AbsenceRule</c> credits or reduces by it; the Backend's Skema save refuses (422) a code
/// that is not in it. The Backend may reach the Rule Engine only over HTTP
/// (docs/ARCHITECTURE.md hard rule 2), so the set cannot live in the engine. Nothing here is
/// served to the screen (RR-4): with <see cref="AbsenceTypes.SickDay"/> credited, every code is
/// credit or reduction, which is exactly the screen's own day formula (worked + absence − norm).
/// </para>
///
/// <para>
/// <b>What it decides, and what it does not.</b> It decides WHETHER a code counts. HOW MUCH it
/// counts is <c>AbsenceRule.EffectiveHours</c>: the stored hours, which the Skema save already
/// caps at the day's per-day norm (<c>DayNorm</c>, the "planned hours" of § 7 nr. 2; RR-2).
/// So a whole sick day credits the day's norm, and a partial sick day (hours worked plus the
/// day's remaining hours registered as sick) also closes with a delta of 0 (RR-3).
/// </para>
///
/// <para>
/// <b>OK versions.</b> The classification has no OK-version split, deliberately: the source
/// text, Statens arbejdstidsaftale § 7 nr. 2 and its remark, is identical in the OK24 text
/// (Medst. 014-21 / CIR1H 9551) and the OK26 text (Medst. 022-26), verified token by token
/// (docs/references/sick-day-credit-and-agreement-texts-research.md, R1-1). A future version
/// that changes it would add the split here.
/// </para>
///
/// <para>
/// <b>Pure.</b> Static, immutable data; no I/O, no clock. A duplicate code fails type
/// initialisation (the <see cref="ByCode"/> build throws), so a copy-paste slip cannot give one
/// code two effects.
/// </para>
/// </summary>
public static class AbsenceClassification
{
    /// <summary>
    /// Every vocabulary code with its effect, in the order of <see cref="AbsenceTypes"/>.
    /// </summary>
    public static readonly IReadOnlyList<KeyValuePair<string, AbsenceNormEffect>> InDeclarationOrder =
    [
        // ── Own sickness: CREDIT for every agreement StatsTid encodes (S147, RR-1). ──
        // Source split:
        //  • HK and PROSA — SOURCED. Statens arbejdstidsaftale § 7 nr. 2: "Dage med ret til fravær
        //    med løn medregnes med det antal timer, den ansatte skulle have arbejdet den
        //    pågældende dag. Hvis der ikke er fastsat noget timetal for den pågældende dag,
        //    medregnes for fuldtidsansatte 7,4 timer og for ansatte på deltid eller plustid et
        //    forholdsmæssigt timetal." Its remark: "Fravær med ret til løn er fx sygedage, …"
        //    (Medst. 022-26 = 014-21). HK is under that agreement by 087-25 § 38; PROSA by its
        //    organisation agreement § 12 (019-26; OK24 079-25).
        //  • AC-agreement staff — a LABELLED DEFAULT BY ANALOGY, not a sourced rule. No central
        //    AC text sets the credit (PAV 19.3.4: "Arbejdstid som begreb er ikke nærmere
        //    fastlagt"); PAV 19.5.9 requires sick days to be counted into the flex account, and
        //    the amount is the local flex agreement's. The HK/PROSA rule is applied as the
        //    default: owner ruling D-8b (Q-1) = (a), 2026-10-29. A local agreement may set
        //    another rule (022-26 § 3); that per-institution parameter is S148's.
        new(AbsenceTypes.SickDay, AbsenceNormEffect.Credit),

        // ── Paid holiday: CREDIT (§ 7 nr. 2's remark names "feriedage"). Unchanged from pre-S147.
        new(AbsenceTypes.Vacation, AbsenceNormEffect.Credit),

        // ── Omsorgsdage, paid leave: CREDIT (§ 7 nr. 2's remark: "tjenestefrihed med løn").
        //    Unchanged from pre-S147.
        new(AbsenceTypes.CareDay, AbsenceNormEffect.Credit),

        // ── A child's 1st, 2nd and 3rd sick day: CREDIT. The leave is paid as during sickness
        //    (029-24, 021-26 and 020-26 § 14 stk. 2; CIR1H 9333 § 18 stk. 6 for AC), so it is paid
        //    absence under § 7 nr. 2 (research R1-7). Pre-S147 these were credited only under an
        //    old numbered spelling that nothing stored (QUAL-231). Which agreements may use day 2 and
        //    day 3, and the quotas, are NOT decided here (QUAL-236, S148).
        new(AbsenceTypes.ChildSick1, AbsenceNormEffect.Credit),
        new(AbsenceTypes.ChildSick2, AbsenceNormEffect.Credit),
        new(AbsenceTypes.ChildSick3, AbsenceNormEffect.Credit),

        // ── Barsel: CREDIT. Unchanged from pre-S147.
        new(AbsenceTypes.ParentalLeave, AbsenceNormEffect.Credit),

        // ── Seniordage, paid leave: CREDIT. Unchanged from pre-S147.
        new(AbsenceTypes.SeniorDay, AbsenceNormEffect.Credit),

        // ── Særlige feriedage, paid holiday: CREDIT. Unchanged from pre-S147.
        new(AbsenceTypes.SpecialHolidayAllowance, AbsenceNormEffect.Credit),

        // ── Tjenestefrihed med løn: CREDIT (named in § 7 nr. 2's remark). Unchanged from pre-S147.
        new(AbsenceTypes.LeaveWithPay, AbsenceNormEffect.Credit),

        // ── Tjenestefrihed uden løn: REDUCTION. Not paid absence, so § 7 nr. 2 does not credit
        //    it; the hours are taken off the norm instead. Unchanged from pre-S147.
        new(AbsenceTypes.LeaveWithoutPay, AbsenceNormEffect.Reduction),
    ];

    /// <summary>The same classification for lookup, ordinal-keyed (stored codes are case-exact).</summary>
    public static readonly IReadOnlyDictionary<string, AbsenceNormEffect> ByCode =
        InDeclarationOrder.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

    /// <summary>True when <paramref name="absenceType"/> is a vocabulary code (null is not).</summary>
    public static bool IsKnown(string? absenceType) =>
        absenceType is not null && ByCode.ContainsKey(absenceType);

    /// <summary>
    /// The effect of <paramref name="absenceType"/>; false for a code outside the vocabulary
    /// (and for null), which then counts as neither credit nor reduction.
    /// </summary>
    public static bool TryGetEffect(string? absenceType, out AbsenceNormEffect effect)
    {
        if (absenceType is not null && ByCode.TryGetValue(absenceType, out effect))
            return true;
        effect = default;
        return false;
    }

    /// <summary>True when the code's hours are credited toward the norm.</summary>
    public static bool IsCredit(string? absenceType) =>
        TryGetEffect(absenceType, out var effect) && effect == AbsenceNormEffect.Credit;

    /// <summary>True when the code's hours reduce the norm instead (unpaid leave).</summary>
    public static bool IsReduction(string? absenceType) =>
        TryGetEffect(absenceType, out var effect) && effect == AbsenceNormEffect.Reduction;
}
