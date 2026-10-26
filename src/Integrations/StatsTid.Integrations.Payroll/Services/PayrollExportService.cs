using Npgsql;
using NpgsqlTypes;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Outbox;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Models;
using System.Globalization;
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
    /// <item><description>(S145 / TASK-14512 Do 7) when the calculation required the PREVIOUS month to be
    /// APPROVED (<see cref="PayrollExportContext.PriorMonthToRecheck"/> set — the
    /// <c>/calculate-and-export</c> path), re-asserts that under <c>SELECT … FOR SHARE</c>, once, before any
    /// group's own lock — a previous month no longer APPROVED aborts the WHOLE tx with
    /// <see cref="PriorMonthNotApprovedException"/>;</description></item>
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
    ///
    /// <para>
    /// <b>S146 / TASK-14605 — the flex ledger (QUAL-188; R-3, R-14, R-15, R-16).</b>
    /// <list type="bullet">
    /// <item><description><b>The ledger lock, first.</b> The FIRST statement of the transaction takes the
    /// per-employee ledger lock for every employee of the call, in one statement ordered by key
    /// (<see cref="PayrollExportRecordRepository.AcquireFlexLedgerLocksAsync"/>), before the previous month's
    /// <c>FOR SHARE</c>, the current approval's <c>FOR UPDATE</c> and the record insert. All three routes that call
    /// this method take it, the raw ones included.</description></item>
    /// <item><description><b>The recheck</b> (calculating route only, <see cref="PayrollExportContext.FlexLedger"/>
    /// set). Right after the lock, the derivation runs again on in-transaction reads
    /// (<see cref="FlexLedgerWrite.RecheckUnderLock"/>); a different (opening, provenance) or a refusal aborts the
    /// whole transaction with <see cref="FlexLedgerRefusalException"/> <c>payroll-ledger-changed</c>.</description></item>
    /// <item><description><b>The balances</b> are written with the record (opening, closing, provenance); null on
    /// the raw routes and under <c>UNTRACKED</c>.</description></item>
    /// <item><description><b>The manifest</b> (S146 Step 7a, R-54). The calculating route's
    /// <see cref="PayrollExportContext.ManifestId"/> rides the <see cref="PayrollExportGenerated"/> event and its audit
    /// row (<c>manifestId</c>), so a record with no lines still names the calculation that produced its closing; a
    /// calculating-route context without one (null or <see cref="Guid.Empty"/>) is refused before the transaction
    /// opens. Null on the raw routes.</description></item>
    /// <item><description><b>A zero-line month</b> (R-3): the calculating route passes the record's identity in
    /// <see cref="FlexLedgerWrite"/>, so a month with an EMPLOYED day but no lines still writes its record (lines
    /// empty, balances set, corrections-only from then on). The empty line set hashes like any other line set through
    /// <see cref="PayrollExportManifest.ComputeContentHash"/> (the canonical JSON <c>[]</c>), defined once there. Nothing
    /// is delivered for it (no lines).</description></item>
    /// <item><description><b>The same-hash no-op</b> (R-16) additionally compares the derived (opening,
    /// provenance) and the computed closing with the recorded triple; any difference is the existing
    /// <see cref="PayrollExportConflictException"/> ("the balance changed; use a correction"). <c>UNTRACKED</c>
    /// against an all-null recorded triple is equal, ignoring the computed closing.</description></item>
    /// </list>
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
            .Select(g => (Key: g.Key, Lines: (IEnumerable<PayrollExportLine>)g))
            .ToList();

        // S146 TASK-14605 (R-3): the calculating route's record identity. Every line it sends belongs to that one
        // (employee, year, month); a month with an EMPLOYED day but no lines still gets its record (an empty group).
        var flex = context.FlexLedger;
        ExportGroupKey? flexKey = null;
        if (flex is not null)
        {
            var key = new ExportGroupKey(flex.EmployeeId, flex.Year, flex.Month);
            if (groups.Exists(g => g.Key != key))
            {
                throw new InvalidOperationException(
                    $"PayrollExportService.ExportAsync: the calculating route's flex-ledger write is for employee " +
                    $"'{flex.EmployeeId}' {flex.Year}-{flex.Month:D2}, but its lines also fall in another (employee, " +
                    "year, month). A calculation is one employee and one month; nothing was written.");
            }
            // S146 Step 7a (R-54): the calculating route's export names the manifest of the calculation that produced
            // it, carried onto PayrollExportGenerated. A record with no lines (R-3's employed zero-line month) has no
            // other carrier of that link, so an export without it is refused here, before the transaction opens. An
            // empty Guid counts as no manifest, the meaning StampAuditContext gives it ("no persisted manifest").
            if (context.ManifestId is not { } manifestId || manifestId == Guid.Empty)
            {
                throw new InvalidOperationException(
                    "PayrollExportService.ExportAsync: the calculating route's export names no manifest (null or empty); " +
                    "a recorded closing must link the calculation that produced it (ADR-016 D10). Nothing was written.");
            }
            if (groups.Count == 0)
                groups.Add((key, Array.Empty<PayrollExportLine>()));
            flexKey = key;
        }

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

            // S146 TASK-14605 (R-15; C3) — the ledger lock is the FIRST statement: every employee of the call, one
            // statement, ordered by key. It precedes every row lock below (the previous month's FOR SHARE, the
            // current approval's FOR UPDATE, the record insert's UNIQUE key), so payroll transactions lock in one
            // order: advisory, then approval rows, then records. The raw routes take it too: they are writers of
            // payroll_export_records, and a raw export racing a calculating export of the same employee must wait.
            await PayrollExportRecordRepository.AcquireFlexLedgerLocksAsync(
                conn, tx, groups.Select(g => g.Key.EmployeeId).Distinct(StringComparer.Ordinal).ToArray(), ct);

            // S146 TASK-14605 (R-14) — the under-lock recheck. The opening the calculation used was derived before
            // the calculation, outside any lock; another writer may have recorded or corrected the previous month
            // since. The SAME function runs again here on in-transaction reads (records and employment windows),
            // after the lock, so it sees everything committed before the lock was granted. A different (opening,
            // provenance) pair, or a refusal, throws FlexLedgerRefusalException payroll-ledger-changed and the
            // whole transaction rolls back: no record, no event, no audit row.
            if (flex is not null)
            {
                await flex.RecheckUnderLock(conn, tx, ct);
            }

            // S145 TASK-14512 Do 7 — the PREVIOUS month first. When the calculation's admission required the
            // previous month to be APPROVED (calculate-and-export threads it), re-check that approval here,
            // under a row lock held to COMMIT, BEFORE the current month's own FOR UPDATE below. Every export
            // thus locks an earlier month's approval row before a later one's, and a reopen locks exactly one
            // approval row (its own month's, after advisory locks no export takes), so no wait cycle can form.
            // Raw routes carry no previous month and skip this.
            if (context.PriorMonthToRecheck is { } priorMonth)
            {
                foreach (var employeeId in groups.Select(g => g.Key.EmployeeId).Distinct(StringComparer.Ordinal))
                {
                    await AssertPriorMonthApprovedForShareAsync(
                        conn, tx, employeeId, priorMonth.PeriodStart, priorMonth.PeriodEnd, ct);
                }
            }

            foreach (var group in groups)
            {
                var key = group.Key;
                var groupLines = PayrollExportManifest.OrderLines(group.Lines);

                // B2 — re-assert APPROVED under FOR UPDATE when this export maps to an approval
                // period (calculate-and-export threads the periodId). The raw /export +
                // /export-period bypass approval and carry no periodId → skip the re-check.
                if (context.PeriodId is { } periodId)
                {
                    await AssertPeriodApprovedForUpdateAsync(conn, tx, periodId, key, ct);
                }

                var contentHash = PayrollExportManifest.ComputeContentHash(groupLines);
                var manifestJson = PayrollExportManifest.Serialize(groupLines);

                // S146 TASK-14605 (C1): the balances this record stores — the calculating route's, for its own month,
                // unless flex is not tracked (UNTRACKED is never stored); none on the raw routes.
                var balances = flex is not null && key == flexKey && !flex.IsUntracked
                    ? new RecordedBalances(
                        flex.OpeningBalance ?? throw MissingBalance(flex, "opening"),
                        flex.ClosingBalance ?? throw MissingBalance(flex, "closing"),
                        flex.Provenance)
                    : null;

                // INSERT with idempotency. ON CONFLICT (employee_id, year, month) DO NOTHING +
                // RETURNING distinguishes the first write (a row comes back) from a conflict
                // (no row → read the existing row and compare hashes).
                var existingExportId = await TryInsertRecordAsync(
                    conn, tx, key, context, exportedAt, contentHash, manifestJson, balances, ct);

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
                        // S146 TASK-14605 (C1): what the record stores — null on the raw routes.
                        OpeningFlexBalance = balances?.Opening,
                        ClosingFlexBalance = balances?.Closing,
                        FlexProvenance = balances?.Provenance,
                        // S146 Step 7a (R-54): the calculation's manifest — null on the raw routes.
                        ManifestId = context.ManifestId,
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
                    var existingRecord = await ReadExistingRecordAsync(conn, tx, key, ct);
                    if (!string.Equals(existingRecord.ContentHash, contentHash, StringComparison.Ordinal))
                    {
                        throw new PayrollExportConflictException(
                            $"Employee '{key.EmployeeId}' already has a payroll export for {key.Year}-{key.Month:D2} " +
                            $"with different content; use a correction (/api/payroll/recalculate) to amend an exported month.");
                    }

                    // S146 TASK-14605 (R-16; contract 8): on the calculating route the same lines are not enough — the
                    // derived (opening, provenance) and the computed closing must equal the recorded triple, or the
                    // month's balance changed and only a correction may change it. UNTRACKED (a raw month before the
                    // ledger start) is equal to an all-null recorded triple whatever the computed closing; the raw
                    // routes keep the content-hash compare alone.
                    if (flex is not null && key == flexKey && !RecordedTripleMatches(flex, existingRecord))
                    {
                        throw new PayrollExportConflictException(
                            $"Employee '{key.EmployeeId}' already has a payroll export for {key.Year}-{key.Month:D2} " +
                            "with the same lines but a different flex balance: the balance changed; use a correction " +
                            "(/api/payroll/recalculate) to amend an exported month.");
                    }

                    // Same hash → idempotent no-op: return the existing export id, emit NOTHING.
                    perGroup.Add(new GroupExportOutcome(existingRecord.ExportId, IsNew: false));
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

        // S146 TASK-14605 (R-3): a newly recorded zero-line month (the calculating route's employed month with no
        // lines) hands payroll nothing; its record exists for the ledger and the lock. No envelope, no POST.
        if (newlyExportedLines.Count == 0)
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
    /// S145 / TASK-14512 Do 7 — closes the reopen-vs-export race on the PREVIOUS month (adjudicator R-4 on
    /// TASK-14509). Re-reads the previous month's approval status under <c>SELECT … FOR SHARE</c> inside
    /// the export transaction and requires <c>APPROVED</c>; a missing row or any other status throws
    /// <see cref="PriorMonthNotApprovedException"/>, which rolls the whole export back.
    ///
    /// <para>
    /// <b>The race (plain language).</b> October's first week holds September's last days, so October is
    /// calculated only while September is APPROVED (TASK-14512 Do 2). A reopen of September locks
    /// September's approval row and, in the same transaction, writes a worklist row for October if October
    /// is already exported. Without this recheck an October export could read September as APPROVED a
    /// moment before the reopen committed, the reopen could read "October not exported" a moment before the
    /// export committed, and both would commit: October paid on a September being withdrawn, and no
    /// worklist row telling HR.
    /// </para>
    ///
    /// <para>
    /// <b>Why a row lock, and why it is enough.</b> A plain SELECT under READ COMMITTED would not wait for
    /// the reopen. <c>FOR SHARE</c> conflicts with the reopen's <c>FOR UPDATE</c> on the same row, so the
    /// two transactions serialize: if the reopen holds the row, this read waits for its commit and then
    /// reads the committed status (DRAFT) and refuses; if this export holds the row, the reopen waits until
    /// the export commits and then — its next statement seeing committed data — finds October's export
    /// record and writes the worklist row. <c>FOR SHARE</c> rather than <c>FOR UPDATE</c> because the export
    /// only reads the row: two exports may share it, and nothing here ever upgrades the lock.
    /// </para>
    ///
    /// <para>
    /// <b>Lock order.</b> This is taken before the current month's row lock
    /// (<see cref="AssertPeriodApprovedForUpdateAsync"/>), so every export locks an earlier month before a
    /// later one. The reopen takes its advisory locks (which no export takes) and then exactly one approval
    /// row lock, its own month's, before it reads the export records (a plain read, no lock). No
    /// transaction ever holds a later month's approval row while waiting for an earlier one's, so no wait
    /// cycle can form.
    /// </para>
    /// </summary>
    private static async Task AssertPriorMonthApprovedForShareAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string employeeId, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            """
            SELECT status FROM approval_periods
            WHERE employee_id = @employeeId AND period_start = @periodStart AND period_end = @periodEnd
            FOR SHARE
            """, conn, tx);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("periodStart", periodStart);
        cmd.Parameters.AddWithValue("periodEnd", periodEnd);
        var status = await cmd.ExecuteScalarAsync(ct) as string;

        if (!string.Equals(status, "APPROVED", StringComparison.Ordinal))
        {
            throw new PriorMonthNotApprovedException(
                periodStart,
                periodEnd,
                $"Payroll export aborted: the previous month of employee '{employeeId}' " +
                $"({periodStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.." +
                $"{periodEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}) is " +
                $"'{status ?? "(no approval row)"}', not " +
                "APPROVED, at export time (re-checked under a row lock; it was withdrawn after the calculation " +
                "read it). Nothing was exported.");
        }
    }

    /// <summary>
    /// Inserts the lock/manifest record with <c>ON CONFLICT (employee_id, year, month) DO NOTHING
    /// RETURNING export_id</c>. Returns the new export id on a first write, or <c>null</c> when the
    /// UNIQUE key already exists (the caller then reads + hash-compares for idempotency).
    /// </summary>
    private static async Task<Guid?> TryInsertRecordAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, ExportGroupKey key, PayrollExportContext context,
        DateTimeOffset exportedAt, string contentHash, string manifestJson, RecordedBalances? balances,
        CancellationToken ct)
    {
        var exportId = Guid.NewGuid();
        // S146 TASK-14605 (C1): the three balance columns, all set (a calculating route's tracked month) or all null
        // (the raw routes, and UNTRACKED); the CHECK payroll_export_records_balances_paired refuses anything between.
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO payroll_export_records (
                export_id, period_id, employee_id, year, month, exported_at,
                original_lines, current_effective_lines, content_hash, source,
                opening_balance, closing_balance, balance_provenance
            ) VALUES (
                @exportId, @periodId, @employeeId, @year, @month, @exportedAt,
                @originalLines::jsonb, @currentLines::jsonb, @contentHash, @source,
                @openingBalance, @closingBalance, @balanceProvenance
            )
            ON CONFLICT (employee_id, year, month) DO NOTHING
            RETURNING export_id
            """, conn, tx);
        cmd.Parameters.Add(new NpgsqlParameter("openingBalance", NpgsqlDbType.Numeric) { Value = (object?)balances?.Opening ?? DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter("closingBalance", NpgsqlDbType.Numeric) { Value = (object?)balances?.Closing ?? DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter("balanceProvenance", NpgsqlDbType.Text) { Value = (object?)balances?.Provenance ?? DBNull.Value });
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

    private static async Task<ExistingRecord> ReadExistingRecordAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, ExportGroupKey key, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            """
            SELECT export_id, content_hash, opening_balance, closing_balance, balance_provenance
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
        return new ExistingRecord(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetDecimal(2),
            reader.IsDBNull(3) ? null : reader.GetDecimal(3),
            reader.IsDBNull(4) ? null : reader.GetString(4));
    }

    /// <summary>
    /// S146 / TASK-14605 (R-16; contract 8) — the same-hash re-run's balance compare. A tracked month matches only
    /// when the recorded opening, provenance and closing all equal the derived opening, its provenance and the
    /// computed closing. <c>UNTRACKED</c> matches an all-null recorded triple and ignores the computed closing (a
    /// raw month before the ledger start keeps no balance, so there is nothing to compare it with).
    /// </summary>
    private static bool RecordedTripleMatches(FlexLedgerWrite flex, ExistingRecord recorded)
    {
        if (flex.IsUntracked)
        {
            return recorded.OpeningBalance is null
                && recorded.ClosingBalance is null
                && recorded.Provenance is null;
        }

        return recorded.OpeningBalance == flex.OpeningBalance
            && recorded.ClosingBalance == flex.ClosingBalance
            && string.Equals(recorded.Provenance, flex.Provenance, StringComparison.Ordinal);
    }

    private static InvalidOperationException MissingBalance(FlexLedgerWrite flex, string which) =>
        new($"PayrollExportService.ExportAsync: the calculating route's flex-ledger write for employee " +
            $"'{flex.EmployeeId}' {flex.Year}-{flex.Month:D2} is {flex.Provenance} but carries no {which} balance; " +
            "a tracked month always has both. Nothing was written.");

    private readonly record struct ExportGroupKey(string EmployeeId, int Year, int Month);

    private sealed record RecordedBalances(decimal Opening, decimal Closing, string Provenance);

    private sealed record ExistingRecord(
        Guid ExportId, string ContentHash, decimal? OpeningBalance, decimal? ClosingBalance, string? Provenance);

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

    /// <summary>
    /// S145 / TASK-14512 Do 7 — the previous month's pay period the calculation's admission required to be
    /// APPROVED (calculate-and-export only, from <c>PeriodCalculationOutcome.PriorMonthApprovalRequired</c>);
    /// <c>null</c> when the check did not apply, and always for the raw endpoints. When set, the export
    /// re-checks that approval under a row lock, before the current month's, inside its transaction.
    /// </summary>
    public (DateOnly PeriodStart, DateOnly PeriodEnd)? PriorMonthToRecheck { get; init; }

    /// <summary>
    /// S146 / TASK-14605 (QUAL-188; R-3, R-14, R-16) — the calculating route's flex-ledger write: the record's
    /// identity, the derived opening and provenance, the computed closing, and the under-lock recheck. Set ONLY by
    /// <c>/calculate-and-export</c>, from <see cref="PeriodCalculationService.FlexLedgerWriteFor"/>, for a month
    /// holding an EMPLOYED day; <c>null</c> on the raw routes, which write null balances and are otherwise
    /// unchanged.
    /// </summary>
    public FlexLedgerWrite? FlexLedger { get; init; }

    /// <summary>
    /// S146 Step 7a (R-54; TASK-14610) — the manifest of the calculation whose figures this export stores
    /// (ADR-016 D10), carried onto <see cref="PayrollExportGenerated.ManifestId"/>: calculate-and-export only; null
    /// on the raw routes. Required whenever <see cref="FlexLedger"/> is set — <see cref="PayrollExportService.ExportAsync"/>
    /// refuses a calculating-route export that names no manifest (null or <see cref="Guid.Empty"/>), before its
    /// transaction opens, because a record with no lines (the employed zero-line month) has no other carrier of the
    /// link.
    /// </summary>
    public Guid? ManifestId { get; init; }

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
/// S146 / TASK-14605 (QUAL-188; rulings R-3, R-14, R-16) — what the calculating route hands
/// <see cref="PayrollExportService.ExportAsync"/> about the month's flex balance.
///
/// <para>
/// <b>Plain language.</b> The calculation derived the month's opening balance from the recorded ledger before it
/// ran, and the engine returned the closing. The export stores both on the month's record, but only after checking,
/// under the per-employee ledger lock, that the ledger still gives the same opening: if another export or a
/// correction recorded the previous month in between, the stored balance would be built on a stale opening, so the
/// export refuses (<c>payroll-ledger-changed</c>) and the caller retries.
/// </para>
///
/// <para>
/// <b>Members.</b> The record's identity (<see cref="EmployeeId"/>, <see cref="Year"/>, <see cref="Month"/>), so a
/// month with an EMPLOYED day but no lines still writes its record (R-3); the derived
/// <see cref="OpeningBalance"/> and <see cref="Provenance"/> and the computed <see cref="ClosingBalance"/> (both
/// balances null under <c>UNTRACKED</c>, which is never stored); and <see cref="RecheckUnderLock"/>, the
/// derivation's in-transaction re-run, owned by <see cref="PeriodCalculationService"/>
/// (<see cref="PeriodCalculationService.RecheckFlexOpeningUnderLockAsync"/>), which the export calls right after the
/// ledger lock. Built by <see cref="PeriodCalculationService.FlexLedgerWriteFor"/>.
/// </para>
/// </summary>
public sealed class FlexLedgerWrite
{
    /// <summary>The employee of the record.</summary>
    public required string EmployeeId { get; init; }

