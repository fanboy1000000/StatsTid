namespace StatsTid.SharedKernel.Models;

public sealed record class EmploymentProfile
{
    public required string EmployeeId { get; init; }

    /// <summary>
    /// Agreement code for the employee at the consumption-time asOfDate (e.g. "AC", "HK",
    /// "PROSA"). Per ADR-023 D2 + S34: this value is sourced from the dated
    /// <c>user_agreement_codes</c> table via
    /// <c>UserAgreementCodeRepository.GetByUserIdAtAsync(employeeId, asOfDate, ct)</c>
    /// at PCS replay consumption sites; <c>users.agreement_code</c> remains as a
    /// denormalized cache for live-only consumers (JWT mint, current-row reads).
    /// Replay-sensitive readers MUST NOT hydrate this field from <c>users.agreement_code</c>
    /// — past-period historical replays would silently use today's agreement_code instead
    /// of the period-effective value. ADR-016 D10 closed for this field in S34
    /// (4th and final rule-engine input).
    /// </summary>
    public required string AgreementCode { get; init; }
    public required string OkVersion { get; init; }
    public required string EmploymentCategory { get; init; }
    public bool IsPartTime { get; init; }
    public decimal PartTimeFraction { get; init; } = 1.0m;

    /// <summary>
    /// Position code for position-based rule overrides (e.g. "DEPARTMENT_HEAD", "RESEARCHER").
    /// Null means no position override — base agreement config applies.
    /// </summary>
    public string? Position { get; init; }

    /// <summary>
    /// Organization id (matches <c>organizations.org_id</c>). Required by S21's
    /// <c>local_agreement_profiles</c> hydration in <c>BuildPlanForLegacyCallersAsync</c>
    /// (ADR-017 D9c) and by the Phase-4 versioned-history sub-sprints. Null means
    /// the calling shape has no org context — D9c contract: no profile boundaries
    /// are hydrated for profile-less calls (test fixtures, internal use that does
    /// not bind employees to orgs). Existing pre-S21 callers default to null
    /// without breaking; Backend.Api / Payroll Integration loaders that pull the
    /// employee row populate it from <c>users.org_id</c>.
    /// </summary>
    public string? OrgId { get; init; }
}
