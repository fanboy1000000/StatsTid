using System.Text.Json;
using Npgsql;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Infrastructure;

/// <summary>
/// S59 / TASK-5905 / ADR-029 — authoritative store + dated read-model for the
/// per-employee entitlement-eligibility fact (CHILD_SICK this sprint; SENIOR_DAY
/// is fully age-derived and is NEVER recorded here — refinement line 117).
///
/// <para>
/// <b>Shape (mirrors <see cref="EmployeeProfileRepository"/>).</b> One dated,
/// version-guarded lineage per <c>(employee_id, entitlement_type)</c> on
/// <c>employee_entitlement_eligibility</c>: re-setting eligibility either updates the
/// live row in-place (same-day, Case B) or closes the predecessor end-exclusive and
/// inserts a new live successor (cross-day, Case C), under a <c>SELECT ... FOR UPDATE</c>
/// lock. The partial-unique-index <c>idx_employee_entitlement_eligibility_live</c>
/// guarantees at most one open (<c>effective_to IS NULL</c>) row per
/// <c>(employee_id, entitlement_type)</c> so dated reads are deterministic (ADR-019/020).
/// </para>
///
/// <para>
/// <b>Absent-row default = INELIGIBLE (opt-in, refinement R1).</b> The absence of a
/// record is meaningful: <see cref="GetEligibleAsOfAsync"/> returns
/// <c>Eligible = false</c> when no row covers the as-of date. There is therefore NO
/// production seed/backfill of eligibility rows — see <see cref="ProjectionBackfillService"/>
/// note in this file's "Projection rebuild" region.
/// </para>
///
/// <para>
/// <b>Atomic-outbox contract (ADR-018 D5/D13).</b> The write path
/// (<see cref="SupersedeAndCreateAsync"/>) is a <c>(conn, tx)</c> overload only — the
/// admin endpoint (TASK-5906) owns the transaction and threads the
/// <see cref="EmployeeEntitlementEligibilitySet"/> outbox enqueue + ADR-026 audit-projection
/// write into the same atomic unit. This repository additionally writes the table-level
/// <c>employee_entitlement_eligibility_audit</c> row inside that same <c>(conn, tx)</c>
/// (it owns that table; mirrors the <c>employee_profile_audit</c> write the
/// EmployeeProfileEndpoints handler performs), accepting actor identity as parameters
/// since the repository has no JWT context.
/// </para>
///
/// <para>
/// <b>Caller census (TASK-5905 / feedback: cross-process caller census).</b> The only
/// consumers are the Backend: Skema GET <c>/month</c> + POST <c>/save</c> enforcement
/// (dated read) and the new admin eligibility endpoint (write). <b>The rule engine never
/// reads eligibility</b> — keeping it Backend-local preserves rule-engine determinism on
/// replay (refinement line 18 / Assumption 7).
/// </para>
/// </summary>
public sealed class EmployeeEntitlementEligibilityRepository
{
    private readonly DbConnectionFactory _dbFactory;

    // Web/camelCase defaults so the audit JSONB snapshot shape matches the rest of the
    // audit surface (employee_profile_audit previous_data/new_data are camelCase too).
    private static readonly JsonSerializerOptions AuditJsonOptions = new(JsonSerializerDefaults.Web);

    public EmployeeEntitlementEligibilityRepository(DbConnectionFactory dbFactory)
    {
        _dbFactory = dbFactory;
    }

    // ------------------------------------------------------------------
    // Dated read — as-of-date resolution (ADR-020). Self-managed connection
    // (pure read; ADR-018 D5 (conn,tx) atomicity does not apply to reads),
    // mirroring EmploymentProfileResolver.GetByEmployeeIdAtAsync.
    // ------------------------------------------------------------------

