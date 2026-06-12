using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.Tests.Regression.Balance; // FixedTimeProvider
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Settlement;

/// <summary>
/// S71 / TASK-7104 (ADR-033 slice 3b; SPRINT-71 R1/R3/R4/R6/R12 + owner D-A/D-E) — Docker-gated
/// tests for the operator-authorized settlement REVERSAL service
/// (<see cref="SettlementReversalService"/>), driven service-level (the HTTP endpoint is
/// TASK-7102) with the PAT-008 <c>FixedTimeProvider</c> harness and the established
/// advisory-lock parking choreography (<see cref="SettlementCloseLeaverTests"/>).
///
/// <list type="bullet">
///   <item><description><b>Bare reversal (R3):</b> state-only REVERSED + the durable
///   <c>bare_reversal_not_due</c> marker; snapshot/buckets/disposition preserved; request
///   VOIDed (R6/D-E); <c>SettlementReversed</c> emitted (R10, BARE, operator actor) + ADR-026
///   audit; the tuple is NEVER re-enumerated by the LIVE close service — BOTH the leaver and
///   the ACTIVE enumeration branches (the ONE shared anti-join site) — and a direct stale
///   <c>SettleAsync</c> drive yields the benign NotDue (the in-lock R3 twin).</description></item>
///   <item><description><b>Reverse+supersede (R1/R4):</b> the subsumed end-date correction via
///   the shared <see cref="EmploymentEndDateLifecycleWriter"/> (two-aggregate preconditions),
///   trigger-specific in-tx eligibility, the superseding row at the R1 next-generation sequence
///   (3 after gen-1), SUPERSEDED kind + successor on the event; supersede-as-YEAR_END on a
///   REACTIVATED employee (cleared end date) re-runs the ACTIVE auto-partition.</description></item>
///   <item><description><b>Fail-closed guards:</b> D-A zero-bucket-only (a
///   <c>transfer_days &gt; 0</c> carryover-writer refuses), the R4 reconciled-row exclusion
///   (<c>payout_reconciled_at</c> — the durable S69 operator-reconcile marker), no-active-row,
///   stale-CAS; the B1 GENERATION binding (Step-5a cycle-1: equal versions across generations —
///   the ABA — refused on the sequence BEFORE any version comparison); the B2 affected-span
///   guard (the R13 analog: an end-date correction with ANOTHER active row in the
///   [min..max] ferieår span ⇒ FULL rollback naming the blockers); an INELIGIBLE
///   supersession ⇒ FULL rollback (NOTHING changed — not even the reversal; R4).</description></item>
///   <item><description><b>R12 races (parked-lock choreography):</b> double-reversal (the CAS
///   loser fails clean on the vanished active row) and reversal-vs-Step-B (a stale settle drive
///   parks, the bare reversal commits, the resumed settle sees the marker in-lock ⇒
///   NotDue).</description></item>
/// </list>
///
/// <para>Seeded VACATION config (host seeders): quota 25, MONTHLY_ACCRUAL, reset_month 9,
/// carryover_max 5. Fixed clock 2026-03-05; end date 2026-02-28 ⇒ R6 ferieår 2025, 6 whole
/// months ⇒ crystallized 12.5.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class SettlementReversalTests : IAsyncLifetime
{
    private const string OrgId = "STY01";
    private const string VacationType = "VACATION";
    private const string YearEnd = "YEAR_END";
    private const string Termination = "TERMINATION";
    private const string OperatorId = "hr_s71_reversal_op";
    private const string OperatorRole = "LocalHR";

    /// <summary>Fixed clock: Copenhagen 2026-03-05 (the S70 settlement-test convention).</summary>
    private static readonly DateOnly Clock = new(2026, 3, 5);

    /// <summary>The standard leaver end date — R6 ferieår 2025 (month 2 &lt; 9 ⇒ 2026 − 1).</summary>
    private static readonly DateOnly EndDate = new(2026, 2, 28);
    private const int EndDateFerieaar = 2025;

    /// <summary>A go-live before the standard end date and before ferieår 2024's boundary
    /// (31 Dec 2025) — both the TERMINATION and the prior-year YEAR_END legs are live.</summary>
    private static readonly DateOnly NarrowGoLive = new(2025, 1, 1);

    /// <summary>Go-live before every candidate boundary (the boundary-tests convention).</summary>
    private static readonly DateOnly BroadGoLive = new(2020, 1, 1);

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════
    // R3 + R6 + R10 — bare reversal: state-only REVERSED + marker; snapshot/buckets preserved;
    // request VOIDed; SettlementReversed (BARE, operator actor) + ADR-026 audit, one tx.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task BareReverse_Termination_MarkerSet_StateOnly_RequestVoided_EventEmitted()
    {
        var (settle, reversal) = BootFixedClockServices(Clock);
        var employeeId = await SeedEmployeeAsync();
        await MarkLeaverAsync(employeeId, EndDate);
        var settled = await SettleInOwnTxAsync(settle, employeeId, EndDateFerieaar, Termination);
        Assert.True(settled.DidSettle);
        var before = await ReadRowAsync(employeeId, EndDateFerieaar, sequence: 1);
        await SeedOpenRequestAsync(employeeId, EndDateFerieaar, settlementSequence: 1);

        var result = await reversal.ReverseAsync(BareCommand(employeeId, EndDateFerieaar, expectedVersion: 1));

        Assert.True(result.Succeeded);
        Assert.Equal(SettlementReversalFailure.None, result.Failure);
        Assert.NotNull(result.ReversedRow);
        Assert.True(result.ReversedRow!.BareReversalNotDue);
        Assert.Null(result.SupersedingRow);
        Assert.Equal(1, result.VoidedRequestIds.Count);

        // The row: STATE-ONLY transition — same sequence, marker set, version bumped, snapshot
        // BYTE-IDENTICAL, buckets and disposition untouched (the 7100 CHECKs admit the combo).
        var after = await ReadRowAsync(employeeId, EndDateFerieaar, sequence: 1);
        Assert.Equal("REVERSED", after.State);
        Assert.True(after.BareMarker);
        Assert.Equal(2L, after.Version);
        Assert.Equal(before.SnapshotJson, after.SnapshotJson);
        Assert.Equal(before.Transfer, after.Transfer);
        Assert.Equal(before.Payout, after.Payout);
        Assert.Equal(before.Forfeit, after.Forfeit);
        Assert.Equal(Termination, after.Trigger);
        // No new sequence was allocated (R10: a reversal allocates NO row sequence).
        Assert.Equal(1L, await CountAsync(
            "vacation_settlements", "employee_id = @e", ("e", employeeId)));

        // R6/D-E — the bound OPEN request was VOIDed in the same tx.
        var request = await ReadRequestAsync(employeeId, EndDateFerieaar, 1);
        Assert.Equal("VOIDED_BY_REVERSAL", request!.Value.State);
        Assert.Equal(2L, request.Value.Version);

        // R10 — SettlementReversed: BARE kind, no successor, the REVERSED row's sequence, the
        // TERMINATION crystallized day-count (snapshot-sourced), the OPERATOR as actor.
        var payload = await ReadLatestOutboxPayloadAsync($"employee-{employeeId}", "SettlementReversed");
        Assert.NotNull(payload);
        using (var doc = JsonDocument.Parse(payload!))
        {
            var root = doc.RootElement;
            Assert.Equal("BARE", root.GetProperty("reversalKind").GetString());
            Assert.Equal(1, root.GetProperty("settlementSequence").GetInt32());
            Assert.True(!root.TryGetProperty("successorSequence", out var succ)
                        || succ.ValueKind == JsonValueKind.Null);
            Assert.Equal(Termination, root.GetProperty("trigger").GetString());
            Assert.Equal(12.5m, root.GetProperty("crystallizedDays").GetDecimal());
            Assert.Equal(OperatorId, root.GetProperty("actorId").GetString());
            Assert.Equal(OperatorRole, root.GetProperty("actorRole").GetString());
        }

        // ADR-026 audit projection + the settlement-table UPDATED audit row, operator actor.
        Assert.Equal(1L, await CountAsync(
            "audit_projection",
            "event_type = 'SettlementReversed' AND target_resource_id = @r AND target_org_id = @o",
            ("r", employeeId), ("o", OrgId)));
        Assert.Equal(1L, await CountAsync(
            "vacation_settlement_audit",
            "employee_id = @e AND action = 'UPDATED' AND version_before = 1 AND version_after = 2 " +
            "AND actor_id = @a",
            ("e", employeeId), ("a", OperatorId)));
    }

    // ════════════════════════════════════════════════════════════════════════
    // R3 — a bare-reversed tuple is NEVER re-enumerated: the LIVE close service's shared
    // anti-join (BOTH branches) + the in-lock SettleAsync rejection twin.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task BareReversedTuple_NeverReEnumerated_BothBranches_AndSettleAsyncRejects()
    {
        var (settle, reversal) = BootFixedClockServices(Clock);

        // LEAVER-branch subject: a bare-reversed TERMINATION tuple.
        var leaver = await SeedEmployeeAsync();
        await MarkLeaverAsync(leaver, EndDate);
        await SettleInOwnTxAsync(settle, leaver, EndDateFerieaar, Termination);
        Assert.True((await reversal.ReverseAsync(BareCommand(leaver, EndDateFerieaar, 1))).Succeeded);

        // ACTIVE-branch subject: a bare-reversed zero-transfer auto-partition tuple (D-A
        // reversible: transfer_days = 0 — no §21 agreement seeded; payout 5 / forfeit-flag 20).
        var activeEmp = await SeedEmployeeAsync();
        await SettleInOwnTxAsync(settle, activeEmp, 2024, YearEnd);
        Assert.True((await reversal.ReverseAsync(BareCommand(activeEmp, 2024, 1))).Succeeded);

        // ── LEAVER branch: a gated poll (go-live 2026-01-01 ⇒ the ONLY due tuple shape is a
        // TERMINATION with end date after go-live) must not re-enumerate the marked tuple.
        var leaverWitness = await SeedEmployeeAsync();
        await SetEndDateAsync(leaverWitness, EndDate); // active; Step A flips, Step B settles
        BootFixedClockHost(Clock, new DateOnly(2026, 1, 1));
        Assert.True(await WaitForSettlementAsync(leaverWitness, EndDateFerieaar, TimeSpan.FromSeconds(30)),
            "the leaver witness must settle — proves the leaver branch ran on this poll.");
        Assert.Equal(1L, await CountAsync(
            "vacation_settlements", "employee_id = @e AND entitlement_year = @y",
            ("e", leaver), ("y", EndDateFerieaar)));
        Assert.Equal("REVERSED", (await ReadRowAsync(leaver, EndDateFerieaar, 1)).State);
        Assert.Equal(1L, await CountOutboxByTypeAsync(leaver, "TerminationSettled")); // only the original

        // ── ACTIVE branch: a broad-gated poll must not re-enumerate the marked 2024 tuple
        // (other due years of activeEmp legitimately settle — the assert is year-scoped).
        var activeWitness = await SeedEmployeeAsync();
        BootFixedClockHost(Clock, BroadGoLive);
        Assert.True(await WaitForSettlementAsync(activeWitness, 2024, TimeSpan.FromSeconds(30)),
            "the active witness must settle 2024 — proves the ACTIVE branch ran on this poll.");
        Assert.Equal(1L, await CountAsync(
            "vacation_settlements", "employee_id = @e AND entitlement_year = 2024", ("e", activeEmp)));
        Assert.Equal("REVERSED", (await ReadRowAsync(activeEmp, 2024, 1)).State);

        // ── The in-lock twin (R3 "SettleAsync rejects"): a direct stale drive at the marked
        // tuple yields the benign NotDue — no row, no event, no 23505 against the REVERSED PK.
        var outcome = await SettleInOwnTxAsync(settle, leaver, EndDateFerieaar, Termination, NarrowGoLive);
        Assert.False(outcome.DidSettle);
        Assert.True(outcome.NotDue);
        Assert.Null(outcome.Row);
        Assert.Equal(1L, await CountAsync(
            "vacation_settlements", "employee_id = @e AND entitlement_year = @y",
            ("e", leaver), ("y", EndDateFerieaar)));
    }

    // ════════════════════════════════════════════════════════════════════════
    // R1 + R4 — reverse+supersede TERMINATION with the subsumed end-date correction: old row
    // REVERSED (snapshot intact), successor at sequence 3 (next generation), request VOIDed,
    // SUPERSEDED kind + successor on the event, the lifecycle write audited via the shared writer.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ReverseSupersede_Termination_CorrectedEndDate_SuccessorAtSequenceThree()
    {
        var (settle, reversal) = BootFixedClockServices(Clock);
        var employeeId = await SeedEmployeeAsync();
        await MarkLeaverAsync(employeeId, EndDate); // users.version stays 1 (direct seed)
        await SettleInOwnTxAsync(settle, employeeId, EndDateFerieaar, Termination);
        var before = await ReadRowAsync(employeeId, EndDateFerieaar, 1);
        await SeedOpenRequestAsync(employeeId, EndDateFerieaar, 1);

        // Corrected end date 2025-12-31: SAME ferieår (2025), still passed at Clock ⇒ the user
        // stays lifecycle-deactivated and the supersession re-crystallizes as TERMINATION.
        var correctedEndDate = new DateOnly(2025, 12, 31);
        var result = await reversal.ReverseAsync(new SettlementReversalCommand
        {
            EmployeeId = employeeId,
            EntitlementType = VacationType,
            EntitlementYear = EndDateFerieaar,
            ExpectedSettlementSequence = 1,
            ExpectedSettlementVersion = 1,
            Mode = SettlementReversalMode.ReverseAndSupersede,
            HasEndDateCorrection = true,
            CorrectedEndDate = correctedEndDate,
            ExpectedUserVersion = 1,
            SupersedeGoLiveFloor = NarrowGoLive,
            ActorId = OperatorId,
            ActorRole = OperatorRole,
            ActorOrgId = OrgId,
        });

        Assert.True(result.Succeeded);
        Assert.NotNull(result.SupersedingRow);
        Assert.Equal(3, result.SupersedingRow!.Sequence); // R1: gen 2 ⇒ row sequence 2·2−1 = 3
        Assert.Equal(2L, result.UserVersionAfter);
        Assert.False(result.UserIsActiveAfter!.Value);    // still-passed date ⇒ stays deactivated
        Assert.Equal(1, result.VoidedRequestIds.Count);

        // The old row: REVERSED, NO bare marker (a successor exists), snapshot byte-intact.
        var oldRow = await ReadRowAsync(employeeId, EndDateFerieaar, 1);
        Assert.Equal("REVERSED", oldRow.State);
        Assert.False(oldRow.BareMarker);
        Assert.Equal(before.SnapshotJson, oldRow.SnapshotJson);

        // The successor: TERMINATION SETTLED at sequence 3, re-crystallized off the CORRECTED
        // end date — 4 whole months (Sep..Dec) ⇒ 25 × 4/12 = 8.33 (Round2 ToEven).
        var newRow = await ReadRowAsync(employeeId, EndDateFerieaar, 3);
        Assert.Equal(Termination, newRow.Trigger);
        Assert.Equal("SETTLED", newRow.State);
        Assert.Equal(0m, newRow.Transfer);
        Assert.Equal(0m, newRow.Payout);
        Assert.Equal(0m, newRow.Forfeit);
        using (var snap = JsonDocument.Parse(newRow.SnapshotJson))
        {
            Assert.Equal(8.33m, snap.RootElement.GetProperty("crystallizedDays").GetDecimal());
            Assert.Equal(correctedEndDate.ToString("yyyy-MM-dd"),
                snap.RootElement.GetProperty("terminationDate").GetString());
        }

        // The corrected user tuple (the shared lifecycle writer's versioned write + R10 event).
        var user = await ReadUserTupleAsync(employeeId);
        Assert.Equal(correctedEndDate, user.EndDate);
        Assert.False(user.IsActive);
        Assert.True(user.EndDateDeactivated);
        Assert.Equal(2L, user.Version);
        var endDateEvent = await ReadLatestOutboxPayloadAsync(
            $"employee-{employeeId}", "EmployeeEmploymentEndDateSet");
        Assert.NotNull(endDateEvent);
        using (var doc = JsonDocument.Parse(endDateEvent!))
        {
            Assert.Equal(EndDate.ToString("yyyy-MM-dd"), doc.RootElement.GetProperty("oldEndDate").GetString());
            Assert.Equal(correctedEndDate.ToString("yyyy-MM-dd"), doc.RootElement.GetProperty("newEndDate").GetString());
            Assert.Equal(OperatorId, doc.RootElement.GetProperty("actorId").GetString());
        }

        // R6/D-E — the request is VOIDed (HR re-records against sequence 3 via 7102).
        Assert.Equal("VOIDED_BY_REVERSAL", (await ReadRequestAsync(employeeId, EndDateFerieaar, 1))!.Value.State);

        // R10 — SUPERSEDED kind + the successor's sequence; the successor emitted its own
        // TerminationSettled (sequence 3).
        var payload = await ReadLatestOutboxPayloadAsync($"employee-{employeeId}", "SettlementReversed");
        using (var doc = JsonDocument.Parse(payload!))
        {
            Assert.Equal("SUPERSEDED", doc.RootElement.GetProperty("reversalKind").GetString());
            Assert.Equal(3, doc.RootElement.GetProperty("successorSequence").GetInt32());
            Assert.Equal(1, doc.RootElement.GetProperty("settlementSequence").GetInt32());
            Assert.Equal(12.5m, doc.RootElement.GetProperty("crystallizedDays").GetDecimal());
        }
        Assert.Equal(2L, await CountOutboxByTypeAsync(employeeId, "TerminationSettled"));
    }

    // ════════════════════════════════════════════════════════════════════════
    // R4 — supersede-as-YEAR_END: a reversed leaver-deferred row on a REACTIVATED employee
    // (corrected end date = CLEAR ⇒ the R1(c) provenance-guarded reactivation) re-runs the
    // ACTIVE §21/§24 auto-partition at the next-generation sequence.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ReverseSupersede_YearEnd_ReactivatedEmployee_AutoPartitionAtSequenceThree()
    {
        var (settle, reversal) = BootFixedClockServices(Clock);
        var employeeId = await SeedEmployeeAsync();
        await MarkLeaverAsync(employeeId, EndDate);
        // The leaver's prior due ferieår (2024) → the fail-closed deferred-disposition row.
        var deferred = await SettleInOwnTxAsync(settle, employeeId, 2024, YearEnd, NarrowGoLive);
        Assert.True(deferred.DidSettle);
        Assert.Equal("PENDING_REVIEW", deferred.Row!.SettlementState);

        // Reverse+supersede with the correction CLEAR (null WITH the flag): R1(c) reactivates
        // the lifecycle-deactivated row; the supersession then runs the ACTIVE YEAR_END
        // auto-partition (boundary 31 Dec 2025 passed and after the go-live floor).
        var result = await reversal.ReverseAsync(new SettlementReversalCommand
        {
            EmployeeId = employeeId,
            EntitlementType = VacationType,
            EntitlementYear = 2024,
            ExpectedSettlementSequence = 1,
            ExpectedSettlementVersion = 1,
            Mode = SettlementReversalMode.ReverseAndSupersede,
            HasEndDateCorrection = true,
            CorrectedEndDate = null, // clear
            ExpectedUserVersion = 1,
            SupersedeGoLiveFloor = NarrowGoLive,
            ActorId = OperatorId,
            ActorRole = OperatorRole,
            ActorOrgId = OrgId,
        });

        Assert.True(result.Succeeded);
        Assert.True(result.UserIsActiveAfter!.Value);

        // The user is reactivated with no end date (R1(c)).
        var user = await ReadUserTupleAsync(employeeId);
        Assert.True(user.IsActive);
        Assert.False(user.EndDateDeactivated);
        Assert.Null(user.EndDate);

        // The old deferred row: REVERSED with its DeferredDisposition snapshot intact.
        var oldRow = await ReadRowAsync(employeeId, 2024, 1);
        Assert.Equal("REVERSED", oldRow.State);
        Assert.False(oldRow.BareMarker);
        using (var snap = JsonDocument.Parse(oldRow.SnapshotJson))
            Assert.True(snap.RootElement.GetProperty("deferredDisposition").GetBoolean());

        // The successor: the ACTIVE auto-partition at sequence 3 — quota 25, no consumption, no
        // §21 agreement ⇒ underCap 5 → payout 5, overCap 20 → forfeit-flag (PENDING_REVIEW).
        var newRow = await ReadRowAsync(employeeId, 2024, 3);
        Assert.Equal(YearEnd, newRow.Trigger);
        Assert.Equal("PENDING_REVIEW", newRow.State);
        Assert.Equal(0m, newRow.Transfer);
        Assert.Equal(5m, newRow.Payout);
        Assert.Equal(20m, newRow.Forfeit);
        using (var snap = JsonDocument.Parse(newRow.SnapshotJson))
            Assert.False(snap.RootElement.TryGetProperty("deferredDisposition", out var dd) && dd.GetBoolean());

        // The successor's own §24 event rides the new sequence; zero transfer ⇒ no carryover.
        var payout = await ReadLatestOutboxPayloadAsync($"employee-{employeeId}", "VacationAutoPaidOut");
        Assert.NotNull(payout);
        using (var doc = JsonDocument.Parse(payout!))
            Assert.Equal(3, doc.RootElement.GetProperty("sequence").GetInt32());
        Assert.Equal(0L, await CountOutboxByTypeAsync(employeeId, "VacationCarryoverExecuted"));
        Assert.Null(await ReadCarryoverInAsync(employeeId, 2025));

        var payload = await ReadLatestOutboxPayloadAsync($"employee-{employeeId}", "SettlementReversed");
        using (var doc = JsonDocument.Parse(payload!))
        {
            Assert.Equal("SUPERSEDED", doc.RootElement.GetProperty("reversalKind").GetString());
            Assert.Equal(3, doc.RootElement.GetProperty("successorSequence").GetInt32());
            Assert.Equal(YearEnd, doc.RootElement.GetProperty("trigger").GetString());
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // R4 — failed-leg FULL rollback: an INELIGIBLE supersession (corrected to a future date in
    // ANOTHER ferieår ⇒ reactivated user, tuple boundary not passed) rolls back EVERYTHING —
    // no reversal, no lifecycle write, no VOID, no event. The original row stands.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ReverseSupersede_IneligibleCorrectedState_FullRollback_NothingChanged()
    {
        var (settle, reversal) = BootFixedClockServices(Clock);
        var employeeId = await SeedEmployeeAsync();
        await MarkLeaverAsync(employeeId, EndDate);
        await SettleInOwnTxAsync(settle, employeeId, EndDateFerieaar, Termination);
        await SeedOpenRequestAsync(employeeId, EndDateFerieaar, 1);

        // 2026-09-15: a FUTURE date in ferieår 2026 ⇒ the lifecycle correction REACTIVATES the
        // user (in-tx), the TERMINATION predicate fails (active + ferieår mismatch) and the
        // YEAR_END predicate fails (ferieår 2025's boundary 31 Dec 2026 has not passed) ⇒ the
        // supersession leg fails ⇒ R4 FULL rollback.
        var result = await reversal.ReverseAsync(new SettlementReversalCommand
        {
            EmployeeId = employeeId,
            EntitlementType = VacationType,
            EntitlementYear = EndDateFerieaar,
            ExpectedSettlementSequence = 1,
            ExpectedSettlementVersion = 1,
            Mode = SettlementReversalMode.ReverseAndSupersede,
            HasEndDateCorrection = true,
            CorrectedEndDate = new DateOnly(2026, 9, 15),
            ExpectedUserVersion = 1,
            SupersedeGoLiveFloor = NarrowGoLive,
            ActorId = OperatorId,
            ActorRole = OperatorRole,
            ActorOrgId = OrgId,
        });

        Assert.False(result.Succeeded);
        Assert.Equal(SettlementReversalFailure.SupersedeNotEligible, result.Failure);

        // NOTHING changed: the settlement row, the user tuple, the request and the outbox are
        // all exactly as before the call.
        var row = await ReadRowAsync(employeeId, EndDateFerieaar, 1);
        Assert.Equal("SETTLED", row.State);
        Assert.False(row.BareMarker);
        Assert.Equal(1L, row.Version);
        Assert.Equal(1L, await CountAsync(
            "vacation_settlements", "employee_id = @e", ("e", employeeId)));

        var user = await ReadUserTupleAsync(employeeId);
        Assert.Equal(EndDate, user.EndDate);
        Assert.False(user.IsActive);
        Assert.True(user.EndDateDeactivated);
        Assert.Equal(1L, user.Version);

        Assert.Equal("OPEN", (await ReadRequestAsync(employeeId, EndDateFerieaar, 1))!.Value.State);
        Assert.Equal(0L, await CountOutboxByTypeAsync(employeeId, "SettlementReversed"));
        Assert.Equal(0L, await CountOutboxByTypeAsync(employeeId, "EmployeeEmploymentEndDateSet"));
        Assert.Equal(0L, await CountAsync(
            "vacation_settlement_audit", "employee_id = @e AND actor_id = @a",
            ("e", employeeId), ("a", OperatorId)));
    }

    // ════════════════════════════════════════════════════════════════════════
    // B2 (Step-5a cycle-1) — the affected-span settlement guard INSIDE the reversal tx (the
    // R13 analog; R4/R12): active rows in ferieår 2024 AND 2025; reversing the 2024 row with
    // an end-date correction whose [min..max] ferieår span covers 2025 must FULL-rollback —
    // the active 2025 row would otherwise remain standing on superseded lifecycle facts.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ReverseSupersede_OtherActiveRowInCorrectionSpan_FullRollback()
    {
        var (settle, reversal) = BootFixedClockServices(Clock);
        var employeeId = await SeedEmployeeAsync();
        await MarkLeaverAsync(employeeId, EndDate); // ferieår(old end date 2026-02-28) = 2025

        // Active row #1 — ferieår 2024: the leaver-deferred PENDING_REVIEW row (the target).
        var deferred = await SettleInOwnTxAsync(settle, employeeId, 2024, YearEnd, NarrowGoLive);
        Assert.True(deferred.DidSettle);
        // Active row #2 — ferieår 2025: the TERMINATION crystallization (the blocker).
        var termination = await SettleInOwnTxAsync(settle, employeeId, EndDateFerieaar, Termination);
        Assert.True(termination.DidSettle);
        await SeedOpenRequestAsync(employeeId, 2024, settlementSequence: 1);

        // Corrected end date 2025-06-30 → ferieår 2024; old 2026-02-28 → ferieår 2025 ⇒ the
        // R13 span [2024..2025]. The just-reversed 2024 target is self-excluded (REVERSED
        // in-tx); the ACTIVE 2025 TERMINATION row blocks ⇒ FULL rollback, the blocker NAMED.
        var result = await reversal.ReverseAsync(new SettlementReversalCommand
        {
            EmployeeId = employeeId,
            EntitlementType = VacationType,
            EntitlementYear = 2024,
            ExpectedSettlementSequence = 1,
            ExpectedSettlementVersion = 1,
            Mode = SettlementReversalMode.ReverseAndSupersede,
            HasEndDateCorrection = true,
            CorrectedEndDate = new DateOnly(2025, 6, 30),
            ExpectedUserVersion = 1,
            SupersedeGoLiveFloor = NarrowGoLive,
            ActorId = OperatorId,
            ActorRole = OperatorRole,
            ActorOrgId = OrgId,
        });

        Assert.False(result.Succeeded);
        Assert.Equal(SettlementReversalFailure.AffectedSpanConflict, result.Failure);
        Assert.Contains("VACATION/2025 sequence 1 (SETTLED)", result.FailureReason!); // blocker NAMED

        // FULL rollback — the 2024 target row is STILL ACTIVE (the in-tx CAS rolled back) …
        var target = await ReadRowAsync(employeeId, 2024, 1);
        Assert.Equal("PENDING_REVIEW", target.State);
        Assert.False(target.BareMarker);
        Assert.Equal(1L, target.Version);

        // … the 2025 blocker is untouched …
        var blocker = await ReadRowAsync(employeeId, EndDateFerieaar, 1);
        Assert.Equal("SETTLED", blocker.State);
        Assert.Equal(1L, blocker.Version);

        // … the user lifecycle is unchanged …
        var user = await ReadUserTupleAsync(employeeId);
        Assert.Equal(EndDate, user.EndDate);
        Assert.False(user.IsActive);
        Assert.True(user.EndDateDeactivated);
        Assert.Equal(1L, user.Version);

        // … and nothing else escaped the rollback: the request VOID (step 6 ran BEFORE the
        // guard) rolled back to OPEN; no events; no operator audit row; no successor row.
        Assert.Equal("OPEN", (await ReadRequestAsync(employeeId, 2024, 1))!.Value.State);
        Assert.Equal(0L, await CountOutboxByTypeAsync(employeeId, "SettlementReversed"));
        Assert.Equal(0L, await CountOutboxByTypeAsync(employeeId, "EmployeeEmploymentEndDateSet"));
        Assert.Equal(0L, await CountAsync(
            "vacation_settlement_audit", "employee_id = @e AND actor_id = @a",
            ("e", employeeId), ("a", OperatorId)));
        Assert.Equal(2L, await CountAsync(
            "vacation_settlements", "employee_id = @e", ("e", employeeId)));
    }

    // ════════════════════════════════════════════════════════════════════════
    // D-A — zero-bucket-only: a carryover-WRITING row (transfer_days > 0) refuses, loudly.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ZeroBucketGuard_CarryoverWritingRow_Refused_RowUntouched()
    {
        var (settle, reversal) = BootFixedClockServices(Clock);
        var employeeId = await SeedEmployeeAsync();
        await SeedTransferAgreementAsync(employeeId, 2024, transferDays: 3m);
        var settled = await SettleInOwnTxAsync(settle, employeeId, 2024, YearEnd);
        Assert.Equal(3m, settled.Row!.TransferDays); // the §21 carryover writer
        Assert.Equal(3m, await ReadCarryoverInAsync(employeeId, 2025));

        var result = await reversal.ReverseAsync(BareCommand(employeeId, 2024, 1));

        Assert.False(result.Succeeded);
        Assert.Equal(SettlementReversalFailure.CarryoverWritingRow, result.Failure);

        var row = await ReadRowAsync(employeeId, 2024, 1);
        Assert.NotEqual("REVERSED", row.State);
        Assert.False(row.BareMarker);
        Assert.Equal(1L, row.Version);
        Assert.Equal(3m, await ReadCarryoverInAsync(employeeId, 2025)); // carryover untouched
        Assert.Equal(0L, await CountOutboxByTypeAsync(employeeId, "SettlementReversed"));
    }

    // ════════════════════════════════════════════════════════════════════════
    // R4 — reconciled-row exclusion: an operator-reconciled §24 disposition
    // (payout_reconciled_at — the durable S69 marker) is NOT reversible in 3b.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ReconciledRow_Refused_RowUntouched()
    {
        var (settle, reversal) = BootFixedClockServices(Clock);
        var employeeId = await SeedEmployeeAsync();
        await SettleInOwnTxAsync(settle, employeeId, 2024, YearEnd); // transfer 0 ⇒ D-A passes
        await ExecAsync(
            """
            UPDATE vacation_settlements
               SET payout_reconciled_at = NOW(), payout_reconciled_by = 'hr_s69_reconciler'
             WHERE employee_id = @e AND entitlement_type = @t AND entitlement_year = 2024 AND sequence = 1
            """, ("e", employeeId), ("t", VacationType));

        var result = await reversal.ReverseAsync(BareCommand(employeeId, 2024, 1));

        Assert.False(result.Succeeded);
        Assert.Equal(SettlementReversalFailure.ReconciledRow, result.Failure);

        var row = await ReadRowAsync(employeeId, 2024, 1);
        Assert.NotEqual("REVERSED", row.State);
        Assert.False(row.BareMarker);
        Assert.Equal(1L, row.Version);
        Assert.Equal(0L, await CountOutboxByTypeAsync(employeeId, "SettlementReversed"));
    }

    // ════════════════════════════════════════════════════════════════════════
    // R5/7100 — PENDING_REVIEW → REVERSED is legal, with DEFER history PRESERVED.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PendingReview_DeferMarked_BareReversal_DeferPreserved()
    {
        var (settle, reversal) = BootFixedClockServices(Clock);
        var employeeId = await SeedEmployeeAsync();
        await MarkLeaverAsync(employeeId, EndDate);
        // The leaver-deferred PENDING_REVIEW row, then the committed DEFER outcome (the resolve
        // endpoint's CAS shape, seeded directly — version 2).
        await SettleInOwnTxAsync(settle, employeeId, 2024, YearEnd, NarrowGoLive);
        await ExecAsync(
            """
            UPDATE vacation_settlements
               SET review_disposition = 'DEFER', version = version + 1, updated_at = NOW()
             WHERE employee_id = @e AND entitlement_type = @t AND entitlement_year = 2024 AND sequence = 1
            """, ("e", employeeId), ("t", VacationType));
        var before = await ReadRowAsync(employeeId, 2024, 1);
        Assert.Equal("PENDING_REVIEW", before.State);

        var result = await reversal.ReverseAsync(BareCommand(employeeId, 2024, expectedVersion: 2));

        Assert.True(result.Succeeded);
        var after = await ReadRowAsync(employeeId, 2024, 1);
        Assert.Equal("REVERSED", after.State);             // PENDING_REVIEW → REVERSED (widened CHECK)
        Assert.Equal("DEFER", after.ReviewDisposition);    // DEFER history preserved (R5)
        Assert.True(after.BareMarker);
        Assert.Equal(3L, after.Version);
        Assert.Equal(before.SnapshotJson, after.SnapshotJson);
        Assert.Equal(before.Forfeit, after.Forfeit);       // the deferred-disposition flag preserved
    }

    // ════════════════════════════════════════════════════════════════════════
    // ADR-019 — a stale If-Match loses the CAS cleanly (the row is untouched).
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task StaleIfMatch_CasConflict_FailsClean()
    {
        var (settle, reversal) = BootFixedClockServices(Clock);
        var employeeId = await SeedEmployeeAsync();
        await MarkLeaverAsync(employeeId, EndDate);
        await SettleInOwnTxAsync(settle, employeeId, EndDateFerieaar, Termination);

        var result = await reversal.ReverseAsync(BareCommand(employeeId, EndDateFerieaar, expectedVersion: 99));

        Assert.False(result.Succeeded);
        Assert.Equal(SettlementReversalFailure.CasConflict, result.Failure);
        Assert.Equal(1L, result.ActualSettlementVersion);

        var row = await ReadRowAsync(employeeId, EndDateFerieaar, 1);
        Assert.Equal("SETTLED", row.State);
        Assert.Equal(1L, row.Version);
        Assert.Equal(0L, await CountOutboxByTypeAsync(employeeId, "SettlementReversed"));
    }

    // ════════════════════════════════════════════════════════════════════════
    // B1 (Step-5a cycle-1) / R2 — the ABA across generations: settlement-row versions restart
    // at 1 per generation, so a stale command built against gen-1 (sequence 1, version 1)
    // carries a version that MATCHES the gen-2 successor (sequence 3, version 1). The
    // generation binding must refuse on the SEQUENCE — BEFORE any version comparison —
    // otherwise the CAS silently reverses the WRONG (current) row.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task StaleGeneration_EqualVersionsAcrossGenerations_RefusedOnSequence()
    {
        var (settle, reversal) = BootFixedClockServices(Clock);
        var employeeId = await SeedEmployeeAsync();
        await MarkLeaverAsync(employeeId, EndDate);
        await SettleInOwnTxAsync(settle, employeeId, EndDateFerieaar, Termination);
        // A stale operator reads gen-1 here: (sequence 1, version 1).

        // Meanwhile ANOTHER operator reverse+supersedes (the same-ferieår correction
        // choreography) → the gen-2 active row at sequence 3, version 1 — the ABA setup.
        var first = await reversal.ReverseAsync(new SettlementReversalCommand
        {
            EmployeeId = employeeId,
            EntitlementType = VacationType,
            EntitlementYear = EndDateFerieaar,
            ExpectedSettlementSequence = 1,
            ExpectedSettlementVersion = 1,
            Mode = SettlementReversalMode.ReverseAndSupersede,
            HasEndDateCorrection = true,
            CorrectedEndDate = new DateOnly(2025, 12, 31),
            ExpectedUserVersion = 1,
            SupersedeGoLiveFloor = NarrowGoLive,
            ActorId = "hr_s71_other_op",
            ActorRole = OperatorRole,
            ActorOrgId = OrgId,
        });
        Assert.True(first.Succeeded);
        Assert.Equal(3, first.SupersedingRow!.Sequence);
        Assert.Equal(1L, (await ReadRowAsync(employeeId, EndDateFerieaar, 3)).Version); // v1 AGAIN

        // The stale command fires: expected (sequence 1, version 1). The active row is
        // (sequence 3, version 1) — the VERSION MATCHES; only the sequence discriminates.
        var stale = await reversal.ReverseAsync(BareCommand(
            employeeId, EndDateFerieaar, expectedVersion: 1, expectedSequence: 1));

        Assert.False(stale.Succeeded);
        Assert.Equal(SettlementReversalFailure.SequenceMismatch, stale.Failure);
        Assert.Equal(3, stale.ActualSettlementSequence);

        // The gen-2 row is untouched — NOT reversed by the stale command — and no second
        // SettlementReversed was emitted (only the first operator's).
        var successor = await ReadRowAsync(employeeId, EndDateFerieaar, 3);
        Assert.Equal("SETTLED", successor.State);
        Assert.False(successor.BareMarker);
        Assert.Equal(1L, successor.Version);
        Assert.Equal(1L, await CountOutboxByTypeAsync(employeeId, "SettlementReversed"));
        Assert.Equal(0L, await CountAsync(
            "vacation_settlement_audit", "employee_id = @e AND actor_id = @a",
            ("e", employeeId), ("a", OperatorId)));
    }

    // ════════════════════════════════════════════════════════════════════════
    // R12 — double-reversal race: the loser parks on the employee advisory lock; the winner's
    // committed bare reversal lands in the window; the resumed loser re-reads under the lock,
    // finds NO active row and fails CLEAN (NoActiveRow — nothing written, no event, no throw).
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DoubleReversal_Race_ParkedLoser_FailsClean_NoActiveRow()
    {
        var (settle, reversal) = BootFixedClockServices(Clock);
        var employeeId = await SeedEmployeeAsync();
        await MarkLeaverAsync(employeeId, EndDate);
        await SettleInOwnTxAsync(settle, employeeId, EndDateFerieaar, Termination);

        var result = await RunRacedAsync(
            () => reversal.ReverseAsync(BareCommand(employeeId, EndDateFerieaar, 1)),
            commitWhileParked: () => ExecAsync(
                """
                UPDATE vacation_settlements
                   SET settlement_state = 'REVERSED', bare_reversal_not_due = TRUE,
                       version = version + 1, updated_at = NOW()
                 WHERE employee_id = @e AND entitlement_type = @t
                   AND entitlement_year = @y AND sequence = 1
                """, ("e", employeeId), ("t", VacationType), ("y", EndDateFerieaar)),
            lockEmployeeId: employeeId);

        // The loser: clean NoActiveRow (the winner's reversal stands; no double transition).
        Assert.False(result.Succeeded);
        Assert.Equal(SettlementReversalFailure.NoActiveRow, result.Failure);

        var row = await ReadRowAsync(employeeId, EndDateFerieaar, 1);
        Assert.Equal("REVERSED", row.State);
        Assert.Equal(2L, row.Version); // ONLY the winner's bump — the loser wrote nothing
        // No SettlementReversed from the loser (the winner was modeled as committed SQL).
        Assert.Equal(0L, await CountOutboxByTypeAsync(employeeId, "SettlementReversed"));
        Assert.Equal(0L, await CountAsync(
            "vacation_settlement_audit", "employee_id = @e AND actor_id = @a",
            ("e", employeeId), ("a", OperatorId)));
    }

    // ════════════════════════════════════════════════════════════════════════
    // R12 — reversal-vs-Step-B race: a STALE settle drive (the Step-B shape) parks on the lock;
    // the bare reversal commits in the window; the resumed settle re-reads under the lock —
    // no active row BUT the R3 marker ⇒ the benign NotDue (no resurrection, no 23505).
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ReversalVsStepB_Race_StaleSettleParks_SeesMarkerInLock_NotDue()
    {
        var (settle, _) = BootFixedClockServices(Clock);
        var employeeId = await SeedEmployeeAsync();
        await MarkLeaverAsync(employeeId, EndDate);
        await SettleInOwnTxAsync(settle, employeeId, EndDateFerieaar, Termination);

        var outcome = await RunRacedAsync(
            () => SettleInOwnTxAsync(settle, employeeId, EndDateFerieaar, Termination, NarrowGoLive),
            commitWhileParked: () => ExecAsync(
                """
                UPDATE vacation_settlements
                   SET settlement_state = 'REVERSED', bare_reversal_not_due = TRUE,
                       version = version + 1, updated_at = NOW()
                 WHERE employee_id = @e AND entitlement_type = @t
                   AND entitlement_year = @y AND sequence = 1
                """, ("e", employeeId), ("t", VacationType), ("y", EndDateFerieaar)),
            lockEmployeeId: employeeId);

        // Benign NotDue (the in-lock marker check) — NOT a 23505 against the REVERSED row's PK,
        // NOT a fresh sequence-1 row, NOT an AlreadySettled fabrication.
        Assert.False(outcome.DidSettle);
        Assert.True(outcome.NotDue);
        Assert.Null(outcome.Row);
        Assert.Equal(1L, await CountAsync(
            "vacation_settlements", "employee_id = @e AND entitlement_year = @y",
            ("e", employeeId), ("y", EndDateFerieaar)));
        Assert.Equal(1L, await CountOutboxByTypeAsync(employeeId, "TerminationSettled")); // only the original
    }

    // ─────────────────────────────── race choreography ───────────────────────────────

    /// <summary>The established parked-lock choreography (the SettlementCloseLeaverTests /
    /// TASK-7002 R12 convention): hold the employee advisory lock on a foreign tx, fire the
    /// operation (it parks on its own lock acquisition), prove it parked via pg_stat_activity,
    /// commit the competing mutation as direct SQL (the committed outcome of the winning path,
    /// which would itself have held the same lock), release, and return the loser's outcome.</summary>
    private async Task<T> RunRacedAsync<T>(
        Func<Task<T>> racedOperation, Func<Task> commitWhileParked, string lockEmployeeId)
    {
        await using var lockConn = new NpgsqlConnection(_harness.ConnectionString);
        await lockConn.OpenAsync();
        await using var lockTx = await lockConn.BeginTransactionAsync();
        await using (var lockCmd = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtext('employee-' || @employeeId))", lockConn, lockTx))
        {
            lockCmd.Parameters.AddWithValue("employeeId", lockEmployeeId);
            await lockCmd.ExecuteScalarAsync();
        }

        var racedTask = Task.Run(racedOperation);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        var parked = false;
        while (!parked && DateTime.UtcNow < deadline)
        {
            if (racedTask.IsCompleted)
            {
                await racedTask; // propagate a real failure
                Assert.Fail("the raced operation completed without parking on the held advisory lock — " +
                            "the R12 race window was not exercised.");
            }
            parked = await IsAdvisoryLockWaiterPresentAsync();
            if (!parked)
                await Task.Delay(100);
        }
        Assert.True(parked, "the raced operation never parked on the held employee advisory lock within 30s.");

        await commitWhileParked();
        await lockTx.RollbackAsync(); // release — the loser resumes and re-evaluates in-lock

        return await racedTask;
    }

    private async Task<bool> IsAdvisoryLockWaiterPresentAsync()
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM pg_stat_activity
            WHERE datname = current_database()
              AND wait_event_type = 'Lock'
              AND query ILIKE '%pg_advisory_xact_lock%'
            """, conn);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync()) > 0;
    }

    // ─────────────────────────────── drives / boots ───────────────────────────────

    private SettlementReversalCommand BareCommand(
        string employeeId, int year, long expectedVersion, int expectedSequence = 1) => new()
    {
        EmployeeId = employeeId,
        EntitlementType = VacationType,
        EntitlementYear = year,
        ExpectedSettlementSequence = expectedSequence,
        ExpectedSettlementVersion = expectedVersion,
        Mode = SettlementReversalMode.Bare,
        ActorId = OperatorId,
        ActorRole = OperatorRole,
        ActorOrgId = OrgId,
    };

    /// <summary>A fixed-clock derived host (PAT-008) WITHOUT a go-live date — its own poller
    /// stays Step-B-dormant (Step A no-ops on the pre-flipped leavers these tests seed) — and
    /// the DI-registered settlement + reversal services resolved from it.</summary>
    private (VacationSettlementService Settle, SettlementReversalService Reversal) BootFixedClockServices(
        DateOnly fixedDate)
    {
        var derived = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(fixedDate))));
        _ = derived.CreateClient();
        return (derived.Services.GetRequiredService<VacationSettlementService>(),
                derived.Services.GetRequiredService<SettlementReversalService>());
    }

    /// <summary>A GATED fixed-clock live-poller host (the SettlementCloseLeaverTests harness) —
    /// boots Step A + the gated Step B immediately against the fixed clock.</summary>
    private void BootFixedClockHost(DateOnly fixedDate, DateOnly goLiveDate)
    {
        var derived = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Settlement:GoLiveDate"] = goLiveDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                }));
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(fixedDate));
            });
        });
        _ = derived.CreateClient(); // host build + hosted-service start (immediate poll)
    }

    /// <summary>One settlement pass in its OWN committed ReadCommitted tx — the exact Step-B
    /// drive shape (the SettlementCloseLeaverTests helper).</summary>
    private async Task<SettlementOutcome> SettleInOwnTxAsync(
        VacationSettlementService service, string employeeId, int year, string trigger,
        DateOnly? goLiveFloor = null)
    {
        await using var conn = _factory.Services.GetRequiredService<DbConnectionFactory>().Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try
        {
            var outcome = await service.SettleAsync(
                employeeId, VacationType, year, trigger, conn, tx, leaverGoLiveFloor: goLiveFloor);
            await tx.CommitAsync();
            return outcome;
        }
        catch
        {
            if (tx.Connection is not null)
                await tx.RollbackAsync();
            throw;
        }
    }

    private async Task<bool> WaitForSettlementAsync(string employeeId, int year, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await CountAsync(
                    "vacation_settlements",
                    "employee_id = @e AND entitlement_type = @t AND entitlement_year = @y AND settlement_state <> 'REVERSED'",
                    ("e", employeeId), ("t", VacationType), ("y", year)) >= 1)
                return true;
            await Task.Delay(250);
        }
        return false;
    }

    // ─────────────────────────────── seeding ───────────────────────────────

    private async Task<string> SeedEmployeeAsync()
    {
        var employeeId = "emp_s71_rev_" + Guid.NewGuid().ToString("N")[..8];
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, OrgId, "AC", "OK24");
        return employeeId;
    }

    private async Task SetEndDateAsync(string employeeId, DateOnly endDate)
    {
        await ExecAsync(
            "UPDATE users SET employment_end_date = @endDate, updated_at = NOW() WHERE user_id = @id",
            ("id", employeeId), ("endDate", endDate));
    }

    /// <summary>The post-Step-A leaver state, seeded directly (the TerminationSettlementTests
    /// convention; users.version stays 1).</summary>
    private async Task MarkLeaverAsync(string employeeId, DateOnly endDate)
    {
        await ExecAsync(
            """
            UPDATE users SET employment_end_date = @endDate, is_active = FALSE,
                             end_date_deactivated = TRUE, updated_at = NOW()
            WHERE user_id = @id
            """, ("id", employeeId), ("endDate", endDate));
    }

    /// <summary>An OPEN §26 payout request bound to the EXACT settlement row (the 7100 column
    /// contract; the 7102 endpoint does not exist yet — direct seed per convention).</summary>
    private async Task SeedOpenRequestAsync(string employeeId, int year, int settlementSequence)
    {
        await ExecAsync(
            """
            INSERT INTO termination_payout_requests
                (employee_id, entitlement_type, entitlement_year, settlement_sequence,
                 state, request_date, recorded_by, version)
            VALUES (@e, @t, @y, @s, 'OPEN', @d, 'test_s71_hr', 1)
            """,
            ("e", employeeId), ("t", VacationType), ("y", year), ("s", settlementSequence),
            ("d", new DateOnly(2026, 3, 1)));
    }

    private async Task SeedTransferAgreementAsync(string employeeId, int year, decimal transferDays)
    {
        await ExecAsync(
            """
            INSERT INTO vacation_transfer_agreements
                (employee_id, entitlement_year, entitlement_type, transfer_days, agreement_date, recorded_by, version)
            VALUES (@e, @y, @t, @days, @date, 'test_s71_hr', 1)
            """,
            ("e", employeeId), ("y", year), ("t", VacationType), ("days", transferDays),
            ("date", new DateOnly(year + 1, 6, 30))); // within the §21 deadline (31 Dec E+1)
    }

    // ─────────────────────────────── reads ───────────────────────────────

    private sealed record RowState(
        string State, string Trigger, decimal Transfer, decimal Payout, decimal Forfeit,
        string? ReviewDisposition, bool BareMarker, long Version, string SnapshotJson);

    private async Task<RowState> ReadRowAsync(string employeeId, int year, int sequence)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT settlement_state, trigger, transfer_days, payout_days, forfeit_days,
                   review_disposition, bare_reversal_not_due, version, snapshot::text
            FROM vacation_settlements
            WHERE employee_id = @e AND entitlement_type = @t AND entitlement_year = @y AND sequence = @s
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("t", VacationType);
        cmd.Parameters.AddWithValue("y", year);
        cmd.Parameters.AddWithValue("s", sequence);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(),
            $"settlement row expected for {employeeId}/{year}/seq {sequence}");
        return new RowState(
            reader.GetString(0), reader.GetString(1),
            reader.GetDecimal(2), reader.GetDecimal(3), reader.GetDecimal(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.GetBoolean(6), reader.GetInt64(7), reader.GetString(8));
    }

    private async Task<(string State, long Version)?> ReadRequestAsync(
        string employeeId, int year, int settlementSequence)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT state, version FROM termination_payout_requests
            WHERE employee_id = @e AND entitlement_type = @t
              AND entitlement_year = @y AND settlement_sequence = @s
            ORDER BY request_id DESC
            LIMIT 1
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("t", VacationType);
        cmd.Parameters.AddWithValue("y", year);
        cmd.Parameters.AddWithValue("s", settlementSequence);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return (reader.GetString(0), reader.GetInt64(1));
    }

    private async Task<(DateOnly? EndDate, bool EndDateDeactivated, bool IsActive, long Version)>
        ReadUserTupleAsync(string employeeId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT employment_end_date, end_date_deactivated, is_active, version
            FROM users WHERE user_id = @id
            """, conn);
        cmd.Parameters.AddWithValue("id", employeeId);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), $"user row expected for {employeeId}");
        return (
            reader.IsDBNull(0) ? null : reader.GetFieldValue<DateOnly>(0),
            reader.GetBoolean(1),
            reader.GetBoolean(2),
            reader.GetInt64(3));
    }

    private async Task<decimal?> ReadCarryoverInAsync(string employeeId, int year)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT carryover_in FROM entitlement_balances
            WHERE employee_id = @e AND entitlement_type = @t AND entitlement_year = @y
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("t", VacationType);
        cmd.Parameters.AddWithValue("y", year);
        var result = await cmd.ExecuteScalarAsync();
        return result is null or DBNull ? null : (decimal)result;
    }

    private async Task<string?> ReadLatestOutboxPayloadAsync(string streamId, string eventType)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT event_payload FROM outbox_events
            WHERE stream_id = @streamId AND event_type = @eventType
            ORDER BY outbox_id DESC
            LIMIT 1
            """, conn);
        cmd.Parameters.AddWithValue("streamId", streamId);
        cmd.Parameters.AddWithValue("eventType", eventType);
        return (string?)await cmd.ExecuteScalarAsync();
    }

    private async Task<long> CountOutboxByTypeAsync(string employeeId, string eventType)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM outbox_events WHERE stream_id = @s AND event_type = @t", conn);
        cmd.Parameters.AddWithValue("s", $"employee-{employeeId}");
        cmd.Parameters.AddWithValue("t", eventType);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private async Task<long> CountAsync(string table, string whereClause, params (string Name, object Value)[] ps)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"SELECT COUNT(*) FROM {table} WHERE {whereClause}", conn);
        foreach (var (name, value) in ps)
            cmd.Parameters.AddWithValue(name, value);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private async Task ExecAsync(string sql, params (string Name, object Value)[] ps)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (name, value) in ps)
            cmd.Parameters.AddWithValue(name, value);
        await cmd.ExecuteNonQueryAsync();
    }
}
