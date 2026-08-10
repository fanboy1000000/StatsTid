using System.Net;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StatsTid.Infrastructure.Outbox;
using StatsTid.Tests.Regression.Outbox;
using Xunit;

namespace StatsTid.Tests.Regression.Approval;

/// <summary>
/// S127 / TASK-12708 — AC-17: real-route forced-outbox-rollback for the send command, through BOTH
/// adapters (<c>POST /api/approval/send</c> and <c>POST /api/approval/{periodId}/employee-approve</c>).
///
/// <para>This REPLACES the inline-orchestration mirror the retired <c>Outbox/ApprovalAtomicTests</c>
/// <c>Submit_OutboxFails</c> / <c>EmployeeApprove_OutboxFails</c> tests used: those re-implemented the
/// endpoint's create+transition+audit+enqueue steps in the test body and stayed GREEN even after the
/// <c>/submit</c> endpoint was deleted and the by-id path was rewritten — the exact failure class this
/// sprint keeps hitting. Here the failure is forced through the REAL wire: a derived host whose
/// <see cref="IOutboxEnqueue"/> throws (<see cref="ForcedRollbackHarness.ThrowingOutboxEnqueue"/>) so
/// the outbox enqueue — the LAST in-tx step of <c>ExecuteSendAsync</c> — throws AFTER the row is
/// created/transitioned, the send is stamped, the deadlines are written and the audit row is inserted.
/// The unhandled throw before <c>tx.CommitAsync</c> rolls the whole transaction back.</para>
///
/// <para>FAILS IF the send is NOT atomic: any of the period status change, the SUBMITTED audit row, the
/// canonical event, the outbox row, or the audit-projection row would survive on a fresh connection.
/// If the endpoint were deleted, the route would 404/405 and these tests go RED — unlike the inline
/// mirror they replace.</para>
///
/// <para>Boot order (S63/S65 lesson): the shared fixture already booted the normal host, which
/// backfilled every init.sql user's profile + agreement-code row; the per-test employee is fully
/// seeded before the throwing host boots — so the derived host's startup seeders find nothing to
/// backfill and never invoke the throwing outbox at startup.</para>
/// </summary>
[Trait("Category", "Docker")]
[Collection("SendConcurrency")]
public sealed class SendAtomicityTests : SendConcurrencyTestBase
{
    public SendAtomicityTests(SendConcurrencyFixture fx) : base(fx) { }

    /// <summary>A derived host identical to the shared one EXCEPT its <see cref="IOutboxEnqueue"/>
    /// throws on every enqueue. Disposing it stops only this host; the fixture's container + normal
    /// factory are untouched.</summary>
    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> ThrowingOutboxFactory()
        => Fx.Factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IOutboxEnqueue>();
                services.AddSingleton<IOutboxEnqueue>(new ForcedRollbackHarness.ThrowingOutboxEnqueue());
            }));

    // ── Adapter 1: POST /api/approval/send — the CREATE arm ───────────────────────────────────────
    [Fact]
    public async Task Send_CreateArm_OutboxThrows_RollsBackWholeSend()
    {
        var emp = UniqueEmp("17s");
        await SeedEmployeeAsync(emp);
        await CoverMarchWithAbsencesAsync(emp); // pass coverage + allocation so the state change is reached

        using var factory = ThrowingOutboxFactory();
        _ = factory.CreateClient(); // boot the throwing host (seeders are no-ops — emp already seeded)
        var client = ClientFor(factory, emp, StatsTid.SharedKernel.Security.StatsTidRoles.Employee, Org,
            new StatsTid.SharedKernel.Security.RoleScope(
                StatsTid.SharedKernel.Security.StatsTidRoles.Employee, Org, "ORG_ONLY"));

        var rsp = await PostSendAsync(client, emp);
        Assert.Equal(HttpStatusCode.InternalServerError, rsp.StatusCode); // the throw escaped, no commit

        // The whole send rolled back: no row was created, no audit, no event, no outbox, no projection.
        Assert.Equal(0L, await CountApprovalRowsAsync(emp));
        // F7 — the approval_audit orphan check: the SUBMITTED audit row is written just before the outbox
        // throws, and approval_audit has no FK on period_id, so a self-managed audit write would survive
        // the rollback. Keyed on the self-send actor because the create-arm period id rolled back with it.
        Assert.Equal(0L, await CountApprovalAuditByActorAsync(emp));
        await ForcedRollbackHarness.AssertNoOutboxRowAsync(Fx.ConnectionString, SendStreamId(emp));
        await ForcedRollbackHarness.AssertNoEventRowAsync(Fx.ConnectionString, SendStreamId(emp));
        Assert.Equal(0L, await CountAuditProjectionByEmployeeAsync(emp));
    }

    // ── Adapter 2: POST /api/approval/{periodId}/employee-approve — the TRANSITION arm ─────────────
    [Fact]
    public async Task EmployeeApprove_ByIdAdapter_OutboxThrows_RollsBackWholeSend()
    {
        var emp = UniqueEmp("17e");
        await SeedEmployeeAsync(emp);
        await CoverMarchWithAbsencesAsync(emp);
        var periodId = await SeedApprovalRowAsync(emp, "DRAFT", MarchStart, MarchEnd);

        using var factory = ThrowingOutboxFactory();
        _ = factory.CreateClient();
        var client = ClientFor(factory, emp, StatsTid.SharedKernel.Security.StatsTidRoles.Employee, Org,
            new StatsTid.SharedKernel.Security.RoleScope(
                StatsTid.SharedKernel.Security.StatsTidRoles.Employee, Org, "ORG_ONLY"));

        var rsp = await PostEmployeeApproveAsync(client, periodId);
        Assert.Equal(HttpStatusCode.InternalServerError, rsp.StatusCode);

        // The transition + stamp + audit all rolled back: the row is still DRAFT with no send stamp,
        // and no SUBMITTED audit, event, outbox, or projection row exists.
        var row = await ReadRowAsync(periodId);
        Assert.NotNull(row);
        Assert.Equal("DRAFT", row!.Value.Status);
        Assert.Null(row.Value.EmployeeApprovedAt);
        Assert.Equal(0L, await CountApprovalAuditAsync(periodId, "SUBMITTED"));
        await ForcedRollbackHarness.AssertNoOutboxRowAsync(Fx.ConnectionString, SendStreamId(emp));
        await ForcedRollbackHarness.AssertNoEventRowAsync(Fx.ConnectionString, SendStreamId(emp));
        Assert.Equal(0L, await CountAuditProjectionByPeriodAsync(periodId));
    }
}
