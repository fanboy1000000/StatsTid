using Npgsql;
using StatsTid.Infrastructure;

namespace StatsTid.Integrations.Payroll.Services;

/// <summary>
/// S69 / TASK-6904 (ADR-033 D4) — the data-access seam for the §24 settlement-export emitter.
/// Encapsulates every read/write the <see cref="SettlementExportEmitter"/> performs against the
/// shared <c>statstid</c> database: the canonical <c>events</c>-table poll (the unconsumed
/// <c>VacationAutoPaidOut</c> selection), the employee advisory lock, the reconciled-skip probe,
/// the immutable <c>settlement_export_lines</c> insert (with verify-on-conflict), and the
/// terminal-aware <c>settlement_payroll_inbox</c> upserts.
///
/// <para>
/// <b>Exactly-once invariant (Step-0b B1/C2-B1/C3-B1/C4-B1).</b> The inbox PK (<c>source_event_id</c>)
/// is the authoritative consumer dedup; the line UNIQUE business key
/// <c>(employee_id, entitlement_type, entitlement_year, sequence, bucket)</c> is the line dedup.
/// Every inbox write moves MONOTONICALLY toward a terminal status: the success/skip path is a
/// conditional PROMOTION (<c>ON CONFLICT (source_event_id) DO UPDATE … WHERE processing_status =
/// 'RETRY_PENDING'</c>) that promotes a row left RETRY_PENDING by a prior transient failure and is an
/// idempotent no-op against an already-terminal row; the post-rollback diagnostics write uses the SAME
/// terminal-aware guard so a concurrently-committed terminal status is NEVER overwritten.
/// </para>
///
/// <para>
/// <b>Cross-domain read (DECLARED).</b> The Payroll context reads the canonical <c>events</c> table
/// directly here (the Backend is the publisher; ADR-018 D6). This is the same cross-table read shape
/// <see cref="StatsTid.Infrastructure.SettlementCloseService"/> uses (it reads <c>users</c> /
/// <c>vacation_settlements</c> from the shared DB). The emitter needs <c>(event_id, data)</c> together,
/// which <see cref="IEventStore"/> does not expose, so the poll SQL is targeted here; the event body
/// is deserialized via the shared <see cref="EventSerializer"/>.
/// </para>
/// </summary>
public sealed class SettlementInboxLineRepository
{
    private readonly DbConnectionFactory _connectionFactory;

    public SettlementInboxLineRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <summary>One raw <c>VacationAutoPaidOut</c> row to consume: its <c>events.event_id</c> + JSONB body.</summary>
    public readonly record struct PendingEvent(Guid EventId, string Data);

