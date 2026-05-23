namespace StatsTid.SharedKernel.Audit;

/// <summary>
/// S43 / ADR-026 D2. Mapper output threaded to
/// <c>AuditProjectionRepository.InsertAsync</c>. Carries the event-specific
/// fields the mapper derived from the source event.
/// </summary>
/// <param name="VisibilityScope">Per-event visibility classification per
/// ADR-026 D3.</param>
/// <param name="TargetOrgId">Resolved target organization for
/// TENANT_TARGETED rows; NULL for GLOBAL_*. CHECK constraint at the schema
/// layer rejects malformed combinations.</param>
/// <param name="TargetResourceId">Natural-key string identifying the
/// affected resource (e.g., <c>user_id</c>, <c>config_id</c>) — caller-derived,
/// no schema enforcement.</param>
/// <param name="DetailsJson">Pre-serialized JSON for the
/// <c>audit_projection.details</c> JSONB column. Mappers serialize once with
/// the canonical <c>JsonSerializerOptions</c> used at their endpoint so
/// the persisted shape matches the wire shape consumers expect.</param>
public sealed record AuditProjectionRowData(
    AuditVisibilityScope VisibilityScope,
    string? TargetOrgId,
    string? TargetResourceId,
    string DetailsJson);
