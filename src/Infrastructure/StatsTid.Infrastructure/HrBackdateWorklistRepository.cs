using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Npgsql;
using NpgsqlTypes;
using StatsTid.Infrastructure.Outbox;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Calendar;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Infrastructure;

// ═══════════════════════════════════════════════════════════════════════════════════════════
// S138 / TASK-13803 (ADR-040 D8, Increment 3 — temporal editing): the HR backdate diagnostic
// worklist. THIS FILE holds the storage-shape models, the pure derived-flag logic and the
// repository together (the TASK-13803 dispatch scope names this single Infrastructure file; the
// types are small and belong to one aggregate — split them out when a second consumer appears).
//
// PLAIN-LANGUAGE WHAT: when HR corrects history ("her part-time fraction actually changed on the
// 10th, not today"), the months already SENT TO PAYROLL and the holiday years already SETTLED
// are now stale. Nothing recalculates automatically (ADR-013 — no cascade). Instead each such
// month / year gets a row on this worklist so HR can see exactly what to re-plan (the payroll
// correction path) or reverse-then-re-settle (ADR-033 D4/D5), and then record what they did.
// It is a LIST, not a workflow.
// ═══════════════════════════════════════════════════════════════════════════════════════════

/// <summary>
/// The correction that triggers worklist rows — the FIXED Wave-1 ↔ Wave-2 contract: the two
/// write endpoints (TASK-13802) construct one of these per dated-history write and hand it to
/// <see cref="HrBackdateWorklistRepository.WriteForExportedMonthsAsync"/> and
/// <see cref="HrBackdateWorklistRepository.WriteForSettledYearsAsync"/> in their transaction.
/// </summary>
/// <param name="Kind">One of <see cref="WorklistTriggerKinds"/>.</param>
/// <param name="EventId">The causing correction event (e.g. <c>EmployeeProfileSuperseded</c>) — the causal link into dated history.</param>
/// <param name="EffectiveFrom">The correction's effective date — the backdate.</param>
/// <param name="ActorId">Who made the correction (becomes <c>created_by</c> on a new row; recorded per trigger).</param>
public sealed record WorklistTrigger(string Kind, Guid EventId, DateOnly EffectiveFrom, string ActorId);

/// <summary>The three correction kinds that can land a worklist row (refinement rev 4, TASK-13803).</summary>
public static class WorklistTriggerKinds
{
    public const string ProfileChange = "PROFILE_CHANGE";
    public const string AgreementCodeChange = "AGREEMENT_CODE_CHANGE";
    public const string EmploymentCategoryChange = "EMPLOYMENT_CATEGORY_CHANGE";

    public static bool IsKnown(string? kind) =>
        string.Equals(kind, ProfileChange, StringComparison.Ordinal)
        || string.Equals(kind, AgreementCodeChange, StringComparison.Ordinal)
        || string.Equals(kind, EmploymentCategoryChange, StringComparison.Ordinal);
}

/// <summary>The two row kinds of <c>hr_backdate_worklist</c> (the <c>kind</c> CHECK).</summary>
public static class WorklistKinds
{
    public const string ExportedMonth = "EXPORTED_MONTH";
    public const string SettledYear = "SETTLED_YEAR";
}

/// <summary>The operator's resolution verbs (the <c>resolution</c> CHECK).</summary>
public static class WorklistResolutions
{
    public const string Recalculated = "RECALCULATED";
    public const string Dismissed = "DISMISSED";

    public static bool IsKnown(string? resolution) =>
        string.Equals(resolution, Recalculated, StringComparison.Ordinal)
        || string.Equals(resolution, Dismissed, StringComparison.Ordinal);
}

/// <summary>
/// One element of the row's <c>triggers</c> JSONB array — a correction that touched the row,
/// with the BASELINE it saw at its own append time (never inherited from an earlier trigger):
/// the export record's <c>content_hash</c> for an EXPORTED_MONTH row, the active settlement's
/// <c>sequence</c> + <c>settlement_state</c> for a SETTLED_YEAR row. Serialized camelCase via
/// <see cref="WorklistTriggerJson"/>.
/// </summary>
public sealed record StoredWorklistTrigger(
    string Kind,
    Guid EventId,
    DateOnly EffectiveFrom,
    DateTimeOffset AppendedAt,
    string ActorId,
    string? BaselineContentHash,
    int? BaselineSettlementSequence,
    string? BaselineSettlementState);

/// <summary>
/// What the referenced export record / settlement tuple looks like NOW — read alongside the row
/// so the derived flags compare "then" (per-trigger baselines) against "now" without a second
/// round-trip. <see cref="CurrentContentHash"/> is null when the row is not EXPORTED_MONTH (or
/// the export record is gone); the settlement members are null/empty when not SETTLED_YEAR.
/// </summary>
public sealed record WorklistCurrentState(
    string? CurrentContentHash,
    int? HighestSettlementSequence,
    IReadOnlyCollection<int> ReversedSettlementSequences)
{
    public static readonly WorklistCurrentState Empty = new(null, null, Array.Empty<int>());
}

/// <summary>A stored <c>hr_backdate_worklist</c> row + its current-state companion.</summary>
public sealed record HrBackdateWorklistRow(
    Guid WorklistId,
    string EmployeeId,
    string Kind,
    int? Year,
    int? Month,
    Guid? ExportId,
    string? EntitlementType,
    int? EntitlementYear,
    IReadOnlyList<StoredWorklistTrigger> Triggers,
    DateTimeOffset CreatedAt,
    string CreatedBy,
    DateTimeOffset? ResolvedAt,
    string? ResolvedBy,
    string? Resolution,
    string? ResolutionReason,
    long Version,
    WorklistCurrentState Current);

/// <summary>The resolver's identity for <see cref="HrBackdateWorklistRepository.ResolveAsync"/>.</summary>
public sealed record WorklistActor(string ActorId, string? ActorRole, Guid? CorrelationId);

/// <summary>Outcome of <see cref="HrBackdateWorklistRepository.ResolveAsync"/> — the post-write state the endpoint echoes (ETag = <see cref="NewVersion"/>).</summary>
public sealed record WorklistResolveResult(
    Guid WorklistId,
    string EmployeeId,
    string Resolution,
    DateTimeOffset ResolvedAt,
    long VersionBefore,
    long NewVersion);

/// <summary>Thrown by <see cref="HrBackdateWorklistRepository.ResolveAsync"/> when the row was already resolved (the endpoint maps it to 409).</summary>
public sealed class BackdateWorklistAlreadyResolvedException : Exception
{
    public Guid WorklistId { get; }

    public BackdateWorklistAlreadyResolvedException(Guid worklistId)
        : base($"Backdate worklist row {worklistId} is already resolved.")
    {
        WorklistId = worklistId;
    }
}

/// <summary>The one JSON codec for the <c>triggers</c> JSONB column (camelCase, nulls omitted).</summary>
public static class WorklistTriggerJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    public static string Serialize(IReadOnlyList<StoredWorklistTrigger> triggers) =>
        JsonSerializer.Serialize(triggers, Options);

    public static IReadOnlyList<StoredWorklistTrigger> Deserialize(string json) =>
        JsonSerializer.Deserialize<List<StoredWorklistTrigger>>(json, Options)
        ?? new List<StoredWorklistTrigger>();
}

/// <summary>
/// The PURE derived-field logic of the worklist (DB-free; pinned in
/// <c>tests/StatsTid.Tests.Unit/Worklist</c>). Everything here is a function of a row + the
/// current state read with it — never of the operator's resolution verb.
///
/// <list type="bullet">
///   <item><b>recalcBlockedBy</b> — which known limitation stops the payroll correction re-plan
///     TODAY for an EXPORTED_MONTH row: <c>QUAL-149</c> when a PROFILE / EMPLOYMENT_CATEGORY
///     trigger's <c>effectiveFrom</c> lies STRICTLY INSIDE the month (a re-plan would produce
///     ≥ 2 EMPLOYED segments, which the live rule set refuses); <c>QUAL-150</c> when an
///     AGREEMENT_CODE trigger's <c>effectiveFrom</c> lies strictly inside the month (the
///     wage-type key is plan-wide, so no correct recalc exists). "Strictly inside" = after the
///     1st and on or before the last day: a change ON the 1st coincides with the period start
///     (one segment), a change on the last day still splits the month. Both are OUT this sprint
///     (owner ruling OQ-3 (a)) and made VISIBLE here. SETTLED_YEAR rows are never blocked this
///     way — their fix is ADR-033's reverse-then-re-settle, not a re-plan.</item>
///   <item><b>recalculatedSince</b> (EXPORTED_MONTH) — per trigger: the export record's CURRENT
///     <c>content_hash</c> differs from the baseline THAT trigger captured (corrections advance
///     the hash in place via <c>PayrollExportRecordRepository.UpdateCurrentEffectiveLinesAsync</c>,
///     so this is exact). Row-level: true only when EVERY trigger's baseline differs — a trigger
///     appended after the last recalc keeps the row honest.</item>
///   <item><b>reversedSince</b> (SETTLED_YEAR) — per trigger: the tuple's highest current
///     <c>sequence</c> exceeds the trigger's baseline sequence (ADR-033 D4/D5's correct fix is
///     ONE tx that marks the old row REVERSED and inserts SETTLED at sequence + 1) OR the
///     baseline row itself is now REVERSED (a bare reversal, no re-settle yet). Row-level: every
///     trigger.</item>
/// </list>
/// </summary>
public static class BackdateWorklistDerivation
{
    /// <summary>The re-plan-refusal register id for a profile / category split inside an exported month.</summary>
    public const string RecalcBlockedProfileSplit = "QUAL-149";

