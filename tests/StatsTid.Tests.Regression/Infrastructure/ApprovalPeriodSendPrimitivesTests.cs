using System.Data;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Models;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.Infrastructure;

/// <summary>
/// S127 / TASK-12702 — one Postgres testcontainer shared by every test in
/// <see cref="ApprovalPeriodSendPrimitivesTests"/> (xUnit <c>IClassFixture</c>). The schema is the
/// REAL <c>docker/postgres/init.sql</c>, applied once: these tests turn on the exact
/// <c>UNIQUE (employee_id, period_start, period_end)</c> constraint declared there
/// (<c>init.sql:892</c>), so a hand-rolled fixture DDL would be able to drift out from under the very
/// property under test — the class would go green against a constraint production does not have.
/// </summary>
public sealed class ApprovalPeriodSendPrimitivesFixture : IAsyncLifetime
{
    // Private field, not a public property: TestFixtures is internal, so exposing the harness type
    // itself would be a CS0053 accessibility leak. The two members the tests need are enough.
    private TestFixtures.DockerHarness _harness = null!;

    public DbConnectionFactory Factory => _harness.Factory;
    public string ConnectionString => _harness.ConnectionString;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
    }

    public async Task DisposeAsync()
    {
        if (_harness is not null) await _harness.DisposeAsync();
    }
}

