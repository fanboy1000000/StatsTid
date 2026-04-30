namespace StatsTid.SharedKernel.Segmentation;

/// <summary>
/// What kind of artifact a rule produces (ADR-016 D2 — third axis of the multi-axis
/// classification triple).
///
/// The family axis exists because <see cref="ADR-015"/> (compliance-check-result-pattern)
/// requires a separate result shape for compliance findings; same-result-shape merging
/// from PAT-006 does not apply uniformly across both families.
/// </summary>
public enum Family
{
    /// <summary>Rule emits <c>CalculationResult</c>-compatible artifacts (PAT-006).
    /// Examples: supplements, overtime, norm checks.</summary>
    Calculation,

    /// <summary>Rule emits <c>ComplianceCheckResult</c>-compatible artifacts (ADR-015).
    /// Examples: rest period, weekly 48h ceiling, overtime governance.</summary>
    Compliance,
}