    /// <summary>The register id for a plan-wide wage-type key change inside an exported month.</summary>
    public const string RecalcBlockedAgreementKey = "QUAL-150";

    /// <summary>True when <paramref name="date"/> is after the 1st and on or before the last day of (year, month).</summary>
    public static bool IsStrictlyInsideMonth(DateOnly date, int year, int month)
    {
        var first = new DateOnly(year, month, 1);
        var last = first.AddMonths(1).AddDays(-1);
        return date > first && date <= last;
    }

    /// <summary>The blocking register id for ONE trigger of an EXPORTED_MONTH row, or null when that trigger does not block the re-plan.</summary>
    public static string? RecalcBlockedByForTrigger(string triggerKind, DateOnly effectiveFrom, int year, int month)
    {
        if (!IsStrictlyInsideMonth(effectiveFrom, year, month))
            return null;

        return triggerKind switch
        {
            WorklistTriggerKinds.ProfileChange => RecalcBlockedProfileSplit,
            WorklistTriggerKinds.EmploymentCategoryChange => RecalcBlockedProfileSplit,
            WorklistTriggerKinds.AgreementCodeChange => RecalcBlockedAgreementKey,
            _ => null,
        };
    }

    /// <summary>The SET (distinct, stable order) of blocking register ids over all triggers of the row; empty for SETTLED_YEAR rows.</summary>
    public static IReadOnlyList<string> RecalcBlockedBy(HrBackdateWorklistRow row)
    {
        if (!string.Equals(row.Kind, WorklistKinds.ExportedMonth, StringComparison.Ordinal)
            || row.Year is not int year || row.Month is not int month)
            return Array.Empty<string>();

        var set = new List<string>(2);
        foreach (var t in row.Triggers)
        {
            var id = RecalcBlockedByForTrigger(t.Kind, t.EffectiveFrom, year, month);
            if (id is not null && !set.Contains(id, StringComparer.Ordinal))
                set.Add(id);
        }
        set.Sort(StringComparer.Ordinal);
        return set;
    }

    /// <summary>Per-trigger: the current export hash is known and differs from the trigger's captured baseline.</summary>
    public static bool RecalculatedSinceForTrigger(string? currentContentHash, string? baselineContentHash) =>
        currentContentHash is not null
        && baselineContentHash is not null
        && !string.Equals(currentContentHash, baselineContentHash, StringComparison.Ordinal);

    /// <summary>Per-trigger over a row; null when the row is not EXPORTED_MONTH.</summary>
    public static bool? RecalculatedSinceForTrigger(HrBackdateWorklistRow row, StoredWorklistTrigger trigger) =>
        string.Equals(row.Kind, WorklistKinds.ExportedMonth, StringComparison.Ordinal)
            ? RecalculatedSinceForTrigger(row.Current.CurrentContentHash, trigger.BaselineContentHash)
            : null;