    /// <summary>
    /// S59 / TASK-5905 — dated eligibility read (ADR-020). Returns the eligibility state
    /// effective on <paramref name="asOf"/> for <paramref name="employeeId"/> +
    /// <paramref name="entitlementType"/>, using the end-exclusive temporal predicate
    /// <c>effective_from &lt;= asOf AND (effective_to IS NULL OR effective_to &gt; asOf)</c>
    /// — identical to <see cref="EmploymentProfileResolver.GetByEmployeeIdAtAsync"/> and the
    /// S29 WageTypeMapping export-time lookup.
    ///
    /// <para>
    /// <b>Absent-row default = INELIGIBLE (opt-in, refinement R1).</b> When no row covers
    /// <paramref name="asOf"/>, returns <see cref="EligibilityResult.Default"/> —
    /// <c>(Eligible: false, RowExists: false)</c>. The effective answer for the caller is
    /// always <see cref="EligibilityResult.Eligible"/>; <see cref="EligibilityResult.RowExists"/>
    /// is exposed only so a caller may distinguish an explicit "set to ineligible" from a
    /// never-recorded employee if useful (e.g. diagnostics) — both resolve to the same
    /// enforcement verdict, satisfying the GET/POST absent-row-parity AC.
    /// </para>
    ///
    /// <para>
    /// Replay-safe: the read keys off <paramref name="asOf"/> (the absence date for POST,
    /// the requested month-end for GET), NOT wall-clock, so a later admin toggle never
    /// retroactively changes a historical verdict (forward-only enforcement).
    /// </para>
    /// </summary>
    public async Task<EligibilityResult> GetEligibleAsOfAsync(
        string employeeId, string entitlementType, DateOnly asOf, CancellationToken ct = default)
    {
        const string sql =
            """
            SELECT eligible
            FROM employee_entitlement_eligibility
            WHERE employee_id = @employeeId
              AND entitlement_type = @entitlementType
              AND effective_from <= @asOf
              AND (effective_to IS NULL OR effective_to > @asOf)
            """;

        await using var conn = _dbFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("entitlementType", entitlementType);
        cmd.Parameters.AddWithValue("asOf", asOf);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            // No dated row → opt-in absent-row default = ineligible (refinement R1).
            return EligibilityResult.Default;
        }
        var eligible = reader.GetBoolean(0);
        return new EligibilityResult(Eligible: eligible, RowExists: true);
    }

    /// <summary>
    /// S59 / Step-7a BLOCKER 1 — read the current LIVE eligibility row (the open
    /// <c>effective_to IS NULL</c> row) for <paramref name="employeeId"/> +
    /// <paramref name="entitlementType"/>, returning its <c>eligible</c> flag together with the
    /// <c>version</c> the admin GET stamps as an ETag so the UI can read-then-If-Match.
    ///
    /// <para>
    /// Returns <c>null</c> when NO live row exists. The caller (admin GET) renders that as the
    /// opt-in absent-row default (ineligible) with NO ETag, signalling the client to use
    /// <c>If-None-Match: *</c> to create. A non-null result carries the version for the
    /// subsequent <c>If-Match</c> toggle. Self-managed connection (pure read).
    /// </para>
    /// </summary>
    public async Task<LiveEligibility?> GetLiveAsync(
        string employeeId, string entitlementType, CancellationToken ct = default)
    {
        const string sql =
            """
            SELECT eligible, effective_from, version
            FROM employee_entitlement_eligibility
            WHERE employee_id = @employeeId
              AND entitlement_type = @entitlementType
              AND effective_to IS NULL
            """;

        await using var conn = _dbFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("entitlementType", entitlementType);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null; // No live row → absent-row default (ineligible), no ETag.

        return new LiveEligibility(
            Eligible: reader.GetBoolean(0),
            EffectiveFrom: reader.GetFieldValue<DateOnly>(1),
            Version: reader.GetInt64(2));
    }

    // ------------------------------------------------------------------
    // Write — atomic-outbox (conn, tx) overload only (ADR-018 D5). 3-case routing
    // (Created / Updated / Superseded) under SELECT ... FOR UPDATE, mirroring
    // EmployeeProfileRepository.SupersedeAndCreateAsync. Consumes the
    // EmployeeEntitlementEligibilitySet event. Writes the table-level audit row in the
    // same (conn, tx). Endpoint emits the outbox event + ADR-026 audit-projection row
    // in the same tx after this returns.
    // ------------------------------------------------------------------

    /// <summary>
    /// S59 / TASK-5905 — versioned eligibility write consuming
    /// <see cref="EmployeeEntitlementEligibilitySet"/>, with ADR-019/020 dated supersession
    /// and admin-strict If-Match optimistic concurrency. Routes under a
    /// <c>SELECT ... FOR UPDATE</c> lock on the live row:
    /// <list type="bullet">
    ///   <item><description><b>Case A — Created.</b> No live row. Allowed only when
    ///     <paramref name="expectedVersion"/> is <c>null</c>. INSERT a fresh row at
    ///     <c>(effective_from = event.EffectiveFrom, effective_to = NULL, version = 1)</c>.</description></item>
    ///   <item><description><b>Case B — Updated.</b> Live row whose <c>effective_from</c>
    ///     equals <c>event.EffectiveFrom</c>. UPDATE-in-place: refresh <c>eligible</c>, bump
    ///     <c>version + 1</c>.</description></item>
    ///   <item><description><b>Case C — Superseded.</b> Live row at an earlier
    ///     <c>effective_from</c>. Close the predecessor end-exclusive
    ///     (<c>effective_to = event.EffectiveFrom</c>; version unchanged), then INSERT a new
    ///     live row at <c>predecessor.version + 1</c> (ETag monotonicity across supersession,
    ///     mirroring EmployeeProfileRepository.InsertLiveRowAsync).</description></item>
    /// </list>
    /// The table-level <c>employee_entitlement_eligibility_audit</c> row is written in the
    /// same <c>(conn, tx)</c> with <c>previous_data</c>/<c>new_data</c> JSONB snapshots and
    /// the version-before/after pair. Caller commits or rolls back; this method does NOT.
    /// </summary>
    /// <exception cref="OptimisticConcurrencyException">
    /// <paramref name="expectedVersion"/> non-null and (a) no live row exists
    /// (<c>ActualVersion = null</c>) or (b) the live row's version differs. Endpoint maps to 412.
    /// </exception>
    /// <exception cref="EligibilityAlreadyExistsException">
    /// S59 / Step-7a BLOCKER 1 — <paramref name="expectedVersion"/> is <c>null</c>
    /// (<c>If-None-Match: *</c> first-create intent) but a LIVE row already exists. The
    /// null-expectedVersion path is now strictly create-only: it must NEVER blind-overwrite
    /// an existing HR-set value. The caller must read the current version and retry with
    /// <c>If-Match: "&lt;version&gt;"</c>. Endpoint maps to 409 Conflict.
    /// </exception>
    /// <exception cref="InvalidEligibilitySupersessionException">
    /// <c>event.EffectiveFrom</c> strictly earlier than the predecessor's
    /// <c>effective_from</c> (backdate rejected, ADR-018 D9 strict-less under end-exclusive).
    /// </exception>
    public async Task<SaveEligibilityResult> SupersedeAndCreateAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        EmployeeEntitlementEligibilitySet @event, long? expectedVersion,
        string actorId, string actorRole, CancellationToken ct = default)
    {
        // 1. Lock the live row (if any). Partial-unique-index guarantees ≤1 match; the
        //    row-level lock serializes concurrent writers on the same (employee, type).
        var predecessor = await AcquireLockAsync(conn, tx, @event.EmployeeId, @event.EntitlementType, ct);

        // 2. Case A — no live row.
        if (predecessor is null)
        {
            if (expectedVersion is not null)
            {
                throw new OptimisticConcurrencyException(
                    $"No live eligibility row exists for employee_id='{@event.EmployeeId}', " +
                    $"entitlement_type='{@event.EntitlementType}', but caller sent " +
                    $"If-Match: \"{expectedVersion.Value}\"; refresh and retry.",
                    expectedVersion: expectedVersion,
                    actualVersion: null);
            }
            Guid createdId;
            long createdVersion;
            try
            {
                (createdId, createdVersion) = await InsertLiveRowAsync(conn, tx, @event, nextVersion: 1L, ct);
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                // S59 / Step-7a cycle-2 — concurrent create race. AcquireLockAsync above
                // locked NO row (none existed yet), so two `If-None-Match: *` writers can both
                // reach this INSERT; the partial-unique live index rejects the loser with a
                // 23505. Translate it into the SAME create-only conflict the existing-live-row
                // guard (3a) raises, so the endpoint returns 409 (not an uncaught 500). The
                // winner inserted version 1; the loser must GET + retry with If-Match.
                throw new EligibilityAlreadyExistsException(
                    $"A live eligibility row was created concurrently for employee_id='{@event.EmployeeId}', " +
                    $"entitlement_type='{@event.EntitlementType}'. If-None-Match: * is create-only; " +
                    $"read the current version and retry with If-Match.",
                    currentVersion: 1L);
            }
            await WriteAuditAsync(
                conn, tx, createdId, @event.EmployeeId, "CREATED",
                previous: null,
                next: new EligibilitySnapshot(@event.EntitlementType, @event.Eligible, @event.EffectiveFrom),
                versionBefore: null, versionAfter: createdVersion,
                actorId, actorRole, ct);
            return new SaveEligibilityResult(createdId, createdVersion, SaveEligibilityOutcome.Created);
        }

        var pred = predecessor.Value;

        // 3a. Create-only guard (S59 / Step-7a BLOCKER 1 — lost-update prevention).
        //     A null expectedVersion encodes `If-None-Match: *` (first-create intent). It must
        //     ONLY succeed when NO live row exists (Case A above). Reaching here means a live
        //     row DOES exist, so a null expectedVersion is a blind-overwrite attempt against an
        //     HR-set value — reject it (no lost update). The caller must read the current
        //     version (GET) and retry with If-Match: "<version>". Endpoint maps to 409.
        if (expectedVersion is null)
        {
            throw new EligibilityAlreadyExistsException(
                $"A live eligibility row already exists for employee_id='{@event.EmployeeId}', " +
                $"entitlement_type='{@event.EntitlementType}' (version {pred.Version}). " +
                $"If-None-Match: * is create-only; read the current version and retry with " +
                $"If-Match: \"{pred.Version}\".",
                currentVersion: pred.Version);
        }

        // 3b. Optimistic concurrency (If-Match) — expectedVersion is non-null here.
        if (pred.Version != expectedVersion.Value)
        {
            throw new OptimisticConcurrencyException(
                $"Eligibility version is {pred.Version}, but caller sent " +
                $"If-Match: \"{expectedVersion.Value}\"; refresh and retry.",
                expectedVersion: expectedVersion,
                actualVersion: pred.Version);
        }

        // 4. Backdate guard (ADR-018 D9 strict-less under end-exclusive).
        if (@event.EffectiveFrom < pred.EffectiveFrom)
        {
            throw new InvalidEligibilitySupersessionException(
                $"Cannot supersede eligibility for employee_id='{@event.EmployeeId}', " +
                $"entitlement_type='{@event.EntitlementType}' with effective_from " +
                $"{@event.EffectiveFrom:yyyy-MM-dd} earlier than predecessor's " +
                $"{pred.EffectiveFrom:yyyy-MM-dd}.");
        }

        var prevSnapshot = new EligibilitySnapshot(@event.EntitlementType, pred.Eligible, pred.EffectiveFrom);
        var nextSnapshot = new EligibilitySnapshot(@event.EntitlementType, @event.Eligible, @event.EffectiveFrom);

        // 5. Case B — same-day in-place UPDATE with version bump.
        if (@event.EffectiveFrom == pred.EffectiveFrom)
        {
            var (updatedId, updatedVersion) = await UpdateInPlaceAsync(conn, tx, pred.Id, @event.Eligible, ct);
            await WriteAuditAsync(
                conn, tx, updatedId, @event.EmployeeId, "UPDATED",
                previous: prevSnapshot, next: nextSnapshot,
                versionBefore: pred.Version, versionAfter: updatedVersion,
                actorId, actorRole, ct);
            return new SaveEligibilityResult(updatedId, updatedVersion, SaveEligibilityOutcome.Updated);
        }

        // 6. Case C — cross-day supersession. Close predecessor end-exclusive (version
        //    unchanged), insert new live row at predecessor.version + 1.
        await ClosePredecessorAsync(conn, tx, pred.Id, @event.EffectiveFrom, ct);
        var (supersededId, supersededVersion) =
            await InsertLiveRowAsync(conn, tx, @event, nextVersion: pred.Version + 1, ct);
        await WriteAuditAsync(
            conn, tx, supersededId, @event.EmployeeId, "SUPERSEDED",
            previous: prevSnapshot, next: nextSnapshot,
            versionBefore: pred.Version, versionAfter: supersededVersion,
            actorId, actorRole, ct);
        return new SaveEligibilityResult(supersededId, supersededVersion, SaveEligibilityOutcome.Superseded);
    }

    // ------------------------------------------------------------------
    // Private helpers — mirror EmployeeProfileRepository's lock / update / close / insert.
    // ------------------------------------------------------------------

    private static async Task<(Guid Id, long Version, DateOnly EffectiveFrom, bool Eligible)?> AcquireLockAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string employeeId, string entitlementType, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            """
            SELECT id, version, effective_from, eligible
            FROM employee_entitlement_eligibility
            WHERE employee_id = @employeeId
              AND entitlement_type = @entitlementType
              AND effective_to IS NULL
            FOR UPDATE
            """, conn, tx);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("entitlementType", entitlementType);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return (
            reader.GetGuid(0),
            reader.GetInt64(1),
            reader.GetFieldValue<DateOnly>(2),
            reader.GetBoolean(3));
    }

    private static async Task<(Guid Id, long Version)> InsertLiveRowAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        EmployeeEntitlementEligibilitySet @event, long nextVersion, CancellationToken ct)
    {
        var newId = Guid.NewGuid();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO employee_entitlement_eligibility (
                id, employee_id, entitlement_type, eligible,
                effective_from, effective_to, version)
            VALUES (
                @id, @employeeId, @entitlementType, @eligible,
                @effectiveFrom, NULL, @version)
            RETURNING id, version
            """, conn, tx);
        cmd.Parameters.AddWithValue("id", newId);
        cmd.Parameters.AddWithValue("employeeId", @event.EmployeeId);
        cmd.Parameters.AddWithValue("entitlementType", @event.EntitlementType);
        cmd.Parameters.AddWithValue("eligible", @event.Eligible);
        cmd.Parameters.AddWithValue("effectiveFrom", @event.EffectiveFrom);
        cmd.Parameters.AddWithValue("version", nextVersion);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            throw new InvalidOperationException(
                $"InsertLiveRowAsync produced no row for employee_id='{@event.EmployeeId}', " +
                $"entitlement_type='{@event.EntitlementType}' at " +
                $"effective_from='{@event.EffectiveFrom:yyyy-MM-dd}'.");
        }
        return (reader.GetGuid(0), reader.GetInt64(1));
    }

    private static async Task<(Guid Id, long Version)> UpdateInPlaceAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid id, bool eligible, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE employee_entitlement_eligibility SET
                eligible = @eligible,
                version = version + 1,
                updated_at = NOW()
            WHERE id = @id
            RETURNING id, version
            """, conn, tx);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("eligible", eligible);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            throw new InvalidOperationException(
                $"UpdateInPlaceAsync produced no row for id='{id}'; FOR UPDATE invariant violated.");
        }
        return (reader.GetGuid(0), reader.GetInt64(1));
    }

    private static async Task ClosePredecessorAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid id, DateOnly closeDate, CancellationToken ct)
    {
        // Close end-exclusive (ADR-018 D9): predecessor window becomes
        // [effective_from, closeDate). Version UNCHANGED — close is lifecycle, not a
        // content edit. updated_at refreshed for observability.
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE employee_entitlement_eligibility
               SET effective_to = @closeDate, updated_at = NOW()
             WHERE id = @id
            """, conn, tx);
        cmd.Parameters.AddWithValue("closeDate", closeDate);
        cmd.Parameters.AddWithValue("id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task WriteAuditAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid eligibilityId, string employeeId, string action,
        EligibilitySnapshot? previous, EligibilitySnapshot next,
        long? versionBefore, long? versionAfter,
        string actorId, string actorRole, CancellationToken ct)
    {
        var previousData = previous is null
            ? (object)DBNull.Value
            : JsonSerializer.Serialize(previous, AuditJsonOptions);
        var newData = JsonSerializer.Serialize(next, AuditJsonOptions);

        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO employee_entitlement_eligibility_audit (
                eligibility_id, employee_id, action,
                previous_data, new_data,
                version_before, version_after,
                actor_id, actor_role)
            VALUES (
                @eligibilityId, @employeeId, @action,
                @previousData::jsonb, @newData::jsonb,
                @versionBefore, @versionAfter,
                @actorId, @actorRole)
            """, conn, tx);
        cmd.Parameters.AddWithValue("eligibilityId", eligibilityId);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("action", action);
        cmd.Parameters.AddWithValue("previousData", previousData);
        cmd.Parameters.AddWithValue("newData", newData);
        cmd.Parameters.AddWithValue("versionBefore", (object?)versionBefore ?? DBNull.Value);
        cmd.Parameters.AddWithValue("versionAfter", (object?)versionAfter ?? DBNull.Value);
        cmd.Parameters.AddWithValue("actorId", actorId);
        cmd.Parameters.AddWithValue("actorRole", actorRole);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}

