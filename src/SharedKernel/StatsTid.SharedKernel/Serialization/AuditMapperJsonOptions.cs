using System.Text.Json;

namespace StatsTid.SharedKernel.Serialization;

/// <summary>
/// S44 / TASK-4407 (hoisted to SharedKernel S133 / TASK-13308b, QUAL-027).
/// Canonical <see cref="JsonSerializerOptions"/> used by every
/// <c>IAuditProjectionMapper&lt;T&gt;</c> when serializing the
/// <c>AuditProjectionRowData.DetailsJson</c> payload. A single shared instance
/// ensures all ~79 mappers produce wire-shape-consistent JSON — no per-mapper
/// drift in property casing or null handling.
///
/// <para><b>Why this lives in SharedKernel (and is NOT business logic).</b>
/// The audit payload is persisted verbatim into <c>audit_log</c> and the
/// ADR-026 audit-projection <c>details</c> JSONB, and it is read across
/// assemblies: mappers exist in BOTH <c>StatsTid.Backend.Api.AuditMappers</c>
/// and <c>StatsTid.Infrastructure.AuditMappers</c>, and Infrastructure cannot
/// reference Backend.Api (that would invert the dependency direction). Before
/// this hoist the options were duplicated — the Backend canonical plus 17
/// byte-identical private copies in Infrastructure — so a single settings
/// change would silently split the audit byte-format across assemblies, an
/// auditability hazard (QUAL-027). This class is pure cross-cutting
/// serialization configuration (a shared wire-format contract), not a domain
/// rule or entitlement calculation, so it belongs to the shared kernel that
/// both assemblies already reference — it carries no business logic and holds
/// no domain state, satisfying SharedKernel's "no business logic" rule.</para>
///
/// <para>Conventions (MUST stay byte-identical — a shift corrupts the
/// ADR-026 audit projection):</para>
/// <list type="bullet">
///   <item><description><see cref="JsonSerializerDefaults.Web"/>: camelCase
///   property names (matches the audit-log GET endpoint + AuditLogView.tsx
///   frontend rendering).</description></item>
///   <item><description><see cref="System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull"/>:
///   omit null fields from the details payload to reduce storage + render
///   noise.</description></item>
///   <item><description><c>WriteIndented = false</c>: compact storage.</description></item>
/// </list>
/// </summary>
public static class AuditMapperJsonOptions
{
    public static JsonSerializerOptions Default { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };
}