    /// <summary>
    /// The authoritative poll selection (Step-0b B1/W8): the canonical <c>events</c>-table
    /// <c>VacationAutoPaidOut</c> rows that have NO TERMINAL inbox row — i.e. no
    /// <c>settlement_payroll_inbox</c> row at all, or one still in the single non-terminal
    /// <c>RETRY_PENDING</c> status. A terminal inbox row (PROCESSED / SKIPPED_RECONCILED / DEAD_LETTER)
    /// excludes the event from re-selection. Ordered by <c>global_position</c> for stable progress; no
    /// cursor table this sprint (events are rare and none exist pre-launch — the inbox is the dedup).
    /// </summary>
    public async Task<IReadOnlyList<PendingEvent>> GetUnconsumedAutoPaidOutEventsAsync(int batchSize, CancellationToken ct)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT e.event_id, e.data::text
            FROM events e
            WHERE e.event_type = 'VacationAutoPaidOut'
              AND NOT EXISTS (
                    SELECT 1 FROM settlement_payroll_inbox i
                    WHERE i.source_event_id = e.event_id
                      AND i.processing_status IN ('PROCESSED', 'SKIPPED_RECONCILED', 'DEAD_LETTER'))
            ORDER BY e.global_position
            LIMIT @batchSize
            """, conn);
        cmd.Parameters.AddWithValue("batchSize", batchSize);

        var result = new List<PendingEvent>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            result.Add(new PendingEvent(reader.GetGuid(0), reader.GetString(1)));
        return result;
    }

    /// <summary>
    /// Opens a connection + transaction for processing ONE event, takes the ADR-032 D4 employee
    /// advisory lock FIRST (held to commit — the SAME key as the Backend close service + the reconcile
    /// retrofit, so emitter-claim and reconcile are mutually exclusive), and returns the open handles
    /// to the caller. The caller commits or rolls back.
    /// </summary>
    public async Task<(NpgsqlConnection Conn, NpgsqlTransaction Tx)> BeginLockedAsync(string employeeId, CancellationToken ct)
    {
        var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        // ReadCommitted: the in-lock reconciled-skip probe must observe a reconcile that committed just
        // before the lock was granted (same reasoning as SettlementCloseService's per-tuple tx).
        var tx = await conn.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct);
        await AcquireEmployeeLockAsync(conn, tx, employeeId, ct);
        return (conn, tx);
    }

    /// <summary>
    /// The ADR-032 D4 employee-scoped advisory lock (<c>pg_advisory_xact_lock(hashtext('employee-'||id))</c>),
    /// xact-scoped (auto-released at COMMIT/ROLLBACK). Identical SQL/key to
    /// <see cref="StatsTid.Infrastructure.VacationSettlementService"/> and the reconcile retrofit — the
    /// VALUE is what serializes the three writers.
    /// </summary>
    public static async Task AcquireEmployeeLockAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string employeeId, CancellationToken ct)
    {
        await using var lockCmd = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtext('employee-' || @employeeId))", conn, tx);
        lockCmd.Parameters.AddWithValue("employeeId", employeeId);
        await lockCmd.ExecuteScalarAsync(ct);
    }

    /// <summary>
    /// Reads the inbox <c>processing_status</c> for <paramref name="eventId"/> UNDER THE HELD LOCK
    /// (Step-5a BLOCKER — the select→lock TOCTOU re-check). The poll selection
    /// (<see cref="GetUnconsumedAutoPaidOutEventsAsync"/>) filters terminal rows, but it runs OUTSIDE
    /// the employee advisory lock, so a concurrent worker can finalize this event (PROCESSED /
    /// SKIPPED_RECONCILED / DEAD_LETTER) between selection and this worker acquiring the lock. The
    /// claim path re-reads the status here and skips when it is already terminal — preventing a freshly
    /// staged line from being paired with a terminal (e.g. DEAD_LETTER) checkpoint that the
    /// terminal-aware PROMOTE would then silently leave un-promoted (0 rows updated). Returns the status
    /// string, or <c>null</c> when no inbox row exists yet (absent ⇒ proceed to stage).
    /// </summary>
    public async Task<string?> GetInboxStatusAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, Guid eventId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT processing_status FROM settlement_payroll_inbox WHERE source_event_id = @eventId",
            conn, tx);
        cmd.Parameters.AddWithValue("eventId", eventId);
        var value = await cmd.ExecuteScalarAsync(ct);
        return value is string s ? s : null;
    }

    /// <summary>
    /// Reads the EVENT's settlement row's <c>payout_reconciled_at</c> under the held lock (the
    /// reconciled-skip gate, Step-0b B2). Returns <c>true</c> when an operator has already reconciled
    /// the §24 bucket (the emitter must then stage NO line and write a SKIPPED_RECONCILED checkpoint).
    /// A missing row, or a row with a NULL <c>payout_reconciled_at</c>, returns <c>false</c> — there is
    /// nothing to skip-for, and the claim path's downstream validation still governs.
    ///
    /// <para>
    /// Step-5a WARNING (P1) — the probe binds the EVENT's full settlement identity
    /// <c>(employee_id, entitlement_type, entitlement_year, sequence)</c>, NOT a
    /// <c>settlement_state = 'SETTLED'</c>-only selector. After a reversal/re-close a DIFFERENT
    /// sequence becomes the active SETTLED row, and a state-only probe could read THAT row's reconcile
    /// marker instead of the one for the sequence this event was emitted from. Binding the event's
    /// sequence keeps the skip decision tied to the exact settlement the line would pair with. (For
    /// slice 1b — no reversals — the active SETTLED row IS the event's sequence, so behavior is
    /// unchanged today; this makes it forward-correct.)
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

    /// <summary>
    /// The immutable §24 line insert with verify-on-conflict (Step-0b C2-B2). Inserts the money-free
    /// line keyed on the business key; on a business-key conflict (a line already exists for this
    /// bucket) it inspects the existing row's <c>source_event_id</c>:
    /// <list type="bullet">
    ///   <item><c>Inserted</c> — the line was newly staged;</item>
    ///   <item><c>BenignRedelivery</c> — the existing line came from the SAME immutable event (same
    ///     <c>source_event_id</c>) ⇒ idempotent success (the event is immutable, so same payload);</item>
    ///   <item><c>Collision</c> — the existing line came from a DIFFERENT source event ⇒ the caller must
    ///     dead-letter + report (never silently no-op a semantic mismatch).</item>
    /// </list>
    /// </summary>
    public async Task<LineInsertOutcome> InsertLineAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, SettlementExportLineInput line, CancellationToken ct)
    {
        await using (var insertCmd = new NpgsqlCommand(
            """
            INSERT INTO settlement_export_lines (
                employee_id, entitlement_type, entitlement_year, sequence, bucket,
                wage_type, hours, amount, ok_version, agreement_code, position,
                period_start, period_end, source_event_id, created_by)
            VALUES (
                @employeeId, @entitlementType, @entitlementYear, @sequence, @bucket,
                @wageType, @hours, 0, @okVersion, @agreementCode, @position,
                @periodStart, @periodEnd, @sourceEventId, @createdBy)
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

