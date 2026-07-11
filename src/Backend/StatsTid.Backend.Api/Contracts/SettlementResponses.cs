using System.ComponentModel.DataAnnotations;

namespace StatsTid.Backend.Api.Contracts;

// S117 / TASK-11701 (Fork B retrofit Pass 4, PAT-010/PAT-012) — named response records for the
// settlement bucket (VacationSettlementEndpoints + SettlementReversalEndpoints +
// TerminationPayoutRequestEndpoints). Each record is an EXACT shape-copy of the anonymous object
// its handler previously returned: same member NAMES, same ORDER, same nullability — serialized
// camelCase via the .NET 8 minimal-API JsonSerializerDefaults.Web default, NO [JsonPropertyName].
// BYTE-IDENTICAL wire JSON. Every decimal is an ADR-033 D1 DAY-COUNT (NUMERIC(6,2)) copied
// verbatim from the row/snapshot — never rounded, never recomputed at the serialization site.
//
// PINNED PROHIBITION (SPRINT-117): no Contracts type may embed VacationSettlementSnapshot — its 4
// [JsonIgnore(WhenWritingNull/Default)] members are conditionally ABSENT from the wire and must
// never enter the response-reachable closure through this bucket. Handlers copy scalar fields only.
//
// NOTE: POST .../resolve is NOT typed here (the flag-and-defer rule's SECOND firing, owner-ruled
// S117 OQ-2): its 4 success branches emit genuinely DIFFERENT key sets, so one record cannot
// describe them and typing it would be a wire change. See tools/openapi-convention-exempt.txt.

/// <summary>One GET /api/vacation-settlements/payout-pending row — a §24 SETTLED settlement with an
/// un-reconciled positive payout bucket awaiting the S69 payroll line.
/// <paramref name="PayoutDays"/> is the NUMERIC(6,2) day-count verbatim;
/// <paramref name="SettledAt"/> maps the row's <c>created_at</c> (the prior anonymous shape's
/// <c>settledAt</c> name is preserved).</summary>
public sealed record PayoutPendingItem(
    string EmployeeId,
    string EntitlementType,
    int EntitlementYear,
    int Sequence,
    decimal PayoutDays,
    long Version,
    DateTime SettledAt,
    string PrimaryOrgId);

/// <summary>The GET /api/vacation-settlements/payout-pending 200 envelope —
/// <c>{ items: [...], count: n }</c> (NOT a bare array). ONE record constructed at BOTH return
/// sites: the empty-scope branch (an empty typed list + count 0) and the populated branch.</summary>
public sealed record PayoutPendingListResponse(
    IReadOnlyList<PayoutPendingItem> Items,
    int Count);

/// <summary>The nested successor body of <see cref="SettlementReversalResponse"/> — present only
/// when the REVERSE_AND_SUPERSEDE mode produced a superseding settlement row in the same tx.
/// Enum authorities: <paramref name="SettlementState"/> = the vacation_settlements
/// <c>settlement_state</c> DB CHECK (docker/postgres/init.sql:2918);
/// <paramref name="Trigger"/> = the <c>trigger</c> DB CHECK (docker/postgres/init.sql:2919).</summary>
public sealed record SettlementSuccessor(
    int Sequence,
    [property: AllowedValues("PENDING_REVIEW", "SETTLED", "REVERSED")] string SettlementState,
    [property: AllowedValues("YEAR_END", "TERMINATION")] string Trigger,
    long Version);

/// <summary>The POST /api/admin/employees/{employeeId}/settlement-reversal 200 body — BOTH
/// aggregates' outcomes (no ETag is stamped; the body carries every version — the S71 R4 DECLARED
/// shape). <paramref name="ReversalKind"/> is a provably-TOTAL projection of the handler
/// (<c>SupersedingRow is null ? "BARE" : "SUPERSEDED"</c> — SettlementReversalEndpoints.cs);
/// <paramref name="Successor"/> is the S117 nullable-complex-wrapper mechanism's FIRST NEW consumer
/// (null on a BARE reversal, the nested record on a supersession — spec-emitted as
/// <c>type: object + allOf: [$ref] + nullable: true</c>, required);
/// <paramref name="UserVersionAfter"/>/<paramref name="UserIsActiveAfter"/> are null unless an
/// end-date correction touched the user aggregate.</summary>
public sealed record SettlementReversalResponse(
    string EmployeeId,
    string EntitlementType,
    int EntitlementYear,
    [property: AllowedValues("BARE", "SUPERSEDED")] string ReversalKind,
    int ReversedSequence,
    long ReversedVersion,
    bool BareReversalNotDue,
    SettlementSuccessor? Successor,
    IReadOnlyList<long> VoidedRequestIds,
    long? UserVersionAfter,
    bool? UserIsActiveAfter);

/// <summary>The POST /api/admin/employees/{employeeId}/termination-payout-request 201 body — the
/// created §26 request row + the snapshot-COPIED quantities (ADR-033 D3 — never recomputed).
/// <paramref name="State"/> enum authority: the termination_payout_requests <c>state</c> DB CHECK
/// (docker/postgres/init.sql:3480) — the FULL {OPEN, LINE_STAGED, VOIDED_BY_REVERSAL} superset.
/// Enum-fidelity is MEMBERSHIP: this endpoint always emits OPEN (a freshly-created request), but
/// the CHECK is the durable authority — the S69/S71 machinery advances the row to the other
/// members, and a narrower declared set would turn a future read surface into a liar.</summary>
public sealed record TerminationPayoutRequestResponse(
    long RequestId,
    string EmployeeId,
    string EntitlementType,
    int EntitlementYear,
    int SettlementSequence,
    [property: AllowedValues("OPEN", "LINE_STAGED", "VOIDED_BY_REVERSAL")] string State,
    DateOnly RequestDate,
    string? EvidenceNote,
    decimal CrystallizedDays,
    DateOnly SettlementBoundaryDate,
    long Version);

/// <summary>The POST .../reconcile-payout 200 body — the audited §24 manual-reconciliation marker
/// receipt (the op binds NO request DTO: route params + If-Match only — declared in
/// tools/openapi-bodyless-declared.txt, the list's 3rd member).</summary>
public sealed record ReconcilePayoutResponse(
    string EmployeeId,
    string EntitlementType,
    int EntitlementYear,
    int Sequence,
    DateTime PayoutReconciledAt,
    string PayoutReconciledBy,
    long Version);

/// <summary>The shared POST (201) / PUT (200) /api/vacation-transfer-agreements/{employeeId} body —
/// ONE payload construction serves both verbs (the sibling-record rule, PAT-012 paved road §1).
/// <paramref name="EntitlementType"/> enum authority: the endpoint's Guard 1 FORCES the constant
/// "VACATION" (§21 stk.2 applies to the &gt;4-week VACATION tranche only — any other value is
/// 422-rejected before persistence), so the singleton set is guard-total, not aspirational.
/// <paramref name="TransferDays"/> is the NUMERIC(6,2) §21 day-count verbatim.</summary>
public sealed record TransferAgreementResponse(
    string EmployeeId,
    int EntitlementYear,
    [property: AllowedValues("VACATION")] string EntitlementType,
    decimal TransferDays,
    DateOnly AgreementDate,
    string RecordedBy,
    long Version);
