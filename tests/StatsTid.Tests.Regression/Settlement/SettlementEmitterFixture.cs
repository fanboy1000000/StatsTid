using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.Integrations.Payroll.Services;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Models;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Settlement;

/// <summary>
/// S69 / TASK-6907 (ADR-033 slice 1b) — shared Docker-gated harness for the §24 settlement-export
/// emitter D-tests (<see cref="SettlementExportEmitterTests"/>,
/// <see cref="ReconcileEmitterMutualExclusionTests"/>, <see cref="Settlement24WageMappingSchemaTests"/>).
///
/// <para>
/// <b>Harness shape (the established settlement-suite pattern — mirrors
/// <see cref="SettlementCloseServiceBoundaryTests"/> / <see cref="SettlementSchemaConstraintTests"/>).</b>
/// A per-test Postgres testcontainer (<see cref="TestFixtures.DockerHarness"/>) + the FULL canonical
/// <c>docker/postgres/init.sql</c> schema (<see cref="StatsTidWebApplicationFactory.ApplyFullSchemaAsync"/>),
/// so the new <c>settlement_payroll_inbox</c> + <c>settlement_export_lines</c> tables and the §24
/// <c>wage_type_mappings</c> seed (SLS_TBD_S24) are present exactly as production creates them. Each
/// test seeds a FRESH employee id (unique GUID) via <see cref="RegressionSeed"/> (users +
/// user_agreement_codes + employee_profiles), so the per-employee assertions never collide with the
/// init.sql seed users.
/// </para>
///
/// <para>
/// <b>Driving the emitter deterministically (W6 crash matrix).</b> The
/// <see cref="SettlementExportEmitter"/> is a <see cref="Microsoft.Extensions.Hosting.BackgroundService"/>
/// whose <c>ExecuteAsync</c> runs ONE <c>DrainOnceAsync</c> immediately on entry, BEFORE its first
/// 30-second <c>Task.Delay</c>. <see cref="ProcessOnceAsync"/> constructs the REAL emitter (its real
/// repository + the real <see cref="WageTypeMappingRepository"/>), starts it, waits on a bounded
/// condition for the inbox row to reach the expected state, then stops it — so exactly ONE real drain
/// runs (the 30s delay guarantees no second drain inside the wait window). This tests the production
/// code path, not a re-implementation of it. There is NO production test seam required (the emitter's
/// repository methods are already <c>public</c>).
/// </para>
///
/// <para>
/// <b>FAIL-002 close protocol.</b> These are Docker-gated regression tests
/// (<c>[Trait("Category", "Docker")]</c>); run under a fresh Docker session with EXCLUSIVE full-suite
/// runs and Out-File log capture per the S65 FAIL-002 protocol; never modify tests for a
/// testcontainer-churn shed (DockerApiException at [1 ms] class-init).
/// </para>
/// </summary>
internal static class SettlementEmitterFixture
{
    public const string OrgId = "STY01";
    public const string VacationType = "VACATION";
    public const string AutoPayoutBucket = "AUTO_PAYOUT_24";
    public const string SettlementTimeType = "VACATION_SETTLEMENT_PAYOUT";
    public const string SentinelWageType = "SLS_TBD_S24";

    // S71 / TASK-7105 — §26 + reversal consumer axes (SPRINT-71 R6/R7/R8/R11).
    public const string TerminationBucket = "TERMINATION_PAYOUT_26";
    public const string TerminationTimeType = "VACATION_TERMINATION_PAYOUT";
    public const string TerminationSentinelWageType = "SLS_TBD_S26";
    public const string EventLevelBucket = "_EVENT";

    /// <summary>The synthetic settlement-boundary date written into the D-tests' snapshots and used as
    /// the emitter's dated-mapping <c>asOf</c>. A fixed, freely-chosen value — NOT the production
    /// reset-9 ferieår-end (a real VACATION reset_month-9 ferieår 2024 ends Aug 31 2025, per
    /// <see cref="StatsTid.SharedKernel.Models.VacationSettlementSnapshot.SettlementBoundaryDate"/>;
    /// see also S69 Step-7a W1). These tests assert the dated-lookup BEHAVIOR (snapshot-keyed, not live),
    /// so the exact calendar value only needs to be fixed/deterministic — no wall-clock dependence (the
    /// snapshot carries it and the emitter keys the mapping lookup off it).</summary>
    public static readonly DateOnly BoundaryDate = new(2025, 12, 31);

