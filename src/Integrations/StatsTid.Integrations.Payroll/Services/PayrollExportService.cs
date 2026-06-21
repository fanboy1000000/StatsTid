using Npgsql;
using NpgsqlTypes;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Outbox;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Models;
using System.Text.Json;

namespace StatsTid.Integrations.Payroll.Services;

public sealed class PayrollExportService
{
    // ADR-033 slice 1b — fail-closed settlement-line delivery guard.
    // A settlement line is identified by its DATA (not a caller-supplied flag, which would be
    // bypassable): a placeholder sentinel wage_type prefix, or the §24 settlement time_type.
    // The placeholder lønart (SLS_TBD_*) is unverified against SLS and must NEVER leave the
    // system, even once non-sentinel settlement delivery is later enabled via config.
    private const string SettlementSentinelWageTypePrefix = "SLS_TBD_";
    private const string SettlementPayoutTimeType = "VACATION_SETTLEMENT_PAYOUT";

    // Fail-closed config gate for NON-sentinel settlement lines. Absent/not-exactly-"true" ⇒ disabled.
    private const string SettlementLineDeliveryEnabledKey = "Settlement:LineDeliveryEnabled";

    // The canonical serialization + content hash for the manifest columns lives in
    // PayrollExportManifest so the corrections path (RetroactiveCorrectionService) — which rewrites
    // current_effective_lines (B3) — hashes/serializes IDENTICALLY to this first-export write.

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly DbConnectionFactory _connectionFactory;
    private readonly IOutboxEnqueue _outbox;
    private readonly AuditProjectionRepository _auditRepo;
    private readonly IAuditProjectionMapper<PayrollExportGenerated> _auditMapper;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PayrollExportService> _logger;
    private readonly string _mockPayrollUrl;

    public PayrollExportService(
        IHttpClientFactory httpClientFactory,
        DbConnectionFactory connectionFactory,
        IOutboxEnqueue outbox,
        AuditProjectionRepository auditRepo,
        IAuditProjectionMapper<PayrollExportGenerated> auditMapper,
        IConfiguration configuration,
        ILogger<PayrollExportService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _connectionFactory = connectionFactory;
        _outbox = outbox;
        _auditRepo = auditRepo;
        _auditMapper = auditMapper;
        _configuration = configuration;
        _logger = logger;
        _mockPayrollUrl = configuration["ServiceUrls:MockPayroll"] ?? "http://mock-payroll:8080";
    }

