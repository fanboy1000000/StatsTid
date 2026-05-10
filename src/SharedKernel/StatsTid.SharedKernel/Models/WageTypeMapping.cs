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

    /// <summary>
    /// Surrogate UUID PK (S29 / TASK-2901 migration <c>s29-d1-wtm-effective-dating</c>) added
    /// to support effective-dating + supersession (ADR-020 D2). Distinct from the natural key
    /// <c>(time_type, ok_version, agreement_code, position)</c> which now identifies a history
    /// LINEAGE rather than a single row. Defaulted to <see cref="Guid.Empty"/> so existing
    /// constructor sites continue to compile; the repo generates a fresh GUID at insert time
    /// when Empty is supplied (mirrors <c>LocalAgreementProfile.ProfileId</c> precedent).
    /// </summary>
    public Guid MappingId { get; init; } = Guid.Empty;

    /// <summary>
    /// Inclusive lower bound of this row's effective range (S29 / TASK-2901). DB column is
    /// <c>DATE NOT NULL</c>; backfill set existing rows to <c>2020-01-01</c>. Defaulted to
    /// <c>default(DateOnly)</c> here for compile-time backward compatibility — repo INSERT
    /// paths bind the caller-supplied value explicitly.
    /// </summary>
    public DateOnly EffectiveFrom { get; init; }

    /// <summary>
    /// Exclusive upper bound of this row's effective range (S29 / TASK-2901). <c>null</c>
    /// means "currently open / unbounded above" — at most one open row per natural key per
    /// the <c>idx_wtm_natural_key_open</c> partial-unique-index. A non-null value means the
    /// row has been superseded or soft-deleted (<c>effective_to = closure_date</c>).
    /// </summary>
    public DateOnly? EffectiveTo { get; init; }
}
