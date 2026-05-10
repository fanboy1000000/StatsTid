using StatsTid.SharedKernel.Models;

namespace StatsTid.SharedKernel.Segmentation;

/// <summary>
/// Seam for non-rule consumers to register <see cref="SnapshotContract"/>-equivalent
/// entries at planner time (ADR-020 D1).
///
/// <para>
/// Today (S29 binding), <c>PeriodPlanner.HasAnySnapshotContract</c> gates
/// <see cref="SegmentSnapshot"/> creation on at least one rule declaring a
/// <see cref="SnapshotContract"/>. Phase 4d-1 needs the manifest to carry replay-stable
/// inputs (the wage-type-mapping natural-key triple
/// <c>(OkVersion, AgreementCode, Position)</c>) that are <strong>not rule-driven</strong> —
/// they're payroll-export concerns. Rather than pollute <c>RuleRegistry</c> with a sentinel
/// rule, non-rule consumers register through this seam.
/// </para>
///
/// <para>
/// <strong>API contract:</strong> a registered hydrator runs once per segment when
/// <c>PeriodPlanner.Plan</c> materializes a <see cref="SegmentSnapshot"/>. The returned
/// value lands at <see cref="SegmentSnapshot.Values"/><c>[contractKey]</c> alongside
/// rule-declared non-dated source values. Test call-sites that pass
/// <c>profile = null</c> bypass enrollment by design (ADR-020 D1.5 forward-compat).
/// </para>
/// </summary>
public interface IPlannerEnrollment
{
    /// <summary>
    /// Register a non-rule snapshot contract. The hydrator receives the planner's
    /// <see cref="EmploymentProfile"/> and returns the value stored at
    /// <see cref="SegmentSnapshot.Values"/><c>[contractKey]</c>.
    /// </summary>
    void RegisterSnapshotContract(string contractKey, Func<EmploymentProfile, object?> hydrator);

    /// <summary>
    /// Snapshot of all registered enrollments at the moment of planning. The planner
    /// iterates this list per segment to invoke each hydrator.
    /// </summary>
    IReadOnlyList<(string ContractKey, Func<EmploymentProfile, object?> Hydrator)> GetEnrollments();
}

/// <summary>
/// Default in-memory <see cref="IPlannerEnrollment"/> backed by a list. Constructed
/// per-call by the production call-site (PCS L585 today); registrations are scoped to
/// a single <c>PeriodPlanner.Plan</c> invocation.
/// </summary>
public sealed class PlannerEnrollment : IPlannerEnrollment
{
    private readonly List<(string ContractKey, Func<EmploymentProfile, object?> Hydrator)> _entries = new();

    public void RegisterSnapshotContract(string contractKey, Func<EmploymentProfile, object?> hydrator)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contractKey);
        ArgumentNullException.ThrowIfNull(hydrator);
        _entries.Add((contractKey, hydrator));
    }

    public IReadOnlyList<(string ContractKey, Func<EmploymentProfile, object?> Hydrator)> GetEnrollments()
        => _entries;
}
