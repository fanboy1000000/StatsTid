namespace StatsTid.SharedKernel.Segmentation;

/// <summary>
/// Identifies the source that introduced a segment boundary within a <see cref="PlannedCalculation"/>.
///
/// Adding new values is non-breaking: the projection column <c>boundary_cause_summary TEXT[]</c>
/// in the <c>segment_manifests</c> table (ADR-016 D10) stores these as free-text strings, so
/// new enum members never require a DB migration.
///
/// Correspondence with the DB schema: these values appear verbatim in the <c>boundary_cause_summary</c>
/// array; the column's GIN index makes them filterable without normalising them to a lookup table.
/// </summary>
public enum BoundaryCause
{
    /// <summary>
    /// An OK collective-agreement version transition (e.g. OK24 → OK26 on 2026-04-01).
    /// </summary>
    OkTransition,

    /// <summary>
    /// A DRAFT → ACTIVE promotion of an <c>agreement_config</c> row (ADR-014; its
    /// <c>active_from</c> date fell inside the calculation period).
    /// </summary>
    AgreementConfigPromotion,

    /// <summary>
    /// A <c>local_agreement_profiles</c> row whose <c>effective_from</c> date (ADR-017, S21)
    /// falls inside the calculation period — splitting the calculation into
    /// pre-activation and post-activation segments.
    ///
    /// Tie-break order (ADR-017 D9b as extended by ADR-040 D5): <c>EmploymentStarted &gt;
    /// EmploymentEnded &gt; OkTransition &gt; AgreementConfigPromotion &gt;
    /// LocalProfileActivation &gt; PositionOverrideEffective &gt; EmployeeProfileChange &gt;
    /// EuWtdRulesetVersion</c>.
    /// </summary>
    LocalProfileActivation,

    /// <summary>
    /// A position-override policy whose <c>effective_from</c> date (ADR-013, S11/S14) falls
    /// inside the calculation period.
    /// </summary>
    PositionOverrideEffective,

    /// <summary>
    /// A compliance-ruleset version bump for the EU Working Time Directive (ADR-015, S16)
    /// that takes effect inside the calculation period.
    /// </summary>
    EuWtdRulesetVersion,

    // --- Phase 4 follow-up values (reserved by ADR-016 D5b for "Versioned History for
    //     Non-Dated Boundary Sources" sprints — see ROADMAP Phase 4) ---

    /// <summary>
    /// Reserved for Phase 4: an entitlement-policy effective-date boundary.
    /// </summary>
    EntitlementPolicyChange,

    /// <summary>
    /// An <c>employee_profiles</c> row whose <c>effective_from</c> date falls inside the
    /// calculation period (position / part-time-fraction change). Reserved by ADR-016 D5b;
    /// ACTIVATED by ADR-040 D5 (S137) — profile effective dates now split segments, closing
    /// the "UI shows what payroll will not pay" divergence. Tie-break slot: after
    /// <see cref="PositionOverrideEffective"/>, before <see cref="EuWtdRulesetVersion"/>.
    /// </summary>
    EmployeeProfileChange,

    // --- ADR-040 D5 employment-window causes (S137). APPENDED at the end deliberately:
    //     the projection column is free text (see the type doc above), so ordinals are
    //     serialization-irrelevant — appending keeps every existing ordinal stable for the
    //     legacy NUMERIC rows the tolerant reader still accepts (QUAL-002). Their tie-break
    //     PRIORITY is nevertheless the HIGHEST (BoundaryDetector iterates them first):
    //     enum position and tie-break rank are independent by design. ---

    /// <summary>
    /// The employee's employment window opens inside the calculation period (ADR-040 D5):
    /// the boundary date is <c>employment_start_date</c> itself — the first employed day.
    /// The pre-hire NOT_EMPLOYED prefix ends at <c>start − 1</c> (ADR-040 D1). Outranks
    /// every other cause when boundaries coincide (non-employment is the strongest fact
    /// about a date — a hire on 2026-04-01 coincides with the OK24→OK26 transition and
    /// must record as the hire).
    /// </summary>
    EmploymentStarted,

    /// <summary>
    /// The employee's employment window closes inside the calculation period (ADR-040 D5):
    /// the boundary date is <c>employment_end_date + 1</c> — the FIRST NOT-employed day,
    /// because the window's end date is INCLUSIVE (the last day employed, ADR-040 D1 /
    /// ADR-033 S70 R1 semantics) and a boundary date is always the first day of the NEW
    /// segment. Second-highest tie-break rank, after <see cref="EmploymentStarted"/>.
    /// </summary>
    EmploymentEnded,
}
