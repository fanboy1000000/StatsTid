namespace StatsTid.SharedKernel.Models;

/// <summary>
/// S34 / TASK-3402 — versioned-history row for the per-user agreement-code assignment
/// (Phase 4e, ADR-023 D2 option (b)). Mirrors the bitemporal shape used by
/// <see cref="WageTypeMapping"/> / <see cref="EntitlementConfig"/> /
/// <see cref="EmploymentProfile"/>: each row carries <see cref="EffectiveFrom"/> +
/// <see cref="EffectiveTo"/> (end-exclusive per ADR-018 D9), a monotonic
/// <see cref="Version"/> column for ADR-019 admin-strict If-Match optimistic concurrency,
/// and the natural key <see cref="UserId"/> on which the partial-unique-index
/// <c>WHERE effective_to IS NULL</c> enforces "at most one live row per user".
///
/// <para>
/// <b>Canonical source contract.</b> Past-period readers (PCS planner snapshot resolution,
/// payroll export effective-date lookup) MUST route through
/// <c>UserAgreementCodeRepository.GetByUserIdAtAsync</c>; live-only consumers (JWT mint
/// at sign-in, Skema/Overtime/Compliance endpoint reads of "today's" agreement) MAY read
/// the denormalized <c>users.agreement_code</c> cache. The cache is written in the same
/// atomic tx as the repository call by the admin endpoint (TASK-3407).
/// </para>
/// </summary>
public sealed record UserAgreementCode
{
    /// <summary>Surrogate row key (UUID PRIMARY KEY). Generated client-side per S29 WTM /
    /// S33 EmployeeProfile precedent so the endpoint can include it in outbox event bodies.</summary>
    public required Guid AssignmentId { get; init; }

    /// <summary>Natural key — the <c>user_id</c> this assignment belongs to. Foreign key to
    /// <c>users.user_id</c>.</summary>
    public required string UserId { get; init; }

    /// <summary>The Danish agreement code in effect for the user across this row's history
    /// window (e.g. "AC", "HK", "PROSA"). Drives rule-engine routing and payroll export
    /// wage-type-mapping lookup.</summary>
    public required string AgreementCode { get; init; }

    /// <summary>Inclusive start of this row's history window (ADR-018 D9 end-exclusive
    /// <c>[from, to)</c> semantic).</summary>
    public required DateOnly EffectiveFrom { get; init; }

    /// <summary>Exclusive end of this row's history window; <c>null</c> means this is the
    /// currently-live row (partial-unique-index <c>idx_user_agreement_codes_live</c>
    /// guarantees at most one such row per user).</summary>
    public DateOnly? EffectiveTo { get; init; }

    /// <summary>Monotonic optimistic-concurrency token (ADR-019 admin-strict If-Match).
    /// Case A (Created) starts at 1; Case B (Updated) bumps by 1; Case C (Superseded) sets
    /// the successor to <c>predecessor.Version + 1</c> per S33 Step 7a P1 ETag-monotonicity
    /// refinement so racing admins holding stale If-Match values surface a 412 instead of
    /// silently overwriting the new row.</summary>
    public required long Version { get; init; }
}