            var inserted = await insertCmd.ExecuteScalarAsync(ct);
            if (inserted is not null && inserted is not DBNull)
                return LineInsertOutcome.Inserted;
        }

        // Business-key conflict — verify the existing line's origin (C2-B2).
        await using var probeCmd = new NpgsqlCommand(
            """
            SELECT source_event_id FROM settlement_export_lines
            WHERE employee_id = @employeeId AND entitlement_type = @entitlementType
              AND entitlement_year = @entitlementYear AND sequence = @sequence AND bucket = @bucket
            """, conn, tx);
        probeCmd.Parameters.AddWithValue("employeeId", line.EmployeeId);
        probeCmd.Parameters.AddWithValue("entitlementType", line.EntitlementType);
        probeCmd.Parameters.AddWithValue("entitlementYear", line.EntitlementYear);
        probeCmd.Parameters.AddWithValue("sequence", line.Sequence);
        probeCmd.Parameters.AddWithValue("bucket", line.Bucket);
        var existingSource = await probeCmd.ExecuteScalarAsync(ct);

        if (existingSource is Guid existing && existing == line.SourceEventId)
            return LineInsertOutcome.BenignRedelivery;

        return LineInsertOutcome.Collision;
    }

    /// <summary>
    /// The terminal-aware inbox PROMOTION write for the success/skip path (Step-0b C4-B1). Inserts a
    /// terminal row if absent; on <c>ON CONFLICT (source_event_id)</c> promotes a row to the terminal
    /// status ONLY while it is still <c>RETRY_PENDING</c> (so a retry that succeeds after a prior
    /// transient failure is promoted; an already-terminal redelivery is an idempotent no-op — a terminal
    /// is never rewritten into another terminal).
    /// </summary>
    public async Task PromoteToTerminalAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid eventId, SettlementIdentity identity, string terminalStatus, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO settlement_payroll_inbox (
                source_event_id, employee_id, entitlement_type, entitlement_year, sequence, bucket,
                processing_status, processed_at)
            VALUES (
                @eventId, @employeeId, @entitlementType, @entitlementYear, @sequence, @bucket,
                @status, NOW())
            ON CONFLICT (source_event_id) DO UPDATE
              SET processing_status = @status, processed_at = NOW(), updated_at = NOW()
              WHERE settlement_payroll_inbox.processing_status = 'RETRY_PENDING'
            """, conn, tx);
        BindIdentity(cmd, eventId, identity);
        cmd.Parameters.AddWithValue("status", terminalStatus);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// The terminal-aware post-rollback DIAGNOSTICS write (Step-0b C3-B1). Runs in a SEPARATE committed
    /// transaction (the stage tx rolled back, releasing the xact lock), so it RE-ACQUIRES the employee
    /// advisory lock and uses the SAME <c>WHERE processing_status = 'RETRY_PENDING'</c> guard on the
    /// UPDATE — it can NEVER overwrite a concurrently-committed terminal status. Inserts the row if
    /// absent (first failure); otherwise bumps <c>attempts</c> + records <c>last_error</c>.
    ///
    /// <para>
    /// Step-5a WARNING (P2) — the RETRY_PENDING-vs-DEAD_LETTER decision is computed ATOMICALLY
    /// SERVER-SIDE off the POST-increment count inside this single locked upsert (no unlocked
    /// <c>attempts</c> pre-read drives it). A non-self-healing <paramref name="forceDeadLetter"/>
    /// (a line COLLISION) dead-letters immediately; otherwise the row stays RETRY_PENDING until the
    /// incremented <c>attempts</c> reaches <paramref name="budget"/>, then flips to DEAD_LETTER. The
    /// INSERT-if-absent branch (first failure, attempts ⇒ 1) dead-letters up-front only when forced or
    /// when the budget is already 1. One mechanism for BOTH the retry increment and the collision
    /// dead-letter.
    /// </para>
    /// </summary>
    public async Task WriteDiagnosticsAsync(
        Guid eventId, SettlementIdentity identity, bool forceDeadLetter, int budget, string error, CancellationToken ct)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct);
        try
        {
            await AcquireEmployeeLockAsync(conn, tx, identity.EmployeeId, ct);

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
                ON CONFLICT (source_event_id) DO UPDATE
                  SET attempts = settlement_payroll_inbox.attempts + 1,
                      processing_status = CASE
                          WHEN @forceDeadLetter OR settlement_payroll_inbox.attempts + 1 >= @budget
                          THEN 'DEAD_LETTER' ELSE 'RETRY_PENDING' END,
                      last_error = @error,
                      updated_at = NOW()
                  WHERE settlement_payroll_inbox.processing_status = 'RETRY_PENDING'
                """, conn, tx);
            BindIdentity(cmd, eventId, identity);
            cmd.Parameters.AddWithValue("forceDeadLetter", forceDeadLetter);
            cmd.Parameters.AddWithValue("budget", budget);
            cmd.Parameters.AddWithValue("error", (object?)error ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct);

            await tx.CommitAsync(ct);
        }
        catch
        {
            if (tx.Connection is not null)
                await tx.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>
    /// Dead-letters a POISON event — one whose <c>VacationAutoPaidOut</c> payload cannot be deserialized
    /// (<see cref="EventSerializer.Deserialize"/> threw), so it has NO recoverable settlement identity
    /// (S69 Step-7a FIX 1). Writes a TERMINAL <c>DEAD_LETTER</c> inbox row keyed SOLELY by
    /// <paramref name="eventId"/> (the identity columns left NULL — only permitted on this poison path,
    /// per the schema comment), recording <paramref name="error"/> in <c>last_error</c>, so the poll
    /// (<see cref="GetUnconsumedAutoPaidOutEventsAsync"/>) excludes it and the consumer is no longer
    /// stalled re-selecting the un-parseable event every poll forever.
    ///
    /// <para>
    /// <b>No advisory lock.</b> A parse failure yields no <c>employee_id</c> to hash a
    /// <c>pg_advisory_xact_lock('employee-'||id)</c> on, and the poison row touches no settlement bucket
    /// (no line, no reconcile interplay), so there is nothing to serialize against — this is a standalone
    /// terminal mark in its own small tx, keyed by the PK.
    /// </para>
    ///
    /// <para>
    /// <b>Terminal-aware (no clobber).</b> Same monotonic guard as the other inbox writes: INSERT the
    /// terminal row if absent; on <c>ON CONFLICT (source_event_id)</c> flip to <c>DEAD_LETTER</c> ONLY
    /// while the row is still <c>RETRY_PENDING</c> (it never overwrites a concurrently-committed terminal
    /// status, and never reopens a terminal). <c>attempts</c> is incremented on the conflict branch.
    /// </para>
    /// </summary>
    public async Task DeadLetterPoisonEventAsync(Guid eventId, string error, CancellationToken ct)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO settlement_payroll_inbox (
                source_event_id,
                employee_id, entitlement_type, entitlement_year, sequence, bucket,
                processing_status, attempts, last_error)
            VALUES (
                @eventId,
                NULL, NULL, NULL, NULL, NULL,
                'DEAD_LETTER', 1, @error)
            ON CONFLICT (source_event_id) DO UPDATE
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

    private static void BindIdentity(NpgsqlCommand cmd, Guid eventId, SettlementIdentity identity)
    {
        cmd.Parameters.AddWithValue("eventId", eventId);
        cmd.Parameters.AddWithValue("employeeId", identity.EmployeeId);
        cmd.Parameters.AddWithValue("entitlementType", identity.EntitlementType);
        cmd.Parameters.AddWithValue("entitlementYear", identity.EntitlementYear);
        cmd.Parameters.AddWithValue("sequence", identity.Sequence);
        cmd.Parameters.AddWithValue("bucket", identity.Bucket);
    }
}

/// <summary>The settlement identity + bucket the inbox/line rows are keyed on (ADR-033 D4).</summary>
public readonly record struct SettlementIdentity(
    string EmployeeId, string EntitlementType, int EntitlementYear, int Sequence, string Bucket);

/// <summary>The immutable money-free §24 line to stage (<c>amount</c> is pinned to 0 in SQL — no rate read).</summary>
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
}

/// <summary>The outcome of the verify-on-conflict line insert (Step-0b C2-B2).</summary>
public enum LineInsertOutcome
{
    /// <summary>A new line was staged.</summary>
    Inserted,

    /// <summary>An identical line from the SAME source event already existed — idempotent success.</summary>
    BenignRedelivery,

    /// <summary>A line from a DIFFERENT source event holds this bucket — a real collision (dead-letter + report).</summary>
    Collision,
}
