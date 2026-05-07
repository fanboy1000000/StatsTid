namespace StatsTid.SharedKernel.Models;

public sealed class WageTypeMapping
{
    public required string TimeType { get; init; }
    public required string WageType { get; init; }
    public required string OkVersion { get; init; }
    public required string AgreementCode { get; init; }
    public string? Description { get; init; }
    public string Position { get; init; } = "";

    /// <summary>
    /// Row-version optimistic-concurrency token (ADR-018 D7 + ADR-019 pending). Mirrors the
    /// <see cref="LocalAgreementProfile.Version"/> pattern: BIGINT (long) row-version, first-
    /// insert is <c>1</c>; each in-place UPDATE bumps it by one. Combined with
    /// <c>If-Match: "&lt;version&gt;"</c> on PUT (RFC 7232 quoted), this provides optimistic-
    /// concurrency control on admin-config edits per ADR-019 (D2.2 propagation).
    ///
    /// Defaulted to <c>1</c> (rather than <c>required</c>) so existing constructor sites in
    /// endpoints / payroll mapping continue to compile during the Phase 1 → Phase 2
    /// transition. Phase 2 repository updates (TASK-2505) will read the column from the
    /// DB and set this property explicitly. The DB column is <c>BIGINT NOT NULL DEFAULT 1</c>
    /// (TASK-2501 migration <c>s25-d2-2-version</c>).
    /// </summary>
    public long Version { get; init; } = 1;
}
