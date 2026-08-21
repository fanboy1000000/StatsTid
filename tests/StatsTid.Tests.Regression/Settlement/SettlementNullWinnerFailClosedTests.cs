using System.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Outbox;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Interfaces;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Settlement;

/// <summary>
/// S132 / TASK-132-2c (QUAL-004) — the fail-closed winner-null guard across ALL FOUR
/// <c>DuplicateActiveSettlementException</c> recovery blocks in <see cref="VacationSettlementService"/>.
///
/// <para><b>What these tests pin.</b> Each of the four settlement entry points
/// (<c>SettleActiveYearEndAsync</c>, <c>SettleTerminationAsync</c>,
/// <c>SettleLeaverDeferredDispositionAsync</c>, <c>SettleSpecialHolidayGodtgoerelseAsync</c>) catches the
/// 23505 single-settle backstop and re-reads the active "winner". When that re-read returns NULL —
/// the 23505 PROVED an active row existed at insert time, so a null re-read is an impossible-under-the-
/// advisory-lock invariant breach — the block MUST fail LOUD (throw), NEVER fabricate an
/// <c>AlreadySettled</c> outcome from the unpersisted candidate row (ADR-033 D10).</para>
///
/// <para><b>RED-on-old vs regression-lock — READ THIS so the suite is not misread.</b> Only
/// <c>SettleActiveYearEndAsync</c> was BROKEN before S132 (it returned <c>AlreadySettled(row)</c> — a
/// fabricated success): <see cref="ActiveYearEnd_NullWinner_FailsLoud_GENUINE_RED_on_old"/> is the
/// genuine RED-on-baseline / GREEN-after-fix proof. The other three blocks were ALREADY hardened in
/// prior sprints (S70 B3 / S80); their tests here are GREEN on baseline too — they LOCK the existing
/// correct throw against regression, they are NOT evidence those three were ever broken.</para>
///
/// <para><b>Why a test-double.</b> collision-with-null-winner cannot occur against a real Postgres:
/// the 23505 fires on <c>idx_vacation_settlements_active</c> (predicate <c>state &lt;&gt; 'REVERSED'</c>)
/// and <c>GetActiveAsync</c> filters on the SAME predicate, so a collision always implies a visible
/// winner (the sibling <c>TerminationSettlementTests</c> committed-competitor choreography confirms it).
/// The state is therefore forced with <see cref="NullWinnerSettlementRepo"/> — a subclass of the real
/// repo (unsealed + two <c>virtual</c> members, S132 seam) that throws the backstop from
/// <c>InsertAsync</c> and returns null from the in-tx <c>GetActiveAsync</c>, wired into an OTHERWISE-real
/// service (real advisory lock, real conn/tx, real snapshot capture through the real config/user repos).</para>
///
/// <para>Harness + seeding mirror <see cref="VacationSettlementServiceTests"/> /
/// <see cref="TerminationSettlementTests"/>.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class SettlementNullWinnerFailClosedTests : IAsyncLifetime
{
    private const string OrgId = "STY01";
    private const string VacationType = "VACATION";
    private const string SpecialHolidayType = "SPECIAL_HOLIDAY";
    private const string YearEnd = "YEAR_END";
    private const string Termination = "TERMINATION";

    // A long-closed VACATION / SPECIAL_HOLIDAY year (reset 9 / reset 1) — settleable under a default clock.
    private const int ClosedYear = 2021;

    // The TERMINATION leaver end date 2026-02-28 ⇒ R6 ferieår 2025 (month 2 < 9 ⇒ 2026 − 1); its ferieår
    // is settled with trigger=TERMINATION, and the SAME leaver's OTHER closed ferieår (2021) with
    // trigger=YEAR_END routes to the leaver-deferred fork (end date is in the past under today's clock).
    private static readonly DateOnly LeaverEndDate = new(2026, 2, 28);
    private const int LeaverTerminationFerieaar = 2025;

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        // Boot the seeders (VACATION config quota 25 / reset 9 / cap 5; SPECIAL_HOLIDAY config quota 5 /
        // reset 1 / cap 0) so every snapshot capture below resolves and the pass reaches InsertAsync.
        _ = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ─────────────────────────────── the test-double + service wiring ───────────────────────────────

    /// <summary>
    /// Forces the impossible collision-with-null-winner state: <c>InsertAsync</c> throws the
    /// single-settle backstop WITHOUT persisting (and without dirtying the caller tx — no failed SQL),
    /// and the in-tx <c>GetActiveAsync</c> re-read returns null. Every OTHER repo member uses the real
    /// base implementation against the real DB (e.g. <c>HasBareReversalMarkerAsync</c> → false for a
    /// fresh tuple), so the pass reaches each recovery block's catch normally.
    /// </summary>
    private sealed class NullWinnerSettlementRepo : VacationSettlementRepository
    {
        public NullWinnerSettlementRepo(DbConnectionFactory factory) : base(factory) { }

        public override Task<VacationSettlementRow?> GetActiveAsync(
            NpgsqlConnection conn, NpgsqlTransaction tx,
            string employeeId, string entitlementType, int entitlementYear, CancellationToken ct = default)
            => Task.FromResult<VacationSettlementRow?>(null);

        public override Task<VacationSettlementRow> InsertAsync(
            NpgsqlConnection conn, NpgsqlTransaction tx,
            VacationSettlementRow row, string snapshotJson, string actorId, string actorRole,
            CancellationToken ct = default)
            => throw new DuplicateActiveSettlementException(
                row.EmployeeId, row.EntitlementType, row.EntitlementYear,
                new InvalidOperationException("forced single-settle backstop (QUAL-004 null-winner test)"));
    }

    /// <summary>Reconstructs the service with all-real DI dependencies EXCEPT the settlement repo, which
    /// is the null-winner test-double. (The DI-registered service resolves all of these already.)</summary>
    private VacationSettlementService BuildServiceWithNullWinnerRepo()
    {
        var sp = _factory.Services;
        return new VacationSettlementService(
            sp.GetRequiredService<EntitlementBalanceRepository>(),
            sp.GetRequiredService<EntitlementConfigRepository>(),
            sp.GetRequiredService<UserRepository>(),
            sp.GetRequiredService<UserAgreementCodeRepository>(),
            sp.GetRequiredService<VacationTransferAgreementRepository>(),
            new NullWinnerSettlementRepo(sp.GetRequiredService<DbConnectionFactory>()),
            sp.GetRequiredService<IEmploymentProfileResolver>(),
            sp.GetRequiredService<IOutboxEnqueue>(),
            sp.GetRequiredService<IAuditProjectionMapperRegistry>(),
            sp.GetRequiredService<AuditProjectionRepository>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<VacationSettlementService>>());
    }

    // ─────────────────────────────── the four fail-closed proofs ───────────────────────────────

    /// <summary>
    /// GENUINE RED-on-old — <c>SettleActiveYearEndAsync</c>. On baseline the winner-null fallback
    /// returned <c>SettlementOutcome.AlreadySettled(row)</c> (a fabricated success from the unpersisted
    /// candidate row) ⇒ NO throw ⇒ this test FAILS. After the S132 fix it throws, mirroring the siblings.
    /// </summary>
    [Fact]
    public async Task ActiveYearEnd_NullWinner_FailsLoud_GENUINE_RED_on_old()
    {
        var employeeId = await SeedActiveEmployeeAsync();
        await AssertNullWinnerFailsClosedAsync(
            BuildServiceWithNullWinnerRepo(), employeeId, VacationType, ClosedYear, YearEnd,
            expectedPathMarker: "(YEAR_END auto-partition)");
    }

    /// <summary>
    /// Regression-lock (GREEN on baseline — this block was already hardened in S70 B3) —
    /// <c>SettleTerminationAsync</c>. A leaver whose end-date ferieår is settled with trigger=TERMINATION.
    /// </summary>
    [Fact]
    public async Task Termination_NullWinner_FailsLoud_RegressionLock()
    {
        var employeeId = await SeedActiveEmployeeAsync();
        await MarkLeaverAsync(employeeId, LeaverEndDate);
        await AssertNullWinnerFailsClosedAsync(
            BuildServiceWithNullWinnerRepo(), employeeId, VacationType, LeaverTerminationFerieaar, Termination,
            expectedPathMarker: "(TERMINATION)");
    }

    /// <summary>
    /// Regression-lock (GREEN on baseline — this block was already hardened) —
    /// <c>SettleLeaverDeferredDispositionAsync</c>. A leaver's OTHER closed ferieår settled with
    /// trigger=YEAR_END routes to the fail-closed deferred-disposition fork.
    /// </summary>
    [Fact]
    public async Task LeaverDeferred_NullWinner_FailsLoud_RegressionLock()
    {
        var employeeId = await SeedActiveEmployeeAsync();
        await MarkLeaverAsync(employeeId, LeaverEndDate);
        await AssertNullWinnerFailsClosedAsync(
            BuildServiceWithNullWinnerRepo(), employeeId, VacationType, ClosedYear, YearEnd,
            expectedPathMarker: "(leaver deferred-disposition)");
    }

    /// <summary>
    /// Regression-lock (GREEN on baseline — this block was already hardened in S80) —
    /// <c>SettleSpecialHolidayGodtgoerelseAsync</c>. An active employee's closed SPECIAL_HOLIDAY accrual
    /// year. The path-marker assertion also proves the throw is the winner-null guard and NOT the
    /// upstream "no entitlement config resolvable" fail-closed (which lacks the guard's message).
    /// </summary>
    [Fact]
    public async Task SpecialHolidayGodtgoerelse_NullWinner_FailsLoud_RegressionLock()
    {
        var employeeId = await SeedActiveEmployeeAsync();
        await AssertNullWinnerFailsClosedAsync(
            BuildServiceWithNullWinnerRepo(), employeeId, SpecialHolidayType, ClosedYear, YearEnd,
            expectedPathMarker: "SPECIAL_HOLIDAY settlement:");
    }

    // ─────────────────────────────── drive + seeding ───────────────────────────────

    /// <summary>Drives one pass in its own ReadCommitted tx (the SettlementCloseService shape) and
    /// asserts it THROWS the winner-null fail-closed guard — the two message fragments common to all
    /// four blocks, plus a per-block path marker proving the RIGHT recovery block was reached.</summary>
    private async Task AssertNullWinnerFailsClosedAsync(
        VacationSettlementService service, string employeeId, string entitlementType, int year, string trigger,
        string expectedPathMarker)
    {
        await using var conn = _factory.Services.GetRequiredService<DbConnectionFactory>().Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SettleAsync(employeeId, entitlementType, year, trigger, conn, tx));

        Assert.Contains("no active settlement row is visible", ex.Message);
        Assert.Contains("refusing to fabricate", ex.Message);
        Assert.Contains(expectedPathMarker, ex.Message);

        // Nothing was persisted (the fake never inserts); the caller tx is clean — roll it back.
        if (tx.Connection is not null)
            await tx.RollbackAsync();
    }

    private async Task<string> SeedActiveEmployeeAsync()
    {
        var employeeId = "emp_qual004_" + Guid.NewGuid().ToString("N")[..8];
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, OrgId, "AC", "OK24");
        return employeeId;
    }

    private async Task MarkLeaverAsync(string employeeId, DateOnly endDate)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE users SET employment_end_date = @endDate, is_active = FALSE,
                             end_date_deactivated = TRUE, updated_at = NOW()
            WHERE user_id = @id
            """, conn);
        cmd.Parameters.AddWithValue("id", employeeId);
        cmd.Parameters.AddWithValue("endDate", endDate);
        await cmd.ExecuteNonQueryAsync();
    }
}
