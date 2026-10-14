using System.Text.Json.Serialization;

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
/// In plain terms: inputs that are DATA (each piece's part-time fraction and agreement, the
/// opening flex balance, the hours rows) are stored with the calculation or pinned by id.
/// Inputs that are CODE (agreement configs, the arithmetic version) cannot be stored as
/// such, so only their fingerprint is stored, and a replay whose code has changed is refused
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
/// <param name="ConfigFingerprints">
/// The fingerprints the Rule Engine returned for the agreement configs it actually resolved,
/// one per call. A replay is refused when the engine's fingerprint no longer matches, because
/// the configs are code, not data.
/// </param>
/// <param name="EvaluationModel">
/// The evaluation-model marker the Rule Engine returned (for example
/// <c>weekly-timeline-v1</c>). A replay is refused when it no longer matches.
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
    [property: JsonRequired] IReadOnlyList<string> ConfigFingerprints,
    [property: JsonRequired] string EvaluationModel);

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
    string? Position);
