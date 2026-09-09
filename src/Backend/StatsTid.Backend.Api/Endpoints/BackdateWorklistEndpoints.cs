using StatsTid.Auth;
using StatsTid.Backend.Api.Contracts;
using StatsTid.Backend.Api.Endpoints.Helpers;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Security;
using StatsTid.SharedKernel.Security;

namespace StatsTid.Backend.Api.Endpoints;

/// <summary>
/// S138 / TASK-13803 (ADR-040 D8, Increment 3 — temporal editing): the HR backdate DIAGNOSTIC
/// WORKLIST surface. Two endpoints, both <c>HROrAbove</c> with the LocalHR per-scope floor bound
/// to the target employee (FAIL-001: the policy proves role shape, the validator binds the org).
///
/// <para>
/// <b>What HR sees and why.</b> A backdated profile / agreement-code / employment-category
/// correction (TASK-13801/13802) is recorded truthfully in dated history, but the months already
/// EXPORTED to payroll and the holiday years already SETTLED are now stale. ADR-013 forbids an
/// automatic cascade, so this list tells HR exactly which months to re-plan through the payroll
/// correction path and which years to reverse-then-re-settle (ADR-033 D4/D5), together with the
/// honest derived state: whether the export hash / settlement sequence has ALREADY moved since
/// each trigger (<c>recalculatedSince</c> / <c>reversedSince</c>), and which known limitation
/// blocks the re-plan today (<c>recalcBlockedBy</c> = QUAL-149 for a profile / category split
/// inside the month, QUAL-150 for an agreement-code change inside the month — owner ruling OQ-3
/// (a): OUT this sprint, made VISIBLE here).
/// </para>
///
/// <list type="bullet">
///   <item><description>
///     <b>GET /api/hr/backdate-worklist?employeeId=&amp;open=</b> — WITH <c>employeeId</c>: the
///     terminated-INCLUSIVE scope check (<see cref="OrgScopeValidator.ValidateEmployeeAccessIncludingTerminatedAsync"/>
///     with the LocalHR floor — HR corrects leavers' history too); WITHOUT it: the org-wide
///     listing filtered by the actor's HR-floored accessible-org set
///     (<see cref="OrgScopeValidator.GetAccessibleOrgsAsync"/>, the AuditEndpoints / users-list
///     precedent; null = GlobalAdmin unrestricted, empty = 403). <c>open</c> defaults to true.
///     Typed BARE array of <see cref="BackdateWorklistRow"/>.
///   </description></item>
///   <item><description>
///     <b>POST /api/hr/backdate-worklist/{worklistId}/resolve</b> — body
///     <see cref="ResolveBackdateWorklistRequest"/>; admin-strict If-Match (ADR-019 D2: 428 when
///     missing / malformed, 412 when stale); 404 unknown id; 403 out of scope; 403 when a
///     NON-GlobalAdmin resolves an <c>EXPORTED_MONTH</c> row as <c>RECALCULATED</c> (S140 /
///     TASK-14010, owner ruling OQ-7 (a) — see the in-handler gate); 409 already
///     resolved; 422 bad verb / blank reason. The resolve writes the row, emits
///     <c>BackdateWorklistRowResolved</c> + its ADR-026 row in ONE tx (that pair IS the audit record
///     — no <c>*_audit</c> table by design) and returns 200 with the new ETag.
///   </description></item>
/// </list>
///
/// <para><b>No automatic recalculation anywhere here</b> (ADR-013) — a resolution is HR's recorded
/// verb, the derived flags stay derived.</para>
/// </summary>
public static class BackdateWorklistEndpoints
{
    private const int MaxReasonLength = 1000;

