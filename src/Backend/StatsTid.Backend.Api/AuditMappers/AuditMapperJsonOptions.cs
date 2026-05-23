using System.Text.Json;

namespace StatsTid.Backend.Api.AuditMappers;

/// <summary>
/// S44 / TASK-4407. Canonical <see cref="JsonSerializerOptions"/> used by
/// every <c>IAuditProjectionMapper&lt;T&gt;</c> when serializing the
/// <see cref="StatsTid.SharedKernel.Audit.AuditProjectionRowData.DetailsJson"/>
/// payload. Single shared instance ensures the 6 (and eventually ~53)
/// mappers produce wire-shape-consistent JSON — no per-mapper drift in
/// property casing or null handling.
///
/// <para>Per Step 0b cycle 1 Codex W2 + Reviewer NOTE-3 absorption — the
/// initial S44 plan said "shared options" without specifying. This static
/// class is the named source.</para>
///
/// <para>Conventions:</para>
/// <list type="bullet">
///   <item><description><see cref="JsonSerializerDefaults.Web"/>: camelCase
///   property names (matches consumer wire shape for the future GET endpoint
///   in S44f + AuditLogView.tsx frontend rendering).</description></item>
///   <item><description><see cref="JsonIgnoreCondition.WhenWritingNull"/>:
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
