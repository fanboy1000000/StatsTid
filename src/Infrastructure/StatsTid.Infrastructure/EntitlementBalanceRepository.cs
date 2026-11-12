using Npgsql;
using StatsTid.SharedKernel.Calendar;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Infrastructure;

public sealed class EntitlementBalanceRepository
{
    private readonly DbConnectionFactory _connectionFactory;

    public EntitlementBalanceRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<EntitlementBalance>> GetByEmployeeAsync(
        string employeeId, int entitlementYear, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM entitlement_balances WHERE employee_id = @employeeId AND entitlement_year = @entitlementYear ORDER BY entitlement_type",
            conn);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("entitlementYear", entitlementYear);
        return await ReadBalancesAsync(cmd, ct);
    }

    public async Task<EntitlementBalance?> GetByEmployeeAndTypeAsync(
        string employeeId, string entitlementType, int entitlementYear, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM entitlement_balances WHERE employee_id = @employeeId AND entitlement_type = @entitlementType AND entitlement_year = @entitlementYear",
            conn);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("entitlementType", entitlementType);
        cmd.Parameters.AddWithValue("entitlementYear", entitlementYear);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadBalance(reader) : null;
    }

    /// <summary>
    /// In-transaction sibling overload of
    /// <see cref="GetByEmployeeAndTypeAsync(string, string, int, CancellationToken)"/>.
    /// Reuses the caller-supplied <paramref name="conn"/> + <paramref name="tx"/> so the read
    /// observes the same snapshot as the outer transaction (ADR-018 D3 transactional-outbox
    /// contract; required by <see cref="CheckAndAdjustAsync(NpgsqlConnection, NpgsqlTransaction, string, string, int, decimal, decimal, decimal, int?, IReadOnlyCollection{string}, CancellationToken)"/>
    /// failure-path under RepeatableRead). The caller commits or rolls back; this method does NOT.
    /// </summary>
    public async Task<EntitlementBalance?> GetByEmployeeAndTypeAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string employeeId, string entitlementType, int entitlementYear, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM entitlement_balances WHERE employee_id = @employeeId AND entitlement_type = @entitlementType AND entitlement_year = @entitlementYear",
            conn, tx);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("entitlementType", entitlementType);
        cmd.Parameters.AddWithValue("entitlementYear", entitlementYear);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadBalance(reader) : null;
    }

    public async Task UpsertAsync(EntitlementBalance balance, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            @"INSERT INTO entitlement_balances (balance_id, employee_id, entitlement_type, entitlement_year, total_quota, used, planned, carryover_in, updated_at)
              VALUES (@balanceId, @employeeId, @entitlementType, @entitlementYear, @totalQuota, @used, @planned, @carryoverIn, NOW())
              ON CONFLICT (employee_id, entitlement_type, entitlement_year)
              DO UPDATE SET total_quota = @totalQuota, used = @used, planned = @planned, carryover_in = @carryoverIn, updated_at = NOW()",
            conn);
        cmd.Parameters.AddWithValue("balanceId", balance.BalanceId);
        cmd.Parameters.AddWithValue("employeeId", balance.EmployeeId);
        cmd.Parameters.AddWithValue("entitlementType", balance.EntitlementType);
        cmd.Parameters.AddWithValue("entitlementYear", balance.EntitlementYear);
        cmd.Parameters.AddWithValue("totalQuota", balance.TotalQuota);
        cmd.Parameters.AddWithValue("used", balance.Used);
        cmd.Parameters.AddWithValue("planned", balance.Planned);
        cmd.Parameters.AddWithValue("carryoverIn", balance.CarryoverIn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<decimal> AdjustUsedAsync(
        string employeeId, string entitlementType, int entitlementYear, decimal deltaDays, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            @"INSERT INTO entitlement_balances (employee_id, entitlement_type, entitlement_year, total_quota, used, updated_at)
              VALUES (@employeeId, @entitlementType, @entitlementYear, 0, @deltaDays, NOW())
              ON CONFLICT (employee_id, entitlement_type, entitlement_year)
              DO UPDATE SET used = entitlement_balances.used + @deltaDays, updated_at = NOW()
              RETURNING used",
            conn);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("entitlementType", entitlementType);
        cmd.Parameters.AddWithValue("entitlementYear", entitlementYear);
        cmd.Parameters.AddWithValue("deltaDays", deltaDays);
        var result = await cmd.ExecuteScalarAsync(ct);
        return (decimal)result!;
    }

    /// <summary>
    /// S145 / TASK-14507 (A0, R23) — the UNGATED in-transaction adjust a Skema save uses when it
    /// REPLACES absence cells and the signed change for the entitlement type is zero or negative
    /// (an unchanged re-save, or a reduction). In plain terms: a reduction gives days back, so it
    /// must never be refused, not even when <c>used</c> already sits above the cap (an ADR-032 D4
    /// revaluation may legitimately leave it there, <see cref="ApplyRevaluationAsync"/>). Increases
    /// keep going through the guarded
    /// <see cref="CheckAndAdjustAsync(NpgsqlConnection, NpgsqlTransaction, string, string, int, decimal, decimal, decimal, int?, IReadOnlyCollection{string}, CancellationToken)"/>.
    ///
    /// <para>Shape: the guarded path's two statements without the quota WHERE. Statement 1 is the
    /// same ensure-row INSERT; statement 2 is <c>used = used + delta</c> with no guard, RETURNING the
    /// new <c>used</c> for the <c>EntitlementBalanceAdjusted</c> event. Participates in the caller's
    /// transaction (ADR-018 D3); the caller commits or rolls back, this method does NOT. The caller
    /// must hold the employee's consumption lock, as for every balance write of the save path.</para>
    ///
    /// <para><b>A row this call creates</b> (QUAL-196; owner Q1 and Q4, 2026-11-11). Its
    /// <c>total_quota</c> is <paramref name="seedQuota"/>, the year's annual entitlement as the
    /// caller resolved it (the Skema save: the config in force at the entitlement year's start,
    /// pro-rated where that config says so). Its <c>used</c> ends at the Σ of the current recorded
    /// feriedage of the type and year (<see cref="SumCurrentFeriedageAsync"/>, ADR-032 D2): the
    /// insert seeds <c>Σ − delta</c> and statement 2 adds the delta. The Σ is read on this
    /// transaction, so it already contains the absence rows the caller wrote before calling (the
    /// Skema save writes them first); the seed is therefore the Σ before the save, and a booking
    /// made while the row was missing is never forgotten. An EXISTING row is never re-seeded: it
    /// only moves by the delta. <c>ON CONFLICT DO NOTHING</c> stays the concurrency backstop.</para>
    ///
    /// <para><b>An omitted seed input</b> (no caller omits them today: the one production call, the
    /// Skema save, passes both). Without <paramref name="seedAbsenceTypes"/> no Σ is read and a created row
    /// starts at <c>used = 0</c> plus the delta, which under-counts any booking already recorded for
    /// the year. <paramref name="seedResetMonth"/> is required when
    /// <paramref name="seedAbsenceTypes"/> is given (the method throws otherwise).</para>
    /// </summary>
    /// <param name="seedResetMonth">The <c>reset_month</c> that keyed <paramref name="entitlementYear"/>;
    /// it fixes the year's date window for the Σ (ignored for SPECIAL_HOLIDAY).</param>
    /// <param name="seedAbsenceTypes">The absence types that map to <paramref name="entitlementType"/>
    /// (the Backend's <c>EntitlementMapping</c>); the Σ counts only those rows.</param>
    public async Task<decimal> AdjustUsedAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string employeeId, string entitlementType, int entitlementYear,
        decimal deltaDays, decimal seedQuota,
        int? seedResetMonth = null, IReadOnlyCollection<string>? seedAbsenceTypes = null,
        CancellationToken ct = default)
    {
        var currentSum = await SumCurrentFeriedageAsync(
            conn, tx, employeeId, entitlementType, entitlementYear, seedResetMonth, seedAbsenceTypes, ct);
        var seedUsed = currentSum is { } sum ? sum - deltaDays : 0m;

        await using (var ensureCmd = new NpgsqlCommand(
            @"INSERT INTO entitlement_balances (
                  balance_id, employee_id, entitlement_type, entitlement_year,
                  total_quota, used, planned, carryover_in, updated_at)
              VALUES (
                  gen_random_uuid(), @employeeId, @entitlementType, @entitlementYear,
                  @seedQuota, @seedUsed, 0, 0, NOW())
              ON CONFLICT (employee_id, entitlement_type, entitlement_year) DO NOTHING",
            conn, tx))
        {
            ensureCmd.Parameters.AddWithValue("employeeId", employeeId);
            ensureCmd.Parameters.AddWithValue("entitlementType", entitlementType);
            ensureCmd.Parameters.AddWithValue("entitlementYear", entitlementYear);
            ensureCmd.Parameters.AddWithValue("seedQuota", seedQuota);
            ensureCmd.Parameters.AddWithValue("seedUsed", seedUsed);
            await ensureCmd.ExecuteNonQueryAsync(ct);
        }

        await using var cmd = new NpgsqlCommand(
            @"UPDATE entitlement_balances
              SET used = entitlement_balances.used + @deltaDays, updated_at = NOW()
              WHERE employee_id = @employeeId AND entitlement_type = @entitlementType AND entitlement_year = @entitlementYear
              RETURNING used",
            conn, tx);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("entitlementType", entitlementType);
        cmd.Parameters.AddWithValue("entitlementYear", entitlementYear);
        cmd.Parameters.AddWithValue("deltaDays", deltaDays);
        var result = await cmd.ExecuteScalarAsync(ct);
        return (decimal)result!;
    }

    /// <summary>
    /// ADR-032 D4 — apply a profile-change revaluation in the caller's transaction:
    /// (1) overwrite each affected absence's authoritative per-row
    /// <c>absences_projection.feriedage</c> from the replacement set; and (2) adjust
    /// <c>entitlement_balances.used</c> by <paramref name="usedDelta"/> via an <b>ungated</b>
    /// upsert (used += delta, NO quota-WHERE — revaluation MAY push <c>used</c> past the cap; this
    /// is NOT the booking path and MUST NOT route through
    /// <see cref="CheckAndAdjustAsync(string, string, int, decimal, decimal, decimal, CancellationToken)"/>'s
    /// guarded form).
    ///
    /// <para>
    /// <b>All-or-nothing (ADR-032 D4):</b> each projection UPDATE must affect exactly one row;
    /// if the total affected-row count != <paramref name="replacements"/> count, this THROWS
    /// <see cref="InvalidOperationException"/> so the caller's transaction rolls back — never
    /// a partial success (a replacement targeting a missing/duplicate absence row is a
    /// correctness fault, not a tolerable no-op). Both writes participate in the caller's tx
    /// (ADR-018 D3); the caller commits or rolls back — this method does NOT.
    /// </para>
    ///
    /// <para>
    /// <b>A row this call creates</b> (QUAL-196; owner Q1 and Q4, 2026-11-11). Its <c>used</c> is
    /// the Σ of the current recorded feriedage of the type and year, read on this transaction
    /// AFTER the overwrite in (1) (<see cref="SumCurrentFeriedageAsync"/>, ADR-032 D2): a replaced
    /// row counts its new value and an unreplaced row with a NULL value counts 0. It is never "a
    /// stored seed plus <paramref name="usedDelta"/>", because the delta's old side values a NULL
    /// row at hours ÷ 7.4, which would leave the row short. Its <c>total_quota</c> is
    /// <paramref name="seedQuota"/>: the caller passes the config in force at the start of the
    /// record's own entitlement year, pro-rated by the new fraction where that config says so,
    /// never the live row. An EXISTING row is never re-seeded: it keeps <c>used += usedDelta</c>
    /// and its own <c>total_quota</c>.
    /// </para>
    ///
    /// <para>
    /// <b>An omitted seed input</b> (no test calls this method; the one production call passes all
    /// three). Without <paramref name="seedAbsenceTypes"/> no Σ is read and a created row starts at
    /// <c>used = usedDelta</c>; without <paramref name="seedQuota"/> it starts at
    /// <c>total_quota = 0</c>, so readers show "of 0 days" and a negative remaining.
    /// <paramref name="seedResetMonth"/> is required when <paramref name="seedAbsenceTypes"/> is
    /// given (the method throws otherwise).
    /// </para>
    /// </summary>
    /// <param name="seedQuota">The <c>total_quota</c> of a row this call creates.</param>
    /// <param name="seedResetMonth">The <c>reset_month</c> that keyed <paramref name="entitlementYear"/>;
    /// it fixes the year's date window for the Σ (ignored for SPECIAL_HOLIDAY).</param>
    /// <param name="seedAbsenceTypes">The absence types that map to <paramref name="entitlementType"/>
    /// (the Backend's <c>EntitlementMapping</c>); the Σ counts only those rows.</param>
    public async Task ApplyRevaluationAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string employeeId, string entitlementType, int entitlementYear,
        decimal usedDelta, IReadOnlyList<AbsenceFeriedageReplacement> replacements,
        decimal seedQuota = 0m, int? seedResetMonth = null, IReadOnlyCollection<string>? seedAbsenceTypes = null,
        CancellationToken ct = default)
    {
        // (1) Per-absence projection feriedage overwrite — all-or-nothing. Each UPDATE must
        // hit exactly one row (event_id is the absences_projection PK); a mismatch ⇒ THROW ⇒
        // caller-tx rollback (ADR-032 D4: never partial success). It runs BEFORE the balance write
        // so the Σ seed of a created row reads the rewritten values (QUAL-196).
        await using (var replCmd = new NpgsqlCommand(
            @"UPDATE absences_projection SET feriedage = @feriedage WHERE event_id = @absenceEventId",
            conn, tx))
        {
            var feriedageParam = replCmd.Parameters.Add("feriedage", NpgsqlTypes.NpgsqlDbType.Numeric);
            var eventIdParam = replCmd.Parameters.Add("absenceEventId", NpgsqlTypes.NpgsqlDbType.Uuid);

            foreach (var repl in replacements)
            {
                feriedageParam.Value = repl.NewFeriedage;
                eventIdParam.Value = repl.AbsenceEventId;
                var affected = await replCmd.ExecuteNonQueryAsync(ct);
                if (affected != 1)
                    throw new InvalidOperationException(
                        $"ADR-032 D4 revaluation: absences_projection feriedage UPDATE for event_id={repl.AbsenceEventId} " +
                        $"affected {affected} rows (expected 1). Replacement set is inconsistent with the projection — " +
                        "rolling back to avoid partial revaluation.");
            }
        }

        // (2) Ungated used adjustment: an existing row moves by the delta (no quota-WHERE); a
        // created row starts at the Σ read after (1) and at the year-start quota (see the summary).
        var currentSum = await SumCurrentFeriedageAsync(
            conn, tx, employeeId, entitlementType, entitlementYear, seedResetMonth, seedAbsenceTypes, ct);
        await using var usedCmd = new NpgsqlCommand(
            @"INSERT INTO entitlement_balances (employee_id, entitlement_type, entitlement_year, total_quota, used, updated_at)
              VALUES (@employeeId, @entitlementType, @entitlementYear, @seedQuota, @seedUsed, NOW())
              ON CONFLICT (employee_id, entitlement_type, entitlement_year)
              DO UPDATE SET used = entitlement_balances.used + @usedDelta, updated_at = NOW()",
            conn, tx);
        usedCmd.Parameters.AddWithValue("employeeId", employeeId);
        usedCmd.Parameters.AddWithValue("entitlementType", entitlementType);
        usedCmd.Parameters.AddWithValue("entitlementYear", entitlementYear);
        usedCmd.Parameters.AddWithValue("seedQuota", seedQuota);
        usedCmd.Parameters.AddWithValue("seedUsed", currentSum ?? usedDelta);
        usedCmd.Parameters.AddWithValue("usedDelta", usedDelta);
        await usedCmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// S68 / TASK-6804 (ADR-033 D6) — the provenance-keyed <c>carryover_in</c> writer: the FIRST
    /// non-zero <c>carryover_in</c> producer in the system. Writes next entitlement-year's
    /// <c>carryover_in</c> DERIVED from the settling year's §21 <c>transfer_days</c> (the written-
    /// agreement transfer; ADR-033 D5/D8). Participates in the caller's settlement transaction
    /// (ADR-018 D3) under the advisory lock; the caller commits or rolls back — this method does NOT.
    ///
    /// <para>
    /// <b>Ungated upsert (mirrors <see cref="ApplyRevaluationAsync"/> shape, NOT the booking
    /// guard).</b> This is NOT a consumption write — it must NOT route through
    /// <see cref="CheckAndAdjustAsync(string, string, int, decimal, decimal, decimal, CancellationToken)"/>'s
    /// quota-WHERE form. The target row is the NEXT year's balance, which may not exist yet (the
    /// employee may have no next-year row), so the INSERT creates it with the carryover applied;
    /// an existing row has only its <c>carryover_in</c> overwritten.
    /// </para>
    ///
    /// <para>
    /// <b>A row this call creates</b> (QUAL-196; owner Q1 and Q4, 2026-11-11). Its <c>used</c> is
    /// the Σ of the current recorded feriedage already booked into the next year
    /// (<see cref="SumCurrentFeriedageAsync"/>, ADR-032 D2), read on this transaction; no delta is
    /// added. Its <c>total_quota</c> is <paramref name="seedQuota"/>: the settlement passes the
    /// next year's OWN year-start quota (cycle-3 ruling R1), falling back to the settled year's
    /// quota, logged, only when no config resolves for the next year (OA-5). <c>planned</c> starts
    /// at 0 (it has no production writer).
    /// </para>
    ///
    /// <para>
    /// <b>An omitted seed input</b> (no test calls this method; both production calls pass both).
    /// Without <paramref name="seedAbsenceTypes"/> no Σ is read and a created row starts at
    /// <c>used = 0</c>, which under-counts any booking already recorded for the next year.
    /// <paramref name="seedResetMonth"/> is required when <paramref name="seedAbsenceTypes"/> is
    /// given (the method throws otherwise).
    /// </para>
    ///
    /// <para>
    /// <b>Idempotent by construction (Codex W).</b> <c>carryover_in</c> is OVERWRITTEN to the
    /// DERIVED value (<c>SET carryover_in = @carryoverDays</c>), NEVER incrementally added. A
    /// retried/re-run settlement recomputes the SAME §21 <c>transfer_days</c> from the immutable
    /// snapshot and writes the same total — so a second pass is a no-op, not a double-add. In
    /// slice 1 the carryover total IS the §21 term alone (the §22 feriehindring composition term
    /// is 0 — not modeled until slice 4); the value is source-keyed on <c>transfer_days</c> so the
    /// later §22 composition stays deterministic. An existing row's <c>used</c>/<c>planned</c> are
    /// NOT touched here, and a created row's <c>used</c> is the recorded Σ above (ADR-032 D2 pins
    /// <c>used</c> to recorded absences; the §24/§34 disposition lives on the settlement row, NOT the
    /// balance — ADR-033 D6 clarification). <c>total_quota</c> is seeded to
    /// the supplied <paramref name="seedQuota"/> on first-INSERT only, preserving its "annual
    /// entitlement" invariant; an existing row keeps its quota.
    /// </para>
    /// </summary>
    /// <param name="employeeId">The employee whose NEXT-year balance receives the carryover.</param>
    /// <param name="entitlementType">The entitlement type (VACATION in slice 1).</param>
    /// <param name="nextEntitlementYear">The year the carryover lands in (settling year + 1).</param>
    /// <param name="carryoverDays">The DERIVED carryover total — in slice 1, the §21 <c>transfer_days</c>.</param>
    /// <param name="seedQuota">The next year's annual entitlement as of that year's start; seeds
    /// <c>total_quota</c> on first-INSERT only (an existing row's quota is preserved).</param>
    /// <param name="seedResetMonth">The <c>reset_month</c> that keys <paramref name="nextEntitlementYear"/>;
    /// it fixes the year's date window for the Σ (ignored for SPECIAL_HOLIDAY).</param>
    /// <param name="seedAbsenceTypes">The absence types that map to <paramref name="entitlementType"/>;
    /// the Σ counts only those rows.</param>
    public async Task WriteCarryoverInAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string employeeId, string entitlementType, int nextEntitlementYear,
        decimal carryoverDays, decimal seedQuota,
        int? seedResetMonth = null, IReadOnlyCollection<string>? seedAbsenceTypes = null,
        CancellationToken ct = default)
    {
        // Ungated upsert — INSERT creates the row with used = the Σ already booked into the year
        // (0 when no seed input is given), planned = 0 and the carryover applied; ON CONFLICT
        // OVERWRITES carryover_in only (SET, not +=) so a re-run is idempotent and an existing
        // row's used / total_quota are never touched. total_quota seeded on first-INSERT only.
        var currentSum = await SumCurrentFeriedageAsync(
            conn, tx, employeeId, entitlementType, nextEntitlementYear, seedResetMonth, seedAbsenceTypes, ct);
        await using var cmd = new NpgsqlCommand(
            @"INSERT INTO entitlement_balances (
                  balance_id, employee_id, entitlement_type, entitlement_year,
                  total_quota, used, planned, carryover_in, updated_at)
              VALUES (
                  gen_random_uuid(), @employeeId, @entitlementType, @entitlementYear,
                  @seedQuota, @seedUsed, 0, @carryoverDays, NOW())
              ON CONFLICT (employee_id, entitlement_type, entitlement_year)
              DO UPDATE SET carryover_in = @carryoverDays, updated_at = NOW()",
            conn, tx);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("entitlementType", entitlementType);
        cmd.Parameters.AddWithValue("entitlementYear", nextEntitlementYear);
        cmd.Parameters.AddWithValue("seedQuota", seedQuota);
        cmd.Parameters.AddWithValue("seedUsed", currentSum ?? 0m);
        cmd.Parameters.AddWithValue("carryoverDays", carryoverDays);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Atomically checks quota and adjusts the used balance. Returns (success, newUsed). If
    /// the adjustment would exceed <paramref name="guardCap"/> + carryoverIn, returns
    /// (false, currentUsed). Eliminates TOCTOU race between validation and adjustment.
    ///
    /// <para>
    /// <b>S60 / TASK-6004 — two distinct caps (no conflation):</b>
    /// <list type="bullet">
    /// <item>
    /// <paramref name="guardCap"/> — the per-type bookable cap <b>EXCLUDING carryover</b>.
    /// Used in the atomic WHERE-clause guard, which itself adds <c>carryover_in</c> exactly
    /// once (<c>used + delta &lt;= guardCap + carryover_in</c>). The caller computes
    /// <c>guardCap = businessBookableLimit − carryover</c> (the rule engine's
    /// carryover-INCLUSIVE <c>bookableLimit</c> minus carryover) so carryover is never
    /// double-counted.
    /// </item>
    /// <item>
    /// <paramref name="seedQuota"/> — the <b>ANNUAL entitlement</b>. Seeds <c>total_quota</c>
    /// on the first-INSERT of a missing row only; preserves <c>total_quota</c>'s invariant
    /// meaning ("annual entitlement", ADR-021 D6 / P3) regardless of earned-to-date or
    /// forskud caps. Never used in the guard.
    /// </item>
    /// </list>
    /// (Pre-S60 a single <c>effectiveQuota</c> arg fed BOTH the seed and the guard, which
    /// risked seeding <c>total_quota</c> with a forskud/earned-derived cap and double-counting
    /// carryover once the business cap already included it.)
    /// </para>
    ///
    /// <para>
    /// S26 Step 7a B3 fix: previously this was a UPDATE-only path that returned (false, 0m)
    /// when no row existed for the (employee, type, year) tuple — indistinguishable from a
    /// real quota breach. Pre-S26 callers silently skipped balance adjustment but committed
    /// the corresponding events (Skema first-absence-of-year would emit AbsenceRegistered
    /// without any balance state). The fix uses two statements inside an internal tx: (1) an
    /// idempotent <c>INSERT ... ON CONFLICT DO NOTHING</c> with <c>used = 0</c> baseline so
    /// missing rows materialize at zero-state without touching used; (2) the existing
    /// TOCTOU-safe atomic UPDATE-WHERE-quota-guard against the now-guaranteed-present row.
    /// Net behavior: missing-row first absence within quota succeeds with (true, deltaDays);
    /// genuine breach (existing or freshly-created) returns (false, currentUsed). Same shape
    /// applies to the <see cref="CheckAndAdjustAsync(NpgsqlConnection, NpgsqlTransaction, string, string, int, decimal, decimal, decimal, int?, IReadOnlyCollection{string}, CancellationToken)"/>
    /// in-tx sibling, which also seeds a created row's <c>used</c> from the recorded feriedage
    /// when its caller passes the seed inputs (QUAL-196). This overload passes none (it has no
    /// production caller), so a row it creates still starts at <c>used = 0</c>.
    /// </para>
    /// </summary>
    public async Task<(bool Success, decimal NewUsed)> CheckAndAdjustAsync(
        string employeeId, string entitlementType, int entitlementYear,
        decimal deltaDays, decimal guardCap, decimal seedQuota, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            var result = await CheckAndAdjustInternalAsync(
                conn, tx, employeeId, entitlementType, entitlementYear, deltaDays, guardCap, seedQuota, ct);
            await tx.CommitAsync(ct);
            return result;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>
    /// In-transaction sibling overload of
    /// <see cref="CheckAndAdjustAsync(string, string, int, decimal, decimal, decimal, CancellationToken)"/>.
    /// Reuses the caller-supplied <paramref name="conn"/> + <paramref name="tx"/> so the
    /// atomic-quota-check single-UPDATE participates in the outer transaction (ADR-018 D3
    /// transactional-outbox contract). Preserves the v1 TOCTOU-safe shape: WITH-current-CTE +
    /// UPDATE-WHERE-quota-guard + RETURNING-used. Failure-path fallback routes through the
    /// (conn, tx) overload of <see cref="GetByEmployeeAndTypeAsync(NpgsqlConnection, NpgsqlTransaction, string, string, int, CancellationToken)"/>
    /// so the current-Used read for the 422 response observes the same snapshot under
    /// RepeatableRead. The caller commits or rolls back; this method does NOT.
    ///
    /// <para>
    /// S60 / TASK-6004 — <paramref name="guardCap"/> is carryover-EXCLUDED (the guard adds
    /// <c>carryover_in</c> once); <paramref name="seedQuota"/> is the ANNUAL entitlement that
    /// seeds <c>total_quota</c> on first-INSERT. See the self-managed-tx overload for the full
    /// rationale.
    /// </para>
    ///
    /// <para>
    /// <b>A row this call creates</b> (QUAL-196; owner Q1 and Q4, 2026-11-11). Its
    /// <c>total_quota</c> is <paramref name="seedQuota"/>, the year's annual entitlement as the
    /// caller resolved it (the Skema save: the config in force at the entitlement year's start,
    /// pro-rated where that config says so). Its <c>used</c> is seeded at <c>Σ − delta</c>, where
    /// Σ is the current recorded feriedage of the type and year read on this transaction
    /// (<see cref="SumCurrentFeriedageAsync"/>, ADR-032 D2) and so already contains the absence
    /// rows the caller wrote before calling (the Skema save writes them first). The seed is
    /// therefore the Σ before the save, the guard judges <c>seed + delta</c> against the cap, and a
    /// booking made while the row was missing is never forgotten. An EXISTING row is never
    /// re-seeded. <c>ON CONFLICT DO NOTHING</c> stays the concurrency backstop.
    /// </para>
    ///
    /// <para>
    /// <b>An omitted seed input</b> (only test-only callers omit them, the self-managed-tx overload
    /// among them; the production call passes both). Without <paramref name="seedAbsenceTypes"/>
    /// no Σ is read and a created row starts at <c>used = 0</c>, so the guard and the stored
    /// <c>used</c> ignore any booking already recorded for the year.
    /// <paramref name="seedResetMonth"/> is required when <paramref name="seedAbsenceTypes"/> is
    /// given (the method throws otherwise).
    /// </para>
    /// </summary>
    /// <param name="seedResetMonth">The <c>reset_month</c> that keyed <paramref name="entitlementYear"/>;
    /// it fixes the year's date window for the Σ (ignored for SPECIAL_HOLIDAY).</param>
    /// <param name="seedAbsenceTypes">The absence types that map to <paramref name="entitlementType"/>
    /// (the Backend's <c>EntitlementMapping</c>); the Σ counts only those rows.</param>
    public async Task<(bool Success, decimal NewUsed)> CheckAndAdjustAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string employeeId, string entitlementType, int entitlementYear,
        decimal deltaDays, decimal guardCap, decimal seedQuota,
        int? seedResetMonth = null, IReadOnlyCollection<string>? seedAbsenceTypes = null,
        CancellationToken ct = default)
    {
        return await CheckAndAdjustInternalAsync(
            conn, tx, employeeId, entitlementType, entitlementYear, deltaDays, guardCap, seedQuota, ct,
            seedResetMonth, seedAbsenceTypes);
    }

    // Two-statement core for both v1 (self-managed tx) and v2 (caller-supplied tx). Statement 1
    // materializes a missing balance row; Statement 2 is the TOCTOU-safe atomic
    // UPDATE-WHERE-quota-guard. Together they distinguish "missing-row booking within quota"
    // (succeeds) from "existing/freshly-created row with quota breach" (false, currentUsed)
    // rather than collapsing both to a false 422. The seed inputs trail the token only because
    // the self-managed-tx overload calls this positionally and passes none (it is test-only).
    private static async Task<(bool Success, decimal NewUsed)> CheckAndAdjustInternalAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string employeeId, string entitlementType, int entitlementYear,
        decimal deltaDays, decimal guardCap, decimal seedQuota, CancellationToken ct,
        int? seedResetMonth = null, IReadOnlyCollection<string>? seedAbsenceTypes = null)
    {
        // Statement 1: ensure-row INSERT, so the row exists before Statement 2's quota check
        // evaluates `used + deltaDays <= guardCap + carryover_in`. A created row starts at
        // used = Σ − delta (QUAL-196: the Σ of the current recorded feriedage of the type and year,
        // which already holds the rows the caller wrote; with no seed input, used = 0), so
        // Statement 2 lands it at the Σ and the guard judges the real consumption.
        // total_quota = seedQuota, the ANNUAL entitlement (NOT the bookable/
        // forskud-derived guardCap). carryover_in starts at 0: only the settlement writes it.
        // ON CONFLICT DO NOTHING — concurrent INSERTs from another session deduplicate cleanly,
        // and an existing row is never re-seeded.
        var currentSum = await SumCurrentFeriedageAsync(
            conn, tx, employeeId, entitlementType, entitlementYear, seedResetMonth, seedAbsenceTypes, ct);
        var seedUsed = currentSum is { } sum ? sum - deltaDays : 0m;

        await using (var ensureCmd = new NpgsqlCommand(
            @"INSERT INTO entitlement_balances (
                  balance_id, employee_id, entitlement_type, entitlement_year,
                  total_quota, used, planned, carryover_in, updated_at)
              VALUES (
                  gen_random_uuid(), @employeeId, @entitlementType, @entitlementYear,
                  @seedQuota, @seedUsed, 0, 0, NOW())
              ON CONFLICT (employee_id, entitlement_type, entitlement_year) DO NOTHING",
            conn, tx))
        {
            ensureCmd.Parameters.AddWithValue("employeeId", employeeId);
            ensureCmd.Parameters.AddWithValue("entitlementType", entitlementType);
            ensureCmd.Parameters.AddWithValue("entitlementYear", entitlementYear);
            ensureCmd.Parameters.AddWithValue("seedQuota", seedQuota);
            ensureCmd.Parameters.AddWithValue("seedUsed", seedUsed);
            await ensureCmd.ExecuteNonQueryAsync(ct);
        }

        // Statement 2: TOCTOU-safe atomic check-and-adjust on the now-guaranteed-present row.
        // Single-statement UPDATE-WHERE-quota-guard with WITH-CTE for the snapshot read.
        // guardCap is carryover-EXCLUDED; this clause adds carryover_in exactly ONCE, so the
        // effective cap is `guardCap + carryover_in` with no double-count (the caller's
        // business bookableLimit already incl. carryover is passed here minus carryover).
        await using var cmd = new NpgsqlCommand(
            @"WITH current AS (
                SELECT used, carryover_in
                FROM entitlement_balances
                WHERE employee_id = @employeeId AND entitlement_type = @entitlementType AND entitlement_year = @entitlementYear
              )
              UPDATE entitlement_balances
              SET used = entitlement_balances.used + @deltaDays, updated_at = NOW()
              WHERE employee_id = @employeeId AND entitlement_type = @entitlementType AND entitlement_year = @entitlementYear
                AND (SELECT used FROM current) + @deltaDays <= @guardCap + (SELECT carryover_in FROM current)
              RETURNING used",
            conn, tx);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("entitlementType", entitlementType);
        cmd.Parameters.AddWithValue("entitlementYear", entitlementYear);
        cmd.Parameters.AddWithValue("deltaDays", deltaDays);
        cmd.Parameters.AddWithValue("guardCap", guardCap);
        var result = await cmd.ExecuteScalarAsync(ct);
        if (result is decimal newUsed)
            return (true, newUsed);

        // UPDATE didn't match — quota would be exceeded (an existing row, OR a freshly-created
        // row whose seed + deltaDays exceeds guardCap + 0 carryover). Return current used for the
        // 422 response payload. Routes the read through the (conn, tx) overload so the
        // snapshot is consistent with the outer tx under RepeatableRead (refinement W3).
        var balance = await GetByEmployeeAndTypeInternalAsync(
            conn, tx, employeeId, entitlementType, entitlementYear, ct);
        return (false, balance?.Used ?? 0m);
    }

    /// <summary>
    /// QUAL-196 (owner Q1, 2026-11-11; ADR-032 D2) — the <c>used</c> seed of a balance row a writer
    /// CREATES: the Σ of the stored <c>feriedage</c> of the employee's CURRENT absence rows
    /// (<c>superseded_by_outbox_id IS NULL</c>, any source; a NULL value counts 0, as the Skema
    /// save's replaced-cells read counts it) whose absence type is one of
    /// <paramref name="absenceTypes"/> and whose date is keyed to <paramref name="entitlementYear"/>.
    /// The recorded per-absence feriedage is the consumption record, so a created row agrees with
    /// what is booked rather than starting from 0.
    ///
    /// <para><b>"Keyed to the year"</b> is the date window
    /// <see cref="EntitlementPeriodResolver.Resolve"/> sends to that year, per type: AccrualStart ..
    /// AccrualEnd for VACATION and every type but SPECIAL_HOLIDAY (VACATION 2025: 1 Sep 2025 –
    /// 31 Aug 2026), and TakingStart .. Boundary for SPECIAL_HOLIDAY (2025: 1 May 2026 –
    /// 30 Apr 2027). VACATION never uses TakingStart .. Boundary, which would run to 31 Dec and
    /// count the next ferieår's bookings.</para>
    ///
    /// <para>Read on the caller's (conn, tx) so it sees the transaction's own uncommitted absence
    /// writes, under the employee consumption lock the caller already holds (ADR-032 D4; the
    /// settlement and its §22 rescue take the same lock). Returns <c>null</c> when
    /// <paramref name="absenceTypes"/> is null (the caller gave no seed input: no Σ is read).</para>
    /// </summary>
    private static async Task<decimal?> SumCurrentFeriedageAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string employeeId, string entitlementType, int entitlementYear,
        int? resetMonth, IReadOnlyCollection<string>? absenceTypes, CancellationToken ct)
    {
        if (absenceTypes is null)
            return null;
        if (resetMonth is not { } month)
            throw new ArgumentException(
                "A balance seed needs the reset_month that keyed the entitlement year when absence types are given.",
                nameof(resetMonth));

        var period = EntitlementPeriodResolver.ResolveForYear(entitlementType, month, entitlementYear);
        var (from, to) = EntitlementPeriodResolver.IsSpecialHoliday(entitlementType)
            ? (period.TakingStart, period.Boundary)
            : (period.AccrualStart, period.AccrualEnd);

        await using var cmd = new NpgsqlCommand(
            @"SELECT COALESCE(SUM(COALESCE(feriedage, 0)), 0)
              FROM absences_projection
              WHERE employee_id = @employeeId
                AND absence_type = ANY(@absenceTypes)
                AND date >= @from AND date <= @to
                AND superseded_by_outbox_id IS NULL",
            conn, tx);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("absenceTypes", absenceTypes.ToArray());
        cmd.Parameters.AddWithValue("from", from);
        cmd.Parameters.AddWithValue("to", to);
        return (decimal)(await cmd.ExecuteScalarAsync(ct))!;
    }

    private static async Task<EntitlementBalance?> GetByEmployeeAndTypeInternalAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string employeeId, string entitlementType, int entitlementYear, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM entitlement_balances WHERE employee_id = @employeeId AND entitlement_type = @entitlementType AND entitlement_year = @entitlementYear",
            conn, tx);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("entitlementType", entitlementType);
        cmd.Parameters.AddWithValue("entitlementYear", entitlementYear);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadBalance(reader) : null;
    }

    private static async Task<IReadOnlyList<EntitlementBalance>> ReadBalancesAsync(NpgsqlCommand cmd, CancellationToken ct)
    {
        var balances = new List<EntitlementBalance>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            balances.Add(ReadBalance(reader));
        return balances;
    }

    private static EntitlementBalance ReadBalance(NpgsqlDataReader reader) => new()
    {
        BalanceId = reader.GetGuid(reader.GetOrdinal("balance_id")),
        EmployeeId = reader.GetString(reader.GetOrdinal("employee_id")),
        EntitlementType = reader.GetString(reader.GetOrdinal("entitlement_type")),
        EntitlementYear = reader.GetInt32(reader.GetOrdinal("entitlement_year")),
        TotalQuota = reader.GetDecimal(reader.GetOrdinal("total_quota")),
        Used = reader.GetDecimal(reader.GetOrdinal("used")),
        Planned = reader.GetDecimal(reader.GetOrdinal("planned")),
        CarryoverIn = reader.GetDecimal(reader.GetOrdinal("carryover_in")),
        UpdatedAt = reader.GetDateTime(reader.GetOrdinal("updated_at"))
    };
}
