using System.Text.Json;

namespace StatsTid.SharedKernel.Events;

/// <summary>
/// Per-field old-and-new pair for profile delta events (ADR-017 D6). The values are
/// rendered as JsonElement so each field's native type (string, number, bool) round-trips
/// through System.Text.Json without forced boxing or per-type variants.
/// </summary>
public sealed record FieldChange(JsonElement Old, JsonElement New);
