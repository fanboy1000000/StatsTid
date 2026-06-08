using System.Text.Json;
using Npgsql;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Settlement;

/// <summary>
/// S68 / Step-7a Codex W4 — DB-level integrity floors on <c>vacation_settlements</c>. The service and
/// endpoints already clamp, but a malformed DIRECT write (a future repo path, a migration, an operator
/// query) must not be able to persist a legally-impossible row. These tests prove the new CHECK
/// constraints REJECT the bad states Codex flagged as DB-valid (negative buckets, zero counters,
/// SETTLED+DEFER / PENDING_REVIEW+FORFEIT coupling) while admitting the valid combinations.
///
/// <para>Postgres CHECK violations surface as <see cref="PostgresException"/> with SQLSTATE 23514.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class SettlementSchemaConstraintTests : IAsyncLifetime
{
    private const string OrgId = "STY01";
    private const string VacationType = "VACATION";
    private const string CheckViolation = "23514";

    private TestFixtures.DockerHarness _harness = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
    }

    public async Task DisposeAsync()
    {
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ─────────────────────────────── negative paths ───────────────────────────────

    [Fact]
    public async Task NegativeBucket_IsRejected_23514()
    {
        var emp = await SeedEmployeeAsync();
        var ex = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertAsync(emp, state: "SETTLED", sequence: 1, version: 1,
                transfer: 0m, payout: 0m, forfeit: -1m, disposition: null));
        Assert.Equal(CheckViolation, ex.SqlState);
    }

    [Fact]
    public async Task ZeroSequence_IsRejected_23514()
    {
        var emp = await SeedEmployeeAsync();
        var ex = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertAsync(emp, state: "SETTLED", sequence: 0, version: 1,
                transfer: 0m, payout: 0m, forfeit: 0m, disposition: null));
        Assert.Equal(CheckViolation, ex.SqlState);
    }

    [Fact]
    public async Task ZeroVersion_IsRejected_23514()
    {
        var emp = await SeedEmployeeAsync();
        var ex = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertAsync(emp, state: "SETTLED", sequence: 1, version: 0,
                transfer: 0m, payout: 0m, forfeit: 0m, disposition: null));
        Assert.Equal(CheckViolation, ex.SqlState);
    }

    [Fact]
    public async Task SettledWithDefer_IsRejected_23514()
    {
        // DEFER (suspected §22) must LEAVE the row PENDING_REVIEW until slice 4 — it can never coexist
        // with a resolved SETTLED state.
        var emp = await SeedEmployeeAsync();
        var ex = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertAsync(emp, state: "SETTLED", sequence: 1, version: 1,
                transfer: 0m, payout: 0m, forfeit: 0m, disposition: "DEFER"));
        Assert.Equal(CheckViolation, ex.SqlState);
    }

    [Fact]
    public async Task PendingReviewWithForfeit_IsRejected_23514()
    {
        // A FORFEIT outcome RESOLVED the review — it cannot still be PENDING_REVIEW.
        var emp = await SeedEmployeeAsync();
        var ex = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertAsync(emp, state: "PENDING_REVIEW", sequence: 1, version: 1,
                transfer: 0m, payout: 0m, forfeit: 0m, disposition: "FORFEIT"));
        Assert.Equal(CheckViolation, ex.SqlState);
    }

    // ─────────────────────────────── positive controls ───────────────────────────────

    [Fact]
    public async Task PendingReviewWithDefer_IsAccepted()
    {
        var emp = await SeedEmployeeAsync();
        await InsertAsync(emp, state: "PENDING_REVIEW", sequence: 1, version: 1,
            transfer: 0m, payout: 0m, forfeit: 20m, disposition: "DEFER");
    }

    [Fact]
    public async Task SettledWithForfeit_IsAccepted()
    {
        var emp = await SeedEmployeeAsync();
        await InsertAsync(emp, state: "SETTLED", sequence: 1, version: 1,
            transfer: 0m, payout: 0m, forfeit: 20m, disposition: "FORFEIT");
    }

    [Fact]
    public async Task SettledWithNoDisposition_IsAccepted()
    {
        // The auto-resolved YEAR_END close (no §34 remainder) records no review_disposition.
        var emp = await SeedEmployeeAsync();
        await InsertAsync(emp, state: "SETTLED", sequence: 1, version: 1,
            transfer: 5m, payout: 0m, forfeit: 0m, disposition: null);
    }

    // ─────────────────────────────── helpers ───────────────────────────────

    private async Task<string> SeedEmployeeAsync()
    {
        var employeeId = "emp_s68_chk_" + Guid.NewGuid().ToString("N")[..8];
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, OrgId, "AC", "OK24");
        return employeeId;
    }

    private async Task InsertAsync(
        string employeeId, string state, int sequence, long version,
        decimal transfer, decimal payout, decimal forfeit, string? disposition)
    {
        var snapshotJson = JsonSerializer.Serialize(new
        {
            recordedAbsences = Array.Empty<object>(),
            earned = 25m, used = 0m, planned = 0m, carryoverIn = 0m,
            annualQuota = 25m, carryoverMax = 5m, resetMonth = 9, okVersion = "OK24",
            transferAgreementDays = transfer, isFeriehindret = false,
        });

        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO vacation_settlements
                (employee_id, entitlement_type, entitlement_year, sequence,
                 settlement_state, trigger, snapshot, transfer_days, payout_days, forfeit_days,
                 review_disposition, version)
            VALUES
                (@e, @t, 2021, @seq, @state, 'YEAR_END', @snapshot::jsonb, @transfer, @payout, @forfeit,
                 @disposition, @version)
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("t", VacationType);
        cmd.Parameters.AddWithValue("seq", sequence);
        cmd.Parameters.AddWithValue("state", state);
        cmd.Parameters.AddWithValue("snapshot", snapshotJson);
        cmd.Parameters.AddWithValue("transfer", transfer);
        cmd.Parameters.AddWithValue("payout", payout);
        cmd.Parameters.AddWithValue("forfeit", forfeit);
        cmd.Parameters.AddWithValue("disposition", (object?)disposition ?? DBNull.Value);
        cmd.Parameters.AddWithValue("version", version);
        await cmd.ExecuteNonQueryAsync();
    }
}