    /// <summary>Row-level: EVERY trigger's baseline differs from the current hash; null when not EXPORTED_MONTH.</summary>
    public static bool? RecalculatedSince(HrBackdateWorklistRow row)
    {
        if (!string.Equals(row.Kind, WorklistKinds.ExportedMonth, StringComparison.Ordinal))
            return null;
        if (row.Triggers.Count == 0)
            return false;
        foreach (var t in row.Triggers)
        {
            if (!RecalculatedSinceForTrigger(row.Current.CurrentContentHash, t.BaselineContentHash))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Per-trigger: a later settlement sequence exists (re-settle leg) OR the baseline row is now
    /// REVERSED (bare-reversal leg). <c>null</c> = UNKNOWN: the trigger carries no baseline.
    ///
    /// <para>
    /// Why null and not false (S138 post-close, Codex NOTE absorbed): a trigger has no baseline only
    /// on the degraded write path — the caller reported a skip but this repository's stricter lookup
    /// saw no active settlement, so the row was raised as a note that the settlement state is
    /// inconsistent rather than 500-ing away a valid correction. Answering <c>false</c> there tells
    /// HR "nothing has been reversed since", which is a claim the system cannot make. A diagnostic
    /// list that reassures when it does not know is worse than one that says so. The wire field is
    /// already <c>bool?</c> (null = not applicable on EXPORTED_MONTH rows), so "unknown" rides the
    /// same nullable without a contract change.
    /// </para>
    /// </summary>
    public static bool? ReversedSinceForTrigger(
        int? highestCurrentSequence, IReadOnlyCollection<int> reversedSequences, int? baselineSequence)
    {
        if (baselineSequence is not int baseline)
            return null;
        if (highestCurrentSequence is int highest && highest > baseline)
            return true;
        return reversedSequences.Contains(baseline);
    }

    /// <summary>Per-trigger over a row; null when the row is not SETTLED_YEAR, or when the trigger has no baseline (unknown).</summary>
    public static bool? ReversedSinceForTrigger(HrBackdateWorklistRow row, StoredWorklistTrigger trigger) =>
        string.Equals(row.Kind, WorklistKinds.SettledYear, StringComparison.Ordinal)
            ? ReversedSinceForTrigger(
                row.Current.HighestSettlementSequence,
                row.Current.ReversedSettlementSequences,
                trigger.BaselineSettlementSequence)
            : null;

    /// <summary>
    /// Row-level: EVERY trigger's baseline settlement has been superseded or reversed.
    /// <c>false</c> as soon as one trigger is definitely NOT reversed; <c>null</c> when the row is not
    /// SETTLED_YEAR, or when no trigger is definitely-not-reversed but at least one is UNKNOWN (no
    /// baseline) — the row cannot honestly claim "all reversed" from a baseline it never saw.
    /// </summary>
    public static bool? ReversedSince(HrBackdateWorklistRow row)
    {
        if (!string.Equals(row.Kind, WorklistKinds.SettledYear, StringComparison.Ordinal))
            return null;
        if (row.Triggers.Count == 0)
            return false;
        var anyUnknown = false;
        foreach (var t in row.Triggers)
        {
            var reversed = ReversedSinceForTrigger(
                row.Current.HighestSettlementSequence,
                row.Current.ReversedSettlementSequences,
                t.BaselineSettlementSequence);
            if (reversed is false)
                return false;
            if (reversed is null)
                anyUnknown = true;
        }
        return anyUnknown ? null : true;
    }

    /// <summary>
    /// Whether the half-open correction interval <c>[from, toExclusive)</c> (null upper = open-ended)
    /// touches the CLOSED calendar window <c>[windowStart, windowEndInclusive]</c>.
    /// </summary>
    public static bool IntervalIntersectsClosedWindow(
        DateOnly from, DateOnly? toExclusive, DateOnly windowStart, DateOnly windowEndInclusive) =>
        from <= windowEndInclusive && (toExclusive is null || toExclusive.Value > windowStart);

    /// <summary>
    /// <b>Half (b) of the settled-year date rule — the GEOMETRY.</b> Does the correction interval
    /// touch the entitlement year's ACCRUAL window OR its TAKING window (per-type geometry from the
    /// shared <see cref="EntitlementPeriodResolver"/> — SPECIAL_HOLIDAY's entitlement year is the
    /// accrual year whose taking window opens the FOLLOWING May, S80/8001; <paramref name="resetMonth"/>
    /// is ignored for it)? A fraction backdate into either window can change that year's consumption
    /// or earning under a frozen ADR-033 disposition (refinement discovery 6). Composed with half (a)
    /// by <see cref="SettledYearThreatened"/> — neither half is the rule on its own.
    /// </summary>
    public static bool SettledYearIntersects(
        string entitlementType, int resetMonth, int entitlementYear, DateOnly from, DateOnly? toExclusive)
    {
        var period = EntitlementPeriodResolver.ResolveForYear(entitlementType, resetMonth, entitlementYear);
        return IntervalIntersectsClosedWindow(from, toExclusive, period.AccrualStart, period.AccrualEnd)
            || IntervalIntersectsClosedWindow(from, toExclusive, period.TakingStart, period.Boundary);
    }

    /// <summary>
    /// <b>Half (a) of the settled-year date rule — the settlement's VALUATION BOUNDARY.</b>
    ///
    /// <para>
    /// Plain language: a settlement is a photograph. At one moment we valued a holiday year and
    /// froze the numbers (ADR-033). A correction can only make that photograph wrong if it changes
    /// something on or before the LAST DAY the settlement counted. So this half asks exactly one thing:
    /// <i>does the correction reach back past the moment we froze this settlement?</i>
    /// </para>
    ///
    /// <para>
    /// <b>Unknown boundary ⇒ this half says "maybe", i.e. true.</b>
    /// <paramref name="settlementBoundaryDate"/> is <c>null</c> when the settlement's snapshot
    /// carries no usable <c>settlementBoundaryDate</c> (an unset value serializes as
    /// <c>0001-01-01</c> and is treated as absent — the same fail-closed reading as the §26 payout
    /// path). We cannot place the valuation boundary, so we do not let this half exonerate the
    /// settlement: "the flag is the honesty".
    /// </para>
    ///
    /// <para>
    /// NOT the whole rule — see <see cref="SettledYearThreatened"/>. Alone it over-flags: a narrow
    /// correction deep in the past predates EVERY settlement frozen since, so correcting one week of
    /// 2020 would flag 2020, 2021, 2022 and every year after it.
    /// </para>
    /// </summary>
    /// <param name="correctedIntervalStart">The written row's <c>effective_from</c> — the earliest day the correction changes.</param>
    /// <param name="settlementBoundaryDate">The ACTIVE settlement's valuation boundary — the LAST DAY it counted,
    /// inclusive — or <c>null</c> when it cannot be read.</param>
    public static bool CorrectionReachesSettlementBoundary(
        DateOnly correctedIntervalStart, DateOnly? settlementBoundaryDate) =>
        settlementBoundaryDate is not { } boundary || correctedIntervalStart <= boundary;

    /// <summary>
    /// S138 / TASK-13810 (owner ruling 2026-09-03, amending OQ-2 (i)) — <b>THE settled-year DATE
    /// rule: both halves, conjoined.</b> A row is raised when the correction reaches back before the
    /// settlement's valuation boundary AND the corrected interval actually overlaps that year's
    /// entitlement window.
    ///
    /// <para>
    /// <b>Why it takes both, in plain language.</b> Each half alone over-flags, on a DIFFERENT axis,
    /// and the two failure modes are independent — so neither one is a "narrower version" of the
    /// other and dropping either brings its own false positives back:
    /// <list type="bullet">
    ///   <item><b>Geometry alone</b> fires for an ordinary PRESENT-DAY edit, because a settled year's
    ///     taking window commonly still runs months past today (VACATION's runs to 31 December). "Her
    ///     fraction changes today" would raise a row about a year nothing can have changed.</item>
    ///   <item><b>The boundary test alone</b> fires for EVERY settlement valued after an old correction.
    ///     Correcting one week of 2020 would raise a row for 2020, 2021, 2022 and every year since —
    ///     six dismissals where one row is true.</item>
    /// </list>
    /// Conjoined they say the only thing that is actually true of a threatened settlement:
    /// <i>this correction reaches into a year that was already frozen.</i> A diagnostic list that is
    /// usually wrong is one HR learns to ignore, at which point it also stops surfacing the genuine
    /// rows — which is the failure this whole worklist exists to prevent.
    /// </para>
    ///
    /// <para>
    /// This is the DATE rule only. A SETTLED_YEAR row is ALSO raised, unconditionally and with no
    /// geometry test at all, whenever the revaluation ACTUALLY SKIPPED a (type, year) group — see
    /// <see cref="HrBackdateWorklistRepository.WriteForSkippedSettledYearsAsync"/>, which explains
    /// why that path must not be conjoined with anything.
    /// </para>
    /// </summary>
    public static bool SettledYearThreatened(
        string entitlementType,
        int resetMonth,
        int entitlementYear,
        DateOnly from,
        DateOnly? toExclusive,
        DateOnly? settlementBoundaryDate) =>
        CorrectionReachesSettlementBoundary(from, settlementBoundaryDate)
        && SettledYearIntersects(entitlementType, resetMonth, entitlementYear, from, toExclusive);
}

/// <summary>
/// S138 / TASK-13803 — DB-facing surface for <c>hr_backdate_worklist</c>: the two IN-TRANSACTION
/// writers the Wave-2 correction endpoints call (fixed signatures), the reads the HR endpoints
/// serve, and the resolve write. Every write emits its event on <c>employee-{employeeId}</c>
/// through the atomic outbox in the CALLER'S transaction (ADR-018 D3) and projects the ADR-026
/// audit row in the same tx (D12) — the <c>EmploymentEndDateLifecycleWriter</c> precedent for an
/// Infrastructure-side emitter. This repository NEVER commits or rolls back the caller's tx.
///
/// <para>
/// <b>Cross-context READS only (ADR-034).</b> <c>payroll_export_records</c> is Payroll-owned:
/// this repository reads it (the ApprovalEndpoints precedent) to select exported months and
/// capture the baseline hash; it never writes it and the worklist carries no FK to it.
/// </para>
///
/// <para>
/// <b>Selection semantics.</b> Exported months = every export record of the employee whose
/// (year, month) intersects <c>[from, toExclusive)</c>; a null upper bound is open-ended and is
/// clipped at the CURRENT Copenhagen month (an export for a later month cannot exist yet).
/// </para>
///
/// <para>
/// <b>Settled years — TWO independent paths (S138 / TASK-13810, owner ruling 2026-09-03 amending
/// OQ-2 (i)).</b> Both write the SAME row kind against the SAME partial UNIQUE, so a year reached
/// by both in one request ends up as ONE row with TWO triggers.
/// <list type="number">
///   <item><b>The DATE path</b> (<see cref="WriteForSettledYearsAsync"/>) — every
///     (entitlement_type, entitlement_year) with an ACTIVE settlement row (highest sequence,
///     state ≠ REVERSED — PENDING_REVIEW counts: it is a frozen disposition in progress) that the
///     correction THREATENS. "Threatens" is TWO conditions conjoined: the correction starts before
///     the settlement's VALUATION BOUNDARY — the last day it counted (the settle-time snapshot's
///     <c>settlementBoundaryDate</c>) AND the corrected interval overlaps that year's accrual or
///     taking window. Each alone over-flags on a different axis — see
///     <see cref="BackdateWorklistDerivation.SettledYearThreatened"/>. When either half cannot be
///     evaluated (an unreadable boundary; no live config to place the window) that half flags
///     CONSERVATIVELY: a dismissable false positive beats a silently-missed stale settlement —
///     "the flag is the honesty".</item>
///   <item><b>The SKIP path</b> (<see cref="WriteForSkippedSettledYearsAsync"/>) — every group the
///     caller's revaluation ACTUALLY declined to re-record because that year is settled, with NO
///     date test of any kind. This is what keeps a withheld correction from being silent, and it is
///     deliberately unconditional where the date path is conjoined (that path predicts; this one
///     reports).</item>
/// </list>
/// Only revaluing callers (the profile / category corrections) use the second path; an
/// agreement-code correction runs no revaluation, so it uses the date path alone — the asymmetry
/// is spelled out on <see cref="WriteForSkippedSettledYearsAsync"/>.
/// </para>
///
/// <para>
/// <b>Append-on-open.</b> An open row already existing for the key (the partial UNIQUE) receives
/// the trigger APPENDED to <c>triggers</c> (baseline captured NOW, not inherited) and a version
/// bump; the same <see cref="BackdateWorklistRowCreated"/> event type is emitted with
/// <c>Appended = true</c>. Single-statement <c>INSERT … ON CONFLICT … DO UPDATE</c> against the
/// partial index, so two writers racing on the same key cannot both insert.
/// </para>
/// </summary>
public sealed class HrBackdateWorklistRepository
{
    private readonly DbConnectionFactory _connectionFactory;
    private readonly IOutboxEnqueue _outbox;
    private readonly IAuditProjectionMapper<BackdateWorklistRowCreated> _createdAuditMapper;
    private readonly IAuditProjectionMapper<BackdateWorklistRowResolved> _resolvedAuditMapper;
    private readonly AuditProjectionRepository _auditRepo;
    private readonly TimeProvider _timeProvider;

    public HrBackdateWorklistRepository(
        DbConnectionFactory connectionFactory,
        IOutboxEnqueue outbox,
        IAuditProjectionMapper<BackdateWorklistRowCreated> createdAuditMapper,
        IAuditProjectionMapper<BackdateWorklistRowResolved> resolvedAuditMapper,
        AuditProjectionRepository auditRepo,
        TimeProvider timeProvider)
    {
        _connectionFactory = connectionFactory;
        _outbox = outbox;
        _createdAuditMapper = createdAuditMapper;
        _resolvedAuditMapper = resolvedAuditMapper;
        _auditRepo = auditRepo;
        _timeProvider = timeProvider;
    }

    // ── SQL (literal constants only — CA2100 discipline; one statement per method) ──────────

    // Cross-context READ of the Payroll-owned lock table (ADR-034): the employee's export
    // records whose month-start falls in [lo, hi). make_date(year, month, 1) is the month key.
    private const string SelectExportedMonthsSql =
        """
        SELECT export_id, year, month, content_hash
        FROM payroll_export_records
        WHERE employee_id = @employeeId
          AND make_date(year, month, 1) >= @lo
          AND make_date(year, month, 1) < @hi
        ORDER BY year, month
        """;

    // The ACTIVE settlement per tuple: highest sequence AND not REVERSED (the ADR-033 D5 state
    // machine keeps the two aligned; both predicates are stated so a drift would exclude, not
    // include).
    //
    // S138 / TASK-13810 — the row also yields its VALUATION BOUNDARY (the LAST DAY it counted,
    // INCLUSIVE — S138 Step-7a corrected both the name and the comparison): the immutable settle-time
    // snapshot's `settlementBoundaryDate` (VacationSettlementSnapshot.SettlementBoundaryDate — the
    // asOf the settlement was valued at). Read as JSONB TEXT and parsed in C# rather than cast with
    // `::date` in SQL, so a legacy or malformed value degrades to "unknown" (⇒ flag conservatively)
    // instead of failing the whole correction with a Postgres cast error.
    private const string SelectActiveSettlementsSql =
        """
        SELECT v.entitlement_type, v.entitlement_year, v.sequence, v.settlement_state,
               v.snapshot ->> 'settlementBoundaryDate' AS settlement_boundary_date
        FROM vacation_settlements v
        WHERE v.employee_id = @employeeId
          AND v.settlement_state <> 'REVERSED'
          AND v.sequence = (
                SELECT MAX(s.sequence)
                FROM vacation_settlements s
                WHERE s.employee_id = v.employee_id
                  AND s.entitlement_type = v.entitlement_type
                  AND s.entitlement_year = v.entitlement_year)
        ORDER BY v.entitlement_type, v.entitlement_year
        """;

    // The live (open) entitlement config's reset month for (type, agreement, ok) — the same
    // predicate as EntitlementConfigRepository.GetCurrentOpenAsync. Places the entitlement-period
    // window for half (b) of the settled-year date rule.
    private const string SelectLiveResetMonthSql =
        """
        SELECT reset_month
        FROM entitlement_configs
        WHERE entitlement_type = @entitlementType
          AND agreement_code = @agreementCode
          AND ok_version = @okVersion
          AND effective_to IS NULL
        """;

    // Subject facts — deliberately NO is_active filter (HR corrects deactivated leavers; the
    // terminated-inclusive role gate governs WHO may write, not which employees exist).
    // primary_org_id is the ADR-026 audit projection's TARGET tenant; agreement_code / ok_version
    // key the live entitlement config whose reset_month places the window.
    private const string SelectSubjectSql =
        """
        SELECT primary_org_id, agreement_code, ok_version
        FROM users
        WHERE user_id = @employeeId
        """;

    // Insert-or-append against the EXPORTED_MONTH partial unique (one OPEN row per month).
    // @trigger is a ONE-ELEMENT jsonb array so `||` appends the element on conflict.
    private const string UpsertExportedMonthSql =
        """
        INSERT INTO hr_backdate_worklist
            (worklist_id, employee_id, kind, year, month, export_id, triggers, created_by)
        VALUES
            (@worklistId, @employeeId, 'EXPORTED_MONTH', @year, @month, @exportId, @trigger, @actorId)
        ON CONFLICT (employee_id, year, month) WHERE resolved_at IS NULL AND kind = 'EXPORTED_MONTH'
        DO UPDATE SET triggers = hr_backdate_worklist.triggers || EXCLUDED.triggers,
                      version  = hr_backdate_worklist.version + 1
        RETURNING worklist_id, version
        """;

    // Insert-or-append against the SETTLED_YEAR partial unique (one OPEN row per year).
    private const string UpsertSettledYearSql =
        """
        INSERT INTO hr_backdate_worklist
            (worklist_id, employee_id, kind, entitlement_type, entitlement_year, triggers, created_by)
        VALUES
            (@worklistId, @employeeId, 'SETTLED_YEAR', @entitlementType, @entitlementYear, @trigger, @actorId)
        ON CONFLICT (employee_id, entitlement_type, entitlement_year) WHERE resolved_at IS NULL AND kind = 'SETTLED_YEAR'
        DO UPDATE SET triggers = hr_backdate_worklist.triggers || EXCLUDED.triggers,
                      version  = hr_backdate_worklist.version + 1
        RETURNING worklist_id, version
        """;

    // The ONE read: rows + their current-state companions. Parameter-driven predicates (typed
    // NULL = "no filter") keep this a single literal statement for every read shape.
    private const string SelectRowsSql =
        """
        SELECT w.worklist_id, w.employee_id, w.kind, w.year, w.month, w.export_id,
               w.entitlement_type, w.entitlement_year, w.triggers::text AS triggers_text,
               w.created_at, w.created_by, w.resolved_at, w.resolved_by, w.resolution,
               w.resolution_reason, w.version,
               per.content_hash AS current_content_hash,
               vs.highest_sequence,
               vs.reversed_sequences
        FROM hr_backdate_worklist w
        JOIN users u ON u.user_id = w.employee_id
        LEFT JOIN payroll_export_records per ON per.export_id = w.export_id
        LEFT JOIN LATERAL (
            SELECT MAX(s.sequence) AS highest_sequence,
                   COALESCE(ARRAY_AGG(s.sequence) FILTER (WHERE s.settlement_state = 'REVERSED'), ARRAY[]::INT[]) AS reversed_sequences
            FROM vacation_settlements s
            WHERE w.kind = 'SETTLED_YEAR'
              AND s.employee_id = w.employee_id
              AND s.entitlement_type = w.entitlement_type
              AND s.entitlement_year = w.entitlement_year
        ) vs ON TRUE
        WHERE (@openOnly = FALSE OR w.resolved_at IS NULL)
          AND (@employeeId IS NULL OR w.employee_id = @employeeId)
          AND (@allOrgs OR u.primary_org_id = ANY(@orgIds))
          AND (@worklistId IS NULL OR w.worklist_id = @worklistId)
        ORDER BY w.created_at,
                 -- Rows raised by ONE correction share a created_at by construction (PAT-024: one
                 -- clock per transaction), so created_at alone leaves them tied and the old
                 -- worklist_id tiebreak ordered a HUMAN's list by random UUID. Order the tie by the
                 -- PERIOD the row is about — the only order that means anything to the person
                 -- working the list — and keep worklist_id last so the sort stays total.
                 -- (S138 Step-7a CI: two month-ordering pins failed on exactly this.)
                 COALESCE(w.year, w.entitlement_year) NULLS LAST,
                 w.month NULLS FIRST,
                 w.entitlement_type NULLS FIRST,
                 w.worklist_id
        """;

    // Resolve, step 1: lock the row (FOR UPDATE) — the canonical snapshot for the If-Match check.
    private const string LockRowSql =
        """
        SELECT employee_id, kind, year, month, export_id, entitlement_type, entitlement_year,
               jsonb_array_length(triggers) AS trigger_count, resolved_at, version
        FROM hr_backdate_worklist
        WHERE worklist_id = @worklistId
        FOR UPDATE
        """;

    // Resolve, step 2: the guarded versioned write (ADR-019 D2 — WHERE version = expected).
    private const string ResolveRowSql =
        """
        UPDATE hr_backdate_worklist
        SET resolved_at = NOW(),
            resolved_by = @actorId,
            resolution = @resolution,
            resolution_reason = @reason,
            version = version + 1
        WHERE worklist_id = @worklistId
          AND resolved_at IS NULL
          AND version = @expectedVersion
        RETURNING resolved_at, version
        """;

    // ── The two IN-TX writers (FIXED signatures — consumed verbatim by TASK-13802) ───────────

    /// <summary>
    /// Lands / appends an EXPORTED_MONTH row for every <c>payroll_export_records</c> row of the
    /// employee whose month intersects <c>[from, toExclusive)</c> (null = open-ended, clipped at
    /// the current Copenhagen month). Emits <see cref="BackdateWorklistRowCreated"/> per affected
    /// row in the caller's tx. Returns the affected row ids (new or appended), in month order.
    /// </summary>
    public async Task<IReadOnlyList<Guid>> WriteForExportedMonthsAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        string employeeId,
        WorklistTrigger trigger,
        DateOnly from,
        DateOnly? toExclusive,
        CancellationToken ct)
    {
        ValidateTrigger(trigger);
        if (toExclusive is { } upper && upper <= from)
            return Array.Empty<Guid>();

        var lo = new DateOnly(from.Year, from.Month, 1);
        var hi = toExclusive ?? FirstDayOfNextMonth(CopenhagenBusinessDate.Today(_timeProvider));
        if (hi <= lo)
            return Array.Empty<Guid>();

        var exports = await ReadExportedMonthsAsync(conn, tx, employeeId, lo, hi, ct);
        if (exports.Count == 0)
            return Array.Empty<Guid>();

        var subject = await ReadSubjectAsync(conn, tx, employeeId, ct);
        var appendedAt = _timeProvider.GetUtcNow();
        var ids = new List<Guid>(exports.Count);

        foreach (var export in exports)
        {
            var stored = new StoredWorklistTrigger(
                Kind: trigger.Kind,
                EventId: trigger.EventId,
                EffectiveFrom: trigger.EffectiveFrom,
                AppendedAt: appendedAt,
                ActorId: trigger.ActorId,
                BaselineContentHash: export.ContentHash,
                BaselineSettlementSequence: null,
                BaselineSettlementState: null);

            var (worklistId, version) = await UpsertExportedMonthAsync(
                conn, tx, employeeId, export, stored, trigger.ActorId, ct);

            var created = new BackdateWorklistRowCreated
            {
                WorklistId = worklistId,
                EmployeeId = employeeId,
                Kind = WorklistKinds.ExportedMonth,
                Year = export.Year,
                Month = export.Month,
                ExportId = export.ExportId,
                TriggerKind = trigger.Kind,
                TriggerEventId = trigger.EventId,
                TriggerEffectiveFrom = trigger.EffectiveFrom,
                BaselineContentHash = export.ContentHash,
                Appended = version > 1,
                RowVersion = version,
                ActorId = trigger.ActorId,
            };
            await EmitAsync(conn, tx, employeeId, subject.PrimaryOrgId, created, _createdAuditMapper, ct);
            ids.Add(worklistId);
        }

        return ids;
    }

    /// <summary>
    /// The DATE path (owner ruling 2026-09-03, amending OQ-2 (i); conjunction ruled the same day):
    /// lands / appends a SETTLED_YEAR row for every (entitlement_type, entitlement_year) of the
    /// employee that has an ACTIVE settlement the correction THREATENS — i.e. the correction starts
    /// on or before that settlement's valuation boundary AND the corrected interval overlaps that year's
    /// accrual or taking window. Both halves are required; see
    /// <see cref="BackdateWorklistDerivation.SettledYearThreatened"/> for why each alone over-flags
    /// on its own axis. Emits <see cref="BackdateWorklistRowCreated"/> per affected row in the
    /// caller's tx. Returns the affected row ids.
    /// </summary>
    public async Task<IReadOnlyList<Guid>> WriteForSettledYearsAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        string employeeId,
        WorklistTrigger trigger,
        DateOnly from,
        DateOnly? toExclusive,
        CancellationToken ct)
    {
        ValidateTrigger(trigger);
        if (toExclusive is { } upper && upper <= from)
            return Array.Empty<Guid>();

        var active = await ReadActiveSettlementsAsync(conn, tx, employeeId, ct);
        if (active.Count == 0)
            return Array.Empty<Guid>();

        var subject = await ReadSubjectAsync(conn, tx, employeeId, ct);
        var resetMonthByType = new Dictionary<string, int?>(StringComparer.Ordinal);
        var selected = new List<ActiveSettlement>(active.Count);

        foreach (var settlement in active)
        {
            if (await ThreatensSettledYearAsync(conn, tx, subject, settlement, from, toExclusive, resetMonthByType, ct))
                selected.Add(settlement);
        }
        if (selected.Count == 0)
            return Array.Empty<Guid>();

        return await UpsertAndEmitSettledYearsAsync(conn, tx, employeeId, subject, trigger, selected, ct);
    }

