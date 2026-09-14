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
/// <b>EffectiveFrom validator — S141 / TASK-14104 (ADR-040 Increment 4): the future is now legal.</b>
/// Plain language: HR can say "her fraction changes on 1 November", and the system records the
/// decision today and lets it take effect on the day it says. That is the whole point of
/// Increment 4, and until S141 this endpoint refused it — the refusal ran here, in the validator,
/// <i>before</i> the write ever reached the repository, so lifting it in the data layer alone
/// would have shipped the date picker dead: HR picks a date, the endpoint says no, and every layer
/// beneath it would have accepted the write happily.
/// <br/>
/// The history, kept because it explains the shape: S33 accepted only <c>EffectiveFrom == today</c>;
/// S138 widened it to <c>&lt;= today</c> (backdating — "her fraction actually changed on the 10th");
/// S141 removes the upper bound entirely. What REMAINS is the PRESENCE guard (a non-nullable
/// <c>DateOnly</c> that is omitted binds <c>0001-01-01</c>, which would route as a correction
/// covering all recorded history) — that is a malformed-request check, not a policy — and the
/// employment-start floor raised by the writer, which is date-free because the hire date must never
/// reach the wire (ADR-040 D7).
/// <br/>
/// "Today" is still the UTC day, read from the injected <see cref="TimeProvider"/>
/// (<c>TimeProvider.System</c> in production) since S139 / TASK-13907, so a date-sensitive test host
/// can fix it. It no longer gates the request; it is what the response body and the scheduled-change
/// lookup are anchored on. The Copenhagen business-date convention used by the settlement / worklist
/// paths is deliberately NOT used here: this endpoint must agree with the browser's
/// <c>new Date().toISOString().slice(0,10)</c> UTC slice, not with the settlement calendar.
/// </para>
///
/// <para>
/// <b>What lifting the refusal cost elsewhere, in one sentence, because it is not obvious.</b> The
/// refusal was the only reason "the row with no end date" and "the row describing today" were the
/// same row. Wave 1 (TASK-14102) separated them everywhere in the data layer; this endpoint's share
/// is that every read it performs is an AS-OF-TODAY read, the concurrency token it stamps is
/// <c>users.version</c> rather than any row's own version, and the DELETE retires scheduled rows
/// alongside the current one.
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
            //
            // S141 / TASK-14104 (refinement B0, owner requirement 2026-09-11) — the read now also
            // carries the NEXT CHANGE ALREADY SCHEDULED, in the SAME statement. The owner asked:
            // "should it not be visible to an HR employee looking at a page, that another has
            // scheduled a change?" Once a change can be dated ahead, a screen that shows only
            // today's value is not merely incomplete, it is misleading — HR reads 0.8, does not
            // know it becomes 0.6 on 1 November, and acts on a number that is about to stop being
            // true. Carrying it in the PAYLOAD rather than leaving each screen to fetch it is the
            // difference between a requirement and a bolt-on: every present and future consumer
            // gets it, and none can forget to ask.
            //
            // NOT extended to the "no row covers today" case, deliberately: this read anchors on
            // the covering row, so an employee whose ONLY row is scheduled still 404s here. That
            // state is unreachable through the product (create always writes at today, this PUT is
            // edit-only, and the DELETE retires scheduled rows with the current one) and it is what
            // S141's B8 detector exists to SURFACE rather than what this payload exists to display.
            var hit = await repository.GetByEmployeeIdWithScheduledAsync(employeeId, ct);
            if (hit is null)
                return Results.NotFound(new { error = "Employee profile not found" });
            var (profile, version, scheduled) = hit;

            context.Response.Headers.ETag = $"\"{version}\"";
            // S112 / TASK-11201 — named record (EmployeeProfileResponse) replaces the anonymous shape.
            // S141 / TASK-14104 — one ADDITIVE, nullable member (`scheduled`); every existing client
            // ignores it and reads the same five fields in the same order.
            return Results.Ok(new EmployeeProfileResponse(
                profile.EmployeeId,
                profile.PartTimeFraction,
                profile.Position,
                profile.IsPartTime,
                version,
                ToDto(scheduled)));
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
        // S141 / TASK-14104 changes (ADR-040 Increment 4):
        //   • The future-date refusal is GONE (see the class doc). Any date is now routed.
        //   • The 404 pre-check asks "does a row COVER TODAY", not "is there an open row" —
        //     the two stopped being the same question the moment a change can be dated ahead.
        //   • The 200 body carries the next SCHEDULED change (B0) alongside today's values.
        //   • OQ-6: a today-dated edit that a scheduled change would truncate can, at the
        //     caller's explicit request, ALSO carry the edited field into that scheduled change.
        //
        // Error mapping:
        //   • 422 — missing EffectiveFrom (presence guard), the writer's employment-start floor,
        //           or InvalidProfileSupersessionException defense-in-depth
        //   • 428 — missing / malformed If-Match (EtagHeaderHelper)
        //   • 412 — OptimisticConcurrencyException (stale version, ADR-019 D2)
        //   • 404 — no row covers today for the employee (pre-check OR KeyNotFoundException)
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

            // ── S141 / TASK-14104 — THE FUTURE-DATE REFUSAL IS LIFTED HERE ──
            // What used to sit on this line: `if (body.EffectiveFrom > today) return 422`. It was
            // one of SEVEN such refusals (four in the repositories, three in endpoint validators),
            // and this is one of the three that face the user — it ran BEFORE the write reached the
            // repository, so lifting the repository guards alone would have left HR picking
            // 1 November and being told the date cannot be in the future while every layer beneath
            // accepted it. See the class doc for what the refusal was load-bearing FOR.
            //
            // `today` survives the refusal's removal and is now used for three things, none of them
            // a gate: the AS-OF-TODAY response body, the scheduled-change lookup B0 returns, and the
            // OQ-6 test for whether the row that truncated this write is a SCHEDULED change (it
            // starts after today) or ordinary closed history (it does not). Still the UTC day off
            // the injected TimeProvider (S139 / TASK-13907), so a fixed-clock test host moves all
            // three together.
            var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

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

                // Step 0b Reviewer BLOCKER-3 absorption — Case A 404 pre-check.
                // PUT is an admin EDIT surface; it must NOT create a net-new row, and after a
                // soft-delete it must not RESURRECT a deliberately retired profile (the router's
                // trailing-gap case T stays a repository-level case — refinement Reviewer c3 NEW-8).
                // Everything "before"-shaped now comes from the writer's COVERING pre-image, so this
                // read is reduced to the existence probe it always logically was.
                //
                // ── S141 / TASK-14104 (wave-1 finding L) — the probe asks "COVERS TODAY" ──
                // It used to ask `effective_to IS NULL`, which meant the same thing only while
                // future-dating was refused. Under scheduling an employee whose ONLY row starts in
                // November HAS an open row, so the old probe admitted them — and the write then
                // routed as an insert-BEFORE-the-first-row, which is exactly the net-new creation
                // this pre-check exists to forbid, reached through the one verb documented as
                // incapable of it. Asking about coverage of today also puts all three verbs on one
                // question: the GET returns 404 for that employee, the DELETE's own 404 test is
                // coverage of today (wave 1), and now so is this. One question, three verbs, one
                // answer.
                //
                // Trade-off, stated because it is a real loss: if such an employee ever existed
                // (seeded, imported, or legacy), HR can no longer repair them through this PUT —
                // they get a clean 404 instead of a silent row creation. That is the honest answer
                // for a surface whose own GET cannot show them either, and the state is what
                // S141's B8 detector (TASK-14105) exists to surface. A deliberate repair path, if
                // one is ever wanted, is a decision to take with a ruling rather than a side effect
                // of an inconsistent probe.
                bool coversToday;
                await using (var preCmd = new NpgsqlCommand(
                    """
                    SELECT 1
                    FROM employee_profiles
                    WHERE employee_id = @employeeId
                      AND effective_from <= @today
                      AND (effective_to IS NULL OR effective_to > @today)
                    """, conn, tx))
                {
                    preCmd.Parameters.AddWithValue("employeeId", employeeId);
                    preCmd.Parameters.AddWithValue("today", today);
                    coversToday = await preCmd.ExecuteScalarAsync(ct) is not null;
                }

                if (!coversToday)
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
                    // The writer's remaining refusals. S141 / TASK-14104: the future-dating refusal
                    // is GONE from this list — the router no longer raises it and the validator above
                    // no longer pre-empts it. What is left is PrecedesEmploymentStart (a pure
                    // predicate raised before any lock) and NoRecordedEmploymentCategory (S138
                    // Step-5a), raised AFTER the lock because it depends on the routed case: case E
                    // lands before every recorded row, so nothing records which category held then
                    // and guessing would mislabel history. Hence the rollback here covers both.
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
                    // S141 / TASK-14104 (B0) — a no-op still reports what is scheduled. The request
                    // changed nothing, but a change dated ahead is still true and the drawer still
                    // has to show it.
                    //
                    // OQ-6 carry-forward is deliberately NOT attempted on this branch, and the reason
                    // is a definition rather than a shortcut: carry-forward propagates "the field HR
                    // actually edited", and on a no-op the writer has decided, inside the lock, that
                    // NO field was edited — the request equalled the row covering the date
                    // field-for-field. There is nothing to carry. (Wave-1 finding N is the same fact
                    // from the other side: a same-values write is a genuine no-op, so scheduling a
                    // change to a value someone already has correctly reports nothing scheduled. That
                    // is correct behaviour and must not be "fixed" into a phantom.)
                    var noOpScheduled = await ReadScheduledChangeAsync(conn, tx, employeeId, today, ct);
                    await tx.CommitAsync(ct);
                    context.Response.Headers.ETag = $"\"{result.Version}\"";
                    return Results.Ok(new EmployeeProfileResponse(
                        employeeId,
                        noOpFraction,
                        noOpPosition,
                        noOpFraction < 1.0m,
                        result.Version,
                        noOpScheduled));
                }

                // ── S141 / TASK-14104 — everything ONE routed write owes, in one place ──
                // Extracted, not invented: the body below is the S138 sequence verbatim, with the
                // request's three field values and the audit `source` passed in rather than read off
                // `body`. The reason it had to become callable twice is owner ruling OQ-6: a
                // today-dated edit made while a change is already scheduled may, at HR's explicit
                // request, ALSO carry the edited field into that scheduled change — and that is a
                // SECOND routed write. None of these obligations may be skipped for it: ADR-018 D3
                // says a state-changing write emits its event in the same transaction, ADR-026 says
                // it lands an audit-projection row, ADR-032 D4 says the absences inside its interval
                // are re-recorded, and ADR-013 says what it makes stale goes on the HR worklist.
                // Writing the second write "lightly" would have produced a row nobody could explain.
                //
                // Returns the aggregate token AFTER this write, so the caller can chain a second
                // write's If-Match onto it and stamp the LAST one as the ETag.
                async Task<long> ApplyRoutedWriteAsync(
                    SaveEmployeeProfileResult write,
                    decimal partTimeFraction,
                    string? position,
                    string? requestedCategory,
                    DateOnly requestedFrom,
                    long fallbackVersionBefore,
                    string source)
                {
                    var profileId = write.ProfileId;
                    var newVersion = write.Version;

                    // S138 / TASK-13802 — the COVERING row's pre-image is the single source for every
                    // "before"-shaped fact below (audit `previous_data`, the mutation predicate, the
                    // Superseded event's predecessor fields). It is null only for the insert-into-a-gap
                    // cases (E / G), where nothing was covering the date and nothing was closed.
                    var covering = write.Covering;

                    // The interval the write actually produced. `null` upper bound = the row is open.
                    // Everything downstream (revaluation, worklist) is confined to THIS interval, not
                    // to [from, ∞) — recon discovery 1: an inserted history row must not revalue the
                    // absences that belong to the row after it. S141 makes the same sentence true in
                    // the other direction: a TODAY-dated edit must not revalue the absences that
                    // belong to a row someone SCHEDULED, which is why the bound is read off the write.
                    var writtenFrom = write.NewEffectiveFrom ?? requestedFrom;
                    var writtenTo = write.NewEffectiveTo;

                    // The category value the writer actually stamped on the row: the request value when
                    // supplied, else the covering row's, else the live users cache (the writer's own
                    // COALESCE order — mirrored here only to narrate the audit row + the events).
                    var writtenCategory = requestedCategory
                        ?? covering?.EmploymentCategory
                        ?? auditUser.EmploymentCategory;

                    // The aggregate token transition. S141 / TASK-14102 (owner ruling OQ-3 (a)) moved
                    // this token OFF any row: `TimelineVersionBefore` is now `users.version` as observed
                    // under the lock (= the client's If-Match under admin-strict on the FIRST write), and
                    // `write.Version` is `users.version` after it — bumped on EVERY timeline write,
                    // including a history-only split, so the profile GET's ETag moves and a second edit
                    // issued against the stale token 412s.
                    //
                    // Why it had to leave the row: a per-ROW token cannot name a timeline that has more
                    // than one live-ish row. Once a change can be scheduled ahead, "the open row" is the
                    // FUTURE one, so a token read off it would have had nothing to do with the row the
                    // edit actually touched, and every profile edit would have 412'd forever after a
                    // single scheduled change — with a refresh handing back the very number the check
                    // rejects. `users.version` belongs to no row, so it survives a timeline of any shape.
                    var versionBefore = write.TimelineVersionBefore ?? fallbackVersionBefore;

                    // S138 — audit action per the ROUTED case, still inside the 4-valued CHECK
                    // (CREATED / UPDATED / DELETED / SUPERSEDED — no enum widening, refinement
                    // discovery 5): a split closes a row ⇒ SUPERSEDED (whether the covering row was the
                    // open row or a history row); an in-place edit ⇒ UPDATED; a gap fill closes nothing
                    // ⇒ CREATED.
                    var auditAction = write.Kind switch
                    {
                        TemporalWriteKind.Updated => "UPDATED",
                        TemporalWriteKind.Superseded or TemporalWriteKind.Inserted => "SUPERSEDED",
                        _ => "CREATED",
                    };

                    // Audit row.
                    //
                    // version_before / version_after are the per-employee AGGREGATE token (the profile
                    // GET's ETag — `users.version` since S141 / TASK-14102) — NOT the touched row's own
                    // version, which for a history-only split is never issued to any client.
                    // previous_data is the COVERING row's pre-image incl. its interval; new_data is the
                    // written row's values incl. the interval it now occupies (`effectiveTo` null =
                    // open), plus S141's `source` (see ProfileAuditSource).
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
                        partTimeFraction,
                        position,
                        employmentCategory = writtenCategory,
                        effectiveFrom = writtenFrom.ToString("yyyy-MM-dd"),
                        effectiveTo = writtenTo?.ToString("yyyy-MM-dd"),
                        source,
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

                    // ── The users-row write, and why its audit row now looks redundant but is not ──
                    // `users.employment_category` means "the category as of TODAY"; the writer refreshes
                    // it from the row covering today (never from the request), so a purely historical
                    // correction leaves the VALUE untouched by construction.
                    //
                    // S141 / TASK-14102 (B5) changed the other half: `users.version` is now bumped
                    // UNCONDITIONALLY on every real timeline write, because it is the client's token and
                    // a token that does not move on every change detects nothing. So this branch — which
                    // asks "was the users row written?" — is now taken on EVERY real write, and on the
                    // ordinary fraction-or-position edit the row it writes has previous_data ==
                    // new_data. That is correct, not noise: ADR-018 D7 / ADR-019 D8 say a users-row
                    // write owes an audit row for its version transition, and the invariant worth
                    // keeping is "every token transition has an audit row". The pre-image being
                    // unchanged is the honest record of what happened — the token moved, the cached
                    // category did not.
                    //
                    // S141 / TASK-14104 absorbs the wave-1 review's consequence: an auditor looking at
                    // identical before-and-after images needs to know WHY the row exists, and `action`
                    // cannot say (it is CHECK-constrained to four values and widening it is a schema
                    // change this sprint does not take). So the payload carries `source`, which names
                    // the write that produced it — and, once OQ-6 can produce two writes in one request,
                    // distinguishes them from each other as well.
                    if (write.UsersCacheWritten)
                    {
                        var previousUserData = JsonSerializer.Serialize(new
                        {
                            employmentCategory = write.PreviousEmploymentCategoryCache,
                            source,
                        });
                        var newUserData = JsonSerializer.Serialize(new
                        {
                            employmentCategory = write.NewEmploymentCategoryCache,
                            source,
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
                        userAuditCmd.Parameters.AddWithValue("versionBefore", write.UsersVersionBefore!.Value);
                        userAuditCmd.Parameters.AddWithValue("versionAfter", write.UsersVersionAfter!.Value);
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
                    // The last group is UNREACHABLE through this surface (the covers-today pre-check +
                    // the admin-strict If-Match make an uncovered timeline a 404/412), but it is mapped
                    // rather than thrown so the switch is total against the router's kinds.
                    //
                    // S66 / TASK-6604 — the emitted event's EventId is the revaluation's
                    // TriggeringProfileEventId AND the worklist trigger's causal link.
                    Guid triggeringProfileEventId;
                    if (write.Kind == TemporalWriteKind.Updated)
                    {
                        // B' — the row STARTING on the requested date edited in place (open, history, or
                        // — new in S141 — the SCHEDULED row an OQ-6 carry-forward targets).
                        var updatedEvent = new EmployeeProfileUpdated
                        {
                            ProfileId = profileId,
                            EmployeeId = employeeId,
                            PartTimeFraction = partTimeFraction,
                            Position = position,
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
                    else if (write.Kind is TemporalWriteKind.Superseded or TemporalWriteKind.Inserted)
                    {
                        // C' — the covering row was CLOSED at the requested date and a new row inserted
                        // for the remainder of its old interval. Under end-exclusive semantics
                        // (ADR-018 D9) the predecessor's effective_to == the new row's effective_from.
                        // NewEffectiveTo distinguishes the two shapes: null = the classic cross-day
                        // supersession (the new row is open); a date = D8's insert-between (the new row
                        // ends where the covering row used to end, and the later rows are untouched).
                        // S141: a today-dated edit made while a change is SCHEDULED lands in the second
                        // shape — the new row ends where the scheduled change begins, which is exactly
                        // the truncation OQ-6 exists to make visible and, on request, to carry through.
                        var supersededEvent = new EmployeeProfileSuperseded
                        {
                            PredecessorProfileId = covering!.ProfileId,
                            NewProfileId = profileId,
                            EmployeeId = employeeId,
                            PredecessorEffectiveFrom = covering.EffectiveFrom,
                            PredecessorEffectiveTo = writtenFrom,
                            NewEffectiveFrom = writtenFrom,
                            NewEffectiveTo = writtenTo,
                            PartTimeFraction = partTimeFraction,
                            Position = position,
                            EmploymentCategory = writtenCategory,
                            PredecessorVersion = covering.Version,
                            NewVersion = write.ProducedRowVersion,
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
                            PartTimeFraction = partTimeFraction,
                            Position = position,
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
                    var fractionChanged = covering is null || covering.PartTimeFraction != partTimeFraction;
                    var positionChanged = covering is null
                        || !string.Equals(covering.Position, position, StringComparison.Ordinal);
                    var categoryChanged = requestedCategory is not null
                        && !string.Equals(covering?.EmploymentCategory, requestedCategory, StringComparison.Ordinal);

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
                            conn, tx, employeeId, partTimeFraction, position,
                            writtenFrom, writtenTo, auditUser.PrimaryOrgId,
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

                    return newVersion;
                }

                // ── The write HR actually asked for ──
                var newVersion = await ApplyRoutedWriteAsync(
                    result, body.PartTimeFraction, body.Position, body.EmploymentCategory,
                    body.EffectiveFrom, expectedVersion, ProfileAuditSource.AdminEdit);

                // ── S141 / TASK-14104 — OQ-6: carry the edited field into the scheduled change ──
                //
                // The problem, in plain language. HR fixes someone's job title today while a change is
                // already scheduled for 1 November. The edit becomes a row that ENDS on 1 November, so
                // on that date the title silently reverts to whatever the scheduled row says — taking
                // the payroll wage-type key with it. Nobody chose that and no screen showed it.
                //
                // Owner ruling OQ-6 (a): ASK. The drawer detects the scheduled change (B0 puts it in
                // the payload, so this costs no extra query) and offers two readings of "today":
                // apply until the scheduled change, or apply AND update the scheduled change too. The
                // first is the default and is exactly the pre-S141 behaviour; the second is this block.
                //
                // The two things the ruling is precise about, and both are load-bearing:
                //   • Carry-forward updates ONLY the field HR actually edited. Rewriting the whole
                //     scheduled row would silently discard the rest of a colleague's scheduled
                //     decision, which is the same class of surprise in the other direction. So each
                //     field is taken from the request when it CHANGED against the covering row, and
                //     from the scheduled row otherwise.
                //   • The target is the row that TRUNCATED this write and starts after today. Reading
                //     `NewEffectiveTo` gives the following row for free; testing `> today` is what
                //     separates a SCHEDULED change (carry into it — it has not happened yet) from
                //     ordinary closed HISTORY (never — carrying into it would rewrite the past on a
                //     backdated insert-between, which OQ-6 never contemplated and nobody asked for).
                //
                // It is a second routed write through the SAME writer, dated at the scheduled row's
                // start, so it lands on the router's in-place-edit case. It is deliberately NOT a new
                // repository method: nothing about it is new behaviour, only a second use of existing
                // behaviour. It bumps the token again — correctly — so the response carries the LAST
                // token, and the audit trail holds two chained transitions rather than one that skips
                // a number.
                var carryForwardTarget = body.CarryForwardToScheduledChange == true
                    && result.NewEffectiveTo is { } boundary && boundary > today
                        ? boundary
                        : (DateOnly?)null;

                if (carryForwardTarget is { } carryFrom)
                {
                    // The target row as it stands, read in-tx and addressed BY ITS START DATE: the
                    // values HR did not edit must survive verbatim, so they have to be read from the
                    // row that is about to be edited in place.
                    //
                    // Addressing it by date rather than by "the next row after today" is not
                    // pedantry — it is a bug I wrote and then found. Those two are the same row only
                    // when the primary write is dated TODAY. If HR schedules a change for 1 October
                    // while one already exists for 1 November, "the next row after today" is the row
                    // this request just created, not the one it was truncated by, and the
                    // carry-forward would have silently done nothing while reporting success.
                    var scheduledRow = await ReadRowStartingAtAsync(conn, tx, employeeId, carryFrom, ct);
                    if (scheduledRow is not null)
                    {
                        var coveringPre = result.Covering;
                        var carryFraction =
                            coveringPre is null || coveringPre.PartTimeFraction != body.PartTimeFraction
                                ? body.PartTimeFraction
                                : scheduledRow.PartTimeFraction;
                        var carryPosition =
                            coveringPre is null
                            || !string.Equals(coveringPre.Position, body.Position, StringComparison.Ordinal)
                                ? body.Position
                                : scheduledRow.Position;
                        // Category is the only OPTIONAL field: `null` on the request means "keep what
                        // the covering row says", so an unsupplied category cannot have been edited and
                        // the scheduled row's own value is carried through unchanged.
                        var carryCategory =
                            body.EmploymentCategory is not null
                            && !string.Equals(coveringPre?.EmploymentCategory, body.EmploymentCategory, StringComparison.Ordinal)
                                ? body.EmploymentCategory
                                : scheduledRow.EmploymentCategory;

                        SaveEmployeeProfileResult carryResult;
                        try
                        {
                            carryResult = await repository.SupersedeAndCreateAsync(
                                conn, tx,
                                new EmployeeProfileSupersedeRequest(
                                    EmployeeId: employeeId,
                                    PartTimeFraction: carryFraction,
                                    Position: carryPosition,
                                    EffectiveFrom: carryFrom,
                                    EmploymentCategory: carryCategory,
                                    EmploymentStartDate: auditUser.EmploymentStartDate),
                                // The token the FIRST write left behind. Chaining it rather than
                                // re-sending the client's If-Match is what keeps the two writes one
                                // logical request: a second write validated against the pre-first-write
                                // token would 412 against the bump its own sibling just performed.
                                expectedVersion: newVersion,
                                ct);
                        }
                        catch (TemporalWriteRejectedException ex)
                        {
                            // Only the employment-start floor can fire here, and only absurdly (the
                            // carry date is after today, which is after any hire that already has a row
                            // covering today). Mapped rather than swallowed so a future writer refusal
                            // surfaces as a 422 the caller can read instead of a 500.
                            await tx.RollbackAsync(ct);
                            return Results.UnprocessableEntity(new { error = ex.Message });
                        }

                        // A carry-forward whose merged values already equal the scheduled row is a
                        // genuine no-op — HR asked to propagate a value that is already there. Nothing
                        // written, nothing audited, token unmoved. Treating it as a write would
                        // manufacture an audit row describing a change that did not happen.
                        if (!carryResult.IsNoOp)
                        {
                            newVersion = await ApplyRoutedWriteAsync(
                                carryResult, carryFraction, carryPosition, carryCategory,
                                carryFrom, newVersion, ProfileAuditSource.ScheduledCarryForward);
                        }
                    }
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
                //
                // S141 / TASK-14104 (B0 + B6, together) — the same re-read now also yields the
                // scheduled change, so the drawer sees what a save actually left behind: the values
                // in force today, and the change still dated ahead (updated, if HR carried into it).
                // This is also what makes the ROUND TRIP inert in a way a client can verify: a GET
                // returns today's values, a PUT of those same values at today is the writer's
                // same-values no-op, and the body that comes back describes the identical state.
                var todayAsOf = await ReadProfileAsOfTodayAsync(conn, tx, employeeId, today, ct);
                var (todayFraction, todayPosition) =
                    todayAsOf ?? (body.PartTimeFraction, body.Position);
                var todayScheduled = await ReadScheduledChangeAsync(conn, tx, employeeId, today, ct);

                await tx.CommitAsync(ct);

                context.Response.Headers.ETag = $"\"{newVersion}\"";
                // S112 / TASK-11201 — named record (EmployeeProfileResponse), the SAME record as the
                // GET. The VALUES are sourced from the row covering today rather than from the
                // request (TASK-13810); S141 adds the additive `scheduled` member and nothing else.
                // The ETag is the aggregate token AFTER the last write this request performed —
                // which, when HR chose carry-forward, is the SECOND write's. Handing back the first
                // write's number would give the drawer a token the server has already superseded,
                // and its very next save would 412 for a change it made itself.
                return Results.Ok(new EmployeeProfileResponse(
                    employeeId,
                    todayFraction,
                    todayPosition,
                    todayFraction < 1.0m,
                    newVersion,
                    todayScheduled));
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
        // ADR-023 D8 soft-delete divergence: the closed row's `version` column is
        // UNCHANGED — soft-delete is row-state-change, not field-mutation; the row
        // "disappears" from as-of-today reads, so bumping a version is redundant.
        //
        // ── S141 / TASK-14104 — what a delete means once a change can be SCHEDULED ──
        //
        // The defect, in plain language. "Delete the profile" used to mean "stamp an end date on
        // the row with no end date". Schedule a change for 1 November and the row with no end date
        // IS that November row — so the delete stamped today onto a row starting in November,
        // producing a backwards interval covering nothing, while the row actually describing the
        // employee survived untouched. The endpoint returned 204, the audit trail said DELETED, and
        // the person was not deleted. Wave 1 fixed the write; this endpoint's share is to ADOPT it
        // and to record what it did.
        //
        // Owner ruling OQ-5 (a) — "delete both", taken AGAINST the standing recommendation — with
        // one condition the owner attached and this handler exists to meet: **the retirement of a
        // scheduled change is itself audited.** A row that was audited into existence must not
        // vanish unrecorded, least of all when the person who scheduled it is not the person
        // deleting. So each retired row gets its own `employee_profile_audit` row AND its own
        // domain event with an ADR-026 projection — the same treatment its creation got, because
        // "someone else's decision was destroyed" is exactly the kind of fact an audit trail is for.
        //
        // Wave-1 review W1, also fixed here: the audit's version columns now record the AGGREGATE
        // token (`users.version`), the same kind of number the PUT records. They used to hold the
        // closed row's own version, so a reconstruction reading the audit table in order hit two
        // different number series and could not chain them. The token is unchanged by a delete, so
        // before still equals after and ADR-023 D8's "no bump on delete" intent is preserved
        // exactly — what changes is WHICH number is written, not whether it moves.
        //
        // Error mapping:
        //   • 428 — missing / malformed If-Match (EtagHeaderHelper)
        //   • 412 — OptimisticConcurrencyException (a row covers today, `users.version` differs)
        //   • 404 — KeyNotFoundException (no row covers today — also the retry-after-delete
        //           case per ADR-023 D8 row-disappearance idempotency)
        //   • 403 — OrgScopeValidator denial (cross-org guard)
        // ═══════════════════════════════════════════
        app.MapDelete("/api/admin/employee-profiles/{employeeId}", async (
            string employeeId,
            EmployeeProfileRepository repository,
            DbConnectionFactory connectionFactory,
            IOutboxEnqueue outbox,
            IAuditProjectionMapper<EmployeeProfileSoftDeleted> softDeletedAuditMapper,
            // S141 / TASK-14104 (owner ruling OQ-5 (a)) — the mapper for the retirement event.
            IAuditProjectionMapper<EmployeeProfileScheduledChangeRetired> scheduledRetiredAuditMapper,
            AuditProjectionRepository auditRepo,
            UserRepository userRepo,
            OrgScopeValidator scopeValidator,
            // S139 / TASK-13907 — the server-"today" seam (TimeProvider.System in production).
            // Read ONCE below into `today` and used for BOTH dated outputs of this DELETE: the
            // row's `effective_to` stamp (passed into the writer as `closeDate`, where the
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
                // ── S141 / TASK-14104 — the full soft-delete, and everything it retired ──
                // The pre-S141 handler read the profile separately for the audit's `previous_data`
                // and then called the 2-tuple `SoftDeleteAsync`. Both halves had to change, for the
                // same reason: neither could describe a timeline with more than one live-ish row.
                // The separate read answered "the open row", which under scheduling is the FUTURE
                // one — so the audit would have recorded values that were never in force. And the
                // 2-tuple return could not carry what the delete retired, which owner ruling
                // OQ-5 (a) makes mandatory to audit.
                //
                // `SoftDeleteTimelineAsync` returns both: the pre-image of the row that ACTUALLY
                // covered today (`Covering`) and every scheduled row it retired. One call, one lock,
                // one snapshot — so nothing here can describe a different moment than the write did.
                EmployeeProfileSoftDeleteResult deleted;
                try
                {
                    deleted = await repository.SoftDeleteTimelineAsync(
                        conn, tx, employeeId, expectedVersion, today, ct);
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
                    // No row covers today. A stale If-Match retry after a successful soft-delete
                    // also lands here per ADR-023 D8 row-disappearance idempotency (locked by the
                    // TASK-3312 D-test): the repository evaluates coverage BEFORE the token, so the
                    // answer stays "gone" (404) rather than "changed" (412).
                    await tx.RollbackAsync(ct);
                    return Results.NotFound(new { error = "Employee profile not found" });
                }

                var profileId = deleted.ProfileId;

                // ── The audit's version columns: the AGGREGATE token, before == after ──
                // Wave-1 review W1. The PUT records `users.version`; this handler used to record the
                // closed ROW's own version, so the two audit families spoke different number series
                // and a reconstruction reading `employee_profile_audit` in order could not chain a
                // delete to the edits around it. `UsersVersion` is the aggregate token as observed
                // under the delete's own lock. ADR-023 D8's intent survives untouched: the delete
                // does not move the token, so before and after are the SAME value here — the "no
                // bump on soft-delete" divergence from the sibling ADR-019 D8 endpoints is preserved
                // exactly. What changed is WHICH number both columns carry.
                var aggregateToken = deleted.UsersVersion;

                // Audit row — action='DELETED' per the 4-valued CHECK. `previous_data` is the
                // COVERING row's pre-image: the values that were ACTUALLY in force when HR deleted,
                // which under future-dating is not what the open row says. new_data carries only the
                // `source` discriminator — there is no "current" payload after a close, but an
                // auditor still needs to tell this row apart from a retired scheduled change, whose
                // action is also DELETED (see ProfileAuditSource).
                var previousData = JsonSerializer.Serialize(new
                {
                    partTimeFraction = deleted.Covering.PartTimeFraction,
                    position = deleted.Covering.Position,
                    employmentCategory = deleted.Covering.EmploymentCategory,
                    effectiveFrom = deleted.Covering.EffectiveFrom.ToString("yyyy-MM-dd"),
                    effectiveTo = deleted.Covering.EffectiveTo?.ToString("yyyy-MM-dd"),
                });
                var deleteNewData = JsonSerializer.Serialize(new
                {
                    effectiveTo = deleted.EffectiveTo.ToString("yyyy-MM-dd"),
                    source = ProfileAuditSource.AdminDelete,
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
                        @previousData::jsonb, @newData::jsonb,
                        @versionBefore, @versionAfter,
                        @actorId, @actorRole)
                    """, conn, tx))
                {
                    auditCmd.Parameters.AddWithValue("profileId", profileId);
                    auditCmd.Parameters.AddWithValue("employeeId", employeeId);
                    auditCmd.Parameters.AddWithValue("previousData", previousData);
                    auditCmd.Parameters.AddWithValue("newData", deleteNewData);
                    auditCmd.Parameters.AddWithValue("versionBefore", aggregateToken);
                    auditCmd.Parameters.AddWithValue("versionAfter", aggregateToken);
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
                    // Taken from the writer's own report rather than recomputed, which is the same
                    // guarantee arrived at from the other end.
                    EffectiveTo = deleted.EffectiveTo,
                    // The closed ROW's own version, unchanged (ADR-023 D8). This event describes a
                    // row, so a row version is the right thing for it to carry — unlike the audit
                    // columns above, which describe the AGGREGATE and therefore carry its token.
                    RowVersion = deleted.Version,
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
                var targetOrgId = auditUser?.PrimaryOrgId
                    ?? throw new InvalidOperationException(
                        $"Audit projection: employee {employeeId} not found or inactive.");
                var auditCtx = new AuditProjectionContext(
                    ActorId: actor.ActorId,
                    ActorPrimaryOrgId: actor.OrgId,
                    CorrelationId: actor.CorrelationId,
                    OccurredAt: new DateTimeOffset(softDeletedEvent.OccurredAt),
                    ResolvedTargetOrgId: targetOrgId);
                var auditRow = softDeletedAuditMapper.Map(softDeletedEvent, auditCtx);
                await auditRepo.InsertAsync(conn, tx, softDeletedEvent.EventId, outboxId, softDeletedEvent.EventType, auditRow, auditCtx, ct);

                // ── S141 / TASK-14104 (owner ruling OQ-5 (a)) — the retired scheduled changes ──
                //
                // The condition the owner attached to "delete both": a scheduled change may have
                // been entered by a DIFFERENT HR person, days earlier, as a deliberate decision.
                // Deleting the profile destroys it as a side effect of an unrelated action. The
                // owner accepted that cost knowingly — on condition that the destruction is recorded
                // as deliberately as the creation was. So each retired row gets the same treatment
                // its creation got: its own `employee_profile_audit` row AND its own domain event
                // with an ADR-026 projection, all in THIS transaction (ADR-018 D3), so the
                // retirement can never commit without its record or vice versa.
                //
                // Emitted AFTER the soft-delete event, on the SAME stream, deliberately: per-stream
                // ordering then reads as cause then consequence — "the profile was deleted, and
                // therefore these scheduled changes were retired" — which is the order a replay
                // needs to make sense of them.
                //
                // The list is EMPTY in the ordinary case, which is the case to optimise the reader's
                // attention for: nothing scheduled, nothing retired, no extra rows at all.
                foreach (var retired in deleted.RetiredScheduledRows)
                {
                    // The retirement is the zero-width close `[from, from)` — the row stays on the
                    // timeline to be explained rather than vanishing from it (no timeline table in
                    // this system has ever hard-deleted a row). previous_data is what the row was
                    // GOING to bring and when; new_data records the zero-width shape it now has plus
                    // the `source` that separates it from the delete's own DELETED row.
                    var retiredPrevious = JsonSerializer.Serialize(new
                    {
                        partTimeFraction = retired.PartTimeFraction,
                        position = retired.Position,
                        employmentCategory = retired.EmploymentCategory,
                        effectiveFrom = retired.EffectiveFrom.ToString("yyyy-MM-dd"),
                        effectiveTo = retired.PreviousEffectiveTo?.ToString("yyyy-MM-dd"),
                    });
                    var retiredNew = JsonSerializer.Serialize(new
                    {
                        effectiveFrom = retired.EffectiveFrom.ToString("yyyy-MM-dd"),
                        effectiveTo = retired.EffectiveFrom.ToString("yyyy-MM-dd"),
                        source = ProfileAuditSource.ScheduledChangeRetired,
                    });
                    await using (var retiredAuditCmd = new NpgsqlCommand(
                        """
                        INSERT INTO employee_profile_audit (
                            profile_id, employee_id, action,
                            previous_data, new_data,
                            version_before, version_after,
                            actor_id, actor_role)
                        VALUES (
                            @profileId, @employeeId, 'DELETED',
                            @previousData::jsonb, @newData::jsonb,
                            @versionBefore, @versionAfter,
                            @actorId, @actorRole)
                        """, conn, tx))
                    {
                        retiredAuditCmd.Parameters.AddWithValue("profileId", retired.ProfileId);
                        retiredAuditCmd.Parameters.AddWithValue("employeeId", employeeId);
                        retiredAuditCmd.Parameters.AddWithValue("previousData", retiredPrevious);
                        retiredAuditCmd.Parameters.AddWithValue("newData", retiredNew);
                        // Same aggregate token, same before == after, for the same reason as the
                        // delete's own row: nothing about a retirement moves the client's token.
                        retiredAuditCmd.Parameters.AddWithValue("versionBefore", aggregateToken);
                        retiredAuditCmd.Parameters.AddWithValue("versionAfter", aggregateToken);
                        retiredAuditCmd.Parameters.AddWithValue("actorId", actorId);
                        retiredAuditCmd.Parameters.AddWithValue("actorRole", actorRole);
                        await retiredAuditCmd.ExecuteNonQueryAsync(ct);
                    }

                    var retiredEvent = new EmployeeProfileScheduledChangeRetired
                    {
                        ProfileId = retired.ProfileId,
                        EmployeeId = employeeId,
                        EffectiveFrom = retired.EffectiveFrom,
                        PreviousEffectiveTo = retired.PreviousEffectiveTo,
                        PartTimeFraction = retired.PartTimeFraction,
                        Position = retired.Position,
                        EmploymentCategory = retired.EmploymentCategory,
                        RowVersion = retired.Version,
                        // The causal link: which delete did this. Without it a replay sees a row
                        // stop existing with nothing to attribute it to, which is the exact hole the
                        // owner's condition exists to close.
                        RetiredWithProfileId = profileId,
                        RetiredOn = deleted.EffectiveTo,
                        ActorId = actorId,
                        ActorRole = actorRole,
                        CorrelationId = actor.CorrelationId,
                    };
                    var retiredOutboxId = await outbox.EnqueueAndReturnIdAsync(
                        conn, tx, streamId, retiredEvent, ct);
                    var retiredCtx = auditCtx with { OccurredAt = new DateTimeOffset(retiredEvent.OccurredAt) };
                    var retiredProjection = scheduledRetiredAuditMapper.Map(retiredEvent, retiredCtx);
                    await auditRepo.InsertAsync(
                        conn, tx, retiredEvent.EventId, retiredOutboxId, retiredEvent.EventType,
                        retiredProjection, retiredCtx, ct);
                }

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
    /// of the OPEN row. Those two coincided only while future-dating was refused; S141 / TASK-14104
    /// lifts that refusal and they now genuinely diverge — an open row starting in November is the
    /// open row and covers no day today. This read was written for that divergence in S138 and is
    /// correct under it unchanged, which is the whole point of having written it that way early.
    /// </para>
    ///
    /// <para>
    /// Returns <c>null</c> when NO row covers today, and the callers then answer with the values
    /// they just wrote. S138 Step-7a (Codex WARNING, absorbed): an earlier version threw here on the
    /// stated ground that "the 404 pre-check guarantees a row covers today" — but that pre-check
    /// guaranteed an OPEN row, which is not the same thing. (S141 / TASK-14104 closed that
    /// particular gap from the other end: the PUT's pre-check now asks about coverage of today, so
    /// the two statements finally mean what the old comment claimed. The null branch STAYS
    /// regardless — seeded, imported or legacy data can still produce an uncovered timeline, and
    /// nothing in the schema forbids it.) Throwing was the worse failure by far: this read runs
    /// INSIDE the transaction, so the exception rolled back an otherwise VALID correction and
    /// returned 500 — losing the write to protect a courtesy view of it. When there is genuinely no
    /// state as of today, echoing what was just written is the only honest answer available, and the
    /// correction survives.
    /// </para>
    /// </summary>
    private static async Task<(decimal PartTimeFraction, string? Position)?> ReadProfileAsOfTodayAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string employeeId, DateOnly today, CancellationToken ct)
    {
        // S141 / TASK-14104 — deterministic single row. Pre-S141 the as-of-today predicate could not
        // match twice because the WRITERS could not produce two rows covering one day; that is still
        // true, but the DATABASE does not forbid it (the partial-unique index enforces "at most one
        // OPEN row", and non-overlap of dated rows is a router invariant rather than a constraint).
        // The sibling repository read added ORDER BY + LIMIT for exactly this reason; a read used to
        // build a response body should not be the one place that trusts an invariant instead of
        // stating it.
        await using var cmd = new NpgsqlCommand(
            """
            SELECT part_time_fraction, position
            FROM employee_profiles
            WHERE employee_id = @employeeId
              AND effective_from <= @today
              AND (effective_to IS NULL OR effective_to > @today)
            ORDER BY effective_from DESC
            LIMIT 1
            """, conn, tx);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("today", today);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;
        return (reader.GetDecimal(0), reader.IsDBNull(1) ? null : reader.GetString(1));
    }

    // ── S141 / TASK-14104 (refinement B0, owner requirement 2026-09-11) — the scheduled change ──

    /// <summary>
    /// The next profile change already scheduled AFTER <paramref name="today"/>, read inside the
    /// PUT's own transaction so it reflects what this request just did.
    ///
    /// <para>
    /// <b>Why the PUT carries it at all, not just the GET.</b> The 200 body of an edit is what the
    /// drawer re-renders from. After a today-dated save that a scheduled change truncates — and after
    /// an OQ-6 carry-forward that rewrote that scheduled change — the drawer must show what is now
    /// true, and "re-fetch and hope" is not a contract. The read is one statement in the same
    /// transaction as the write, so the values, the token and the scheduled change can never describe
    /// three different moments.
    /// </para>
    ///
    /// <para>
    /// A ZERO-WIDTH row <c>[f, f)</c> is excluded: it covers no day at all, and it is the trace a
    /// retired scheduled row leaves behind (the DELETE's zero-width close), so reporting it would show
    /// HR a change that was deliberately cancelled. Same rule as the repository's own reads — stated
    /// here rather than assumed, because the two SQL texts have to agree and nothing enforces that
    /// they do.
    /// </para>
    /// </summary>
    private static async Task<ScheduledProfileChange?> ReadScheduledChangeAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string employeeId, DateOnly today, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            """
            SELECT effective_from, effective_to, part_time_fraction, position, employment_category
            FROM employee_profiles
            WHERE employee_id = @employeeId
              AND effective_from > @today
              AND (effective_to IS NULL OR effective_to > effective_from)
            ORDER BY effective_from
            LIMIT 1
            """, conn, tx);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("today", today);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;
        return new ScheduledProfileChange(
            EffectiveFrom: reader.GetFieldValue<DateOnly>(0),
            EffectiveTo: reader.IsDBNull(1) ? null : reader.GetFieldValue<DateOnly>(1),
            PartTimeFraction: reader.GetDecimal(2),
            Position: reader.IsDBNull(3) ? null : reader.GetString(3),
            EmploymentCategory: reader.IsDBNull(4) ? null : reader.GetString(4));
    }

    /// <summary>
    /// S141 / TASK-14104 (owner ruling OQ-6) — the profile row that STARTS on a given date, read
    /// inside the PUT's transaction.
    ///
    /// <para>
    /// <b>Why by START DATE and not "the next row after today".</b> A carry-forward edits the row
    /// that truncated the primary write, and it must preserve every field HR did not touch — so it
    /// has to read exactly that row. "The next row after today" is the same row ONLY when the primary
    /// write was dated today. Schedule a change for 1 October while one already exists for
    /// 1 November and the two diverge: the nearest future row is the one this very request just
    /// created. Addressing by date is also what the write itself does (the router's in-place case is
    /// selected by a row starting on the requested date), so read and write agree by construction.
    /// </para>
    ///
    /// <para>
    /// <c>(employee_id, effective_from)</c> is unique (<c>idx_employee_profiles_history</c>), so this
    /// matches at most one row without a tie-break. Zero-width rows are NOT excluded here, unlike the
    /// scheduled-change read: this method answers "what is in that row", and the caller has already
    /// established from the write's own result that the row is a real boundary.
    /// </para>
    /// </summary>
    private static async Task<ScheduledProfileChange?> ReadRowStartingAtAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string employeeId, DateOnly from, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            """
            SELECT effective_from, effective_to, part_time_fraction, position, employment_category
            FROM employee_profiles
            WHERE employee_id = @employeeId
              AND effective_from = @from
            """, conn, tx);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("from", from);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;
        return new ScheduledProfileChange(
            EffectiveFrom: reader.GetFieldValue<DateOnly>(0),
            EffectiveTo: reader.IsDBNull(1) ? null : reader.GetFieldValue<DateOnly>(1),
            PartTimeFraction: reader.GetDecimal(2),
            Position: reader.IsDBNull(3) ? null : reader.GetString(3),
            EmploymentCategory: reader.IsDBNull(4) ? null : reader.GetString(4));
    }

    /// <summary>
    /// Project the repository's <see cref="ScheduledEmployeeProfileChange"/> (the GET's source, read
    /// on the repository's own connection) onto the wire record. Two types for one concept is not
    /// duplication for its own sake: the repository record is infrastructure and may grow fields the
    /// API has not decided to publish, and the wire record is a versioned contract the OpenAPI gate
    /// watches. The projection is the place that decision is made explicitly.
    /// </summary>
    private static ScheduledProfileChange? ToDto(ScheduledEmployeeProfileChange? scheduled)
        => scheduled is null
            ? null
            : new ScheduledProfileChange(
                scheduled.EffectiveFrom,
                scheduled.EffectiveTo,
                scheduled.PartTimeFraction,
                scheduled.Position,
                scheduled.EmploymentCategory);

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
        // S141 / TASK-14104 — the two written values, passed explicitly instead of the request DTO.
        // The PUT can now perform TWO routed writes in one transaction (owner ruling OQ-6's
        // carry-forward), and the second one writes values that are a MERGE of the request and the
        // scheduled row rather than the request itself. Taking the DTO here would have quietly
        // revalued the second interval under the first write's values.
        decimal newPartTimeFraction,
        string? newPosition,
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
                PartTimeFraction = newPartTimeFraction,
                Position = newPosition,
                IsPartTime = newPartTimeFraction < 1.0m,
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
    /// <see cref="EffectiveFrom"/> widened from "today only" to any past-or-today date (S141 /
    /// TASK-14104 removes the remaining upper bound — see below); the writer routes it against the
    /// row covering that date.
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
        /// <summary>S33 — required. <b>S141 / TASK-14104 (ADR-040 Increment 4): ANY date — past,
        /// today, or FUTURE.</b> Dating a change ahead is the feature; the future was refused only
        /// while "the open row" and "the row covering today" had to be the same row, which wave 1
        /// (TASK-14102) ended by converting every current-state read to as-of-today. The value must
        /// still be PRESENT (an omitted non-nullable <c>DateOnly</c> binds <c>0001-01-01</c> and
        /// would route as a correction covering all recorded history), and the writer still refuses
        /// a date before the employee's employment start with a DATE-FREE 422.</summary>
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

        /// <summary>
        /// S141 / TASK-14104 (owner ruling OQ-6 (a)) — OPTIONAL fifth field: HR's answer to "what
        /// did you mean by today?" when a change is already scheduled.
        ///
        /// <para>
        /// <b>Plain language.</b> HR fixes a job title today; a change is already dated for
        /// 1 November. Does the fix apply UNTIL that change (and then revert), or should the new
        /// title carry into the scheduled change as well? Nobody can answer that for HR, so the
        /// drawer asks and sends the answer here. <c>true</c> = carry it forward; <c>null</c> or
        /// <c>false</c> = apply until the scheduled change only.
        /// </para>
        ///
        /// <para>
        /// <b>Why the DEFAULT is "do not carry".</b> Absent means the client did not know about the
        /// prompt — an older frontend, a script, a test. The safe reading of silence is the
        /// pre-S141 behaviour, which is incomplete but never destructive: the edit occupies its own
        /// interval and touches nobody else's decision. Defaulting the other way would let a client
        /// that never asked HR anything overwrite a colleague's scheduled change.
        /// </para>
        ///
        /// <para>
        /// <b>Ignored unless it can mean something.</b> It has an effect only when this write is
        /// actually truncated by a row that starts AFTER today — i.e. a genuine scheduled change.
        /// Sent on an ordinary edit it does nothing, so a client may set it unconditionally without
        /// having to reason about the timeline.
        /// </para>
        /// </summary>
        public bool? CarryForwardToScheduledChange { get; init; }
    }

    // ── S141 / TASK-14104 — who wrote this audit row ──

    /// <summary>
    /// The <c>source</c> discriminator stamped into the <c>new_data</c> JSONB of every audit row
    /// this endpoint writes.
    ///
    /// <para>
    /// <b>Why it exists (wave-1 review W-note, absorbed).</b> Two S141 changes made the audit trail
    /// ambiguous in a way it had not been. First, <c>users.version</c> now bumps on EVERY profile
    /// write, so a <c>users_audit</c> row is written every time — and on an ordinary fraction edit
    /// its before- and after-images are IDENTICAL, because the cached employment category did not
    /// move. An auditor reading that row sees a version transition with no visible cause. Second,
    /// owner ruling OQ-6 lets one request perform TWO routed writes, so two rows can now describe
    /// one HR action and nothing in the columns distinguishes them.
    /// </para>
    ///
    /// <para>
    /// <b>Why a payload field rather than a new action.</b> Both audit tables constrain
    /// <c>action</c> with a four-valued CHECK (CREATED / UPDATED / DELETED / SUPERSEDED). Widening
    /// it is a schema change, and S141 is deliberately schema-free (refinement Assumption 3). The
    /// JSONB payload carries the discriminator instead, which costs nothing and is queryable.
    /// </para>
    /// </summary>
    private static class ProfileAuditSource
    {
        /// <summary>The write HR asked for through the admin profile PUT.</summary>
        public const string AdminEdit = "ADMIN_PROFILE_PUT";

        /// <summary>The second, DERIVED write of an OQ-6 carry-forward: the edited field propagated
        /// into the change that was already scheduled. Distinguishable from the primary write so a
        /// reconstruction can tell what HR typed from what the ruling then did with it.</summary>
        public const string ScheduledCarryForward = "SCHEDULED_CHANGE_CARRY_FORWARD";

        /// <summary>The row covering today, closed by the admin profile DELETE.</summary>
        public const string AdminDelete = "ADMIN_PROFILE_DELETE";

        /// <summary>A row that was SCHEDULED and was retired as a side effect of that delete
        /// (owner ruling OQ-5 (a)). Its own action is DELETED like the row above, so without this
        /// the two are indistinguishable — and they are not the same event: one is what HR chose,
        /// the other is what that choice destroyed.</summary>
        public const string ScheduledChangeRetired = "SCHEDULED_CHANGE_RETIRED_BY_DELETE";
    }

    // S141 / TASK-14104 — `FutureDatedProfileError` is GONE. It was the message of the refusal this
    // sprint lifts (ADR-040 Increment 4), and a refusal message with no refusal behind it is worse
    // than absent: the next reader would take it as evidence the policy still exists. Its sibling on
    // the agreement side (`AdminEndpoints.FutureDatedAgreementCodeError`) went the same way. The
    // DATE-FREE discipline it carried is NOT gone and still governs what remains — the writer's
    // employment-start-floor refusal must never echo the employee's hire date to the wire (ADR-040
    // D7; an HR-scoped field, same handling class as the birth date).

    /// <summary>
    /// S138 / TASK-13802 — the missing-date refusal (see the presence guard in the PUT). It
    /// OUTLIVES the future-dating refusal deliberately: this one names a MALFORMED REQUEST (a
    /// non-nullable <c>DateOnly</c> that was omitted binds <c>0001-01-01</c>, which would route as
    /// a correction covering all recorded history), not a policy about which dates are allowed.
    /// </summary>
    private const string MissingEffectiveFromError =
        "EffectiveFrom is required and must be a real date.";
}
