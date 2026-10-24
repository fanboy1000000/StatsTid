using Microsoft.AspNetCore.Http;
using StatsTid.Infrastructure.Security;
using StatsTid.Integrations.Payroll.Services;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Models;
using static StatsTid.Tests.Unit.Payroll.EmploymentWindowPcsFixture;

namespace StatsTid.Tests.Unit.Payroll;

/// <summary>
/// S146 / TASK-14604-T (QUAL-205; rulings R-21, R-22, R-27; AC-A1, AC-A3) — a calculation whose proof (its
/// segment manifest) was not stored is refused before any payroll write, through the ONE predicate both
/// calculating routes call: <see cref="PeriodCalculationService.PeriodCalculationOutcome.ThrowIfNotProvable"/>.
/// DB-free: the fixtures run on <see cref="InMemorySegmentManifestStore"/>, whose
/// <see cref="InMemorySegmentManifestStore.FailPersist"/> switch is the fault seam.
///
/// <para>
/// <b>Plain language.</b> Every payroll figure must be provable later from its stored manifest. If the manifest
/// could not be stored, the figure is right today but unprovable tomorrow, so payroll must not export it or
/// correct anyone's pay from it; the route answers "retry" (503) instead. Two outcomes are unprovable: the store
/// threw (<c>NotPersisted</c>), and a successful calculation that wrote no manifest at all
/// (<c>NoManifest</c> with success). Two outcomes are NOT this predicate's business and must pass: a complete
/// one, and a calculation the rule engine refused (it carries no figure; the routes answer it with their own
/// 422). The two "passes" facts keep the predicate from being "always throw".
/// </para>
///
/// <para>
/// <b>Where the "before any payroll write" half is pinned.</b> The routes' order (the predicate before the
/// export or the correction transaction) is a host fact; this file pins the predicate itself and the outcome the
/// persist failure produces, the two things a host fact cannot reach without a database:
/// <c>ManifestPersistenceHostTests</c> (Docker, CI-pending) drives the same refusal through the two routes.
/// </para>
/// </summary>
public sealed class ManifestPersistenceRefusalTests
{
    private const string EmployeeId = "EMP-S146-REFUSE";

    /// <summary>The discriminator of the 503 body, from the spec (refinement A, AC-A1).</summary>
    private const string ExpectedKind = "payroll-manifest-not-persisted";

    private static async Task<(PeriodCalculationService.PeriodCalculationOutcome Outcome, InMemorySegmentManifestStore Store)>
        CalculateAsync(
            InMemorySegmentManifestStore? store = null,
            bool emitAuditEvents = true,
            Func<RecordingRuleEngine.Call, HttpResponseMessage>? responder = null)
    {
        store ??= new InMemorySegmentManifestStore();
        var plan = Plan(EmployeeId, Mar01, Mar31, windows: null);
        var engine = new RecordingRuleEngine(responder);
        var pcs = BuildPcs(engine, store, hoursReader: WeekdayHours(EmployeeId, Mar01, Mar31));
        var outcome = await pcs.CalculateWithOutcomeAsync(
            plan, Profile(EmployeeId), emitAuditEvents: emitAuditEvents);
        return (outcome, store);
    }

