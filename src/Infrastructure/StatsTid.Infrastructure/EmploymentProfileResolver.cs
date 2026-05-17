using Npgsql;
using StatsTid.SharedKernel.Interfaces;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Infrastructure;

/// <summary>
/// S33 / TASK-3301 — concrete <see cref="IEmploymentProfileResolver"/> implementation
/// for ADR-023. Returns the fully-hydrated <see cref="EmploymentProfile"/> for a given
/// (employee_id, as-of-date) by joining the dated <c>employee_profiles</c> row covering
/// <paramref name="asOfDate"/> against the live <c>users</c> row.
///
/// <para>
/// <b>Dated vs live split (ADR-023 D1/D2).</b> Dated fields (<c>weekly_norm_hours</c>,
/// <c>part_time_fraction</c>, <c>position</c>) come from <c>employee_profiles</c> with
/// the end-exclusive predicate <c>effective_from &lt;= asOfDate AND (effective_to IS
/// NULL OR effective_to &gt; asOfDate)</c> — the same temporal predicate the S31
/// WageTypeMapping versioned-history sub-sprint uses for ADR-018 D14 export-time
/// effective-date lookup. Live fields (<c>agreement_code</c>, <c>ok_version</c>,
/// <c>employment_category</c>, <c>primary_org_id</c>) are joined live from
/// <c>users</c> per the documented determinism gap in ADR-023 D2 (Phase 4e
/// launch-blocking).
/// </para>
///
/// <para>
/// <b>Fail-closed semantic (ADR-023 D3).</b> Returns <c>null</c> on no match.
/// Never throws on missing-row — the caller (PCS / ComplianceEndpoints) decides
/// whether to throw <see cref="StatsTid.SharedKernel.Exceptions.EmployeeProfileNotFoundException"/>
/// (fail-closed) or to fall back to a default (legacy callers).
/// </para>
///
/// <para>
/// <b>Self-managed read-only.</b> Unlike <see cref="EmployeeProfileRepository"/>, this
/// resolver is a hot-path read used per-segment by PCS; it owns its own connection
/// and is NOT a candidate for (conn, tx) overloading (ADR-018 D5 does not apply to
/// pure reads). PostgreSQL connection pooling + the live-row index on
/// <c>employee_profiles(employee_id, effective_to)</c> handle the throughput.
/// </para>
///
/// <para>
/// <b>IsPartTime is computed (S31 cycle 2 absorption).</b> The schema does NOT have an
/// <c>is_part_time</c> column; <see cref="EmploymentProfile.IsPartTime"/> is derived
/// as <c>part_time_fraction &lt; 1.0m</c> when constructing the in-memory profile,
/// matching the established pattern in <see cref="EmployeeProfileRepository"/>.
/// </para>
/// </summary>
public sealed class EmploymentProfileResolver : IEmploymentProfileResolver
{
    private readonly DbConnectionFactory _dbFactory;

    public EmploymentProfileResolver(DbConnectionFactory dbFactory)
    {
        _dbFactory = dbFactory;
    }

    /// <inheritdoc />
    public async Task<EmploymentProfile?> GetByEmployeeIdAtAsync(
        string employeeId, DateOnly asOfDate, CancellationToken ct = default)
    {
        // Dated fields from employee_profiles via the end-exclusive temporal predicate;
        // live fields from users per ADR-023 D2 (documented determinism gap — Phase 4e
        // launch-blocking work will move agreement_code / ok_version / employment_category /
        // primary_org_id into a dated history table too).
        const string sql =
            """
            SELECT
                ep.weekly_norm_hours,
                ep.part_time_fraction,
                ep.position,
                u.agreement_code,
                u.ok_version,
                u.employment_category,
                u.primary_org_id
            FROM employee_profiles ep
            INNER JOIN users u ON u.user_id = ep.employee_id
            WHERE ep.employee_id = @employeeId
              AND ep.effective_from <= @asOfDate
              AND (ep.effective_to IS NULL OR ep.effective_to > @asOfDate)
            """;

        await using var conn = _dbFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("asOfDate", asOfDate);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            // ADR-023 D3: never throw on missing-row; caller decides fail-closed
            // vs fallback semantic.
            return null;
        }

        var partTimeFraction = reader.GetDecimal(reader.GetOrdinal("part_time_fraction"));
        return new EmploymentProfile
        {
            EmployeeId = employeeId,
            WeeklyNormHours = reader.GetDecimal(reader.GetOrdinal("weekly_norm_hours")),
            PartTimeFraction = partTimeFraction,
            Position = reader.IsDBNull(reader.GetOrdinal("position"))
                ? null
                : reader.GetString(reader.GetOrdinal("position")),
            AgreementCode = reader.GetString(reader.GetOrdinal("agreement_code")),
            OkVersion = reader.GetString(reader.GetOrdinal("ok_version")),
            EmploymentCategory = reader.GetString(reader.GetOrdinal("employment_category")),
            OrgId = reader.GetString(reader.GetOrdinal("primary_org_id")),
            // S31 cycle 2 absorption: IsPartTime is computed, not stored.
            IsPartTime = partTimeFraction < 1.0m,
        };
    }
}
