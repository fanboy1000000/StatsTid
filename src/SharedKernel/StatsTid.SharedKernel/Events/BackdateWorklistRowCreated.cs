namespace StatsTid.SharedKernel.Events;

/// <summary>
/// S138 / TASK-13803 (ADR-040 D8, Increment 3 — temporal editing). Emitted when a backdated
/// profile / agreement-code / employment-category correction lands a row on the HR diagnostic
/// worklist (<c>hr_backdate_worklist</c>) — OR appends a further trigger to an already-OPEN row
/// for the same key (<see cref="Appended"/> = true; same event type, so a reader of the
/// employee stream sees every correction that touched the row in order).
///
/// <para>
/// <b>Two row kinds, one event.</b> <see cref="Kind"/> is <c>EXPORTED_MONTH</c> (the correction
/// reaches a month already sent to payroll — keys <see cref="Year"/>/<see cref="Month"/>/
/// <see cref="ExportId"/>, baseline <see cref="BaselineContentHash"/>) or <c>SETTLED_YEAR</c>
/// (the correction reaches a holiday year with an ACTIVE ADR-033 settlement — keys
/// <see cref="EntitlementType"/>/<see cref="EntitlementYear"/>, baseline
/// <see cref="BaselineSettlementSequence"/> + <see cref="BaselineSettlementState"/>). The
/// baseline is captured AT APPEND TIME so "has this been recalculated / reversed since?" is
/// derivable exactly at read time (refinement rev 4, Assumption 12).
/// </para>
///
/// <para>
/// <b>Not a command.</b> This records that a worklist row exists / grew; nothing recalculates
/// automatically (ADR-013). <see cref="TriggerEventId"/> is the causing correction event
/// (<c>EmployeeProfileSuperseded</c> / <c>UserAgreementCodeSuperseded</c> / …) — the causal link
/// back to the dated-history write. Stream: <c>employee-{employeeId}</c> (ADR-018 D6).
/// </para>
/// </summary>
public sealed class BackdateWorklistRowCreated : DomainEventBase
{
    public override string EventType => "BackdateWorklistRowCreated";

    public required Guid WorklistId { get; init; }
    public required string EmployeeId { get; init; }

    /// <summary><c>EXPORTED_MONTH</c> | <c>SETTLED_YEAR</c>.</summary>
    public required string Kind { get; init; }

    // EXPORTED_MONTH keys (null for SETTLED_YEAR).
    public int? Year { get; init; }
    public int? Month { get; init; }
    /// <summary>The <c>payroll_export_records.export_id</c> this row points at — a REFERENCE, never a FK (ADR-034).</summary>
    public Guid? ExportId { get; init; }

    // SETTLED_YEAR keys (null for EXPORTED_MONTH).
    public string? EntitlementType { get; init; }
    public int? EntitlementYear { get; init; }

    /// <summary><c>PROFILE_CHANGE</c> | <c>AGREEMENT_CODE_CHANGE</c> | <c>EMPLOYMENT_CATEGORY_CHANGE</c>.</summary>
    public required string TriggerKind { get; init; }
    /// <summary>The correction event that caused this trigger (the causal link into dated history).</summary>
    public required Guid TriggerEventId { get; init; }
    /// <summary>The correction's effective date — the backdate.</summary>
    public required DateOnly TriggerEffectiveFrom { get; init; }

    /// <summary>EXPORTED_MONTH: the export record's <c>content_hash</c> at append time.</summary>
    public string? BaselineContentHash { get; init; }
    /// <summary>SETTLED_YEAR: the active settlement's <c>sequence</c> at append time.</summary>
    public int? BaselineSettlementSequence { get; init; }
    /// <summary>SETTLED_YEAR: the active settlement's <c>settlement_state</c> at append time.</summary>
    public string? BaselineSettlementState { get; init; }

    /// <summary>false = a NEW open row was inserted; true = this trigger was APPENDED to an existing open row.</summary>
    public required bool Appended { get; init; }

    /// <summary>The worklist row's version after this write (1 on insert; the ETag the resolve endpoint expects).</summary>
    public required long RowVersion { get; init; }
}
