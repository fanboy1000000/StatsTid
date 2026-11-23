using System.Text.Json.Serialization;
using StatsTid.SharedKernel.Models;

namespace StatsTid.SharedKernel.Segmentation;

/// <summary>
/// The frozen INPUTS of one segmented calculation, stored beside the manifest so a replay can
/// re-run the calculation on exactly what it was fed (S145 / A7). Carried by
/// <see cref="SegmentManifest.PlanInputs"/>, by the event
/// <see cref="Events.SegmentManifestCreated.PlanInputs"/> (the record of truth, because the
/// projection is rebuildable from events, ADR-016 D10) and by the
/// <c>segment_manifests.plan_inputs_jsonb</c> column.
///
/// <para>
/// In plain terms: every input the calculation was paid on is stored with it or pinned by id: each
/// piece's part-time fraction, agreement, position and organisation, the agreement configuration
/// sent with each piece (<see cref="ConfigRuns"/>), the opening flex balance and the hours rows.
/// Agreement configurations are data that administrators publish and date (ADR-043 D12), so they
/// are frozen by value, and a replay sends exactly these and never looks them up again
/// (ADR-016 D10). Only the arithmetic is code: its version is recorded
/// (<see cref="EvaluationModel"/>), and a replay whose engine answers another version is refused
/// by name rather than silently recomputed.
/// </para>
///
/// <para>
/// <b>Required members (S145 ruling R-18).</b> A missing member is a damaged record, refused at
/// replay; a member that may legitimately be absent is nullable here and omitted in the JSON.
/// Every non-nullable member therefore carries <c>[JsonRequired]</c>, so decoding a stored record
/// that lacks one throws a <see cref="System.Text.Json.JsonException"/> instead of reading the
/// missing member as its default (a missing opening flex balance read as 0 would replay a
/// different month while every digest still matched). <see cref="SettlementCutoff"/> is not
/// required: a null cutoff is legitimate (R41) and is written as an omitted key.
/// <see cref="ConfigRuns"/> is not required either, for the opposite reason: its absence marks a
/// record written before the configurations were frozen, which a replay refuses by name (R-5).
/// </para>
///
/// <para>
/// Pure data; no logic. Equality is by reference for the list members (a record compares its
/// members with their own <c>Equals</c>, and a list does not compare by content), so tests
/// compare the members, not the record.
/// </para>
/// </summary>
/// <param name="Pieces">
/// One <see cref="FrozenPiece"/> for every EMPLOYED day the calculation evaluated: the plan's
/// segments AND the context days the week-by-week evaluation reads around them. A replay builds
/// the employment profile from the piece covering each day; it does not ask the dated resolver.
/// </param>
/// <param name="SettlementCutoff">
/// The frozen settlement cutoff (R41): the employment window's inclusive <c>End</c> when that is
/// on or before the period end, otherwise <c>null</c>. It must be frozen because it cannot be
/// recovered from the plan: the planner turns a window end into a boundary at <c>End + 1</c>, the
/// boundary detector drops any boundary after the period end, and a planned segment carries no
/// window end. A leaver whose last day is the 30th and a stayer therefore have identical pieces
/// and segments for that month, yet the closing week settles in a different month for each.
/// </param>
/// <param name="OpeningFlexBalance">
/// The opening flex balance the calculation used. A replay reads it from here and takes no value
/// from its caller.
/// </param>
/// <param name="EntryEventIds">
/// The <c>TimeEntryRegistered</c> event ids the calculation read, sorted ordinally by string form
/// (the same order the digest lines use). A replay re-reads exactly these rows by id; only an id
/// that is genuinely absent is refused.
/// </param>
/// <param name="AbsenceEventIds">
/// The <c>AbsenceRegistered</c> event ids the calculation read, sorted the same way.
/// </param>
/// <param name="AsOfOutboxId">
/// The highest <c>outbox_id</c> among the rows consumed: the as-of mark of the read. The
/// manifest's <c>CreatedAt</c> is a wall clock at build time, not an as-of, so the binding to
/// the data is the recorded id set together with this mark.
/// </param>
/// <param name="EntriesDigest">
/// Lowercase-hex SHA-256 of the entry rows exactly as read, before any normalization, so a
/// replay can prove the rows are unchanged.
/// </param>
/// <param name="AbsencesDigest">
/// The same digest over the absence rows.
/// </param>
/// <param name="EvaluationModel">
/// The evaluation-model marker the Rule Engine returned (for example
/// <c>weekly-timeline-v2</c>). A replay is refused when it no longer matches.
/// </param>
public sealed record PlanInputs(
    [property: JsonRequired] IReadOnlyList<FrozenPiece> Pieces,
    DateOnly? SettlementCutoff,
    [property: JsonRequired] decimal OpeningFlexBalance,
    [property: JsonRequired] IReadOnlyList<Guid> EntryEventIds,
    [property: JsonRequired] IReadOnlyList<Guid> AbsenceEventIds,
    [property: JsonRequired] long AsOfOutboxId,
    [property: JsonRequired] string EntriesDigest,
    [property: JsonRequired] string AbsencesDigest,
    [property: JsonRequired] string EvaluationModel)
{
    /// <summary>
    /// The agreement configuration sent to the Rule Engine with each of <see cref="Pieces"/>, by value: one
    /// <see cref="FrozenConfigRun"/> per piece, in the pieces' order (<c>ConfigRuns[i]</c> for <c>Pieces[i]</c>),
    /// each with the key of the run the piece lies in and the full configuration in force over it (ADR-043 D12:
    /// every value a calculation used is frozen into its manifest; ADR-016 D10). A replay sends exactly these
    /// and never resolves a configuration again (R42), so a configuration published or re-dated after the
    /// calculation never changes what a replay shows; it reaches a paid month only through an explicit
    /// correction. Two pieces of one run (a part-time fraction change cuts a piece, not a run) each carry the
    /// configuration: one entry per piece keeps replay a plain index match.
    ///
    /// <para>
    /// Optional and NOT <c>[JsonRequired]</c> (S148 ruling R-5), deliberately non-positional: a record written
    /// before the configurations were frozen lacks it and decodes with it null, on the projection row and on the
    /// event alike. A required member would make the event path's decode throw, which the store reads as
    /// "not found", so the same old manifest would be refused two different ways. Instead a replay's
    /// completeness check refuses null by name (<c>replay-manifest-without-frozen-inputs</c>), before anything
    /// is evaluated. An empty list goes with an empty <see cref="Pieces"/>.
    /// </para>
    /// </summary>
    public IReadOnlyList<FrozenConfigRun>? ConfigRuns { get; init; }

    /// <summary>
    /// The frozen provenance of <see cref="OpeningFlexBalance"/> (S146): <c>LEDGER_START</c>,
    /// <c>CHAINED</c>, <c>REHIRE</c> or <c>UNTRACKED</c>. A replay copies it onto its result.
    /// Optional and NOT <c>[JsonRequired]</c>: absent in a record stored before S146, read as
    /// null. Deliberately non-positional so no existing construction changes, and deliberately
    /// outside the completeness check of a stored record.
    /// </summary>
    public string? OpeningFlexProvenance { get; init; }
}

