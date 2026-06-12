using Npgsql;

namespace StatsTid.Infrastructure;

/// <summary>
/// S71 / TASK-7104 (SPRINT-71 R6/D-E; ADR-033 slice 3b). DB-facing surface for
/// <c>termination_payout_requests</c> — the §26 stk.1 <i>efter anmodning</i> payout-request
/// record (TASK-7100 schema). One LIVE (non-voided) request per settlement row is enforced by
/// the partial-unique index <c>idx_termination_payout_requests_nonvoided</c>; VOIDED history
/// rows coexist with a later live request (surrogate <c>request_id</c> PK).
///
/// <para>
/// <b>Ownership (SPRINT-71 cross-cutting pin):</b> CREATED by TASK-7104 (single owner); the
/// TASK-7102 §26 request endpoint CONSUMES <see cref="CreateAsync"/> /
/// <see cref="GetActiveBySettlementAsync(NpgsqlConnection, NpgsqlTransaction, string, string, int, int, CancellationToken)"/>,
/// and the reversal service consumes <see cref="VoidBySettlementRowAsync"/> (R6: a reversal
/// VOIDs BOTH OPEN and LINE_STAGED requests in the SAME tx; D-E fail-closed — HR re-records
/// against the new settlement row). The <c>OPEN → LINE_STAGED</c> promotion is the §26
/// CONSUMER's write (TASK-7105, R6 — consumer promotion is authoritative), not surfaced here.
/// </para>
///
/// <para>
/// <b>Tx contract:</b> all writes take the caller-supplied <c>(conn, tx)</c> (the caller holds
/// the R12 employee advisory lock); this repository never commits or rolls back.
/// <see cref="CreateAsync"/> SAVEPOINT-wraps its INSERT (the
/// <see cref="VacationSettlementRepository.InsertAsync"/> mechanics) so the partial-unique 23505
/// surfaces as the typed <see cref="DuplicateActiveTerminationPayoutRequestException"/> on a
/// still-usable caller tx (7102 maps it to 409).
/// </para>
/// </summary>
public sealed class TerminationPayoutRequestRepository
{
    private readonly DbConnectionFactory _connectionFactory;

    public TerminationPayoutRequestRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    private const string Columns =
        "request_id, employee_id, entitlement_type, entitlement_year, settlement_sequence, " +
        "state, request_date, recorded_by, evidence_note, version, created_at, updated_at";

