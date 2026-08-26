using System.Data;
using System.Net;
using Npgsql;
using StatsTid.Backend.Api.Services;
using StatsTid.Tests.Regression.Approval;
using Xunit;

namespace StatsTid.Tests.Regression.Security;

/// <summary>
/// S136 / TASK-13603 — THE RACE PIN for the employment-window LOCK REGIME (ADR-040 D3;
/// refinement Codex-B1): a window EDIT and a REGISTRATION for the same employee must serialize
/// on <see cref="EmployeeConsumptionLock"/>, and the registration's window check must be the
/// IN-LOCK AUTHORITATIVE read — otherwise D3's "airtight in both directions" premise breaks:
/// a registration racing a window-narrowing edit could commit out-of-window data that the
/// edit's strand guard, running concurrently, never saw.
///
/// <para><b>Test shape</b> — the same advisory-then-authoritative choreography as the S128
/// send-wins-then-POST pin (<c>SendConcurrencyTests.S128_SendWinsThenTimeEntry_...</c>), with
/// the window edit in the send's seat: a blocker connection takes the employee's advisory lock
/// and — INSIDE that lock-holding transaction, exactly like the production employment-date PUT,
/// which acquires this same lock as its first in-tx statement (the lock regime) — narrows
/// <c>employment_end_date</c> to BEFORE the registration date, uncommitted. The real
/// registration request is fired, proven to QUEUE on the lock via <c>pg_locks</c> (the
/// contention barrier — <c>Task.WhenAll</c> is not proof of overlap), and nothing commits
/// inside the window. The blocker then commits; the registration acquires, re-reads the window
/// IN-LOCK (the <c>(conn, tx)</c> resolver overload under the ReadCommitted pin) and must
/// refuse with the shared date-free 422.</para>
///
/// <para><b>FAILS IF</b> the gate stops being in-lock authoritative: a pre-lock check, a
/// self-managed (own-connection) resolver read, or a RepeatableRead regression would all
/// observe the PRE-EDIT window (NULL end ⇒ EMPLOYED — the blocker's UPDATE is uncommitted
/// while any pre-lock read runs) and 201/200 a row into the narrowed window — caught by the
/// final status + zero-row asserts. If the writer stops enrolling in the lock entirely,
/// <c>WaitForWaitersAsync</c> times out (fails loud).</para>
/// </summary>
[Trait("Category", "Docker")]
[Collection("SendConcurrency")]
public sealed class EmploymentWindowRaceTests : SendConcurrencyTestBase
{
    public EmploymentWindowRaceTests(SendConcurrencyFixture fx) : base(fx) { }

    /// <summary>2026-03-12, a Thursday inside March 2026 — AFTER the narrowed end (03-10).</summary>
    private static readonly DateOnly RegistrationDay = new(2026, 3, 12);
    private static readonly DateOnly NarrowedEnd = new(2026, 3, 10);