/// <summary>
/// The per-piece profile inputs of one stretch of employed days, frozen into
/// <see cref="PlanInputs.Pieces"/> (S145 / A7): a date range, the part-time fraction and the
/// agreement in force over it, and the position. Both replay lanes build the employment profile
/// from the piece covering a day instead of reading the dated profile history.
///
/// <para>
/// <b>Required members (S145 ruling R-18).</b> A missing member is a damaged record, refused at
/// replay; a member that may legitimately be absent is nullable here and omitted in the JSON. The
/// four non-nullable members carry <c>[JsonRequired]</c>; <see cref="Position"/> does not (null
/// when no position applies, written as an omitted key).
/// </para>
/// </summary>
/// <param name="From">First day of the piece (inclusive).</param>
/// <param name="To">Last day of the piece (inclusive).</param>
/// <param name="PartTimeFraction">The part-time fraction in force over the piece.</param>
/// <param name="AgreementCode">The agreement code in force over the piece.</param>
/// <param name="Position">The position code in force over the piece, or <c>null</c> when none applies.</param>
public sealed record FrozenPiece(
    [property: JsonRequired] DateOnly From,
    [property: JsonRequired] DateOnly To,
    [property: JsonRequired] decimal PartTimeFraction,
    [property: JsonRequired] string AgreementCode,
    string? Position)
{
    /// <summary>
    /// The organisation (institution) the employee belonged to over the piece: the dated record's, never a
    /// request body's (ADR-040 D4). It selects the institution's local profile, so it is part of the key of the
    /// run whose configuration was frozen with the piece (<see cref="FrozenConfigRun.OrgId"/>, ADR-043 D12), and a
    /// replay builds the piece's profile with it without reading the employee's organisation history (R42). No
    /// rule reads it, so it is not sent to the Rule Engine. Optional, NOT <c>[JsonRequired]</c> and
    /// non-positional, so no existing construction changes (S148 ruling R-5): absent in a record written before it
    /// existed, read as null and omitted in the JSON when null, like <see cref="Position"/>; a replay refuses a
    /// piece without one as a manifest without frozen inputs.
    /// </summary>
    public string? OrgId { get; init; }
}