    public static WebApplication MapBackdateWorklistEndpoints(this WebApplication app)
    {
        // ═══════════════════════════════════════════
        // 1. GET /api/hr/backdate-worklist?employeeId=&open=
        // ═══════════════════════════════════════════
        app.MapGet("/api/hr/backdate-worklist", async (
            string? employeeId,
            bool? open,
            HrBackdateWorklistRepository worklistRepo,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();
            var openOnly = open ?? true;

            IReadOnlyList<HrBackdateWorklistRow> rows;
            if (!string.IsNullOrWhiteSpace(employeeId))
            {
                // Per-employee read: terminated-INCLUSIVE with the LocalHR per-scope floor (S76 B1)
                // — a deactivated leaver's stale exported months are exactly what HR must still see.
                var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessIncludingTerminatedAsync(
                    actor, employeeId, StatsTidRoles.LocalHR, ct);
                if (!allowed)
                    return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

                rows = openOnly
                    ? await worklistRepo.GetOpenAsync(employeeId, ct)
                    : await worklistRepo.GetAllAsync(employeeId, ct);
            }
            else
            {
                // Org-wide listing: the actor's HR-floored accessible-org set (the AuditEndpoints /
                // users-list precedent). null = GLOBAL scope clearing the floor → unrestricted;
                // empty = no HR-floored org → 403 (never an empty 200 that hides a scope problem).
                var accessibleOrgIds = await scopeValidator.GetAccessibleOrgsAsync(actor, StatsTidRoles.LocalHR, ct);
                if (accessibleOrgIds is { Count: 0 })
                    return Results.Json(new { error = "Access denied", reason = "No HR-level organisation scope" }, statusCode: 403);

                rows = openOnly
                    ? await worklistRepo.GetOpenForOrgSubtreeAsync(accessibleOrgIds, ct)
                    : await worklistRepo.GetAllForOrgSubtreeAsync(accessibleOrgIds, ct);
            }

            return Results.Ok(rows.Select(ToDto).ToList());
        }).RequireAuthorization("HROrAbove")
        .Produces<IEnumerable<BackdateWorklistRow>>(StatusCodes.Status200OK); // BARE array — NOT an envelope

        // ═══════════════════════════════════════════
        // 2. POST /api/hr/backdate-worklist/{worklistId}/resolve
        // ═══════════════════════════════════════════
        app.MapPost("/api/hr/backdate-worklist/{worklistId:guid}/resolve", async (
            Guid worklistId,
            ResolveBackdateWorklistRequest body,
            HrBackdateWorklistRepository worklistRepo,
            OrgScopeValidator scopeValidator,
            DbConnectionFactory connectionFactory,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // Admin-strict If-Match — 428 if missing / malformed / If-None-Match: * (ADR-019 D2).
            if (!EtagHeaderHelper.TryParseIfMatch(context.Request, out var expectedVersion, out var headerError))
                return Results.Json(new { error = headerError }, statusCode: 428);

            if (body is null || !WorklistResolutions.IsKnown(body.Resolution))
            {
                return Results.UnprocessableEntity(new
                {
                    error = $"resolution must be '{WorklistResolutions.Recalculated}' or '{WorklistResolutions.Dismissed}'.",
                });
            }
            if (string.IsNullOrWhiteSpace(body.Reason) || body.Reason.Length > MaxReasonLength)
            {
                return Results.UnprocessableEntity(new
                {
                    error = $"reason is required (1–{MaxReasonLength} characters).",
                });
            }

            // Pre-read outside the tx to learn the subject employee (the scope check binds to the
            // row's employee, not to anything the caller supplied). 404 before 403 is unavoidable
            // here — the employee is unknown until the row is read; ids are random UUIDs on an
            // HR-only surface, so existence is not a meaningful disclosure.
            var existing = await worklistRepo.GetByIdWithVersionAsync(worklistId, ct);
            if (existing is null)
                return Results.NotFound(new { error = "Worklist row not found" });

            var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessIncludingTerminatedAsync(
                actor, existing.EmployeeId, StatsTidRoles.LocalHR, ct);
            if (!allowed)
                return Results.Json(new { error = "Access denied", reason }, statusCode: 403);

            // ── S140 / TASK-14010, owner ruling OQ-7 (a): RECALCULATED on an EXPORTED_MONTH row
            // is GLOBAL-ADMIN ONLY, enforced HERE and not only on the screen ──────────────────
            //
            // The gate tracks THE REMEDY, not the surface. An EXPORTED_MONTH row is fixed by
            // POST /api/payroll/recalculate on the Payroll host, which is GlobalAdminOnly
            // (Payroll Program.cs — `RequireAuthorization("GlobalAdminOnly")`, ADR-034 D5), so
            // recording "Recalculated" on such a row ASSERTS an act only a Global Admin may
            // perform; an HR user must not be able to close the row by claiming it. A
            // SETTLED_YEAR row is fixed by the settlement REVERSAL, which is HROrAbove
            // (SettlementReversalEndpoints), so any HR-capable actor may mark THAT kind
            // Recalculated. DISMISSED stays open to HR for BOTH kinds — dismissing records a
            // judgement, not a payroll act. The frontend hides the button for the same
            // combination (WorklistList.tsx); that is a courtesy, this is the gate.
            //
            // WHY THIS POSITION. AFTER the row read and the org-scope validation, so a caller who
            // may not see the row still gets the scope 403 first — this refusal can never double
            // as an existence (or kind) oracle for a row outside the caller's scope. BEFORE the
            // already-resolved 409 and before the transaction, so the authorization decision
            // never depends on mutable row state (no state can "unlock" it) and the 409 body's
            // resolution details are not handed to a caller who may not take the action at all.
            // Reading Kind from the pre-read row is safe: `kind` is written once at INSERT and
            // never updated, and any concurrent write to the row bumps `version`, which the in-tx
            // If-Match guard then rejects with a 412.
            if (string.Equals(body.Resolution, WorklistResolutions.Recalculated, StringComparison.Ordinal)
                && string.Equals(existing.Kind, WorklistKinds.ExportedMonth, StringComparison.Ordinal)
                && !IsGlobalAdmin(actor))
            {
                return Results.Json(new
                {
                    error = "Access denied",
                    reason = $"Only GlobalAdmin can resolve a {WorklistKinds.ExportedMonth} row as "
                           + $"{WorklistResolutions.Recalculated} — its remedy is the Global-Admin-only "
                           + "payroll recalculation. HR may resolve it as "
                           + $"{WorklistResolutions.Dismissed}.",
                }, statusCode: 403);
            }

            if (existing.ResolvedAt is not null)
            {
                return Results.Json(new
                {
                    error = "Worklist row is already resolved",
                    resolution = existing.Resolution,
                    resolvedAt = existing.ResolvedAt,
                }, statusCode: 409);
            }

            var actorId = actor.ActorId ?? "unknown";

            await using var conn = connectionFactory.Create();
            await conn.OpenAsync(ct);
            // PAT-015: pin ReadCommitted explicitly — the in-lock FOR UPDATE re-read + version
            // guard depend on the post-lock snapshot.
            await using var tx = await conn.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct);
            try
            {
                WorklistResolveResult result;
                try
                {
                    result = await worklistRepo.ResolveAsync(
                        conn, tx, worklistId, expectedVersion, body.Resolution, body.Reason.Trim(),
                        new WorklistActor(actorId, actor.ActorRole, actor.CorrelationId), ct);
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
                    return Results.NotFound(new { error = "Worklist row not found" });
                }
                catch (BackdateWorklistAlreadyResolvedException)
                {
                    await tx.RollbackAsync(ct);
                    return Results.Json(new { error = "Worklist row is already resolved" }, statusCode: 409);
                }

                await tx.CommitAsync(ct);

                context.Response.Headers.ETag = $"\"{result.NewVersion}\"";
                return Results.Ok(new BackdateWorklistResolveResponse(
                    WorklistId: result.WorklistId,
                    EmployeeId: result.EmployeeId,
                    Resolution: result.Resolution,
                    ResolvedAt: result.ResolvedAt,
                    Version: result.NewVersion));
            }
            catch
            {
                if (tx.Connection is not null)
                    await tx.RollbackAsync(ct);
                throw;
            }
        }).RequireAuthorization("HROrAbove")
        .Accepts<ResolveBackdateWorklistRequest>("application/json")
        .Produces<BackdateWorklistResolveResponse>(StatusCodes.Status200OK);

