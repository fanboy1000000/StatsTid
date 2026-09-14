using Npgsql;
using NpgsqlTypes;

namespace StatsTid.Infrastructure;

// ═══════════════════════════════════════════════════════════════════════════════════════════
// S141 / TASK-14113 (refinement C2) — the EMPLOYMENT HISTORY reads. READ-ONLY: this file
// contains no INSERT, UPDATE or DELETE.
//
// PLAIN-LANGUAGE WHAT, AND WHY IT DID NOT EXIST BEFORE. HR needs to answer one question about a
// person: "what has changed for this employee, and when did it take effect?" Until now there was
// no way to ask it. The system already STORES the answer — every profile change (job title,
// part-time fraction, employment category) and every agreement-code change is written as a DATED
// row with its own validity interval, so the history is sitting in two tables — but nothing read
// it back as a history. The one surface that looked like it might, the audit log, cannot
// substitute for two reasons, and both matter:
//
//   1. it has no subject filter — you cannot ask it for one employee; and
//   2. it orders by when a change was RECORDED, not by when it took EFFECT.
//
// Those two orderings genuinely differ. HR corrects March's part-time fraction in September: the
// audit log files that under September, because that is when somebody typed it. A person asking
// "what changed and when did it take effect" means March. This repository answers with the second
// ordering — `ORDER BY effective_from` — which is the whole point of the endpoint above it.
//
// FOUR RULES THIS FILE OBEYS, because breaking any of them is how a history view starts lying:
//
//  1. INTERVALS ARE END-EXCLUSIVE (ADR-018 D9). A row with effective_to = 2026-06-01 covers
//     2026-05-31 and NOT 2026-06-01 — the boundary day belongs to the successor. Every predicate
//     here is written that way, and the DTO above carries `effectiveTo` with the same meaning.
//     Reading it as inclusive would double-count a boundary day in every consumer.
//
//  2. NO ORG COLUMN IS READ HERE, AND THAT IS DELIBERATE. Neither `employee_profiles` nor
//     `user_agreement_codes` carries an organisation; the only organisation in play is the
//     subject's CURRENT home, `users.primary_org_id`, which the endpoint's
//     `OrgScopeValidator` call resolves. So the stamped-org drift that bit the S140 follow-up
//     reads (a transferred employee showing up for the OLD organisation's HR) is not merely
//     avoided here — it is structurally impossible, because there is no stamped org to drift.
//     This repository must therefore NEVER grow an org predicate of its own: the scope decision
//     belongs to the validator, made once, before we are called.
//
//  3. ONE DATE PER REQUEST (PAT-028). This repository holds no clock and takes no "today". The
//     endpoint computes the day once and derives each interval's status from it, so a request
//     crossing midnight cannot age one half of the answer against one day and the other half
//     against the next.
//
//  4. NO CONCURRENCY TOKEN LEAVES THIS FILE. The `version` column is deliberately NOT selected.
//     ADR-019 gives each aggregate ONE client token — for an employee's timeline that is the OPEN
//     row's version, handed out by the profile GET's ETag — and a history row's version is not it.
//     Selecting it here would put a plausible-looking-but-wrong If-Match value on a read-only
//     screen, and the first person to use it would silently write against the wrong row.
// ═══════════════════════════════════════════════════════════════════════════════════════════

/// <summary>
/// One dated <c>employee_profiles</c> interval, exactly as stored. <paramref name="EffectiveTo"/>
/// is END-EXCLUSIVE (ADR-018 D9); <c>null</c> means the interval is open-ended — it is the live
/// row and covers every day from <paramref name="EffectiveFrom"/> forward.
/// </summary>
public sealed record EmploymentProfileHistoryRow(
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    decimal PartTimeFraction,
    string? Position,
    string EmploymentCategory);

/// <summary>
/// One dated <c>user_agreement_codes</c> interval, exactly as stored. Same end-exclusive
/// convention as <see cref="EmploymentProfileHistoryRow"/>.
///
/// <para><b>No <c>ok_version</c> here, on purpose.</b> Since S137 (ADR-040 D4) the OK version is a
/// PURE FUNCTION of a date (<c>OkVersionResolver.ResolveVersion</c>), not a stored column — and an
/// agreement interval can SPAN an OK-version transition. Stamping one version onto an interval that
/// straddles the boundary would be a confident wrong answer, so the history reports the dated fact
/// it actually has (the agreement code) and leaves version resolution to the per-date resolver that
/// owns it.</para>
/// </summary>
public sealed record AgreementCodeHistoryRow(
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    string AgreementCode);

/// <summary>
/// S141 / TASK-14113 — the two RANGE READS behind <c>GET /api/hr/employees/{employeeId}/history</c>.
/// Both are plain range queries over records that already exist; this task adds NO schema and NO
/// index (see the index note on each method).
///
/// <para><b>Read-only, holds no clock, holds no state</b> beyond the connection factory ⇒
/// singleton-safe, the <c>HrFollowUpApprovalReadRepository</c> shape.</para>
/// </summary>
public sealed class EmploymentHistoryReadRepository
{
    private readonly DbConnectionFactory _dbFactory;