/// <summary>
/// The agreement configuration frozen with one <see cref="FrozenPiece"/>, in
/// <see cref="PlanInputs.ConfigRuns"/>: the key of the run the piece lies in (its dates, agreement, OK
/// version, position and organisation; nothing that selects a configuration changes inside a run) and the
/// <see cref="AgreementRuleConfig"/> the calculation sent to the Rule Engine with that piece, every public
/// member by value (ADR-043 D12, ADR-016 D10). The key repeats the piece's own values, so a replay can check
/// that the entry belongs to its piece before sending the configuration; an entry that does not match is a
/// damaged record, refused by name.
///
/// <para>
/// <b>Required members (S145 ruling R-18, as on <see cref="FrozenPiece"/>).</b> The five non-nullable members
/// carry <c>[JsonRequired]</c>; <see cref="Position"/> and <see cref="OrgId"/> do not (null when none applies,
/// written as an omitted key). A record written before the configurations were frozen lacks the whole
/// <see cref="PlanInputs.ConfigRuns"/> member, never part of an entry.
/// </para>
///
/// <para>
/// <b>What "by value" covers.</b> The event serializer omits a null member, and an omitted member decodes to
/// its initializer. No resolved configuration holds a null member: the one reference-typed member with an
/// initializer, <see cref="AgreementRuleConfig.DefaultCompensationModel"/>, is <c>NOT NULL</c> in
/// <c>agreement_configs</c> and every layer copies it from the central version.
/// </para>
/// </summary>
/// <param name="From">First day of the piece (inclusive).</param>
/// <param name="To">Last day of the piece (inclusive).</param>
/// <param name="AgreementCode">The agreement code of the piece.</param>
/// <param name="OkVersion">The OK version of the piece's days (a piece lies in one OK version, ADR-003).</param>
/// <param name="Position">The position code of the piece, or <c>null</c> when none applies.</param>
/// <param name="OrgId">The organisation of the piece (<see cref="FrozenPiece.OrgId"/>).</param>
/// <param name="Config">The configuration sent with the piece: central → position override → the
/// organisation's local profile, as resolved for the piece's first day.</param>
public sealed record FrozenConfigRun(
    [property: JsonRequired] DateOnly From,
    [property: JsonRequired] DateOnly To,
    [property: JsonRequired] string AgreementCode,
    [property: JsonRequired] string OkVersion,
    string? Position,
    string? OrgId,
    [property: JsonRequired] AgreementRuleConfig Config);
