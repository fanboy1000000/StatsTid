namespace StatsTid.SharedKernel.Models;

/// <summary>
/// S136 / ADR-040 D1 — the employment-window fact for an (employee, date) pair: was this
/// person employed on that date? Named as a DOMAIN FACT, not a resolver-result, because
/// ADR-040 D5 reuses this exact enum as the employment state carried by typed payroll
/// segments (<c>PlannedSegment</c> gains an EMPLOYED / NOT_EMPLOYED state when Increment 2
/// activates employment-boundary segmentation).
///
/// <para>
/// <b><c>EMPLOYED</c> MUST stay 0.</b> ADR-040 D5 pins the replay default: pre-D5 segment
/// manifests lack the employment state in their JSON, and deserialization must default the
/// missing field to EMPLOYED — a NOT_EMPLOYED default would make historical replays silently
/// evaluate zero rules. <c>default(EmploymentWindowStatus) == EMPLOYED</c> makes that
/// property structural. Member casing mirrors <see cref="AgreementConfigStatus"/> (the
/// SCREAMING_CASE precedent for status enums whose names cross serialization boundaries).
/// </para>
/// </summary>
public enum EmploymentWindowStatus
{
    /// <summary>The date falls inside the employee's employment window (ADR-040 D1/D2).</summary>
    EMPLOYED = 0,

    /// <summary>The date falls outside the employee's employment window.</summary>
    NOT_EMPLOYED = 1,
}
