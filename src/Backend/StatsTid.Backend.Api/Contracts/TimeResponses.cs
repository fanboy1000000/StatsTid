namespace StatsTid.Backend.Api.Contracts;

// S120 / TASK-12000 (Fork B retrofit Pass 7, PAT-010/PAT-012) — named response records for the
// time family (TimeEndpoints). Each record is an EXACT shape-copy of the anonymous object its
// handler previously returned: same member NAMES, same ORDER, same nullability — serialized
// camelCase via the .NET 8 minimal-API JsonSerializerDefaults.Web default, NO [JsonPropertyName].
// BYTE-IDENTICAL wire JSON except the ONE owner-ruled delta below.
//
// The two projection GETs (time-entries, absences) serialize the NAMED SharedKernel models
// (TimeEntry / AbsenceEntry) directly — the model IS the wire shape by construction (the handler
// news up the model itself, not a projection subset), so they take `.Produces<IEnumerable<T>>`
// with NO new record (the PAT-012 named-model rule).

/// <summary>The POST /api/time-entries 201 receipt — <c>{ eventId, streamId }</c>. The event id
/// of the enqueued <c>TimeEntryRegistered</c> + the consolidated employee stream id
/// (<c>employee-{employeeId}</c>, ADR-018 D6).</summary>
public sealed record TimeEntryCreatedResponse(
    Guid EventId,
    string StreamId);

/// <summary>The GET /api/flex-balance/{employeeId} 200 body — the S120 owner-ruled ONE shape
/// (ruling #1, branch-normalization class, 1st instance): all 5 members ALWAYS present. The
/// no-history branch serves <c>balance: 0</c> with the 3 history members null and the vestigial
/// <c>message</c> DROPPED (no reader existed); the with-history branch is byte-identical to the
/// pre-S120 wire (<c>FlexBalanceUpdated.Reason</c> is CLR non-null, so <c>reason</c> is null
/// ONLY on the no-history branch).</summary>
public sealed record FlexBalanceResponse(
    string EmployeeId,
    decimal Balance,
    decimal? PreviousBalance,
    decimal? Delta,
    string? Reason);
