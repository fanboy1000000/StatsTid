namespace StatsTid.Backend.Api.Contracts;

// S141 / TASK-14113 (refinement C2, PAT-012 typed contracts) — the employment-history wire shapes.
// Serialized camelCase via the .NET 8 minimal-API JsonSerializerDefaults.Web default.
//
// WHY THESE EXIST AS NAMED RECORDS RATHER THAN AN ANONYMOUS BODY. The CI convention gate
// (tools/check_openapi_convention.py) HARD-FAILS any NEW operation whose success response has no
// schema in the committed spec — an untyped `Results.Ok(new {...})` lands in the spec with an empty
// 200 content, and that emptiness IS the detection signal. The gate exists because the same
// FE/BE shape-mismatch bug recurred three times (S97 → S99 → S100) with the lesson written down,
// because nothing enforced it. So the history endpoint declares `.Produces<EmploymentHistoryResponse>`.
//
// ADR-040 D7 CHECK, DONE DELIBERATELY RATHER THAN ASSUMED. D7 forbids EMPLOYMENT dates (hire /
// end date) in any DTO, response or error body. Nothing here carries one: `effectiveFrom` /
// `effectiveTo` are the validity bounds of a PROFILE or AGREEMENT-CODE record — when a job title or
// an agreement started applying — which is a different fact from when the person was hired or left.
// The same distinction the backdate worklist already relies on (BackdateWorklistResponses.cs), and
// the same guard rail: this is an HROrAbove-only surface, so no employee-facing DTO reaches it.

/// <summary>
/// Where one history interval sits relative to today. A deliberately small, closed vocabulary — the
/// screen must be able to render "not yet in force" DIFFERENTLY from "in force now", and a boolean
/// would collapse past and scheduled into the same "not current".
/// </summary>
public static class EmploymentHistoryIntervalStatus
{
    /// <summary>The interval has ENDED: <c>effectiveTo &lt;= today</c> (end-exclusive, ADR-018 D9).</summary>
    public const string Past = "PAST";

    /// <summary>The interval covers today: <c>effectiveFrom &lt;= today</c> and it has not ended.</summary>
    public const string Current = "CURRENT";

    /// <summary>
    /// The interval has NOT STARTED yet: <c>effectiveFrom &gt; today</c>. Possible since S141 wave 1
    /// made a future-dated change legal (ADR-040 D8) — a change HR has scheduled but which is not in
    /// force. It is shown, and shown as not-yet-in-force; see the endpoint's own note on why hiding it
    /// would be the worst possible place to hide it.
    ///
    /// <para><b>A CANCELLED scheduled change never appears with this status.</b> Retiring a scheduled
    /// row closes it to zero width rather than deleting it, and the read drops zero-width rows
    /// entirely — so a change somebody called off is absent from the history rather than displayed as
    /// still forthcoming. There is deliberately no fourth "RETIRED" status: an interval covering no
    /// days was never in force and never will be, and a history of EFFECTIVE PERIODS should not carry
    /// one. The retirement itself is separately audited.</para>
    /// </summary>
    public const string Scheduled = "SCHEDULED";
}

/// <summary>
/// The field names that may appear in <c>ChangedFields</c>. Named constants rather than loose strings
/// so the screen and the server cannot drift on spelling — a mismatch there shows up as a silently
/// empty "what changed" column, which looks like "nothing changed" rather than like a bug.
/// </summary>
public static class EmploymentHistoryFields
{
    public const string PartTimeFraction = "partTimeFraction";
    public const string Position = "position";
    public const string EmploymentCategory = "employmentCategory";
    public const string AgreementCode = "agreementCode";
}

