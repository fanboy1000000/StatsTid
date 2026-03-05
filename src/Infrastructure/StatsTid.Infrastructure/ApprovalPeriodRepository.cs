using Npgsql;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Infrastructure;

public sealed class ApprovalPeriodRepository
{
    private readonly DbConnectionFactory _connectionFactory;

    public ApprovalPeriodRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<ApprovalPeriod?> GetByIdAsync(Guid periodId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM approval_periods WHERE period_id = @periodId", conn);
        cmd.Parameters.AddWithValue("periodId", periodId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadPeriod(reader) : null;
    }

    public async Task<IReadOnlyList<ApprovalPeriod>> GetByEmployeeAsync(string employeeId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM approval_periods WHERE employee_id = @employeeId ORDER BY period_start DESC", conn);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        return await ReadPeriodsAsync(cmd, ct);
    }

    public async Task<ApprovalPeriod?> GetByEmployeeAndPeriodAsync(
        string employeeId, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM approval_periods WHERE employee_id = @employeeId AND period_start = @periodStart AND period_end = @periodEnd", conn);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("periodStart", periodStart);
        cmd.Parameters.AddWithValue("periodEnd", periodEnd);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadPeriod(reader) : null;
    }

    public async Task<IReadOnlyList<ApprovalPeriod>> GetPendingByOrgAsync(string orgId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM approval_periods WHERE org_id = @orgId AND status IN ('SUBMITTED', 'EMPLOYEE_APPROVED') ORDER BY period_start", conn);
        cmd.Parameters.AddWithValue("orgId", orgId);
        return await ReadPeriodsAsync(cmd, ct);
    }

    public async Task<IReadOnlyList<ApprovalPeriod>> GetPendingByOrgPathAsync(string orgPathPrefix, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT ap.* FROM approval_periods ap
            JOIN organizations o ON ap.org_id = o.org_id
            WHERE o.materialized_path LIKE @pathPrefix AND ap.status IN ('SUBMITTED', 'EMPLOYEE_APPROVED')
            ORDER BY ap.period_start
            """, conn);
        cmd.Parameters.AddWithValue("pathPrefix", orgPathPrefix + "%");
        return await ReadPeriodsAsync(cmd, ct);
    }

    public async Task<Guid> CreateAsync(ApprovalPeriod period, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        var periodId = Guid.NewGuid();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO approval_periods (period_id, employee_id, org_id, period_start, period_end, period_type, status, agreement_code, ok_version)
            VALUES (@periodId, @employeeId, @orgId, @periodStart, @periodEnd, @periodType, @status, @agreementCode, @okVersion)
            """, conn);
        cmd.Parameters.AddWithValue("periodId", periodId);
        cmd.Parameters.AddWithValue("employeeId", period.EmployeeId);
        cmd.Parameters.AddWithValue("orgId", period.OrgId);
        cmd.Parameters.AddWithValue("periodStart", period.PeriodStart);
        cmd.Parameters.AddWithValue("periodEnd", period.PeriodEnd);
        cmd.Parameters.AddWithValue("periodType", period.PeriodType);
        cmd.Parameters.AddWithValue("status", period.Status);
        cmd.Parameters.AddWithValue("agreementCode", period.AgreementCode);
        cmd.Parameters.AddWithValue("okVersion", period.OkVersion);
        await cmd.ExecuteNonQueryAsync(ct);
        return periodId;
    }

    public async Task UpdateStatusAsync(
        Guid periodId, string status, string? actorId = null,
        string? rejectionReason = null, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);

