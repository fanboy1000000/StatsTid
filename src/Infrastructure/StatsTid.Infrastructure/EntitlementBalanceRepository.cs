using Npgsql;
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
    /// contract; required by <see cref="CheckAndAdjustAsync(NpgsqlConnection, NpgsqlTransaction, string, string, int, decimal, decimal, decimal, CancellationToken)"/>
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
    /// ADR-032 D4 — apply a profile-change revaluation in the caller's transaction:
    /// (1) adjust <c>entitlement_balances.used</c> by <paramref name="usedDelta"/> via the
    /// SAME <b>ungated</b> upsert shape as <see cref="AdjustUsedAsync"/> (used += delta, NO
    /// quota-WHERE — revaluation MAY push <c>used</c> past the cap; this is NOT the booking
    /// path and MUST NOT route through <see cref="CheckAndAdjustAsync(string, string, int, decimal, decimal, decimal, CancellationToken)"/>'s
    /// guarded form); and (2) overwrite each affected absence's authoritative per-row
    /// <c>absences_projection.feriedage</c> from the replacement set.
    ///
    /// <para>
    /// <b>All-or-nothing (ADR-032 D4):</b> each projection UPDATE must affect exactly one row;
    /// if the total affected-row count != <paramref name="replacements"/> count, this THROWS
    /// <see cref="InvalidOperationException"/> so the caller's transaction rolls back — never
    /// a partial success (a replacement targeting a missing/duplicate absence row is a
    /// correctness fault, not a tolerable no-op). Both writes participate in the caller's tx
    /// (ADR-018 D3); the caller commits or rolls back — this method does NOT.
    /// </para>
    /// </summary>
    public async Task ApplyRevaluationAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string employeeId, string entitlementType, int entitlementYear,
        decimal usedDelta, IReadOnlyList<AbsenceFeriedageReplacement> replacements,
        CancellationToken ct = default)
    {
        // (1) Ungated used adjustment — mirrors AdjustUsedAsync's INSERT ... ON CONFLICT
        // DO UPDATE SET used = used + delta (no quota-WHERE). Idempotent-row seed at used=0
        // on first INSERT preserves the "row materializes at zero-state" contract.
        await using (var usedCmd = new NpgsqlCommand(
            @"INSERT INTO entitlement_balances (employee_id, entitlement_type, entitlement_year, total_quota, used, updated_at)
              VALUES (@employeeId, @entitlementType, @entitlementYear, 0, @usedDelta, NOW())
              ON CONFLICT (employee_id, entitlement_type, entitlement_year)
              DO UPDATE SET used = entitlement_balances.used + @usedDelta, updated_at = NOW()",
            conn, tx))
        {
            usedCmd.Parameters.AddWithValue("employeeId", employeeId);
            usedCmd.Parameters.AddWithValue("entitlementType", entitlementType);
            usedCmd.Parameters.AddWithValue("entitlementYear", entitlementYear);
            usedCmd.Parameters.AddWithValue("usedDelta", usedDelta);
            await usedCmd.ExecuteNonQueryAsync(ct);
        }

        // (2) Per-absence projection feriedage overwrite — all-or-nothing. Each UPDATE must
        // hit exactly one row (event_id is the absences_projection PK); a mismatch ⇒ THROW ⇒
        // caller-tx rollback (ADR-032 D4: never partial success).
        await using var replCmd = new NpgsqlCommand(
            @"UPDATE absences_projection SET feriedage = @feriedage WHERE event_id = @absenceEventId",
            conn, tx);
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
    /// employee may have no next-year row), so the INSERT seeds it at zero-state with the carryover
    /// applied; an existing row has only its <c>carryover_in</c> overwritten.
    /// </para>
    ///
    /// <para>
    /// <b>Idempotent by construction (Codex W).</b> <c>carryover_in</c> is OVERWRITTEN to the
    /// DERIVED value (<c>SET carryover_in = @carryoverDays</c>), NEVER incrementally added. A
    /// retried/re-run settlement recomputes the SAME §21 <c>transfer_days</c> from the immutable
    /// snapshot and writes the same total — so a second pass is a no-op, not a double-add. In
    /// slice 1 the carryover total IS the §21 term alone (the §22 feriehindring composition term
    /// is 0 — not modeled until slice 4); the value is source-keyed on <c>transfer_days</c> so the
    /// later §22 composition stays deterministic. <c>used</c>/<c>planned</c> are NOT touched here
    /// (ADR-032 D2 pins <c>used</c> to recorded absences; the §24/§34 disposition lives on the
    /// settlement row, NOT the balance — ADR-033 D6 clarification). <c>total_quota</c> is seeded to
    /// the supplied <paramref name="seedQuota"/> (the next year's annual entitlement) on first-INSERT
    /// only, preserving its "annual entitlement" invariant; an existing row keeps its quota.
    /// </para>
    /// </summary>
    /// <param name="employeeId">The employee whose NEXT-year balance receives the carryover.</param>
    /// <param name="entitlementType">The entitlement type (VACATION in slice 1).</param>
    /// <param name="nextEntitlementYear">The year the carryover lands in (settling year + 1).</param>
    /// <param name="carryoverDays">The DERIVED carryover total — in slice 1, the §21 <c>transfer_days</c>.</param>
    /// <param name="seedQuota">The next year's ANNUAL entitlement; seeds <c>total_quota</c> on
    /// first-INSERT only (an existing row's quota is preserved).</param>
    public async Task WriteCarryoverInAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string employeeId, string entitlementType, int nextEntitlementYear,
        decimal carryoverDays, decimal seedQuota, CancellationToken ct = default)
    {
        // Ungated upsert — INSERT seeds the row at zero-state (used/planned = 0) with the
        // carryover applied; ON CONFLICT OVERWRITES carryover_in only (SET, not +=) so a
        // re-run is idempotent. total_quota seeded on first-INSERT, preserved thereafter.
        await using var cmd = new NpgsqlCommand(
            @"INSERT INTO entitlement_balances (
                  balance_id, employee_id, entitlement_type, entitlement_year,
                  total_quota, used, planned, carryover_in, updated_at)
              VALUES (
                  gen_random_uuid(), @employeeId, @entitlementType, @entitlementYear,
                  @seedQuota, 0, 0, @carryoverDays, NOW())
              ON CONFLICT (employee_id, entitlement_type, entitlement_year)
              DO UPDATE SET carryover_in = @carryoverDays, updated_at = NOW()",
            conn, tx);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("entitlementType", entitlementType);
        cmd.Parameters.AddWithValue("entitlementYear", nextEntitlementYear);
        cmd.Parameters.AddWithValue("seedQuota", seedQuota);
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
    /// applies to the <see cref="CheckAndAdjustAsync(NpgsqlConnection, NpgsqlTransaction, string, string, int, decimal, decimal, decimal, CancellationToken)"/>
    /// in-tx sibling.
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
    /// </summary>
    public async Task<(bool Success, decimal NewUsed)> CheckAndAdjustAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string employeeId, string entitlementType, int entitlementYear,
        decimal deltaDays, decimal guardCap, decimal seedQuota, CancellationToken ct = default)
    {
        return await CheckAndAdjustInternalAsync(
            conn, tx, employeeId, entitlementType, entitlementYear, deltaDays, guardCap, seedQuota, ct);
    }

    // Two-statement core for both v1 (self-managed tx) and v2 (caller-supplied tx). S26 Step 7a
    // B3 fix: Statement 1 idempotently materializes the balance row at zero-state if missing;
    // Statement 2 is the existing TOCTOU-safe atomic UPDATE-WHERE-quota-guard. Together they
    // distinguish "missing-row first absence within quota" (succeeds) from "existing/freshly-
    // created row with quota breach" (false, currentUsed). Pre-S26 the UPDATE-only path
    // collapsed both to (false, 0m), causing the false-422 surface flagged by Codex Step 7a.
    private static async Task<(bool Success, decimal NewUsed)> CheckAndAdjustInternalAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string employeeId, string entitlementType, int entitlementYear,
        decimal deltaDays, decimal guardCap, decimal seedQuota, CancellationToken ct)
    {
        // Statement 1: ensure-row INSERT. used = 0 baseline so the row exists at zero-state
        // before Statement 2's quota check evaluates `used + deltaDays <= guardCap + carryover_in`.
        // ON CONFLICT DO NOTHING — concurrent INSERTs from another session deduplicate cleanly.
        // total_quota seeded with seedQuota (the ANNUAL entitlement) for the freshly-created
        // row — preserving total_quota's "annual entitlement" invariant (NOT the bookable/
        // forskud-derived guardCap). carryover_in defaults to 0 (first-creation has no
        // prior-year carryover; reset_month logic handled elsewhere).
        await using (var ensureCmd = new NpgsqlCommand(
            @"INSERT INTO entitlement_balances (
                  balance_id, employee_id, entitlement_type, entitlement_year,
                  total_quota, used, planned, carryover_in, updated_at)
              VALUES (
                  gen_random_uuid(), @employeeId, @entitlementType, @entitlementYear,
                  @seedQuota, 0, 0, 0, NOW())
              ON CONFLICT (employee_id, entitlement_type, entitlement_year) DO NOTHING",
            conn, tx))
        {
            ensureCmd.Parameters.AddWithValue("employeeId", employeeId);
            ensureCmd.Parameters.AddWithValue("entitlementType", entitlementType);
            ensureCmd.Parameters.AddWithValue("entitlementYear", entitlementYear);
            ensureCmd.Parameters.AddWithValue("seedQuota", seedQuota);
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

        // UPDATE didn't match — quota would be exceeded (existing row OR freshly-created
        // zero-state row where deltaDays > quota + 0 carryover). Return current used for the
        // 422 response payload. Routes the read through the (conn, tx) overload so the
        // snapshot is consistent with the outer tx under RepeatableRead (refinement W3).
        var balance = await GetByEmployeeAndTypeInternalAsync(
            conn, tx, employeeId, entitlementType, entitlementYear, ct);
        return (false, balance?.Used ?? 0m);
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