    // ─────────────────────────────── seeding ───────────────────────────────

    /// <summary>Seeds a fresh employee (users + user_agreement_codes + employee_profiles) and returns
    /// the id. Agreement AC / OK24 (a §24 mapping exists for that pair).</summary>
    public static async Task<string> SeedEmployeeAsync(string connectionString, string prefix = "emp_s69_")
    {
        var employeeId = prefix + Guid.NewGuid().ToString("N")[..8];
        await RegressionSeed.SeedEmployeeAsync(connectionString, employeeId, OrgId, "AC", "OK24");
        return employeeId;
    }

    /// <summary>
    /// Seeds a SETTLED <c>vacation_settlements</c> row for the §24 boundary (sequence 1) directly, with
    /// a valid camelCase snapshot whose <c>agreementCode</c>/<c>okVersion</c>/<c>position</c>/
    /// <c>settlementBoundaryDate</c> are the wage-mapping key the emitter resolves off (B4). The
    /// <c>payoutDays</c> bucket is what the emitter stages as the line's <c>hours</c>.
    /// </summary>
    public static async Task SeedSettledPayoutRowAsync(
        string connectionString, string employeeId, decimal payoutDays,
        string agreementCode = "AC", string okVersion = "OK24", string? position = null,
        DateOnly? boundaryDate = null, bool reconciled = false, int sequence = 1, int year = 2024)
    {
        var snapshotJson = BuildSnapshotJson(agreementCode, okVersion, position, boundaryDate ?? BoundaryDate, payoutDays);

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO vacation_settlements
                (employee_id, entitlement_type, entitlement_year, sequence,
                 settlement_state, trigger, snapshot, transfer_days, payout_days, forfeit_days,
                 review_disposition, version,
                 payout_reconciled_at, payout_reconciled_by)
            VALUES
                (@e, @t, @y, @seq, 'SETTLED', 'YEAR_END', @snapshot::jsonb, 0, @payout, 0,
                 NULL, 1,
                 CASE WHEN @reconciled THEN NOW() ELSE NULL END,
                 CASE WHEN @reconciled THEN 'operator_qa' ELSE NULL END)
            ON CONFLICT (employee_id, entitlement_type, entitlement_year, sequence)
                DO UPDATE SET payout_days = EXCLUDED.payout_days,
                              snapshot = EXCLUDED.snapshot,
                              payout_reconciled_at = EXCLUDED.payout_reconciled_at,
                              payout_reconciled_by = EXCLUDED.payout_reconciled_by
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("t", VacationType);
        cmd.Parameters.AddWithValue("y", year);
        cmd.Parameters.AddWithValue("seq", sequence);
        cmd.Parameters.AddWithValue("snapshot", snapshotJson);
        cmd.Parameters.AddWithValue("payout", payoutDays);
        cmd.Parameters.AddWithValue("reconciled", reconciled);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>A valid camelCase <see cref="VacationSettlementSnapshot"/> JSON carrying the §24
    /// wage-mapping key (B4) + the payout day-count. <paramref name="position"/> null serializes as
    /// absent (the emitter canonicalizes to "").</summary>
    public static string BuildSnapshotJson(
        string? agreementCode, string? okVersion, string? position, DateOnly boundaryDate, decimal payoutDays)
    {
        var snapshot = new VacationSettlementSnapshot
        {
            Earned = 25m,
            Used = 25m - payoutDays,
            Planned = 0m,
            CarryoverIn = 0m,
            AnnualQuota = 25m,
            CarryoverMax = 5m,
            ResetMonth = 9,
            OkVersion = okVersion,
            AgreementCode = agreementCode,
            Position = position,
            SettlementBoundaryDate = boundaryDate,
            TransferAgreementDays = 0m,
            IsFeriehindret = false,
        };
        // Identical options to VacationSettlementService.SnapshotJson / EventSerializer (camelCase).
        return System.Text.Json.JsonSerializer.Serialize(snapshot,
            new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
    }

    // ─────────────────────────────── event writing ───────────────────────────────

    /// <summary>
    /// Writes a <c>VacationAutoPaidOut</c> to the canonical <c>events</c> table (the emitter's source)
    /// via the production <see cref="PostgresEventStore.AppendAsync"/> — so it lands with
    /// <c>event_type='VacationAutoPaidOut'</c> and a payload the emitter's <see cref="EventSerializer"/>
    /// deserialization round-trips. Returns the <c>event_id</c> (the inbox PK to query by). The snapshot
    /// passed here is the immutable settle-time input (its <c>SettlementBoundaryDate</c> drives the
    /// dated mapping lookup).
    /// </summary>
    public static async Task<Guid> WriteAutoPaidOutEventAsync(
        DbConnectionFactory factory, string employeeId, decimal payoutDays,
        VacationSettlementSnapshot snapshot, int year = 2024, int sequence = 1)
    {
        var eventId = Guid.NewGuid();
        var @event = new VacationAutoPaidOut
        {
            EventId = eventId,
            EmployeeId = employeeId,
            EntitlementType = VacationType,
            EntitlementYear = year,
            Sequence = sequence,
            Snapshot = snapshot,
            PayoutDays = payoutDays,
        };
        var store = new PostgresEventStore(factory);
        await store.AppendAsync($"employee-{employeeId}", @event);
        return eventId;
    }

    /// <summary>Convenience: builds the snapshot + writes the event in one call (the common path).</summary>
    public static async Task<Guid> WriteAutoPaidOutEventAsync(
        DbConnectionFactory factory, string employeeId, decimal payoutDays,
        string? agreementCode = "AC", string? okVersion = "OK24", string? position = null,
        DateOnly? boundaryDate = null, int year = 2024, int sequence = 1)
    {
        var snapshot = new VacationSettlementSnapshot
        {
            Earned = 25m,
            Used = 25m - payoutDays,
            Planned = 0m,
            CarryoverIn = 0m,
            AnnualQuota = 25m,
            CarryoverMax = 5m,
            ResetMonth = 9,
            OkVersion = okVersion,
            AgreementCode = agreementCode,
            Position = position,
            SettlementBoundaryDate = boundaryDate ?? BoundaryDate,
            TransferAgreementDays = 0m,
            IsFeriehindret = false,
        };
        return await WriteAutoPaidOutEventAsync(factory, employeeId, payoutDays, snapshot, year, sequence);
    }

    // ─────────────────────────────── driving the emitter ───────────────────────────────

    /// <summary>Builds the REAL emitter against the supplied factory (real repo + real wage-mapping repo).</summary>
    public static SettlementExportEmitter BuildEmitter(DbConnectionFactory factory)
    {
        var repo = new SettlementInboxLineRepository(factory);
        var wtm = new WageTypeMappingRepository(factory);
        return new SettlementExportEmitter(repo, wtm, NullLogger<SettlementExportEmitter>.Instance);
    }

    /// <summary>
    /// Runs EXACTLY ONE real <c>DrainOnceAsync</c>: starts the BackgroundService (its first action is one
    /// drain, before the 30s delay), waits up to <paramref name="settleWait"/> for
    /// <paramref name="until"/> to hold (the drain is async on a background thread), then stops it (which
    /// cancels the 30s delay so no second drain runs). The <paramref name="until"/> predicate is
    /// re-evaluated on a short interval — a bounded wait-on-condition, not a fixed sleep.
    /// </summary>
    public static async Task ProcessOnceAsync(
        SettlementExportEmitter emitter, Func<Task<bool>> until, TimeSpan? settleWait = null)
    {
        var wait = settleWait ?? TimeSpan.FromSeconds(20);
        using var cts = new CancellationTokenSource();
        await emitter.StartAsync(cts.Token);
        try
        {
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < wait)
            {
                if (await until())
                    return;
                await Task.Delay(100);
            }
        }
        finally
        {
            cts.Cancel();
            await emitter.StopAsync(CancellationToken.None);
        }
    }

    // ─────────────────────────────── S71 seeding / events (TASK-7105) ───────────────────────────────

    /// <summary>
    /// Seeds a settlement row DIRECTLY (the TerminationPayoutRequestEndpointTests camelCase-snapshot
    /// shape) — used by the §26/reversal consumer tests where the row state, not the close pipeline,
    /// is the test input. TERMINATION rows carry all-zero buckets + a snapshot
    /// <c>crystallizedDays</c> (SPRINT-70 R5); the snapshot's agreement/OK/boundary are the
    /// wage-mapping key the §26 consumer resolves off.
    /// </summary>
    public static async Task SeedSettlementRowAsync(
        string connectionString, string employeeId,
        string trigger = "TERMINATION", string state = "SETTLED",
        decimal? crystallizedDays = 12.5m,
        string agreementCode = "AC", string okVersion = "OK24", string? position = null,
        DateOnly? boundaryDate = null, int year = 2024, int sequence = 1,
        decimal payoutDays = 0m)
    {
        var boundary = boundaryDate ?? BoundaryDate;
        var snapshot = new Dictionary<string, object?>
        {
            ["recordedAbsences"] = Array.Empty<object>(),
            ["earned"] = 25m,
            ["used"] = 0m,
            ["planned"] = 0m,
            ["carryoverIn"] = 0m,
            ["annualQuota"] = 25m,
            ["carryoverMax"] = 5m,
            ["resetMonth"] = 9,
            ["okVersion"] = okVersion,
            ["agreementCode"] = agreementCode,
            ["transferAgreementDays"] = 0m,
            ["isFeriehindret"] = false,
            ["settlementBoundaryDate"] = boundary.ToString("yyyy-MM-dd"),
        };
        if (position is not null)
            snapshot["position"] = position;
        if (crystallizedDays is not null)
        {
            snapshot["crystallizedDays"] = crystallizedDays.Value;
            snapshot["terminationDate"] = boundary.ToString("yyyy-MM-dd");
            snapshot["crystallizationBasis"] = "S26_WHOLE_MONTH";
        }

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO vacation_settlements
                (employee_id, entitlement_type, entitlement_year, sequence,
                 settlement_state, trigger, snapshot, transfer_days, payout_days, forfeit_days, version)
            VALUES (@e, @t, @y, @seq, @state, @trigger, @snapshot::jsonb, 0, @payout, 0, 1)
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("t", VacationType);
        cmd.Parameters.AddWithValue("y", year);
        cmd.Parameters.AddWithValue("seq", sequence);
        cmd.Parameters.AddWithValue("state", state);
        cmd.Parameters.AddWithValue("trigger", trigger);
        cmd.Parameters.AddWithValue("snapshot", System.Text.Json.JsonSerializer.Serialize(snapshot));
        cmd.Parameters.AddWithValue("payout", payoutDays);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Flips a settlement row to REVERSED directly (the reversal-service end state the
    /// consumers re-read under the lock — the R12 emitter-vs-reversal race input).</summary>
    public static async Task MarkSettlementReversedAsync(
        string connectionString, string employeeId, int year = 2024, int sequence = 1)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE vacation_settlements
               SET settlement_state = 'REVERSED', version = version + 1, updated_at = NOW()
             WHERE employee_id = @e AND entitlement_type = @t
               AND entitlement_year = @y AND sequence = @seq
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("t", VacationType);
        cmd.Parameters.AddWithValue("y", year);
        cmd.Parameters.AddWithValue("seq", sequence);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Seeds a <c>termination_payout_requests</c> row directly (state stated explicitly —
    /// the 7100 no-default contract).</summary>
    public static async Task SeedRequestRowAsync(
        string connectionString, string employeeId, string state = "OPEN",
        int year = 2024, int settlementSequence = 1)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO termination_payout_requests
                (employee_id, entitlement_type, entitlement_year, settlement_sequence,
                 state, request_date, recorded_by, version)
            VALUES (@e, @t, @y, @seq, @state, '2026-01-15', 'hr_qa', 1)
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("t", VacationType);
        cmd.Parameters.AddWithValue("y", year);
        cmd.Parameters.AddWithValue("seq", settlementSequence);
        cmd.Parameters.AddWithValue("state", state);
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task<string?> RequestStateAsync(
        string connectionString, string employeeId, int year = 2024, int settlementSequence = 1)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT state FROM termination_payout_requests
            WHERE employee_id = @e AND entitlement_type = @t
              AND entitlement_year = @y AND settlement_sequence = @seq
            ORDER BY request_id DESC LIMIT 1
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("t", VacationType);
        cmd.Parameters.AddWithValue("y", year);
        cmd.Parameters.AddWithValue("seq", settlementSequence);
        var v = await cmd.ExecuteScalarAsync();
        return v as string;
    }

    /// <summary>Writes a <c>TerminationPayoutRequested</c> to the canonical <c>events</c> table via
    /// the production <see cref="PostgresEventStore.AppendAsync"/> (the §26 consumer's source).</summary>
    public static async Task<Guid> WriteTerminationPayoutRequestedEventAsync(
        DbConnectionFactory factory, string employeeId, decimal crystallizedDays,
        DateOnly? boundaryDate = null, int year = 2024, int settlementSequence = 1)
    {
        var eventId = Guid.NewGuid();
        var @event = new TerminationPayoutRequested
        {
            EventId = eventId,
            EmployeeId = employeeId,
            EntitlementType = VacationType,
            EntitlementYear = year,
            SettlementSequence = settlementSequence,
            RequestDate = new DateOnly(2026, 1, 15),
            CrystallizedDays = crystallizedDays,
            SettlementBoundaryDate = boundaryDate ?? BoundaryDate,
        };
        var store = new PostgresEventStore(factory);
        await store.AppendAsync($"employee-{employeeId}", @event);
        return eventId;
    }

    /// <summary>Writes a <c>SettlementReversed</c> (R10 shape) to the canonical <c>events</c> table
    /// via the production <see cref="PostgresEventStore.AppendAsync"/> (the reversal consumer's source).</summary>
    public static async Task<Guid> WriteSettlementReversedEventAsync(
        DbConnectionFactory factory, string employeeId, int year = 2024, int settlementSequence = 1,
        string reversalKind = "BARE", string trigger = "TERMINATION",
        decimal payoutDays = 0m, decimal? crystallizedDays = 12.5m)
    {
        var eventId = Guid.NewGuid();
        var @event = new SettlementReversed
        {
            EventId = eventId,
            EmployeeId = employeeId,
            EntitlementType = VacationType,
            EntitlementYear = year,
            SettlementSequence = settlementSequence,
            ReversalKind = reversalKind,
            Trigger = trigger,
            TransferDays = 0m,
            PayoutDays = payoutDays,
            ForfeitDays = 0m,
            CrystallizedDays = crystallizedDays,
        };
        var store = new PostgresEventStore(factory);
        await store.AppendAsync($"employee-{employeeId}", @event);
        return eventId;
    }

    /// <summary>Pre-inserts a staged export line directly (a reversal-consumer compensation target,
    /// or a foreign-source collision input). <paramref name="lineKind"/>/<paramref name="reversesLineId"/>
    /// default to the ORIGINAL shape. Returns <c>line_id</c>.</summary>
    public static async Task<long> InsertLineRowAsync(
        string connectionString, string employeeId, Guid sourceEventId,
        string bucket, string wageType, decimal hours,
        int year = 2024, int sequence = 1,
        string lineKind = "ORIGINAL", long? reversesLineId = null,
        string createdBy = "pre-existing-test-line")
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO settlement_export_lines
                (employee_id, entitlement_type, entitlement_year, sequence, bucket,
                 wage_type, hours, amount, ok_version, agreement_code, position,
                 period_start, period_end, source_event_id, created_by, line_kind, reverses_line_id)
            VALUES
                (@e, @t, @y, @seq, @b, @wt, @hours, 0, 'OK24', 'AC', '',
                 @d, @d, @src, @createdBy, @kind, @rev)
            RETURNING line_id
            """, conn);
        cmd.Parameters.AddWithValue("createdBy", createdBy);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("t", VacationType);
        cmd.Parameters.AddWithValue("y", year);
        cmd.Parameters.AddWithValue("seq", sequence);
        cmd.Parameters.AddWithValue("b", bucket);
        cmd.Parameters.AddWithValue("wt", wageType);
        cmd.Parameters.AddWithValue("hours", hours);
        cmd.Parameters.AddWithValue("d", BoundaryDate);
        cmd.Parameters.AddWithValue("src", sourceEventId);
        cmd.Parameters.AddWithValue("kind", lineKind);
        cmd.Parameters.AddWithValue("rev", (object?)reversesLineId ?? DBNull.Value);
        return Convert.ToInt64((await cmd.ExecuteScalarAsync())!);
    }

    // ─────────────────────────────── inbox / line reads ───────────────────────────────

    public static async Task<string?> InboxStatusAsync(string connectionString, Guid eventId)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT processing_status FROM settlement_payroll_inbox WHERE source_event_id = @id", conn);
        cmd.Parameters.AddWithValue("id", eventId);
        var v = await cmd.ExecuteScalarAsync();
        return v as string;
    }

    /// <summary>The inbox status at ONE composite key (S71 — bucket-keyed reads are exact where
    /// multiple rows per event can exist; '_EVENT' reads the event-level sentinel row).</summary>
    public static async Task<string?> InboxStatusForBucketAsync(
        string connectionString, Guid eventId, string bucket)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT processing_status FROM settlement_payroll_inbox WHERE source_event_id = @id AND bucket = @b", conn);
        cmd.Parameters.AddWithValue("id", eventId);
        cmd.Parameters.AddWithValue("b", bucket);
        var v = await cmd.ExecuteScalarAsync();
        return v as string;
    }

    /// <summary>All inbox rows for an event as (bucket → status) — the multi-bucket assertions.</summary>
    public static async Task<Dictionary<string, string>> InboxRowsAsync(string connectionString, Guid eventId)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT bucket, processing_status FROM settlement_payroll_inbox WHERE source_event_id = @id", conn);
        cmd.Parameters.AddWithValue("id", eventId);
        var rows = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows[reader.GetString(0)] = reader.GetString(1);
        return rows;
    }

    /// <summary>Deletes the inbox checkpoint row for <paramref name="eventId"/> (leaving any staged
    /// line intact). Used to make a fully-processed event SELECTABLE again so a second real drain drives
    /// a TRUE duplicate claim through the line-UNIQUE / BenignRedelivery branch (Step-7a FIX 2).</summary>
    public static async Task DeleteInboxRowAsync(string connectionString, Guid eventId)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "DELETE FROM settlement_payroll_inbox WHERE source_event_id = @id", conn);
        cmd.Parameters.AddWithValue("id", eventId);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Plants a POISON <c>VacationAutoPaidOut</c> row in the canonical <c>events</c> table: first writes a
    /// REAL event via the production <see cref="PostgresEventStore.AppendAsync"/> (so the stream + every
    /// NOT-NULL events column is correct exactly as production lands it), then CORRUPTS that row's
    /// <c>data</c> JSONB to a payload the emitter's <see cref="EventSerializer.Deserialize"/> CANNOT bind
    /// (a non-Guid <c>eventId</c>). The poll still selects it (<c>event_type='VacationAutoPaidOut'</c>),
    /// but deserialize throws ⇒ the emitter must dead-letter it terminally (Step-7a FIX 1 test). Returns
    /// the <c>event_id</c>.
    /// </summary>
    public static async Task<Guid> WritePoisonAutoPaidOutEventAsync(DbConnectionFactory factory, string employeeId)
    {
        // (1) A real, well-formed event (correct stream/columns) — reuse the production write path.
        var eventId = await WriteAutoPaidOutEventAsync(factory, employeeId, payoutDays: 5m);

        // (2) Corrupt ONLY its data JSONB so EventSerializer.Deserialize throws (poison). Valid JSON, but
        //     eventId is not a Guid ⇒ the typed deserialize fails — a stand-in for real payload corruption
        //     / a schema drift. The row stays selectable (event_type unchanged).
        await using var conn = factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "UPDATE events SET data = @data::jsonb WHERE event_id = @id", conn);
        cmd.Parameters.AddWithValue("id", eventId);
        cmd.Parameters.AddWithValue("data", """{"eventId":"not-a-guid","employeeId":"x","payoutDays":"xyz"}""");
        await cmd.ExecuteNonQueryAsync();
        return eventId;
    }

    public static async Task<int?> InboxAttemptsAsync(string connectionString, Guid eventId)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT attempts FROM settlement_payroll_inbox WHERE source_event_id = @id", conn);
        cmd.Parameters.AddWithValue("id", eventId);
        var v = await cmd.ExecuteScalarAsync();
        return v is int i ? i : (v is null ? (int?)null : Convert.ToInt32(v));
    }

    /// <summary>Counts the staged lines for the employee's settlement bucket (default the §24
    /// bucket at sequence 1 — the S69 call shape; S71 callers pass other buckets/sequences).</summary>
    public static async Task<long> LineCountAsync(
        string connectionString, string employeeId, int year = 2024, int sequence = 1,
        string bucket = AutoPayoutBucket)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM settlement_export_lines
            WHERE employee_id = @e AND entitlement_type = @t AND entitlement_year = @y
              AND sequence = @seq AND bucket = @b
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("t", VacationType);
        cmd.Parameters.AddWithValue("y", year);
        cmd.Parameters.AddWithValue("seq", sequence);
        cmd.Parameters.AddWithValue("b", bucket);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    /// <summary>Counts ALL staged lines for the employee (any bucket/sequence) — the no-orphan
    /// assertions.</summary>
    public static async Task<long> TotalLineCountAsync(string connectionString, string employeeId)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM settlement_export_lines WHERE employee_id = @e", conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    /// <summary>Reads the single staged line for the bucket (or null). Surfaces the money-free +
    /// wage-key columns + the S71 R8 reversal-shape columns the assertions check.</summary>
    public static async Task<StagedLine?> ReadLineAsync(
        string connectionString, string employeeId, int year = 2024, int sequence = 1,
        string bucket = AutoPayoutBucket)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT wage_type, hours, amount, ok_version, agreement_code, position, source_event_id, created_by,
                   line_kind, reverses_line_id, line_id, period_start, period_end
            FROM settlement_export_lines
            WHERE employee_id = @e AND entitlement_type = @t AND entitlement_year = @y
              AND sequence = @seq AND bucket = @b
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("t", VacationType);
        cmd.Parameters.AddWithValue("y", year);
        cmd.Parameters.AddWithValue("seq", sequence);
        cmd.Parameters.AddWithValue("b", bucket);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new StagedLine(
            reader.GetString(0), reader.GetDecimal(1), reader.GetDecimal(2),
            reader.GetString(3), reader.GetString(4), reader.GetString(5),
            reader.GetGuid(6), reader.GetString(7),
            reader.GetString(8), reader.IsDBNull(9) ? null : reader.GetInt64(9),
            reader.GetInt64(10),
            reader.GetFieldValue<DateOnly>(11), reader.GetFieldValue<DateOnly>(12));
    }

    public readonly record struct StagedLine(
        string WageType, decimal Hours, decimal Amount,
        string OkVersion, string AgreementCode, string Position, Guid SourceEventId, string CreatedBy,
        string LineKind, long? ReversesLineId, long LineId, DateOnly PeriodStart, DateOnly PeriodEnd);
}
