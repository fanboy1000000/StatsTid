using Npgsql;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Infrastructure;

public sealed class OvertimePreApprovalRepository
{
    private readonly DbConnectionFactory _connectionFactory;

    public OvertimePreApprovalRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<OvertimePreApproval?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM overtime_pre_approvals WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadApproval(reader) : null;
    }

    public async Task<IReadOnlyList<OvertimePreApproval>> GetByEmployeeAndPeriodAsync(
        string employeeId, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            @"SELECT * FROM overtime_pre_approvals
              WHERE employee_id = @employeeId AND period_start <= @periodEnd AND period_end >= @periodStart
              ORDER BY created_at DESC",
            conn);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("periodStart", periodStart);
        cmd.Parameters.AddWithValue("periodEnd", periodEnd);
        return await ReadApprovalsAsync(cmd, ct);
    }

    public async Task<IReadOnlyList<OvertimePreApproval>> GetPendingByEmployeesAsync(
        IEnumerable<string> employeeIds, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            @"SELECT * FROM overtime_pre_approvals
              WHERE employee_id = ANY(@employeeIds) AND status = 'PENDING'
              ORDER BY created_at DESC",
            conn);
        cmd.Parameters.AddWithValue("employeeIds", employeeIds.ToArray());
        return await ReadApprovalsAsync(cmd, ct);
    }

    /// <summary>
    /// S116 / TASK-11601 — the scope-bounded ADMIN enumeration: ALL statuses (NOT the PENDING-only
    /// cut of <see cref="GetPendingByEmployeesAsync"/>), bounded by the actor's resolved scope.
    /// The <c>users</c> JOIN is load-bearing three ways: (1) it IS the org derivation —
    /// <c>overtime_pre_approvals</c> has NO org column (init.sql:1850-1861), so
    /// <c>users.primary_org_id</c> is the ONLY org source (N2 consequence, accepted: attribution is
    /// CURRENT-org after a transfer); (2) it carries <c>users.display_name</c> → the non-null
    /// <c>EmployeeName</c>; (3) its <c>is_active = TRUE</c> predicate is the Step-0b CONVERGENT pin
    /// (Codex BLOCKER + Reviewer W1) — without it a terminated employee's rows would still derive an
    /// in-scope org, and the approve/reject act path is fail-closed for inactive targets (see == act),
    /// so an unfiltered list would render always-403 buttons.
    /// A null <paramref name="orgId"/> is the GLOBAL variant (all orgs); non-null is the ORG_ONLY
    /// variant (exactly that org — no subtree, S93/ADR-035 exact membership).
    /// </summary>
    public async Task<IReadOnlyList<OvertimePreApprovalAdminRow>> GetAllScopedWithEmployeeNamesAsync(
        string? orgId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        var sql =
            @"SELECT opa.*, u.display_name
              FROM overtime_pre_approvals opa
              JOIN users u ON u.user_id = opa.employee_id AND u.is_active = TRUE"
            + (orgId is null ? "" : @"
              WHERE u.primary_org_id = @orgId") + @"
              ORDER BY opa.created_at DESC";
        await using var cmd = new NpgsqlCommand(sql, conn);
        if (orgId is not null)
            cmd.Parameters.AddWithValue("orgId", orgId);
        var rows = new List<OvertimePreApprovalAdminRow>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            rows.Add(new OvertimePreApprovalAdminRow(
                Approval: ReadApproval(reader),
                EmployeeName: reader.GetString(reader.GetOrdinal("display_name"))));
        return rows;
    }

    public async Task CreateAsync(OvertimePreApproval approval, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = BuildCreateCommand(conn, null, approval);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// In-transaction sibling overload of <see cref="CreateAsync(OvertimePreApproval, CancellationToken)"/>.
    /// Reuses the caller-supplied <paramref name="conn"/> + <paramref name="tx"/> so the
    /// caller can extend the same transaction across outbox writes (ADR-018 D3 transactional-
    /// outbox contract). The caller commits or rolls back; this method does NOT.
    /// </summary>
    public async Task CreateAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        OvertimePreApproval approval, CancellationToken ct = default)
    {
        await using var cmd = BuildCreateCommand(conn, tx, approval);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static NpgsqlCommand BuildCreateCommand(
        NpgsqlConnection conn, NpgsqlTransaction? tx, OvertimePreApproval approval)
    {
        var sql =
            @"INSERT INTO overtime_pre_approvals (id, employee_id, period_start, period_end, max_hours, status, reason, created_at)
              VALUES (@id, @employeeId, @periodStart, @periodEnd, @maxHours, @status, @reason, NOW())";
        var cmd = tx is null ? new NpgsqlCommand(sql, conn) : new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("id", approval.Id);
        cmd.Parameters.AddWithValue("employeeId", approval.EmployeeId);
        cmd.Parameters.AddWithValue("periodStart", approval.PeriodStart);
        cmd.Parameters.AddWithValue("periodEnd", approval.PeriodEnd);
        cmd.Parameters.AddWithValue("maxHours", approval.MaxHours);
        cmd.Parameters.AddWithValue("status", approval.Status);
        cmd.Parameters.AddWithValue("reason", (object?)approval.Reason ?? DBNull.Value);
        return cmd;
    }

    public async Task UpdateStatusAsync(
        Guid id, string status, string? approvedBy, string? reason, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = BuildUpdateStatusCommand(conn, null, id, status, approvedBy, reason);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// In-transaction sibling overload of <see cref="UpdateStatusAsync(Guid, string, string?, string?, CancellationToken)"/>.
    /// Reuses the caller-supplied <paramref name="conn"/> + <paramref name="tx"/> so the
    /// caller can extend the same transaction across outbox writes (ADR-018 D3 transactional-
    /// outbox contract). Required by TASK-2607 (Overtime approve/reject atomic). The caller
    /// commits or rolls back; this method does NOT.
    /// </summary>
    public async Task UpdateStatusAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid id, string status, string? approvedBy, string? reason, CancellationToken ct = default)
    {
        await using var cmd = BuildUpdateStatusCommand(conn, tx, id, status, approvedBy, reason);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static NpgsqlCommand BuildUpdateStatusCommand(
        NpgsqlConnection conn, NpgsqlTransaction? tx,
        Guid id, string status, string? approvedBy, string? reason)
    {
        var sql =
            @"UPDATE overtime_pre_approvals
              SET status = @status, approved_by = @approvedBy, approved_at = NOW(), reason = @reason
              WHERE id = @id";
        var cmd = tx is null ? new NpgsqlCommand(sql, conn) : new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("status", status);
        cmd.Parameters.AddWithValue("approvedBy", (object?)approvedBy ?? DBNull.Value);
        cmd.Parameters.AddWithValue("reason", (object?)reason ?? DBNull.Value);
        return cmd;
    }

    private static async Task<IReadOnlyList<OvertimePreApproval>> ReadApprovalsAsync(
        NpgsqlCommand cmd, CancellationToken ct)
    {
        var approvals = new List<OvertimePreApproval>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            approvals.Add(ReadApproval(reader));
        return approvals;
    }

    private static OvertimePreApproval ReadApproval(NpgsqlDataReader reader) => new()
    {
        Id = reader.GetGuid(reader.GetOrdinal("id")),
        EmployeeId = reader.GetString(reader.GetOrdinal("employee_id")),
        PeriodStart = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("period_start"))),
        PeriodEnd = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("period_end"))),
        MaxHours = reader.GetDecimal(reader.GetOrdinal("max_hours")),
        ApprovedBy = reader.IsDBNull(reader.GetOrdinal("approved_by")) ? null : reader.GetString(reader.GetOrdinal("approved_by")),
        ApprovedAt = reader.IsDBNull(reader.GetOrdinal("approved_at")) ? null : reader.GetDateTime(reader.GetOrdinal("approved_at")),
        Status = reader.GetString(reader.GetOrdinal("status")),
        Reason = reader.IsDBNull(reader.GetOrdinal("reason")) ? null : reader.GetString(reader.GetOrdinal("reason")),
        CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at"))
    };
}

/// <summary>
/// S116 / TASK-11601 — one row of the scope-bounded admin enumeration
/// (<see cref="OvertimePreApprovalRepository.GetAllScopedWithEmployeeNamesAsync"/>): the
/// pre-approval entity + the employee's <c>users.display_name</c> from the SAME admission join.
/// <paramref name="EmployeeName"/> is NON-NULL by construction — the join's
/// <c>is_active = TRUE</c> predicate guarantees a live users row (display_name is NOT NULL).
/// </summary>
public sealed record OvertimePreApprovalAdminRow(
    OvertimePreApproval Approval,
    string EmployeeName);
