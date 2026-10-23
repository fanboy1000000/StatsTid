using StatsTid.Integrations.Payroll.Services;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Interfaces;
using StatsTid.SharedKernel.Segmentation;

namespace StatsTid.Tests.Unit.Payroll;

/// <summary>
/// S146 / TASK-14604-T (QUAL-205; rulings R-21, R-22) — the in-memory <see cref="ISegmentManifestStore"/> every
/// DB-free Payroll fixture builds <see cref="PeriodCalculationService"/> with. It replaces the nested
/// <c>EmploymentWindowPcsFixture.InMemoryEventStore</c>, which was PCS's <c>IEventStore</c> before the manifest
/// moved behind the seam.
///
/// <para>
/// <b>Plain language.</b> Production writes a calculation's proof (the manifest, and the legacy period summary
/// event) in one database transaction. A unit fixture has no database, so this store keeps what PCS handed it:
/// <see cref="Manifest"/> and <see cref="All"/> expose it exactly as the old in-memory event store did, and
/// <see cref="TryLoadAsync"/> gives a replay the manifest back exactly as it was persisted (including the frozen
/// <see cref="PlanInputs"/>), the way the Postgres store's projection read does.
/// </para>
///
/// <para>
/// <b>The fault seam.</b> <see cref="FailPersist"/> makes <see cref="PersistAsync"/> throw before it keeps
/// anything, which is the "the one transaction did not commit" shape: PCS turns it into
/// <see cref="PeriodCalculationService.AuditState.NotPersisted"/> and the routes refuse. Persisting is
/// all-or-nothing here as in production: a failing persist leaves <see cref="All"/> empty.
/// </para>
/// </summary>
internal sealed class InMemorySegmentManifestStore : ISegmentManifestStore
{
    private readonly List<IDomainEvent> _all = new();
    private readonly Dictionary<Guid, SegmentManifest> _manifests = new();

    /// <summary>When true, <see cref="PersistAsync"/> throws and keeps nothing.</summary>
    public bool FailPersist { get; set; }

    /// <summary>How many times PCS asked to persist, successful or not.</summary>
    public int PersistCalls { get; private set; }

    /// <summary>The persisted events in order: each calculation's manifest event, then its legacy event.</summary>
    public IReadOnlyList<IDomainEvent> All => _all;

    /// <summary>The single persisted manifest event, or null when none was persisted.</summary>
    public SegmentManifestCreated? Manifest => _all.OfType<SegmentManifestCreated>().SingleOrDefault();

    /// <summary>The persisted projection-shaped manifests by id.</summary>
    public IReadOnlyDictionary<Guid, SegmentManifest> Manifests => _manifests;

    public Task PersistAsync(
        SegmentManifestCreated manifestEvent,
        SegmentManifest manifest,
        PeriodCalculationCompleted legacyEvent,
        CancellationToken ct = default)
    {
        PersistCalls++;
        if (FailPersist)
            throw new InvalidOperationException("InMemorySegmentManifestStore.FailPersist: the manifest transaction did not commit.");

        _all.Add(manifestEvent);
        _manifests[manifest.ManifestId] = manifest;
        _all.Add(legacyEvent);
        return Task.CompletedTask;
    }

    public Task<SegmentManifest?> TryLoadAsync(Guid manifestId, CancellationToken ct = default)
        => Task.FromResult(_manifests.TryGetValue(manifestId, out var manifest) ? manifest : null);
}
