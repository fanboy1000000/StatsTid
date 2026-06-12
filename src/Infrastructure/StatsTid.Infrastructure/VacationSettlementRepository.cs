using Npgsql;
using NpgsqlTypes;

namespace StatsTid.Infrastructure;

/// <summary>
/// S68 / TASK-6804 (ADR-033 D5/D6). DB-facing surface for <c>vacation_settlements</c> — the
/// settlement identity + state machine row written by the atomic settlement pass
/// (<see cref="VacationSettlementService"/>). One row per
/// <c>(employee_id, entitlement_type, entitlement_year, sequence)</c>; the partial-unique index
/// <c>idx_vacation_settlements_active</c> enforces at most one non-REVERSED ("active") row per
/// <c>(employee, type, year)</c> — the ADR-018 D8 single-active live-row pattern, here the
/// single-settle backstop.
///
/// <para>
/// <b>Atomic-outbox contract (ADR-018 D3/D5).</b> The write path takes the caller-supplied
/// <c>(conn, tx)</c> and appends the <c>vacation_settlement_audit</c> row in the same
/// transaction (ADR-019 D8 version-transition columns — mirrors
/// <see cref="EntitlementConfigRepository.AppendAuditAsync"/>). The caller commits or rolls
/// back; this method does NOT.
/// </para>
///
/// <para>
/// <b>Single-settle backstop (ADR-018 D8).</b> <see cref="InsertAsync"/> catches the partial-
/// unique-index 23505 and surfaces it as <see cref="DuplicateActiveSettlementException"/> so a
/// racing poller (TASK-6805 concurrent multi-instance) that lost the lock-then-recheck window
/// can swallow it benignly (exactly-one settlement). The in-lock idempotency re-check uses
/// <see cref="GetActiveAsync(NpgsqlConnection, NpgsqlTransaction, string, string, int, CancellationToken)"/>.
/// </para>
/// </summary>
public sealed class VacationSettlementRepository
{
    private readonly DbConnectionFactory _connectionFactory;

    public VacationSettlementRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <summary>
    /// Self-managed-connection read of the ACTIVE (non-REVERSED) settlement for a natural key,
    /// or <c>null</c> when the year is unsettled. Used by balance readers (TASK-6807) and the
    /// endpoint's reject-post-settlement precondition.
    /// </summary>
    public async Task<VacationSettlementRow?> GetActiveAsync(
        string employeeId, string entitlementType, int entitlementYear, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        return await ExecuteGetActiveAsync(conn, null, employeeId, entitlementType, entitlementYear, ct);
    }

