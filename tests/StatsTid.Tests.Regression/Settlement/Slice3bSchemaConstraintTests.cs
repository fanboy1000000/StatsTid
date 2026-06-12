using System.Text.Json;
using Npgsql;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Settlement;

/// <summary>
/// S71 / TASK-7100 — DB-level integrity floors for the slice-3b TERMINATION emission +
/// reversal schema (SPRINT-71 pinned rules R3/R5/R6/R8). Pattern precedent:
/// <see cref="SettlementSchemaConstraintTests"/> (S68). The services and endpoints built by
/// TASK-7102/7103/7104/7105 enforce these rules in code, but a malformed DIRECT write (a
/// future repo path, a migration, an operator query) must not be able to persist a
/// legally-impossible row. Proven here, legal + illegal direction for every new CHECK:
///
/// <list type="bullet">
///   <item><b>R3</b> — <c>vacation_settlements.bare_reversal_not_due</c> may be TRUE only on
///     REVERSED rows (<c>vacation_settlements_bare_reversal_reversed_only</c>); at most ONE
///     marker row per (employee, type, year) tuple
///     (<c>idx_vacation_settlements_bare_reversal_marker</c>, the S71 Step-5a W3 partial-unique
///     backstop) — while the CROSS-ROW half of R3 ("a marker never coexists with a later
///     active row") is deliberately FLOW-enforced in 3b, pinned as such below.</item>
///   <item><b>R5</b> — <c>review_disposition</c> admits MODREGNING/WAIVED
///     (<c>vacation_settlements_review_disposition</c>); resolved dispositions
///     (FORFEIT/MODREGNING/WAIVED) never coexist with PENDING_REVIEW, and a DEFER-marked
///     row CAN be REVERSED with its DEFER history preserved
///     (<c>vacation_settlements_disposition_state</c>); the §7/waiver resolved quantity
///     lives in <c>claim_disposition_days</c> — non-negative
///     (<c>vacation_settlements_claim_disposition_nonneg</c>) and present IFF the
///     disposition is MODREGNING/WAIVED
///     (<c>vacation_settlements_claim_disposition_paired</c> — never left in
///     <c>forfeit_days</c>, never dangling without a claim disposition).</item>
///   <item><b>R8</b> — <c>settlement_export_lines.line_kind</c> is ORIGINAL/REVERSAL
///     (<c>settlement_export_lines_line_kind</c>, DEFAULT ORIGINAL) and
///     <c>reverses_line_id</c> is non-null IFF the line is a REVERSAL
///     (<c>settlement_export_lines_reversal_pairing</c>), FK-bound to a real line
///     (<c>settlement_export_lines_reverses_line_fk</c>).</item>
///   <item><b>R6</b> — <c>termination_payout_requests</c> keys to the EXACT settlement row
///     (composite FK <c>termination_payout_requests_settlement_fk</c> onto
///     vacation_settlements' PK), carries a closed state enum
///     (<c>termination_payout_requests_state</c>), and admits at most ONE non-voided
///     request per settlement row (<c>idx_termination_payout_requests_nonvoided</c> —
///     VOIDED_BY_REVERSAL + a fresh OPEN is the D-E re-record path).</item>
/// </list>
///
/// <para>Postgres surfaces CHECK violations as SQLSTATE 23514, FK violations as 23503,
/// unique violations as 23505 (all via <see cref="PostgresException"/>).</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class Slice3bSchemaConstraintTests : IAsyncLifetime
{
    private const string OrgId = "STY01";
    private const string VacationType = "VACATION";
    private const string CheckViolation = "23514";
    private const string FkViolation = "23503";
    private const string UniqueViolation = "23505";
    private const string Payout24Bucket = "AUTO_PAYOUT_24";

    private TestFixtures.DockerHarness _harness = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
    }

    public async Task DisposeAsync()
    {
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ───────────────────────── R3 — bare-reversal marker ─────────────────────────

    [Fact]
    public async Task BareReversalMarker_TrueOnlyOnReversedRows()
    {
        var emp = await SeedEmployeeAsync();

        // Illegal: the marker on a non-REVERSED row (both non-REVERSED states).
        var exSettled = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertSettlementAsync(emp, year: 2021, sequence: 1, state: "SETTLED", bareMarker: true));
        Assert.Equal(CheckViolation, exSettled.SqlState);

        var exPending = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertSettlementAsync(emp, year: 2021, sequence: 1, state: "PENDING_REVIEW", bareMarker: true));
        Assert.Equal(CheckViolation, exPending.SqlState);

        // Legal: the marker on a REVERSED row (the R3 bare-reversal record).
        await InsertSettlementAsync(emp, year: 2021, sequence: 1, state: "REVERSED", bareMarker: true);
    }

    [Fact]
    public async Task BareReversalMarker_FalseIsLegalOnEveryState()
    {
        // The DEFAULT FALSE must remain legal everywhere (legacy rows + every
        // normal write). Distinct years so the single-active index never trips.
        var emp = await SeedEmployeeAsync();
        await InsertSettlementAsync(emp, year: 2021, sequence: 1, state: "SETTLED", bareMarker: false);
        await InsertSettlementAsync(emp, year: 2022, sequence: 1, state: "PENDING_REVIEW", bareMarker: false);
        await InsertSettlementAsync(emp, year: 2023, sequence: 1, state: "REVERSED", bareMarker: false);
    }

    /// <summary>W3 (S71 Step-5a cycle-1) — the one-marker-per-tuple DB backstop: a second
    /// marker row on the SAME tuple (a hypothetical gen-2 bare reversal also claiming to be
    /// the "latest" not-due record) trips the partial-unique index; marker-less REVERSED
    /// history and other tuples' markers are unaffected.</summary>
    [Fact]
    public async Task BareReversalMarker_AtMostOnePerTuple_23505()
    {
        var emp = await SeedEmployeeAsync();
        await InsertSettlementAsync(emp, year: 2021, sequence: 1, state: "REVERSED", bareMarker: true);

        var ex = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertSettlementAsync(emp, year: 2021, sequence: 3, state: "REVERSED", bareMarker: true));
        Assert.Equal(UniqueViolation, ex.SqlState);
        Assert.Equal("idx_vacation_settlements_bare_reversal_marker", ex.ConstraintName);

        // Marker-less REVERSED history coexists on the tuple; a different tuple's
        // marker is its own scope.
        await InsertSettlementAsync(emp, year: 2021, sequence: 3, state: "REVERSED", bareMarker: false);
        await InsertSettlementAsync(emp, year: 2022, sequence: 1, state: "REVERSED", bareMarker: true);
    }

    /// <summary>Documents the BOUNDARY of the W3 backstop (S71 Step-5a cycle-1): the CROSS-ROW
    /// half of R3 — "a marker never coexists with a LATER ACTIVE row" (in that state the
    /// Step-B anti-join would wrongly suppress a live tuple) — is FLOW-enforced in 3b, NOT
    /// DB-enforced: the marker is terminal (no 3b operation clears it), <c>SettleAsync</c>
    /// rejects on the in-lock marker probe, and <c>ResettleSupersedingAsync</c> exists only
    /// inside the reversal tx (which targets the active row, never a marked tuple) — so the
    /// combination is unreachable through 3b code paths, and the DB deliberately ADMITS it
    /// here. The REHIRE/recovery follow-up (marker-clearing + the R1 g+1 revival) owns the
    /// stronger backstop; when it lands, this pin must flip with it.</summary>
    [Fact]
    public async Task BareReversalMarker_CoexistingLaterActiveRow_AdmittedByDb_FlowEnforcedIn3b()
    {
        var emp = await SeedEmployeeAsync();
        await InsertSettlementAsync(emp, year: 2021, sequence: 1, state: "REVERSED", bareMarker: true);

        // The DB admits a later active row next to the marker — only the 3b service
        // flows make this state unreachable.
        await InsertSettlementAsync(emp, year: 2021, sequence: 3, state: "SETTLED", bareMarker: false);
    }

    // ───────────────────────── R5 — widened dispositions ─────────────────────────

    [Fact]
    public async Task ModregningAndWaived_AcceptedOnSettled_AndSurviveReversal()
    {
        var emp = await SeedEmployeeAsync();

        // The §7 deduct-in-full resolution (quantity recorded in its own column).
        await InsertSettlementAsync(emp, year: 2021, sequence: 1, state: "SETTLED",
            disposition: "MODREGNING", claimDays: 3.5m);

        // The waive-in-full resolution.
        await InsertSettlementAsync(emp, year: 2022, sequence: 1, state: "SETTLED",
            disposition: "WAIVED", claimDays: 2m);

        // Disposition history survives reversal (state <> PENDING_REVIEW admits REVERSED).
        await InsertSettlementAsync(emp, year: 2023, sequence: 1, state: "REVERSED",
            disposition: "MODREGNING", claimDays: 1.25m);
    }

    [Fact]
    public async Task ResolvedDispositions_CannotBePendingReview_23514()
    {
        // MODREGNING/WAIVED RESOLVED the review — like FORFEIT they can never
        // coexist with PENDING_REVIEW.
        var emp = await SeedEmployeeAsync();

        var exModregning = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertSettlementAsync(emp, year: 2021, sequence: 1, state: "PENDING_REVIEW",
                disposition: "MODREGNING", claimDays: 3.5m));
        Assert.Equal(CheckViolation, exModregning.SqlState);

        var exWaived = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertSettlementAsync(emp, year: 2021, sequence: 1, state: "PENDING_REVIEW",
                disposition: "WAIVED", claimDays: 2m));
        Assert.Equal(CheckViolation, exWaived.SqlState);
    }

    [Fact]
    public async Task DeferHistory_SurvivesReversal_ButNeverSettles()
    {
        var emp = await SeedEmployeeAsync();

        // S71 widening: a DEFER-marked row CAN be REVERSED — reversal must never
        // destroy the DEFER history (R5).
        await InsertSettlementAsync(emp, year: 2021, sequence: 1, state: "REVERSED",
            disposition: "DEFER");

        // Unchanged S68 rule: DEFER+SETTLED stays impossible (DEFER is unresolved).
        var ex = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertSettlementAsync(emp, year: 2022, sequence: 1, state: "SETTLED",
                disposition: "DEFER"));
        Assert.Equal(CheckViolation, ex.SqlState);
    }

    [Fact]
    public async Task UnknownDisposition_IsRejected_23514()
    {
        var emp = await SeedEmployeeAsync();
        var ex = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertSettlementAsync(emp, year: 2021, sequence: 1, state: "SETTLED",
                disposition: "PAID_EXTERNALLY"));
        Assert.Equal(CheckViolation, ex.SqlState);
    }

    // ───────────────────────── R5 — claim-quantity pairing ─────────────────────────

    [Fact]
    public async Task ClaimQuantity_RequiresModregningOrWaived_23514()
    {
        var emp = await SeedEmployeeAsync();

        // A quantity on a FORFEIT row would conflate the §7/waiver claim with §34
        // forfeiture — rejected.
        var exForfeit = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertSettlementAsync(emp, year: 2021, sequence: 1, state: "SETTLED",
                disposition: "FORFEIT", forfeit: 5m, claimDays: 5m));
        Assert.Equal(CheckViolation, exForfeit.SqlState);

        // A quantity with NO disposition is a dangling claim record — rejected.
        var exNull = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertSettlementAsync(emp, year: 2021, sequence: 1, state: "SETTLED",
                disposition: null, claimDays: 5m));
        Assert.Equal(CheckViolation, exNull.SqlState);

        // A quantity on a DEFER row — DEFER is unresolved, no claim quantity exists yet.
        var exDefer = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertSettlementAsync(emp, year: 2021, sequence: 1, state: "PENDING_REVIEW",
                disposition: "DEFER", claimDays: 5m));
        Assert.Equal(CheckViolation, exDefer.SqlState);
    }

    [Fact]
    public async Task ModregningOrWaived_RequireClaimQuantity_23514()
    {
        // The paired CHECK is bidirectional: a MODREGNING/WAIVED row WITHOUT its
        // resolved quantity would lose the legal record (the quantity must live in
        // claim_disposition_days, never implicitly elsewhere).
        var emp = await SeedEmployeeAsync();

        var exModregning = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertSettlementAsync(emp, year: 2021, sequence: 1, state: "SETTLED",
                disposition: "MODREGNING", claimDays: null));
        Assert.Equal(CheckViolation, exModregning.SqlState);

        var exWaived = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertSettlementAsync(emp, year: 2021, sequence: 1, state: "SETTLED",
                disposition: "WAIVED", claimDays: null));
        Assert.Equal(CheckViolation, exWaived.SqlState);
    }

    [Fact]
    public async Task ClaimQuantity_NonNegative_ZeroAccepted()
    {
        var emp = await SeedEmployeeAsync();

        var ex = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertSettlementAsync(emp, year: 2021, sequence: 1, state: "SETTLED",
                disposition: "MODREGNING", claimDays: -0.5m));
        Assert.Equal(CheckViolation, ex.SqlState);

        // Zero is a legal resolved quantity (>= 0, not > 0).
        await InsertSettlementAsync(emp, year: 2021, sequence: 1, state: "SETTLED",
            disposition: "WAIVED", claimDays: 0m);
    }

    // ───────────────────────── R8 — export-line reversal shape ─────────────────────────

    [Fact]
    public async Task ReversalPairing_PointerIffReversal()
    {
        var emp = await SeedEmployeeAsync();

        // Legal ORIGINAL (no pointer) at the odd R1 settlement-generation export sequence.
        var originalId = await InsertExportLineAsync(emp, sequence: 1, lineKind: "ORIGINAL", reversesLineId: null);

        // Legal REVERSAL pointing at the original, at the even R1 export sequence.
        await InsertExportLineAsync(emp, sequence: 2, lineKind: "REVERSAL", reversesLineId: originalId);

        // Illegal: an ORIGINAL carrying a pointer.
        var exOriginal = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertExportLineAsync(emp, sequence: 3, lineKind: "ORIGINAL", reversesLineId: originalId));
        Assert.Equal(CheckViolation, exOriginal.SqlState);

        // Illegal: a REVERSAL without a pointer (the compensated line must be unambiguous —
        // the source event id identifies the reversal EVENT, not the original LINE).
        var exReversal = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertExportLineAsync(emp, sequence: 3, lineKind: "REVERSAL", reversesLineId: null));
        Assert.Equal(CheckViolation, exReversal.SqlState);
    }

    [Fact]
    public async Task LineKind_DefaultsToOriginal_AndRejectsUnknownKind()
    {
        var emp = await SeedEmployeeAsync();

        // Omitting line_kind takes the DEFAULT — the same DEFAULT that backfills
        // legacy rows to ORIGINAL on the guarded ALTER path.
        var lineId = await InsertExportLineAsync(emp, sequence: 1, lineKind: null, reversesLineId: null);
        Assert.Equal("ORIGINAL", await ReadLineKindAsync(lineId));

        var ex = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertExportLineAsync(emp, sequence: 3, lineKind: "COMPENSATION", reversesLineId: null));
        Assert.Equal(CheckViolation, ex.SqlState);
    }

    [Fact]
    public async Task ReversalPointer_DanglingFk_IsRejected_23503()
    {
        var emp = await SeedEmployeeAsync();
        var ex = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertExportLineAsync(emp, sequence: 2, lineKind: "REVERSAL", reversesLineId: 999_999L));
        Assert.Equal(FkViolation, ex.SqlState);
    }

    // ───────────────────────── R6 — §26 payout requests ─────────────────────────

    [Fact]
    public async Task Request_KeysToExactSettlementRow_23503()
    {
        var emp = await SeedEmployeeAsync();
        await InsertSettlementAsync(emp, year: 2021, sequence: 1, state: "SETTLED", trigger: "TERMINATION");

        // Legal: OPEN request against the real settlement row.
        await InsertRequestAsync(emp, year: 2021, settlementSequence: 1, state: "OPEN");

        // Illegal: the bare year tuple is NOT enough — a request against a sequence
        // that does not exist must FK-fail (R2: requests key on the EXACT row).
        var exSequence = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertRequestAsync(emp, year: 2021, settlementSequence: 2, state: "OPEN"));
        Assert.Equal(FkViolation, exSequence.SqlState);

        // Illegal: no settlement row at all for the year.
        var exYear = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertRequestAsync(emp, year: 2022, settlementSequence: 1, state: "OPEN"));
        Assert.Equal(FkViolation, exYear.SqlState);
    }

    [Fact]
    public async Task Request_OneNonVoidedPerSettlementRow_23505()
    {
        var emp = await SeedEmployeeAsync();
        await InsertSettlementAsync(emp, year: 2021, sequence: 1, state: "SETTLED", trigger: "TERMINATION");
        await InsertSettlementAsync(emp, year: 2022, sequence: 1, state: "SETTLED", trigger: "TERMINATION");

        // OPEN blocks a second OPEN.
        await InsertRequestAsync(emp, year: 2021, settlementSequence: 1, state: "OPEN");
        var exOpen = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertRequestAsync(emp, year: 2021, settlementSequence: 1, state: "OPEN"));
        Assert.Equal(UniqueViolation, exOpen.SqlState);

        // LINE_STAGED is equally non-voided — it blocks a new OPEN too.
        await InsertRequestAsync(emp, year: 2022, settlementSequence: 1, state: "LINE_STAGED");
        var exStaged = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertRequestAsync(emp, year: 2022, settlementSequence: 1, state: "OPEN"));
        Assert.Equal(UniqueViolation, exStaged.SqlState);
    }

    [Fact]
    public async Task Request_VoidedThenNewOpen_IsAccepted()
    {
        // The D-E re-record path: reversal VOIDs the request; HR records a fresh one.
        // The voided history row coexists with the new live request.
        var emp = await SeedEmployeeAsync();
        await InsertSettlementAsync(emp, year: 2021, sequence: 1, state: "SETTLED", trigger: "TERMINATION");

        await InsertRequestAsync(emp, year: 2021, settlementSequence: 1, state: "VOIDED_BY_REVERSAL");
        await InsertRequestAsync(emp, year: 2021, settlementSequence: 1, state: "OPEN");

        Assert.Equal(2, await CountRequestsAsync(emp, year: 2021, settlementSequence: 1));
    }

    [Fact]
    public async Task Request_StateEnumAndVersionFloor_23514()
    {
        var emp = await SeedEmployeeAsync();
        await InsertSettlementAsync(emp, year: 2021, sequence: 1, state: "SETTLED", trigger: "TERMINATION");

        // Closed state enum — D-D explicitly deferred any external-payment variant.
        var exState = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertRequestAsync(emp, year: 2021, settlementSequence: 1, state: "CLOSED_PAID_EXTERNALLY"));
        Assert.Equal(CheckViolation, exState.SqlState);

        // version is a 1-based CAS counter.
        var exVersion = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertRequestAsync(emp, year: 2021, settlementSequence: 1, state: "OPEN", version: 0));
        Assert.Equal(CheckViolation, exVersion.SqlState);
    }

    // ─────────────────────────────── helpers ───────────────────────────────

    private async Task<string> SeedEmployeeAsync()
    {
        var employeeId = "emp_s71_chk_" + Guid.NewGuid().ToString("N")[..8];
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, OrgId, "AC", "OK24");
        return employeeId;
    }

    private async Task InsertSettlementAsync(
        string employeeId, int year, int sequence, string state,
        string trigger = "YEAR_END", string? disposition = null, decimal? claimDays = null,
        bool bareMarker = false, decimal transfer = 0m, decimal payout = 0m, decimal forfeit = 0m)
    {
        var snapshotJson = JsonSerializer.Serialize(new
        {
            recordedAbsences = Array.Empty<object>(),
            earned = 25m, used = 0m, planned = 0m, carryoverIn = 0m,
            annualQuota = 25m, carryoverMax = 5m, resetMonth = 9, okVersion = "OK24",
            transferAgreementDays = transfer, isFeriehindret = false,
        });

        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO vacation_settlements
                (employee_id, entitlement_type, entitlement_year, sequence,
                 settlement_state, trigger, snapshot, transfer_days, payout_days, forfeit_days,
                 review_disposition, claim_disposition_days, bare_reversal_not_due, version)
            VALUES
                (@e, @t, @year, @seq, @state, @trigger, @snapshot::jsonb, @transfer, @payout, @forfeit,
                 @disposition, @claimDays, @bareMarker, 1)
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("t", VacationType);
        cmd.Parameters.AddWithValue("year", year);
        cmd.Parameters.AddWithValue("seq", sequence);
        cmd.Parameters.AddWithValue("state", state);
        cmd.Parameters.AddWithValue("trigger", trigger);
        cmd.Parameters.AddWithValue("snapshot", snapshotJson);
        cmd.Parameters.AddWithValue("transfer", transfer);
        cmd.Parameters.AddWithValue("payout", payout);
        cmd.Parameters.AddWithValue("forfeit", forfeit);
        cmd.Parameters.AddWithValue("disposition", (object?)disposition ?? DBNull.Value);
        cmd.Parameters.AddWithValue("claimDays", (object?)claimDays ?? DBNull.Value);
        cmd.Parameters.AddWithValue("bareMarker", bareMarker);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Inserts an export line. <paramref name="lineKind"/> null = OMIT the column so the
    /// schema DEFAULT applies (the legacy-backfill path). Returns the new line_id.
    /// </summary>
    private async Task<long> InsertExportLineAsync(
        string employeeId, int sequence, string? lineKind, long? reversesLineId)
    {
        var lineKindColumn = lineKind is null ? "" : ", line_kind";
        var lineKindValue = lineKind is null ? "" : ", @lineKind";

        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            $"""
            INSERT INTO settlement_export_lines
                (employee_id, entitlement_type, entitlement_year, sequence, bucket,
                 wage_type, hours, amount, ok_version, agreement_code, position,
                 period_start, period_end, source_event_id, created_by, reverses_line_id{lineKindColumn})
            VALUES
                (@e, @t, 2021, @seq, @bucket, 'SLS_TBD_S24', 5.00, 0, 'OK24', 'AC', '',
                 '2021-09-01', '2022-08-31', @sourceEventId, 'test', @reversesLineId{lineKindValue})
            RETURNING line_id
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("t", VacationType);
        cmd.Parameters.AddWithValue("seq", sequence);
        cmd.Parameters.AddWithValue("bucket", Payout24Bucket);
        cmd.Parameters.AddWithValue("sourceEventId", Guid.NewGuid());
        cmd.Parameters.AddWithValue("reversesLineId", (object?)reversesLineId ?? DBNull.Value);
        if (lineKind is not null)
            cmd.Parameters.AddWithValue("lineKind", lineKind);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private async Task<string> ReadLineKindAsync(long lineId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT line_kind FROM settlement_export_lines WHERE line_id = @id", conn);
        cmd.Parameters.AddWithValue("id", lineId);
        return (string)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task InsertRequestAsync(
        string employeeId, int year, int settlementSequence, string state, long version = 1)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO termination_payout_requests
                (employee_id, entitlement_type, entitlement_year, settlement_sequence,
                 state, request_date, recorded_by, evidence_note, version)
            VALUES
                (@e, @t, @year, @seq, @state, '2021-10-01', 'hr001', 'test evidence', @version)
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("t", VacationType);
        cmd.Parameters.AddWithValue("year", year);
        cmd.Parameters.AddWithValue("seq", settlementSequence);
        cmd.Parameters.AddWithValue("state", state);
        cmd.Parameters.AddWithValue("version", version);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<int> CountRequestsAsync(string employeeId, int year, int settlementSequence)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM termination_payout_requests
            WHERE employee_id = @e AND entitlement_type = @t
              AND entitlement_year = @year AND settlement_sequence = @seq
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("t", VacationType);
        cmd.Parameters.AddWithValue("year", year);
        cmd.Parameters.AddWithValue("seq", settlementSequence);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }
}
