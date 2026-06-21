using System.Text.Json;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Integrations.Payroll.Services;

/// <summary>
/// S90 / TASK-9004 (ADR-034) — the SINGLE canonical (de)serialization + content-hash for the
/// <c>payroll_export_records</c> manifest columns (<c>original_lines</c> /
/// <c>current_effective_lines</c>) and the idempotency <c>content_hash</c>.
///
/// <para>
/// Both the atomic export path (<see cref="PayrollExportService"/>, which writes
/// <c>original_lines</c> == <c>current_effective_lines</c> on first export) and the corrections
/// path (<see cref="RetroactiveCorrectionService"/>, which READS <c>current_effective_lines</c> as
/// the diff baseline (B3) and then UPDATES it to the corrected lines in the SAME audit tx) MUST
/// serialize/hash identically — otherwise a correction's rewritten baseline would no longer match
/// the export-time content hash, breaking the idempotency/re-export-conflict semantics. Keeping the
/// JSON options, the deterministic per-line sort key, and the SHA-256 hash in ONE place is the
/// guarantee.
/// </para>
/// </summary>
public static class PayrollExportManifest
{
    // Stable, ordered JSON for the content hash + the JSONB manifest columns. Property order is
    // fixed by the record's declaration order; we additionally sort the line list by a deterministic
    // key (below) so a reordered input list produces the SAME bytes (idempotency content semantics).
    private static readonly JsonSerializerOptions ManifestJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    /// <summary>Deterministic per-line sort key so a reordered input list serializes identically.</summary>
    public static string SortKey(PayrollExportLine l) =>
        $"{l.WageType}|{l.PeriodStart:yyyy-MM-dd}|{l.PeriodEnd:yyyy-MM-dd}|{l.OkVersion}|{l.Hours}|{l.Amount}|{l.SourceRuleId}|{l.SourceTimeType}";

    /// <summary>Orders the lines by <see cref="SortKey"/> (ordinal) so serialization is order-stable.</summary>
    public static List<PayrollExportLine> OrderLines(IEnumerable<PayrollExportLine> lines) =>
        lines.OrderBy(SortKey, StringComparer.Ordinal).ToList();

    /// <summary>Serializes the (already deterministically ordered) lines to the canonical manifest JSON.</summary>
    public static string Serialize(IReadOnlyList<PayrollExportLine> orderedLines) =>
        JsonSerializer.Serialize(orderedLines, ManifestJsonOptions);

    /// <summary>
    /// Deserializes the manifest JSON stored in <c>original_lines</c> / <c>current_effective_lines</c>
    /// back to <see cref="PayrollExportLine"/> records (the corrections diff baseline). Returns an empty
    /// list for a null/empty document.
    /// </summary>
    public static IReadOnlyList<PayrollExportLine> Deserialize(string? manifestJson)
    {
        if (string.IsNullOrWhiteSpace(manifestJson))
            return [];
        return JsonSerializer.Deserialize<List<PayrollExportLine>>(manifestJson, ManifestJsonOptions) ?? [];
    }

    /// <summary>
    /// Deterministic content hash over the (already deterministically ordered) lines.
    /// Same key + same hash → idempotent no-op; same key + different hash → 409 (use a correction).
    /// </summary>
    public static string ComputeContentHash(IReadOnlyList<PayrollExportLine> orderedLines)
    {
        var json = Serialize(orderedLines);
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes);
    }
}
