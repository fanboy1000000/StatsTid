namespace StatsTid.Backend.Api.Contracts;

// S120 / TASK-12000 (Fork B retrofit Pass 7, PAT-010/PAT-012) — named response records for the
// time family (TimeEndpoints). Each record is an EXACT shape-copy of the anonymous object its
// handler previously returned: same member NAMES, same ORDER, same nullability — serialized
// camelCase via the .NET 8 minimal-API JsonSerializerDefaults.Web default, NO [JsonPropertyName].
// BYTE-IDENTICAL wire JSON. (The ONE owner-ruled delta this family carried — S120 ruling #1 on the
// /api/flex-balance no-history branch — retired with that route and its FlexBalanceResponse record
// in S146 / TASK-14607; the ruling's class stands.)
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
