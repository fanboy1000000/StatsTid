using Npgsql;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Security;

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

    public async Task<IReadOnlyList<ApprovalPeriod>> GetByMonthAndOrgPathAsync(
        string orgPathPrefix, int year, int month, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        var monthStart = new DateOnly(year, month, 1);
        var nextMonthStart = monthStart.AddMonths(1);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT ap.* FROM approval_periods ap
            JOIN organizations o ON ap.org_id = o.org_id
            WHERE o.materialized_path LIKE @pathPrefix AND ap.period_start < @nextMonthStart AND ap.period_end >= @monthStart
            ORDER BY ap.period_start
            """, conn);
        cmd.Parameters.AddWithValue("pathPrefix", orgPathPrefix + "%");
        cmd.Parameters.AddWithValue("monthStart", monthStart);
        cmd.Parameters.AddWithValue("nextMonthStart", nextMonthStart);
        return await ReadPeriodsAsync(cmd, ct);
    }

    public async Task<IReadOnlyList<ApprovalPeriod>> GetByMonthAndOrgAsync(
        string orgId, int year, int month, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        var monthStart = new DateOnly(year, month, 1);
        var nextMonthStart = monthStart.AddMonths(1);
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM approval_periods WHERE org_id = @orgId AND period_start < @nextMonthStart AND period_end >= @monthStart ORDER BY period_start", conn);
        cmd.Parameters.AddWithValue("orgId", orgId);
        cmd.Parameters.AddWithValue("monthStart", monthStart);
        cmd.Parameters.AddWithValue("nextMonthStart", nextMonthStart);
        return await ReadPeriodsAsync(cmd, ct);
    }

    /// <summary>
    /// Returns approval periods (any status) for a given month for employees where the
    /// given actor is the designated approver (ACTING-precedence), intersected with the
    /// actor's org scope.
    /// </summary>
    public async Task<List<ApprovalPeriod>> GetByMonthForDesignatedReportsAsync(
        string actorId, IReadOnlyList<RoleScope> actorScopes, int year, int month, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);

        var monthStart = new DateOnly(year, month, 1);
        var nextMonthStart = monthStart.AddMonths(1);

        // Build org-scope filter dynamically from actorScopes.
        var hasGlobal = actorScopes.Any(s => s.ScopeType == "GLOBAL");
        var orgScopeClause = "";
        var orgParams = new List<NpgsqlParameter>();

        if (!hasGlobal)
        {
            var conditions = new List<string>();
            var paramIndex = 0;

            foreach (var scope in actorScopes)
            {
                if (scope.OrgId is null) continue;

                if (scope.ScopeType == "ORG_AND_DESCENDANTS")
                {
                    var paramName = $"@scopeOrgId_{paramIndex}";
                    conditions.Add($"o.materialized_path LIKE (SELECT materialized_path FROM organizations WHERE org_id = {paramName}) || '%'");
                    orgParams.Add(new NpgsqlParameter($"scopeOrgId_{paramIndex}", scope.OrgId));
                    paramIndex++;
                }
                else if (scope.ScopeType == "ORG_ONLY")
                {
                    var paramName = $"@orgId_{paramIndex}";
                    conditions.Add($"o.org_id = {paramName}");
                    orgParams.Add(new NpgsqlParameter($"orgId_{paramIndex}", scope.OrgId));
                    paramIndex++;
                }
            }

            if (conditions.Count == 0)
                return new List<ApprovalPeriod>(); // No org coverage → no results.

            orgScopeClause = $"AND ({string.Join(" OR ", conditions)})";
        }

        var sql = $"""
            WITH RECURSIVE managed_employees AS (
                -- Direct reports (where actor is designated approver per ACTING-precedence)
                SELECT rl.employee_id
                FROM reporting_lines rl
                LEFT JOIN reporting_lines acting ON acting.employee_id = rl.employee_id
                    AND acting.relationship = 'ACTING'
                    AND acting.effective_to IS NULL
                WHERE rl.manager_id = @actorId
                  AND rl.effective_to IS NULL
                  AND (
                      rl.relationship = 'ACTING'
                      OR (rl.relationship = 'PRIMARY' AND acting.reporting_line_id IS NULL)
                  )
                UNION
                -- Transitive reports (reports of reports, following ACTING-precedence chain)
                SELECT rl2.employee_id
                FROM reporting_lines rl2
                JOIN managed_employees me ON rl2.manager_id = me.employee_id
                LEFT JOIN reporting_lines acting2 ON acting2.employee_id = rl2.employee_id
                    AND acting2.relationship = 'ACTING'
                    AND acting2.effective_to IS NULL
                WHERE rl2.effective_to IS NULL
                  AND (
                      rl2.relationship = 'ACTING' AND rl2.manager_id = me.employee_id
                      OR (rl2.relationship = 'PRIMARY' AND acting2.reporting_line_id IS NULL)
                  )
            )
            SELECT DISTINCT ap.* FROM approval_periods ap
            JOIN managed_employees me ON me.employee_id = ap.employee_id
            JOIN organizations o ON o.org_id = ap.org_id
            WHERE ap.period_start < @nextMonthStart AND ap.period_end >= @monthStart
              {orgScopeClause}
            ORDER BY ap.period_start
            """;

        // CA2100 suppression: orgScopeClause is constructed entirely from RoleScope enum values
        // and parameterized placeholders (@pathPrefix_N, @orgId_N); no user input is string-concatenated.
#pragma warning disable CA2100
        await using var cmd = new NpgsqlCommand(sql, conn);
#pragma warning restore CA2100
        cmd.Parameters.AddWithValue("actorId", actorId);
        cmd.Parameters.AddWithValue("monthStart", monthStart);
        cmd.Parameters.AddWithValue("nextMonthStart", nextMonthStart);
        foreach (var p in orgParams)
            cmd.Parameters.Add(p);

        var periods = new List<ApprovalPeriod>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            periods.Add(ReadPeriod(reader));
        return periods;
    }

    /// <summary>
    /// Returns pending approval periods for employees where the given actor is the
    /// designated approver (ACTING-precedence), intersected with the actor's org scope.
    /// Uses a single SQL query with ACTING-precedence logic: includes the employee if the
    /// actor holds an ACTING line (always takes precedence), or a PRIMARY line when no
    /// active ACTING line exists for that employee.
    /// </summary>
    public async Task<List<ApprovalPeriod>> GetPendingForDesignatedReportsAsync(
        string actorId, IReadOnlyList<RoleScope> actorScopes, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);

        // Build org-scope filter dynamically from actorScopes.
        var hasGlobal = actorScopes.Any(s => s.ScopeType == "GLOBAL");
        var orgScopeClause = "";
        var orgParams = new List<NpgsqlParameter>();

        if (!hasGlobal)
        {
            var conditions = new List<string>();
            var paramIndex = 0;

            foreach (var scope in actorScopes)
            {
                if (scope.OrgId is null) continue;

                if (scope.ScopeType == "ORG_AND_DESCENDANTS")
                {
                    var paramName = $"@scopeOrgId_{paramIndex}";
                    conditions.Add($"o.materialized_path LIKE (SELECT materialized_path FROM organizations WHERE org_id = {paramName}) || '%'");
                    orgParams.Add(new NpgsqlParameter($"scopeOrgId_{paramIndex}", scope.OrgId));
                    paramIndex++;
                }
                else if (scope.ScopeType == "ORG_ONLY")
                {
                    var paramName = $"@orgId_{paramIndex}";
                    conditions.Add($"o.org_id = {paramName}");
                    orgParams.Add(new NpgsqlParameter($"orgId_{paramIndex}", scope.OrgId));
                    paramIndex++;
                }
            }

            if (conditions.Count == 0)
                return new List<ApprovalPeriod>(); // No org coverage → no results.

            orgScopeClause = $"AND ({string.Join(" OR ", conditions)})";
        }

        var sql = $"""
            WITH RECURSIVE managed_employees AS (
                -- Direct reports (where actor is designated approver per ACTING-precedence)
                SELECT rl.employee_id
                FROM reporting_lines rl
                LEFT JOIN reporting_lines acting ON acting.employee_id = rl.employee_id
                    AND acting.relationship = 'ACTING'
                    AND acting.effective_to IS NULL
                WHERE rl.manager_id = @actorId
                  AND rl.effective_to IS NULL
                  AND (
                      rl.relationship = 'ACTING'
                      OR (rl.relationship = 'PRIMARY' AND acting.reporting_line_id IS NULL)
                  )
                UNION
                -- Transitive reports (reports of reports, following ACTING-precedence chain)
                SELECT rl2.employee_id
                FROM reporting_lines rl2
                JOIN managed_employees me ON rl2.manager_id = me.employee_id
                LEFT JOIN reporting_lines acting2 ON acting2.employee_id = rl2.employee_id
                    AND acting2.relationship = 'ACTING'
                    AND acting2.effective_to IS NULL
                WHERE rl2.effective_to IS NULL
                  AND (
                      rl2.relationship = 'ACTING' AND rl2.manager_id = me.employee_id
                      OR (rl2.relationship = 'PRIMARY' AND acting2.reporting_line_id IS NULL)
                  )
            )
            SELECT DISTINCT ap.* FROM approval_periods ap
            JOIN managed_employees me ON me.employee_id = ap.employee_id
            JOIN organizations o ON o.org_id = ap.org_id
            WHERE ap.status IN ('SUBMITTED', 'EMPLOYEE_APPROVED')
              {orgScopeClause}
            ORDER BY ap.period_start
            """;

        // CA2100 suppression: orgScopeClause is constructed entirely from RoleScope enum values
        // and parameterized placeholders (@pathPrefix_N, @orgId_N); no user input is string-concatenated.
#pragma warning disable CA2100
        await using var cmd = new NpgsqlCommand(sql, conn);
#pragma warning restore CA2100
        cmd.Parameters.AddWithValue("actorId", actorId);
        foreach (var p in orgParams)
            cmd.Parameters.Add(p);

        var periods = new List<ApprovalPeriod>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            periods.Add(ReadPeriod(reader));
        return periods;
    }

    /// <summary>
    /// Self-managed overload of <see cref="CreateAsync(NpgsqlConnection, NpgsqlTransaction, ApprovalPeriod, CancellationToken)"/>:
    /// opens its own connection (no transaction). For atomic outbox + audit + state mutation
    /// (ADR-018 D3) call the in-transaction sibling.
    /// </summary>
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

    /// <summary>
    /// In-transaction sibling overload of
    /// <see cref="CreateAsync(ApprovalPeriod, CancellationToken)"/>. Reuses the caller-supplied
    /// <paramref name="conn"/> + <paramref name="tx"/> so the caller can extend the same
    /// transaction across audit + outbox writes (ADR-018 D3 transactional-outbox contract).
    /// The caller commits or rolls back; this method does NOT.
    /// </summary>
    public async Task<Guid> CreateAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        ApprovalPeriod period, CancellationToken ct = default)
    {
        var periodId = Guid.NewGuid();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO approval_periods (period_id, employee_id, org_id, period_start, period_end, period_type, status, agreement_code, ok_version)
            VALUES (@periodId, @employeeId, @orgId, @periodStart, @periodEnd, @periodType, @status, @agreementCode, @okVersion)
            """, conn, tx);
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
        string? rejectionReason = null,
        string? designatedApproverId = null, string? approvalMethod = null,
        bool explicitFallbackConfirmation = false,
        CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = BuildUpdateStatusCommand(conn, null, periodId, status, actorId, rejectionReason, designatedApproverId, approvalMethod, explicitFallbackConfirmation);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// In-transaction sibling overload of <see cref="UpdateStatusAsync(Guid, string, string?, string?, string?, string?, bool, CancellationToken)"/>.
    /// </summary>
    public async Task UpdateStatusAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid periodId, string status, string? actorId = null,
        string? rejectionReason = null,
        string? designatedApproverId = null, string? approvalMethod = null,
        bool explicitFallbackConfirmation = false,
        CancellationToken ct = default)
    {
        await using var cmd = BuildUpdateStatusCommand(conn, tx, periodId, status, actorId, rejectionReason, designatedApproverId, approvalMethod, explicitFallbackConfirmation);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static NpgsqlCommand BuildUpdateStatusCommand(
        NpgsqlConnection conn, NpgsqlTransaction? tx,
        Guid periodId, string status, string? actorId, string? rejectionReason,
        string? designatedApproverId = null, string? approvalMethod = null,
        bool explicitFallbackConfirmation = false)
    {
        var sql = status switch
        {
            "SUBMITTED" => "UPDATE approval_periods SET status = 'SUBMITTED', submitted_at = NOW(), submitted_by = @actorId WHERE period_id = @periodId",
            "EMPLOYEE_APPROVED" => "UPDATE approval_periods SET status = 'EMPLOYEE_APPROVED', employee_approved_at = NOW(), employee_approved_by = @actorId WHERE period_id = @periodId",
            "APPROVED" => "UPDATE approval_periods SET status = 'APPROVED', approved_by = @actorId, approved_at = NOW(), designated_approver_id = @designatedApproverId, approval_method = @approvalMethod, explicit_fallback_confirmation = @explicitFallback WHERE period_id = @periodId",
            "REJECTED" => "UPDATE approval_periods SET status = 'REJECTED', approved_by = @actorId, approved_at = NOW(), rejection_reason = @rejectionReason, designated_approver_id = @designatedApproverId, approval_method = @approvalMethod, explicit_fallback_confirmation = @explicitFallback WHERE period_id = @periodId",
            "DRAFT" => "UPDATE approval_periods SET status = 'DRAFT', submitted_at = NULL, submitted_by = NULL, approved_by = NULL, approved_at = NULL, rejection_reason = NULL, employee_approved_at = NULL, employee_approved_by = NULL, explicit_fallback_confirmation = FALSE WHERE period_id = @periodId",
            _ => throw new ArgumentException($"Invalid status: {status}")
        };

        var cmd = tx is null ? new NpgsqlCommand(sql, conn) : new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("periodId", periodId);
        cmd.Parameters.AddWithValue("actorId", (object?)actorId ?? DBNull.Value);
        if (status == "REJECTED")
            cmd.Parameters.AddWithValue("rejectionReason", (object?)rejectionReason ?? DBNull.Value);
        if (status is "APPROVED" or "REJECTED")
        {
            cmd.Parameters.AddWithValue("designatedApproverId", (object?)designatedApproverId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("approvalMethod", (object?)approvalMethod ?? DBNull.Value);
            cmd.Parameters.AddWithValue("explicitFallback", explicitFallbackConfirmation);
        }
        return cmd;
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

    /// <summary>
    /// In-transaction sibling overload of <see cref="UpdateDeadlinesAsync(Guid, DateOnly?, DateOnly?, CancellationToken)"/>.
    /// </summary>
    public async Task UpdateDeadlinesAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid periodId, DateOnly? employeeDeadline, DateOnly? managerDeadline, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            "UPDATE approval_periods SET employee_deadline = @employeeDeadline, manager_deadline = @managerDeadline WHERE period_id = @periodId",
            conn, tx);
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

    /// <summary>
    /// In-transaction sibling overload of <see cref="AppendAuditAsync(Guid, string, string, string, string?, CancellationToken)"/>.
    /// </summary>
    public async Task AppendAuditAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid periodId, string action, string actorId, string actorRole,
        string? comment = null, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO approval_audit (period_id, action, actor_id, actor_role, comment)
            VALUES (@periodId, @action, @actorId, @actorRole, @comment)
            """, conn, tx);
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
        DesignatedApproverId = reader.IsDBNull(reader.GetOrdinal("designated_approver_id")) ? null : reader.GetString(reader.GetOrdinal("designated_approver_id")),
        ApprovalMethod = reader.IsDBNull(reader.GetOrdinal("approval_method")) ? null : reader.GetString(reader.GetOrdinal("approval_method")),
        ExplicitFallbackConfirmation = !reader.IsDBNull(reader.GetOrdinal("explicit_fallback_confirmation")) && reader.GetBoolean(reader.GetOrdinal("explicit_fallback_confirmation")),
        CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at"))
    };
}