    public EmploymentHistoryReadRepository(DbConnectionFactory dbFactory)
        => _dbFactory = dbFactory;

    /// <summary>
    /// Every <c>employee_profiles</c> interval for <paramref name="employeeId"/> that OVERLAPS the
    /// window <c>[from, to)</c>, ordered by <c>effective_from</c> ascending — by when the change took
    /// EFFECT, which is the ordering the question "when did this take effect?" actually asks for.
    ///
    /// <para><b>Both bounds are optional and both default to unbounded.</b> <paramref name="from"/>
    /// <c>= null</c> means "from the beginning of the record"; <paramref name="to"/> <c>= null</c>
    /// means "to the end of the record, INCLUDING intervals that have not started yet" — see the
    /// endpoint's SCHEDULED discussion. The overlap test is end-exclusive on both sides (ADR-018 D9):
    /// an interval is in the window when it has not already ENDED at or before <paramref name="from"/>
    /// and it STARTS strictly before <paramref name="to"/>.</para>
    ///
    /// <para><b>Index:</b> served by the existing <c>idx_employee_profiles_history</c> UNIQUE
    /// <c>(employee_id, effective_from)</c> — an exact match for this equality-plus-ordering shape,
    /// which is why no new index is needed.</para>
    /// </summary>
    public async Task<IReadOnlyList<EmploymentProfileHistoryRow>> GetProfileIntervalsAsync(
        string employeeId, DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        const string sql =
            """
            SELECT effective_from, effective_to, part_time_fraction, position, employment_category
            FROM employee_profiles
            WHERE employee_id = @employeeId
              AND (@from IS NULL OR effective_to IS NULL OR effective_to > @from)
              AND (@to   IS NULL OR effective_from < @to)
            ORDER BY effective_from
            """;

        await using var conn = _dbFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.Add(new NpgsqlParameter("employeeId", NpgsqlDbType.Text) { Value = employeeId });
        AddNullableDate(cmd, "from", from);
        AddNullableDate(cmd, "to", to);

        var rows = new List<EmploymentProfileHistoryRow>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new EmploymentProfileHistoryRow(
                EffectiveFrom: reader.GetFieldValue<DateOnly>(0),
                EffectiveTo: reader.IsDBNull(1) ? null : reader.GetFieldValue<DateOnly>(1),
                PartTimeFraction: reader.GetDecimal(2),
                Position: reader.IsDBNull(3) ? null : reader.GetString(3),
                EmploymentCategory: reader.GetString(4)));
        }
        return rows;
    }

    /// <summary>
    /// Every <c>user_agreement_codes</c> interval for <paramref name="employeeId"/> that OVERLAPS
    /// <c>[from, to)</c>, ordered by <c>effective_from</c> ascending. Identical window semantics to
    /// <see cref="GetProfileIntervalsAsync"/> — deliberately the same predicate, so the two halves of
    /// one history response can never disagree about what "inside the window" means.
    ///
    /// <para><b>Index:</b> served by the existing <c>idx_user_agreement_codes_history</c> UNIQUE
    /// <c>(user_id, effective_from)</c>. No new index.</para>
    /// </summary>
    public async Task<IReadOnlyList<AgreementCodeHistoryRow>> GetAgreementCodeIntervalsAsync(
        string employeeId, DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        const string sql =
            """
            SELECT effective_from, effective_to, agreement_code
            FROM user_agreement_codes
            WHERE user_id = @employeeId
              AND (@from IS NULL OR effective_to IS NULL OR effective_to > @from)
              AND (@to   IS NULL OR effective_from < @to)
            ORDER BY effective_from
            """;

        await using var conn = _dbFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.Add(new NpgsqlParameter("employeeId", NpgsqlDbType.Text) { Value = employeeId });
        AddNullableDate(cmd, "from", from);
        AddNullableDate(cmd, "to", to);

        var rows = new List<AgreementCodeHistoryRow>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new AgreementCodeHistoryRow(
                EffectiveFrom: reader.GetFieldValue<DateOnly>(0),
                EffectiveTo: reader.IsDBNull(1) ? null : reader.GetFieldValue<DateOnly>(1),
                AgreementCode: reader.GetString(2)));
        }
        return rows;
    }

    /// <summary>
    /// Binds an optional business date. The NpgsqlDbType is stated EXPLICITLY rather than inferred,
    /// because a <c>null</c> carries no CLR type to infer from: without it the <c>@from IS NULL</c>
    /// branch would fail to resolve the parameter's type at the server.
    /// </summary>
    private static void AddNullableDate(NpgsqlCommand cmd, string name, DateOnly? value)
        => cmd.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Date)
        {
            Value = value.HasValue ? value.Value : (object)DBNull.Value,
        });
}
