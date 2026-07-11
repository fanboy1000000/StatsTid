namespace StatsTid.Backend.Api.Contracts;

// S115 / TASK-11501 (Fork B retrofit Pass 2, PAT-010/PAT-012) — named response records for the
// GET /api/admin/audit read (AuditEndpoints). EXACT shape-copies of the prior anonymous objects:
// same member NAMES, same ORDER, same nullability — serialized camelCase via the .NET 8
// JsonSerializerDefaults.Web default, NO [JsonPropertyName]. BYTE-IDENTICAL wire JSON.

/// <summary>One audit-projection row of <see cref="AuditLogResponse"/> — mirrors the
/// <c>AuditProjectionRow</c> repository shape MINUS <c>outboxId</c> (never serialized).
/// <paramref name="Details"/> is the RAW per-event-type JSONB text passed through as a plain
/// string (the caller deserializes per the audit-projection catalog) — deliberately NOT
/// reshaped into a structured member.</summary>
public sealed record AuditLogRow(
    Guid ProjectionId,
    Guid EventId,
    string EventType,
    string VisibilityScope,
    string? TargetOrgId,
    string? TargetResourceId,
    string? ActorId,
    string? ActorPrimaryOrgId,
    DateTimeOffset OccurredAt,
    Guid? CorrelationId,
    string Details,
    DateTimeOffset ProjectedAt);

/// <summary>The GET /api/admin/audit envelope — <c>{ rows, totalCount, page, pageSize }</c>
/// (NOT a bare array). <paramref name="TotalCount"/> is the exact scope-filtered match count
/// (a Postgres COUNT — long), which may exceed the page.</summary>
public sealed record AuditLogResponse(
    IReadOnlyList<AuditLogRow> Rows,
    long TotalCount,
    int Page,
    int PageSize);