    /// <summary>
    /// S138 / TASK-13810 — the SKIP path, and the more important half of the 2026-09-03 ruling.
    ///
    /// <para>
    /// <b>Plain language.</b> The date rule above asks "could this correction have changed what the
    /// settlement froze?". This one answers a question that needs no guessing: the revaluation
    /// ALREADY REFUSED to re-record a group's consumption because that holiday year is settled. A
    /// withheld correction that nobody is told about is worse than a false positive — the system
    /// would quietly hold back a change HR believes it made. So every group the revaluation actually
    /// skipped gets a row, whatever the dates say.
    /// </para>
    ///
    /// <para>
    /// <b>Why both paths are needed.</b> A TODAY-forward fraction change cannot alter anything the
    /// settlement valued, so the date rule (correctly) raises nothing — yet it CAN still hit a
    /// settled year, because absences already booked into the FUTURE fall inside a taking window
    /// that is still open, and those belong to a (type, year) group the revaluation skips. Without
    /// this path that skip would be silent.
    /// </para>
    ///
    /// <para>
    /// <b>Why this path is UNCONDITIONAL while the date path is CONJOINED.</b> The date path
    /// (<see cref="WriteForSettledYearsAsync"/>) is a PREDICTION — it reasons from dates about what
    /// a correction might have disturbed — so it must be as accurate as we can make it, which took
    /// two conjoined tests (valuation boundary AND window overlap; see
    /// <see cref="BackdateWorklistDerivation.SettledYearThreatened"/>). This path is not a
    /// prediction at all: it is a REPORT of something the code already did. Adding a geometry test
    /// here would mean second-guessing an action that has definitely happened — the revaluation
    /// refused to re-record this group — and the only possible outcome of that is to withhold a
    /// correction AND withhold the notice. So this path takes no date test of any kind.
    /// </para>
    ///
    /// <para>
    /// <b>The trigger-kind asymmetry, stated here because this is where the two rules diverge.</b>
    /// Only a PROFILE correction (part-time fraction or position) runs a revaluation, so only it can
    /// have a skip to report and only it uses BOTH paths. The other two run none and keep the date
    /// path alone, for two different reasons: an AGREEMENT_CODE change moves the wage-type KEY
    /// (which lønart a line books under, ADR-020), not the consumption divisor; and an
    /// EMPLOYMENT_CATEGORY change is not an input to that divisor either — full-day hours come from
    /// `DailyNormCalculator`, whose config lookup is keyed on (ok-version, agreement code, position,
    /// fraction, org), and category is not in that key. Category selects
    /// <c>role_config_overrides</c> instead, which the norm path does not read.
    /// </para>
    ///
    /// <para>
    /// <b>Tripwire (S138 Step-7a, Reviewer WARNING — this paragraph previously claimed category
    /// corrections revalue, which the endpoint never did).</b> If employment category ever becomes
    /// an input to norm resolution, the profile PUT owes a revaluation for a category-only change
    /// AND owes this path its skipped groups. Today it correctly calls neither.
    /// </para>
    ///
    /// <para>
    /// <b>One row, however many paths hit it.</b> This reuses the SETTLED_YEAR row kind, the same
    /// partial UNIQUE on the open row and the same append-a-trigger upsert, so a year selected by
    /// BOTH paths in one request yields ONE worklist row carrying TWO triggers — two independent
    /// reasons, honestly recorded, not two rows for HR to work twice.
    /// </para>
    /// </summary>
    /// <param name="skipped">
    /// The (entitlement type, entitlement year) groups the caller's revaluation declined to
    /// re-record because an ACTIVE settlement exists. Each must still have that active settlement
    /// in the caller's transaction — the baseline sequence/state captured on the trigger is read
    /// from it, and a tuple that cannot be found is a caller/state defect and fails LOUD.
    /// </param>
    public async Task<IReadOnlyList<Guid>> WriteForSkippedSettledYearsAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        string employeeId,
        WorklistTrigger trigger,
        IReadOnlyCollection<(string EntitlementType, int EntitlementYear)> skipped,
        CancellationToken ct)
    {
        ValidateTrigger(trigger);
        ArgumentNullException.ThrowIfNull(skipped);
        if (skipped.Count == 0)
            return Array.Empty<Guid>();

        var subject = await ReadSubjectAsync(conn, tx, employeeId, ct);
        var active = await ReadActiveSettlementsAsync(conn, tx, employeeId, ct);
        var byKey = new Dictionary<(string, int), ActiveSettlement>(active.Count);
        foreach (var settlement in active)
            byKey[(settlement.EntitlementType, settlement.EntitlementYear)] = settlement;

        var selected = new List<ActiveSettlement>(skipped.Count);
        var seen = new HashSet<(string, int)>();
        foreach (var (type, year) in skipped)
        {
            if (!seen.Add((type, year)))
                continue; // the caller may list a group once per changed dimension; one row, one trigger.
            if (!byKey.TryGetValue((type, year), out var settlement))
            {
                // S138 Step-7a (Reviewer WARNING, absorbed) — do NOT throw here.
                //
                // The revaluation skipped this group because IT saw an active settlement, using
                // `VacationSettlementRepository.GetActiveAsync`'s predicate (`state <> 'REVERSED'`).
                // This lookup reads the STRICTER selection predicate (that, AND highest sequence).
                // Today the ADR-033 D5 state machine plus the single-active unique index keep the
                // two aligned, so a disagreement is unreachable through production writes — but the
                // schema permits it, and throwing here would roll the whole transaction back and
                // answer 500, DESTROYING a valid correction because a diagnostic row could not be
                // decorated. That trade is backwards: the correction is the user's work, the row is
                // our note about it.
                //
                // A stricter-than-the-caller lookup must therefore degrade, not fail. Raise the row
                // with no baseline — `reversedSince` then reads as "unknown", which is the honest
                // answer when the settlement state is inconsistent, and a diagnostic list is exactly
                // the right place for an inconsistency to surface.
                selected.Add(new ActiveSettlement(type, year, Sequence: null, SettlementState: null,
                    SettlementBoundaryDate: null));
                continue;
            }
            selected.Add(settlement);
        }

        return await UpsertAndEmitSettledYearsAsync(conn, tx, employeeId, subject, trigger, selected, ct);
    }

    /// <summary>The shared tail of both SETTLED_YEAR entry points: one upsert + one event per
    /// selected settlement, in the caller's tx, against the SAME partial UNIQUE (so a year reached
    /// by both paths appends rather than duplicates).</summary>
    private async Task<IReadOnlyList<Guid>> UpsertAndEmitSettledYearsAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        string employeeId,
        Subject subject,
        WorklistTrigger trigger,
        IReadOnlyList<ActiveSettlement> selected,
        CancellationToken ct)
    {
        if (selected.Count == 0)
            return Array.Empty<Guid>();

        var appendedAt = _timeProvider.GetUtcNow();
        var ids = new List<Guid>(selected.Count);

        foreach (var settlement in selected)
        {
            var stored = new StoredWorklistTrigger(
                Kind: trigger.Kind,
                EventId: trigger.EventId,
                EffectiveFrom: trigger.EffectiveFrom,
                AppendedAt: appendedAt,
                ActorId: trigger.ActorId,
                BaselineContentHash: null,
                BaselineSettlementSequence: settlement.Sequence,
                BaselineSettlementState: settlement.SettlementState);

            var (worklistId, version) = await UpsertSettledYearAsync(
                conn, tx, employeeId, settlement, stored, trigger.ActorId, ct);

            var created = new BackdateWorklistRowCreated
            {
                WorklistId = worklistId,
                EmployeeId = employeeId,
                Kind = WorklistKinds.SettledYear,
                EntitlementType = settlement.EntitlementType,
                EntitlementYear = settlement.EntitlementYear,
                TriggerKind = trigger.Kind,
                TriggerEventId = trigger.EventId,
                TriggerEffectiveFrom = trigger.EffectiveFrom,
                BaselineSettlementSequence = settlement.Sequence,
                BaselineSettlementState = settlement.SettlementState,
                Appended = version > 1,
                RowVersion = version,
                ActorId = trigger.ActorId,
            };
            await EmitAsync(conn, tx, employeeId, subject.PrimaryOrgId, created, _createdAuditMapper, ct);
            ids.Add(worklistId);
        }

        return ids;
    }

    // ── Reads (self-managed connection) ─────────────────────────────────────────────────────

    /// <summary>The employee's OPEN rows (with current state), oldest first, then by the PERIOD the row concerns.</summary>
    public Task<IReadOnlyList<HrBackdateWorklistRow>> GetOpenAsync(string employeeId, CancellationToken ct = default) =>
        QueryAsync(employeeId, openOnly: true, accessibleOrgIds: null, worklistId: null, ct);

    /// <summary>The employee's rows, open AND resolved (with current state), oldest first, then by the PERIOD the row concerns.</summary>
    public Task<IReadOnlyList<HrBackdateWorklistRow>> GetAllAsync(string employeeId, CancellationToken ct = default) =>
        QueryAsync(employeeId, openOnly: false, accessibleOrgIds: null, worklistId: null, ct);

    /// <summary>
    /// OPEN rows of every employee whose <c>users.primary_org_id</c> is in
    /// <paramref name="accessibleOrgIds"/> — the actor's HR-floored accessible-org set from
    /// <c>OrgScopeValidator.GetAccessibleOrgsAsync(actor, LocalHR)</c>: <c>null</c> = unrestricted
    /// (GlobalAdmin), an EMPTY set = nothing (the endpoint 403s before reaching here).
    /// </summary>
    public Task<IReadOnlyList<HrBackdateWorklistRow>> GetOpenForOrgSubtreeAsync(
        IReadOnlyCollection<string>? accessibleOrgIds, CancellationToken ct = default) =>
        QueryAsync(employeeId: null, openOnly: true, accessibleOrgIds, worklistId: null, ct);

    /// <summary>Open AND resolved rows over the accessible-org set (see <see cref="GetOpenForOrgSubtreeAsync"/>).</summary>
    public Task<IReadOnlyList<HrBackdateWorklistRow>> GetAllForOrgSubtreeAsync(
        IReadOnlyCollection<string>? accessibleOrgIds, CancellationToken ct = default) =>
        QueryAsync(employeeId: null, openOnly: false, accessibleOrgIds, worklistId: null, ct);

    /// <summary>One row by id (open or resolved) with its version — the resolve endpoint's pre-read; null when absent.</summary>
    public async Task<HrBackdateWorklistRow?> GetByIdWithVersionAsync(Guid worklistId, CancellationToken ct = default)
    {
        var rows = await QueryAsync(employeeId: null, openOnly: false, accessibleOrgIds: null, worklistId, ct);
        return rows.Count == 0 ? null : rows[0];
    }

    // ── Resolve (in-tx) ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Records HR's resolution on an OPEN row under the ADR-019 D2 precondition: locks the row,
    /// compares <paramref name="expectedVersion"/> (throws <see cref="OptimisticConcurrencyException"/>
    /// on a stale token, <see cref="KeyNotFoundException"/> when the id is unknown,
    /// <see cref="BackdateWorklistAlreadyResolvedException"/> when already resolved), writes
    /// <c>resolved_at/by/resolution/reason</c> + bumps <c>version</c>, and emits
    /// <see cref="BackdateWorklistRowResolved"/> (+ its ADR-026 row) in the caller's tx. The event
    /// is the audit record of the resolution (no <c>*_audit</c> table by design).
    /// </summary>
    public async Task<WorklistResolveResult> ResolveAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        Guid worklistId,
        long expectedVersion,
        string resolution,
        string reason,
        WorklistActor actor,
        CancellationToken ct)
    {
        if (!WorklistResolutions.IsKnown(resolution))
            throw new ArgumentOutOfRangeException(nameof(resolution), resolution,
                $"Resolution must be {WorklistResolutions.Recalculated} or {WorklistResolutions.Dismissed}.");

        var locked = await LockRowAsync(conn, tx, worklistId, ct)
            ?? throw new KeyNotFoundException($"Backdate worklist row {worklistId} not found.");

        if (locked.ResolvedAt is not null)
            throw new BackdateWorklistAlreadyResolvedException(worklistId);

        if (locked.Version != expectedVersion)
        {
            throw new OptimisticConcurrencyException(
                $"Backdate worklist row {worklistId}: expected version {expectedVersion}, actual {locked.Version}.",
                expectedVersion, locked.Version);
        }

        var (resolvedAt, newVersion) = await UpdateResolvedAsync(
            conn, tx, worklistId, expectedVersion, resolution, reason, actor.ActorId, ct);

        var subject = await ReadSubjectAsync(conn, tx, locked.EmployeeId, ct);

        var resolved = new BackdateWorklistRowResolved
        {
            WorklistId = worklistId,
            EmployeeId = locked.EmployeeId,
            Kind = locked.Kind,
            Year = locked.Year,
            Month = locked.Month,
            ExportId = locked.ExportId,
            EntitlementType = locked.EntitlementType,
            EntitlementYear = locked.EntitlementYear,
            Resolution = resolution,
            Reason = reason,
            TriggerCount = locked.TriggerCount,
            VersionBefore = locked.Version,
            VersionAfter = newVersion,
            ActorId = actor.ActorId,
            ActorRole = actor.ActorRole,
            CorrelationId = actor.CorrelationId,
        };
        await EmitAsync(conn, tx, locked.EmployeeId, subject.PrimaryOrgId, resolved, _resolvedAuditMapper, ct);

        return new WorklistResolveResult(worklistId, locked.EmployeeId, resolution, resolvedAt, locked.Version, newVersion);
    }

    // ── internals ───────────────────────────────────────────────────────────────────────────

    private sealed record ExportedMonth(Guid ExportId, int Year, int Month, string ContentHash);

    /// <summary>The ACTIVE settlement of one (type, year) tuple. <c>SettlementBoundaryDate</c> is the
    /// valuation boundary — the last day it counted — read out of its immutable snapshot; <c>null</c> when the snapshot carries
    /// no usable value (⇒ the date predicate flags conservatively).</summary>
    /// <summary>
    /// One active settlement as the worklist sees it. <paramref name="Sequence"/> and
    /// <paramref name="SettlementState"/> are nullable ONLY for the degraded shape S138 Step-7a
    /// introduced: the skip path is told a group was withheld but this repository's stricter
    /// selection predicate cannot see the row (an inconsistent settlement state the schema permits
    /// and the state machine forbids). The row is still raised — with no baseline, so
    /// <c>reversedSince</c> reads "unknown" — rather than throwing away the caller's correction.
    /// </summary>
    private sealed record ActiveSettlement(
        string EntitlementType, int EntitlementYear, int? Sequence, string? SettlementState, DateOnly? SettlementBoundaryDate);

    private sealed record Subject(string PrimaryOrgId, string AgreementCode, string OkVersion);

    private sealed record LockedRow(
        string EmployeeId, string Kind, int? Year, int? Month, Guid? ExportId,
        string? EntitlementType, int? EntitlementYear, int TriggerCount, DateTimeOffset? ResolvedAt, long Version);

    private static void ValidateTrigger(WorklistTrigger trigger)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        if (!WorklistTriggerKinds.IsKnown(trigger.Kind))
            throw new ArgumentOutOfRangeException(nameof(trigger), trigger.Kind, "Unknown worklist trigger kind.");
        if (string.IsNullOrWhiteSpace(trigger.ActorId))
            throw new ArgumentException("Worklist trigger requires an actor id.", nameof(trigger));
    }

    private static DateOnly FirstDayOfNextMonth(DateOnly date) =>
        new DateOnly(date.Year, date.Month, 1).AddMonths(1);

    private static async Task<IReadOnlyList<ExportedMonth>> ReadExportedMonthsAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string employeeId, DateOnly lo, DateOnly hi, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(SelectExportedMonthsSql, conn, tx);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("lo", lo);
        cmd.Parameters.AddWithValue("hi", hi);
        var result = new List<ExportedMonth>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(new ExportedMonth(
                ExportId: reader.GetGuid(0),
                Year: reader.GetInt32(1),
                Month: reader.GetInt32(2),
                ContentHash: reader.GetString(3)));
        }
        return result;
    }

    private static async Task<IReadOnlyList<ActiveSettlement>> ReadActiveSettlementsAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string employeeId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(SelectActiveSettlementsSql, conn, tx);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        var result = new List<ActiveSettlement>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(new ActiveSettlement(
                EntitlementType: reader.GetString(0),
                EntitlementYear: reader.GetInt32(1),
                Sequence: reader.GetInt32(2),
                SettlementState: reader.GetString(3),
                SettlementBoundaryDate: ParseSettlementBoundaryDate(reader.IsDBNull(4) ? null : reader.GetString(4))));
        }
        return result;
    }

    private static async Task<int?> ReadLiveResetMonthAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string entitlementType, string agreementCode, string okVersion, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(SelectLiveResetMonthSql, conn, tx);
        cmd.Parameters.AddWithValue("entitlementType", entitlementType);
        cmd.Parameters.AddWithValue("agreementCode", agreementCode);
        cmd.Parameters.AddWithValue("okVersion", okVersion);
        var value = await cmd.ExecuteScalarAsync(ct);
        return value is null or DBNull ? null : Convert.ToInt32(value);
    }

    /// <summary>
    /// The snapshot's <c>settlementBoundaryDate</c> as a date, or <c>null</c> when it cannot be
    /// trusted. Two "unusable" shapes are folded together deliberately: the key is ABSENT (a legacy
    /// or hand-seeded snapshot), or it is present but holds <c>0001-01-01</c> — the .NET
    /// <c>default(DateOnly)</c> an uninitialized snapshot serializes. Both mean "we do not know when
    /// this settlement was frozen", and both make the caller flag conservatively. Same fail-closed
    /// reading as the §26 termination-payout path's <c>SettlementBoundaryDate == default</c> guard.
    /// </summary>
    private static DateOnly? ParseSettlementBoundaryDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        if (!DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return null;
        return parsed == default ? null : parsed;
    }

    private static async Task<Subject> ReadSubjectAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string employeeId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(SelectSubjectSql, conn, tx);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            // Fail-loud: every caller resolves the subject before correcting its history, so a
            // missing users row here is a caller bug, not a domain state (the
            // EmploymentWindowResolver stance).
            throw new InvalidOperationException(
                $"Backdate worklist: no users row for employee '{employeeId}' — cannot resolve the audit target org.");
        }
        return new Subject(reader.GetString(0), reader.GetString(1), reader.GetString(2));
    }

    /// <summary>
    /// The per-settlement DATE rule: both halves of
    /// <see cref="BackdateWorklistDerivation.SettledYearThreatened"/>, with the freeze-moment half
    /// evaluated FIRST as a short-circuit — it is free, and it is the half that exonerates the
    /// common case (an ordinary today-dated edit), so that request never touches
    /// <c>entitlement_configs</c> at all.
    /// </summary>
    private static async Task<bool> ThreatensSettledYearAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, Subject subject, ActiveSettlement settlement,
        DateOnly from, DateOnly? toExclusive, Dictionary<string, int?> resetMonthByType, CancellationToken ct)
    {
        // Half (a) — the valuation boundary. Cheap, and decisive for the present-day edit.
        if (!BackdateWorklistDerivation.CorrectionReachesSettlementBoundary(from, settlement.SettlementBoundaryDate))
            return false;

        // Half (b) — the entitlement-window geometry. Needs the live reset_month to place the window.
        int resetMonth;
        if (EntitlementPeriodResolver.IsSpecialHoliday(settlement.EntitlementType))
        {
            resetMonth = 1; // ignored by the resolver for SPECIAL_HOLIDAY (fixed 1 Jan accrual / 1 May taking geometry).
        }
        else
        {
            if (!resetMonthByType.TryGetValue(settlement.EntitlementType, out var cached))
            {
                cached = await ReadLiveResetMonthAsync(
                    conn, tx, settlement.EntitlementType, subject.AgreementCode, subject.OkVersion, ct);
                resetMonthByType[settlement.EntitlementType] = cached;
            }
            if (cached is null)
                return true; // conservative on THIS half only: the window cannot be placed, and (a) already held.
            resetMonth = cached.Value;
        }

        return BackdateWorklistDerivation.SettledYearThreatened(
            settlement.EntitlementType, resetMonth, settlement.EntitlementYear,
            from, toExclusive, settlement.SettlementBoundaryDate);
    }

    private static async Task<(Guid WorklistId, long Version)> UpsertExportedMonthAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string employeeId, ExportedMonth export,
        StoredWorklistTrigger stored, string actorId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(UpsertExportedMonthSql, conn, tx);
        cmd.Parameters.AddWithValue("worklistId", Guid.NewGuid());
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("year", export.Year);
        cmd.Parameters.AddWithValue("month", export.Month);
        cmd.Parameters.AddWithValue("exportId", export.ExportId);
        cmd.Parameters.Add(new NpgsqlParameter("trigger", NpgsqlDbType.Jsonb)
        {
            Value = WorklistTriggerJson.Serialize(new[] { stored }),
        });
        cmd.Parameters.AddWithValue("actorId", actorId);
        return await ReadUpsertResultAsync(cmd, ct);
    }

    private static async Task<(Guid WorklistId, long Version)> UpsertSettledYearAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string employeeId, ActiveSettlement settlement,
        StoredWorklistTrigger stored, string actorId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(UpsertSettledYearSql, conn, tx);
        cmd.Parameters.AddWithValue("worklistId", Guid.NewGuid());
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("entitlementType", settlement.EntitlementType);
        cmd.Parameters.AddWithValue("entitlementYear", settlement.EntitlementYear);
        cmd.Parameters.Add(new NpgsqlParameter("trigger", NpgsqlDbType.Jsonb)
        {
            Value = WorklistTriggerJson.Serialize(new[] { stored }),
        });
        cmd.Parameters.AddWithValue("actorId", actorId);
        return await ReadUpsertResultAsync(cmd, ct);
    }

    private static async Task<(Guid WorklistId, long Version)> ReadUpsertResultAsync(NpgsqlCommand cmd, CancellationToken ct)
    {
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new InvalidOperationException("Backdate worklist upsert returned no row.");
        return (reader.GetGuid(0), reader.GetInt64(1));
    }

    private async Task<IReadOnlyList<HrBackdateWorklistRow>> QueryAsync(
        string? employeeId, bool openOnly, IReadOnlyCollection<string>? accessibleOrgIds, Guid? worklistId, CancellationToken ct)
    {
        if (accessibleOrgIds is { Count: 0 })
            return Array.Empty<HrBackdateWorklistRow>();

        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(SelectRowsSql, conn);
        cmd.Parameters.Add(new NpgsqlParameter("openOnly", NpgsqlDbType.Boolean) { Value = openOnly });
        cmd.Parameters.Add(new NpgsqlParameter("employeeId", NpgsqlDbType.Text) { Value = (object?)employeeId ?? DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter("allOrgs", NpgsqlDbType.Boolean) { Value = accessibleOrgIds is null });
        cmd.Parameters.Add(new NpgsqlParameter("orgIds", NpgsqlDbType.Array | NpgsqlDbType.Text)
        {
            Value = accessibleOrgIds?.ToArray() ?? Array.Empty<string>(),
        });
        cmd.Parameters.Add(new NpgsqlParameter("worklistId", NpgsqlDbType.Uuid) { Value = (object?)worklistId ?? DBNull.Value });

        var rows = new List<HrBackdateWorklistRow>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            rows.Add(ReadRow(reader));
        return rows;
    }

    private static HrBackdateWorklistRow ReadRow(NpgsqlDataReader reader)
    {
        var reversedOrdinal = reader.GetOrdinal("reversed_sequences");
        var reversed = reader.IsDBNull(reversedOrdinal)
            ? Array.Empty<int>()
            : reader.GetFieldValue<int[]>(reversedOrdinal);

        return new HrBackdateWorklistRow(
            WorklistId: reader.GetGuid(reader.GetOrdinal("worklist_id")),
            EmployeeId: reader.GetString(reader.GetOrdinal("employee_id")),
            Kind: reader.GetString(reader.GetOrdinal("kind")),
            Year: GetNullableInt32(reader, "year"),
            Month: GetNullableInt32(reader, "month"),
            ExportId: GetNullableGuid(reader, "export_id"),
            EntitlementType: GetNullableString(reader, "entitlement_type"),
            EntitlementYear: GetNullableInt32(reader, "entitlement_year"),
            Triggers: WorklistTriggerJson.Deserialize(reader.GetString(reader.GetOrdinal("triggers_text"))),
            CreatedAt: ToUtcOffset(reader.GetFieldValue<DateTime>(reader.GetOrdinal("created_at"))),
            CreatedBy: reader.GetString(reader.GetOrdinal("created_by")),
            ResolvedAt: GetNullableTimestamp(reader, "resolved_at"),
            ResolvedBy: GetNullableString(reader, "resolved_by"),
            Resolution: GetNullableString(reader, "resolution"),
            ResolutionReason: GetNullableString(reader, "resolution_reason"),
            Version: reader.GetInt64(reader.GetOrdinal("version")),
            Current: new WorklistCurrentState(
                CurrentContentHash: GetNullableString(reader, "current_content_hash"),
                HighestSettlementSequence: GetNullableInt32(reader, "highest_sequence"),
                ReversedSettlementSequences: reversed));
    }

    private static async Task<LockedRow?> LockRowAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, Guid worklistId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(LockRowSql, conn, tx);
        cmd.Parameters.AddWithValue("worklistId", worklistId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;
        return new LockedRow(
            EmployeeId: reader.GetString(reader.GetOrdinal("employee_id")),
            Kind: reader.GetString(reader.GetOrdinal("kind")),
            Year: GetNullableInt32(reader, "year"),
            Month: GetNullableInt32(reader, "month"),
            ExportId: GetNullableGuid(reader, "export_id"),
            EntitlementType: GetNullableString(reader, "entitlement_type"),
            EntitlementYear: GetNullableInt32(reader, "entitlement_year"),
            TriggerCount: reader.GetInt32(reader.GetOrdinal("trigger_count")),
            ResolvedAt: GetNullableTimestamp(reader, "resolved_at"),
            Version: reader.GetInt64(reader.GetOrdinal("version")));
    }

    private static async Task<(DateTimeOffset ResolvedAt, long NewVersion)> UpdateResolvedAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, Guid worklistId, long expectedVersion,
        string resolution, string reason, string actorId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(ResolveRowSql, conn, tx);
        cmd.Parameters.AddWithValue("worklistId", worklistId);
        cmd.Parameters.AddWithValue("expectedVersion", expectedVersion);
        cmd.Parameters.AddWithValue("resolution", resolution);
        cmd.Parameters.AddWithValue("reason", reason);
        cmd.Parameters.AddWithValue("actorId", actorId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            // The FOR UPDATE snapshot said open + version-matched; a zero-row UPDATE here means the
            // row changed under the lock — impossible under READ COMMITTED with the lock held, so
            // surface it as a concurrency failure rather than a silent no-op.
            throw new OptimisticConcurrencyException(
                $"Backdate worklist row {worklistId}: the guarded resolve matched no row.", expectedVersion, null);
        }
        return (ToUtcOffset(reader.GetFieldValue<DateTime>(0)), reader.GetInt64(1));
    }

    private async Task EmitAsync<TEvent>(
        NpgsqlConnection conn, NpgsqlTransaction tx, string employeeId, string targetOrgId,
        TEvent @event, IAuditProjectionMapper<TEvent> mapper, CancellationToken ct)
        where TEvent : DomainEventBase
    {
        // ADR-018 D3: same tx as the row write. ADR-018 D6: the consolidated employee stream.
        var outboxId = await _outbox.EnqueueAndReturnIdAsync(conn, tx, $"employee-{employeeId}", @event, ct);

        // ADR-026 D2/D12: the sync-in-tx audit projection row. The actor's own org is not part of
        // the fixed writer contract (WorklistTrigger carries only the actor id) → null; the
        // TARGET org (the employee's home Organisation) is what the tenant-visibility filter needs.
        var auditCtx = new AuditProjectionContext(
            ActorId: @event.ActorId,
            ActorPrimaryOrgId: null,
            CorrelationId: @event.CorrelationId,
            OccurredAt: ToUtcOffset(@event.OccurredAt),
            ResolvedTargetOrgId: targetOrgId);
        var rowData = mapper.Map(@event, auditCtx);
        await _auditRepo.InsertAsync(conn, tx, @event.EventId, outboxId, @event.EventType, rowData, auditCtx, ct);
    }

    private static DateTimeOffset ToUtcOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc), TimeSpan.Zero);

    private static int? GetNullableInt32(NpgsqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    private static Guid? GetNullableGuid(NpgsqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);
    }

    private static string? GetNullableString(NpgsqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static DateTimeOffset? GetNullableTimestamp(NpgsqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : ToUtcOffset(reader.GetFieldValue<DateTime>(ordinal));
    }
}