    /// <summary>
    /// S90 / TASK-9002 (ADR-034) — the ATOMIC payroll-export refactor.
    ///
    /// <para>
    /// Groups the incoming <paramref name="lines"/> by <c>(employee_id, year, month)</c>
    /// (B4 — the raw <c>/export-period</c> flattens multiple periods/months into one call)
    /// and, in ONE <c>(conn, tx)</c>, per group:
    /// <list type="number">
    /// <item><description>runs the fail-closed <c>GuardSettlementLineDelivery</c> FIRST
    /// (settlement lines must throw before any write);</description></item>
    /// <item><description>(B2) when the group maps to an approval period
    /// (<see cref="PayrollExportContext.PeriodId"/> set — the <c>/calculate-and-export</c>
    /// path), re-asserts <c>status='APPROVED'</c> under <c>SELECT … FOR UPDATE</c> — a period
    /// not APPROVED at export time aborts the WHOLE tx;</description></item>
    /// <item><description>computes a deterministic <c>content_hash</c> over the group's lines;</description></item>
    /// <item><description>INSERTs <c>payroll_export_records</c> — idempotent on
    /// <c>UNIQUE(employee_id, year, month)</c>: same hash → NO-OP (returns the existing
    /// export id, no duplicate, no re-emit); DIFFERENT hash → aborts with an
    /// "already exported, use a correction" error;</description></item>
    /// <item><description>emits <see cref="PayrollExportGenerated"/> to <c>outbox_events</c>
    /// on <c>employee-{id}</c> + writes the ADR-026 audit row in the same tx.</description></item>
    /// </list>
    /// COMMIT sets the lock (OQ-1 "export committed"), independent of delivery.
    /// </para>
    ///
    /// <para>
    /// AFTER commit (best-effort, unchanged delivery): the existing <c>outbox_messages</c>
    /// 'pending' envelope + the synchronous HTTP POST to mock-payroll + the status update.
    /// There is NO background payroll dispatcher (B1), so this stays the delivery path; the
    /// lock does NOT depend on it.
    /// </para>
    /// </summary>
    public async Task<ExportResult> ExportAsync(
        IReadOnlyList<PayrollExportLine> lines,
        PayrollExportContext context,
        CancellationToken ct = default)
    {
        // ADR-033 slice 1b: fail-closed outbound delivery guard. This runs BEFORE any write
        // so a misconfigured settlement delivery attempt fails loudly and NOTHING is written.
        // Non-settlement lines (NORMAL_HOURS, OVERTIME, FLEX_PAYOUT, …) are unaffected.
        GuardSettlementLineDelivery(lines);

        // B4 — group by (employee, year, month). year/month come from each line's PeriodStart;
        // a line whose PeriodStart/PeriodEnd do NOT normalise to a single calendar month is a
        // span across months and is rejected (a single record can't represent a multi-month line).
        foreach (var line in lines)
        {
            if (line.PeriodStart.Year != line.PeriodEnd.Year || line.PeriodStart.Month != line.PeriodEnd.Month)
            {
                throw new PayrollExportConflictException(
                    $"Export line for employee '{line.EmployeeId}' spans more than one calendar month " +
                    $"({line.PeriodStart:yyyy-MM-dd}..{line.PeriodEnd:yyyy-MM-dd}); a payroll-export record is " +
                    $"per (employee, year, month) and cannot represent a month-spanning line.");
            }
        }

        var groups = lines
            .GroupBy(l => new ExportGroupKey(l.EmployeeId, l.PeriodStart.Year, l.PeriodStart.Month))
            .ToList();

        var exportedAt = DateTimeOffset.UtcNow;

        // ── ATOMIC: one (conn, tx) covering ALL groups (all-or-nothing) ──
        var perGroup = new List<GroupExportOutcome>(groups.Count);
        // BLOCKER 1 — accumulate the lines of ONLY the groups that were actually newly exported.
        // A mixed call (one (employee, year, month) is a same-hash idempotent no-op, another is new)
        // must NOT re-deliver the already-exported month: the post-commit envelope + HTTP POST are
        // built from these new-group lines, NOT the full incoming `lines`.
        var newlyExportedLines = new List<PayrollExportLine>();
        await using (var conn = _connectionFactory.Create())
        {
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);

            foreach (var group in groups)
            {
                var key = group.Key;
                var groupLines = PayrollExportManifest.OrderLines(group);

                // B2 — re-assert APPROVED under FOR UPDATE when this export maps to an approval
                // period (calculate-and-export threads the periodId). The raw /export +
                // /export-period bypass approval and carry no periodId → skip the re-check.
                if (context.PeriodId is { } periodId)
                {
                    await AssertPeriodApprovedForUpdateAsync(conn, tx, periodId, key, ct);
                }

                var contentHash = PayrollExportManifest.ComputeContentHash(groupLines);
                var manifestJson = PayrollExportManifest.Serialize(groupLines);

                // INSERT with idempotency. ON CONFLICT (employee_id, year, month) DO NOTHING +
                // RETURNING distinguishes the first write (a row comes back) from a conflict
                // (no row → read the existing row and compare hashes).
                var existingExportId = await TryInsertRecordAsync(
                    conn, tx, key, context, exportedAt, contentHash, manifestJson, ct);

                if (existingExportId is { } existing)
                {
                    // First write → emit the event + audit row in the SAME tx.
                    var exportId = existing;
                    var @event = new PayrollExportGenerated
                    {
                        EmployeeId = key.EmployeeId,
                        Year = key.Year,
                        Month = key.Month,
                        ExportId = exportId,
                        ContentHash = contentHash,
                        ExportedAt = exportedAt,
                        PeriodId = context.PeriodId,
                        ActorId = context.ActorId,
                        ActorRole = context.ActorRole,
                        CorrelationId = context.CorrelationId,
                    };
                    var streamId = $"employee-{key.EmployeeId}";
                    var outboxId = await _outbox.EnqueueAndReturnIdAsync(conn, tx, streamId, @event, ct);

                    var auditCtx = new AuditProjectionContext(
                        ActorId: context.ActorId,
                        ActorPrimaryOrgId: context.ResolvedTargetOrgId,
                        CorrelationId: context.CorrelationId,
                        OccurredAt: new DateTimeOffset(@event.OccurredAt),
                        ResolvedTargetOrgId: context.ResolvedTargetOrgId);
                    var auditRow = _auditMapper.Map(@event, auditCtx);
                    await _auditRepo.InsertAsync(conn, tx, @event.EventId, outboxId, @event.EventType, auditRow, auditCtx, ct);

                    // BLOCKER 1 — this group is newly exported → its lines feed the post-commit
                    // delivery payload. A no-op group below does NOT (it was already delivered).
                    newlyExportedLines.AddRange(groupLines);
                    perGroup.Add(new GroupExportOutcome(exportId, IsNew: true));
                }
                else
                {
                    // Conflict: a record already exists for this (employee, year, month). Read it
                    // and compare the content hash for idempotency.
                    var (existingId, existingHash) = await ReadExistingRecordAsync(conn, tx, key, ct);
                    if (!string.Equals(existingHash, contentHash, StringComparison.Ordinal))
                    {
                        throw new PayrollExportConflictException(
                            $"Employee '{key.EmployeeId}' already has a payroll export for {key.Year}-{key.Month:D2} " +
                            $"with different content; use a correction (/api/payroll/recalculate) to amend an exported month.");
                    }
                    // Same hash → idempotent no-op: return the existing export id, emit NOTHING.
                    perGroup.Add(new GroupExportOutcome(existingId, IsNew: false));
                }
            }

            await tx.CommitAsync(ct);
        }