    /// <summary>The record's year (the calendar month of the period start).</summary>
    public required int Year { get; init; }

    /// <summary>The record's month (1..12).</summary>
    public required int Month { get; init; }

    /// <summary><c>LEDGER_START</c>, <c>CHAINED</c>, <c>REHIRE</c> or <c>UNTRACKED</c>.</summary>
    public required string Provenance { get; init; }

    /// <summary>The derived opening balance; <c>null</c> under <c>UNTRACKED</c>.</summary>
    public decimal? OpeningBalance { get; init; }

    /// <summary>The engine's clamped closing balance (<c>Flex.NewBalance</c>); <c>null</c> under <c>UNTRACKED</c>.</summary>
    public decimal? ClosingBalance { get; init; }

    /// <summary>
    /// The derivation's re-run on in-transaction reads, called inside the export transaction right after the
    /// ledger lock; throws <see cref="FlexLedgerRefusalException"/> <c>payroll-ledger-changed</c> when the ledger
    /// no longer gives the (opening, provenance) the calculation used.
    /// </summary>
    public required Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task> RecheckUnderLock { get; init; }

    /// <summary>True under <c>UNTRACKED</c>: no balance is stored.</summary>
    public bool IsUntracked => string.Equals(Provenance, FlexLedgerOpening.Untracked, StringComparison.Ordinal);
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