    /// <summary>
    /// In-tx INSERT of a new request row (state stated EXPLICITLY by the caller — the 7100
    /// no-default contract; 7102 creates OPEN rows). Returns the persisted row. Throws
    /// <see cref="DuplicateActiveTerminationPayoutRequestException"/> on the partial-unique
    /// non-voided index (a live request already exists for the settlement row — 7102's 409);
    /// any other constraint violation is rethrown raw (a real defect).
    /// </summary>
    public async Task<TerminationPayoutRequestRow> CreateAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        TerminationPayoutRequestRow row, CancellationToken ct = default)
    {
        const string savepoint = "before_request_insert";
        await using (var spCmd = new NpgsqlCommand($"SAVEPOINT {savepoint}", conn, tx))
            await spCmd.ExecuteNonQueryAsync(ct);

        try
        {
            await using var cmd = new NpgsqlCommand(
                $"""
                INSERT INTO termination_payout_requests (
                    employee_id, entitlement_type, entitlement_year, settlement_sequence,
                    state, request_date, recorded_by, evidence_note, version)
                VALUES (
                    @employeeId, @entitlementType, @entitlementYear, @settlementSequence,
                    @state, @requestDate, @recordedBy, @evidenceNote, @version)
                RETURNING {Columns}
                """, conn, tx);
            cmd.Parameters.AddWithValue("employeeId", row.EmployeeId);
            cmd.Parameters.AddWithValue("entitlementType", row.EntitlementType);
            cmd.Parameters.AddWithValue("entitlementYear", row.EntitlementYear);
            cmd.Parameters.AddWithValue("settlementSequence", row.SettlementSequence);
            cmd.Parameters.AddWithValue("state", row.State);
            cmd.Parameters.AddWithValue("requestDate", row.RequestDate);
            cmd.Parameters.AddWithValue("recordedBy", row.RecordedBy);
            cmd.Parameters.AddWithValue("evidenceNote", (object?)row.EvidenceNote ?? DBNull.Value);
            cmd.Parameters.AddWithValue("version", row.Version);

            TerminationPayoutRequestRow inserted;
            await using (var reader = await cmd.ExecuteReaderAsync(ct))
            {
                if (!await reader.ReadAsync(ct))
                {
                    throw new InvalidOperationException(
                        "TerminationPayoutRequestRepository.CreateAsync produced no row for " +
                        $"(employee_id='{row.EmployeeId}', type='{row.EntitlementType}', " +
                        $"year={row.EntitlementYear}, settlement_sequence={row.SettlementSequence}).");
                }
                inserted = ReadRow(reader);
            }

            await using var relCmd = new NpgsqlCommand($"RELEASE SAVEPOINT {savepoint}", conn, tx);
            await relCmd.ExecuteNonQueryAsync(ct);
            return inserted;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await using (var rbCmd = new NpgsqlCommand($"ROLLBACK TO SAVEPOINT {savepoint}", conn, tx))
                await rbCmd.ExecuteNonQueryAsync(ct);

            // DISCRIMINATE: only the partial-unique non-voided index means "a live request
            // already exists" (the clean 409 for 7102). Anything else (FK, CHECK) is a defect.
            if (string.Equals(ex.ConstraintName, "idx_termination_payout_requests_nonvoided", StringComparison.Ordinal))
            {
                throw new DuplicateActiveTerminationPayoutRequestException(
                    row.EmployeeId, row.EntitlementType, row.EntitlementYear, row.SettlementSequence, ex);
            }
            throw;
        }
    }

    /// <summary>
    /// In-tx read of the LIVE (non-voided) request bound to the EXACT settlement row
    /// (R6 — keyed on the 4-tuple incl. <c>settlement_sequence</c>, never the bare year-tuple),
    /// or <c>null</c>. At most one row exists (the partial-unique index).
    /// </summary>
    public async Task<TerminationPayoutRequestRow?> GetActiveBySettlementAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string employeeId, string entitlementType, int entitlementYear, int settlementSequence,
        CancellationToken ct = default)
        => await ExecuteGetActiveAsync(conn, tx, employeeId, entitlementType, entitlementYear, settlementSequence, ct);

    /// <summary>Self-managed-connection sibling of
    /// <see cref="GetActiveBySettlementAsync(NpgsqlConnection, NpgsqlTransaction, string, string, int, int, CancellationToken)"/>
    /// for read surfaces.</summary>
    public async Task<TerminationPayoutRequestRow?> GetActiveBySettlementAsync(
        string employeeId, string entitlementType, int entitlementYear, int settlementSequence,
        CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        return await ExecuteGetActiveAsync(conn, null, employeeId, entitlementType, entitlementYear, settlementSequence, ct);
    }

    private static async Task<TerminationPayoutRequestRow?> ExecuteGetActiveAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx,
        string employeeId, string entitlementType, int entitlementYear, int settlementSequence,
        CancellationToken ct)
    {
        var sql =
            $"""
            SELECT {Columns}
            FROM termination_payout_requests
            WHERE employee_id = @employeeId
              AND entitlement_type = @entitlementType
              AND entitlement_year = @entitlementYear
              AND settlement_sequence = @settlementSequence
              AND state <> 'VOIDED_BY_REVERSAL'
            """;
        await using var cmd = tx is null ? new NpgsqlCommand(sql, conn) : new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("entitlementType", entitlementType);
        cmd.Parameters.AddWithValue("entitlementYear", entitlementYear);
        cmd.Parameters.AddWithValue("settlementSequence", settlementSequence);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadRow(reader) : null;
    }

    /// <summary>
    /// In-tx VOID of EVERY non-voided request bound to the reversed settlement row
    /// (SPRINT-71 R6/D-E: a reversal VOIDs BOTH OPEN and LINE_STAGED requests in the SAME tx —
    /// a LINE_STAGED request's staged line is compensated by the R9 Payroll consumer, never
    /// deleted). State → <c>VOIDED_BY_REVERSAL</c>, version bumped (ADR-019). Returns the voided
    /// <c>request_id</c>s (empty when no live request existed — a benign no-op, NOT an error:
    /// most settlement rows never had a request).
    /// </summary>
    public async Task<IReadOnlyList<long>> VoidBySettlementRowAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string employeeId, string entitlementType, int entitlementYear, int settlementSequence,
        CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE termination_payout_requests
               SET state = 'VOIDED_BY_REVERSAL',
                   version = version + 1,
                   updated_at = NOW()
             WHERE employee_id = @employeeId
               AND entitlement_type = @entitlementType
               AND entitlement_year = @entitlementYear
               AND settlement_sequence = @settlementSequence
               AND state <> 'VOIDED_BY_REVERSAL'
            RETURNING request_id
            """, conn, tx);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("entitlementType", entitlementType);
        cmd.Parameters.AddWithValue("entitlementYear", entitlementYear);
        cmd.Parameters.AddWithValue("settlementSequence", settlementSequence);

        var voided = new List<long>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            voided.Add(reader.GetInt64(0));
        return voided;
    }

    private static TerminationPayoutRequestRow ReadRow(NpgsqlDataReader reader) => new()
    {
        RequestId = reader.GetInt64(reader.GetOrdinal("request_id")),
        EmployeeId = reader.GetString(reader.GetOrdinal("employee_id")),
        EntitlementType = reader.GetString(reader.GetOrdinal("entitlement_type")),
        EntitlementYear = reader.GetInt32(reader.GetOrdinal("entitlement_year")),
        SettlementSequence = reader.GetInt32(reader.GetOrdinal("settlement_sequence")),
        State = reader.GetString(reader.GetOrdinal("state")),
        RequestDate = reader.GetFieldValue<DateOnly>(reader.GetOrdinal("request_date")),
        RecordedBy = reader.GetString(reader.GetOrdinal("recorded_by")),
        EvidenceNote = reader.IsDBNull(reader.GetOrdinal("evidence_note"))
            ? null
            : reader.GetString(reader.GetOrdinal("evidence_note")),
        Version = reader.GetInt64(reader.GetOrdinal("version")),
        CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
        UpdatedAt = reader.GetDateTime(reader.GetOrdinal("updated_at")),
    };
}

/// <summary>
/// S71 / TASK-7104 — repo-row DTO for <c>termination_payout_requests</c> (SPRINT-71 R6;
/// TASK-7100 column contract). Init-only (PAT-001). <see cref="RequestId"/> is the surrogate
/// BIGSERIAL PK (0 until persisted); lifecycle <see cref="State"/> ∈
/// OPEN / LINE_STAGED / VOIDED_BY_REVERSAL (CHECK; no DB default — every writer states it).
/// </summary>
public sealed record TerminationPayoutRequestRow
{
    public const string StateOpen = "OPEN";
    public const string StateLineStaged = "LINE_STAGED";
    public const string StateVoidedByReversal = "VOIDED_BY_REVERSAL";

    public long RequestId { get; init; }
    public required string EmployeeId { get; init; }
    public required string EntitlementType { get; init; }
    public required int EntitlementYear { get; init; }

    /// <summary>The settlement-ROW sequence (R2 vocabulary — the request binds to the EXACT row).</summary>
    public required int SettlementSequence { get; init; }

    public required string State { get; init; }

    /// <summary>The as-stated HR-recorded anmodning date (evidence; created_at records insertion).</summary>
    public required DateOnly RequestDate { get; init; }

    /// <summary>The recording HR actor (users.user_id-shaped TEXT; the audit actor convention).</summary>
    public required string RecordedBy { get; init; }

    public string? EvidenceNote { get; init; }

    /// <summary>ADR-019 If-Match/CAS row version (1-based).</summary>
    public long Version { get; init; } = 1;

    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

/// <summary>
/// S71 / TASK-7104 — raised by <see cref="TerminationPayoutRequestRepository.CreateAsync"/> when
/// the partial-unique non-voided index (<c>idx_termination_payout_requests_nonvoided</c>) rejects
/// the insert: a LIVE request already exists for the settlement row (SPRINT-71 R6 — one
/// non-voided request per row). The TASK-7102 endpoint maps this to 409. Only that index maps
/// here — any other 23505 propagates raw.
/// </summary>
public sealed class DuplicateActiveTerminationPayoutRequestException : Exception
{
    public string EmployeeId { get; }
    public string EntitlementType { get; }
    public int EntitlementYear { get; }
    public int SettlementSequence { get; }

    public DuplicateActiveTerminationPayoutRequestException(
        string employeeId, string entitlementType, int entitlementYear, int settlementSequence,
        Exception innerException)
        : base(
            $"A live (non-voided) termination payout request already exists for " +
            $"(employee_id='{employeeId}', type='{entitlementType}', year={entitlementYear}, " +
            $"settlement_sequence={settlementSequence}); one non-voided request per settlement row " +
            "(idx_termination_payout_requests_nonvoided).",
            innerException)
    {
        EmployeeId = employeeId;
        EntitlementType = entitlementType;
        EntitlementYear = entitlementYear;
        SettlementSequence = settlementSequence;
    }
}
