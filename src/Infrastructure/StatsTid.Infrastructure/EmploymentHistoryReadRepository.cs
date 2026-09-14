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
//
//  5. RETIRED ROWS ARE NOT HISTORY (S141 sprint-end review, BLOCKER). When HR deletes a profile,
//     any change they had SCHEDULED is cancelled — and this system cancels a dated row by closing
//     it to ZERO WIDTH, `[f, f)`, rather than deleting it (EmployeeProfileRepository.SoftDeleteAsync
//     step 4). Such a row covers no day at all: it was never in force and never will be. Left in,
//     it would come back through this read wearing a future start date and the screen would tell HR
//     a change is coming that somebody deliberately called off — confidently wrong, which is worse
//     than silent. Every read below therefore filters on the SHARED
//     `EmploymentTimelineSql.CoversAtLeastOneDayPredicate` — the same definition of "retired" the
//     scheduled-change marker uses, deliberately not a second copy of it. The fact is not lost: the
//     retirement is separately audited, which is what the owner's ruling required.
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
    /// <para><b>Retired rows are excluded</b> (header rule 5) via the shared
    /// <see cref="EmploymentTimelineSql.CoversAtLeastOneDayPredicate"/>.</para>
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
            SELECT s.effective_from, s.effective_to, s.part_time_fraction, s.position, s.employment_category
            FROM employee_profiles s
            WHERE s.employee_id = @employeeId
              AND
            """ + " " + EmploymentTimelineSql.CoversAtLeastOneDayPredicate + "\n" +
            """
              AND (@from IS NULL OR s.effective_to IS NULL OR s.effective_to > @from)
              AND (@to   IS NULL OR s.effective_from < @to)
            ORDER BY s.effective_from
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
    /// <para><b>Retired rows are excluded here too</b>, with the same shared predicate. The
    /// agreement-code table has no delete path that produces one TODAY — only the profile
    /// soft-delete retires scheduled rows — but the exclusion is a statement about what a zero-width
    /// interval MEANS, not about which writer happens to create one, and applying it on one table
    /// only would quietly expire the moment the agreement side grows a retirement path.</para>
    ///
    /// <para><b>Index:</b> served by the existing <c>idx_user_agreement_codes_history</c> UNIQUE
    /// <c>(user_id, effective_from)</c>. No new index.</para>
    /// </summary>
    public async Task<IReadOnlyList<AgreementCodeHistoryRow>> GetAgreementCodeIntervalsAsync(
        string employeeId, DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        const string sql =
            """
            SELECT s.effective_from, s.effective_to, s.agreement_code
            FROM user_agreement_codes s
            WHERE s.user_id = @employeeId
              AND
            """ + " " + EmploymentTimelineSql.CoversAtLeastOneDayPredicate + "\n" +
            """
              AND (@from IS NULL OR s.effective_to IS NULL OR s.effective_to > @from)
              AND (@to   IS NULL OR s.effective_from < @to)
            ORDER BY s.effective_from
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

    // ───────────────────────────────────────────────────────────────────────────────────────
    // THE COMPARISON BASELINE (S141 sprint-end review, WARNING).
    //
    // The problem, in plain language. "What changed here?" can only be answered by comparing an
    // interval with the one BEFORE it. When the caller asks for a WINDOW, the first interval inside
    // that window usually has a predecessor OUTSIDE it — so computing the comparison from the
    // returned list alone made the read claim that interval was the employee's FIRST EVER record
    // ("Første registrering" on screen) and that nothing had changed at it. Both statements were
    // false, and both looked authoritative.
    //
    // The fix is to fetch the one row immediately before the first returned interval and use it ONLY
    // as the comparison baseline — it is never returned, so it cannot widen the window the caller
    // asked for. Note the anchor is the FIRST RETURNED ROW's start, not the `from` filter: a row that
    // straddles `from` is itself inside the window, and the row before IT is the baseline. Anchoring
    // on `from` would have silently compared that straddling row against itself.
    //
    // Same retired-row exclusion as the range reads: a cancelled change is not a state anything was
    // ever in, so it must not become the thing a later interval is described as a change FROM.
    // ───────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The profile interval immediately PRECEDING <paramref name="beforeEffectiveFrom"/> — the latest
    /// non-retired row that starts strictly earlier — or <c>null</c> when none exists, which is the
    /// honest signal that the caller's first interval really is the employee's first record.
    ///
    /// <para><b>Index:</b> a backward seek on <c>idx_employee_profiles_history</c>
    /// <c>(employee_id, effective_from)</c> — the ordering the index already provides, stopped at one
    /// row. No new index.</para>
    /// </summary>
    public async Task<EmploymentProfileHistoryRow?> GetProfileIntervalBeforeAsync(
        string employeeId, DateOnly beforeEffectiveFrom, CancellationToken ct = default)
    {
        const string sql =
            """
            SELECT s.effective_from, s.effective_to, s.part_time_fraction, s.position, s.employment_category
            FROM employee_profiles s
            WHERE s.employee_id = @employeeId
              AND s.effective_from < @before
              AND
            """ + " " + EmploymentTimelineSql.CoversAtLeastOneDayPredicate + "\n" +
            """
            ORDER BY s.effective_from DESC
            LIMIT 1
            """;

        await using var conn = _dbFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.Add(new NpgsqlParameter("employeeId", NpgsqlDbType.Text) { Value = employeeId });
        cmd.Parameters.Add(new NpgsqlParameter("before", NpgsqlDbType.Date) { Value = beforeEffectiveFrom });

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new EmploymentProfileHistoryRow(
            EffectiveFrom: reader.GetFieldValue<DateOnly>(0),
            EffectiveTo: reader.IsDBNull(1) ? null : reader.GetFieldValue<DateOnly>(1),
            PartTimeFraction: reader.GetDecimal(2),
            Position: reader.IsDBNull(3) ? null : reader.GetString(3),
            EmploymentCategory: reader.GetString(4));
    }

    /// <summary>
    /// Agreement-code sibling of <see cref="GetProfileIntervalBeforeAsync"/>; same rule, same index
    /// shape (<c>idx_user_agreement_codes_history</c>).
    /// </summary>
    public async Task<AgreementCodeHistoryRow?> GetAgreementCodeIntervalBeforeAsync(
        string employeeId, DateOnly beforeEffectiveFrom, CancellationToken ct = default)
    {
        const string sql =
            """
            SELECT s.effective_from, s.effective_to, s.agreement_code
            FROM user_agreement_codes s
            WHERE s.user_id = @employeeId
              AND s.effective_from < @before
              AND
            """ + " " + EmploymentTimelineSql.CoversAtLeastOneDayPredicate + "\n" +
            """
            ORDER BY s.effective_from DESC
            LIMIT 1
            """;

        await using var conn = _dbFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.Add(new NpgsqlParameter("employeeId", NpgsqlDbType.Text) { Value = employeeId });
        cmd.Parameters.Add(new NpgsqlParameter("before", NpgsqlDbType.Date) { Value = beforeEffectiveFrom });

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new AgreementCodeHistoryRow(
            EffectiveFrom: reader.GetFieldValue<DateOnly>(0),
            EffectiveTo: reader.IsDBNull(1) ? null : reader.GetFieldValue<DateOnly>(1),
            AgreementCode: reader.GetString(2));
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