        // The lock is now committed (OQ-1). Pick a representative export id for the legacy
        // single-id ExportResult (callers that need per-group ids read the records table).
        var primaryExportId = perGroup.Count == 1
            ? perGroup[0].ExportId.ToString()
            : Guid.NewGuid().ToString();
        var anyNew = perGroup.Exists(g => g.IsNew);

        // ── POST-COMMIT delivery (best-effort, UNCHANGED): the lock does NOT depend on this. ──
        // An idempotent no-op (no new record) must not re-deliver to payroll either.
        if (!anyNew)
        {
            return new ExportResult { ExportId = primaryExportId, Success = true, MessageId = Guid.Empty };
        }

        // BLOCKER 1 — deliver ONLY the newly-exported groups' lines, never the full incoming `lines`.
        // In a mixed call (month A same-hash no-op + month B new), `newlyExportedLines` holds month B
        // only, so the already-exported month A is NOT re-POSTed to payroll / re-enveloped.
        var payload = new { exportId = primaryExportId, lines = (IReadOnlyList<PayrollExportLine>)newlyExportedLines, exportedAt = exportedAt.UtcDateTime };
        var messageId = await WriteToOutboxAsync("payroll", payload, ct);

        var client = _httpClientFactory.CreateClient();
        try
        {
            var response = await client.PostAsJsonAsync($"{_mockPayrollUrl}/api/payroll/receive", payload, ct);
            var success = response.IsSuccessStatusCode;

            await UpdateOutboxStatusAsync(messageId, success ? "delivered" : "failed", ct);
            _logger.LogInformation("Payroll export {ExportId}: {Status}", primaryExportId, success ? "delivered" : "failed");

            // The export RECORD (the lock) is committed regardless of delivery success: a failed
            // downstream POST leaves a 'failed' outbox_messages envelope for ops, NOT an unlocked
            // month. Success here reflects the (best-effort) delivery, not the lock.
            return new ExportResult { ExportId = primaryExportId, Success = success, MessageId = messageId };
        }
        catch (Exception ex)
        {
            await UpdateOutboxStatusAsync(messageId, "failed", ct, ex.Message);
            _logger.LogError(ex, "Payroll export {ExportId} delivery failed (record committed)", primaryExportId);
            return new ExportResult { ExportId = primaryExportId, Success = false, MessageId = messageId };
        }
    }

    // -------------------------------------------------------------------
    //  In-tx helpers
    // -------------------------------------------------------------------

    /// <summary>
    /// B2 — re-locks the approval period row (<c>FOR UPDATE</c>) and re-asserts
    /// <c>status='APPROVED'</c>. The held row lock blocks a concurrent reopen's status flip from
    /// committing until this export tx commits, closing the export↔reopen TOCTOU window. A period
    /// that is missing or not APPROVED at this point aborts the whole export tx.
    /// </summary>
    private static async Task AssertPeriodApprovedForUpdateAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, Guid periodId, ExportGroupKey key, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT status FROM approval_periods WHERE period_id = @periodId FOR UPDATE", conn, tx);
        cmd.Parameters.AddWithValue("periodId", periodId);
        var status = (string?)await cmd.ExecuteScalarAsync(ct);

        if (status is null)
        {
            throw new PayrollExportConflictException(
                $"Approval period '{periodId}' (employee '{key.EmployeeId}', {key.Year}-{key.Month:D2}) not found at export time.");
        }
        if (!string.Equals(status, "APPROVED", StringComparison.Ordinal))
        {
            throw new PayrollExportConflictException(
                $"Approval period '{periodId}' is '{status}', not APPROVED, at export time; payroll export aborted.");
        }
    }

    /// <summary>
    /// Inserts the lock/manifest record with <c>ON CONFLICT (employee_id, year, month) DO NOTHING
    /// RETURNING export_id</c>. Returns the new export id on a first write, or <c>null</c> when the
    /// UNIQUE key already exists (the caller then reads + hash-compares for idempotency).
    /// </summary>
    private static async Task<Guid?> TryInsertRecordAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, ExportGroupKey key, PayrollExportContext context,
        DateTimeOffset exportedAt, string contentHash, string manifestJson, CancellationToken ct)
    {
        var exportId = Guid.NewGuid();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO payroll_export_records (
                export_id, period_id, employee_id, year, month, exported_at,
                original_lines, current_effective_lines, content_hash, source
            ) VALUES (
                @exportId, @periodId, @employeeId, @year, @month, @exportedAt,
                @originalLines::jsonb, @currentLines::jsonb, @contentHash, @source
            )
            ON CONFLICT (employee_id, year, month) DO NOTHING
            RETURNING export_id
            """, conn, tx);
        cmd.Parameters.AddWithValue("exportId", exportId);
        cmd.Parameters.AddWithValue("periodId", (object?)context.PeriodId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("employeeId", key.EmployeeId);
        cmd.Parameters.AddWithValue("year", key.Year);
        cmd.Parameters.AddWithValue("month", key.Month);
        cmd.Parameters.AddWithValue("exportedAt", exportedAt);
        cmd.Parameters.Add(new NpgsqlParameter("originalLines", NpgsqlDbType.Jsonb) { Value = manifestJson });
        cmd.Parameters.Add(new NpgsqlParameter("currentLines", NpgsqlDbType.Jsonb) { Value = manifestJson });
        cmd.Parameters.AddWithValue("contentHash", contentHash);
        cmd.Parameters.AddWithValue("source", context.Source);

        var result = await cmd.ExecuteScalarAsync(ct);
        return result is null or DBNull ? null : (Guid)result;
    }

    private static async Task<(Guid ExportId, string ContentHash)> ReadExistingRecordAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, ExportGroupKey key, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            """
            SELECT export_id, content_hash
            FROM payroll_export_records
            WHERE employee_id = @employeeId AND year = @year AND month = @month
            """, conn, tx);
        cmd.Parameters.AddWithValue("employeeId", key.EmployeeId);
        cmd.Parameters.AddWithValue("year", key.Year);
        cmd.Parameters.AddWithValue("month", key.Month);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            // Should not happen (we got here on a UNIQUE conflict), but fail loud if it does.
            throw new PayrollExportConflictException(
                $"Payroll export record for employee '{key.EmployeeId}' {key.Year}-{key.Month:D2} vanished mid-transaction.");
        }
        return (reader.GetGuid(0), reader.GetString(1));
    }

    private readonly record struct ExportGroupKey(string EmployeeId, int Year, int Month);

    private readonly record struct GroupExportOutcome(Guid ExportId, bool IsNew);

    // ADR-033 slice 1b — one of three independent locks (the others: the D13 go-live gate keeps
    // the emitter dormant pre-launch; no delivery path is wired to settlement lines this sprint).
    // This is the OUTBOUND guard: settlement export lines must not enter outbox_messages this
    // sprint, and a placeholder sentinel lønart must NEVER be deliverable even once delivery is
    // later enabled. Refuse LOUDLY (throw) rather than silently drop — a silent drop would hide a
    // real misconfigured delivery attempt.
    private void GuardSettlementLineDelivery(IReadOnlyList<PayrollExportLine> lines)
    {
        foreach (var line in lines)
        {
            // Discriminator strength (Step-7a FIX 4 — future-hardening). The `SLS_TBD_` sentinel-wage_type
            // prefix is the NON-bypassable guard for slice 1b: the emitter ALWAYS stamps the sentinel
            // lønart on a §24 line, so an `isSentinel` match below cannot be omitted by a caller. The
            // SourceTimeType == VACATION_SETTLEMENT_PAYOUT check is only a SECONDARY discriminator that IS
            // caller-omissible (a caller could construct a settlement line without setting SourceTimeType).
            // ⇒ When a FUTURE slice wires real delivery and replaces the sentinel with a real §24 SLS code,
            // the sentinel match disappears and this guard would rest on the omissible SourceTimeType
            // alone — at that point the discriminator MUST be strengthened to a non-omissible typed
            // line-kind (or settlement lines must move to a dedicated settlement delivery path). No
            // behavior change this sprint (the sentinel is still the load-bearing, unconditional refusal).
            var isSentinel = line.WageType is not null
                && line.WageType.StartsWith(SettlementSentinelWageTypePrefix, StringComparison.Ordinal);
            var isSettlementLine = isSentinel
                || string.Equals(line.SourceTimeType, SettlementPayoutTimeType, StringComparison.Ordinal);

            if (!isSettlementLine)
                continue;

            // The placeholder sentinel lønart is unverified against SLS — refuse unconditionally,
            // regardless of the delivery flag.
            if (isSentinel)
            {
                throw new InvalidOperationException(
                    $"Settlement export line delivery is disabled (ADR-033 slice 1b): placeholder sentinel " +
                    $"lønart wage_type='{line.WageType}' can never enter the outbox.");
            }

            // Non-sentinel settlement line: gated by config, fail-closed (absent/not-"true" ⇒ disabled).
            if (!SettlementLineDeliveryEnabled())
            {
                throw new InvalidOperationException(
                    $"Settlement export line delivery is disabled (ADR-033 slice 1b); line " +
                    $"wage_type='{line.WageType}' cannot enter the outbox. Set '{SettlementLineDeliveryEnabledKey}'=true to enable.");
            }
        }
    }

    private bool SettlementLineDeliveryEnabled() =>
        string.Equals(_configuration[SettlementLineDeliveryEnabledKey], "true", StringComparison.Ordinal);

    private async Task<Guid> WriteToOutboxAsync(string destination, object payload, CancellationToken ct)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);

        var messageId = Guid.NewGuid();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO outbox_messages (message_id, destination, payload, status)
            VALUES (@messageId, @destination, @payload::jsonb, 'pending')
            """, conn);
        cmd.Parameters.AddWithValue("messageId", messageId);
        cmd.Parameters.AddWithValue("destination", destination);
        cmd.Parameters.AddWithValue("payload", JsonSerializer.Serialize(payload));

        await cmd.ExecuteNonQueryAsync(ct);
        return messageId;
    }

    private async Task UpdateOutboxStatusAsync(Guid messageId, string status, CancellationToken ct, string? error = null)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);

        var sql = status == "delivered"
            ? "UPDATE outbox_messages SET status = @status, delivered_at = NOW(), attempt_count = attempt_count + 1 WHERE message_id = @messageId"
            : "UPDATE outbox_messages SET status = @status, last_attempt_at = NOW(), attempt_count = attempt_count + 1, error_message = @error WHERE message_id = @messageId";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("messageId", messageId);
        cmd.Parameters.AddWithValue("status", status);
        if (status != "delivered")
            cmd.Parameters.AddWithValue("error", (object?)error ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);
    }
}

