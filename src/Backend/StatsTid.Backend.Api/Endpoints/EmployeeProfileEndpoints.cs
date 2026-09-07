using System.Text.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Backend.Api.Contracts;
using StatsTid.Backend.Api.Endpoints.Helpers;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Outbox;
using StatsTid.Infrastructure.Security;
using StatsTid.Infrastructure.Temporal;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Calendar;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Exceptions;
using StatsTid.SharedKernel.Interfaces;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Security;

namespace StatsTid.Backend.Api.Endpoints;

/// <summary>
/// S31 / TASK-3107 + S33 / TASK-3308 — Phase 4d-3 Part 2 admin CRUD surface for the
/// authoritative employee profile store. Three endpoints under
/// <c>/api/admin/employee-profiles/{employeeId}</c>:
///   <list type="bullet">
///     <item><description>GET — read the live row, ETag-stamped per ADR-019 D2. UNCHANGED in S33
///       (refinement cycle 1 Reviewer BLOCKER-1 absorption: no <c>?asOf=</c> extension).</description></item>
///     <item><description>
///       PUT — S33 extends with required <c>EffectiveFrom: DateOnly</c>. Routes through
///       <see cref="EmployeeProfileRepository.SupersedeAndCreateAsync"/> (TASK-3302) under
///       admin-strict If-Match + atomic outbox. Emits <see cref="EmployeeProfileUpdated"/> on
///       Case B (same-day in-place edit) or <see cref="EmployeeProfileSuperseded"/> on Case C
///       (cross-day supersession) per ADR-020 D2. Audit row action mirrors the outcome
///       (<c>UPDATED</c> vs <c>SUPERSEDED</c>).
///     </description></item>
///     <item><description>
///       DELETE — S33 / TASK-3308 NEW. Soft-deletes the live row via
///       <see cref="EmployeeProfileRepository.SoftDeleteAsync"/> (TASK-3303) under admin-strict
///       If-Match. Audit row action <c>DELETED</c> with
///       <c>version_before = version_after = predecessor.version</c> per ADR-023 D8
///       (soft-delete is row-state-change, not field-mutation — version intentionally NOT
///       bumped). Emits <see cref="EmployeeProfileSoftDeleted"/> in the same tx (ADR-018 D3).
///       Returns 204 No Content.
///     </description></item>
///   </list>
///
/// <para>
/// <b>Step 0b cycle 1 Codex BLOCKER fix — cross-org HR data-leak prevention.</b>
/// All three endpoints carry <c>RequireAuthorization("HROrAbove")</c> AND an explicit
/// <see cref="OrgScopeValidator.ValidateEmployeeAccessAsync"/> binding to the target
/// <c>employeeId</c>. The policy alone proves role + scope shape but does NOT bind
/// the actor to the target employee's organisation — without OrgScopeValidator an HR
/// user from org X could read/edit/delete profiles of employees in org Y. The cross-org
/// binding is load-bearing.
/// </para>
///
/// <para>
/// <b>ADR-019 admin-strict If-Match contract.</b> PUT + DELETE require
/// <c>If-Match: "&lt;version&gt;"</c> via <see cref="EtagHeaderHelper.TryParseIfMatch"/>
/// (admin-strict mode rejects <c>If-None-Match: *</c>). 428 on missing/malformed;
/// 412 on stale (with structured <c>expectedVersion</c> + <c>actualVersion</c> body
/// per ADR-019 D2); 404 when no live row exists for the employee.
/// </para>
///
/// <para>
/// <b>Case A 404 pre-check on PUT (Step 0b Reviewer BLOCKER-3 absorption).</b>
/// PUT is an admin <i>edit</i> surface — it does NOT create net-new profiles. Before
/// routing through <see cref="EmployeeProfileRepository.SupersedeAndCreateAsync"/>,
/// the endpoint reads the live row via
/// <see cref="EmployeeProfileRepository.GetByEmployeeIdAsync(NpgsqlConnection, NpgsqlTransaction?, string, CancellationToken)"/>;
/// when null, returns 404 immediately. <c>SupersedeAndCreateAsync</c>'s Case A (no-live-row
/// INSERT) is reachable only from <c>AdminEndpoints</c> POST <c>/api/admin/users</c>
/// (S31 TASK-3108 4-way atomicity), NEVER from PUT.
/// </para>
///
/// <para>
/// <b>EffectiveFrom validator — S138 / TASK-13802 (ADR-040 D8 as amended 2026-09-02).</b>
/// Plain language: HR can now say "her fraction actually changed on the 10th, not today".
/// The PUT accepts ANY past-or-today date and refuses only the FUTURE (a date-free 422 —
/// future-dating needs the "current ≠ live" read model and is Increment 4 by owner ruling;
/// until then a not-yet-effective row would be read as "current" by the login token and the
/// <c>users.*</c> caches). The pre-S138 rule was <c>EffectiveFrom == today</c>; it is now
/// <c>EffectiveFrom &lt;= today</c>. The employment-start floor (a date before the hire) is
/// refused by the writer, also date-free — the hire date must never reach the wire.
/// "Today" is the UTC day (not local time), which aligns with the frontend's
/// <c>new Date().toISOString().slice(0,10)</c> UTC extraction. Since S139 / TASK-13907 that day
/// is read from the injected <see cref="TimeProvider"/> (<c>TimeProvider.System</c> in
/// production) rather than <c>DateTime.UtcNow</c> directly — same day, injectable source, so a
/// date-sensitive test host can fix it. The Copenhagen business-date convention used by the
/// settlement / worklist paths is deliberately NOT used here: this validator must agree with the
/// browser's UTC slice, not with the settlement calendar.
/// </para>
///
/// <para>
/// <b>What a backdate does (S138 / TASK-13802).</b> The write routes through the generalized
/// dated writer (TASK-13801): the row COVERING the date is split (or edited in place when it
/// starts on that date), and everything "before"-shaped — the mutation predicate, the audit
/// <c>previous_data</c>, the <see cref="EmployeeProfileSuperseded"/> predecessor fields — is
/// taken from that COVERING row's pre-image, never from the open row (for a backdate the two
/// differ; reading the open row would silently no-op a real correction). Three consequences
/// ride along: the absence revaluation is confined to the written row's interval
/// <c>[NewEffectiveFrom, NewEffectiveTo)</c> and SKIPS any (entitlement type, year) group with
/// an ACTIVE settlement (ADR-033 — a frozen settlement is never silently rewritten); every
/// already-EXPORTED payroll month and every SETTLED holiday year the interval touches lands on
/// the HR diagnostic worklist (TASK-13803, ADR-013: no automatic recalculation); and the scope
/// path is terminated-inclusive with a LocalHR floor, because correcting a departed employee's
/// last months is the common case.
/// </para>
///
/// <para>
/// <b>What the PUT answers with — S138 / TASK-13810 (owner ruling 2026-09-03).</b> The 200 body
/// carries the profile AS OF TODAY, not the values just written, and the same holds on the no-op
/// branch. This resource means "the profile as it stands now" in every other handler, so a
/// backdated correction must not answer as though March's fraction were today's. The dedicated
/// agreement-code endpoint (<c>PUT /api/admin/users/{userId}/agreement-code</c>) already answered
/// this way; the two are now one rule rather than a coincidence. The wire SHAPE is unchanged.
/// </para>
///
/// <para>
/// <b>ADR-023 D8 soft-delete divergence.</b> DELETE soft-deletes the live row by stamping
/// <c>effective_to</c> = today (the UTC day from the injected <see cref="TimeProvider"/>, bound
/// as a SQL parameter since S139 / TASK-13907 — it was the DB-side <c>NOW()::date</c> before,
/// which under a UTC session time zone produced the same day) with the predecessor's
/// <c>version</c> column
/// UNCHANGED — soft-delete is row-state-change, not field-mutation. The audit row
/// accordingly carries <c>version_before = version_after = predecessor.version</c>
/// (deliberate divergence from sibling ADR-019 D8 endpoints — agreement_configs,
/// wage_type_mappings, entitlement_configs — which all bump <c>version + 1</c> on
/// soft-delete). A retry with stale If-Match after a successful soft-delete hits 404
/// (the row "disappeared" from live reads per the partial-unique-index predicate),
/// NOT 412 — this is intentional, locked by D-test
/// <c>SoftDelete_StaleIfMatchAfterSoftDelete_Returns404NotConflict412</c> in TASK-3312.
/// </para>
/// </summary>
public static class EmployeeProfileEndpoints
{
    public static WebApplication MapEmployeeProfileEndpoints(this WebApplication app)
    {
        // ═══════════════════════════════════════════
        // 1. GET /api/admin/employee-profiles/{employeeId}
        //
        // RBAC: HROrAbove policy + OrgScopeValidator binding (Step 0b BLOCKER fix).
        // Returns 404 when no live row exists. On success, sets ETag: "<version>" so
        // the admin UI can compose If-Match on the subsequent PUT / DELETE.
        //
        // S33 / TASK-3308 — UNCHANGED. Refinement cycle 1 Reviewer BLOCKER-1
        // absorption: no `?asOf=` extension on the GET signature.
        // ═══════════════════════════════════════════
        app.MapGet("/api/admin/employee-profiles/{employeeId}", async (
            string employeeId,
            EmployeeProfileRepository repository,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // Step 0b BLOCKER fix — cross-org binding. HROrAbove alone is not enough;
            // bind the actor's scopes to the target employee's organisation.
            // S76 B1: LocalHR floor — the ADMITTING scope must itself be HR+.
            //
            // S138 Step-5a (Reviewer BLOCKER, absorbed): terminated-INCLUSIVE, matching the PUT.
            // The PUT was widened so HR can correct a DEPARTED employee's dated history, but the
            // PUT demands If-Match and THIS GET is the only place its token comes from — so an
            // active-only read here made the sprint's headline capability unreachable through the
            // product (the shared validator resolves the target with an active-only read BEFORE
            // even the GLOBAL-scope short-circuit, so a leaver was 403 "Target employee not found"
            // to in-scope HR and to a GlobalAdmin alike). Safe to widen: this reads a profile row,
            // which carries no employment dates (ADR-040 D7), and the LocalHR floor — unchanged —
            // is what binds privilege. Recorded on the R9c allowlist inventory in OrgScopeValidator.
            var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessIncludingTerminatedAsync(
                actor, employeeId, StatsTidRoles.LocalHR, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            // Step 7a P2 fix — atomic row+version read. Read body fields AND the
            // `version` column from the same SELECT so the ETag stamped on the
            // response matches the data serialized. Pre-fix used two reads
            // (GetByEmployeeIdAsync + ReadLiveVersionAsync) — a concurrent admin
            // edit between the two could have returned stale fields with a NEWER
            // ETag, letting the next If-Match overwrite the racing change.
            var hit = await repository.GetByEmployeeIdWithVersionAsync(employeeId, ct);
            if (hit is null)
                return Results.NotFound(new { error = "Employee profile not found" });
            var (profile, version) = hit.Value;

            context.Response.Headers.ETag = $"\"{version}\"";
            // S112 / TASK-11201 — named record (EmployeeProfileResponse) replaces the anonymous shape;
            // BYTE-IDENTICAL wire JSON (same member names/order/nullability, camelCase Web default).
            return Results.Ok(new EmployeeProfileResponse(
                profile.EmployeeId,
                profile.PartTimeFraction,
                profile.Position,
                profile.IsPartTime,
                version));
        }).RequireAuthorization("HROrAbove")
        .Produces<EmployeeProfileResponse>(StatusCodes.Status200OK);

        // ═══════════════════════════════════════════
        // 2. PUT /api/admin/employee-profiles/{employeeId}
        //
        // RBAC: HROrAbove + OrgScopeValidator (Step 0b BLOCKER fix).
        // Admin-strict If-Match required (ADR-019 D2). Atomic-outbox semantic:
        // (UPDATE-in-place OR close-predecessor + INSERT-new) + INSERT audit row +
        // outbox enqueue all happen in one tx (ADR-018 D3). Returns 200 with new ETag
        // on success.
        //
        // S33 / TASK-3308 changes:
        //   • DTO gains required `EffectiveFrom: DateOnly`; validator rejects anything
        //     other than today (UTC) with 422 per ADR-023 D8.
        //   • Case A 404 pre-check (Step 0b Reviewer BLOCKER-3 absorption) — fail 404
        //     BEFORE routing through SupersedeAndCreateAsync when no live row exists.
        //   • Routes through SupersedeAndCreateAsync (TASK-3302) and discriminates on
        //     `SaveEmployeeProfileOutcome`: Updated → emit EmployeeProfileUpdated +
        //     audit action 'UPDATED' (Case B same-day); Superseded → emit
        //     EmployeeProfileSuperseded + audit action 'SUPERSEDED' (Case C cross-day).
        //
        // Error mapping:
        //   • 422 — backdated or future-dated EffectiveFrom (validator) OR
        //           InvalidProfileSupersessionException defense-in-depth
        //   • 428 — missing / malformed If-Match (EtagHeaderHelper)
        //   • 412 — OptimisticConcurrencyException (stale version, ADR-019 D2)
        //   • 404 — no live row for the employee (pre-check OR KeyNotFoundException)
        //   • 403 — OrgScopeValidator denial (cross-org guard)
        // ═══════════════════════════════════════════
        app.MapPut("/api/admin/employee-profiles/{employeeId}", async (
            string employeeId,
            UpdateEmployeeProfileRequest body,
            EmployeeProfileRepository repository,
            DbConnectionFactory connectionFactory,
            IOutboxEnqueue outbox,
            // S138 / TASK-13802 — the insert-into-a-gap cases (E / G) emit Created, so the PUT
            // now needs the Created mapper alongside Updated + Superseded.
            IAuditProjectionMapper<EmployeeProfileCreated> createdAuditMapper,
            IAuditProjectionMapper<EmployeeProfileUpdated> updatedAuditMapper,
            IAuditProjectionMapper<EmployeeProfileSuperseded> supersededAuditMapper,
            // S66 / TASK-6604 (ADR-032 D4) — profile-change revaluation collaborators.
            IAuditProjectionMapper<EntitlementBalanceRevalued> revaluedAuditMapper,
            StatsTid.Backend.Api.Services.ConsumptionCalculator consumptionCalculator,
            IEmploymentProfileResolver profileResolver,
            AbsenceProjectionRepository absenceProjectionRepo,
            EntitlementBalanceRepository entitlementBalanceRepo,
            EntitlementConfigRepository entitlementConfigRepo,
            // S138 / TASK-13802 — the settled-year SKIP (OQ-2 (i)) + the HR diagnostic worklist.
            VacationSettlementRepository settlementRepo,
            HrBackdateWorklistRepository worklistRepo,
            AuditProjectionRepository auditRepo,
            UserRepository userRepo,
            OrgScopeValidator scopeValidator,
            // S139 / TASK-13907 — the server-"today" seam (TimeProvider.System in production).
            TimeProvider timeProvider,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // Step 0b BLOCKER fix — cross-org binding (mirrors GET above). S76 B1: LocalHR floor.
            // S138 / TASK-13802 — TERMINATED-INCLUSIVE (refinement W6 / Assumption 10): correcting
            // a DEPARTED employee's last months is the common HR case, and this PUT has no
            // `is_active` switch, so widening the read cannot become a reactivation side-door. The
            // LocalHR floor is what actually binds privilege (a leaver's own token is refused
            // outright by the terminated-inclusive validator — SEC-046 holds).
            var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessIncludingTerminatedAsync(
                actor, employeeId, StatsTidRoles.LocalHR, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            // S138 / TASK-13802 — EffectiveFrom PRESENCE guard. `EffectiveFrom` is a non-nullable
            // DateOnly, so a request that OMITS it binds the .NET default 0001-01-01. Pre-S138 the
            // "== today" validator rejected that as a side effect; now that any past date is legal,
            // the sentinel would route as a real (and enormous) correction covering all recorded
            // history. So presence is enforced explicitly — a missing date is a malformed request,
            // not a backdate. (0001-01-01 is also the seeder's sentinel start, which an EDIT
            // surface has no business addressing.)
            if (body.EffectiveFrom == default)
                return Results.UnprocessableEntity(new { error = MissingEffectiveFromError });

            // S138 / TASK-13802 — EffectiveFrom validator, widened from "== today" to "<= today"
            // (ADR-040 D8 as amended: backdating + today now, future-dating in Increment 4).
            // The refusal body is DATE-FREE — the employment-start floor refusal (raised by the
            // writer) shares this shape, and that one must never echo the hire date.
            // S139 / TASK-13907 — "today" now comes from the injected TimeProvider rather than
            // the wall clock, so a fixed-clock test host moves this validator with it. The day is
            // still the UTC day: unchanged behaviour, different clock SOURCE.
            var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
            if (body.EffectiveFrom > today)
                return Results.UnprocessableEntity(new { error = FutureDatedProfileError });

            // Admin-strict If-Match parse — 428 if missing / malformed / If-None-Match: *
            // (per EtagHeaderHelper.TryParseIfMatch admin-strict mode).
            if (!EtagHeaderHelper.TryParseIfMatch(
                    context.Request, out var expectedVersion, out var headerError))
                return Results.Json(new { error = headerError }, statusCode: 428);

            var actorId = actor.ActorId ?? "unknown";
            var actorRole = actor.ActorRole ?? "unknown";
            var streamId = $"employee-profile-{employeeId}";

            await using var conn = connectionFactory.Create();
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                // ── S66 / TASK-6604 (ADR-032 D4) — employee-scoped advisory lock FIRST ──
                // Acquire the shared per-employee consumption lock as the FIRST statement in the
                // tx, BEFORE the predecessor SELECT ... FOR UPDATE below (the advisory lock strictly
                // precedes any row lock per ADR-032 D4) and held to commit. This serializes the
                // profile-change revaluation against a concurrent Skema-save consumption tx (which
                // takes the SAME lock first) so a save's stale-fraction Feriedage cannot be recorded
                // across this PUT's revaluation window. Taken unconditionally — the lock is cheap and
                // keeping it before the FOR UPDATE preserves the single global lock-ordering
                // (advisory → row), so a future trigger-independent change cannot reorder it.
                await StatsTid.Backend.Api.Services.EmployeeConsumptionLock.AcquireAsync(
                    conn, tx, employeeId, ct);

                // S138 / TASK-13802 — the subject row, read TERMINATED-INCLUSIVE, serves two
                // purposes and must be read BEFORE the write: (a) `employment_start_date` is the
                // caller-supplied floor the writer enforces (a correction cannot predate the hire),
                // and (b) `primary_org_id` resolves the ADR-026 audit-projection target org. The
                // pre-S138 read was `GetByIdAsync` (active-only) AFTER the write and threw on a
                // deactivated subject — which is exactly the leaver HR needs to correct.
                var auditUser = await userRepo.GetByIdIncludingTerminatedAsync(conn, tx, employeeId, ct);
                if (auditUser is null)
                {
                    await tx.RollbackAsync(ct);
                    return Results.NotFound(new { error = "Employee profile not found" });
                }

                // Step 0b Reviewer BLOCKER-3 absorption — Case A 404 pre-check, UNCHANGED in S138.
                // PUT is an admin EDIT surface; it must NOT create a net-new row, and after a
                // soft-delete it must not RESURRECT a deliberately retired profile (the router's
                // trailing-gap case T stays a repository-level case — refinement Reviewer c3 NEW-8).
                // Everything "before"-shaped now comes from the writer's COVERING pre-image, so this
                // read is reduced to the existence probe it always logically was.
                bool hasLiveRow;
                await using (var preCmd = new NpgsqlCommand(
                    """
                    SELECT 1
                    FROM employee_profiles
                    WHERE employee_id = @employeeId
                      AND effective_to IS NULL
                    """, conn, tx))
                {
                    preCmd.Parameters.AddWithValue("employeeId", employeeId);
                    hasLiveRow = await preCmd.ExecuteScalarAsync(ct) is not null;
                }

                if (!hasLiveRow)
                {
                    // PUT is edit-only; no live row → 404 BEFORE routing through
                    // SupersedeAndCreateAsync's Case A / case T branches (which would otherwise
                    // INSERT a fresh open row — but here expectedVersion is non-null from
                    // admin-strict If-Match, so the writer would actually throw
                    // OptimisticConcurrencyException with ActualVersion=null mapping to 412. We
                    // pre-empt with the cleaner 404 contract per Step 0b Reviewer BLOCKER-3).
                    await tx.RollbackAsync(ct);
                    return Results.NotFound(new { error = "Employee profile not found" });
                }

                // S138 / TASK-13802 — the generalized dated write (TASK-13801). The repository
                // locks the WHOLE timeline (open row first), validates the aggregate token against
                // the open row, routes on the locked snapshot (A / B' / C' / E / G / T) and decides
                // the same-values no-op INSIDE the lock. The fourth field (`employment_category`,
                // null = keep the covering row's) and the employment-start floor ride along.
                SaveEmployeeProfileResult result;
                try
                {
                    var supersedeRequest = new EmployeeProfileSupersedeRequest(
                        EmployeeId: employeeId,
                        PartTimeFraction: body.PartTimeFraction,
                        Position: body.Position,
                        EffectiveFrom: body.EffectiveFrom,
                        EmploymentCategory: body.EmploymentCategory,
                        EmploymentStartDate: auditUser.EmploymentStartDate);
                    result = await repository.SupersedeAndCreateAsync(
                        conn, tx, supersedeRequest, expectedVersion, ct);
                }
                catch (OptimisticConcurrencyException ex)
                {
                    await tx.RollbackAsync(ct);
                    return Results.Json(new
                    {
                        error = "Concurrency precondition failed",
                        expectedVersion = ex.ExpectedVersion,
                        actualVersion = ex.ActualVersion,
                    }, statusCode: 412);
                }
                catch (TemporalWriteRejectedException ex)
                {
                    // S138 — the writer's three refusals. Two are PURE predicates raised before any
                    // lock: FutureDated (defence-in-depth behind the validator above) and
                    // PrecedesEmploymentStart. The third, NoRecordedEmploymentCategory (Step-5a), is
                    // raised AFTER the lock because it depends on the routed case — case E lands
                    // before every recorded row, so nothing records which category held then and
                    // guessing would mislabel history. Hence the rollback here covers all three.
                    // The body is DATE-FREE by construction: the exception's own messages never
                    // carry a date, so neither the hire date nor the requested date can leak.
                    await tx.RollbackAsync(ct);
                    return Results.UnprocessableEntity(new { error = ex.Message });
                }
                catch (ConcurrentSeedConflictException)
                {
                    // S138 — an INSERT lost a race on the live / history unique index (23505).
                    // Symmetric to the AdminEndpoints users-PUT mapping: 409, refresh and retry.
                    await tx.RollbackAsync(ct);
                    return Results.Conflict(new
                    {
                        error = "The employee profile timeline changed concurrently; refresh and retry.",
                    });
                }
                catch (InvalidProfileSupersessionException ex)
                {
                    // Defense-in-depth — the pre-S138 backdate refusal. The generalized writer no
                    // longer raises it (a backdate is now a legal, routed write); kept so a future
                    // reintroduction surfaces as a 422 rather than a 500.
                    await tx.RollbackAsync(ct);
                    return Results.UnprocessableEntity(new { error = ex.Message });
                }

                // ── S138 / TASK-13802 — the same-values NO-OP (the S23 shape) ──
                // The writer decided, inside the lock and AFTER the If-Match check, that the
                // request equals the row COVERING the requested date field-for-field. Nothing was
                // written: no row, no version bump, no cache write. So: no audit row, no event, no
                // revaluation, no worklist row, the ETag UNCHANGED — and the same 200 as a real
                // edit, because from the caller's point of view the requested state now holds.
                // Note the comparison is against the COVERING row, not the live one: a BACKDATED
                // value equal to TODAY's value is a real change to history and still writes.
                if (result.IsNoOp)
                {
                    // S138 / TASK-13810 — the body is the state AS OF TODAY, the same rule as the
                    // real-write branch below (see ReadProfileAsOfTodayAsync). It used to echo the
                    // request, which is only right when the request date is today: a BACKDATED
                    // no-op matches some closed history row, whose values need not be today's.
                    // Degenerate shape (see ReadProfileAsOfTodayAsync): no row covers today, so
                    // there is no "state as of today" to report — fall back to the covering row the
                    // no-op matched, which is the only value this request can honestly name.
                    var noOpAsOf = await ReadProfileAsOfTodayAsync(conn, tx, employeeId, today, ct);
                    var (noOpFraction, noOpPosition) = noOpAsOf
                        ?? (result.Covering?.PartTimeFraction ?? body.PartTimeFraction,
                            result.Covering?.Position ?? body.Position);
                    await tx.CommitAsync(ct);
                    context.Response.Headers.ETag = $"\"{result.Version}\"";
                    return Results.Ok(new EmployeeProfileResponse(
                        employeeId,
                        noOpFraction,
                        noOpPosition,
                        noOpFraction < 1.0m,
                        result.Version));
                }

                var profileId = result.ProfileId;
                var newVersion = result.Version;

                // S138 / TASK-13802 — the COVERING row's pre-image is the single source for every
                // "before"-shaped fact below (audit `previous_data`, the mutation predicate, the
                // Superseded event's predecessor fields). It is null only for the insert-into-a-gap
                // cases (E / G), where nothing was covering the date and nothing was closed.
                var covering = result.Covering;

                // The interval the write actually produced. `null` upper bound = the row is open.
                // Everything downstream (revaluation, worklist) is confined to THIS interval, not
                // to [from, ∞) — recon discovery 1: an inserted history row must not revalue the
                // absences that belong to the row after it.
                var writtenFrom = result.NewEffectiveFrom ?? body.EffectiveFrom;
                var writtenTo = result.NewEffectiveTo;

                // The category value the writer actually stamped on the row: the request value when
                // supplied, else the covering row's, else the live users cache (the writer's own
                // COALESCE order — mirrored here only to narrate the audit row + the events).
                var writtenCategory = body.EmploymentCategory
                    ?? covering?.EmploymentCategory
                    ?? auditUser.EmploymentCategory;

                // S138 — the aggregate token transition. `TimelineVersionBefore` is the OPEN row's
                // version before the write (= the client's If-Match under admin-strict), and
                // `result.Version` is the token after it — bumped on EVERY timeline write, including
                // a history-only split, so the profile GET's ETag moves and a second backdate issued
                // against the stale token 412s.
                var versionBefore = result.TimelineVersionBefore ?? expectedVersion;

                // S138 — audit action per the ROUTED case, still inside the 4-valued CHECK
                // (CREATED / UPDATED / DELETED / SUPERSEDED — no enum widening, refinement
                // discovery 5): a split closes a row ⇒ SUPERSEDED (whether the covering row was the
                // open row or a history row); an in-place edit ⇒ UPDATED; a gap fill closes nothing
                // ⇒ CREATED.
                var auditAction = result.Kind switch
                {
                    TemporalWriteKind.Updated => "UPDATED",
                    TemporalWriteKind.Superseded or TemporalWriteKind.Inserted => "SUPERSEDED",
                    _ => "CREATED",
                };

                // Audit row.
                //
                // version_before / version_after are the per-employee TIMELINE token (the profile
                // GET's ETag), per refinement Assumption 6 — NOT the touched row's own version,
                // which for a history-only split is never issued to any client. previous_data is
                // the COVERING row's pre-image incl. its interval; new_data is the written row's
                // values incl. the interval it now occupies (`effectiveTo` null = open).
                var previousData = covering is null
                    ? null
                    : JsonSerializer.Serialize(new
                    {
                        partTimeFraction = covering.PartTimeFraction,
                        position = covering.Position,
                        employmentCategory = covering.EmploymentCategory,
                        effectiveFrom = covering.EffectiveFrom.ToString("yyyy-MM-dd"),
                        effectiveTo = covering.EffectiveTo?.ToString("yyyy-MM-dd"),
                    });
                var newData = JsonSerializer.Serialize(new
                {
                    partTimeFraction = body.PartTimeFraction,
                    position = body.Position,
                    employmentCategory = writtenCategory,
                    effectiveFrom = writtenFrom.ToString("yyyy-MM-dd"),
                    effectiveTo = writtenTo?.ToString("yyyy-MM-dd"),
                });
                await using (var auditCmd = new NpgsqlCommand(
                    """
                    INSERT INTO employee_profile_audit (
                        profile_id, employee_id, action,
                        previous_data, new_data,
                        version_before, version_after,
                        actor_id, actor_role)
                    VALUES (
                        @profileId, @employeeId, @action,
                        @previousData::jsonb, @newData::jsonb,
                        @versionBefore, @versionAfter,
                        @actorId, @actorRole)
                    """, conn, tx))
                {
                    auditCmd.Parameters.AddWithValue("profileId", profileId);
                    auditCmd.Parameters.AddWithValue("employeeId", employeeId);
                    auditCmd.Parameters.AddWithValue("action", auditAction);
                    auditCmd.Parameters.AddWithValue("previousData",
                        previousData is null ? (object)DBNull.Value : previousData);
                    auditCmd.Parameters.AddWithValue("newData", newData);
                    auditCmd.Parameters.AddWithValue("versionBefore", versionBefore);
                    auditCmd.Parameters.AddWithValue("versionAfter", newVersion);
                    auditCmd.Parameters.AddWithValue("actorId", actorId);
                    auditCmd.Parameters.AddWithValue("actorRole", actorRole);
                    await auditCmd.ExecuteNonQueryAsync(ct);
                }

                // S138 — the users cache write. `users.employment_category` means "the category as
                // of TODAY"; the writer refreshes it from the row covering today (never from the
                // request), so a purely historical correction leaves it untouched by construction.
                // When it DID move, that is a users-row write under ADR-018 D7 / ADR-019 D8: it
                // bumped `users.version`, so it owes a `users_audit` row — and a stale users ETag
                // correctly 412s afterwards. Shape mirrors AdminEndpoints' users_audit row.
                if (result.UsersCacheWritten)
                {
                    var previousUserData = JsonSerializer.Serialize(new
                    {
                        employmentCategory = result.PreviousEmploymentCategoryCache,
                    });
                    var newUserData = JsonSerializer.Serialize(new
                    {
                        employmentCategory = result.NewEmploymentCategoryCache,
                    });
                    await using var userAuditCmd = new NpgsqlCommand(
                        """
                        INSERT INTO users_audit (
                            user_id, action,
                            previous_data, new_data,
                            version_before, version_after,
                            actor_id, actor_role)
                        VALUES (
                            @userId, 'UPDATED',
                            @previousData::jsonb, @newData::jsonb,
                            @versionBefore, @versionAfter,
                            @actorId, @actorRole)
                        """, conn, tx);
                    userAuditCmd.Parameters.AddWithValue("userId", employeeId);
                    userAuditCmd.Parameters.AddWithValue("previousData", previousUserData);
                    userAuditCmd.Parameters.AddWithValue("newData", newUserData);
                    userAuditCmd.Parameters.AddWithValue("versionBefore", result.UsersVersionBefore!.Value);
                    userAuditCmd.Parameters.AddWithValue("versionAfter", result.UsersVersionAfter!.Value);
                    userAuditCmd.Parameters.AddWithValue("actorId", actorId);
                    userAuditCmd.Parameters.AddWithValue("actorRole", actorRole);
                    await userAuditCmd.ExecuteNonQueryAsync(ct);
                }

                // Atomic-outbox emission (same tx as the row write + audit per ADR-018 D3).
                // S44 TASK-4413: the ADR-026 audit projection is TENANT_TARGETED — it needs the
                // employee's primary_org_id, resolved above from the TERMINATED-INCLUSIVE read so a
                // departed employee's correction still projects into their home org.
                var auditCtx = new AuditProjectionContext(
                    ActorId: actor.ActorId,
                    ActorPrimaryOrgId: actor.OrgId,
                    CorrelationId: actor.CorrelationId,
                    OccurredAt: DateTimeOffset.UtcNow,
                    ResolvedTargetOrgId: auditUser.PrimaryOrgId);

                // S138 / TASK-13802 — emission per ROUTED KIND (the S33 Outcome switch generalized):
                //   Updated (B')                       → EmployeeProfileUpdated
                //   Superseded (C' on the OPEN row)    → EmployeeProfileSuperseded, NewEffectiveTo null
                //   Inserted   (C' on a HISTORY row)   → EmployeeProfileSuperseded, NewEffectiveTo SET
                //   InsertedBeforeFirst / InGap / Trailing / Created (nothing closed)
                //                                      → EmployeeProfileCreated
                // The last group is UNREACHABLE through this surface (the 404 pre-check + the
                // admin-strict If-Match make an empty or open-row-less timeline a 404/412), but it
                // is mapped rather than thrown so the switch is total against the router's kinds.
                //
                // S66 / TASK-6604 — the emitted event's EventId is the revaluation's
                // TriggeringProfileEventId AND the worklist trigger's causal link.
                Guid triggeringProfileEventId;
                if (result.Kind == TemporalWriteKind.Updated)
                {
                    // B' — the row STARTING on the requested date edited in place (open or history).
                    var updatedEvent = new EmployeeProfileUpdated
                    {
                        ProfileId = profileId,
                        EmployeeId = employeeId,
                        PartTimeFraction = body.PartTimeFraction,
                        Position = body.Position,
                        EmploymentCategory = writtenCategory,
                        VersionBefore = versionBefore,
                        VersionAfter = newVersion,
                        ActorId = actorId,
                        ActorRole = actorRole,
                        CorrelationId = actor.CorrelationId,
                    };
                    var outboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, streamId, updatedEvent, ct);
                    var updatedAuditCtx = auditCtx with { OccurredAt = new DateTimeOffset(updatedEvent.OccurredAt) };
                    var auditRow = updatedAuditMapper.Map(updatedEvent, updatedAuditCtx);
                    await auditRepo.InsertAsync(conn, tx, updatedEvent.EventId, outboxId, updatedEvent.EventType, auditRow, updatedAuditCtx, ct);
                    triggeringProfileEventId = updatedEvent.EventId;
                }
                else if (result.Kind is TemporalWriteKind.Superseded or TemporalWriteKind.Inserted)
                {
                    // C' — the covering row was CLOSED at the requested date and a new row inserted
                    // for the remainder of its old interval. Under end-exclusive semantics
                    // (ADR-018 D9) the predecessor's effective_to == the new row's effective_from.
                    // NewEffectiveTo distinguishes the two shapes: null = the classic cross-day
                    // supersession (the new row is open); a date = D8's insert-between (the new row
                    // ends where the covering row used to end, and the later rows are untouched).
                    var supersededEvent = new EmployeeProfileSuperseded
                    {
                        PredecessorProfileId = covering!.ProfileId,
                        NewProfileId = profileId,
                        EmployeeId = employeeId,
                        PredecessorEffectiveFrom = covering.EffectiveFrom,
                        PredecessorEffectiveTo = writtenFrom,
                        NewEffectiveFrom = writtenFrom,
                        NewEffectiveTo = writtenTo,
                        PartTimeFraction = body.PartTimeFraction,
                        Position = body.Position,
                        EmploymentCategory = writtenCategory,
                        PredecessorVersion = covering.Version,
                        NewVersion = result.ProducedRowVersion,
                        ActorId = actorId,
                        ActorRole = actorRole,
                        CorrelationId = actor.CorrelationId,
                    };
                    var outboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, streamId, supersededEvent, ct);
                    var supersededAuditCtx = auditCtx with { OccurredAt = new DateTimeOffset(supersededEvent.OccurredAt) };
                    var auditRow = supersededAuditMapper.Map(supersededEvent, supersededAuditCtx);
                    await auditRepo.InsertAsync(conn, tx, supersededEvent.EventId, outboxId, supersededEvent.EventType, auditRow, supersededAuditCtx, ct);
                    triggeringProfileEventId = supersededEvent.EventId;
                }
                else
                {
                    // E / G / T / A — a row was INSERTED and nothing was closed: CREATED.
                    var createdEvent = new EmployeeProfileCreated
                    {
                        ProfileId = profileId,
                        EmployeeId = employeeId,
                        PartTimeFraction = body.PartTimeFraction,
                        Position = body.Position,
                        EmploymentCategory = writtenCategory,
                        EffectiveFrom = writtenFrom,
                        ActorId = actorId,
                        ActorRole = actorRole,
                        CorrelationId = actor.CorrelationId,
                    };
                    var outboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, streamId, createdEvent, ct);
                    var createdAuditCtx = auditCtx with { OccurredAt = new DateTimeOffset(createdEvent.OccurredAt) };
                    var auditRow = createdAuditMapper.Map(createdEvent, createdAuditCtx);
                    await auditRepo.InsertAsync(conn, tx, createdEvent.EventId, outboxId, createdEvent.EventType, auditRow, createdAuditCtx, ct);
                    triggeringProfileEventId = createdEvent.EventId;
                }

                // S138 — the change predicates, taken against the COVERING row (recon discovery 8).
                // A backdate whose values equal TODAY's but differ from the covering row IS a change;
                // a value equal to the covering row is the real no-op and never reaches here.
                // A gap fill (covering null) is a change by construction — there was nothing there.
                var fractionChanged = covering is null || covering.PartTimeFraction != body.PartTimeFraction;
                var positionChanged = covering is null
                    || !string.Equals(covering.Position, body.Position, StringComparison.Ordinal);
                var categoryChanged = body.EmploymentCategory is not null
                    && !string.Equals(covering?.EmploymentCategory, body.EmploymentCategory, StringComparison.Ordinal);

                // ── S66 / TASK-6604 (ADR-032 D4), re-windowed by S138 / TASK-13802 — revaluation ──
                // Trigger: ANY fullDayHours-affecting field changed — part_time_fraction OR position
                // (position drives the ADR-017 D3 override chain → WeeklyNormHours; a fraction-only
                // trigger would silently skip position-driven revaluations). Same tx as the profile
                // mutation + event + audit (ADR-018 D3): any failure here — including
                // ApplyRevaluationAsync's all-or-nothing row-count throw — rolls EVERYTHING back and
                // surfaces 500 via the outer catch. The advisory lock taken at tx-open serializes us
                // against a racing Skema-save consumption tx (ADR-032 D4). S138 changes the WINDOW
                // from [effectiveFrom, ∞) to exactly the written row's interval, and skips settled
                // holiday years (see the helper's doc).
                // S138 / TASK-13810 — the (type, year) groups the revaluation DECLINED to re-record
                // because the holiday year is settled. Collected here so the worklist block below
                // can flag each one; empty when no revaluation ran or nothing was skipped.
                IReadOnlyList<(string EntitlementType, int EntitlementYear)> skippedSettledGroups =
                    Array.Empty<(string, int)>();

                if (fractionChanged || positionChanged)
                {
                    // ADR-032 D4: the revaluation event rides the CONSOLIDATED employee stream
                    // (balance-event lineage, ADR-018 D6) — NOT this PUT's employee-profile-{id}
                    // stream. Caught by the Adr032RevaluationTests stream pin (TASK-6607).
                    skippedSettledGroups = await RevalueAbsencesInIntervalAsync(
                        conn, tx, employeeId, body, writtenFrom, writtenTo, auditUser.PrimaryOrgId,
                        triggeringProfileEventId, actor, auditCtx,
                        consumptionCalculator, profileResolver, absenceProjectionRepo,
                        entitlementBalanceRepo, entitlementConfigRepo, settlementRepo,
                        outbox, revaluedAuditMapper,
                        auditRepo, $"employee-{employeeId}", ct);
                }

                // ── S138 / TASK-13803 + TASK-13802 — the HR diagnostic worklist ──
                // Nothing recalculates automatically (ADR-013). What the correction owes payroll and
                // HR is VISIBILITY: every already-EXPORTED month and every ACTIVE-settlement holiday
                // year the written interval touches gets a worklist row (or a trigger appended to an
                // open one), written in THIS transaction alongside its outbox event. One trigger per
                // dimension that actually changed — a profile-field correction and a category
                // correction in the same request raise TWO triggers, because they point at different
                // downstream limitations (QUAL-149 vs the category-driven reads).
                //
                // S138 / TASK-13810 (owner ruling 2026-09-03) — SETTLED_YEAR rows now come from TWO
                // places, and both are needed:
                //   • the DATE path (WriteForSettledYearsAsync) PREDICTS which frozen years this
                //     correction could have disturbed, from two conjoined tests: does it reach back
                //     past the moment the settlement was frozen, AND does it overlap that year's
                //     entitlement window? Either test alone over-flags — window-only fires on an
                //     ordinary today-dated edit (taking windows commonly run past today),
                //     freeze-only fires for every settlement frozen after an old correction.
                //   • the SKIP path (WriteForSkippedSettledYearsAsync) REPORTS what actually
                //     happened: the revaluation refused to re-record these groups, so HR must be
                //     told a correction was withheld — a today-forward change CAN hit a settled year
                //     through absences already booked into a still-open taking window. It takes no
                //     date test at all; second-guessing an action that definitely happened could
                //     only withhold the notice as well as the correction.
                // They share the row kind and the partial UNIQUE, so a year hit by both yields ONE
                // row carrying two triggers, not two rows.
                if (fractionChanged || positionChanged)
                {
                    var profileTrigger = new WorklistTrigger(
                        WorklistTriggerKinds.ProfileChange, triggeringProfileEventId, writtenFrom, actorId);
                    await worklistRepo.WriteForExportedMonthsAsync(
                        conn, tx, employeeId, profileTrigger, writtenFrom, writtenTo, ct);
                    await worklistRepo.WriteForSettledYearsAsync(
                        conn, tx, employeeId, profileTrigger, writtenFrom, writtenTo, ct);
                    await worklistRepo.WriteForSkippedSettledYearsAsync(
                        conn, tx, employeeId, profileTrigger, skippedSettledGroups, ct);
                }
                if (categoryChanged)
                {
                    var categoryTrigger = new WorklistTrigger(
                        WorklistTriggerKinds.EmploymentCategoryChange, triggeringProfileEventId, writtenFrom, actorId);
                    await worklistRepo.WriteForExportedMonthsAsync(
                        conn, tx, employeeId, categoryTrigger, writtenFrom, writtenTo, ct);
                    await worklistRepo.WriteForSettledYearsAsync(
                        conn, tx, employeeId, categoryTrigger, writtenFrom, writtenTo, ct);
                }

                // S138 / TASK-13810 (owner ruling 2026-09-03) — the body is the state AS OF TODAY,
                // re-read here INSIDE the tx so it includes this write. See
                // ReadProfileAsOfTodayAsync for the reasoning; the short version is that this
                // resource means "the profile as it stands now" everywhere else, and one of the two
                // correction endpoints answering with backdated values while the other answered
                // with today's was a contradiction waiting for Increment 4's date picker to expose.
                // Degenerate shape (see ReadProfileAsOfTodayAsync): when no row covers today there
                // is no "state as of today" to report, so answer with what this request wrote. The
                // correction is kept either way — the response is a view of it, never its gate.
                var todayAsOf = await ReadProfileAsOfTodayAsync(conn, tx, employeeId, today, ct);
                var (todayFraction, todayPosition) =
                    todayAsOf ?? (body.PartTimeFraction, body.Position);

                await tx.CommitAsync(ct);

                context.Response.Headers.ETag = $"\"{newVersion}\"";
                // S112 / TASK-11201 — named record (EmployeeProfileResponse) replaces the anonymous
                // shape; BYTE-IDENTICAL wire JSON (same member names/order/nullability, camelCase
                // Web default; the SAME record as the GET — both handlers emitted the same 5 fields).
                // The SHAPE is unchanged by TASK-13810; only the VALUES are now sourced from the row
                // covering today rather than from the request. For a today-dated edit — the only
                // shape the frontend sends before Increment 4 — the two are identical, so nothing
                // observable changes for existing clients. The ETag stays the timeline token, so it
                // moves even after a history-only correction that leaves this body untouched.
                return Results.Ok(new EmployeeProfileResponse(
                    employeeId,
                    todayFraction,
                    todayPosition,
                    todayFraction < 1.0m,
                    newVersion));
            }
            catch
            {
                if (tx.Connection is not null)
                    await tx.RollbackAsync(ct);
                throw;
            }
        }).RequireAuthorization("HROrAbove")
        .Produces<EmployeeProfileResponse>(StatusCodes.Status200OK); // S112 / TASK-11201

        // ═══════════════════════════════════════════
        // 3. DELETE /api/admin/employee-profiles/{employeeId}
        //
        // S33 / TASK-3308 — NEW endpoint.
        //
        // RBAC: HROrAbove + OrgScopeValidator (mirrors GET/PUT — Step 0b BLOCKER fix).
        // Admin-strict If-Match required (ADR-019 D2). Atomic-outbox semantic: soft-
        // delete UPDATE + INSERT audit row (action='DELETED') + outbox enqueue all
        // happen in one tx (ADR-018 D3). Returns 204 No Content on success.
        //
        // ADR-023 D8 soft-delete divergence: the predecessor's `version` column is
        // UNCHANGED — soft-delete is row-state-change, not field-mutation; the
        // partial-unique-index `idx_employee_profiles_live` makes the row "disappear"
        // from live reads, so bumping version is redundant. Audit row carries
        // version_before = version_after = predecessor.version per ADR-019 D8 for
        // DELETE actions.
        //
        // Error mapping:
        //   • 428 — missing / malformed If-Match (EtagHeaderHelper)
        //   • 412 — OptimisticConcurrencyException (live row exists, version differs)
        //   • 404 — KeyNotFoundException (no live row — also the retry-after-delete
        //           case per ADR-023 D8 row-disappearance idempotency)
        //   • 403 — OrgScopeValidator denial (cross-org guard)
        // ═══════════════════════════════════════════
        app.MapDelete("/api/admin/employee-profiles/{employeeId}", async (
            string employeeId,
            EmployeeProfileRepository repository,
            DbConnectionFactory connectionFactory,
            IOutboxEnqueue outbox,
            IAuditProjectionMapper<EmployeeProfileSoftDeleted> softDeletedAuditMapper,
            AuditProjectionRepository auditRepo,
            UserRepository userRepo,
            OrgScopeValidator scopeValidator,
            // S139 / TASK-13907 — the server-"today" seam (TimeProvider.System in production).
            // Read ONCE below into `today` and used for BOTH dated outputs of this DELETE: the
            // row's `effective_to` stamp (passed into SoftDeleteAsync as `closeDate`, where the
            // database's NOW()::date used to decide it) and the emitted event's `EffectiveTo`.
            TimeProvider timeProvider,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // Step 0b BLOCKER fix — cross-org binding (mirrors GET / PUT). S76 B1: LocalHR floor.
            var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessAsync(
                actor, employeeId, StatsTidRoles.LocalHR, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            // Admin-strict If-Match parse — 428 if missing / malformed / If-None-Match: *
            // (per EtagHeaderHelper.TryParseIfMatch admin-strict mode).
            if (!EtagHeaderHelper.TryParseIfMatch(
                    context.Request, out var expectedVersion, out var headerError))
                return Results.Json(new { error = headerError }, statusCode: 428);

            var actorId = actor.ActorId ?? "unknown";
            var actorRole = actor.ActorRole ?? "unknown";
            var streamId = $"employee-profile-{employeeId}";

            // S139 / TASK-13907 (Step-5a W1) — ONE date for the whole DELETE, computed here and
            // used twice: as the row's close-stamp (passed to SoftDeleteAsync as `closeDate`) and
            // as the emitted event's `EffectiveTo`. Reading the provider twice would not be the
            // same instant — a request crossing 23:59:59.9 UTC could stamp the row the 8th and
            // announce the 7th in the event that describes it, which is an audit-trail
            // contradiction, not a rounding detail. Same "compute ONCE so they can never disagree"
            // rule S137 applied to the create POST (AdminEndpoints `effectiveFrom`). UTC day, per
            // the endpoint convention documented on this class.
            var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

            await using var conn = connectionFactory.Create();
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                // Snapshot pre-delete payload state for the audit row's `previous_data`
                // JSONB. In-tx read so the audit reflects the same row the UPDATE
                // will close. The repository's SoftDeleteAsync independently enforces
                // optimistic concurrency via `AND version = @expectedVersion` in the
                // UPDATE — we don't need to pre-validate, just snapshot for audit.
                var preDelete = await repository.GetByEmployeeIdAsync(conn, tx, employeeId, ct);
                if (preDelete is null)
                {
                    // Defense-in-depth — SoftDeleteAsync will also raise
                    // KeyNotFoundException for this branch, but pre-empting avoids
                    // doing a no-op UPDATE first. Matches PUT pre-check semantic.
                    await tx.RollbackAsync(ct);
                    return Results.NotFound(new { error = "Employee profile not found" });
                }

                Guid profileId;
                long predecessorVersion;
                try
                {
                    // SoftDeleteAsync returns (profile_id, version) where version is
                    // UNCHANGED from the predecessor's value per ADR-023 D8.
                    var (returnedProfileId, returnedVersion) = await repository.SoftDeleteAsync(
                        conn, tx, employeeId, expectedVersion, today, ct);
                    profileId = returnedProfileId;
                    predecessorVersion = returnedVersion;
                }
                catch (OptimisticConcurrencyException ex)
                {
                    await tx.RollbackAsync(ct);
                    return Results.Json(new
                    {
                        error = "Concurrency precondition failed",
                        expectedVersion = ex.ExpectedVersion,
                        actualVersion = ex.ActualVersion,
                    }, statusCode: 412);
                }
                catch (KeyNotFoundException)
                {
                    // Stale If-Match retry after a successful soft-delete also lands
                    // here per ADR-023 D8 row-disappearance idempotency (locked by
                    // TASK-3312 D-test).
                    await tx.RollbackAsync(ct);
                    return Results.NotFound(new { error = "Employee profile not found" });
                }

                // Audit row — action='DELETED' per init.sql:514 CHECK constraint;
                // version_before = version_after = predecessor.version per ADR-023 D8
                // (no version bump on soft-delete — deliberate divergence from sibling
                // ADR-019 D8 endpoints). new_data = NULL (the row no longer carries a
                // logically "current" payload after the close).
                var previousData = JsonSerializer.Serialize(new
                {
                    partTimeFraction = preDelete.PartTimeFraction,
                    position = preDelete.Position,
                });
                await using (var auditCmd = new NpgsqlCommand(
                    """
                    INSERT INTO employee_profile_audit (
                        profile_id, employee_id, action,
                        previous_data, new_data,
                        version_before, version_after,
                        actor_id, actor_role)
                    VALUES (
                        @profileId, @employeeId, 'DELETED',
                        @previousData::jsonb, NULL,
                        @versionBefore, @versionAfter,
                        @actorId, @actorRole)
                    """, conn, tx))
                {
                    auditCmd.Parameters.AddWithValue("profileId", profileId);
                    auditCmd.Parameters.AddWithValue("employeeId", employeeId);
                    auditCmd.Parameters.AddWithValue("previousData", previousData);
                    auditCmd.Parameters.AddWithValue("versionBefore", predecessorVersion);
                    auditCmd.Parameters.AddWithValue("versionAfter", predecessorVersion);
                    auditCmd.Parameters.AddWithValue("actorId", actorId);
                    auditCmd.Parameters.AddWithValue("actorRole", actorRole);
                    await auditCmd.ExecuteNonQueryAsync(ct);
                }

                // Atomic-outbox emission (same tx as UPDATE + audit per ADR-018 D3).
                // EmployeeProfileSoftDeleted carries the predecessor's profile_id,
                // the close-date (effective_to = today), and the row-version (NAMED
                // `RowVersion` — NOT `Version` — to avoid shadowing DomainEventBase's
                // event-schema-version field, same disambiguation as S30
                // EntitlementConfigSoftDeleted). Actor / correlation come from
                // DomainEventBase init.
                var softDeletedEvent = new EmployeeProfileSoftDeleted
                {
                    ProfileId = profileId,
                    EmployeeId = employeeId,
                    // The SAME `today` the row was stamped with above — one read, two uses, so
                    // the event can never describe a different day than the row it announces.
                    EffectiveTo = today,
                    RowVersion = predecessorVersion,
                    ActorId = actorId,
                    ActorRole = actorRole,
                    CorrelationId = actor.CorrelationId,
                };
                // S44 TASK-4413: capture outbox_id for audit_projection insert
                // (ADR-026 D2 sync-in-tx projection write — atomic with the
                // employee_profiles row + outbox row per ADR-018 D3/D13).
                var outboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, streamId, softDeletedEvent, ct);

                // S44 TASK-4413: resolve employee org for audit projection
                // (TENANT_TARGETED — need employee's primary_org_id).
                var auditUser = await userRepo.GetByIdAsync(conn, tx, employeeId, ct);
                var auditCtx = new AuditProjectionContext(
                    ActorId: actor.ActorId,
                    ActorPrimaryOrgId: actor.OrgId,
                    CorrelationId: actor.CorrelationId,
                    OccurredAt: new DateTimeOffset(softDeletedEvent.OccurredAt),
                    ResolvedTargetOrgId: auditUser?.PrimaryOrgId
                        ?? throw new InvalidOperationException(
                            $"Audit projection: employee {employeeId} not found or inactive."));
                var auditRow = softDeletedAuditMapper.Map(softDeletedEvent, auditCtx);
                await auditRepo.InsertAsync(conn, tx, softDeletedEvent.EventId, outboxId, softDeletedEvent.EventType, auditRow, auditCtx, ct);

                await tx.CommitAsync(ct);

                return Results.NoContent();
            }
            catch
            {
                if (tx.Connection is not null)
                    await tx.RollbackAsync(ct);
                throw;
            }
        }).RequireAuthorization("HROrAbove")
        .Produces(StatusCodes.Status204NoContent); // S112 / TASK-11201 — declared-204 (no body, intentionally)

        return app;
    }

    // ── S138 / TASK-13810 (owner ruling 2026-09-03) — the response is the state AS OF TODAY ──

    /// <summary>
    /// The profile values covering TODAY, read inside the PUT's own transaction so it sees the row
    /// this request just wrote.
    ///
    /// <para>
    /// <b>Why the response is today's state and not the values just written.</b> The resource
    /// <c>/api/admin/employee-profiles/{id}</c> means "this employee's profile as it stands now"
    /// everywhere else — that is what the GET returns, what the <c>users</c> caches hold, and what
    /// the ETag on this aggregate tracks. If the PUT answered a BACKDATED correction with the
    /// backdated values, a client that treats the response as the new current state would display a
    /// March fraction as though it were today's. Today that is invisible only because the frontend
    /// always sends today's date; Increment 4's date picker removes that shield. So both correction
    /// surfaces answer the same question — "what is true now?" — and the dedicated agreement-code
    /// endpoint has always answered it this way (it echoes the live <c>users.agreement_code</c>
    /// cache). Consequence, intended: after a purely historical correction the response does NOT
    /// reflect what the caller just sent. The written interval is visible on the timeline and in the
    /// emitted events; the response body is about today.
    /// </para>
    ///
    /// <para>
    /// Deliberately an AS-OF read (<c>effective_from &lt;= today &lt; effective_to</c>), not a read
    /// of the OPEN row. They coincide today only because future-dating is refused; Increment 4 makes
    /// them diverge, and this read stays correct when it does.
    /// </para>
    ///
    /// <para>
    /// Returns <c>null</c> when NO row covers today, and the callers then answer with the values
    /// they just wrote. S138 Step-7a (Codex WARNING, absorbed): an earlier version threw here on the
    /// stated ground that "the 404 pre-check guarantees a row covers today" — but that pre-check
    /// guarantees an OPEN row, which is not the same thing. An open row whose <c>effective_from</c>
    /// lies in the FUTURE covers no day today; these endpoints refuse future-dating, so they cannot
    /// create that shape, but seeded, imported or legacy data can, and nothing in the schema forbids
    /// it. Throwing was the worse failure by far: this read runs INSIDE the transaction, so the
    /// exception rolled back an otherwise VALID correction and returned 500 — losing the write to
    /// protect a courtesy view of it. When there is genuinely no state as of today, echoing what was
    /// just written is the only honest answer available, and the correction survives.
    /// </para>
    /// </summary>
    private static async Task<(decimal PartTimeFraction, string? Position)?> ReadProfileAsOfTodayAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string employeeId, DateOnly today, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            """
            SELECT part_time_fraction, position
            FROM employee_profiles
            WHERE employee_id = @employeeId
              AND effective_from <= @today
              AND (effective_to IS NULL OR effective_to > @today)
            """, conn, tx);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("today", today);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;
        return (reader.GetDecimal(0), reader.IsDBNull(1) ? null : reader.GetString(1));
    }

    // ── S66 / TASK-6604 (ADR-032 D4), re-windowed by S138 / TASK-13802 — the revaluation ──

    /// <summary>
    /// Recompute and re-record the feriedage of this employee's entitlement-consuming absences
    /// dated inside <c>[from, toExclusive)</c> under the NEW profile values, all inside the
    /// profile-PUT transaction. Called only when a fullDayHours-affecting field changed
    /// (part_time_fraction OR position — ADR-032 D4).
    ///
    /// <para>
    /// <b>S138 / TASK-13802 — the window is the WRITTEN ROW's interval, not <c>[from, ∞)</c>.</b>
    /// The pre-S138 helper enumerated everything from the effective date to
    /// <c>DateOnly.MaxValue</c>, which was correct only because the new row was always the OPEN
    /// one. For a BACKDATED correction the new row ends where the row it split used to end, and
    /// the absences after that belong to the SUCCESSOR row's (unchanged) values — revaluing them
    /// would rewrite consumption under a profile that never applied (recon discovery 1).
    /// </para>
    ///
    /// <para>
    /// <b>S138 / TASK-13802 — SETTLED (type, year) groups are SKIPPED</b> (owner ruling OQ-2 (i)).
    /// A holiday year with an ACTIVE <c>vacation_settlements</c> row is a frozen ADR-033
    /// disposition; the correction is still RECORDED in dated history, but the year's
    /// <c>used</c> / feriedage are not rewritten here. TASK-13803's
    /// <c>WriteForSettledYearsAsync</c> raises a SETTLED_YEAR worklist row for it instead,
    /// pointing HR at the reverse-then-re-settle path. See the skip site below.
    /// </para>
    ///
    /// <para>
    /// <b>In-hand norm (ADR-032 D4 — the resolver cannot see the uncommitted row).</b> For each
    /// affected absence date we resolve the CURRENTLY-committed dated profile (for the UNCHANGED
    /// agreement_code / org / ok_version — none of which this PUT touches), substitute the NEW
    /// part-time-fraction / position from <paramref name="body"/>, and compute <c>fullDayHours</c>
    /// via <see cref="StatsTid.Backend.Api.Services.ConsumptionCalculator.FullDayHoursForProfileAsync"/>
    /// (the in-hand sibling of the resolver-driven path — it delegates the
    /// <c>WeeklyNorm × fraction / 5</c> + ADR-032 D3 semantics to the SHARED
    /// <c>DailyNormCalculator</c>, so there is NO second copy of the norm formula). The new per-row
    /// feriedage is <see cref="StatsTid.Backend.Api.Services.ConsumptionCalculator.ToFeriedage"/>
    /// (the exposed 4dp primitive). The OLD per-row value is the recorded
    /// <c>absences_projection.feriedage</c> (null pre-S66 rows fall back to the
    /// <c>hours/7.4</c> backfill convention — <c>EntitlementMapping.StandardDayHours</c>).
    /// </para>
    ///
    /// <para>
    /// <b>Grouping + write (ADR-032 D4).</b> Affected rows are grouped by (entitlementType,
    /// entitlementYear) — the entitlement year derived the SAME way the Skema / Balance paths use,
    /// via the shared <see cref="EntitlementPeriodResolver"/> (S80/8001: SPECIAL_HOLIDAY keys to the
    /// taking-window accrual year, NOT the raw reset_month calendar year). For each group where any
    /// per-row value changed: <c>usedDelta = Σ(new − old)</c>
    /// is applied — together with the per-absence replacement set — via the UNGATED
    /// <see cref="EntitlementBalanceRepository.ApplyRevaluationAsync"/> (revaluation may push
    /// <c>used</c> past the cap — this is NOT the booking path; all-or-nothing on the projection
    /// row-count). One <see cref="EntitlementBalanceRevalued"/> event per group is emitted on the
    /// consolidated <c>employee-{id}</c> stream (ADR-018 D6) with an ADR-026 audit row, all in the
    /// caller's tx (ADR-018 D3). Negative remaining is NOT clamped/warned/500'd here — that is a
    /// read-side concern (ADR-032 D4).
    /// </para>
    ///
    /// <para>
    /// <b>S138 / TASK-13810 — the return value: the groups this call actually SKIPPED.</b> Owner
    /// ruling 2026-09-03. Plain language: when the revaluation declines to re-record a settled
    /// holiday year, the system is withholding a correction HR believes it made — so somebody has
    /// to be told. The caller hands the returned (type, year) list to
    /// <see cref="HrBackdateWorklistRepository.WriteForSkippedSettledYearsAsync"/>, which raises a
    /// SETTLED_YEAR worklist row for each. This is the half of the ruling that dates cannot
    /// express: a TODAY-forward fraction change can still hit a settled year through absences
    /// already booked into a taking window that is still open.
    /// </para>
    /// </summary>
    /// <returns>The (entitlement type, entitlement year) groups whose revaluation was skipped
    /// because an ACTIVE settlement exists — empty when nothing was skipped.</returns>
    private static async Task<IReadOnlyList<(string EntitlementType, int EntitlementYear)>> RevalueAbsencesInIntervalAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        string employeeId,
        UpdateEmployeeProfileRequest body,
        DateOnly from,
        DateOnly? toExclusive,
        string fallbackOrgId,
        Guid triggeringProfileEventId,
        ActorContext actor,
        AuditProjectionContext auditCtx,
        StatsTid.Backend.Api.Services.ConsumptionCalculator consumptionCalculator,
        IEmploymentProfileResolver profileResolver,
        AbsenceProjectionRepository absenceProjectionRepo,
        EntitlementBalanceRepository entitlementBalanceRepo,
        EntitlementConfigRepository entitlementConfigRepo,
        VacationSettlementRepository settlementRepo,
        IOutboxEnqueue outbox,
        IAuditProjectionMapper<EntitlementBalanceRevalued> revaluedAuditMapper,
        AuditProjectionRepository auditRepo,
        string streamId,
        CancellationToken ct)
    {
        // S138 / TASK-13802 — the window is the WRITTEN ROW's interval, end-EXCLUSIVE
        // [from, toExclusive) (null = open). The absence read is inclusive on both ends, so the
        // exclusive upper bound becomes `toExclusive - 1 day`; an open row keeps DateOnly.MaxValue.
        var lastDay = toExclusive is { } upper ? upper.AddDays(-1) : DateOnly.MaxValue;
        if (lastDay < from)
            return Array.Empty<(string, int)>(); // a zero-width interval covers no date; nothing to revalue.

        // Enumerate the employee's absences dated inside the corrected interval. These are
        // already-committed bookings (NOT in this tx), so the repo's own-connection read is correct.
        var rows = await absenceProjectionRepo.GetByEmployeeAndDateRangeAsync(
            employeeId, from, lastDay, ct);

        // Accumulator per (entitlementType, entitlementYear): the replacement set + Σ(new − old).
        var groups = new Dictionary<(string Type, int Year), (List<AbsenceFeriedageReplacement> Repl, decimal UsedDelta, bool AnyChanged)>();

        // Cache the live reset_month per (entitlementType, agreementCode, okVersion) so the
        // entitlement-year derivation doesn't re-read the config for every absence row.
        var resetMonthCache = new Dictionary<string, int?>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            var entitlementType = Services.EntitlementMapping.GetEntitlementType(row.AbsenceType);
            if (entitlementType is null)
                continue; // non-entitlement absence — consumes nothing; never revalued.

            // The dated profile committed for THIS absence date gives the UNCHANGED agreement_code /
            // org / ok_version (this PUT touches only fraction + position). Build the in-hand NEW
            // profile by substituting the new fraction/position. Fail-loud propagates (rolls back)
            // if the resolver can't cover a date that carries a consuming booking — an integrity
            // violation that should never occur post-backfill (resolver's own contract).
            var datedProfile = await profileResolver.GetByEmployeeIdAtAsync(employeeId, row.Date, ct);
            if (datedProfile is null)
                // No covering profile (e.g. the row predates employment) — leave as recorded.
                //
                // S138 Step-5a (Reviewer WARNING) — RULED DEFERRAL, stated so it is a decision and
                // not a silent hole. This resolver call reads COMMITTED history on its own
                // connection (ADR-032 D4: it supplies the dimensions this PUT does not touch, while
                // the new fraction/position are substituted in hand). For a GAP-FILL (router cases
                // E and G) there is no pre-write row covering these dates, so every absence inside
                // the newly-filled stretch keeps its recorded feriedage instead of being re-recorded
                // under the corrected values — narrower than the sprint goal's "consumption in
                // exactly that interval is re-recorded". Exposure is small (a gap exists only after
                // a delete-then-recreate) and the recorded values are not WRONG, merely stale. The
                // fix is to build the in-hand profile from the PRECEDING row's unchanged dimensions,
                // the same source the category fallback now uses in the writer; registered as owed
                // work rather than taken here, because it changes what a correction WRITES and so
                // wants its own pins. Nothing masks it, and the distinction matters now that a
                // skip-reporting path exists: `WriteForSkippedSettledYearsAsync` reports groups
                // withheld because the year is SETTLED. THIS branch skips dates for want of a
                // covering profile row, which is a different thing and raises no row at all — so
                // the worklist must not be read as covering it (S138 Step-7a, Reviewer NOTE).
                continue;

            var newProfile = datedProfile with
            {
                PartTimeFraction = body.PartTimeFraction,
                Position = body.Position,
                IsPartTime = body.PartTimeFraction < 1.0m,
            };
            var orgId = datedProfile.OrgId ?? fallbackOrgId;

            var newFullDayHours = await consumptionCalculator.FullDayHoursForProfileAsync(
                newProfile, row.Date, orgId, ct);
            var newFeriedage = Services.ConsumptionCalculator.ToFeriedage(row.Hours, newFullDayHours);
            if (newFeriedage is not { } newVal)
                continue; // no meaningful divisor (zero-norm/no-profile) — recorded value untouched.

            // OLD recorded per-row feriedage (null pre-S66 rows → hours/7.4 backfill convention).
            var oldVal = row.Feriedage
                ?? Math.Round(row.Hours / Services.EntitlementMapping.StandardDayHours, 4, MidpointRounding.AwayFromZero);

            // Entitlement year via ferieår anchoring on the live config's reset_month (same
            // derivation as the Skema/Balance paths). reset_month is frozen per natural key
            // (ADR-021 Q1), so the live read is safe for any historical date of this key.
            var resetKey = $"{entitlementType}|{datedProfile.AgreementCode}|{datedProfile.OkVersion}";
            if (!resetMonthCache.TryGetValue(resetKey, out var resetMonth))
            {
                var liveConfig = await entitlementConfigRepo.GetCurrentOpenAsync(
                    entitlementType, datedProfile.AgreementCode, datedProfile.OkVersion, ct);
                resetMonth = liveConfig?.ResetMonth;
                resetMonthCache[resetKey] = resetMonth;
            }
            if (resetMonth is null)
                continue; // no config for this type/agreement/ok — cannot anchor the year; skip.

            // S80 / TASK-8001 (BLOCKER 3 fix) — route the per-row entitlement (accrual) year through
            // the SHARED EntitlementPeriodResolver, NOT the old raw "Month >= resetMonth" helper. The
            // booking/balance paths (SkemaEndpoints / BalanceEndpoints) key SPECIAL_HOLIDAY via the
            // two-calendar-year taking-window mapping (May–Dec T → accrual T−1; Jan–Apr T → accrual
            // T−2), which a raw reset_month=1 helper CANNOT express (it would key every month to its
            // own calendar year) — so the revaluation would hit the WRONG balance row (a split-brain
            // vs booking). The resolver reproduces the pre-S80 keying EXACTLY for VACATION + every
            // other type, so those revaluations are byte-identical.
            var entitlementYear = EntitlementPeriodResolver
                .Resolve(entitlementType, resetMonth.Value, row.Date).EntitlementYear;

            var key = (entitlementType, entitlementYear);
            if (!groups.TryGetValue(key, out var acc))
                acc = (new List<AbsenceFeriedageReplacement>(), 0m, false);
            acc.Repl.Add(new AbsenceFeriedageReplacement(row.EventId, newVal));
            acc.UsedDelta += newVal - oldVal;
            acc.AnyChanged = acc.AnyChanged || newVal != oldVal;
            groups[key] = acc;
        }

        // S138 / TASK-13810 — the groups this call declines to re-record because the holiday year
        // is SETTLED. Returned to the caller so the withheld correction lands on the HR worklist
        // instead of vanishing (owner ruling 2026-09-03; see the method doc).
        var skippedSettledGroups = new List<(string EntitlementType, int EntitlementYear)>();

        // Apply each group that actually changed (per-row replacement + ungated used delta), emit
        // EntitlementBalanceRevalued on the employee-{id} stream + the ADR-026 audit row, all in tx.
        foreach (var ((entitlementType, entitlementYear), acc) in groups)
        {
            if (!acc.AnyChanged)
                continue; // every per-row value identical (e.g. full-time→full-time) — no-op.

            // ── S138 / TASK-13802 (owner ruling OQ-2 (i)) — the SETTLED-YEAR SKIP ──
            // Plain language: once a holiday year has been SETTLED (ADR-033 — the year is closed
            // out and its transfer/payout/forfeit split is a frozen, audited disposition), a
            // backdated fraction change must NOT silently rewrite that year's consumption. Note
            // consumption IS fraction-dependent even though the day-count quota is fraction-flat
            // (ADR-032 D3/D4 vs ADR-031), so this is a real risk, and "settled" does NOT imply
            // "exported" — the exported-month check would not have caught it.
            // What we do instead: record the corrected history (the profile row IS backdated),
            // SKIP the revaluation for this (type, year), and let TASK-13803's
            // WriteForSettledYearsAsync raise a SETTLED_YEAR worklist row pointing HR at ADR-033's
            // reverse-then-re-settle path. The truth is recorded, the frozen settlement is not
            // rewritten behind anyone's back, and the consequence is VISIBLE.
            // PENDING_REVIEW counts as active; REVERSED does not (the repository's own predicate).
            //
            // S138 / TASK-13810 (owner ruling 2026-09-03) — the skip is now REPORTED, not just
            // taken. The group is recorded and handed back to the caller, which raises a
            // SETTLED_YEAR worklist row for it. Note this happens INSIDE the `AnyChanged` guard
            // above: a group whose per-row values did not move was not withheld from anyone (there
            // was nothing to withhold), so it is not a skip worth telling HR about.
            var activeSettlement = await settlementRepo.GetActiveAsync(
                conn, tx, employeeId, entitlementType, entitlementYear, ct);
            if (activeSettlement is not null)
            {
                skippedSettledGroups.Add((entitlementType, entitlementYear));
                continue;
            }

            await entitlementBalanceRepo.ApplyRevaluationAsync(
                conn, tx, employeeId, entitlementType, entitlementYear, acc.UsedDelta, acc.Repl, ct);

            var revaluedEvent = new EntitlementBalanceRevalued
            {
                EmployeeId = employeeId,
                EntitlementType = entitlementType,
                EntitlementYear = entitlementYear,
                Replacements = acc.Repl,
                UsedDelta = acc.UsedDelta,
                TriggeringProfileEventId = triggeringProfileEventId,
                ActorId = actor.ActorId,
                ActorRole = actor.ActorRole,
                CorrelationId = actor.CorrelationId,
            };
            var outboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, streamId, revaluedEvent, ct);
            var revaluedAuditCtx = auditCtx with { OccurredAt = new DateTimeOffset(revaluedEvent.OccurredAt) };
            var auditRow = revaluedAuditMapper.Map(revaluedEvent, revaluedAuditCtx);
            await auditRepo.InsertAsync(
                conn, tx, revaluedEvent.EventId, outboxId, revaluedEvent.EventType, auditRow, revaluedAuditCtx, ct);
        }

        return skippedSettledGroups;
    }

    // ── Request DTO ──

    /// <summary>
    /// PUT request body.
    ///
    /// <para>
    /// <b>S33 / TASK-3308 — named-record syntax + required <c>EffectiveFrom</c>.</b>
    /// Refinement cycle 1 dual-lens BLOCKER + Step 0b Reviewer W absorption: the S31
    /// DTO was a positional record (<c>WeeklyNormHours, PartTimeFraction, Position</c>);
    /// converting to named-record syntax avoids breaking any positional-pattern matches
    /// that might exist downstream, and the new <c>EffectiveFrom</c> field is required
    /// — admins must explicitly state the effective date for the temporal routing.
    /// </para>
    ///
    /// <para>
    /// <b>S138 / TASK-13802 — the date widens and a fourth, OPTIONAL field arrives.</b>
    /// <see cref="EffectiveFrom"/> may now be ANY past-or-today date (the validator refuses
    /// only the future, date-free); the writer routes it against the row covering that date.
    /// <see cref="EmploymentCategory"/> is the fourth editable profile field (ADR-040 D4) and is
    /// the ONE wire addition of this task: OPTIONAL and additive, so every existing client
    /// (the frontend sends the three original fields plus today's date) is unaffected —
    /// <c>null</c> means "keep whatever the covering row already says", exactly the pre-S138
    /// behaviour. When it DOES change, the correction additionally raises an
    /// <c>EMPLOYMENT_CATEGORY_CHANGE</c> worklist trigger.
    /// </para>
    ///
    /// <para>
    /// The three original fields stay required — admins re-state the full edit shape rather
    /// than patching individual fields (mirrors the S29 WTM / S30 EntitlementConfig
    /// admin PUT precedent).
    /// </para>
    /// </summary>
    private sealed record UpdateEmployeeProfileRequest
    {
        /// <summary>S33 — required. S138: any date up to and including today (UTC); a future
        /// date is a date-free 422 (ADR-040 D8 as amended — future-dating is Increment 4).</summary>
        public DateOnly EffectiveFrom { get; init; }
        public decimal PartTimeFraction { get; init; }
        public string? Position { get; init; }

        /// <summary>
        /// S138 / TASK-13802 (ADR-040 D4) — OPTIONAL fourth field. <c>null</c> = keep the covering
        /// row's category (the pre-S138 wire contract, unchanged for every existing caller).
        /// Deliberately NOT declared with <c>[AllowedValues]</c>: the category set is a
        /// config-keyed OPEN set (role-config overrides key new categories by data, not schema) —
        /// the same reasoning recorded on the admin user contracts.
        /// </summary>
        public string? EmploymentCategory { get; init; }
    }

    /// <summary>
    /// S138 / TASK-13802 — the DATE-FREE future-dating refusal. Deliberately carries no
    /// <c>provided</c> / <c>expected</c> dates: this string is the sibling of the writer's
    /// employment-start-floor refusal, and THAT one must never echo the employee's hire date to
    /// the wire (an HR-scoped field, same handling class as the birth date). Keeping both refusals
    /// on one date-free shape means a client cannot tell the two apart by probing — and cannot
    /// learn a date it was not shown.
    /// </summary>
    private const string FutureDatedProfileError =
        "EffectiveFrom cannot be in the future; a profile change may be recorded for today or any past date.";

    /// <summary>
    /// S138 / TASK-13802 — the missing-date refusal (see the presence guard in the PUT). Kept
    /// separate from the future-dating message because it names a MALFORMED REQUEST, not a policy.
    /// </summary>
    private const string MissingEffectiveFromError =
        "EffectiveFrom is required and must be a real date.";
}
