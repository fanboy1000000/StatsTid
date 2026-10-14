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
/// <strong>API contract:</strong> a registered hydrator runs once per EMPLOYED segment when
/// <c>PeriodPlanner.Plan</c> materializes that segment's <see cref="SegmentSnapshot"/>, and
/// receives the segment's start date. The returned value lands at
/// <see cref="SegmentSnapshot.Values"/><c>[contractKey]</c> alongside rule-declared non-dated
/// source values. A NOT_EMPLOYED segment runs no hydrator and carries no enrollment key: it
/// maps nothing. Test call-sites that pass <c>profile = null</c> bypass enrollment by design
/// (ADR-020 D1.5 forward-compat).
/// </para>
///
/// <para>
/// <strong>Per-segment, dated (S145 / TASK-14512, refinement A4 — QUAL-150).</strong> Until S145
/// a hydrator received only the planner's profile, so every segment of a plan carried ONE value
/// hydrated from the request (ADR-020 D1.5, "uniform per plan"). The two-argument overload
/// receives the segment start as well, so a caller can hand in the employee's dated history as
/// data and return the value in force on that day; the planner stays pure. The one-argument
/// overload is kept for callers whose value does not vary by date: it is the two-argument form
/// that ignores the date.
/// </para>
/// </summary>
public interface IPlannerEnrollment
{
    /// <summary>
    /// Register a non-rule snapshot contract whose value does not depend on the segment's date.
    /// The hydrator receives the planner's <see cref="EmploymentProfile"/> and returns the value
    /// stored at <see cref="SegmentSnapshot.Values"/><c>[contractKey]</c>.
    /// </summary>
    void RegisterSnapshotContract(string contractKey, Func<EmploymentProfile, object?> hydrator);

    /// <summary>
    /// S145 / TASK-14512 (A4) — register a DATED non-rule snapshot contract. The hydrator receives
    /// the planner's <see cref="EmploymentProfile"/> and the EMPLOYED segment's start date, and
    /// returns the value in force on that date (for the wage-type key: the dated record's OK
    /// version, agreement code and position).
    /// </summary>
    void RegisterSnapshotContract(string contractKey, Func<EmploymentProfile, DateOnly, object?> hydrator);

    /// <summary>
    /// Snapshot of all registered enrollments at the moment of planning, each in the dated form
    /// (a one-argument registration is adapted to ignore the date). The planner iterates this list
    /// per EMPLOYED segment to invoke each hydrator with that segment's start.
    /// </summary>
    IReadOnlyList<(string ContractKey, Func<EmploymentProfile, DateOnly, object?> Hydrator)> GetEnrollments();
}

/// <summary>
/// Default in-memory <see cref="IPlannerEnrollment"/> backed by a list. Constructed
/// per-call by the production call-site (PCS planning); registrations are scoped to
/// a single <c>PeriodPlanner.Plan</c> invocation.
/// </summary>
public sealed class PlannerEnrollment : IPlannerEnrollment
{
    private readonly List<(string ContractKey, Func<EmploymentProfile, DateOnly, object?> Hydrator)> _entries = new();

    public void RegisterSnapshotContract(string contractKey, Func<EmploymentProfile, object?> hydrator)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contractKey);
        ArgumentNullException.ThrowIfNull(hydrator);
        RegisterSnapshotContract(contractKey, (profile, _) => hydrator(profile));
    }

    public void RegisterSnapshotContract(string contractKey, Func<EmploymentProfile, DateOnly, object?> hydrator)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contractKey);
        ArgumentNullException.ThrowIfNull(hydrator);
        _entries.Add((contractKey, hydrator));
    }

    public IReadOnlyList<(string ContractKey, Func<EmploymentProfile, DateOnly, object?> Hydrator)> GetEnrollments()
        => _entries;
}
