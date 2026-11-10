using System.Data;
using Npgsql;
using NpgsqlTypes;
using StatsTid.Infrastructure;

namespace StatsTid.Tests.Regression.Infrastructure;

/// <summary>
/// <b>AC-12a — the four-eyes rule inside the SQL (RES-003 item 4: a rule written in two places needs each
/// copy tested).</b> The sender of a month cannot also sign it (approve or reject). The rule is enforced by
/// a C# guard at the endpoint and again by the predicate in the signature statement of
/// <see cref="ApprovalPeriodRepository.TryUpdateStatusConditionalAsync"/>
/// (<c>ApprovalPeriodRepository.cs:1955</c>: <c>COALESCE(t.employee_approved_by, t.submitted_by) IS DISTINCT
/// FROM @actorId</c>). The C# guard refuses first, so no endpoint test reaches the SQL's own LEGACY half, the
/// row with only <c>submitted_by</c> filled in. These facts call the repository directly, below the endpoint.
///
/// <para><b>Docker-gated; CI-pending</b> (Docker is unavailable on the dev machine). Uses the shared
/// real-<c>init.sql</c> fixture of <see cref="ApprovalPeriodSendPrimitivesTests"/>.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class ApprovalFourEyesPredicateTests : IClassFixture<ApprovalPeriodSendPrimitivesFixture>
{
    private const string Sender = "s148_fe_sender";
    private const string Other = "s148_fe_other";
    private static readonly DateOnly MarchStart = new(2026, 3, 1);
    private static readonly DateOnly MarchEnd = new(2026, 3, 31);

    private readonly ApprovalPeriodSendPrimitivesFixture _fx;
    private readonly ApprovalPeriodRepository _repo;

    public ApprovalFourEyesPredicateTests(ApprovalPeriodSendPrimitivesFixture fx)
    {
        _fx = fx;
        _repo = new ApprovalPeriodRepository(fx.Factory);
    }

    /// <summary>
    /// <b>AC-12a — kind R, regression pin, holds today.</b> A legacy month row (<c>SUBMITTED</c>,
    /// <c>submitted_by</c> = the sender, <c>employee_approved_by</c> NULL, seeded by SQL). Approving as the
    /// sender returns <c>null</c> and leaves the row <c>SUBMITTED</c> with <c>approved_by</c> NULL; approving
    /// the same row as a different actor, in its own transaction, returns <c>"SUBMITTED"</c> and leaves the
    /// row <c>APPROVED</c> with <c>approved_by</c> = that actor.
    ///
    /// <para><b>Red conditions (Docker; argued).</b> Holds today. The discriminating assertion is the first
    /// call's <c>Assert.Null</c>: with the legacy half dropped (<c>COALESCE(t.employee_approved_by,
    /// t.submitted_by)</c> reduced to <c>t.employee_approved_by</c>), <c>NULL IS DISTINCT FROM sender</c> is
    /// true, the UPDATE would run, the call would return <c>"SUBMITTED"</c> and the row would be
    /// <c>APPROVED</c> by the sender. The second call's <c>"SUBMITTED"</c> fails if the predicate refused
    /// every actor on a legacy row.</para>
    /// </summary>
    [Fact]
    public async Task LegacySubmittedByActor_Refused_DifferentActor_Admitted()
    {
        var periodId = await SeedLegacySubmittedRowAsync("s148_fe_legacy");

        // Refused: the sender signs their own month. Own transaction, ended before the next call
        // (a refusal keeps the row lock to the end of the transaction, ApprovalPeriodRepository.cs:1933-1941).
        await using (var conn = _fx.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            var refused = await _repo.TryUpdateStatusConditionalAsync(
                conn, tx, periodId, "APPROVED", new[] { "SUBMITTED" }, Sender);
            Assert.Null(refused);
            await tx.CommitAsync();
        }
        var (statusAfterRefusal, approvedByAfterRefusal) = await ReadRowAsync(periodId);
        Assert.Equal("SUBMITTED", statusAfterRefusal);
        Assert.Null(approvedByAfterRefusal);

        // Admitted: a different actor on the same row.
        await using (var conn = _fx.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            var admitted = await _repo.TryUpdateStatusConditionalAsync(
                conn, tx, periodId, "APPROVED", new[] { "SUBMITTED" }, Other);
            Assert.Equal("SUBMITTED", admitted);
            await tx.CommitAsync();
        }
        var (statusAfterAdmit, approvedByAfterAdmit) = await ReadRowAsync(periodId);
        Assert.Equal("APPROVED", statusAfterAdmit);
        Assert.Equal(Other, approvedByAfterAdmit);
    }

    // ── Seeding and reads ───────────────────────────────────────────────────────────────────

    /// <summary>A legacy row: <c>SUBMITTED</c>, <c>submitted_by</c> = <see cref="Sender"/>, no
    /// <c>employee_approved_by</c> (typed NULL: an untyped NULL fails 42P08). The columns are the init.sql
    /// ones, so the seed bypasses the send path on purpose.</summary>
    private async Task<Guid> SeedLegacySubmittedRowAsync(string employeeId)
    {
        await using var conn = _fx.Factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO approval_periods
                (employee_id, org_id, period_start, period_end, period_type, status, agreement_code, ok_version,
                 submitted_at, submitted_by, employee_approved_at, employee_approved_by)
            VALUES
                (@e, 'STY01', @s, @en, 'MONTHLY', 'SUBMITTED', 'HK', 'OK24', NOW(), @sb, NULL, @eab)
            RETURNING period_id
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("s", MarchStart);
        cmd.Parameters.AddWithValue("en", MarchEnd);
        cmd.Parameters.AddWithValue("sb", Sender);
        cmd.Parameters.Add(new NpgsqlParameter("eab", NpgsqlDbType.Text) { Value = DBNull.Value });
        var id = await cmd.ExecuteScalarAsync();
        Assert.NotNull(id);
        return (Guid)id!;
    }

    private async Task<(string Status, string? ApprovedBy)> ReadRowAsync(Guid periodId)
    {
        await using var conn = _fx.Factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT status, approved_by FROM approval_periods WHERE period_id = @id", conn);
        cmd.Parameters.AddWithValue("id", periodId);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1));
    }
}