    /// <summary>
    /// In-transaction sibling overload (ADR-018 D3) — the in-lock idempotency re-check. Reuses
    /// the caller-supplied <paramref name="conn"/> + <paramref name="tx"/> (the settlement tx,
    /// holding the advisory lock) so it observes any active row a racing poller committed before
    /// the lock was acquired. Returns the ACTIVE row or <c>null</c>.
    /// </summary>
    public async Task<VacationSettlementRow?> GetActiveAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string employeeId, string entitlementType, int entitlementYear, CancellationToken ct = default)
        => await ExecuteGetActiveAsync(conn, tx, employeeId, entitlementType, entitlementYear, ct);

    private static async Task<VacationSettlementRow?> ExecuteGetActiveAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx,
        string employeeId, string entitlementType, int entitlementYear, CancellationToken ct)
    {
        const string sql =
            """
            SELECT employee_id, entitlement_type, entitlement_year, sequence,
                   settlement_state, trigger, snapshot::text AS snapshot_text,
                   transfer_days, payout_days, forfeit_days,
                   payout_reconciled_at, payout_reconciled_by, review_disposition,
                   claim_disposition_days, bare_reversal_not_due,
                   version, created_at, updated_at
            FROM vacation_settlements
            WHERE employee_id = @employeeId
              AND entitlement_type = @entitlementType
              AND entitlement_year = @entitlementYear
              AND settlement_state <> 'REVERSED'
            """;
        await using var cmd = tx is null ? new NpgsqlCommand(sql, conn) : new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("entitlementType", entitlementType);
        cmd.Parameters.AddWithValue("entitlementYear", entitlementYear);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadRow(reader) : null;
    }

    /// <summary>
    /// In-transaction insert of a settlement row + its CREATED audit row (ADR-018 D5). The
    /// caller (<see cref="VacationSettlementService"/>) supplies the pre-serialized immutable
    /// snapshot JSON (ADR-033 D3) and the partition bucket day-counts (pure of the snapshot).
    /// Throws <see cref="DuplicateActiveSettlementException"/> on the partial-unique-index 23505
    /// (a racing poller already settled this year) so the caller can swallow it benignly.
    /// Returns the persisted row.
    /// </summary>
    public async Task<VacationSettlementRow> InsertAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        VacationSettlementRow row, string snapshotJson, string actorId, string actorRole,
        CancellationToken ct = default)
    {
        // BLOCKER 3 (Codex Step-5a) — wrap the INSERT in a SAVEPOINT so a 23505 does NOT leave the
        // caller's outer tx ABORTED. A unique violation aborts the current (sub)transaction; without
        // a savepoint the catch path could neither query nor let the caller commit the rest of the
        // settlement. SAVEPOINT before_insert → on 23505, ROLLBACK TO SAVEPOINT restores the outer tx
        // to a usable state, then we surface the typed signal (or rethrow a non-active-race collision).
        const string savepoint = "before_insert";
        await using (var spCmd = new NpgsqlCommand($"SAVEPOINT {savepoint}", conn, tx))
            await spCmd.ExecuteNonQueryAsync(ct);

        VacationSettlementRow inserted;
        try
        {
            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO vacation_settlements (
                    employee_id, entitlement_type, entitlement_year, sequence,
                    settlement_state, trigger, snapshot,
                    transfer_days, payout_days, forfeit_days, version)
                VALUES (
                    @employeeId, @entitlementType, @entitlementYear, @sequence,
                    @settlementState, @trigger, @snapshot,
                    @transferDays, @payoutDays, @forfeitDays, @version)
                RETURNING employee_id, entitlement_type, entitlement_year, sequence,
                          settlement_state, trigger, snapshot::text AS snapshot_text,
                          transfer_days, payout_days, forfeit_days,
                          payout_reconciled_at, payout_reconciled_by, review_disposition,
                          claim_disposition_days, bare_reversal_not_due,
                          version, created_at, updated_at
                """, conn, tx);
            cmd.Parameters.AddWithValue("employeeId", row.EmployeeId);
            cmd.Parameters.AddWithValue("entitlementType", row.EntitlementType);
            cmd.Parameters.AddWithValue("entitlementYear", row.EntitlementYear);
            cmd.Parameters.AddWithValue("sequence", row.Sequence);
            cmd.Parameters.AddWithValue("settlementState", row.SettlementState);
            cmd.Parameters.AddWithValue("trigger", row.Trigger);
            cmd.Parameters.Add(new NpgsqlParameter("snapshot", NpgsqlDbType.Jsonb) { Value = snapshotJson });
            cmd.Parameters.AddWithValue("transferDays", row.TransferDays);
            cmd.Parameters.AddWithValue("payoutDays", row.PayoutDays);
            cmd.Parameters.AddWithValue("forfeitDays", row.ForfeitDays);
            cmd.Parameters.AddWithValue("version", row.Version);
            await using (var reader = await cmd.ExecuteReaderAsync(ct))
            {
                if (!await reader.ReadAsync(ct))
                {
                    throw new InvalidOperationException(
                        $"VacationSettlementRepository.InsertAsync produced no row for " +
                        $"(employee_id='{row.EmployeeId}', entitlement_type='{row.EntitlementType}', " +
                        $"entitlement_year={row.EntitlementYear}, sequence={row.Sequence}).");
                }
                inserted = ReadRow(reader);
            } // reader closed here — required before another command runs on this connection (no MARS).

            // Happy path: release the savepoint (frees its resources; the row stays in the outer tx).
            await using var relCmd = new NpgsqlCommand($"RELEASE SAVEPOINT {savepoint}", conn, tx);
            await relCmd.ExecuteNonQueryAsync(ct);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            // The 23505 aborted only the savepoint sub-tx — roll back TO it so the caller's outer tx
            // is usable again (it can commit the no-op or proceed; the backstop GetActiveAsync re-read
            // in the service runs on the restored tx).
            await using (var rbCmd = new NpgsqlCommand($"ROLLBACK TO SAVEPOINT {savepoint}", conn, tx))
                await rbCmd.ExecuteNonQueryAsync(ct);

            // DISCRIMINATE the constraint (Codex Step-5a): ONLY the partial-unique-active index means
            // "an active (non-REVERSED) settlement already exists" — the single-settle race a racing
            // poller (TASK-6805) must swallow benignly. A composite-PK collision
            // (vacation_settlements_pkey: same employee/type/year/SEQUENCE) is a DIFFERENT defect (a
            // duplicate sequence, not an active race) and must NOT be laundered into the benign path —
            // rethrow it so it surfaces.
            if (string.Equals(ex.ConstraintName, "idx_vacation_settlements_active", StringComparison.Ordinal))
            {
                throw new DuplicateActiveSettlementException(
                    row.EmployeeId, row.EntitlementType, row.EntitlementYear, ex);
            }
            throw;
        }

        // CREATED audit row (ADR-019 D8): a fresh settlement has no predecessor state.
        await AppendAuditAsync(
            conn, tx, inserted, "CREATED",
            previousData: null, newData: snapshotJson,
            versionBefore: null, versionAfter: inserted.Version,
            actorId, actorRole, ct);
        return inserted;
    }

    /// <summary>
    /// S71 / TASK-7104 (SPRINT-71 R1) — in-tx read of ALL recorded settlement-row sequences for a
    /// <c>(employee, type, year)</c> tuple, any state (REVERSED included). The reversal service
    /// derives the next-generation row sequence from THIS history
    /// (<c>SettlementReversalService.NextGenerationRowSequence</c>: <c>g = max((s+1)/2) + 1</c> →
    /// row sequence <c>2g−1</c>) — a new settlement of a tuple NEVER restarts at 1. Runs on the
    /// caller's advisory-locked tx so it observes the row the same tx just REVERSED.
    /// </summary>
    public async Task<IReadOnlyList<int>> GetSequencesForTupleAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string employeeId, string entitlementType, int entitlementYear, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            """
            SELECT sequence FROM vacation_settlements
            WHERE employee_id = @employeeId
              AND entitlement_type = @entitlementType
              AND entitlement_year = @entitlementYear
            ORDER BY sequence
            """, conn, tx);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("entitlementType", entitlementType);
        cmd.Parameters.AddWithValue("entitlementYear", entitlementYear);
        var sequences = new List<int>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            sequences.Add(reader.GetInt32(0));
        return sequences;
    }

    /// <summary>
    /// S71 / TASK-7104 (SPRINT-71 R3) — in-tx probe: does the tuple carry the durable
    /// bare-reversal not-due marker (<c>bare_reversal_not_due = TRUE</c> on a REVERSED row)?
    /// <c>SettleAsync</c> rejects (benign NotDue no-op) when TRUE — a bare-reversed tuple is
    /// unrevivable by construction in 3b (the marker-clearing + g+1 revival belong to the
    /// REHIRE/recovery follow-up). The Step-B enumeration's shared anti-join is the poll-side
    /// twin of this in-lock check (the S70 B1 lesson: every enumeration predicate re-evaluated
    /// under the lock).
    /// </summary>
    public async Task<bool> HasBareReversalMarkerAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string employeeId, string entitlementType, int entitlementYear, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1 FROM vacation_settlements
                WHERE employee_id = @employeeId
                  AND entitlement_type = @entitlementType
                  AND entitlement_year = @entitlementYear
                  AND settlement_state = 'REVERSED'
                  AND bare_reversal_not_due = TRUE)
            """, conn, tx);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("entitlementType", entitlementType);
        cmd.Parameters.AddWithValue("entitlementYear", entitlementYear);
        return await cmd.ExecuteScalarAsync(ct) is true;
    }

    /// <summary>
    /// In-transaction audit insert (ADR-018 D5 + ADR-019 D8 version-transition columns). Mirrors
    /// <see cref="EntitlementConfigRepository.AppendAuditAsync"/>; writes the
    /// <c>vacation_settlement_audit</c> row (action CHECK ∈ CREATED/UPDATED/DELETED/SUPERSEDED —
    /// the D10 resolve path / reversal path (TASK-6806/slice-4) use UPDATED).
    /// </summary>
    public async Task AppendAuditAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        VacationSettlementRow row, string action,
        string? previousData, string? newData,
        long? versionBefore, long? versionAfter,
        string actorId, string actorRole, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO vacation_settlement_audit
                (employee_id, entitlement_type, entitlement_year, sequence, action,
                 previous_data, new_data, version_before, version_after,
                 actor_id, actor_role)
            VALUES
                (@employeeId, @entitlementType, @entitlementYear, @sequence, @action,
                 @previousData::jsonb, @newData::jsonb, @versionBefore, @versionAfter,
                 @actorId, @actorRole)
            """, conn, tx);
        cmd.Parameters.AddWithValue("employeeId", row.EmployeeId);
        cmd.Parameters.AddWithValue("entitlementType", row.EntitlementType);
        cmd.Parameters.AddWithValue("entitlementYear", row.EntitlementYear);
        cmd.Parameters.AddWithValue("sequence", row.Sequence);
        cmd.Parameters.AddWithValue("action", action);
        cmd.Parameters.AddWithValue("previousData", (object?)previousData ?? DBNull.Value);
        cmd.Parameters.AddWithValue("newData", (object?)newData ?? DBNull.Value);
        cmd.Parameters.AddWithValue("versionBefore", (object?)versionBefore ?? DBNull.Value);
        cmd.Parameters.AddWithValue("versionAfter", (object?)versionAfter ?? DBNull.Value);
        cmd.Parameters.AddWithValue("actorId", actorId);
        cmd.Parameters.AddWithValue("actorRole", actorRole);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static VacationSettlementRow ReadRow(NpgsqlDataReader reader) => new()
    {
        EmployeeId = reader.GetString(reader.GetOrdinal("employee_id")),
        EntitlementType = reader.GetString(reader.GetOrdinal("entitlement_type")),
        EntitlementYear = reader.GetInt32(reader.GetOrdinal("entitlement_year")),
        Sequence = reader.GetInt32(reader.GetOrdinal("sequence")),
        SettlementState = reader.GetString(reader.GetOrdinal("settlement_state")),
        Trigger = reader.GetString(reader.GetOrdinal("trigger")),
        SnapshotJson = reader.GetString(reader.GetOrdinal("snapshot_text")),
        TransferDays = reader.GetDecimal(reader.GetOrdinal("transfer_days")),
        PayoutDays = reader.GetDecimal(reader.GetOrdinal("payout_days")),
        ForfeitDays = reader.GetDecimal(reader.GetOrdinal("forfeit_days")),
        PayoutReconciledAt = reader.IsDBNull(reader.GetOrdinal("payout_reconciled_at"))
            ? null
            : reader.GetDateTime(reader.GetOrdinal("payout_reconciled_at")),
        PayoutReconciledBy = reader.IsDBNull(reader.GetOrdinal("payout_reconciled_by"))
            ? null
            : reader.GetString(reader.GetOrdinal("payout_reconciled_by")),
        ReviewDisposition = reader.IsDBNull(reader.GetOrdinal("review_disposition"))
            ? null
            : reader.GetString(reader.GetOrdinal("review_disposition")),
        // S71 / TASK-7104 (SPRINT-71 R5/R10) — the §7/waiver resolved claim quantity + the R3
        // bare-reversal marker, read so the reversal service can stamp ClaimDispositionDays onto
        // SettlementReversed and so callers can observe the marker without raw SQL.
        ClaimDispositionDays = reader.IsDBNull(reader.GetOrdinal("claim_disposition_days"))
            ? null
            : reader.GetDecimal(reader.GetOrdinal("claim_disposition_days")),
        BareReversalNotDue = reader.GetBoolean(reader.GetOrdinal("bare_reversal_not_due")),
        // internal-Reviewer BLOCKER (Step-5a) — `version` is BIGINT (Orchestrator schema fix);
        // read it as Int64. GetInt32 would throw InvalidCastException on the BIGINT column. Matches
        // VacationTransferAgreementRepository.ReadAgreement + the system-wide `version BIGINT`.
        Version = reader.GetInt64(reader.GetOrdinal("version")),
        CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
        UpdatedAt = reader.GetDateTime(reader.GetOrdinal("updated_at")),
    };
}

/// <summary>
/// S68 / TASK-6804 — repo-row DTO for <c>vacation_settlements</c> (ADR-033 D5). Co-located with
/// its repository (the <see cref="SaveEntitlementConfigResult"/> precedent). Init-only (PAT-001).
/// The immutable settle-time <c>snapshot</c> is carried as its pre-serialized JSON text
/// (<see cref="SnapshotJson"/>); the partition bucket day-counts (<see cref="TransferDays"/> §21 /
/// <see cref="PayoutDays"/> §24 / <see cref="ForfeitDays"/> §34) are pure functions of that
/// snapshot (ADR-033 D3, replay-stable).
/// </summary>
public sealed record VacationSettlementRow
{
    public required string EmployeeId { get; init; }
    public required string EntitlementType { get; init; }
    public required int EntitlementYear { get; init; }

    /// <summary>Settlement sequence (1 for the first settlement; reversal histories bump it).</summary>
    public required int Sequence { get; init; }

    /// <summary>The state machine value: PENDING_REVIEW / SETTLED / REVERSED (CHECK enum).</summary>
    public required string SettlementState { get; init; }

    /// <summary>What caused the settlement: YEAR_END / TERMINATION (CHECK enum).</summary>
    public required string Trigger { get; init; }

    /// <summary>The immutable settle-time input snapshot, pre-serialized JSON (ADR-033 D3).</summary>
    public required string SnapshotJson { get; init; }

    /// <summary>§21 transferred days (the next-year carryover_in provenance component; ADR-033 D6).</summary>
    public decimal TransferDays { get; init; }

    /// <summary>§24 auto-payout days (day-count payout line; ADR-033 D7).</summary>
    public decimal PayoutDays { get; init; }

    /// <summary>§34 forfeiture-candidate days (auto-resolved only via the D10 manual path).</summary>
    public decimal ForfeitDays { get; init; }

    /// <summary>§24 manual-reconciliation marker timestamp (paired-nullable; S69 honors it).</summary>
    public DateTime? PayoutReconciledAt { get; init; }

    /// <summary>§24 manual-reconciliation actor (paired-nullable with <see cref="PayoutReconciledAt"/>).</summary>
    public string? PayoutReconciledBy { get; init; }

    /// <summary>PENDING_REVIEW operator outcome: FORFEIT / DEFER / MODREGNING / WAIVED
    /// (CHECK, S71 R5 widened; null until resolved).</summary>
    public string? ReviewDisposition { get; init; }

    /// <summary>S71 R5 — the §7/waiver resolved claim day-count (<c>claim_disposition_days</c>;
    /// non-null exactly when <see cref="ReviewDisposition"/> is MODREGNING/WAIVED, DB-paired).</summary>
    public decimal? ClaimDispositionDays { get; init; }

    /// <summary>S71 R3 — the durable bare-reversal not-due marker (TRUE only on a REVERSED row;
    /// TERMINAL in 3b — no operation clears it).</summary>
    public bool BareReversalNotDue { get; init; }

    /// <summary>ADR-019 If-Match row version (sequence=1 first settlement starts at version 1).
    /// <c>long</c> to match the <c>version BIGINT</c> column (internal-Reviewer Step-5a) + the
    /// <see cref="VacationTransferAgreement.Version"/> sibling.</summary>
    public long Version { get; init; } = 1;

    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

/// <summary>
/// S68 / TASK-6804 — raised by <see cref="VacationSettlementRepository.InsertAsync"/> when the
/// partial-unique-active index (<c>idx_vacation_settlements_active</c>) rejects the insert: an
/// active (non-REVERSED) settlement already exists for the <c>(employee, type, year)</c>. ONLY that
/// index maps to this exception — a composite-PK (sequence) collision is a distinct defect and
/// propagates as the raw <see cref="Npgsql.PostgresException"/>. The single-settle backstop
/// (ADR-018 D8) — a racing concurrent poller (TASK-6805) catches this and swallows it benignly,
/// preserving exactly-one settlement + one event family.
/// </summary>
public sealed class DuplicateActiveSettlementException : Exception
{
    public string EmployeeId { get; }
    public string EntitlementType { get; }
    public int EntitlementYear { get; }

    public DuplicateActiveSettlementException(
        string employeeId, string entitlementType, int entitlementYear, Exception innerException)
        : base(
            $"An active vacation settlement already exists for (employee_id='{employeeId}', " +
            $"entitlement_type='{entitlementType}', entitlement_year={entitlementYear}); " +
            "single-settle backstop (idx_vacation_settlements_active).",
            innerException)
    {
        EmployeeId = employeeId;
        EntitlementType = entitlementType;
        EntitlementYear = entitlementYear;
    }
}
