using System.Net;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Security;
using Xunit;
using static StatsTid.Tests.Regression.Leaver.LeaverHrOnly;

namespace StatsTid.Tests.Regression.Leaver;

/// <summary>
/// S147 / TASK-14713-T (AC-230-7; refinement R-7, R-15) — once the door lets HR in, the work behind the door must not fall over for a
/// switched-off leaver, and must file its audit record under the right organisation.
///
/// <para><b>Plain language.</b> Approving, rejecting or reopening a month does three things after the permission check: it takes a lock on
/// the employee's reporting tree (so two people cannot rearrange the tree and approve at the same moment), it moves the month, and it writes
/// an audit record that says which organisation the action concerned. All three used to look the employee up in the list of ACTIVE people
/// only, so for a switched-off leaver the lookup came back empty and the request ended in a server error. TASK-14713 gives these three
/// handlers their own lock routine and audit lookup that can see a switched-off employee, and leaves the shared lock routine (used by the six
/// reporting-line writers, which must keep refusing a leaver) as it was. These facts prove: no server error; the audit record names the
/// LEAVER's organisation, not the acting HR person's; the new lock routine takes the very same lock as the shared one; and the shared routine
/// still refuses a switched-off employee.</para>
///
/// <para><b>Fixture.</b> The leaver is switched off directly on the Backend host without the leaver job, clock pinned to D+6. Docker-gated:
/// <b>CI-pending</b>, never locally green.</para>
/// </summary>
[Trait("Category", "Docker")]
[Collection("LeaverHrOnly")]
public sealed class LeaverTreeLockAndAuditTests : LeaverHrOnlyTestBase
{
    public LeaverTreeLockAndAuditTests(LeaverHrOnlyFixture fx) : base(fx) { }