    /// <summary>
    /// AC-A1, kind <b>N</b> (names <c>ThrowIfNotProvable</c> and <c>ManifestNotPersistedException</c>, which S146
    /// creates). The manifest's one transaction failed (the in-memory store throws): the calculation itself is
    /// valid (the rules succeeded) but PCS reports <c>NotPersisted</c>, nothing of the proof exists (not the
    /// manifest event, not the legacy event: R-18 by construction), and the predicate refuses with the manifest
    /// id of the plan and the fixed 503 discriminator.
    ///
    /// <para>
    /// <b>Red conditions (N).</b> Mutation MU-14604-A, in an isolated worktree: in
    /// <c>PeriodCalculationOutcome.ThrowIfNotProvable</c> delete the <c>NotPersisted</c> clause (the first
    /// <c>if</c>) and keep the API. The first <c>Assert.Throws</c> then fails with "Assert.Throws() Failure:
    /// No exception was thrown". The quoted failure is in the TASK-14604-T report.
    /// </para>
    /// </summary>
    [Fact]
    public async Task NotPersisted_RefusesBeforeAnyPayrollWrite()
    {
        var store = new InMemorySegmentManifestStore { FailPersist = true };

        var (outcome, _) = await CalculateAsync(store);

        // The store was really asked to persist, and it refused: this is the persist-failure path.
        Assert.Equal(1, store.PersistCalls);
        // The rule outputs are valid; only the proof is missing.
        Assert.True(outcome.Result.Success, outcome.Result.ErrorMessage);
        Assert.Equal(PeriodCalculationService.AuditState.NotPersisted, outcome.AuditState);
        // Nothing of the proof exists: neither the manifest event nor the legacy event (both or neither).
        Assert.Empty(store.All);
        Assert.Empty(store.Manifests);

        var refused = Assert.Throws<ManifestNotPersistedException>(() => outcome.ThrowIfNotProvable());

        Assert.Equal(outcome.ManifestId, refused.ManifestId);
        Assert.NotEqual(Guid.Empty, refused.ManifestId); // non-vacuity: the id is the plan's, not a default
        Assert.Equal(ExpectedKind, ManifestNotPersistedException.KindValue);
        // The fixed sentence names no employee, date or id (it reaches the HTTP response).
        Assert.DoesNotContain(EmployeeId, ManifestNotPersistedException.ErrorSentence);
        Assert.DoesNotContain(outcome.ManifestId.ToString(), ManifestNotPersistedException.ErrorSentence);
        Assert.DoesNotMatch(@"\d", ManifestNotPersistedException.ErrorSentence);
    }

    /// <summary>
    /// AC-A1, kind <b>N</b>. A SUCCESSFUL forward calculation that wrote no manifest at all (here
    /// <c>emitAuditEvents: false</c>, the only way a forward calculation reaches <c>NoManifest</c> with success)
    /// is as unprovable as one whose write failed: the predicate refuses it. The store was never asked to persist.
    ///
    /// <para>
    /// <b>Red conditions (N).</b> Mutation MU-14604-B, in an isolated worktree: in
    /// <c>ThrowIfNotProvable</c> delete the <c>AuditState == NoManifest &amp;&amp; Result.Success</c> clause (the
    /// second <c>if</c>). <c>Assert.Throws</c> then fails with "No exception was thrown". Quoted in the report.
    /// </para>
    /// </summary>
    [Fact]
    public async Task NoManifestWithSuccess_Refuses()
    {
        var (outcome, store) = await CalculateAsync(emitAuditEvents: false);

        Assert.Equal(0, store.PersistCalls);
        Assert.True(outcome.Result.Success, outcome.Result.ErrorMessage);
        Assert.Equal(PeriodCalculationService.AuditState.NoManifest, outcome.AuditState);

        var refused = Assert.Throws<ManifestNotPersistedException>(() => outcome.ThrowIfNotProvable());

        Assert.Equal(outcome.ManifestId, refused.ManifestId);
        Assert.NotEqual(Guid.Empty, refused.ManifestId);
    }

    /// <summary>
    /// Boundary pin, holds today: a COMPLETE outcome (the store committed both events) passes the predicate and
    /// both the manifest and the legacy event are in the store. Keeps <c>ThrowIfNotProvable</c> from being
    /// "always throw" and pins that the legacy event is persisted together with the manifest.
    /// </summary>
    [Fact]
    public async Task Complete_DoesNotRefuse_AndPersistsManifestAndLegacyEventTogether()
    {
        var (outcome, store) = await CalculateAsync();

        Assert.True(outcome.Result.Success, outcome.Result.ErrorMessage);
        Assert.Equal(PeriodCalculationService.AuditState.Complete, outcome.AuditState);
        Assert.Equal(1, store.PersistCalls);
        Assert.Single(store.All.OfType<SegmentManifestCreated>());
        Assert.Single(store.All.OfType<PeriodCalculationCompleted>());
        Assert.Equal(outcome.ManifestId, Assert.Single(store.Manifests.Keys));

        outcome.ThrowIfNotProvable(); // does not throw
    }