// ----------------------------------------------------------------------
// Result / request shapes.
// ----------------------------------------------------------------------

/// <summary>
/// S59 / TASK-5905 — result of <see cref="EmployeeEntitlementEligibilityRepository.GetEligibleAsOfAsync"/>.
/// <see cref="Eligible"/> is the authoritative enforcement verdict (absent row ⇒ <c>false</c>,
/// opt-in default per refinement R1). <see cref="RowExists"/> lets a caller distinguish an
/// explicit set from a never-recorded employee if useful; both resolve to the same verdict.
/// </summary>
public readonly record struct EligibilityResult(bool Eligible, bool RowExists)
{
    /// <summary>Absent-row default: ineligible, no row (opt-in, refinement R1).</summary>
    public static EligibilityResult Default => new(Eligible: false, RowExists: false);
}

/// <summary>
/// S59 / Step-7a BLOCKER 1 — current LIVE eligibility row returned by
/// <see cref="EmployeeEntitlementEligibilityRepository.GetLiveAsync"/>. <see cref="Version"/>
/// is the value the admin GET stamps as an ETag so the UI can compose a coherent
/// <c>If-Match</c> on the subsequent toggle. A <c>null</c> return from <c>GetLiveAsync</c>
/// (no live row) is rendered as the absent-row default (ineligible) with no ETag.
/// </summary>
public readonly record struct LiveEligibility(bool Eligible, DateOnly EffectiveFrom, long Version);