/// <summary>
/// S127 / TASK-12702 — the three repository primitives the shared send command (TASK-12703) is built
/// on, tested at the repository level:
///
/// <list type="bullet">
///   <item><see cref="ApprovalPeriodRepository.TryCreateIfAbsentAsync"/> — the race-safe create
///     (<c>ON CONFLICT … DO NOTHING RETURNING</c>), <b>both arms</b>.</item>
///   <item>The <c>(conn, tx)</c> overload of
///     <see cref="ApprovalPeriodRepository.GetByEmployeeAndPeriodAsync(NpgsqlConnection, NpgsqlTransaction, string, DateOnly, DateOnly, CancellationToken)"/>
///     — the authoritative in-lock read by natural key.</item>
///   <item><see cref="ApprovalPeriodRepository.StampSendAsync"/> — the follow-up UPDATE.</item>
/// </list>
///
/// <para>
/// <b>What each test would have to break to stay green — stated, because a test written to
/// demonstrate a result rather than falsify one proves nothing.</b> The conflict-arm tests are RED
/// against the two implementations most likely to be written instead of this one: a plain INSERT
/// (second call raises 23505 rather than returning null) and an INSERT wrapped in
/// <c>catch (PostgresException 23505) { return null; }</c> — that variant returns null correctly but
/// leaves the transaction ABORTED, so the post-conflict database work each test issues on the same
/// transaction fails with 25P02. The negative control
/// (<see cref="TryCreateIfAbsent_ConflictTargetIsTheExactTriple_NotEmployeeAlone"/>) proves the null
/// is not simply what the method always returns after a first write, and pins the conflict target to
/// the full triple rather than to the employee.
/// </para>
///
/// <para>
/// Each test owns a distinct employee id so the shared container needs no inter-test cleanup.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class ApprovalPeriodSendPrimitivesTests : IClassFixture<ApprovalPeriodSendPrimitivesFixture>
{
    private const string OrgId = "STY02";
    private static readonly DateOnly MayStart = new(2026, 5, 1);
    private static readonly DateOnly MayEnd = new(2026, 5, 31);

    private readonly ApprovalPeriodSendPrimitivesFixture _fx;
    private readonly ApprovalPeriodRepository _repo;

    public ApprovalPeriodSendPrimitivesTests(ApprovalPeriodSendPrimitivesFixture fx)
    {
        _fx = fx;
        _repo = new ApprovalPeriodRepository(fx.Factory);
    }

    // ════════════════════════════════════════════════════════════════════════════════════════
    //  AC-7c — TryCreateIfAbsentAsync, the CONFLICT arm
    // ════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// AC-7c, literal shape: two calls on the same natural key with DIFFERENT candidate ids (the
    /// primitive mints a fresh <see cref="Guid.NewGuid"/> per call, exactly as <c>CreateAsync</c>
    /// does). The first returns its id; the second returns <c>null</c>; the row count stays one; the
    /// original id survives; and — the assertion that separates
    /// <c>ON CONFLICT DO NOTHING</c> from a caught 23505 — the transaction REMAINS USABLE, so the
    /// send command can go on to re-read and take the transition arm in the same transaction.
    /// </summary>
    [Fact]
    public async Task TryCreateIfAbsent_TwiceOnSameNaturalKey_SecondReturnsNull_RowSurvives_TxStaysUsable()
    {
        const string employeeId = "s127_ac7c_pair";

        await using var conn = _fx.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        var first = await _repo.TryCreateIfAbsentAsync(conn, tx, NewPeriod(employeeId));
        Assert.NotNull(first);

        var second = await _repo.TryCreateIfAbsentAsync(conn, tx, NewPeriod(employeeId));
        Assert.Null(second);

        // The transaction is still usable. A `catch (PostgresException 23505)` implementation would
        // have returned null above and then failed HERE with 25P02 ("current transaction is aborted").
        // Deliberately real work, not `SELECT 1`: a write on the same table the conflict occurred on.
        await ExecAsync(conn, tx,
            "UPDATE approval_periods SET rejection_reason = 'tx-usable-probe' WHERE period_id = @id",
            ("id", first!.Value));
        var probe = await ScalarAsync<string>(conn, tx,
            "SELECT rejection_reason FROM approval_periods WHERE period_id = @id", ("id", first.Value));
        Assert.Equal("tx-usable-probe", probe);

        // Exactly one row on the natural key, and it is the FIRST call's id — the second call's
        // (different) candidate id was not written.
        Assert.Equal(1L, await CountOnKeyAsync(conn, tx, employeeId));
        Assert.Equal(first.Value, await ScalarAsync<Guid>(conn, tx,
            "SELECT period_id FROM approval_periods WHERE employee_id = @e", ("e", employeeId)));

        await tx.CommitAsync();

        // Survives the commit: still one row, still the first id.
        await using var verify = _fx.Factory.Create();
        await verify.OpenAsync();
        Assert.Equal(1L, await CountOnKeyAsync(verify, null, employeeId));
        Assert.Equal(first.Value, await ScalarAsync<Guid>(verify, null,
            "SELECT period_id FROM approval_periods WHERE employee_id = @e", ("e", employeeId)));
    }

    /// <summary>
    /// AC-7c in its PRODUCTION shape: the conflicting row was committed by a DIFFERENT transaction
    /// (the real race — the loser of the create race blocks on the advisory lock, then finds the
    /// winner's committed row). The winner's id is PINNED by the test via raw SQL, so "different
    /// candidate ids" is not an argument about <see cref="Guid.NewGuid"/> — the surviving id is a
    /// value the SUT could not have produced.
    ///
    /// <para>The loser's transaction is <c>ReadCommitted</c>: under <c>RepeatableRead</c> its snapshot
    /// would predate the winner's commit and the INSERT would raise instead of conflicting cleanly.</para>
    /// </summary>
    [Fact]
    public async Task TryCreateIfAbsent_AgainstCommittedRowFromAnotherTx_ReturnsNull_TxStaysUsable()
    {
        const string employeeId = "s127_ac7c_committed";
        var winnerId = new Guid("bbbbbbbb-1270-4200-9000-00000000c0de");

        // Winner: a separate, already-COMMITTED transaction, with a test-chosen period_id.
        await using (var winnerConn = _fx.Factory.Create())
        {
            await winnerConn.OpenAsync();
            await ExecAsync(winnerConn, null,
                """
                INSERT INTO approval_periods (period_id, employee_id, org_id, period_start, period_end,
                                              period_type, status, agreement_code, ok_version)
                VALUES (@id, @e, @org, @s, @en, 'MONTHLY', 'DRAFT', 'HK', 'OK24')
                """,
                ("id", winnerId), ("e", employeeId), ("org", OrgId), ("s", MayStart), ("en", MayEnd));
        }

        // Loser: a fresh transaction whose candidate id is minted inside the SUT.
        await using var conn = _fx.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        var result = await _repo.TryCreateIfAbsentAsync(conn, tx, NewPeriod(employeeId));
        Assert.Null(result);

        // Transaction still usable after losing — this is what the send command depends on.
        await ExecAsync(conn, tx,
            "UPDATE approval_periods SET rejection_reason = 'loser-continues' WHERE employee_id = @e",
            ("e", employeeId));
        Assert.Equal("loser-continues", await ScalarAsync<string>(conn, tx,
            "SELECT rejection_reason FROM approval_periods WHERE employee_id = @e", ("e", employeeId)));

        // One row, and it is still the WINNER's pinned id — the loser's candidate never landed.
        Assert.Equal(1L, await CountOnKeyAsync(conn, tx, employeeId));
        Assert.Equal(winnerId, await ScalarAsync<Guid>(conn, tx,
            "SELECT period_id FROM approval_periods WHERE employee_id = @e", ("e", employeeId)));

        await tx.RollbackAsync();
    }

    /// <summary>
    /// The NEGATIVE CONTROL for the two tests above: the conflict target is the exact triple
    /// <c>(employee_id, period_start, period_end)</c>, so the same employee in a different month —
    /// and, sharper, the same employee with the same <c>period_start</c> but a different
    /// <c>period_end</c> — both WRITE. Without this, "the second call returns null" would also be
    /// satisfied by a primitive that returns null for everything after its first write, or by one
    /// keyed on the employee alone.
    /// </summary>
    [Fact]
    public async Task TryCreateIfAbsent_ConflictTargetIsTheExactTriple_NotEmployeeAlone()
    {
        const string employeeId = "s127_ac7c_triple";

        await using var conn = _fx.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        var may = await _repo.TryCreateIfAbsentAsync(conn, tx, NewPeriod(employeeId));
        Assert.NotNull(may);

        // Same employee, different month → different triple → writes.
        var june = await _repo.TryCreateIfAbsentAsync(
            conn, tx, NewPeriod(employeeId, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30)));
        Assert.NotNull(june);

        // Same employee, SAME period_start, different period_end → still a different triple → writes.
        // (This is the arm a `UNIQUE (employee_id, period_start)` misreading would fail.)
        var mayShort = await _repo.TryCreateIfAbsentAsync(
            conn, tx, NewPeriod(employeeId, MayStart, new DateOnly(2026, 5, 30)));
        Assert.NotNull(mayShort);

        Assert.Equal(3, new[] { may!.Value, june!.Value, mayShort!.Value }.Distinct().Count());
        Assert.Equal(3L, await ScalarAsync<long>(conn, tx,
            "SELECT COUNT(*) FROM approval_periods WHERE employee_id = @e", ("e", employeeId)));

        // …and the exact May triple still conflicts, in the same transaction that just wrote its
        // two neighbours: the three writes above are not evidence that conflict detection is off.
        Assert.Null(await _repo.TryCreateIfAbsentAsync(conn, tx, NewPeriod(employeeId)));
        Assert.Equal(1L, await CountOnKeyAsync(conn, tx, employeeId));

        await tx.RollbackAsync();
    }

    // ════════════════════════════════════════════════════════════════════════════════════════
    //  The (conn, tx) natural-key read
    // ════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The <c>(conn, tx)</c> overload reads inside the caller's transaction; the self-managed overload
    /// cannot. Falsifier: the test asserts the SAME natural key, at the SAME moment, returns the row
    /// through the new overload and <c>null</c> through the pre-existing one — which is only possible
    /// if the overload genuinely used the supplied transaction. An overload that quietly opened its
    /// own connection would return null too, and this test would go red.
    ///
    /// <para>This is precisely the gap that made the overload necessary: a caller holding
    /// <c>EmployeeConsumptionLock</c> must re-read the month INSIDE its transaction, and the
    /// self-managed read sits outside both the transaction and the lock.</para>
    /// </summary>
    [Fact]
    public async Task GetByEmployeeAndPeriod_ConnTxOverload_SeesCallerTxWrite_SelfManagedDoesNot()
    {
        const string employeeId = "s127_read_intx";

        await using var conn = _fx.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        // Nothing committed yet — both reads must agree there is no row.
        Assert.Null(await _repo.GetByEmployeeAndPeriodAsync(conn, tx, employeeId, MayStart, MayEnd));
        Assert.Null(await _repo.GetByEmployeeAndPeriodAsync(employeeId, MayStart, MayEnd));

        var created = await _repo.TryCreateIfAbsentAsync(conn, tx, NewPeriod(employeeId));
        Assert.NotNull(created);

        // In-transaction read sees the uncommitted row, fully mapped.
        var inTx = await _repo.GetByEmployeeAndPeriodAsync(conn, tx, employeeId, MayStart, MayEnd);
        Assert.NotNull(inTx);
        Assert.Equal(created!.Value, inTx!.PeriodId);
        Assert.Equal(employeeId, inTx.EmployeeId);
        Assert.Equal(MayStart, inTx.PeriodStart);
        Assert.Equal(MayEnd, inTx.PeriodEnd);
        Assert.Equal("DRAFT", inTx.Status);
        Assert.Equal(OrgId, inTx.OrgId);

        // The self-managed overload, on its own connection, cannot see it (ReadCommitted).
        Assert.Null(await _repo.GetByEmployeeAndPeriodAsync(employeeId, MayStart, MayEnd));

        // The overload did not commit or close the caller's transaction.
        Assert.Equal(ConnectionState.Open, conn.State);
        await tx.RollbackAsync();
        Assert.Null(await _repo.GetByEmployeeAndPeriodAsync(employeeId, MayStart, MayEnd));
    }

    // ════════════════════════════════════════════════════════════════════════════════════════
    //  The follow-up UPDATE
    // ════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <see cref="ApprovalPeriodRepository.StampSendAsync"/> writes all five columns on the row the
    /// conditional transition just moved — and the <c>status switch</c> stays untouched, which the
    /// test pins from both sides:
    ///
    /// <list type="number">
    ///   <item>after <c>TryUpdateStatusConditionalAsync(… "EMPLOYEE_APPROVED" …)</c>,
    ///     <c>submitted_at</c> is STILL NULL — so the follow-up UPDATE is doing work the switch does
    ///     not do, rather than restating it;</item>
    ///   <item>after the stamp, a <c>DRAFT</c> transition NULLs <c>submitted_at</c> again — the reopen
    ///     path that makes a re-sent month's null state reachable is unchanged.</item>
    /// </list>
    ///
    /// <para>The row is seeded with DELIBERATELY WRONG dimensions (<c>STY01</c>/<c>AC</c>/<c>OK21</c>)
    /// so the post-stamp assertions cannot be satisfied by the seed — this is the "a legacy row with
    /// wrong caller-supplied values is corrected on re-send" behaviour, made falsifiable.</para>
    /// </summary>
    [Fact]
    public async Task StampSend_WritesAllFiveColumns_AfterConditionalTransition_ReopenStillClearsThem()
    {
        const string employeeId = "s127_stamp";
        const string actorId = "s127_actor";

        // Seeded (committed) with wrong dimensions — the send must correct them.
        Guid periodId;
        await using (var seedConn = _fx.Factory.Create())
        {
            await seedConn.OpenAsync();
            periodId = await ScalarAsync<Guid>(seedConn, null,
                """
                INSERT INTO approval_periods (employee_id, org_id, period_start, period_end,
                                              period_type, status, agreement_code, ok_version)
                VALUES (@e, 'STY01', @s, @en, 'MONTHLY', 'DRAFT', 'AC', 'OK21')
                RETURNING period_id
                """,
                ("e", employeeId), ("s", MayStart), ("en", MayEnd));
        }

        await using var conn = _fx.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        // The guarded transition runs FIRST and holds the row lock to end-of-transaction — which is
        // why the stamp below needs no source-state guard of its own.
        var previous = await _repo.TryUpdateStatusConditionalAsync(
            conn, tx, periodId, "EMPLOYEE_APPROVED",
            new[] { "DRAFT", "SUBMITTED", "REJECTED" }, actorId);
        Assert.Equal("DRAFT", previous);

        // (1) The EMPLOYEE_APPROVED branch of the status switch does NOT stamp submitted_at.
        Assert.Null(await NullableScalarAsync(conn, tx,
            "SELECT submitted_at FROM approval_periods WHERE period_id = @id", ("id", periodId)));

        await _repo.StampSendAsync(conn, tx, periodId, actorId, OrgId, "HK", "OK24");

        var stamped = await _repo.GetByEmployeeAndPeriodAsync(conn, tx, employeeId, MayStart, MayEnd);
        Assert.NotNull(stamped);
        Assert.NotNull(stamped!.SubmittedAt);
        Assert.Equal(actorId, stamped.SubmittedBy);
        Assert.Equal(OrgId, stamped.OrgId);              // corrected from STY01
        Assert.Equal("HK", stamped.AgreementCode);       // corrected from AC
        Assert.Equal("OK24", stamped.OkVersion);         // corrected from OK21
        // The stamp touches only its five columns — the transition's own writes are intact.
        Assert.Equal("EMPLOYEE_APPROVED", stamped.Status);
        Assert.Equal(actorId, stamped.EmployeeApprovedBy);
        Assert.NotNull(stamped.EmployeeApprovedAt);

        // (2) Reopen still clears the whole decision record, submitted_at included.
        var beforeReopen = await _repo.TryUpdateStatusConditionalAsync(
            conn, tx, periodId, "DRAFT", new[] { "EMPLOYEE_APPROVED" }, actorId);
        Assert.Equal("EMPLOYEE_APPROVED", beforeReopen);
        var reopened = await _repo.GetByEmployeeAndPeriodAsync(conn, tx, employeeId, MayStart, MayEnd);
        Assert.NotNull(reopened);
        Assert.Equal("DRAFT", reopened!.Status);
        Assert.Null(reopened.SubmittedAt);
        Assert.Null(reopened.SubmittedBy);
        Assert.Null(reopened.EmployeeApprovedAt);
        // The server-resolved dimensions are NOT part of the decision record and survive the reopen.
        Assert.Equal(OrgId, reopened.OrgId);
        Assert.Equal("HK", reopened.AgreementCode);
        Assert.Equal("OK24", reopened.OkVersion);

        await tx.RollbackAsync();
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────

    private static ApprovalPeriod NewPeriod(string employeeId, DateOnly? start = null, DateOnly? end = null) => new()
    {
        // Non-empty on purpose: the primitive must IGNORE it and mint its own id, exactly as
        // CreateAsync does. If it ever honoured this value the paired-call test would insert the
        // same id twice and fail on the primary key instead of the natural key.
        PeriodId = Guid.NewGuid(),
        EmployeeId = employeeId,
        OrgId = OrgId,
        PeriodStart = start ?? MayStart,
        PeriodEnd = end ?? MayEnd,
        PeriodType = "MONTHLY",
        Status = "DRAFT",
        AgreementCode = "HK",
        OkVersion = "OK24",
    };

    private static Task<long> CountOnKeyAsync(NpgsqlConnection conn, NpgsqlTransaction? tx, string employeeId)
        => ScalarAsync<long>(conn, tx,
            """
            SELECT COUNT(*) FROM approval_periods
            WHERE employee_id = @e AND period_start = @s AND period_end = @en
            """, ("e", employeeId), ("s", MayStart), ("en", MayEnd));

    private static async Task ExecAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx, string sql, params (string Name, object Value)[] ps)
    {
        await using var cmd = tx is null ? new NpgsqlCommand(sql, conn) : new NpgsqlCommand(sql, conn, tx);
        foreach (var (name, value) in ps) cmd.Parameters.AddWithValue(name, value);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Reads a scalar and REQUIRES it to be non-null. Deliberately an unboxing cast rather than
    /// <see cref="Convert.ChangeType(object, Type)"/>: a column that comes back as an unexpected CLR
    /// type should fail the test loudly instead of being silently coerced into agreement.
    /// </summary>
    private static async Task<T> ScalarAsync<T>(
        NpgsqlConnection conn, NpgsqlTransaction? tx, string sql, params (string Name, object Value)[] ps)
    {
        var raw = await NullableScalarAsync(conn, tx, sql, ps);
        Assert.NotNull(raw);
        return (T)raw!;
    }

    private static async Task<object?> NullableScalarAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx, string sql, params (string Name, object Value)[] ps)
    {
        await using var cmd = tx is null ? new NpgsqlCommand(sql, conn) : new NpgsqlCommand(sql, conn, tx);
        foreach (var (name, value) in ps) cmd.Parameters.AddWithValue(name, value);
        var result = await cmd.ExecuteScalarAsync();
        return result is null or DBNull ? null : result;
    }
}
