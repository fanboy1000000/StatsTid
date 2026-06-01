namespace StatsTid.SharedKernel.Models;

public sealed class User
{
    public required string UserId { get; init; }
    public required string Username { get; init; }
    public required string PasswordHash { get; init; }
    public required string DisplayName { get; init; }
    public string? Email { get; init; }
    public required string PrimaryOrgId { get; init; }
    public required string AgreementCode { get; init; }
    public required string OkVersion { get; init; }
    public string EmploymentCategory { get; init; } = "Standard";

    /// <summary>
    /// S59 / ADR-029 (amends ADR-025 D3) — GDPR-sensitive date of birth on the
    /// person record. NULLABLE: an unknown DOB ⇒ fail-closed for SENIOR_DAY age
    /// derivation (enforced in the Backend/rule engine, TASK-5907, not the schema).
    ///
    /// <para>
    /// <b>RBAC — NEVER leak.</b> <c>birth_date</c> is read-gated to
    /// <c>HROrAbove</c> + <c>OrgScopeValidator</c> and must NEVER be serialized into
    /// any Employee-facing DTO, JWT, or export. Only the derived integer age crosses
    /// the rule-engine boundary (TASK-5907); DOB itself stays Backend-local. The
    /// admin user-list / user-GET projections in <c>AdminEndpoints</c> deliberately
    /// do NOT include this field — only the dedicated HR-gated DOB endpoints
    /// (TASK-5906) expose it.
    /// </para>
    /// </summary>
    public DateOnly? BirthDate { get; init; }

    /// <summary>
    /// S60 / ADR-030 (amends ADR-029 read-model precedent) — HR-managed first day of
    /// employment, used to pro-rate the MONTHLY_ACCRUAL earned-to-date computation for
    /// mid-year hires (passed as a pure input to the rule engine's <c>earnedToDate</c>
    /// fn; null ⇒ accrue across the full ferieår). NULLABLE.
    ///
    /// <para>
    /// <b>RBAC — HR-scoped.</b> <c>employment_start_date</c> is set/read-gated to
    /// <c>HROrAbove</c> + <c>OrgScopeValidator</c> and must NEVER be serialized into
    /// any Employee-facing DTO, JWT, or export — same handling class as
    /// <see cref="BirthDate"/>. The admin user-list / user-GET projections in
    /// <c>AdminEndpoints</c> deliberately omit it; only the dedicated HR-gated
    /// employment-start endpoints (TASK-6006) expose it.
    /// </para>
    /// </summary>
    public DateOnly? EmploymentStartDate { get; init; }

    public bool IsActive { get; init; } = true;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;
}
