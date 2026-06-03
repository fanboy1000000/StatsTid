using Npgsql;
using StatsTid.SharedKernel.Calendar;
using StatsTid.SharedKernel.Exceptions;
using StatsTid.SharedKernel.Interfaces;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Infrastructure;

/// <summary>
/// S33 / TASK-3301 — concrete <see cref="IEmploymentProfileResolver"/> implementation
/// for ADR-023. Returns the fully-hydrated <see cref="EmploymentProfile"/> for a given
/// (employee_id, as-of-date) by joining the dated <c>employee_profiles</c> row covering
/// <paramref name="asOfDate"/> against the live <c>users</c> row, then layering the
/// dated <c>agreement_code</c> from <see cref="UserAgreementCodeRepository"/> on top
/// (S34 / TASK-3406 cutover — closes the PCS-replay leg of the ADR-023 D2
/// determinism gap).
///
/// <para>
/// <b>Dated vs live split (ADR-023 D1/D2, post-TASK-3406).</b> Dated fields
/// (<c>weekly_norm_hours</c>, <c>part_time_fraction</c>, <c>position</c>) come from
/// <c>employee_profiles</c> with the end-exclusive predicate
/// <c>effective_from &lt;= asOfDate AND (effective_to IS NULL OR effective_to &gt; asOfDate)</c>
/// — the same temporal predicate the S29 WageTypeMapping versioned-history sub-sprint
/// uses for ADR-018 D14 export-time effective-date lookup. <c>AgreementCode</c> is now
/// dated too, sourced via <see cref="UserAgreementCodeRepository.GetByUserIdAtAsync"/>
/// (TASK-3402) under the identical end-exclusive predicate. The remaining live fields
/// (<c>ok_version</c>, <c>employment_category</c>, <c>primary_org_id</c>) stay joined
/// live from <c>users</c> — they are out of S34 scope and remain documented determinism
/// gaps tracked for future Phase 4e iterations.
/// </para>
///
/// <para>
/// <b>Read-consistency note (Step 0b Codex cycle 2 absorption).</b> The cutover changes
/// the resolver from a single-statement snapshot (one JOIN) to TWO queries on the same
/// <paramref name="asOfDate"/>: (1) the existing JOIN of <c>employee_profiles</c> (dated)
/// + <c>users</c> (live tail of fields), and (2) the new
/// <see cref="UserAgreementCodeRepository.GetByUserIdAtAsync"/> call for the dated
/// agreement code. Under PostgreSQL READ COMMITTED the two reads could see different
/// snapshots if a writer commits between them. This is acceptable for S34 because:
/// (a) writes flow through atomic <c>(conn, tx)</c> paths that update both
/// <c>users.agreement_code</c> cache and <c>user_agreement_codes</c> in the same
/// transaction (TASK-3407 admin PUT/POST contract — see
/// <see cref="UserAgreementCodeRepository"/> canonical-write contract); (b) the resolver
/// returns from a single point-in-time view per call — a concurrent commit during one
/// call is no different from the same commit happening 1 ms later for the next call,
/// so a momentarily-mixed snapshot is observationally identical to picking either side
/// of the commit boundary; (c) PCS retroactive replay reads frozen historical rows that
/// do not race with current writes.
/// </para>
///
/// <para>
/// <b>Fail-closed semantic (ADR-023 D3).</b> Returns <c>null</c> on no match for the
/// <c>employee_profiles</c> JOIN. Never throws on missing dated-profile row — the caller
/// (PCS / ComplianceEndpoints) decides whether to throw
/// <see cref="EmployeeProfileNotFoundException"/> (fail-closed) or to fall back to a
/// default (legacy callers).
/// </para>
///
/// <para>
/// <b>Data-integrity fail-loud (S34 / TASK-3406).</b> If the
/// <c>employee_profiles</c> JOIN succeeds but
/// <see cref="UserAgreementCodeRepository.GetByUserIdAtAsync"/> returns <c>null</c> for
/// the same (user_id, asOfDate), the resolver throws
/// <see cref="EmployeeProfileNotFoundException"/>. This is an inconsistent-state
/// violation that should never occur post-backfill seeder (TASK-3403 seeds at
/// <c>'0001-01-01'</c> covering every historical period for every user with an
/// <c>employee_profiles</c> row). Surfacing it as an exception keeps the data-integrity
/// contract loud rather than silently substituting an empty string and corrupting PCS
/// replays.
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
    private readonly UserAgreementCodeRepository _userAgreementCodes;

    public EmploymentProfileResolver(
        DbConnectionFactory dbFactory,
        UserAgreementCodeRepository userAgreementCodes)
    {
        _dbFactory = dbFactory;
        _userAgreementCodes = userAgreementCodes;
    }

    /// <inheritdoc />
    public async Task<EmploymentProfile?> GetByEmployeeIdAtAsync(
        string employeeId, DateOnly asOfDate, CancellationToken ct = default)
    {
        // Dated fields from employee_profiles via the end-exclusive temporal predicate;
        // remaining live fields from users per ADR-023 D2 (documented determinism gap —
        // future Phase 4e work will move ok_version / employment_category /
        // primary_org_id into dated history tables too). agreement_code is NO LONGER
        // joined here — TASK-3406 cutover sources it from UserAgreementCodeRepository
        // below to close the PCS-replay leg of the gap.
        const string sql =
            """
            SELECT
                ep.part_time_fraction,
                ep.position,
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

        decimal partTimeFraction;
        string? position;
        string okVersion;
        string employmentCategory;
        string orgId;

        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct))
            {
                // ADR-023 D3: never throw on missing-row; caller decides fail-closed
                // vs fallback semantic.
                return null;
            }

            partTimeFraction = reader.GetDecimal(reader.GetOrdinal("part_time_fraction"));
            position = reader.IsDBNull(reader.GetOrdinal("position"))
                ? null
                : reader.GetString(reader.GetOrdinal("position"));
            okVersion = reader.GetString(reader.GetOrdinal("ok_version"));
            employmentCategory = reader.GetString(reader.GetOrdinal("employment_category"));
            orgId = reader.GetString(reader.GetOrdinal("primary_org_id"));
        }

        // S34 / TASK-3406 — dated agreement-code lookup. The two-query read-consistency
        // note in the class xmldoc explains why the same-snapshot guarantee is not needed
        // here. We pass employeeId straight through: users.user_id == employee_profiles.employee_id
        // per the JOIN above, and user_agreement_codes.user_id is the same identifier.
        var datedAgreementCode = await _userAgreementCodes.GetByUserIdAtAsync(
            employeeId, asOfDate, ct);

        if (datedAgreementCode is null)
        {
            // Data-integrity fail-loud: employee_profiles row exists for this
            // (employee_id, asOfDate) but user_agreement_codes has no row covering the
            // same date. Should never occur post-TASK-3403 backfill seeder (which seeds
            // at '0001-01-01' covering all past periods). Surfacing as an exception
            // keeps the contract loud rather than substituting an empty string and
            // silently corrupting PCS replays / payroll export.
            throw new EmployeeProfileNotFoundException(employeeId, asOfDate);
        }

        return new EmploymentProfile
        {
            EmployeeId = employeeId,
            PartTimeFraction = partTimeFraction,
            Position = position,
            AgreementCode = datedAgreementCode,
            OkVersion = okVersion,
            EmploymentCategory = employmentCategory,
            OrgId = orgId,
            // S31 cycle 2 absorption: IsPartTime is computed, not stored.
            IsPartTime = partTimeFraction < 1.0m,
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FractionPeriod>> GetFractionHistoryAsync(
        string employeeId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        // S62 / TASK-6202 — single-table employee_profiles scan returning EVERY period that
        // overlaps the half-open window [from, to). This is the ROW-OVERLAP predicate
        // (effective_from < @to, NOT the point-in-time <= asOfDate of GetByEmployeeIdAtAsync),
        // so the full piecewise-accrual history is fetched in ONE query rather than an N+1
        // per-month dated-lookup loop. NO users JOIN, NO agreement-code lookup, NO throw —
        // fraction history needs only part_time_fraction + the temporal columns; the
        // empty-history polarity (Skema fail-closes / Balance defaults to 1.0) is the caller's.
        const string sql =
            """
            SELECT ep.effective_from, ep.effective_to, ep.part_time_fraction
            FROM employee_profiles ep
            WHERE ep.employee_id = @employeeId
              AND ep.effective_from < @to
              AND (ep.effective_to IS NULL OR ep.effective_to > @from)
            ORDER BY ep.effective_from
            """;

        await using var conn = _dbFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("from", from);
        cmd.Parameters.AddWithValue("to", to);

        var periods = new List<FractionPeriod>();

        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var fromOrd = reader.GetOrdinal("effective_from");
        var toOrd = reader.GetOrdinal("effective_to");
        var fractionOrd = reader.GetOrdinal("part_time_fraction");

        while (await reader.ReadAsync(ct))
        {
            var effectiveFrom = reader.GetFieldValue<DateOnly>(fromOrd);
            DateOnly? effectiveTo = reader.IsDBNull(toOrd)
                ? null
                : reader.GetFieldValue<DateOnly>(toOrd);
            var fraction = reader.GetDecimal(fractionOrd);

            periods.Add(new FractionPeriod(effectiveFrom, effectiveTo, fraction));
        }

        // ADR-023 D3: empty list (never null, never throw) when no row overlaps the window.
        return periods;
    }
}
