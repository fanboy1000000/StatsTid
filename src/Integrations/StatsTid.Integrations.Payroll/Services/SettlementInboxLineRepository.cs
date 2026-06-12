using Npgsql;
using StatsTid.Infrastructure;

namespace StatsTid.Integrations.Payroll.Services;

/// <summary>
/// S69 / TASK-6904 + S71 / TASK-7105 (ADR-033 D4; SPRINT-71 R1/R2/R6/R7/R8/R9) — the data-access
/// seam for the settlement-export consumers. Encapsulates every read/write the
/// <see cref="SettlementExportEmitter"/> performs against the shared <c>statstid</c> database: the
/// canonical <c>events</c>-table poll (the unconsumed settlement-event selection across the three
/// consumed types), the employee advisory lock, the under-lock settlement/request re-reads (R6/R9),
/// the reconciled-skip probe, the immutable <c>settlement_export_lines</c> insert (with
/// verify-on-conflict, now ORIGINAL/REVERSAL-aware per R8), and the terminal-aware
/// <c>settlement_payroll_inbox</c> upserts at the S71 composite key.
///
/// <para>
/// <b>Exactly-once invariant (S69 Step-0b B1/C2-B1/C3-B1/C4-B1; SPRINT-71 R7).</b> The inbox PK
/// <c>(source_event_id, bucket)</c> is the authoritative consumer dedup — ONE consumed event may
/// checkpoint MULTIPLE buckets (the <c>SettlementReversed</c> consumer), all written atomically in
/// one tx; the line UNIQUE business key
/// <c>(employee_id, entitlement_type, entitlement_year, sequence, bucket)</c> is the line dedup
/// (the <c>sequence</c> axis carries the R1/R2 EXPORT sequence: odd <c>2g−1</c> for originals,
/// even <c>2g</c> for compensating reversal lines). Every inbox write moves MONOTONICALLY toward a
/// terminal status, per bucket AND at event level: the success/skip path is a conditional
/// PROMOTION (<c>ON CONFLICT (source_event_id, bucket) DO UPDATE … WHERE processing_status =
/// 'RETRY_PENDING'</c>) that also promotes a prior event-level <c>'_EVENT'</c> RETRY_PENDING
/// diagnostics row to the same terminal; the post-rollback diagnostics write keys at
/// <c>'_EVENT'</c> with the SAME terminal-aware guard so a concurrently-committed terminal status
/// is NEVER overwritten, and (Step-5a cycle-1 B1) NO-OPs entirely when ANY terminal row already
/// exists for the event — a late diagnostics write must never strand a fresh non-terminal
/// <c>'_EVENT'</c> row behind a completed event. Both completion orders therefore converge:
/// diagnostics-then-completion promotes; completion-then-diagnostics no-ops.
/// </para>
///
/// <para>
/// <b>The <c>'_EVENT'</c> sentinel bucket (SPRINT-71 R7).</b> Event-level rows key at
/// <c>bucket = <see cref="EventLevelBucket"/></c>: a TERMINAL <c>'_EVENT'</c> row (DEAD_LETTER —
/// poison/collision/retry budget) covers EVERY bucket of the event (bucket-keyed status reads via
/// <see cref="GetTerminalStatusAsync"/> treat it so) and is mutually exclusive with real-bucket
/// checkpoints; transient-failure diagnostics (RETRY_PENDING) also key at <c>'_EVENT'</c>
/// (non-terminal). A <c>SettlementReversed</c> event with nothing to compensate records a terminal
/// <c>'_EVENT'</c> PROCESSED no-op checkpoint.
/// </para>
///
/// <para>
/// <b>Cross-domain reads (DECLARED).</b> The Payroll context reads the canonical <c>events</c>
/// table, <c>vacation_settlements</c> (the R6/R9 under-lock state/snapshot re-reads) and
/// <c>termination_payout_requests</c> (the R6 request re-read + the consumer-authoritative
/// OPEN→LINE_STAGED promotion) directly here — the same cross-table read shape
/// <see cref="StatsTid.Infrastructure.SettlementCloseService"/> uses against the shared DB. The
/// Backend's <see cref="StatsTid.Infrastructure.TerminationPayoutRequestRepository"/> is not
/// consumable from this context's tx contract, so the two request statements are inlined
/// (SPRINT-71 TASK-7105 working rule).
/// </para>
/// </summary>
public sealed class SettlementInboxLineRepository
{
    /// <summary>The R7 event-level sentinel bucket (poison, diagnostics, no-op checkpoints).</summary>
    public const string EventLevelBucket = "_EVENT";

    /// <summary>The terminal inbox statuses (RETRY_PENDING is the ONLY non-terminal one).</summary>
    private const string TerminalStatusList = "'PROCESSED', 'SKIPPED_RECONCILED', 'SKIPPED_VOIDED', 'DEAD_LETTER'";

    private readonly DbConnectionFactory _connectionFactory;

    public SettlementInboxLineRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <summary>One raw settlement-event row to consume: <c>events.event_id</c> + type + JSONB body.</summary>
    public readonly record struct PendingEvent(Guid EventId, string EventType, string Data);