    /// <summary>
    /// Boundary pin, holds today: a calculation the rule engine refused (S145 R-6: <c>Success == false</c>,
    /// <c>NoManifest</c>) carries no figure and writes no proof by design; the predicate must let it through so the
    /// route answers its own 422, not the manifest 503. Keeps the success clause from dropping the
    /// <c>Result.Success</c> condition.
    /// </summary>
    [Fact]
    public async Task RefusedCalculation_NoManifestWithoutSuccess_DoesNotRefuse()
    {
        var (outcome, store) = await CalculateAsync(responder: RecordingRuleEngine.AllFail);

        Assert.False(outcome.Result.Success);
        Assert.Equal(PeriodCalculationService.AuditState.NoManifest, outcome.AuditState);
        Assert.Equal(0, store.PersistCalls);

        outcome.ThrowIfNotProvable(); // does not throw
    }

    /// <summary>
    /// TASK-14604's accepted behaviour change (Orchestrator addition 2): a calculation REFUSED by the engine path
    /// (S145 R-6, <c>Success == false</c>) no longer stamps its manifest id into the request audit context. Before
    /// S146 the stamp used a <c>!= Guid.Empty</c> guard, so a refused calculation wrote its plan's id into
    /// <c>audit_log.details</c>, an id that resolves in no store.
    ///
    /// <para>
    /// <b>Kind N (argued, and proven by mutation).</b> The behaviour (stamp only a Complete outcome) is
    /// the one S146 introduced into <c>StampAuditContext</c>. <b>Red conditions:</b> mutation MU-14604-C in an isolated
    /// worktree: restore the old guard in <c>StampAuditContext</c>, i.e. replace
    /// <c>outcome.AuditState != AuditState.Complete || outcome.ManifestId == Guid.Empty</c> by
    /// <c>outcome.ManifestId == Guid.Empty</c>. <c>Assert.False(items.ContainsKey(...))</c> then fails
    /// ("Assert.False() Failure"). Quoted in the report. (Placed in the Unit suite instead of the Docker host
    /// file: <c>StampAuditContext</c> is public static over an <c>HttpContext</c>, so no database is needed.)
    /// </para>
    /// </summary>
    [Fact]
    public async Task RefusedCalculation_StampsNoManifestId()
    {
        var (outcome, _) = await CalculateAsync(responder: RecordingRuleEngine.AllFail);
        Assert.False(outcome.Result.Success);
        Assert.NotEqual(Guid.Empty, outcome.ManifestId); // non-vacuity: there IS an id the old guard would stamp
        var http = new DefaultHttpContext();

        PeriodCalculationService.StampAuditContext(http, outcome);

        Assert.False(http.Items.ContainsKey(AuditLoggingMiddleware.ManifestIdItemKey));
    }

    /// <summary>
    /// Boundary pin, holds today (the other direction of the fact above): a COMPLETE outcome stamps exactly its
    /// manifest id, the ADR-016 D10 <c>segment_manifests</c> to <c>audit_log</c> linkage the middleware reads.
    /// Keeps the stamping from being removed outright.
    /// </summary>
    [Fact]
    public async Task CompleteCalculation_StampsItsManifestId()
    {
        var (outcome, _) = await CalculateAsync();
        Assert.Equal(PeriodCalculationService.AuditState.Complete, outcome.AuditState);
        var http = new DefaultHttpContext();

        PeriodCalculationService.StampAuditContext(http, outcome);

        Assert.Equal(outcome.ManifestId, http.Items[AuditLoggingMiddleware.ManifestIdItemKey]);
    }
}