        var sql = status switch
        {
            "SUBMITTED" => "UPDATE approval_periods SET status = 'SUBMITTED', submitted_at = NOW(), submitted_by = @actorId WHERE period_id = @periodId",
            "EMPLOYEE_APPROVED" => "UPDATE approval_periods SET status = 'EMPLOYEE_APPROVED', employee_approved_at = NOW(), employee_approved_by = @actorId WHERE period_id = @periodId",
            "APPROVED" => "UPDATE approval_periods SET status = 'APPROVED', approved_by = @actorId, approved_at = NOW() WHERE period_id = @periodId",
            "REJECTED" => "UPDATE approval_periods SET status = 'REJECTED', approved_by = @actorId, approved_at = NOW(), rejection_reason = @rejectionReason WHERE period_id = @periodId",
            "DRAFT" => "UPDATE approval_periods SET status = 'DRAFT', submitted_at = NULL, submitted_by = NULL, approved_by = NULL, approved_at = NULL, rejection_reason = NULL, employee_approved_at = NULL, employee_approved_by = NULL WHERE period_id = @periodId",
            _ => throw new ArgumentException($"Invalid status: {status}")
        };

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("periodId", periodId);
        cmd.Parameters.AddWithValue("actorId", (object?)actorId ?? DBNull.Value);
        if (status == "REJECTED")
            cmd.Parameters.AddWithValue("rejectionReason", (object?)rejectionReason ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task UpdateDeadlinesAsync(
        Guid periodId, DateOnly? employeeDeadline, DateOnly? managerDeadline, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "UPDATE approval_periods SET employee_deadline = @employeeDeadline, manager_deadline = @managerDeadline WHERE period_id = @periodId", conn);
        cmd.Parameters.AddWithValue("periodId", periodId);
        cmd.Parameters.AddWithValue("employeeDeadline", (object?)employeeDeadline ?? DBNull.Value);
        cmd.Parameters.AddWithValue("managerDeadline", (object?)managerDeadline ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task AppendAuditAsync(
        Guid periodId, string action, string actorId, string actorRole,
        string? comment = null, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO approval_audit (period_id, action, actor_id, actor_role, comment)
            VALUES (@periodId, @action, @actorId, @actorRole, @comment)
            """, conn);
        cmd.Parameters.AddWithValue("periodId", periodId);
        cmd.Parameters.AddWithValue("action", action);
        cmd.Parameters.AddWithValue("actorId", actorId);
        cmd.Parameters.AddWithValue("actorRole", actorRole);
        cmd.Parameters.AddWithValue("comment", (object?)comment ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<IReadOnlyList<ApprovalPeriod>> ReadPeriodsAsync(NpgsqlCommand cmd, CancellationToken ct)
    {
        var periods = new List<ApprovalPeriod>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            periods.Add(ReadPeriod(reader));
        return periods;
    }

    private static ApprovalPeriod ReadPeriod(NpgsqlDataReader reader) => new()
    {
        PeriodId = reader.GetGuid(reader.GetOrdinal("period_id")),
        EmployeeId = reader.GetString(reader.GetOrdinal("employee_id")),
        OrgId = reader.GetString(reader.GetOrdinal("org_id")),
        PeriodStart = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("period_start"))),
        PeriodEnd = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("period_end"))),
        PeriodType = reader.GetString(reader.GetOrdinal("period_type")),
        Status = reader.GetString(reader.GetOrdinal("status")),
        SubmittedAt = reader.IsDBNull(reader.GetOrdinal("submitted_at")) ? null : reader.GetDateTime(reader.GetOrdinal("submitted_at")),
        SubmittedBy = reader.IsDBNull(reader.GetOrdinal("submitted_by")) ? null : reader.GetString(reader.GetOrdinal("submitted_by")),
        ApprovedBy = reader.IsDBNull(reader.GetOrdinal("approved_by")) ? null : reader.GetString(reader.GetOrdinal("approved_by")),
        ApprovedAt = reader.IsDBNull(reader.GetOrdinal("approved_at")) ? null : reader.GetDateTime(reader.GetOrdinal("approved_at")),
        RejectionReason = reader.IsDBNull(reader.GetOrdinal("rejection_reason")) ? null : reader.GetString(reader.GetOrdinal("rejection_reason")),
        EmployeeApprovedAt = reader.IsDBNull(reader.GetOrdinal("employee_approved_at")) ? null : reader.GetDateTime(reader.GetOrdinal("employee_approved_at")),
        EmployeeApprovedBy = reader.IsDBNull(reader.GetOrdinal("employee_approved_by")) ? null : reader.GetString(reader.GetOrdinal("employee_approved_by")),
        EmployeeDeadline = reader.IsDBNull(reader.GetOrdinal("employee_deadline")) ? null : DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("employee_deadline"))),
        ManagerDeadline = reader.IsDBNull(reader.GetOrdinal("manager_deadline")) ? null : DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("manager_deadline"))),
        AgreementCode = reader.GetString(reader.GetOrdinal("agreement_code")),
        OkVersion = reader.GetString(reader.GetOrdinal("ok_version")),
        CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at"))
    };
}
