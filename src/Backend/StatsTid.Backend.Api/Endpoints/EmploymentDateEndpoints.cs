using System.Text.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Backend.Api.Contracts;
using StatsTid.Backend.Api.Endpoints.Helpers;
using StatsTid.Backend.Api.Services;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Security;
using StatsTid.SharedKernel.Calendar;
using StatsTid.SharedKernel.Security;

namespace StatsTid.Backend.Api.Endpoints;

/// <summary>
/// HR admin surface for the employment lifecycle dates. FOUR endpoints, all <c>HROrAbove</c>;
/// cross-org binding is load-bearing in every handler — the policy proves role + scope shape but
/// does NOT bind the actor to the target's organisation (FAIL-001: the validators use
/// <c>FindAll</c>, not <c>FindFirst</c>, on scopes). Neither date ever appears in any
/// Employee-facing DTO / JWT / export.
///   <list type="bullet">
///     <item><description>
///       <b>GET /api/admin/employees/{employeeId}/employment-start-date</b> (S60 / TASK-6006 /
///       ADR-030) — active-only read via
///       <see cref="OrgScopeValidator.ValidateEmployeeAccessAsync"/>; ETag from <c>users.version</c>.
///     </description></item>
///     <item><description>
///       <b>PUT /api/admin/employees/{employeeId}/employment-start-date</b> (S60; S136 /
///       TASK-13604 lifecycle upgrade) — terminated-INCLUSIVE write via
///       <see cref="OrgScopeValidator.ValidateEmployeeAccessIncludingTerminatedAsync"/> (a
///       DELIBERATE S70 R9c allowlist EXTENSION, ruled by ADR-040 D3), admin-strict If-Match
///       (ADR-019 D2), one atomic tx (ADR-018 D3) opened by the ADR-032 D4 employee advisory
///       lock (the S136 lock regime); null clears the date. In-tx guards, all strictly before
///       the UPDATE: the ADR-040 D1 re-hire 409 (settlement-independent), the cross-field
///       inverted-window 422, the ADR-040 D3 strand guard 409. Self-target writes are
///       403-rejected for ALL actors (symmetric with the end-date PUT's S70 Step-7a W1 rule).
///     </description></item>
///     <item><description>
///       <b>GET /api/admin/employees/{employeeId}/employment-end-date</b> (S70 / TASK-7002 /
///       ADR-033 slice 3a) — terminated-INCLUSIVE read via
///       <see cref="OrgScopeValidator.ValidateEmployeeAccessIncludingTerminatedAsync"/> (R9c
///       allowlist surface), so HR can address a deactivated leaver's row; ETag from
///       <c>users.version</c>.
///     </description></item>
///     <item><description>
///       <b>PUT /api/admin/employees/{employeeId}/employment-end-date</b> (S70; S71 / TASK-7102
///       refactor) — terminated-INCLUSIVE write via
///       <see cref="OrgScopeValidator.ValidateEmployeeAccessIncludingTerminatedAsync"/>, with the
///       R1 deactivation lifecycle (delegated to the SHARED
///       <see cref="EmploymentEndDateLifecycleWriter"/> — SPRINT-71 R4 one-implementation; the
///       S70 transitional inline choreography is deleted), the SPRINT-71 R13 range-widened
///       active-settlement 409 guard (full <c>[min..max]</c> ferieår span, with a
///       machine-readable reversal pointer on the 409) and the R12 employee advisory lock in ONE
///       atomic tx; self-target writes are 403-rejected for ALL actors (S70 Step-7a W1 — a
///       second administrator performs self-departures). S136 / TASK-13604 adds the cross-field
///       inverted-window 422 and the ADR-040 D3 strand guard 409, both in-tx before the
///       lifecycle writer.
///     </description></item>
///   </list>
/// </summary>
public static class EmploymentDateEndpoints
{
    public static WebApplication MapEmploymentDateEndpoints(this WebApplication app)
    {
        // ═══════════════════════════════════════════
        // 1. GET /api/admin/employees/{employeeId}/employment-start-date
        //
        // RBAC: HROrAbove + OrgScopeValidator. The ONLY employment-start read surface —
        // it never appears in any Employee-facing DTO / JWT / export. ETag stamped from
        // users.version so the subsequent PUT composes If-Match coherently.
        // 404 when no active user. 403 on cross-org.
        // ═══════════════════════════════════════════
        app.MapGet("/api/admin/employees/{employeeId}/employment-start-date", async (
            string employeeId,
            UserRepository userRepo,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // S76 B1: HROrAbove policy → LocalHR floor (a sub-HR scope covering the employee's
            // org cannot satisfy this HR data gate).
            var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessAsync(actor, employeeId, StatsTidRoles.LocalHR, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            var hit = await userRepo.GetByIdWithVersionAsync(employeeId, ct);
            if (hit is null)
                return Results.NotFound(new { error = "Employee not found" });
            var (user, version) = hit.Value;

            context.Response.Headers.ETag = $"\"{version}\"";
            // S115 / TASK-11501 — named record (BYTE-IDENTICAL wire JSON).
            return Results.Ok(new EmploymentStartDateResponse(
                EmployeeId: user.UserId,
                EmploymentStartDate: user.EmploymentStartDate,
                Version: version));
        }).RequireAuthorization("HROrAbove")
        .Produces<EmploymentStartDateResponse>(StatusCodes.Status200OK); // S115 / TASK-11501

        // ═══════════════════════════════════════════
        // 2. PUT /api/admin/employees/{employeeId}/employment-start-date
        //
        // S60 origin; S136 / TASK-13604 lifecycle upgrade (ADR-040 D1/D3). RBAC: HROrAbove +
        // the terminated-INCLUSIVE OrgScopeValidator path — a DELIBERATE S70 R9c allowlist
        // EXTENSION ruled by ADR-040 D3 (role, not is_active, governs who may edit a
        // deactivated leaver's employment record). Without it the D1 re-hire guard below is
        // UNREACHABLE: a CLOSED spell (end date set and passed) means the Step-A poller already
        // flipped is_active=FALSE, and the old active-only validator/read/UPDATE trio
        // dead-ended at 403/404 before the guard could ever run. Admin-strict If-Match
        // (ADR-019 D2). employmentStartDate may be null (clears an unknown start date).
        //
        // Atomic, in the S136 lock-regime order — all ONE tx (ADR-018 D3):
        //   (1) EmployeeConsumptionLock FIRST (ADR-032 D4; window edits and the registration
        //       writers serialize on this key, making the in-tx guards below authoritative)
        //   (2) FOR-UPDATE terminated-inclusive re-read (canonical snapshot)
        //   (3) version check
        //   (4) ADR-040 D1 re-hire guard (409, settlement-INDEPENDENT)
        //   (5) cross-field inverted-window check (422)
        //   (6) ADR-040 D3 strand guard (409, same conn/tx)
        //   (7) SetEmploymentStartDateIncludingTerminatedAsync (bumps users.version)
        //   (8) users_audit UPDATED row
        // Every guard runs strictly BEFORE the repository UPDATE — a refusal mutates nothing.
        //
        // Error mapping:
        //   • 428 — missing / malformed If-Match (EtagHeaderHelper admin-strict)
        //   • 422 — cross-field: proposed start after the recorded (UNPASSED) end date
        //   • 412 — version mismatch (stale If-Match), on ACTIVE and DEACTIVATED rows alike
        //   • 409 — ADR-040 D1 re-hire signature, or the ADR-040 D3 strand guard
        //   • 404 — user_id does not exist at all (a deactivated leaver is NOT a 404 here)
        //   • 403 — self-target, or terminated-inclusive OrgScopeValidator denial
        // ═══════════════════════════════════════════
        app.MapPut("/api/admin/employees/{employeeId}/employment-start-date", async (
            string employeeId,
            SetEmploymentStartDateRequest body,
            UserRepository userRepo,
            DbConnectionFactory connectionFactory,
            OrgScopeValidator scopeValidator,
            TimeProvider timeProvider,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // S136 / TASK-13604 (Reviewer-W3, extending the S70 Step-7a W1 precedent): self-target
            // exclusion, BEFORE any DB work and for ALL actors (active or terminated;
            // deterministic, fail-closed) — symmetric with the end-date PUT below. This PUT is now
            // an INACTIVE-row write surface too (terminated-inclusive), so a lifecycle-deactivated
            // HR actor's still-valid JWT (8h lifetime, no revocation) could otherwise rewrite its
            // OWN employment-start history — the falsifiable-history vector: no reactivation lever
            // exists here, but the completed employment's RECORD is the asset (Auditability).
            // A second administrator performs legitimate self-corrections; the GET above stays
            // self-readable.
            if (string.Equals(actor.ActorId, employeeId, StringComparison.Ordinal))
                return Results.Json(new
                {
                    error = "Access denied",
                    reason = "Own employment start date cannot be modified; a second administrator must perform this change",
                }, statusCode: 403);

            // S136 / TASK-13604 — R9c allowlist EXTENSION (ADR-040 D3): terminated-INCLUSIVE
            // validator replaces the S60 active-only ValidateEmployeeAccessAsync so HR can correct
            // a deactivated leaver's start date (and the D1 re-hire guard is reachable at all).
            // S76 B1: HROrAbove policy → LocalHR per-scope floor, unchanged by the swap.
            var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessIncludingTerminatedAsync(actor, employeeId, StatsTidRoles.LocalHR, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            // Admin-strict If-Match — 428 if missing / malformed / If-None-Match: *.
            if (!EtagHeaderHelper.TryParseIfMatch(
                    context.Request, out var expectedVersion, out var headerError))
                return Results.Json(new { error = headerError }, statusCode: 428);

            var actorId = actor.ActorId ?? "unknown";
            var actorRole = actor.ActorRole ?? "unknown";

            await using var conn = connectionFactory.Create();
            await conn.OpenAsync(ct);
            // S136 Step-5a (Reviewer NOTE 2) — PIN ReadCommitted EXPLICITLY (PAT-015): this tx's
            // first statement is the blocking advisory-lock acquire below, and the in-lock
            // FOR-UPDATE re-read + guards depend on post-lock snapshots. ReadCommitted is what
            // the driver default resolves to today, but the in-tx employment-window machinery
            // (IEmploymentWindowResolverInTx and the strand check) documents ReadCommitted as
            // its isolation prerequisite — make it explicit, never a default.
            await using var tx = await conn.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct);
            try
            {
                // (1) The S136 lock regime: the ADR-032 D4 employee advisory lock FIRST, before
                // any read or guard, held to commit (mirrors the end-date PUT's R12 order). The
                // registration writers acquire the SAME key in their write tx, so a concurrent
                // registration-vs-window-edit pair cannot interleave into out-of-window data —
                // the strand guard below is authoritative because it reads under this lock.
                await EmployeeConsumptionLock.AcquireAsync(conn, tx, employeeId, ct);

                // (2) FOR-UPDATE terminated-inclusive re-read — canonical snapshot for the
                // If-Match precondition, the guards' other-field inputs (employment_end_date)
                // AND the users_audit previous_data JSONB (closes the stale-snapshot race).
                // R9c allowlist extension: the active-only read would 404 a deactivated leaver.
                var lockedHit = await userRepo.GetByIdWithVersionIncludingTerminatedAsync(conn, tx, employeeId, ct);
                if (lockedHit is null)
                {
                    await tx.RollbackAsync(ct);
                    return Results.NotFound(new { error = "Employee not found" });
                }
                var (lockedUser, lockedVersion) = lockedHit.Value;

                if (lockedVersion != expectedVersion)
                {
                    await tx.RollbackAsync(ct);
                    return Results.Json(new
                    {
                        error = "Concurrency precondition failed",
                        expectedVersion,
                        actualVersion = lockedVersion,
                    }, statusCode: 412);
                }

                // (4) ADR-040 D1 re-hire guard — checked BEFORE the cross-field 422 on purpose:
                // a closed-spell violation satisfies BOTH predicates (start after end is also an
                // inverted window), and the 409 must win or this guard is dead code. The
                // signature is settlement-INDEPENDENT (no vacation_settlements query): moving
                // the start past a CLOSED spell's recorded end is the one edit that overwrites a
                // completed employment's record (Auditability), refused until the spells
                // increment ships. Same-spell corrections (start ≤ recorded end) fall through.
                // The 409 body MAY name dates — this is the HR-scoped admin surface; the ADR-040
                // D3 date-free rule binds only employee-facing registration errors.
                var copenhagenToday = CopenhagenToday(timeProvider);
                if (IsRehireStartDateMove(body.EmploymentStartDate, lockedUser.EmploymentEndDate, copenhagenToday))
                {
                    await tx.RollbackAsync(ct);
                    return Results.Json(new
                    {
                        error = "The proposed employment start date lies after the recorded end date of a " +
                                "closed employment spell; the change is rejected fail-closed regardless of " +
                                "settlement state (ADR-040 D1 re-hire guard) — it would overwrite the " +
                                "completed employment's record.",
                        providedEmploymentStartDate = body.EmploymentStartDate,
                        recordedEmploymentEndDate = lockedUser.EmploymentEndDate,
                        hint = "Same-spell corrections (a start date on or before the recorded end date) " +
                               "remain legal here. A genuine re-hire is a SECOND employment spell — a " +
                               "named deferred increment of the ADR-040 program, not an edit of the " +
                               "closed spell's boundaries.",
                    }, statusCode: 409);
                }

                // (5) Cross-field inverted-window check (ADR-040 D1 geometry): a spell is
                // [start, end] with end INCLUSIVE, so start > end is an inverted (empty) window.
                // Reached only when the recorded end has NOT passed (open spell — the closed
                // case 409'd above). NULL on either side = unbounded (D2), passes through;
                // start == end is a legal one-day spell.
                if (IsInvertedWindow(body.EmploymentStartDate, lockedUser.EmploymentEndDate))
                {
                    await tx.RollbackAsync(ct);
                    return Results.UnprocessableEntity(new
                    {
                        error = "Employment start date must not be after the recorded employment end date.",
                        providedEmploymentStartDate = body.EmploymentStartDate,
                        recordedEmploymentEndDate = lockedUser.EmploymentEndDate,
                    });
                }

                // (6) ADR-040 D3 strand guard — in-tx on the SAME (conn, tx), under the lock
                // from (1): the proposed window [new start, recorded end] must not orphan
                // EXISTING registered data. See CheckStrandedRegistrationsAsync.
                var strandRefusal = await CheckStrandedRegistrationsAsync(
                    conn, tx, employeeId,
                    proposedStart: body.EmploymentStartDate,
                    proposedEnd: lockedUser.EmploymentEndDate, ct);
                if (strandRefusal is not null)
                {
                    await tx.RollbackAsync(ct);
                    return strandRefusal;
                }

                // (7) Guarded versioned write — the S136 R9c-extension repository method (an
                // active-only write shape would false-404 a deactivated leaver; the S60
                // SetEmploymentStartDateAsync it replaced was DELETED in the S136 Step-5a
                // fix-forward once this variant became the only production writer).
                long newVersion;
                try
                {
                    newVersion = await userRepo.SetEmploymentStartDateIncludingTerminatedAsync(
                        conn, tx, employeeId, body.EmploymentStartDate, expectedVersion, ct);
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
                    await tx.RollbackAsync(ct);
                    return Results.NotFound(new { error = "Employee not found" });
                }

                // (8) users_audit UPDATED row — full-row JSONB snapshot captures
                // employment_start_date (init.sql: users_audit stores whole-row
                // previous/new data; no schema change needed). password_hash deliberately
                // excluded — mirrors the birth-date PUT audit row.
                var previousData = JsonSerializer.Serialize(new { employmentStartDate = lockedUser.EmploymentStartDate });
                var newData = JsonSerializer.Serialize(new { employmentStartDate = body.EmploymentStartDate });
                await using (var auditCmd = new NpgsqlCommand(
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
                    """, conn, tx))
                {
                    auditCmd.Parameters.AddWithValue("userId", employeeId);
                    auditCmd.Parameters.AddWithValue("previousData", previousData);
                    auditCmd.Parameters.AddWithValue("newData", newData);
                    auditCmd.Parameters.AddWithValue("versionBefore", lockedVersion);
                    auditCmd.Parameters.AddWithValue("versionAfter", newVersion);
                    auditCmd.Parameters.AddWithValue("actorId", actorId);
                    auditCmd.Parameters.AddWithValue("actorRole", actorRole);
                    await auditCmd.ExecuteNonQueryAsync(ct);
                }

                await tx.CommitAsync(ct);

                context.Response.Headers.ETag = $"\"{newVersion}\"";
                // S115 / TASK-11501 — named record (BYTE-IDENTICAL wire JSON; the GET's shape).
                return Results.Ok(new EmploymentStartDateResponse(
                    EmployeeId: employeeId,
                    EmploymentStartDate: body.EmploymentStartDate,
                    Version: newVersion));
            }
            catch
            {
                if (tx.Connection is not null)
                    await tx.RollbackAsync(ct);
                throw;
            }
        }).RequireAuthorization("HROrAbove")
        .Produces<EmploymentStartDateResponse>(StatusCodes.Status200OK); // S115 / TASK-11501

        // ═══════════════════════════════════════════
        // 3. GET /api/admin/employees/{employeeId}/employment-end-date
        //
        // S70 / TASK-7002 / ADR-033 slice 3a (SPRINT-70 R1/R9). RBAC: HROrAbove + the
        // terminated-INCLUSIVE OrgScopeValidator path (R9c allowlist surface #1) — an HR
        // operator must be able to fetch a DEACTIVATED leaver's current end date + a coherent
        // If-Match token before correcting or clearing it (R1(c) is the only reactivation
        // path for lifecycle-deactivated leavers). ETag stamped from users.version.
        // employment_end_date never appears in any Employee-facing DTO / JWT / export.
        // ═══════════════════════════════════════════
        app.MapGet("/api/admin/employees/{employeeId}/employment-end-date", async (
            string employeeId,
            UserRepository userRepo,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // S76 B1 fix-forward (cycle 2): LocalHR per-scope floor — the sensitive end-date READ
            // (employment_end_date never appears in any Employee-facing DTO) must not be served to
            // a mixed HR@A + Leader@B JWT for an ACTIVE B employee via the Leader scope.
            var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessIncludingTerminatedAsync(actor, employeeId, StatsTidRoles.LocalHR, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            var hit = await userRepo.GetByIdWithVersionIncludingTerminatedAsync(employeeId, ct);
            if (hit is null)
                return Results.NotFound(new { error = "Employee not found" });
            var (user, version) = hit.Value;

            context.Response.Headers.ETag = $"\"{version}\"";
            // S115 / TASK-11501 — named record (BYTE-IDENTICAL wire JSON).
            return Results.Ok(new EmploymentEndDateResponse(
                EmployeeId: user.UserId,
                EmploymentEndDate: user.EmploymentEndDate,
                EndDateDeactivated: user.EndDateDeactivated,
                IsActive: user.IsActive,
                Version: version));
        }).RequireAuthorization("HROrAbove")
        .Produces<EmploymentEndDateResponse>(StatusCodes.Status200OK); // S115 / TASK-11501

        // ═══════════════════════════════════════════
        // 4. PUT /api/admin/employees/{employeeId}/employment-end-date
        //
        // S70 / TASK-7002 / ADR-033 slice 3a (S71 / TASK-7102 refactor) — set / clear / correct
        // users.employment_end_date with the R1 deactivation lifecycle, ALL in ONE atomic tx
        // (ADR-018 D3), in the R12 order: ADR-032 D4 employee advisory lock FIRST → FOR-UPDATE
        // terminated-inclusive re-read → every guard re-evaluated in-lock (If-Match + the
        // R7a/R13 no-active-settlement span check) → the SHARED EmploymentEndDateLifecycleWriter
        // (SPRINT-71 R4 ONE-implementation: guarded versioned write → R1(e) side effects → R10
        // event + ADR-026 audit + users_audit) → commit. The S70 inline lifecycle-write
        // choreography was DELETED here — the writer is the single implementation, consumed by
        // both this PUT and the slice-3b reversal service's subsumed correction.
        // The advisory lock (pg_advisory_xact_lock(hashtext('employee-' || id)) — the SAME key
        // VacationSettlementService.SettleAsync / the reconcile retrofit / Step A / the reversal
        // service hold) is what serializes this endpoint against the settlement path;
        // `users FOR UPDATE` alone would not (the settlement pass does not row-lock users).
        //
        // R1 decision table: see EmploymentEndDateLifecycleWriter.ComputeEndDateLifecycle (the
        // CANONICAL host of the pure decision; this class's same-named member delegates to it
        // for the S70 unit suites). R1(f) DELIBERATE coherence point: the admin general user PUT
        // filters is_active=TRUE (AdminEndpoints ~L1020; UserRepository.cs:94-97 soft-delete
        // semantic), so it can neither edit nor reactivate a deactivated user — R1(c) on THIS
        // endpoint is the ONLY reactivation path for lifecycle-deactivated leavers. By design.
        //
        // R7a correction guard, R13 RANGE-WIDENED (SPRINT-71): the change is REJECTED 409 when
        // an active (non-REVERSED) vacation_settlements row of ANY type/trigger exists for ANY
        // ferieår in the FULL span [min(ferieår(old), ferieår(new)) .. max(...)] — a backward or
        // forward correction crossing an INTERMEDIATE settled ferieår no longer bypasses the
        // guard. The 409 carries a machine-readable reversal pointer (the slice-3b reversal
        // endpoint + every blocking row's identity/sequence/version) so an operator/UI can
        // route the correction through reverse-then-re-settle. This PUT NEVER silently reverses.
        //
        // Error mapping:
        //   • 428 — missing / malformed If-Match (EtagHeaderHelper admin-strict)
        //   • 422 — S136 cross-field: proposed end before the recorded start (inverted window)
        //   • 412 — version mismatch (stale If-Match), on ACTIVE and DEACTIVATED rows alike
        //   • 409 — R7a/R13 active-settlement conflict (with the reversal pointer), or the
        //           S136 / ADR-040 D3 strand guard (with the stranded-month pointer list)
        //   • 404 — user_id does not exist at all (a deactivated leaver is NOT a 404 here)
        //   • 403 — terminated-inclusive OrgScopeValidator denial
        // ═══════════════════════════════════════════
        app.MapPut("/api/admin/employees/{employeeId}/employment-end-date", async (
            string employeeId,
            SetEmploymentEndDateRequest body,
            UserRepository userRepo,
            DbConnectionFactory connectionFactory,
            OrgScopeValidator scopeValidator,
            EmploymentEndDateLifecycleWriter lifecycleWriter,
            TimeProvider timeProvider,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // S70 Step-7a W1 (internal Reviewer) — self-target exclusion, BEFORE any DB work and
            // for ALL actors (active or terminated; deterministic, fail-closed). The end-date PUT
            // is the first INACTIVE-row write surface: without this, a lifecycle-deactivated HR
            // actor's still-valid JWT (8h lifetime, no revocation) could PUT its OWN
            // employmentEndDate: null and permanently self-reinstate via the R1(c) reactivation.
            // A second administrator performs legitimate self-departures; the GET above stays
            // self-readable.
            if (string.Equals(actor.ActorId, employeeId, StringComparison.Ordinal))
                return Results.Json(new
                {
                    error = "Access denied",
                    reason = "Own employment end date cannot be modified; a second administrator must perform this change",
                }, statusCode: 403);

            // R9c allowlist surface — terminated-INCLUSIVE validator (HROrAbove + subtree
            // binding; the shared ValidateEmployeeAccessAsync would 403 a deactivated leaver).
            // S76 B1 fix-forward (cycle 2): LocalHR per-scope floor — the end-date WRITE (the R1
            // lifecycle/reactivation surface) must not be reachable by a mixed HR@A + Leader@B JWT
            // for an ACTIVE B employee via the Leader scope.
            var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessIncludingTerminatedAsync(actor, employeeId, StatsTidRoles.LocalHR, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            // Admin-strict If-Match — 428 if missing / malformed / If-None-Match: *.
            if (!EtagHeaderHelper.TryParseIfMatch(
                    context.Request, out var expectedVersion, out var headerError))
                return Results.Json(new { error = headerError }, statusCode: 428);

            var actorId = actor.ActorId ?? "unknown";
            var actorRole = actor.ActorRole ?? "unknown";

            await using var conn = connectionFactory.Create();
            await conn.OpenAsync(ct);
            // S136 Step-5a (Reviewer NOTE 2) — PIN ReadCommitted EXPLICITLY (PAT-015): same
            // reasoning as the start-date PUT above — the blocking advisory-lock acquire is this
            // tx's first statement and every in-lock guard needs a post-lock snapshot; the in-tx
            // window machinery documents ReadCommitted as its prerequisite. Explicit, not default.
            await using var tx = await conn.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct);
            try
            {
                // (1) R12 — the ADR-032 D4 employee advisory lock FIRST, before any read or
                // guard, held to commit. Serializes this mutation against Step A (flip), Step B
                // (settle), the manual resolve and the reconcile-payout writers on the SAME key.
                await EmployeeConsumptionLock.AcquireAsync(conn, tx, employeeId, ct);

                // (2) FOR-UPDATE terminated-inclusive re-read — the 404/412 pre-checks and the
                // R7a/R13 guard's old-end-date input (closes the stale-snapshot race). The shared
                // writer re-reads the SAME row in the same tx for its own canonical snapshot —
                // idempotent under the row lock this read already took.
                var lockedHit = await userRepo.GetByIdWithVersionIncludingTerminatedAsync(conn, tx, employeeId, ct);
                if (lockedHit is null)
                {
                    await tx.RollbackAsync(ct);
                    return Results.NotFound(new { error = "Employee not found" });
                }
                var (lockedUser, lockedVersion) = lockedHit.Value;

                if (lockedVersion != expectedVersion)
                {
                    await tx.RollbackAsync(ct);
                    return Results.Json(new
                    {
                        error = "Concurrency precondition failed",
                        expectedVersion,
                        actualVersion = lockedVersion,
                    }, statusCode: 412);
                }

                // (2b) S136 / TASK-13604 cross-field inverted-window check (ADR-040 D1
                // geometry): a spell is [start, end] with end INCLUSIVE, so a proposed end
                // BEFORE the FOR-UPDATE-locked row's recorded start is an inverted (empty)
                // window — 422 before any settlement/strand query. NULL on either side =
                // unbounded (D2), passes through; end == start is a legal one-day spell. The
                // body MAY name dates (HR-scoped admin surface; the ADR-040 D3 date-free rule
                // binds only employee-facing registration errors).
                if (IsInvertedWindow(lockedUser.EmploymentStartDate, body.EmploymentEndDate))
                {
                    await tx.RollbackAsync(ct);
                    return Results.UnprocessableEntity(new
                    {
                        error = "Employment end date must not be before the recorded employment start date.",
                        providedEmploymentEndDate = body.EmploymentEndDate,
                        recordedEmploymentStartDate = lockedUser.EmploymentStartDate,
                    });
                }

                // (3) R7a guard, R13 RANGE-WIDENED (SPRINT-71), re-evaluated IN-LOCK (R12): any
                // active (non-REVERSED) settlement row — ANY entitlement type, ANY trigger
                // (fail-closed) — for ANY ferieår in the FULL [min..max] span rejects the change.
                // ALL blockers are fetched (not just the first) so the 409 can carry the complete
                // machine-readable reversal pointer (R7a-409 contract, SPRINT-71).
                var affectedYears = AffectedFerieaarSpan(lockedUser.EmploymentEndDate, body.EmploymentEndDate);
                if (affectedYears.Length > 0)
                {
                    var blockers = new List<(string Type, int Year, int Sequence, string State, long Version)>();
                    await using (var guardCmd = new NpgsqlCommand(
                        """
                        SELECT entitlement_type, entitlement_year, sequence, settlement_state, version
                        FROM vacation_settlements
                        WHERE employee_id = @employeeId
                          AND entitlement_year = ANY(@years)
                          AND settlement_state <> 'REVERSED'
                        ORDER BY entitlement_year, entitlement_type, sequence
                        """, conn, tx))
                    {
                        guardCmd.Parameters.AddWithValue("employeeId", employeeId);
                        guardCmd.Parameters.AddWithValue("years", affectedYears);
                        await using var guardReader = await guardCmd.ExecuteReaderAsync(ct);
                        while (await guardReader.ReadAsync(ct))
                        {
                            blockers.Add((guardReader.GetString(0), guardReader.GetInt32(1),
                                guardReader.GetInt32(2), guardReader.GetString(3), guardReader.GetInt64(4)));
                        }
                    }
                    if (blockers.Count > 0)
                    {
                        var first = blockers[0];
                        await tx.RollbackAsync(ct);
                        return Results.Json(new
                        {
                            error = "An active settlement exists for a ferieår this end-date change affects; " +
                                    "the change is rejected fail-closed (SPRINT-70 R7a).",
                            conflictingSettlement = new
                            {
                                entitlementType = first.Type,
                                entitlementYear = first.Year,
                                settlementState = first.State,
                            },
                            // SPRINT-71 R7a-409 reversal pointer: EVERY blocking row's identity +
                            // settlement-row sequence + version — exactly what the reversal
                            // endpoint's body (expectedSettlementSequence) and If-Match need.
                            blockingSettlements = blockers.Select(b => new
                            {
                                entitlementType = b.Type,
                                entitlementYear = b.Year,
                                sequence = b.Sequence,
                                settlementState = b.State,
                                version = b.Version,
                            }).ToArray(),
                            affectedEntitlementYears = affectedYears,
                            reversalEndpoint = $"/api/admin/employees/{employeeId}/settlement-reversal",
                            hint = "Route the correction through the slice-3b reversal endpoint: POST reversalEndpoint " +
                                   "with a blocking row's entitlementType/entitlementYear/expectedSettlementSequence " +
                                   "(If-Match: its version) — reverse-then-re-settle subsumes this end-date " +
                                   "correction; the explicit bare-reversal mode parks the tuple instead.",
                        }, statusCode: 409);
                    }
                }

                // (3b) S136 / TASK-13604 — the ADR-040 D3 strand guard, in-tx on the SAME
                // (conn, tx) under the R12 advisory lock from (1) (authoritative: the
                // registration writers serialize on the same key): the proposed window
                // [recorded start, new end] must not orphan EXISTING registered data. Runs
                // AFTER the R7a/R13 settlement guard — a settlement conflict keeps its pinned
                // 409 shape — and strictly BEFORE the lifecycle writer's side effects. See
                // CheckStrandedRegistrationsAsync.
                var strandRefusal = await CheckStrandedRegistrationsAsync(
                    conn, tx, employeeId,
                    proposedStart: lockedUser.EmploymentStartDate,
                    proposedEnd: body.EmploymentEndDate, ct);
                if (strandRefusal is not null)
                {
                    await tx.RollbackAsync(ct);
                    return strandRefusal;
                }

                // (4)–(8) — the SHARED lifecycle writer (SPRINT-71 R4 one-implementation): the
                // R1 decision off the Copenhagen business date, the guarded versioned write, the
                // R1(e) ReportingLineManagerDeactivated side effects, the R10 event + ADR-026
                // audit-projection row and the users_audit UPDATED row — byte-identical to the
                // S70 inline choreography this delegation replaced (pinned by the S70 lifecycle
                // suite + EndDateLifecycleWriterEffectTests).
                EmploymentEndDateLifecycleResult lifecycle;
                try
                {
                    lifecycle = await lifecycleWriter.ApplyAsync(
                        conn, tx, employeeId, body.EmploymentEndDate, expectedVersion,
                        actorId, actorRole, actor.OrgId, actor.CorrelationId,
                        CopenhagenToday(timeProvider), ct);
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
                    await tx.RollbackAsync(ct);
                    return Results.NotFound(new { error = "Employee not found" });
                }

                await tx.CommitAsync(ct);

                context.Response.Headers.ETag = $"\"{lifecycle.VersionAfter}\"";
                // S115 / TASK-11501 — named record (BYTE-IDENTICAL wire JSON; the GET's shape).
                // The 409 settlement-conflict body stays UNTYPED (error-shape typing is explicitly
                // out of the Pass-2 scope).
                return Results.Ok(new EmploymentEndDateResponse(
                    EmployeeId: employeeId,
                    EmploymentEndDate: body.EmploymentEndDate,
                    EndDateDeactivated: lifecycle.NewEndDateDeactivated,
                    IsActive: lifecycle.NewIsActive,
                    Version: lifecycle.VersionAfter));
            }
            catch
            {
                if (tx.Connection is not null)
                    await tx.RollbackAsync(ct);
                throw;
            }
        }).RequireAuthorization("HROrAbove")
        .Produces<EmploymentEndDateResponse>(StatusCodes.Status200OK); // S115 / TASK-11501

        return app;
    }

    // ── S70 / TASK-7002 — pure lifecycle helpers (unit-tested; no I/O) ──

    /// <summary>
    /// The R1 deactivation-lifecycle decision — since the S71 / TASK-7102 refactor a THIN
    /// DELEGATION to the CANONICAL implementation,
    /// <see cref="EmploymentEndDateLifecycleWriter.ComputeEndDateLifecycle"/> (SPRINT-71 R4
    /// one-implementation — the S70 transitional duplicate died here). The public symbol is
    /// retained because the S70 unit suite (<c>EmploymentEndDateLifecycleLogicTests</c>) and the
    /// 7104 parity suite (<c>EndDateLifecycleWriterParityTests</c>) reference it; parity is now
    /// by-construction. See the writer's doc for the R1(a)–(d) decision table.
    /// </summary>
    public static (bool IsActive, bool EndDateDeactivated) ComputeEndDateLifecycle(
        DateOnly? newEndDate, bool oldIsActive, bool oldEndDateDeactivated, DateOnly copenhagenToday)
        => EmploymentEndDateLifecycleWriter.ComputeEndDateLifecycle(
            newEndDate, oldIsActive, oldEndDateDeactivated, copenhagenToday);

    /// <summary>
    /// R6 ferieår resolution, executable: the entitlement year containing <paramref name="date"/>
    /// (VACATION <c>reset_month</c> = 9, uniform by DB CHECK per S68 B1 — the ferieår starting
    /// 1 Sep of <c>entitlementYear</c>).
    /// </summary>
    public static int FerieaarOf(DateOnly date) => date.Month >= 9 ? date.Year : date.Year - 1;

    /// <summary>
    /// The S70 R7a affected-ferieår PAIR: ferieår(old end date) and ferieår(new end date), each
    /// when non-null, de-duplicated. Empty when both dates are null (clearing a never-set date).
    /// SUPERSEDED for the PUT's guard by <see cref="AffectedFerieaarSpan"/> (SPRINT-71 R13 — the
    /// pair misses INTERMEDIATE ferieårs a multi-year correction crosses); retained because the
    /// S70 unit suite pins it and it documents the pre-R13 semantics.
    /// </summary>
    public static int[] AffectedFerieaar(DateOnly? oldEndDate, DateOnly? newEndDate)
    {
        var years = new HashSet<int>();
        if (oldEndDate is { } o) years.Add(FerieaarOf(o));
        if (newEndDate is { } n) years.Add(FerieaarOf(n));
        return years.ToArray();
    }

    /// <summary>
    /// SPRINT-71 R13 — the RANGE-WIDENED affected-ferieår set: the FULL inclusive span
    /// <c>[min(ferieår(old), ferieår(new)) .. max(...)]</c>, so a forward OR backward end-date
    /// correction crossing an INTERMEDIATE ferieår holding an active settlement row 409s instead
    /// of silently bypassing the guard. A null date contributes no pivot (single-pivot span = a
    /// single year — identical to the S70 pair semantics for set/clear); both null ⇒ empty.
    /// Ascending order. PURE; the in-tx twin lives in
    /// <c>SettlementReversalService.GetOtherActiveRowsInCorrectionSpanAsync</c> (the B2 guard).
    /// </summary>
    public static int[] AffectedFerieaarSpan(DateOnly? oldEndDate, DateOnly? newEndDate)
    {
        var pivots = new List<int>(2);
        if (oldEndDate is { } o) pivots.Add(FerieaarOf(o));
        if (newEndDate is { } n) pivots.Add(FerieaarOf(n));
        if (pivots.Count == 0) return [];
        var low = pivots.Min();
        var high = pivots.Max();
        return Enumerable.Range(low, high - low + 1).ToArray();
    }

    // ── S136 / TASK-13604 — pure employment-window guard predicates (ADR-040 D1; unit-tested,
    // no I/O). Both PUTs evaluate them against the OTHER field from the FOR-UPDATE-locked row. ──

    /// <summary>
    /// S136 / TASK-13604 — the ADR-040 D1 re-hire signature, PURE: moving
    /// <c>employment_start_date</c> to a date strictly AFTER the recorded
    /// <c>employment_end_date</c> of a CLOSED spell — end date set and PASSED, where "passed"
    /// means <paramref name="copenhagenToday"/> is STRICTLY after it (the end date is the LAST
    /// day employed; same definition as the R1 lifecycle's flip). This is the one edit that
    /// unambiguously writes a second employment over the record of the first, so the endpoint
    /// refuses it (409) <b>settlement-independently</b> — deliberately unlike the R7a/R13 guard,
    /// which keys on <c>vacation_settlements</c> — until the ADR-040 spells increment ships
    /// (Auditability: a completed employment's record is never overwritten). Same-spell
    /// corrections (new start ≤ recorded end) return false and stay legal; NULL on either side
    /// is unbounded (D2) — never a re-hire signature. An UNPASSED end with a start after it is
    /// an inverted window instead — <see cref="IsInvertedWindow"/>'s 422, evaluated AFTER this
    /// predicate at the call site (a closed-spell violation satisfies both; the 409 must win).
    /// </summary>
    public static bool IsRehireStartDateMove(
        DateOnly? newStartDate, DateOnly? recordedEndDate, DateOnly copenhagenToday)
        => newStartDate is { } start && recordedEndDate is { } end
           && start > end && copenhagenToday > end;

    /// <summary>
    /// S136 / TASK-13604 — cross-field window validation, PURE (ADR-040 D1 geometry): an
    /// employment spell is <c>[start, end]</c> with end INCLUSIVE, so <c>end &lt; start</c> is
    /// an inverted (empty) window — refused 422 on BOTH employment-date PUTs (before this
    /// sprint, nothing anywhere checked the two dates against each other).
    /// <c>start == end</c> is a legal one-day spell; NULL = unbounded (D2), never inverted.
    /// </summary>
    public static bool IsInvertedWindow(DateOnly? start, DateOnly? end)
        => start is { } s && end is { } e && e < s;

    /// <summary>
    /// S136 / TASK-13604 — the ADR-040 D3 strand guard's in-tx check + 409 shaping, SHARED by
    /// both employment-date PUTs so the predicate and the response contract cannot drift.
    /// Returns the 409 result when any <c>time_entries_projection</c>,
    /// <c>absences_projection</c> or <c>work_time_projection</c> row for
    /// <paramref name="employeeId"/> is dated OUTSIDE the proposed window
    /// [<paramref name="proposedStart"/>, <paramref name="proposedEnd"/>] (end inclusive per
    /// D1), else null (the edit may proceed).
    ///
    /// <para><b>Where the query lives (S136 Step-5a fix-forward).</b> The strand QUERY itself
    /// was lifted into the shared Infrastructure
    /// <see cref="StatsTid.Infrastructure.EmploymentWindowStrandCheck"/> — Codex BLOCKER 2
    /// found a SECOND end-date writer (the settlement reversal's subsumed correction,
    /// <c>SettlementReversalService</c>) writing through the lifecycle writer with no strand
    /// check at all; both surfaces now call the one query (owner-ruled cross-domain
    /// Backend+Infrastructure change). The Reviewer-W1 fix added the third
    /// <c>work_time_projection</c> arm there: the write side gates all three registration
    /// arrays, so the edit side sees all three. This method keeps ONLY the HTTP 409
    /// shaping.</para>
    ///
    /// <para><b>Predicate scope.</b> The WHOLE proposed window is checked, not just the edited
    /// side — fail-closed: a window edit never commits while ANY registered data sits outside
    /// it, keeping D6's premise ("an approvable month's content is window-clean") airtight in
    /// both directions. Approved-month status is deliberately NOT queried separately: any
    /// stranded registration suffices to refuse (an approved month's content is itself
    /// registrations, so it is covered by construction). NULL bounds are unbounded (D2) —
    /// both-NULL skips the query entirely, which is why reactivation (clearing the end date)
    /// and D2's no-backfill rollout cost nothing here.</para>
    ///
    /// <para><b>Why the caller's (conn, tx).</b> Both PUTs hold the ADR-032 D4 employee
    /// advisory lock on that tx, and the registration writers acquire the SAME key — a
    /// self-managed connection would read OUTSIDE the lock and miss a registration committed
    /// while this edit was blocked on it (the S136 lock regime / the TimeEndpoints race class).
    /// Never "simplify" this to a pooled read.</para>
    ///
    /// <para><b>Response contract.</b> Mirrors the R7a/R13 settlement-span 409 (the sibling
    /// guard above): error prose + a structured pointer array (<c>strandedMonths</c>, ascending
    /// <c>YYYY-MM</c> with per-type counts) + a <c>hint</c>. Naming dates/months is deliberate —
    /// this is the HR-scoped admin surface; the ADR-040 D3 date-free rule binds only
    /// employee-facing registration errors.</para>
    /// </summary>
    private static async Task<IResult?> CheckStrandedRegistrationsAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string employeeId,
        DateOnly? proposedStart, DateOnly? proposedEnd, CancellationToken ct)
    {
        var strandedMonths = await EmploymentWindowStrandCheck.QueryStrandedMonthsAsync(
            conn, tx, employeeId, proposedStart, proposedEnd, ct);

        if (strandedMonths.Count == 0)
            return null;

        return Results.Json(new
        {
            error = "Existing registrations would fall outside the proposed employment window; " +
                    "the change is rejected fail-closed (ADR-040 D3 strand guard).",
            proposedEmploymentStartDate = proposedStart,
            proposedEmploymentEndDate = proposedEnd,
            strandedMonths = strandedMonths.Select(m => new
            {
                month = m.Month,
                timeEntryCount = m.TimeEntryCount,
                absenceCount = m.AbsenceCount,
                workTimeCount = m.WorkTimeCount,
            }).ToArray(),
            hint = "Correct or remove the out-of-window registrations in the listed months first " +
                   "(or choose a window that covers them) and retry — the employment window is " +
                   "never moved past existing registered data (ADR-040 D3: if the window is " +
                   "wrong, HR corrects the window, not the data past it; if the data is wrong, " +
                   "correct the data first).",
        }, statusCode: 409);
    }

    // ── Europe/Copenhagen business-date helper (ADR-033 D3 boundary-timezone; the injected
    // TimeProvider is the test seam, PAT-008). S132 TASK-132-3b (QUAL-005): the DST-correct zone
    // resolution + conversion now lives once in SharedKernel (CopenhagenBusinessDate); this is a
    // thin adapter. ──

    private static DateOnly CopenhagenToday(TimeProvider timeProvider) =>
        CopenhagenBusinessDate.Today(timeProvider);

    // ── Request DTO ──

    /// <summary>PUT employment-start body. <c>EmploymentStartDate</c> may be null
    /// (clear an unknown start date).</summary>
    private sealed record SetEmploymentStartDateRequest
    {
        public DateOnly? EmploymentStartDate { get; init; }
    }

    /// <summary>PUT employment-end body. <c>EmploymentEndDate</c> may be null
    /// (clear — R1(c) provenance-guarded reactivation).</summary>
    private sealed record SetEmploymentEndDateRequest
    {
        public DateOnly? EmploymentEndDate { get; init; }
    }
}