/// <summary>
/// S90 / TASK-9002 — the actor/source/period context threaded into the atomic
/// <see cref="PayrollExportService.ExportAsync(IReadOnlyList{PayrollExportLine}, PayrollExportContext, CancellationToken)"/>.
///
/// <para>
/// <see cref="PeriodId"/> is set ONLY by <c>/calculate-and-export</c> (which has an approval
/// period); when set, the export re-asserts <c>status='APPROVED'</c> under <c>FOR UPDATE</c>
/// (B2). The raw <c>/export</c> + <c>/export-period</c> bypass approval and pass a null
/// <see cref="PeriodId"/> — they still get the lock record + idempotency, but skip the
/// APPROVED re-check (by design — they intentionally bypass approval).
/// </para>
/// </summary>
public sealed class PayrollExportContext
{
    /// <summary>The approval period (calculate-and-export only); null for the raw endpoints.</summary>
    public Guid? PeriodId { get; init; }

    /// <summary>The originating endpoint: CALCULATE_AND_EXPORT / EXPORT / EXPORT_PERIOD.</summary>
    public required string Source { get; init; }

    /// <summary>The acting user (JWT sub) for the audit row + the event actor field.</summary>
    public string? ActorId { get; init; }

    /// <summary>The acting role for the event actor field.</summary>
    public string? ActorRole { get; init; }

    /// <summary>The target employee's resolved org (employee → primary_org_id) for the audit row.</summary>
    public string? ResolvedTargetOrgId { get; init; }

    public Guid? CorrelationId { get; init; }
}

/// <summary>
/// S90 / TASK-9002 — a 409-mappable conflict raised by the atomic export path: a month-spanning
/// line (B4), a not-APPROVED period at export time (B2), or a same-key/different-content
/// re-export (idempotency). The endpoint maps this to a 409 Conflict.
/// </summary>
public sealed class PayrollExportConflictException : Exception
{
    public PayrollExportConflictException(string message) : base(message) { }
}

public sealed class ExportResult
{
    public required string ExportId { get; init; }
    public required bool Success { get; init; }
    public Guid MessageId { get; init; }
}