        return app;
    }

    // ── the in-handler Global-Admin test (S140 / TASK-14010, owner ruling OQ-7 (a)) ──

    /// <summary>
    /// Whether the actor is a Global Admin, decided PURELY from its own claims — and decided THE
    /// SAME WAY the <c>GlobalAdminOnly</c> policy decides it, because the only job of this gate is
    /// to mirror that policy's verdict on THE REMEDY (<c>POST /api/payroll/recalculate</c>).
    ///
    /// <para><b>The PRIMARY ROLE CLAIM, and nothing else.</b> <c>GlobalAdminOnly</c> is declared
    /// with <c>requireOrgScope: false</c> (<c>AuthorizationPolicies.cs</c>), so
    /// <see cref="ScopeAuthorizationHandler"/> decides it from the <c>role</c> claim ALONE: it
    /// reads that claim, fails the requirement when the claim is not in the allowed set, and —
    /// because no org scope is required — succeeds and returns WITHOUT ever reading the
    /// <c>scopes</c> array. The role claim is therefore both necessary and sufficient there, and a
    /// GlobalAdmin token commonly carries no scopes at all.</para>
    ///
    /// <para><b>Why there is deliberately NO scope fallback</b> (S140 sprint-end review; the
    /// SEC-021 mixed-role over-grant family). An earlier revision of this predicate ALSO accepted
    /// a GLOBAL <see cref="RoleScope"/> whose own role was GlobalAdmin, reasoning that refusing it
    /// would make this gate STRICTER than the payroll endpoint. That reasoning was inverted: the
    /// payroll endpoint is strictly primary-role, so the fallback made this gate LOOSER, not safer
    /// — an actor whose PRIMARY role was LocalHR or LocalAdmin but who held a GLOBAL scope with
    /// <c>Role = GlobalAdmin</c> was REFUSED by <c>/api/payroll/recalculate</c> and ADMITTED here.
    /// It could record "this exported payroll month has been recalculated" as audited fact, then be
    /// unable to actually perform the recalculation, and the row would silently leave HR's open
    /// list with nobody chasing it. Deleting the fallback is strictly TIGHTENING: no actor who can
    /// perform the remedy loses anything, because such an actor's primary role IS GlobalAdmin.</para>
    ///
    /// <para><b>Not to be "re-aligned" with <c>OrchestratorScopeHelpers.IsGlobalAdmin</c>.</b> That
    /// helper keeps a GLOBAL-scope fallback on purpose, but it answers a DIFFERENT question — a
    /// task-READ bypass that must survive an unresolvable subject — not "may this actor perform the
    /// Global-Admin-only payroll remedy". A gate that mirrors a policy must evaluate exactly what
    /// that policy evaluates; consistency with a differently-purposed helper is not a reason to
    /// widen this one.</para>
    /// </summary>
    private static bool IsGlobalAdmin(ActorContext actor) =>
        string.Equals(actor.ActorRole, StatsTidRoles.GlobalAdmin, StringComparison.Ordinal);

    // ── projection: storage row → wire DTO (derived fields computed here, DB-free) ──

    private static BackdateWorklistRow ToDto(HrBackdateWorklistRow row)
    {
        var isExportedMonth = string.Equals(row.Kind, WorklistKinds.ExportedMonth, StringComparison.Ordinal);

        var triggers = row.Triggers
            .Select(t => new BackdateWorklistTriggerDto(
                Kind: t.Kind,
                EventId: t.EventId,
                EffectiveFrom: t.EffectiveFrom,
                AppendedAt: t.AppendedAt,
                ActorId: t.ActorId,
                BaselineContentHash: t.BaselineContentHash,
                BaselineSettlementSequence: t.BaselineSettlementSequence,
                BaselineSettlementState: t.BaselineSettlementState,
                RecalculatedSince: BackdateWorklistDerivation.RecalculatedSinceForTrigger(row, t),
                ReversedSince: BackdateWorklistDerivation.ReversedSinceForTrigger(row, t),
                RecalcBlockedBy: isExportedMonth && row.Year is int y && row.Month is int m
                    ? BackdateWorklistDerivation.RecalcBlockedByForTrigger(t.Kind, t.EffectiveFrom, y, m)
                    : null))
            .ToList();

        return new BackdateWorklistRow(
            WorklistId: row.WorklistId,
            EmployeeId: row.EmployeeId,
            Kind: row.Kind,
            Year: row.Year,
            Month: row.Month,
            ExportId: row.ExportId,
            EntitlementType: row.EntitlementType,
            EntitlementYear: row.EntitlementYear,
            Triggers: triggers,
            RecalcBlockedBy: BackdateWorklistDerivation.RecalcBlockedBy(row),
            RecalculatedSince: BackdateWorklistDerivation.RecalculatedSince(row),
            ReversedSince: BackdateWorklistDerivation.ReversedSince(row),
            CreatedAt: row.CreatedAt,
            CreatedBy: row.CreatedBy,
            ResolvedAt: row.ResolvedAt,
            ResolvedBy: row.ResolvedBy,
            Resolution: row.Resolution,
            ResolutionReason: row.ResolutionReason,
            Version: row.Version);
    }
}