/// <summary>
/// S59 / TASK-5905 — JSONB snapshot shape for <c>employee_entitlement_eligibility_audit</c>
/// <c>previous_data</c> / <c>new_data</c>.
/// </summary>
public sealed record EligibilitySnapshot(string EntitlementType, bool Eligible, DateOnly EffectiveFrom);

/// <summary>
/// S59 / TASK-5905 — result of
/// <see cref="EmployeeEntitlementEligibilityRepository.SupersedeAndCreateAsync"/>.
/// <see cref="Outcome"/> discriminates the ADR-020 D2 routing branch so the endpoint can
/// stamp the right audit action / response code; the table-level audit row is already
/// written by the repository.
/// </summary>
public sealed record SaveEligibilityResult(Guid Id, long Version, SaveEligibilityOutcome Outcome);

/// <summary>S59 / TASK-5905 — ADR-020 D2 routing discriminator.</summary>
public enum SaveEligibilityOutcome
{
    /// <summary>Case A — no live row existed; INSERT produced a brand-new live row.</summary>
    Created,
    /// <summary>Case B — live row existed at the same <c>effective_from</c>; UPDATE-in-place with version bump.</summary>
    Updated,
    /// <summary>Case C — live row existed at an earlier <c>effective_from</c>; predecessor closed end-exclusive, new live row inserted.</summary>
    Superseded,
}