/// <summary>
/// One interval of the employee's PROFILE timeline: the values that applied, the dates they applied
/// between, and what changed at the start of this interval.
/// </summary>
/// <param name="EffectiveFrom">First day this interval applies.</param>
/// <param name="EffectiveTo">
/// END-EXCLUSIVE upper bound (ADR-018 D9): the first day this interval NO LONGER applies. <c>null</c>
/// = open-ended (the live row). A consumer that renders this as an inclusive "until" date will show
/// every interval one day too long.
/// </param>
/// <param name="Status">One of <see cref="EmploymentHistoryIntervalStatus"/>.</param>
/// <param name="IsInitial">
/// True only when this really is the employee's FIRST recorded interval — nothing precedes it,
/// inside the window or outside it. Its <paramref name="ChangedFields"/> is then empty, and that
/// emptiness means "there is nothing to compare against", not "nothing changed".
///
/// <para><b>Corrected at the S141 sprint-end review.</b> This used to mean "first row in the window",
/// so a windowed read announced a false "first registration" for an interval that plainly had
/// predecessors. The server now fetches the row before the window purely as a comparison baseline
/// (it is never returned), so the flag answers the question a reader actually asks.</para>
/// </param>
/// <param name="ChangedFields">
/// Which fields differ from the IMMEDIATELY PRECEDING interval, from
/// <see cref="EmploymentHistoryFields"/> — including when that predecessor falls OUTSIDE the
/// requested window. Empty on a genuinely initial interval; empty on a later interval means the
/// boundary carried no field change at all (possible when a backdated edit split a row and a
/// still-later edit restored the values) — reported honestly rather than hidden.
/// </param>
public sealed record EmploymentProfileHistoryInterval(
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    string Status,
    bool IsInitial,
    IReadOnlyList<string> ChangedFields,
    decimal PartTimeFraction,
    string? Position,
    string EmploymentCategory);

/// <summary>
/// One interval of the employee's AGREEMENT-CODE timeline. Same interval semantics as
/// <see cref="EmploymentProfileHistoryInterval"/>; the only dated fact is the code itself.
///
/// <para><b>No <c>okVersion</c>:</b> since ADR-040 D4 the OK version is a pure function of a DATE,
/// not a stored column, and one agreement interval can span an OK-version transition — one stamped
/// version per interval would therefore be wrong for exactly the intervals where it matters most.</para>
/// </summary>
public sealed record AgreementCodeHistoryInterval(
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    string Status,
    bool IsInitial,
    IReadOnlyList<string> ChangedFields,
    string AgreementCode);

/// <summary>
/// <c>GET /api/hr/employees/{employeeId}/history</c> — the whole answer to "what has changed for this
/// employee, and when did it take effect?".
///
/// <para><b>Two parallel tracks, not one merged list</b>, because they are two independent dated
/// records with independent boundaries. Merging them would require intersecting the two interval sets
/// and inventing composite intervals that no stored row corresponds to; keeping them parallel lets the
/// screen interleave by date for display while the server never asserts a boundary the data does not
/// have.</para>
/// </summary>
/// <param name="EmployeeId">The subject. Echoed so a cached or logged response is self-describing.</param>
/// <param name="Today">
/// The server day every <c>status</c> in this response was derived against — ONE date for the whole
/// response (PAT-028), so a request crossing midnight cannot age one track against one day and the
/// other against the next. It is the UTC day, which is the SAME derivation the profile and
/// agreement-code write validators use, so a change saved as "today" can never come back marked
/// SCHEDULED.
/// </param>
/// <param name="WindowFrom">The <c>from</c> filter actually applied, or null when unbounded.</param>
/// <param name="WindowTo">The <c>to</c> filter actually applied (END-EXCLUSIVE), or null when unbounded.</param>
/// <param name="ProfileHistory">Profile intervals, ordered by effective date ascending.</param>
/// <param name="AgreementCodeHistory">Agreement-code intervals, ordered by effective date ascending.</param>
public sealed record EmploymentHistoryResponse(
    string EmployeeId,
    DateOnly Today,
    DateOnly? WindowFrom,
    DateOnly? WindowTo,
    IReadOnlyList<EmploymentProfileHistoryInterval> ProfileHistory,
    IReadOnlyList<AgreementCodeHistoryInterval> AgreementCodeHistory);
