using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Segmentation;

namespace StatsTid.Integrations.Payroll.Services;

/// <summary>
/// S146 / TASK-14604 (QUAL-205; rulings R-17, R-21, R-22) — where a calculation's proof is kept: the
/// segment manifest (ADR-016 D10), written once, all or nothing, and read back for a replay.
///
/// <para>
/// <b>Why it exists (plain language).</b> Every payroll figure must be provable later from its manifest: the
/// record of how the month was split and which inputs it ran on. Before S146 the manifest was written in two
/// separate steps on two connections (an event-store append, then a projection row), and a failure of both
/// left a calculation that still exported or corrected, pointing at a manifest that existed nowhere. This
/// seam has one write, which either commits everything that proves the calculation or throws, so the
/// calculation can be refused instead (<see cref="PeriodCalculationService.PeriodCalculationOutcome.ThrowIfNotProvable"/>).
/// </para>
///
/// <para>
/// <b>Why a seam and not the outbox interface directly (R-21 (c)).</b> The manifest has a write path and a
/// load path, and both must work in the calculation fixtures that run without a database. Production
/// registers <see cref="PostgresSegmentManifestStore"/>; a fixture uses an in-memory store that holds what it
/// persisted. Payroll-local: no other bounded context writes or reads manifests.
/// </para>
/// </summary>
public interface ISegmentManifestStore
{
    /// <summary>
    /// Persists a successful calculation's proof in ONE transaction: the <see cref="SegmentManifestCreated"/>
    /// event (enqueued through the ADR-018 outbox on <c>segment-manifest-{manifestId}</c>), the
    /// <c>segment_manifests</c> projection row of <paramref name="manifest"/>, and the legacy
    /// <see cref="PeriodCalculationCompleted"/> summary event (enqueued on
    /// <c>period-calc-{employeeId}-{periodStart:yyyy-MM-dd}</c>). It returns only when all three are
    /// committed; any failure throws and leaves none of them (R-18 by construction: both events or neither).
    /// </summary>
    /// <param name="manifestEvent">The manifest event; its <c>ManifestId</c> must equal
    /// <paramref name="manifest"/>'s.</param>
    /// <param name="manifest">The manifest the projection row is written from.</param>
    /// <param name="legacyEvent">The legacy period summary event; its stream is derived from its own
    /// employee id and period start.</param>
    /// <param name="ct">Cancellation.</param>
    Task PersistAsync(
        SegmentManifestCreated manifestEvent,
        SegmentManifest manifest,
        PeriodCalculationCompleted legacyEvent,
        CancellationToken ct = default);

    /// <summary>
    /// Loads a persisted manifest for a replay: the <c>segment_manifests</c> projection first, then the
    /// event store as fallback; <c>null</c> when neither has it.
    ///
    /// <para>
    /// <b>A found row is never substituted (S145 ruling R-17, unchanged).</b> The event fallback covers only
    /// "the projection could not be consulted" and "no row". A FOUND row whose stored inputs cannot be decoded
    /// is refused, never replaced by the event: an undecodable <c>plan_inputs_jsonb</c> throws
    /// <see cref="ReplayRefusedException"/> (<see cref="ReplayRefusedException.ManifestWithoutFrozenInputs"/>),
    /// an undecodable <c>segments_jsonb</c> throws <see cref="InvalidOperationException"/>, and a NULL
    /// <c>plan_inputs_jsonb</c> comes back as a manifest without frozen inputs, which the replay refuses by the
    /// same name (R-14 vii). Cancellation propagates; it is never reported as "not found".
    /// </para>
    /// </summary>
    /// <param name="manifestId">The manifest to load.</param>
    /// <param name="ct">Cancellation.</param>
    Task<SegmentManifest?> TryLoadAsync(Guid manifestId, CancellationToken ct = default);
}