    /// <summary>
    /// AC-230-7. Kind <b>F</b>. HR approves, rejects and reopens a switched-off leaver's EMPLOYEE_APPROVED March: each answers 200 (not a
    /// server error from the lock or the audit read), the month moves, and the audit_projection row for the event names the LEAVER's
    /// organisation (STY02) as its target. The acting HR person's token carries a DIFFERENT primary organisation (STY01) while its scope
    /// covers STY02, so a target taken from the actor would read STY01: the fact discriminates "the subject's organisation" from "the actor's".
    ///
    /// <para><b>Red conditions.</b> Today the active-only validator answers HR 403, so the 200 assertion fails. Behind the gate, the
    /// pre-TASK-14713 handlers would still 500: the shared lock throws for an inactive employee, and the active-only audit read returns null
    /// and throws. Mutation (argued, Docker-only): the audit read left active-only throws "Audit projection: employee ... not found" and
    /// answers 500; reading the actor's organisation as the target fails the <c>Assert.Equal("STY02", ...)</c>.</para>
    /// </summary>
    [Theory]
    [InlineData("approve", "PeriodApproved")]
    [InlineData("reject", "PeriodRejected")]
    [InlineData("reopen", "PeriodReopened")]
    public async Task HrActsOnASwitchedOffLeaver_NoServerError_AuditTargetOrganisationIsTheSubjects(string action, string auditEventType)
    {
        var leaver = await SeedLeaverAsync(Cs, $"tl_{action}");
        var periodId = await SeedPeriodAsync(Cs, leaver, "EMPLOYEE_APPROVED");
        var hr = NewId("tl_hr");
        var token = Token(hr, StatsTidRoles.LocalHR, OtherOrg, new RoleScope(StatsTidRoles.LocalHR, HkOrg, "ORG_ONLY"));

        using var client = ClientAt(Day(6), token);
        var rsp = await ActAsync(action, client, leaver, periodId, 3);

        await AssertStatusAsync(rsp, HttpStatusCode.OK, $"HR's {action} of a switched-off leaver's month");
        Assert.Equal(StatusAfter(action), (await ReadPeriodAsync(Cs, periodId)).Status);

        await using var conn = new NpgsqlConnection(Cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT target_org_id, actor_primary_org_id FROM audit_projection WHERE target_resource_id = @id AND event_type = @t", conn);
        cmd.Parameters.AddWithValue("id", periodId.ToString());
        cmd.Parameters.AddWithValue("t", auditEventType);
        await using var r = await cmd.ExecuteReaderAsync();
        Assert.True(await r.ReadAsync(), $"{auditEventType} must have exactly one audit_projection row for the month");
        Assert.Equal(HkOrg, r.GetString(0));          // the SUBJECT's organisation
        Assert.Equal(OtherOrg, r.GetString(1));     // control: the actor's primary organisation is a different one
        Assert.False(await r.ReadAsync(), "exactly one audit_projection row");
    }

    /// <summary>
    /// AC-230-7 / R-7, the new tree-lock routine. Kind <b>N</b> (names <c>AcquireTreeLockForEmployeeIncludingTerminatedAsync</c>, which this
    /// sprint creates). Three parts. (1) For a switched-off employee in STY02 the routine returns STY02 (no exception). (2) It takes the SAME
    /// lock as every other tree-lock caller: while a second transaction holds the reporting-tree lock for STY02, the routine's own transaction
    /// (with a 750 ms lock timeout) fails with lock_not_available (55P03). (3) Control: while the lock is held for a DIFFERENT organisation
    /// (STY01) the routine is not blocked and returns STY02, so the 55P03 in part 2 is about the shared key, not about the routine locking
    /// everything.
    ///
    /// <para><b>Red conditions (argued; Docker-only).</b> The fact does not compile without the routine (N), so the RED is a named MUTATION,
    /// not a compile error. M-nolock: the routine does not take the advisory (or takes a different key) — part 2 sees no block and
    /// <c>Assert.Equal("55P03", ...)</c> fails (the <c>ThrowsAsync</c> finds no exception). M-active: the routine keeps the active-only filter —
    /// part 1 throws InvalidOperationException ("not found, or home Organisation inactive").</para>
    /// </summary>
    [Fact]
    public async Task TreeLockOverload_ResolvesASwitchedOffEmployee_AndTakesTheSameLockAsTheOthers()
    {
        var leaver = await SeedLeaverAsync(Cs, "tl_lock");
        var repo = new ReportingLineRepository(new DbConnectionFactory(Cs));

        // (1) resolves a switched-off employee to their organisation.
        await using (var conn = new NpgsqlConnection(Cs))
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            Assert.Equal(HkOrg, await repo.AcquireTreeLockForEmployeeIncludingTerminatedAsync(conn, tx, leaver));
        }

        // (2) the same advisory key: blocked while another transaction holds the STY02 reporting-tree lock.
        await using (var holder = new NpgsqlConnection(Cs))
        {
            await holder.OpenAsync();
            await using var holderTx = await holder.BeginTransactionAsync();
            await ReportingLineRepository.AcquireTreeLockAsync(holder, holderTx, HkOrg);

            await using var conn = new NpgsqlConnection(Cs);
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            await using (var timeout = new NpgsqlCommand("SET LOCAL lock_timeout = '750ms'", conn, tx))
                await timeout.ExecuteNonQueryAsync();
            var blocked = await Assert.ThrowsAsync<PostgresException>(
                () => repo.AcquireTreeLockForEmployeeIncludingTerminatedAsync(conn, tx, leaver));
            Assert.Equal(PostgresErrorCodes.LockNotAvailable, blocked.SqlState);
        }

        // (3) control: a lock held for a DIFFERENT organisation does not block it.
        await using (var holder = new NpgsqlConnection(Cs))
        {
            await holder.OpenAsync();
            await using var holderTx = await holder.BeginTransactionAsync();
            await ReportingLineRepository.AcquireTreeLockAsync(holder, holderTx, OtherOrg);

            await using var conn = new NpgsqlConnection(Cs);
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            await using (var timeout = new NpgsqlCommand("SET LOCAL lock_timeout = '750ms'", conn, tx))
                await timeout.ExecuteNonQueryAsync();
            Assert.Equal(HkOrg, await repo.AcquireTreeLockForEmployeeIncludingTerminatedAsync(conn, tx, leaver));
        }
    }

    /// <summary>
    /// AC-230-7 / R-7, the shared routine is unchanged. Kind <b>R</b> (regression pin — holds today): the shared
    /// <c>AcquireTreeLockForEmployeeAsync</c>, which the six reporting-line writers call, STILL refuses a switched-off employee
    /// (InvalidOperationException), so nobody can rearrange a leaver's reporting line through it. Guards against the new routine being "fixed"
    /// by widening the shared one.
    ///
    /// <para><b>Red conditions (argued).</b> Mutation: the active filter dropped from the shared derivation makes the call return STY02 and the
    /// <c>ThrowsAsync</c> fail.</para>
    /// </summary>
    [Fact]
    public async Task SharedTreeLock_StillRefusesASwitchedOffEmployee()
    {
        var leaver = await SeedLeaverAsync(Cs, "tl_shared");
        var repo = new ReportingLineRepository(new DbConnectionFactory(Cs));

        await using var conn = new NpgsqlConnection(Cs);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repo.AcquireTreeLockForEmployeeAsync(conn, tx, leaver));
    }
}
