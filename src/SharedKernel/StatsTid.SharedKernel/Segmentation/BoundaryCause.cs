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
    /// Tie-break order (ADR-017 D9b): <c>OkTransition &gt; AgreementConfigPromotion &gt;
    /// LocalProfileActivation &gt; PositionOverrideEffective &gt; EuWtdRulesetVersion</c>.
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

    // --- Phase 4 follow-up values (not yet active; reserved for "Versioned History for
    //     Non-Dated Boundary Sources" sprints — see ADR-016 D5b and ROADMAP Phase 4) ---

    /// <summary>
    /// Reserved for Phase 4: an entitlement-policy effective-date boundary.
    /// </summary>
    EntitlementPolicyChange,

    /// <summary>
    /// Reserved for Phase 4: an employee-profile effective-date boundary (e.g. hours change).
    /// </summary>
    EmployeeProfileChange,
}