/// <summary>
/// S59 / TASK-5905 — thrown when an eligibility supersession's <c>effective_from</c> is
/// strictly earlier than the predecessor's (backdate rejected, ADR-018 D9). Mirrors
/// <c>InvalidProfileSupersessionException</c>. Endpoint maps to 400/422.
/// </summary>
public sealed class InvalidEligibilitySupersessionException : Exception
{
    public InvalidEligibilitySupersessionException(string message) : base(message) { }
}

/// <summary>
/// S59 / Step-7a BLOCKER 1 — thrown by
/// <see cref="EmployeeEntitlementEligibilityRepository.SupersedeAndCreateAsync"/> when a
/// <c>null</c> expectedVersion (<c>If-None-Match: *</c> first-create intent) is sent but a
/// LIVE row already exists. The null-expectedVersion path is strictly create-only — it must
/// never blind-overwrite an HR-set value (lost-update prevention). The caller reads the
/// current version (via the admin GET) and retries with <c>If-Match: "&lt;version&gt;"</c>.
/// Endpoint maps to 409 Conflict.
/// </summary>
public sealed class EligibilityAlreadyExistsException : Exception
{
    /// <summary>The version of the existing live row; the client should retry with this as If-Match.</summary>
    public long CurrentVersion { get; }

    public EligibilityAlreadyExistsException(string message, long currentVersion) : base(message)
    {
        CurrentVersion = currentVersion;
    }
}
