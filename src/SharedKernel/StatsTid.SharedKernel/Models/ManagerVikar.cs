namespace StatsTid.SharedKernel.Models;

/// <summary>
/// S74 / ADR-027 Phase 5 (SPRINT-74 R2). An approver-owned vikar (stand-in approver)
/// row in <c>manager_vikar</c> — the go-forward storage for self-service delegation,
/// REPLACING the per-report <c>SELF_DELEGATION</c> ACTING fan-out in
/// <c>reporting_lines</c>.
///
/// <para>
/// The vikar covers the <see cref="AbsentApproverId"/>'s CURRENT + FUTURE PRIMARY
/// reports automatically — the resolver (ADR-027 D5) consults the active row at
/// approval-routing time. <see cref="UntilDate"/> is INCLUSIVE ("til og med"): the
/// vikar is effective THROUGH that date; expiry closes it the day AFTER (R4a).
/// <see cref="EffectiveTo"/> is the close marker (NULL = active); the DB partial-unique
/// <c>uq_manager_vikar_active</c> guarantees AT MOST ONE active vikar per absent approver.
/// </para>
/// </summary>
public sealed class ManagerVikar
{
    public required Guid VikarId { get; init; }
    public required string AbsentApproverId { get; init; }
    public required string VikarUserId { get; init; }
    public required DateOnly UntilDate { get; init; }
    public required string Reason { get; init; }
    public required string TreeRootOrgId { get; init; }
    public required long Version { get; init; }
    public required string CreatedBy { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateOnly? EffectiveTo { get; init; }
}
