namespace StatsTid.Backend.Api.Contracts;

// S111 / TASK-11101 (Fork B typed-client) — the named REQUEST DTO for the proof mutation
// POST /api/admin/units. Relocated here (from the Endpoints namespace) so the request-side convention —
// a named Contracts/ record consumed by .Accepts<CreateUnitRequest>(...) — has a home alongside the
// response records. NOTE (Step-0b): the request side is spec≡DTO, weaker than the response side's
// spec≡runtime gate (JsonSerializerDefaults.Web is case-insensitive on INPUT, so a request-casing
// mismatch breaks only the generated TS client, not deserialization).

/// <summary>POST /api/admin/units body. <c>ParentUnitId</c> null = a top-level unit (directly under
/// the Organisation).</summary>
public sealed record CreateUnitRequest(string OrganisationId, Guid? ParentUnitId, string Type, string Name);