    /// <summary>The window edit's in-tx write, verbatim shape of the production end-date PUT's
    /// effect (rides the BLOCKER's lock-holding transaction — uncommitted until the test says).</summary>
    private static async Task NarrowEndDateInTxAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string employeeId, DateOnly endDate)
    {
        await using var cmd = new NpgsqlCommand(
            "UPDATE users SET employment_end_date = @e, updated_at = NOW() WHERE user_id = @id",
            conn, tx);
        cmd.Parameters.AddWithValue("e", endDate);
        cmd.Parameters.AddWithValue("id", employeeId);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
    }

    /// <summary>The deactivation's in-tx write, verbatim effect-shape of the production
    /// deactivation writers (the end-date PUT's R1(a) flip / the Step-A settlement flip), which
    /// commit under this same lock (rides the BLOCKER's lock-holding transaction).</summary>
    private static async Task DeactivateInTxAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string employeeId)
    {
        await using var cmd = new NpgsqlCommand(
            "UPDATE users SET is_active = FALSE, updated_at = NOW() WHERE user_id = @id",
            conn, tx);
        cmd.Parameters.AddWithValue("id", employeeId);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
    }

    // ── Writer #1: POST /api/time-entries ─────────────────────────────────────────────────────

    [Fact]
    public async Task WindowEditWins_ThenTimeEntry_ObservesNarrowedWindowInLock_And422s()
    {
        var emp = UniqueEmp("wed");
        await SeedEmployeeAsync(emp); // NULL employment dates — unbounded until the edit lands
        var (classId, objId) = await AdvisoryKeyPartsAsync(emp);

        // The "window edit" holds the lock and narrows the end date, uncommitted — the exact
        // in-lock position the production employment-date PUT gives this write.
        await using var blockerConn = Fx.Db.Create();
        await blockerConn.OpenAsync();
        await using var blockerTx = await blockerConn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        await EmployeeConsumptionLock.AcquireAsync(blockerConn, blockerTx, emp);
        await NarrowEndDateInTxAsync(blockerConn, blockerTx, emp, NarrowedEnd);

        // Fire the REAL registration — its pre-lock reads (the terminated-inclusive subject
        // read) see the pre-edit row (MVCC), so only the in-lock gate can catch the edit.
        var teClient = EmployeeClient(emp);
        var timeEntry = Task.Run(() => PostTimeEntryAsync(teClient, emp, RegistrationDay, 7.4m));

        // Prove genuine contention: the POST is QUEUED on our advisory key…
        await WaitForWaitersAsync(classId, objId, expected: 1);
        // …and nothing has committed while the edit holds the window.
        Assert.Equal(0L, await CountTimeEntryRowsAsync(emp, RegistrationDay));

        // The edit commits; the POST acquires and must now see end=03-10 < 03-12 in-lock.
        await blockerTx.CommitAsync();

        var rsp = await timeEntry;
        var body = await BodyAsync(rsp);
        Assert.True(rsp.StatusCode == HttpStatusCode.UnprocessableEntity,
            $"expected the in-lock window 422, got {(int)rsp.StatusCode}: {body}");
        Assert.Contains("outside_employment_period", body);
        Assert.DoesNotContain("2026-03-12", body); // date-free even under the race (ADR-040 D3)

        // THE invariant: no registration committed into the narrowed window.
        Assert.Equal(0L, await CountTimeEntryRowsAsync(emp, RegistrationDay));
    }

    // ── Writer #2: POST /api/skema/{id}/save (WorkTime arm — same gate, same lock) ────────────

    [Fact]
    public async Task WindowEditWins_ThenSkemaSave_ObservesNarrowedWindowInLock_And422s()
    {
        var emp = UniqueEmp("wes");
        await SeedEmployeeAsync(emp);
        var (classId, objId) = await AdvisoryKeyPartsAsync(emp);

        await using var blockerConn = Fx.Db.Create();
        await blockerConn.OpenAsync();
        await using var blockerTx = await blockerConn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        await EmployeeConsumptionLock.AcquireAsync(blockerConn, blockerTx, emp);
        await NarrowEndDateInTxAsync(blockerConn, blockerTx, emp, NarrowedEnd);

        var skClient = EmployeeClient(emp);
        var save = Task.Run(() => PostSkemaWorkTimeSaveAsync(skClient, emp, RegistrationDay, "08:00", "16:00"));

        await WaitForWaitersAsync(classId, objId, expected: 1);
        Assert.Equal(0L, await CountWorkTimeRowsAsync(emp, RegistrationDay));

        await blockerTx.CommitAsync();

        var rsp = await save;
        var body = await BodyAsync(rsp);
        Assert.True(rsp.StatusCode == HttpStatusCode.UnprocessableEntity,
            $"expected the in-lock window 422, got {(int)rsp.StatusCode}: {body}");
        Assert.Contains("outside_employment_period", body);
        Assert.DoesNotContain("2026-03-12", body);

        Assert.Equal(0L, await CountWorkTimeRowsAsync(emp, RegistrationDay));
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // S136 Step-5a (Codex BLOCKER 1 — the SEC-046 race): the SUBJECT-STATE (is_active) twin of
    // the window races above. The ADR-040 D3 role floor (subject deactivated ⇒ HROrAbove) was
    // decided from the UNLOCKED pre-transaction subject read; these pins prove the IN-LOCK
    // re-check is authoritative — a deactivation that commits while the writer waits on
    // EmployeeConsumptionLock must refuse the write. Same pg_locks-proven choreography, with
    // the deactivation in the window edit's seat.
    //
    // FAILS IF the subject-state re-check stops being in-lock authoritative: a pre-lock-only
    // check (the pre-S136-Step-5a state), a self-managed (own-connection) re-read, or a
    // RepeatableRead regression would all observe the PRE-FLIP row (is_active TRUE) and
    // 201/200 a write for a just-deactivated subject.
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>Writer #1: the deactivation commits while the time-entry POST waits on the lock
    /// ⇒ the Employee's still-live token is refused with the D3-floor 403 and writes nothing.</summary>
    [Fact]
    public async Task DeactivationWins_ThenTimeEntry_ObservesDeactivatedSubjectInLock_And403s()
    {
        var emp = UniqueEmp("ded");
        await SeedEmployeeAsync(emp); // active; NULL employment dates (the window gate is inert)
        var (classId, objId) = await AdvisoryKeyPartsAsync(emp);

        // The "deactivation" holds the lock and flips is_active, uncommitted — the exact
        // in-lock position the production deactivation writers give this flip.
        await using var blockerConn = Fx.Db.Create();
        await blockerConn.OpenAsync();
        await using var blockerTx = await blockerConn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        await EmployeeConsumptionLock.AcquireAsync(blockerConn, blockerTx, emp);
        await DeactivateInTxAsync(blockerConn, blockerTx, emp);

        // Fire the REAL registration — its pre-lock subject read sees the pre-flip row (MVCC),
        // so only the in-lock re-check can catch the deactivation.
        var teClient = EmployeeClient(emp);
        var timeEntry = Task.Run(() => PostTimeEntryAsync(teClient, emp, RegistrationDay, 7.4m));

        // Prove genuine contention: the POST is QUEUED on our advisory key…
        await WaitForWaitersAsync(classId, objId, expected: 1);
        // …and nothing has committed while the deactivation holds the lock.
        Assert.Equal(0L, await CountTimeEntryRowsAsync(emp, RegistrationDay));

        // The deactivation commits; the POST acquires and must now see is_active=FALSE in-lock.
        await blockerTx.CommitAsync();

        var rsp = await timeEntry;
        var body = await BodyAsync(rsp);
        Assert.True(rsp.StatusCode == HttpStatusCode.Forbidden,
            $"expected the in-lock D3-floor 403, got {(int)rsp.StatusCode}: {body}");
        Assert.Contains("deactivated employee", body); // the pre-check's exact refusal shape

        // THE invariant: no registration committed for the just-deactivated subject.
        Assert.Equal(0L, await CountTimeEntryRowsAsync(emp, RegistrationDay));
    }

    /// <summary>Writer #2: the same deactivation race against the Skema save (WorkTime arm) ⇒
    /// the in-lock D3-floor 403, zero work-time rows.</summary>
    [Fact]
    public async Task DeactivationWins_ThenSkemaSave_ObservesDeactivatedSubjectInLock_And403s()
    {
        var emp = UniqueEmp("des");
        await SeedEmployeeAsync(emp);
        var (classId, objId) = await AdvisoryKeyPartsAsync(emp);

        await using var blockerConn = Fx.Db.Create();
        await blockerConn.OpenAsync();
        await using var blockerTx = await blockerConn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        await EmployeeConsumptionLock.AcquireAsync(blockerConn, blockerTx, emp);
        await DeactivateInTxAsync(blockerConn, blockerTx, emp);

        var skClient = EmployeeClient(emp);
        var save = Task.Run(() => PostSkemaWorkTimeSaveAsync(skClient, emp, RegistrationDay, "08:00", "16:00"));

        await WaitForWaitersAsync(classId, objId, expected: 1);
        Assert.Equal(0L, await CountWorkTimeRowsAsync(emp, RegistrationDay));

        await blockerTx.CommitAsync();

        var rsp = await save;
        var body = await BodyAsync(rsp);
        Assert.True(rsp.StatusCode == HttpStatusCode.Forbidden,
            $"expected the in-lock D3-floor 403, got {(int)rsp.StatusCode}: {body}");
        Assert.Contains("deactivated employee", body);

        Assert.Equal(0L, await CountWorkTimeRowsAsync(emp, RegistrationDay));
    }
}