    /// <summary>
    /// The authoritative poll selection (S69 Step-0b B1/W8; S71 type-discriminated): the canonical
    /// <c>events</c>-table rows of the three consumed settlement types
    /// (<c>VacationAutoPaidOut</c> / <c>TerminationPayoutRequested</c> / <c>SettlementReversed</c>)
    /// that have NO TERMINAL inbox row — i.e. no <c>settlement_payroll_inbox</c> row at all, or
    /// only rows still in the single non-terminal <c>RETRY_PENDING</c> status. ANY terminal row
    /// (any bucket, incl. a terminal <c>'_EVENT'</c> row) excludes the event from re-selection —
    /// sound because a multi-bucket event writes ALL its bucket checkpoints atomically in one tx
    /// (R7: no partial subset can ever commit). Ordered by <c>global_position</c> for stable
    /// cross-type progress; no cursor table (events are rare; the inbox is the dedup).
    /// </summary>
    public async Task<IReadOnlyList<PendingEvent>> GetUnconsumedSettlementEventsAsync(int batchSize, CancellationToken ct)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            $"""
            SELECT e.event_id, e.event_type, e.data::text
            FROM events e
            WHERE e.event_type IN ('VacationAutoPaidOut', 'TerminationPayoutRequested', 'SettlementReversed')
              AND NOT EXISTS (
                    SELECT 1 FROM settlement_payroll_inbox i
                    WHERE i.source_event_id = e.event_id
                      AND i.processing_status IN ({TerminalStatusList}))
            ORDER BY e.global_position
            LIMIT @batchSize
            """, conn);
        cmd.Parameters.AddWithValue("batchSize", batchSize);

        var result = new List<PendingEvent>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            result.Add(new PendingEvent(reader.GetGuid(0), reader.GetString(1), reader.GetString(2)));
        return result;
    }

    /// <summary>
    /// Opens a connection + transaction for processing ONE event, takes the ADR-032 D4 employee
    /// advisory lock FIRST (held to commit — the SAME key as the Backend close service, the
    /// reversal service and the reconcile retrofit, so consumer-claim, reversal and reconcile are
    /// mutually exclusive; SPRINT-71 R12), and returns the open handles to the caller. The caller
    /// commits or rolls back.
    /// </summary>
    public async Task<(NpgsqlConnection Conn, NpgsqlTransaction Tx)> BeginLockedAsync(string employeeId, CancellationToken ct)
    {
        var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        // ReadCommitted: the in-lock probes must observe a reversal/reconcile that committed just
        // before the lock was granted (same reasoning as SettlementCloseService's per-tuple tx).
        var tx = await conn.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct);
        await AcquireEmployeeLockAsync(conn, tx, employeeId, ct);
        return (conn, tx);
    }

    /// <summary>
    /// The ADR-032 D4 employee-scoped advisory lock (<c>pg_advisory_xact_lock(hashtext('employee-'||id))</c>),
    /// xact-scoped (auto-released at COMMIT/ROLLBACK). Identical SQL/key to
    /// <see cref="StatsTid.Infrastructure.VacationSettlementService"/>, the S71 reversal service and
    /// the reconcile retrofit — the VALUE is what serializes the writers (R12).
    /// </summary>
    public static async Task AcquireEmployeeLockAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string employeeId, CancellationToken ct)
    {
        await using var lockCmd = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtext('employee-' || @employeeId))", conn, tx);
        lockCmd.Parameters.AddWithValue("employeeId", employeeId);
        await lockCmd.ExecuteScalarAsync(ct);
    }

    /// <summary>
    /// Reads the inbox TERMINAL status covering <paramref name="eventId"/> UNDER THE HELD LOCK
    /// (the S69 Step-5a select→lock TOCTOU re-check, R7-bucket-aware). Returns the terminal status
    /// when the event is already finalized for the probe's scope, else <c>null</c> (absent or
    /// RETRY_PENDING rows ⇒ proceed):
    /// <list type="bullet">
    ///   <item><paramref name="bucket"/> non-null — bucket-scoped: a terminal row at THAT bucket
    ///     OR a terminal event-level <c>'_EVENT'</c> row counts (R7: a terminal <c>'_EVENT'</c>
    ///     row covers every bucket);</item>
    ///   <item><paramref name="bucket"/> null — event-level: ANY terminal row counts (used by the
    ///     multi-bucket <c>SettlementReversed</c> consumer — sound because its bucket checkpoints
    ///     only ever commit as a complete atomic set, R7 — and by the
    ///     <see cref="WriteDiagnosticsAsync"/> completed-event no-op guard, Step-5a cycle-1 B1).</item>
    /// </list>
    /// </summary>
    public async Task<string?> GetTerminalStatusAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, Guid eventId, string? bucket, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            $"""
            SELECT processing_status FROM settlement_payroll_inbox
            WHERE source_event_id = @eventId
              AND processing_status IN ({TerminalStatusList})
              AND (@bucket::text IS NULL OR bucket = @bucket OR bucket = '{EventLevelBucket}')
            LIMIT 1
            """, conn, tx);
        cmd.Parameters.AddWithValue("eventId", eventId);
        cmd.Parameters.AddWithValue("bucket", (object?)bucket ?? DBNull.Value);
        var value = await cmd.ExecuteScalarAsync(ct);
        return value is string s ? s : null;
    }

    /// <summary>
    /// Reads the EVENT's settlement row's <c>payout_reconciled_at</c> under the held lock (the
    /// reconciled-skip gate, S69 Step-0b B2). Returns <c>true</c> when an operator has already
    /// reconciled the §24 bucket (the emitter must then stage NO line and write a
    /// SKIPPED_RECONCILED checkpoint). A missing row, or a row with a NULL
    /// <c>payout_reconciled_at</c>, returns <c>false</c> — there is nothing to skip-for, and the
    /// claim path's downstream validation still governs.
    ///
    /// <para>
    /// S69 Step-5a WARNING (P1) — the probe binds the EVENT's full settlement identity
    /// <c>(employee_id, entitlement_type, entitlement_year, sequence)</c>, NOT a
    /// <c>settlement_state = 'SETTLED'</c>-only selector: after a reversal/re-close a DIFFERENT
    /// sequence becomes the active row, and the skip decision must stay tied to the exact
    /// settlement the line would pair with (load-bearing since S71 made reversal real).
    /// </para>
    /// </summary>
    public async Task<bool> IsPayoutReconciledAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string employeeId, string entitlementType, int entitlementYear, int sequence, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            """
            SELECT payout_reconciled_at
            FROM vacation_settlements
            WHERE employee_id = @employeeId
              AND entitlement_type = @entitlementType
              AND entitlement_year = @entitlementYear
              AND sequence = @sequence
            """, conn, tx);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("entitlementType", entitlementType);
        cmd.Parameters.AddWithValue("entitlementYear", entitlementYear);
        cmd.Parameters.AddWithValue("sequence", sequence);
        var value = await cmd.ExecuteScalarAsync(ct);
        return value is not null && value is not DBNull;
    }

    /// <summary>A minimal under-lock probe of one settlement ROW at its exact sequence (R6/R9).</summary>
    public readonly record struct SettlementRowProbe(string State, string Trigger, string SnapshotJson);

    /// <summary>
    /// Reads the settlement row at the EVENT's exact <c>(identity, sequence)</c> under the held
    /// lock (SPRINT-71 R6/R9 — the under-lock active-settlement re-check every line-staging
    /// consumer performs before staging). Returns <c>null</c> when no row exists (the S69 §24
    /// emitter tolerates a missing row — its event is the authority; the §26 consumer treats a
    /// missing row as a broken FK contract and fails closed).
    /// </summary>
    public async Task<SettlementRowProbe?> GetSettlementRowAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string employeeId, string entitlementType, int entitlementYear, int sequence, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            """
            SELECT settlement_state, trigger, snapshot::text
            FROM vacation_settlements
            WHERE employee_id = @employeeId
              AND entitlement_type = @entitlementType
              AND entitlement_year = @entitlementYear
              AND sequence = @sequence
            """, conn, tx);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("entitlementType", entitlementType);
        cmd.Parameters.AddWithValue("entitlementYear", entitlementYear);
        cmd.Parameters.AddWithValue("sequence", sequence);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;
        return new SettlementRowProbe(reader.GetString(0), reader.GetString(1), reader.GetString(2));
    }

    /// <summary>The R6 request re-read result: how many request rows exist for the settlement row,
    /// and the state of the (at most one — partial-unique) LIVE (non-voided) request, or null.</summary>
    public readonly record struct RequestProbe(int TotalCount, string? LiveState);

    /// <summary>
    /// Reads the <c>termination_payout_requests</c> rows bound to the EXACT settlement row under
    /// the held lock (SPRINT-71 R6 — the §26 consumer's under-lock request re-read; inline SQL,
    /// see the class doc's cross-domain declaration). At most one non-voided row exists
    /// (<c>idx_termination_payout_requests_nonvoided</c>).
    /// </summary>
    public async Task<RequestProbe> GetRequestProbeAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string employeeId, string entitlementType, int entitlementYear, int settlementSequence, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            """
            SELECT state
            FROM termination_payout_requests
            WHERE employee_id = @employeeId
              AND entitlement_type = @entitlementType
              AND entitlement_year = @entitlementYear
              AND settlement_sequence = @settlementSequence
            """, conn, tx);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("entitlementType", entitlementType);
        cmd.Parameters.AddWithValue("entitlementYear", entitlementYear);
        cmd.Parameters.AddWithValue("settlementSequence", settlementSequence);

        var total = 0;
        string? live = null;
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            total++;
            var state = reader.GetString(0);
            if (!string.Equals(state, "VOIDED_BY_REVERSAL", StringComparison.Ordinal))
                live = state;
        }
        return new RequestProbe(total, live);
    }

    /// <summary>
    /// The consumer-authoritative <c>OPEN → LINE_STAGED</c> promotion (SPRINT-71 R6 — written by
    /// the §26 CONSUMER in its staging tx, the same tx as the line + checkpoint). Conditional on
    /// <c>state = 'OPEN'</c>: an idempotent no-op on a redelivery whose request is already
    /// LINE_STAGED (replay parity), and it can never resurrect a VOIDED_BY_REVERSAL row. Bumps the
    /// ADR-019 version. Returns the number of rows promoted (0 or 1).
    /// </summary>
    public async Task<int> PromoteRequestToLineStagedAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string employeeId, string entitlementType, int entitlementYear, int settlementSequence, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE termination_payout_requests
               SET state = 'LINE_STAGED',
                   version = version + 1,
                   updated_at = NOW()
             WHERE employee_id = @employeeId
               AND entitlement_type = @entitlementType
               AND entitlement_year = @entitlementYear
               AND settlement_sequence = @settlementSequence
               AND state = 'OPEN'
            """, conn, tx);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("entitlementType", entitlementType);
        cmd.Parameters.AddWithValue("entitlementYear", entitlementYear);
        cmd.Parameters.AddWithValue("settlementSequence", settlementSequence);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>One staged ORIGINAL line — a compensation target for the reversal consumer (R9).</summary>
    public sealed record StagedOriginalLine
    {
        public required long LineId { get; init; }
        public required string Bucket { get; init; }
        public required string WageType { get; init; }
        public required decimal Hours { get; init; }
        public required string OkVersion { get; init; }
        public required string AgreementCode { get; init; }
        public required string Position { get; init; }
        public required DateOnly PeriodStart { get; init; }
        public required DateOnly PeriodEnd { get; init; }
    }

    /// <summary>
    /// Reads the Payroll-side staged ORIGINAL lines for the reversed settlement row — by identity +
    /// the settlement-ROW sequence (the originals carry the odd <c>2g−1</c> export sequence, which
    /// EQUALS the row sequence per R1) — under the held lock. SPRINT-71 R9: the
    /// <c>SettlementReversed</c> consumer derives its compensation TARGETS from its OWN staged-line
    /// records, never from the payload and never via a live re-derivation; it compensates exactly
    /// what it itself staged. <c>line_kind = 'ORIGINAL'</c> filters out compensating lines (a
    /// reversal is never itself compensated).
    /// </summary>
    public async Task<IReadOnlyList<StagedOriginalLine>> GetOriginalLinesForSettlementAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string employeeId, string entitlementType, int entitlementYear, int settlementSequence, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            """
            SELECT line_id, bucket, wage_type, hours, ok_version, agreement_code, position,
                   period_start, period_end
            FROM settlement_export_lines
            WHERE employee_id = @employeeId
              AND entitlement_type = @entitlementType
              AND entitlement_year = @entitlementYear
              AND sequence = @settlementSequence
              AND line_kind = 'ORIGINAL'
            ORDER BY bucket
            """, conn, tx);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("entitlementType", entitlementType);
        cmd.Parameters.AddWithValue("entitlementYear", entitlementYear);
        cmd.Parameters.AddWithValue("settlementSequence", settlementSequence);

        var result = new List<StagedOriginalLine>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(new StagedOriginalLine
            {
                LineId = reader.GetInt64(0),
                Bucket = reader.GetString(1),
                WageType = reader.GetString(2),
                Hours = reader.GetDecimal(3),
                OkVersion = reader.GetString(4),
                AgreementCode = reader.GetString(5),
                Position = reader.GetString(6),
                PeriodStart = reader.GetFieldValue<DateOnly>(7),
                PeriodEnd = reader.GetFieldValue<DateOnly>(8),
            });
        }
        return result;
    }

    /// <summary>
    /// The immutable line insert with verify-on-conflict (S69 Step-0b C2-B2; R8-extended; SPRINT-71
    /// Step-5a cycle-1 B2). Inserts the money-free line keyed on the business key (the
    /// <c>sequence</c> axis carries the R1/R2 EXPORT sequence); a REVERSAL line additionally
    /// carries <c>line_kind = 'REVERSAL'</c> + <c>reverses_line_id</c> (the 7100 pairing CHECK
    /// enforces the iff). On a business-key conflict it compares the existing row against the
    /// would-be insert on the FULL immutable identity — <c>source_event_id</c>, <c>line_kind</c>,
    /// <c>reverses_line_id</c> (R8: the FK participates in replay/collision validation), the
    /// mapping (<c>wage_type</c>/<c>ok_version</c>/<c>agreement_code</c>/<c>position</c>), the
    /// period fields and the quantity (<c>hours</c>); the employee/settlement identity + export
    /// sequence + bucket are equal by construction (they ARE the conflict key):
    /// <list type="bullet">
    ///   <item><c>Inserted</c> — the line was newly staged;</item>
    ///   <item><c>BenignRedelivery</c> — the existing line came from the SAME immutable event AND
    ///     matches on EVERY immutable field ⇒ idempotent success (a true redelivery);</item>
    ///   <item><c>Collision</c> — a DIFFERENT source event holds the slot, OR a same-event row
    ///     deviates on ANY immutable field (e.g. a wrong <c>reverses_line_id</c>) ⇒ the caller must
    ///     dead-letter + report with <see cref="LineInsertResult.CollisionDetail"/> enumerating the
    ///     mismatches (fail-LOUD; never silently no-op or PROCESS a semantic mismatch).</item>
    /// </list>
    /// </summary>
    public async Task<LineInsertResult> InsertLineAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, SettlementExportLineInput line, CancellationToken ct)
    {
        await using (var insertCmd = new NpgsqlCommand(
            """
            INSERT INTO settlement_export_lines (
                employee_id, entitlement_type, entitlement_year, sequence, bucket,
                wage_type, hours, amount, ok_version, agreement_code, position,
                period_start, period_end, source_event_id, created_by,
                line_kind, reverses_line_id)
            VALUES (
                @employeeId, @entitlementType, @entitlementYear, @sequence, @bucket,
                @wageType, @hours, 0, @okVersion, @agreementCode, @position,
                @periodStart, @periodEnd, @sourceEventId, @createdBy,
                @lineKind, @reversesLineId)
            ON CONFLICT (employee_id, entitlement_type, entitlement_year, sequence, bucket) DO NOTHING
            RETURNING line_id
            """, conn, tx))
        {
            insertCmd.Parameters.AddWithValue("employeeId", line.EmployeeId);
            insertCmd.Parameters.AddWithValue("entitlementType", line.EntitlementType);
            insertCmd.Parameters.AddWithValue("entitlementYear", line.EntitlementYear);
            insertCmd.Parameters.AddWithValue("sequence", line.Sequence);
            insertCmd.Parameters.AddWithValue("bucket", line.Bucket);
            insertCmd.Parameters.AddWithValue("wageType", line.WageType);
            insertCmd.Parameters.AddWithValue("hours", line.Hours);
            insertCmd.Parameters.AddWithValue("okVersion", line.OkVersion);
            insertCmd.Parameters.AddWithValue("agreementCode", line.AgreementCode);
            insertCmd.Parameters.AddWithValue("position", line.Position);
            insertCmd.Parameters.AddWithValue("periodStart", line.PeriodStart);
            insertCmd.Parameters.AddWithValue("periodEnd", line.PeriodEnd);
            insertCmd.Parameters.AddWithValue("sourceEventId", line.SourceEventId);
            insertCmd.Parameters.AddWithValue("createdBy", line.CreatedBy);
            insertCmd.Parameters.AddWithValue("lineKind", line.LineKind);
            insertCmd.Parameters.AddWithValue("reversesLineId", (object?)line.ReversesLineId ?? DBNull.Value);

            var inserted = await insertCmd.ExecuteScalarAsync(ct);
            if (inserted is not null && inserted is not DBNull)
                return new LineInsertResult(LineInsertOutcome.Inserted, null);
        }

        // Business-key conflict — verify the existing line against the would-be insert on the FULL
        // immutable identity (C2-B2; SPRINT-71 Step-5a cycle-1 B2 / R8). source_event_id equality
        // alone is NOT sufficient: a same-event row with a deviant shape (a wrong reverses_line_id,
        // mapping, period or quantity) is a real integrity violation, never a benign redelivery.
        await using var probeCmd = new NpgsqlCommand(
            """
            SELECT source_event_id, line_kind, reverses_line_id, wage_type, hours,
                   ok_version, agreement_code, position, period_start, period_end, created_by
            FROM settlement_export_lines
            WHERE employee_id = @employeeId AND entitlement_type = @entitlementType
              AND entitlement_year = @entitlementYear AND sequence = @sequence AND bucket = @bucket
            """, conn, tx);
        probeCmd.Parameters.AddWithValue("employeeId", line.EmployeeId);
        probeCmd.Parameters.AddWithValue("entitlementType", line.EntitlementType);
        probeCmd.Parameters.AddWithValue("entitlementYear", line.EntitlementYear);
        probeCmd.Parameters.AddWithValue("sequence", line.Sequence);
        probeCmd.Parameters.AddWithValue("bucket", line.Bucket);

        await using var reader = await probeCmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            // The conflicting row is not visible to this tx (it should be — ReadCommitted under the
            // shared employee lock). Fail loud rather than guess.
            return new LineInsertResult(LineInsertOutcome.Collision,
                "the conflicting settlement_export_lines row could not be re-read in this transaction");
        }

        var mismatches = new List<string>();
        void CompareField<T>(string name, T existing, T incoming)
        {
            if (!EqualityComparer<T>.Default.Equals(existing, incoming))
                mismatches.Add($"{name}: existing={existing?.ToString() ?? "NULL"}, incoming={incoming?.ToString() ?? "NULL"}");
        }

        CompareField("source_event_id", reader.GetGuid(0), line.SourceEventId);
        CompareField("line_kind", reader.GetString(1), line.LineKind);
        CompareField("reverses_line_id", reader.IsDBNull(2) ? (long?)null : reader.GetInt64(2), line.ReversesLineId);
        CompareField("wage_type", reader.GetString(3), line.WageType);
        CompareField("hours", reader.GetDecimal(4), line.Hours);
        CompareField("ok_version", reader.GetString(5), line.OkVersion);
        CompareField("agreement_code", reader.GetString(6), line.AgreementCode);
        CompareField("position", reader.GetString(7), line.Position);
        CompareField("period_start", reader.GetFieldValue<DateOnly>(8), line.PeriodStart);
        CompareField("period_end", reader.GetFieldValue<DateOnly>(9), line.PeriodEnd);
        // created_by is immutable provenance (cycle-2 residual blocker): a same-event row written by
        // a DIFFERENT actor is never a benign redelivery of THIS write.
        CompareField("created_by", reader.GetString(10), line.CreatedBy);

        return mismatches.Count == 0
            ? new LineInsertResult(LineInsertOutcome.BenignRedelivery, null)
            : new LineInsertResult(LineInsertOutcome.Collision, string.Join("; ", mismatches));
    }

    /// <summary>
    /// The terminal-aware inbox PROMOTION write for the success/skip path (S69 Step-0b C4-B1;
    /// SPRINT-71 R7 composite key). Inserts a terminal row at
    /// <c>(eventId, identity.Bucket)</c> if absent; on <c>ON CONFLICT (source_event_id, bucket)</c>
    /// promotes a row to the terminal status ONLY while it is still <c>RETRY_PENDING</c> (an
    /// already-terminal redelivery is an idempotent no-op — a terminal is never rewritten into
    /// another terminal). Then performs the EVENT-LEVEL monotonic completion (R7): a prior
    /// transient-failure diagnostics row at <c>'_EVENT'</c> (RETRY_PENDING) is promoted to the
    /// SAME terminal status, so a completed event never leaves a dangling non-terminal
    /// event-level row (and the inbox-status read stays unambiguous). The completion UPDATE
    /// carries the same <c>WHERE … = 'RETRY_PENDING'</c> guard — it can never clobber a terminal
    /// <c>'_EVENT'</c> row (which the under-lock re-check would have skipped on anyway).
    /// </summary>
    public async Task PromoteToTerminalAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid eventId, SettlementIdentity identity, string terminalStatus, CancellationToken ct)
    {
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO settlement_payroll_inbox (
                source_event_id, employee_id, entitlement_type, entitlement_year, sequence, bucket,
                processing_status, processed_at)
            VALUES (
                @eventId, @employeeId, @entitlementType, @entitlementYear, @sequence, @bucket,
                @status, NOW())
            ON CONFLICT (source_event_id, bucket) DO UPDATE
              SET processing_status = @status, processed_at = NOW(), updated_at = NOW()
              WHERE settlement_payroll_inbox.processing_status = 'RETRY_PENDING'
            """, conn, tx))
        {
            BindIdentity(cmd, eventId, identity, identity.Bucket);
            cmd.Parameters.AddWithValue("status", terminalStatus);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        if (string.Equals(identity.Bucket, EventLevelBucket, StringComparison.Ordinal))
            return; // the primary upsert WAS the event-level row (the reversal no-op checkpoint).

        // Event-level monotonic completion (R7): promote a prior '_EVENT' RETRY_PENDING
        // diagnostics row to the same terminal. Idempotent; never touches a terminal row.
        await using var eventCmd = new NpgsqlCommand(
            $"""
            UPDATE settlement_payroll_inbox
               SET processing_status = @status, processed_at = NOW(), updated_at = NOW()
             WHERE source_event_id = @eventId
               AND bucket = '{EventLevelBucket}'
               AND processing_status = 'RETRY_PENDING'
            """, conn, tx);
        eventCmd.Parameters.AddWithValue("eventId", eventId);
        eventCmd.Parameters.AddWithValue("status", terminalStatus);
        await eventCmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// The terminal-aware post-rollback DIAGNOSTICS write (S69 Step-0b C3-B1; SPRINT-71 R7). Runs
    /// in a SEPARATE committed transaction (the stage tx rolled back, releasing the xact lock), so
    /// it RE-ACQUIRES the employee advisory lock. Keys at the EVENT-LEVEL sentinel
    /// <c>(eventId, '_EVENT')</c> (R7: transient-failure diagnostics also key at <c>'_EVENT'</c> —
    /// a multi-bucket event's failure must never leave a partial real-bucket checkpoint; the
    /// identity columns are populated from <paramref name="identity"/>, its <c>Bucket</c> field is
    /// informational only and NOT used as the key). Uses the SAME
    /// <c>WHERE processing_status = 'RETRY_PENDING'</c> guard on the UPDATE — it can NEVER
    /// overwrite a concurrently-committed terminal status. Inserts the row if absent (first
    /// failure); otherwise bumps <c>attempts</c> + records <c>last_error</c>. Returns <c>true</c>
    /// when diagnostics were written, <c>false</c> on the completed-event no-op below.
    ///
    /// <para>
    /// SPRINT-71 Step-5a cycle-1 B1 — the LATE-DIAGNOSTICS guard. Because this write runs in its
    /// own re-locked tx AFTER the failed stage tx rolled back, a COMPETING worker may have fully
    /// COMPLETED the event in the gap (its real-bucket terminal checkpoint committed while no
    /// <c>'_EVENT'</c> row existed yet). The upsert's <c>(eventId, '_EVENT')</c> conflict guard
    /// alone cannot see that — it would INSERT a fresh non-terminal <c>'_EVENT'</c> RETRY_PENDING
    /// row that the poll (which suppresses the event on the real terminal row) would never promote:
    /// a non-terminal row stranded FOREVER, violating R7's no-dangling-non-terminal-after-completion
    /// monotonicity. So, FIRST, under the lock: probe for ANY terminal row for the event (real
    /// bucket OR <c>'_EVENT'</c>) — present ⇒ NO-OP (the event is complete; diagnostics are moot).
    /// The inverse order (diagnostics first, completion second) is covered by
    /// <see cref="PromoteToTerminalAsync"/>'s event-level monotonic promotion — both directions
    /// hold.
    /// </para>
    ///
    /// <para>
    /// S69 Step-5a WARNING (P2) — the RETRY_PENDING-vs-DEAD_LETTER decision is computed ATOMICALLY
    /// SERVER-SIDE off the POST-increment count inside this single locked upsert (no unlocked
    /// <c>attempts</c> pre-read drives it). A non-self-healing <paramref name="forceDeadLetter"/>
    /// (a line COLLISION) dead-letters immediately; otherwise the row stays RETRY_PENDING until the
    /// incremented <c>attempts</c> reaches <paramref name="budget"/>, then flips to DEAD_LETTER —
    /// and a DEAD_LETTER at <c>'_EVENT'</c> is event-level terminal, suppressing ALL subsequent
    /// processing of the event (R7).
    /// </para>
    /// </summary>
    public async Task<bool> WriteDiagnosticsAsync(
        Guid eventId, SettlementIdentity identity, bool forceDeadLetter, int budget, string error, CancellationToken ct)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct);
        try
        {
            await AcquireEmployeeLockAsync(conn, tx, identity.EmployeeId, ct);

            // B1 (SPRINT-71 Step-5a cycle-1): a competing worker may have COMPLETED the event
            // between this worker's rollback and this re-locked write. ANY terminal row for the
            // event (any bucket — real or '_EVENT') means the event is finalized: writing a fresh
            // non-terminal '_EVENT' diagnostics row now would strand it forever (the poll already
            // suppresses the event on the terminal row). NO-OP — diagnostics are moot.
            var terminal = await GetTerminalStatusAsync(conn, tx, eventId, bucket: null, ct);
            if (terminal is not null)
            {
                await tx.CommitAsync(ct);
                return false;
            }

            // The status is decided server-side off the POST-increment attempts (UPDATE branch) or the
            // first attempt = 1 (INSERT branch). @forceDeadLetter short-circuits both to DEAD_LETTER.
            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO settlement_payroll_inbox (
                    source_event_id, employee_id, entitlement_type, entitlement_year, sequence, bucket,
                    processing_status, attempts, last_error)
                VALUES (
                    @eventId, @employeeId, @entitlementType, @entitlementYear, @sequence, @bucket,
                    CASE WHEN @forceDeadLetter OR 1 >= @budget THEN 'DEAD_LETTER' ELSE 'RETRY_PENDING' END,
                    1, @error)
                ON CONFLICT (source_event_id, bucket) DO UPDATE
                  SET attempts = settlement_payroll_inbox.attempts + 1,
                      processing_status = CASE
                          WHEN @forceDeadLetter OR settlement_payroll_inbox.attempts + 1 >= @budget
                          THEN 'DEAD_LETTER' ELSE 'RETRY_PENDING' END,
                      last_error = @error,
                      updated_at = NOW()
                  WHERE settlement_payroll_inbox.processing_status = 'RETRY_PENDING'
                """, conn, tx);
            BindIdentity(cmd, eventId, identity, EventLevelBucket);
            cmd.Parameters.AddWithValue("forceDeadLetter", forceDeadLetter);
            cmd.Parameters.AddWithValue("budget", budget);
            cmd.Parameters.AddWithValue("error", (object?)error ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct);

            await tx.CommitAsync(ct);
            return true;
        }
        catch
        {
            if (tx.Connection is not null)
                await tx.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>
    /// Dead-letters a POISON event — one whose payload cannot be deserialized
    /// (<see cref="EventSerializer.Deserialize"/> threw), so it has NO recoverable settlement
    /// identity (S69 Step-7a FIX 1). Writes a TERMINAL <c>DEAD_LETTER</c> inbox row at
    /// <c>(eventId, '_EVENT')</c> (the identity columns left NULL — only permitted on this poison
    /// path, per the schema comment; the bucket carries the R7 event-level sentinel since S71 made
    /// <c>bucket</c> NOT NULL), recording <paramref name="error"/> in <c>last_error</c>, so the
    /// poll excludes it and the consumer is no longer stalled re-selecting the un-parseable event
    /// every poll forever.
    ///
    /// <para>
    /// <b>No advisory lock.</b> A parse failure yields no <c>employee_id</c> to hash a
    /// <c>pg_advisory_xact_lock('employee-'||id)</c> on, and the poison row touches no settlement
    /// bucket (no line, no reconcile interplay), so there is nothing to serialize against — this is
    /// a standalone terminal mark in its own small tx, keyed by the PK.
    /// </para>
    ///
    /// <para>
    /// <b>Terminal-aware (no clobber).</b> Same monotonic guard as the other inbox writes: INSERT
    /// the terminal row if absent; on <c>ON CONFLICT (source_event_id, bucket)</c> flip to
    /// <c>DEAD_LETTER</c> ONLY while the row is still <c>RETRY_PENDING</c> (it never overwrites a
    /// concurrently-committed terminal status, and never reopens a terminal). <c>attempts</c> is
    /// incremented on the conflict branch.
    /// </para>
    /// </summary>
    public async Task DeadLetterPoisonEventAsync(Guid eventId, string error, CancellationToken ct)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            $"""
            INSERT INTO settlement_payroll_inbox (
                source_event_id,
                employee_id, entitlement_type, entitlement_year, sequence, bucket,
                processing_status, attempts, last_error)
            VALUES (
                @eventId,
                NULL, NULL, NULL, NULL, '{EventLevelBucket}',
                'DEAD_LETTER', 1, @error)
            ON CONFLICT (source_event_id, bucket) DO UPDATE
              SET processing_status = 'DEAD_LETTER',
                  attempts = settlement_payroll_inbox.attempts + 1,
                  last_error = @error,
                  updated_at = NOW()
              WHERE settlement_payroll_inbox.processing_status = 'RETRY_PENDING'
            """, conn);
        cmd.Parameters.AddWithValue("eventId", eventId);
        cmd.Parameters.AddWithValue("error", (object?)error ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static void BindIdentity(NpgsqlCommand cmd, Guid eventId, SettlementIdentity identity, string bucket)
    {
        cmd.Parameters.AddWithValue("eventId", eventId);
        cmd.Parameters.AddWithValue("employeeId", identity.EmployeeId);
        cmd.Parameters.AddWithValue("entitlementType", identity.EntitlementType);
        cmd.Parameters.AddWithValue("entitlementYear", identity.EntitlementYear);
        cmd.Parameters.AddWithValue("sequence", identity.Sequence);
        cmd.Parameters.AddWithValue("bucket", bucket);
    }
}

/// <summary>
/// The settlement identity + bucket the inbox/line rows are keyed on (ADR-033 D4).
/// <c>Sequence</c> carries the EXPORT sequence (SPRINT-71 R1/R2): the odd settlement-row sequence
/// <c>2g−1</c> for original lines, the even <c>2g</c> for compensating reversal lines.
/// </summary>
public readonly record struct SettlementIdentity(
    string EmployeeId, string EntitlementType, int EntitlementYear, int Sequence, string Bucket);

/// <summary>
/// The immutable money-free line to stage (<c>amount</c> is pinned to 0 in SQL — no rate read).
/// <see cref="LineKind"/>/<see cref="ReversesLineId"/> default to the ORIGINAL shape; the
/// SettlementReversed consumer sets <c>("REVERSAL", originalLineId)</c> per R8 (the 7100 pairing
/// CHECK enforces the iff).
/// </summary>
public sealed record SettlementExportLineInput
{
    public required string EmployeeId { get; init; }
    public required string EntitlementType { get; init; }
    public required int EntitlementYear { get; init; }
    public required int Sequence { get; init; }
    public required string Bucket { get; init; }
    public required string WageType { get; init; }
    public required decimal Hours { get; init; }
    public required string OkVersion { get; init; }
    public required string AgreementCode { get; init; }
    public required string Position { get; init; }
    public required DateOnly PeriodStart { get; init; }
    public required DateOnly PeriodEnd { get; init; }
    public required Guid SourceEventId { get; init; }
    public required string CreatedBy { get; init; }
    public string LineKind { get; init; } = "ORIGINAL";
    public long? ReversesLineId { get; init; }
}

/// <summary>The outcome of the verify-on-conflict line insert (S69 Step-0b C2-B2).</summary>
public enum LineInsertOutcome
{
    /// <summary>A new line was staged.</summary>
    Inserted,

    /// <summary>A line from the SAME source event, IDENTICAL on every immutable field, already
    /// existed — idempotent success (SPRINT-71 Step-5a cycle-1 B2: shape-verified, not just
    /// source-verified).</summary>
    BenignRedelivery,

    /// <summary>A line from a DIFFERENT source event holds this slot, OR a same-event line deviates
    /// on an immutable field — a real collision (dead-letter + report, fail-LOUD).</summary>
    Collision,
}

/// <summary>The verify-on-conflict result (SPRINT-71 Step-5a cycle-1 B2): the
/// <see cref="LineInsertOutcome"/> plus, on <see cref="LineInsertOutcome.Collision"/>, the
/// enumerated immutable-field mismatches for the fail-LOUD dead-letter report.</summary>
public readonly record struct LineInsertResult(LineInsertOutcome Outcome, string? CollisionDetail);
