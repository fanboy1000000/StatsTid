using System.Text.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Backend.Api.Endpoints.Helpers;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Outbox;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Backend.Api.Endpoints;

/// <summary>
/// S30 / TASK-3007 — admin CRUD surface for entitlement-policy versioned history (ADR-021 D2 +
/// ADR-018 D14 export-time effective-date lookup pattern). Mirrors S29 WTM endpoint shape
/// verbatim: 5 endpoints under <c>/api/admin/entitlement-configs</c>, all
/// <c>RequireAuthorization("GlobalAdminOnly")</c> per ADR-019 D5. POST / PUT / DELETE run under
/// a single <c>SELECT ... FOR UPDATE WHERE effective_to IS NULL</c> natural-key lock via
/// <see cref="EntitlementConfigRepository.AcquireLockAsync"/>; the repo's
/// <see cref="EntitlementConfigRepository.SupersedeAndCreateAsync"/> dispatches ADR-020 D2's
/// 3-case routing (Case A no-predecessor / Case B cross-day / Case C same-day update-in-place).
///
/// <para>
/// <b>Same-day-only-edit validator</b> (refinement L127, cycle 3 symmetric forbid — S29 WTM
/// precedent): POST/PUT bodies require <c>effective_from = today</c>; any other value → 422.
/// POST defaults a missing field to today. PUT requires it explicitly.
/// </para>
///
/// <para>
/// <b>reset_month / accrual_model immutability</b> (PLAN-s30 Callout 8 + Q1 sub-fork (i)
/// freeze): when a predecessor exists, the request body's <c>reset_month</c> and
/// <c>accrual_model</c> must match the predecessor's values; otherwise 422 with structured
/// error body. These two fields drive the entitlement-year boundary + accrual semantics and
/// would change the consumption read site silently if mutated — admins must create a new
/// <c>ok_version</c> row instead. On POST with no predecessor (Case A) the guard is trivially
/// satisfied.
/// </para>
///
/// <para>
/// <b>Dual-emission on Case B</b> (ADR-019 D1): cross-day supersession emits TWO outbox events
/// and TWO audit rows in the same atomic transaction (ADR-018 D3) — one
/// <c>EntitlementConfigSuperseded</c> + <c>SUPERSEDED</c> audit on the predecessor's
/// <c>config_id</c>, and one <c>EntitlementConfigCreated</c> + <c>CREATED</c> audit on the new
/// row's <c>config_id</c>. Both share the same natural-key stream
/// <c>entitlement-config-{type}-{agreement}-{ok}</c> because the natural key is unchanged
/// across supersession (replay determinism per ADR-016 D10).
/// </para>
///
/// <para>
/// <b>ETag/If-Match contract</b> per ADR-019 D2/D5/D6: PUT + DELETE require
/// <c>If-Match: "&lt;version&gt;"</c> (admin-strict mode rejects <c>If-None-Match: *</c>). On
/// the write-side: 412 when stale, 428 when missing, 409 when If-Match references a row that
/// cannot match any current open row (already soft-deleted / never existed).
/// </para>
/// </summary>
public static class EntitlementConfigEndpoints
{
    public static WebApplication MapEntitlementConfigEndpoints(this WebApplication app)
    {
        // ═══════════════════════════════════════════
        // 1. GET /api/admin/entitlement-configs — List all live (open) entitlement configs
        //    Filters to effective_to IS NULL (admin sees the current row per natural key
        //    only; history is replay-only). Per-row `version` is the source of truth for
        //    composing `If-Match` on subsequent mutations (no per-resource ETag header
        //    because multiple rows are returned).
        // ═══════════════════════════════════════════
        app.MapGet("/api/admin/entitlement-configs", async (
            EntitlementConfigRepository repo,
            CancellationToken ct) =>
        {
            var configs = await repo.GetAllAsync(ct);
            return Results.Ok(configs.Select(MapToResponse).ToList());
        }).RequireAuthorization("GlobalAdminOnly");

        // ═══════════════════════════════════════════
        // 2. GET /api/admin/entitlement-configs/{configId:guid} — Get one by ID
        //    Returns the row (possibly a historical / closed one) with
        //    `ETag: "<version>"` so the admin UI can compose `If-Match` for the next
        //    mutation. 404 if the config_id is unknown.
        // ═══════════════════════════════════════════
        app.MapGet("/api/admin/entitlement-configs/{configId:guid}", async (
            Guid configId,
            DbConnectionFactory connectionFactory,
            HttpContext context,
            CancellationToken ct) =>
        {
            await using var conn = connectionFactory.Create();
            await conn.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand(
                "SELECT * FROM entitlement_configs WHERE config_id = @configId",
                conn);
            cmd.Parameters.AddWithValue("configId", configId);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                return Results.NotFound(new { error = "Entitlement config not found" });

            var config = new EntitlementConfig
            {
                ConfigId = reader.GetGuid(reader.GetOrdinal("config_id")),
                EntitlementType = reader.GetString(reader.GetOrdinal("entitlement_type")),
                AgreementCode = reader.GetString(reader.GetOrdinal("agreement_code")),
                OkVersion = reader.GetString(reader.GetOrdinal("ok_version")),
                AnnualQuota = reader.GetDecimal(reader.GetOrdinal("annual_quota")),
                AccrualModel = reader.GetString(reader.GetOrdinal("accrual_model")),
                ResetMonth = reader.GetInt32(reader.GetOrdinal("reset_month")),
                CarryoverMax = reader.GetDecimal(reader.GetOrdinal("carryover_max")),
                ProRateByPartTime = reader.GetBoolean(reader.GetOrdinal("pro_rate_by_part_time")),
                IsPerEpisode = reader.GetBoolean(reader.GetOrdinal("is_per_episode")),
                MinAge = reader.IsDBNull(reader.GetOrdinal("min_age"))
                    ? null
                    : reader.GetInt32(reader.GetOrdinal("min_age")),
                Description = reader.IsDBNull(reader.GetOrdinal("description"))
                    ? null
                    : reader.GetString(reader.GetOrdinal("description")),
                // S73 / TASK-7301 (R2): full-day-only flag threads the read surface.
                FullDayOnly = reader.GetBoolean(reader.GetOrdinal("full_day_only")),
                CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
                Version = reader.GetInt64(reader.GetOrdinal("version")),
                EffectiveFrom = reader.GetFieldValue<DateOnly>(reader.GetOrdinal("effective_from")),
                EffectiveTo = reader.IsDBNull(reader.GetOrdinal("effective_to"))
                    ? null
                    : reader.GetFieldValue<DateOnly>(reader.GetOrdinal("effective_to")),
            };

            context.Response.Headers.ETag = $"\"{config.Version}\"";
            return Results.Ok(MapToResponse(config));
        }).RequireAuthorization("GlobalAdminOnly");

        // ═══════════════════════════════════════════
        // 3. POST /api/admin/entitlement-configs — Create config
        //
        // ADR-020 D2 Case A — fresh INSERT when no open row exists for the natural key. The
        // repo's `SupersedeAndCreateAsync(predecessor: null)` runs the INSERT inside the same
        // transaction that holds the `AcquireLockAsync` lock (which finds nothing → null
        // predecessor → Case A branch).
        //
        // Edge: when an admin issued a soft-DELETE earlier today and then POSTs the same
        // natural key today, the open-row lookup returns null AND the row at
        // (effective_from = today, effective_to = today) collides with the new INSERT on
        // `idx_ec_natural_key_history` → PostgresException 23505. We rescue with 412 (mirrors
        // S29 WTM L186-201 + S22 LocalAgreementProfile empty-slot precedent at L317-336).
        //
        // Same-day-only-edit validator (refinement L127) runs BEFORE opening the tx. POST
        // defaults a missing `effective_from` to today (preserves the common admin-create-now
        // shape); any other supplied date → 422.
        // ═══════════════════════════════════════════
        app.MapPost("/api/admin/entitlement-configs", async (
            CreateEntitlementConfigRequest body,
            EntitlementConfigRepository repo,
            DbConnectionFactory connectionFactory,
            IOutboxEnqueue outbox,
            IAuditProjectionMapper<EntitlementConfigCreated> createdMapper,
            AuditProjectionRepository auditRepo,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();
            var actorId = actor.ActorId ?? "unknown";
            var actorRole = actor.ActorRole ?? "unknown";

            // 1. Same-day-only-edit validator (cycle 3 symmetric forbid).
            var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
            var requestedEffectiveFrom = body.EffectiveFrom ?? today;
            if (requestedEffectiveFrom != today)
            {
                return Results.UnprocessableEntity(new
                {
                    error = "effective_from must equal today (same-day-only edits permitted in S30)",
                    suppliedEffectiveFrom = requestedEffectiveFrom,
                    today = today,
                });
            }

            // 1b. VACATION reset_month is STATUTORILY 9 (1 Sep ferieår; LBK 230/2021). The §21/§24
            //     settlement boundary depends on it; a non-9 VACATION config would let the close
            //     poller diverge from the dated-snapshot valuation (S68 Step-7a Codex c2 B1). Reject
            //     with a friendly 422 here; the DB CHECK entitlement_configs_vacation_reset_month is
            //     the data-layer backstop for any other write path.
            if (string.Equals(body.EntitlementType, "VACATION", StringComparison.Ordinal) && body.ResetMonth != 9)
            {
                return Results.UnprocessableEntity(new
                {
                    error = "VACATION reset_month must be 9 (the statutory 1 Sep – 31 Aug ferieår).",
                    suppliedResetMonth = body.ResetMonth,
                });
            }

            // 1c. S73 / TASK-7301 (SPRINT-73 R2 construction-enforcement, the S68-B1 lesson):
            //     CARE_DAY + SENIOR_DAY are FULL-DAY-ONLY per the D-A owner ruling (2026-06-13)
            //     — a PRODUCT RULE, not a default. A POST with the flag false/ABSENT (the DTO
            //     defaults a missing field to false) is rejected with a friendly 422; the DB
            //     CHECK entitlement_configs_full_day_only_types is the data-layer backstop for
            //     any other write path.
            if (FullDayOnlyGuard.IsViolated(body.EntitlementType, body.FullDayOnly, out var fullDayError))
                return Results.UnprocessableEntity(fullDayError!);

            var streamId = $"entitlement-config-{body.EntitlementType}-{body.AgreementCode}-{body.OkVersion}";

            await using var conn = connectionFactory.Create();
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                // 2. Acquire the natural-key lock on the currently-open row (if any).
                //    For POST, we expect null (Case A). If an open row exists, the admin
                //    should be using PUT — surface 409 (resource exists, wrong verb).
                var existingOpen = await repo.AcquireLockAsync(
                    conn, tx, body.EntitlementType, body.AgreementCode, body.OkVersion, ct);
                if (existingOpen is not null)
                {
                    await tx.RollbackAsync(ct);
                    return Results.Conflict(new
                    {
                        error = "An open entitlement config already exists for this natural key; use PUT to edit.",
                        currentState = MapToResponse(existingOpen),
                    });
                }

                // 3. Build the new config row. reset_month/accrual_model immutability guard
                //    is trivially satisfied here (no predecessor) per PLAN-s30 Callout 8.
                var newConfigId = Guid.NewGuid();
                var newConfig = new EntitlementConfig
                {
                    ConfigId = newConfigId,
                    EntitlementType = body.EntitlementType,
                    AgreementCode = body.AgreementCode,
                    OkVersion = body.OkVersion,
                    AnnualQuota = body.AnnualQuota,
                    AccrualModel = body.AccrualModel,
                    ResetMonth = body.ResetMonth,
                    CarryoverMax = body.CarryoverMax,
                    ProRateByPartTime = body.ProRateByPartTime,
                    IsPerEpisode = body.IsPerEpisode,
                    MinAge = body.MinAge,
                    Description = body.Description,
                    FullDayOnly = body.FullDayOnly, // S73 / TASK-7301 (R2)
                    EffectiveFrom = today,
                };

                SaveEntitlementConfigResult saveResult;
                try
                {
                    saveResult = await repo.SupersedeAndCreateAsync(
                        conn, tx, newConfig, predecessor: null, expectedCurrentVersion: null, ct);
                }
                catch (PostgresException ex) when (
                    ex.SqlState == "23505" && ex.ConstraintName == "idx_ec_natural_key_history")
                {
                    // Same-day collision — earlier soft-DELETE today left a closed-today row
                    // at (effective_from = today). Surface 412 with the current state of the
                    // (now-null open) row context.
                    await tx.RollbackAsync(ct);
                    return Results.Json(new
                    {
                        error = "Concurrency precondition failed",
                        message = "A closed-today row already exists for this natural key; the history-uniqueness index forbids INSERT at the same effective_from.",
                    }, statusCode: 412);
                }
                catch (PostgresException ex) when (
                    ex.SqlState == "23505" && ex.ConstraintName == "idx_ec_natural_key_open")
                {
                    // Concurrent CREATE won the open-row partial-unique-index race.
                    await tx.RollbackAsync(ct);
                    var currentState = await repo.GetCurrentOpenAsync(
                        body.EntitlementType, body.AgreementCode, body.OkVersion, ct);
                    return Results.Json(new
                    {
                        error = "Concurrency precondition failed",
                        message = "Another entitlement config was created concurrently for this natural key; refresh and retry.",
                        currentState = currentState is null ? null : MapToResponse(currentState),
                    }, statusCode: 412);
                }

                var persistedConfig = saveResult.Config;
                var persistedVersion = saveResult.Version;

                // 4. Audit (CREATED — version_before = null per ADR-019 D8) + outbox emission
                //    (ADR-018 D3 atomic same-tx).
                await repo.AppendAuditAsync(
                    conn, tx,
                    persistedConfig.ConfigId,
                    body.EntitlementType, body.AgreementCode, body.OkVersion,
                    action: "CREATED",
                    previousData: null,
                    newData: JsonSerializer.Serialize(body),
                    versionBefore: null,
                    versionAfter: persistedVersion,
                    actorId, actorRole, ct);

                var createdEvent = new EntitlementConfigCreated
                {
                    ConfigId = persistedConfig.ConfigId,
                    EntitlementType = persistedConfig.EntitlementType,
                    AgreementCode = persistedConfig.AgreementCode,
                    OkVersion = persistedConfig.OkVersion,
                    EffectiveFrom = persistedConfig.EffectiveFrom,
                    EffectiveTo = persistedConfig.EffectiveTo,
                    RowVersion = persistedVersion,
                    AnnualQuota = persistedConfig.AnnualQuota,
                    AccrualModel = persistedConfig.AccrualModel,
                    ResetMonth = persistedConfig.ResetMonth,
                    CarryoverMax = persistedConfig.CarryoverMax,
                    ProRateByPartTime = persistedConfig.ProRateByPartTime,
                    IsPerEpisode = persistedConfig.IsPerEpisode,
                    MinAge = persistedConfig.MinAge,
                    Description = persistedConfig.Description,
                    FullDayOnly = persistedConfig.FullDayOnly, // S73 / TASK-7301 (R2, additive-nullable)
                    ActorId = actorId,
                    ActorRole = actorRole,
                    CorrelationId = actor.CorrelationId,
                };
                // S44 TASK-4413: capture outbox_id for audit_projection insert
                // (ADR-026 D2 sync-in-tx projection write — atomic with the
                // entitlement_configs row + outbox row per ADR-018 D3/D13).
                var outboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, streamId, createdEvent, ct);

                var auditCtx = new AuditProjectionContext(
                    ActorId: actor.ActorId,
                    ActorPrimaryOrgId: actor.OrgId,
                    CorrelationId: actor.CorrelationId,
                    OccurredAt: new DateTimeOffset(createdEvent.OccurredAt));
                var auditRow = createdMapper.Map(createdEvent, auditCtx);
                await auditRepo.InsertAsync(conn, tx, createdEvent.EventId, outboxId, createdEvent.EventType, auditRow, auditCtx, ct);

                await tx.CommitAsync(ct);

                context.Response.Headers.ETag = $"\"{persistedVersion}\"";
                return Results.Created(
                    $"/api/admin/entitlement-configs/{persistedConfig.ConfigId}",
                    MapToResponse(persistedConfig));
            }
            catch
            {
                if (tx.Connection is not null)
                    await tx.RollbackAsync(ct);
                throw;
            }
        }).RequireAuthorization("GlobalAdminOnly");

        // ═══════════════════════════════════════════
        // 4. PUT /api/admin/entitlement-configs/{configId:guid} — Edit config
        //
        // Routes through `SupersedeAndCreateAsync` per ADR-020 D2: the repo's internal dispatch
        // chooses Case B (cross-day) or Case C (same-day update-in-place) based on
        // `newConfig.EffectiveFrom == predecessor.EffectiveFrom`.
        //
        //   Case B (predecessor.EffectiveFrom < today)
        //     → close predecessor at effective_to = today + INSERT new open row at version 1.
        //     Dual-emission per ADR-019 D1:
        //       • SUPERSEDED audit + EntitlementConfigSuperseded outbox on predecessor's
        //         config_id (carries SupersededByConfigId pointer to the new row)
        //       • CREATED audit + EntitlementConfigCreated outbox on the new config_id
        //     Both events emit on the SAME natural-key stream
        //     (entitlement-config-{type}-{agreement}-{ok}) because the natural key is
        //     unchanged across supersession (replay determinism per ADR-016 D10).
        //
        //   Case C (predecessor.EffectiveFrom == today)
        //     → in-place UPDATE on the open row + version bump. UPDATED audit + a single
        //     EntitlementConfigCreated outbox (S29 WTM precedent uses Updated/Created
        //     symmetric; we mirror by reusing Created which carries the post-mutation
        //     snapshot — replay-deterministic via dated reads). Single-emission.
        //
        // reset_month / accrual_model immutability guard (PLAN-s30 Callout 8) runs AFTER the
        // FOR UPDATE lock acquires predecessor and BEFORE SupersedeAndCreateAsync. When the
        // body's value differs from predecessor's, return 422 with the structured error body
        // (Q1 sub-fork (i) freeze — admins create a new ok_version row instead of editing).
        //
        // Admin-strict If-Match: "<version>" required. 412 on stale, 428 on missing.
        // 409 when no open row exists (already soft-deleted; admins must POST a new row).
        // ═══════════════════════════════════════════
        app.MapPut("/api/admin/entitlement-configs/{configId:guid}", async (
            Guid configId,
            UpdateEntitlementConfigRequest body,
            EntitlementConfigRepository repo,
            DbConnectionFactory connectionFactory,
            IOutboxEnqueue outbox,
            IAuditProjectionMapper<EntitlementConfigCreated> createdMapper,
            IAuditProjectionMapper<EntitlementConfigSuperseded> supersededMapper,
            AuditProjectionRepository auditRepo,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();
            var actorId = actor.ActorId ?? "unknown";
            var actorRole = actor.ActorRole ?? "unknown";

            // 1. Same-day-only-edit validator (cycle 3 symmetric forbid).
            var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
            if (body.EffectiveFrom != today)
            {
                return Results.UnprocessableEntity(new
                {
                    error = "effective_from must equal today (same-day-only edits permitted in S30)",
                    suppliedEffectiveFrom = body.EffectiveFrom,
                    today = today,
                });
            }

            // 1b. S73 / TASK-7301 (SPRINT-73 R2 construction-enforcement, the S68-B1 lesson):
            //     a PUT must not silently un-rule the D-A full-day-only ruling for
            //     CARE_DAY/SENIOR_DAY — flag false/ABSENT → 422 (the body validator runs before
            //     the If-Match protocol parse, mirroring the same-day validator's precedence).
            if (FullDayOnlyGuard.IsViolated(body.EntitlementType, body.FullDayOnly, out var fullDayError))
                return Results.UnprocessableEntity(fullDayError!);

            // 2. Admin-strict If-Match parse — 428 if missing or If-None-Match: * supplied.
            if (!EtagHeaderHelper.TryParseIfMatch(
                    context.Request, out var expectedVersion, out var headerError))
                return Results.Json(new { error = headerError }, statusCode: 428);

            var streamId = $"entitlement-config-{body.EntitlementType}-{body.AgreementCode}-{body.OkVersion}";

            await using var conn = connectionFactory.Create();
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                // 3. Acquire the natural-key lock on the open row. PUT requires a predecessor;
                //    a null result means the row was soft-deleted (or never existed) → 409
                //    disjoint per ADR-019 D6 (admin tries to update an already-closed row).
                var predecessor = await repo.AcquireLockAsync(
                    conn, tx, body.EntitlementType, body.AgreementCode, body.OkVersion, ct);
                if (predecessor is null)
                {
                    await tx.RollbackAsync(ct);
                    return Results.Conflict(new
                    {
                        error = "No open entitlement config exists for this natural key; use POST to create.",
                    });
                }

                // 4. Cross-check the URL config_id vs the open row identity. Per the
                //    natural-key model, the open row IS the resource at this URL; if they
                //    disagree, the client is targeting a closed/historical row → 409
                //    disjoint per ADR-019 D6.
                if (predecessor.ConfigId != configId)
                {
                    await tx.RollbackAsync(ct);
                    return Results.Conflict(new
                    {
                        error = "URL config_id targets a closed or unknown row; the currently-open row has a different config_id.",
                        urlConfigId = configId,
                        currentOpenConfigId = predecessor.ConfigId,
                    });
                }

                // 5. reset_month / accrual_model immutability guard (PLAN-s30 Callout 8 +
                //    Q1 sub-fork (i) freeze). These two fields drive entitlement-year
                //    boundary + accrual semantics; mutating them via admin CRUD would
                //    silently shift the consumption read site. Admins must create a new
                //    ok_version row instead. 422 with structured error body.
                if (body.ResetMonth != predecessor.ResetMonth ||
                    !string.Equals(body.AccrualModel, predecessor.AccrualModel, StringComparison.Ordinal))
                {
                    await tx.RollbackAsync(ct);
                    return Results.UnprocessableEntity(new
                    {
                        error = "reset_month and accrual_model are agreement-defining and cannot be edited via admin CRUD; create a new ok_version row instead",
                        supplied = new { reset_month = body.ResetMonth, accrual_model = body.AccrualModel },
                        immutable = new[] { "reset_month", "accrual_model" },
                    });
                }

                // 6. Build the new config row and call SupersedeAndCreateAsync. The repo
                //    dispatches Case B vs Case C based on predecessor.EffectiveFrom vs
                //    newConfig.EffectiveFrom (== today). OptimisticConcurrencyException
                //    surfaces as 412 below.
                var newConfig = new EntitlementConfig
                {
                    ConfigId = Guid.NewGuid(), // only used on Case B (cross-day INSERT)
                    EntitlementType = body.EntitlementType,
                    AgreementCode = body.AgreementCode,
                    OkVersion = body.OkVersion,
                    AnnualQuota = body.AnnualQuota,
                    AccrualModel = body.AccrualModel,
                    ResetMonth = body.ResetMonth,
                    CarryoverMax = body.CarryoverMax,
                    ProRateByPartTime = body.ProRateByPartTime,
                    IsPerEpisode = body.IsPerEpisode,
                    MinAge = body.MinAge,
                    Description = body.Description,
                    FullDayOnly = body.FullDayOnly, // S73 / TASK-7301 (R2 version-survival: the editor round-trips the flag)
                    EffectiveFrom = today,
                };

                SaveEntitlementConfigResult saveResult;
                try
                {
                    saveResult = await repo.SupersedeAndCreateAsync(
                        conn, tx, newConfig, predecessor, expectedCurrentVersion: expectedVersion, ct);
                }
                catch (OptimisticConcurrencyException ex)
                {
                    await tx.RollbackAsync(ct);
                    return Results.Json(new
                    {
                        error = "Concurrency precondition failed",
                        expectedVersion = ex.ExpectedVersion,
                        actualVersion = ex.ActualVersion,
                        currentState = MapToResponse(predecessor),
                    }, statusCode: 412);
                }
                catch (InvalidProfileSupersessionException ex)
                {
                    // Backdate guard from the repo (ADR-018 D9). Unreachable through the
                    // same-day-only-edit validator above (today >= any past day), but
                    // surface 400 defensively.
                    await tx.RollbackAsync(ct);
                    return Results.BadRequest(new { error = ex.Message });
                }

                var isCrossDay = saveResult.SupersededConfigId is not null;

                if (isCrossDay)
                {
                    // ── Case B — dual-emission per ADR-019 D1.

                    // Emission 1: predecessor close — SUPERSEDED audit + EntitlementConfigSuperseded
                    // outbox on predecessor's config_id. Captures the version transition
                    // (predecessor's version is NOT bumped on close per ADR-021 D2; we record
                    // version_before = version_after = predecessor.Version).
                    await repo.AppendAuditAsync(
                        conn, tx,
                        predecessor.ConfigId,
                        body.EntitlementType, body.AgreementCode, body.OkVersion,
                        action: "SUPERSEDED",
                        previousData: JsonSerializer.Serialize(predecessor),
                        newData: $"{{\"supersededByConfigId\":\"{saveResult.Config.ConfigId}\"}}",
                        versionBefore: predecessor.Version,
                        versionAfter: predecessor.Version,
                        actorId, actorRole, ct);

                    var supersededEvent = new EntitlementConfigSuperseded
                    {
                        ConfigId = predecessor.ConfigId,
                        EntitlementType = predecessor.EntitlementType,
                        AgreementCode = predecessor.AgreementCode,
                        OkVersion = predecessor.OkVersion,
                        EffectiveFrom = predecessor.EffectiveFrom,
                        EffectiveTo = today, // the close-date stamped on the predecessor
                        RowVersion = predecessor.Version,
                        SupersededByConfigId = saveResult.Config.ConfigId,
                        FullDayOnly = predecessor.FullDayOnly, // S73 / TASK-7301 (R2, additive-nullable)
                        ActorId = actorId,
                        ActorRole = actorRole,
                        CorrelationId = actor.CorrelationId,
                    };
                    // S44 TASK-4413: capture outbox_id for audit_projection insert
                    // (ADR-026 D2 sync-in-tx — dual-emit first audit row for the
                    // superseded predecessor).
                    var supersededOutboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, streamId, supersededEvent, ct);

                    var supersededAuditCtx = new AuditProjectionContext(
                        ActorId: actor.ActorId,
                        ActorPrimaryOrgId: actor.OrgId,
                        CorrelationId: actor.CorrelationId,
                        OccurredAt: new DateTimeOffset(supersededEvent.OccurredAt));
                    var supersededAuditRow = supersededMapper.Map(supersededEvent, supersededAuditCtx);
                    await auditRepo.InsertAsync(conn, tx, supersededEvent.EventId, supersededOutboxId, supersededEvent.EventType, supersededAuditRow, supersededAuditCtx, ct);

                    // Emission 2: new row CREATED audit + EntitlementConfigCreated outbox on
                    // the new config_id. Same natural-key stream (replay determinism per
                    // ADR-016 D10).
                    await repo.AppendAuditAsync(
                        conn, tx,
                        saveResult.Config.ConfigId,
                        body.EntitlementType, body.AgreementCode, body.OkVersion,
                        action: "CREATED",
                        previousData: null,
                        newData: JsonSerializer.Serialize(body),
                        versionBefore: null,
                        versionAfter: saveResult.Version,
                        actorId, actorRole, ct);
                }
                else
                {
                    // ── Case C — same-day in-place UPDATE. Single UPDATED audit + single
                    //    EntitlementConfigCreated outbox carrying the post-mutation snapshot.
                    await repo.AppendAuditAsync(
                        conn, tx,
                        saveResult.Config.ConfigId,
                        body.EntitlementType, body.AgreementCode, body.OkVersion,
                        action: "UPDATED",
                        previousData: JsonSerializer.Serialize(predecessor),
                        newData: JsonSerializer.Serialize(body),
                        versionBefore: predecessor.Version,
                        versionAfter: saveResult.Version,
                        actorId, actorRole, ct);
                }

                var createdEvent = new EntitlementConfigCreated
                {
                    ConfigId = saveResult.Config.ConfigId,
                    EntitlementType = saveResult.Config.EntitlementType,
                    AgreementCode = saveResult.Config.AgreementCode,
                    OkVersion = saveResult.Config.OkVersion,
                    EffectiveFrom = saveResult.Config.EffectiveFrom,
                    EffectiveTo = saveResult.Config.EffectiveTo,
                    RowVersion = saveResult.Version,
                    AnnualQuota = saveResult.Config.AnnualQuota,
                    AccrualModel = saveResult.Config.AccrualModel,
                    ResetMonth = saveResult.Config.ResetMonth,
                    CarryoverMax = saveResult.Config.CarryoverMax,
                    ProRateByPartTime = saveResult.Config.ProRateByPartTime,
                    IsPerEpisode = saveResult.Config.IsPerEpisode,
                    MinAge = saveResult.Config.MinAge,
                    Description = saveResult.Config.Description,
                    FullDayOnly = saveResult.Config.FullDayOnly, // S73 / TASK-7301 (R2, additive-nullable)
                    ActorId = actorId,
                    ActorRole = actorRole,
                    CorrelationId = actor.CorrelationId,
                };
                // S44 TASK-4413: capture outbox_id for audit_projection insert
                // (ADR-026 D2 sync-in-tx projection write — covers both Case B
                // emission 2 and Case C single-emission).
                var createdOutboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, streamId, createdEvent, ct);

                var createdAuditCtx = new AuditProjectionContext(
                    ActorId: actor.ActorId,
                    ActorPrimaryOrgId: actor.OrgId,
                    CorrelationId: actor.CorrelationId,
                    OccurredAt: new DateTimeOffset(createdEvent.OccurredAt));
                var createdAuditRow = createdMapper.Map(createdEvent, createdAuditCtx);
                await auditRepo.InsertAsync(conn, tx, createdEvent.EventId, createdOutboxId, createdEvent.EventType, createdAuditRow, createdAuditCtx, ct);

                await tx.CommitAsync(ct);

                context.Response.Headers.ETag = $"\"{saveResult.Version}\"";
                return Results.Ok(MapToResponse(saveResult.Config));
            }
            catch
            {
                if (tx.Connection is not null)
                    await tx.RollbackAsync(ct);
                throw;
            }
        }).RequireAuthorization("GlobalAdminOnly");

        // ═══════════════════════════════════════════
        // 5. DELETE /api/admin/entitlement-configs/{configId:guid} — Soft-delete config
        //
        // Soft-delete via `effective_to = today` per ADR-021 D2 (replay determinism preserved).
        // Admin-strict If-Match: "<version>" required. 204 No Content on success — NO ETag
        // header (resource gone; nothing to ETag).
        //
        // 409 disjoint when no open row exists (already soft-deleted); 412 on version mismatch;
        // 428 on missing If-Match.
        // ═══════════════════════════════════════════
        app.MapDelete("/api/admin/entitlement-configs/{configId:guid}", async (
            Guid configId,
            EntitlementConfigRepository repo,
            DbConnectionFactory connectionFactory,
            IOutboxEnqueue outbox,
            IAuditProjectionMapper<EntitlementConfigSoftDeleted> softDeletedMapper,
            AuditProjectionRepository auditRepo,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();
            var actorId = actor.ActorId ?? "unknown";
            var actorRole = actor.ActorRole ?? "unknown";

            // 1. Admin-strict If-Match parse.
            if (!EtagHeaderHelper.TryParseIfMatch(
                    context.Request, out var expectedVersion, out var headerError))
                return Results.Json(new { error = headerError }, statusCode: 428);

            var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

            await using var conn = connectionFactory.Create();
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                // 2. Look up the row by config_id under SELECT ... FOR UPDATE to serialize
                //    against concurrent edits. We use a direct query rather than
                //    AcquireLockAsync because DELETE targets a specific config_id (which may
                //    be the open row OR an already-closed historical row — we need to
                //    distinguish the two for the 409 disjoint vs 412 stale paths).
                EntitlementConfig? row = null;
                await using (var lockCmd = new NpgsqlCommand(
                    "SELECT * FROM entitlement_configs WHERE config_id = @configId FOR UPDATE",
                    conn, tx))
                {
                    lockCmd.Parameters.AddWithValue("configId", configId);
                    await using var reader = await lockCmd.ExecuteReaderAsync(ct);
                    if (await reader.ReadAsync(ct))
                    {
                        row = new EntitlementConfig
                        {
                            ConfigId = reader.GetGuid(reader.GetOrdinal("config_id")),
                            EntitlementType = reader.GetString(reader.GetOrdinal("entitlement_type")),
                            AgreementCode = reader.GetString(reader.GetOrdinal("agreement_code")),
                            OkVersion = reader.GetString(reader.GetOrdinal("ok_version")),
                            AnnualQuota = reader.GetDecimal(reader.GetOrdinal("annual_quota")),
                            AccrualModel = reader.GetString(reader.GetOrdinal("accrual_model")),
                            ResetMonth = reader.GetInt32(reader.GetOrdinal("reset_month")),
                            CarryoverMax = reader.GetDecimal(reader.GetOrdinal("carryover_max")),
                            ProRateByPartTime = reader.GetBoolean(reader.GetOrdinal("pro_rate_by_part_time")),
                            IsPerEpisode = reader.GetBoolean(reader.GetOrdinal("is_per_episode")),
                            MinAge = reader.IsDBNull(reader.GetOrdinal("min_age"))
                                ? null
                                : reader.GetInt32(reader.GetOrdinal("min_age")),
                            Description = reader.IsDBNull(reader.GetOrdinal("description"))
                                ? null
                                : reader.GetString(reader.GetOrdinal("description")),
                            // S73 / TASK-7301 (R2): keeps the DELETE audit's previousData honest.
                            FullDayOnly = reader.GetBoolean(reader.GetOrdinal("full_day_only")),
                            CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
                            Version = reader.GetInt64(reader.GetOrdinal("version")),
                            EffectiveFrom = reader.GetFieldValue<DateOnly>(reader.GetOrdinal("effective_from")),
                            EffectiveTo = reader.IsDBNull(reader.GetOrdinal("effective_to"))
                                ? null
                                : reader.GetFieldValue<DateOnly>(reader.GetOrdinal("effective_to")),
                        };
                    }
                }

                if (row is null)
                {
                    await tx.RollbackAsync(ct);
                    return Results.NotFound(new { error = "Entitlement config not found" });
                }

                // 3. 409 disjoint — admin tries to soft-delete an already-closed row.
                if (row.EffectiveTo is not null)
                {
                    await tx.RollbackAsync(ct);
                    return Results.Conflict(new
                    {
                        error = "Entitlement config is already closed (soft-deleted or superseded); cannot delete.",
                        effectiveTo = row.EffectiveTo,
                    });
                }

                // 4. 412 stale — If-Match version doesn't match the locked row's current version.
                if (row.Version != expectedVersion)
                {
                    await tx.RollbackAsync(ct);
                    return Results.Json(new
                    {
                        error = "Concurrency precondition failed",
                        expectedVersion = expectedVersion,
                        actualVersion = row.Version,
                        currentState = MapToResponse(row),
                    }, statusCode: 412);
                }

                // 5. Soft-delete via repo (stamps effective_to = today; version NOT bumped per
                //    S22 lifecycle-close-is-not-content-edit precedent inherited by S30).
                var closedRow = await repo.SoftDeleteAsync(conn, tx, row, today, ct);

                // 6. DELETED audit (v3 overload — version_before = version_after = pre-deletion
                //    version per ADR-019 D8 DELETE convention).
                await repo.AppendAuditAsync(
                    conn, tx,
                    closedRow.ConfigId,
                    closedRow.EntitlementType, closedRow.AgreementCode, closedRow.OkVersion,
                    action: "DELETED",
                    previousData: JsonSerializer.Serialize(row),
                    newData: null,
                    versionBefore: row.Version,
                    versionAfter: row.Version,
                    actorId, actorRole, ct);

                // 7. EntitlementConfigSoftDeleted outbox on the natural-key stream.
                var streamId = $"entitlement-config-{closedRow.EntitlementType}-{closedRow.AgreementCode}-{closedRow.OkVersion}";
                var softDeletedEvent = new EntitlementConfigSoftDeleted
                {
                    ConfigId = closedRow.ConfigId,
                    EntitlementType = closedRow.EntitlementType,
                    AgreementCode = closedRow.AgreementCode,
                    OkVersion = closedRow.OkVersion,
                    EffectiveFrom = closedRow.EffectiveFrom,
                    EffectiveTo = closedRow.EffectiveTo,
                    RowVersion = closedRow.Version,
                    ActorId = actorId,
                    ActorRole = actorRole,
                    CorrelationId = actor.CorrelationId,
                };
                // S44 TASK-4413: capture outbox_id for audit_projection insert
                // (ADR-026 D2 sync-in-tx projection write — atomic with the
                // entitlement_configs close + outbox row per ADR-018 D3/D13).
                var outboxId = await outbox.EnqueueAndReturnIdAsync(conn, tx, streamId, softDeletedEvent, ct);

                var auditCtx = new AuditProjectionContext(
                    ActorId: actor.ActorId,
                    ActorPrimaryOrgId: actor.OrgId,
                    CorrelationId: actor.CorrelationId,
                    OccurredAt: new DateTimeOffset(softDeletedEvent.OccurredAt));
                var auditRow = softDeletedMapper.Map(softDeletedEvent, auditCtx);
                await auditRepo.InsertAsync(conn, tx, softDeletedEvent.EventId, outboxId, softDeletedEvent.EventType, auditRow, auditCtx, ct);

                await tx.CommitAsync(ct);

                // 204 No Content — no body, no ETag header (resource gone).
                return Results.NoContent();
            }
            catch
            {
                if (tx.Connection is not null)
                    await tx.RollbackAsync(ct);
                throw;
            }
        }).RequireAuthorization("GlobalAdminOnly");

        return app;
    }

    // ── Response Mapping ──

    /// <summary>
    /// Map the entity to the admin response shape — surfaces <c>version</c> for the frontend
    /// to compose <c>If-Match</c> on subsequent mutations (ADR-019 D2.2 propagation).
    /// </summary>
    private static object MapToResponse(EntitlementConfig c) => new
    {
        configId = c.ConfigId,
        entitlementType = c.EntitlementType,
        agreementCode = c.AgreementCode,
        okVersion = c.OkVersion,
        annualQuota = c.AnnualQuota,
        accrualModel = c.AccrualModel,
        resetMonth = c.ResetMonth,
        carryoverMax = c.CarryoverMax,
        proRateByPartTime = c.ProRateByPartTime,
        isPerEpisode = c.IsPerEpisode,
        minAge = c.MinAge,
        description = c.Description,
        // S73 / TASK-7301 (R2): served so the admin editor (TASK-7302) can round-trip the flag.
        fullDayOnly = c.FullDayOnly,
        effectiveFrom = c.EffectiveFrom,
        effectiveTo = c.EffectiveTo,
        version = c.Version,
    };

    // ── Request DTOs (co-located) ──

    /// <summary>
    /// POST request body. <see cref="EffectiveFrom"/> is OPTIONAL — when omitted, the endpoint
    /// defaults it to today (preserves the common admin-create-now case). When supplied, the
    /// same-day-only-edit validator (refinement L127) requires it == today.
    /// </summary>
    private sealed class CreateEntitlementConfigRequest
    {
        public required string EntitlementType { get; init; }
        public required string AgreementCode { get; init; }
        public required string OkVersion { get; init; }
        public required decimal AnnualQuota { get; init; }
        public required string AccrualModel { get; init; }
        public required int ResetMonth { get; init; }
        public required decimal CarryoverMax { get; init; }
        public required bool ProRateByPartTime { get; init; }
        public required bool IsPerEpisode { get; init; }
        public int? MinAge { get; init; }
        public string? Description { get; init; }

        // S73 / TASK-7301 (R2): the full-day-only day-shape flag. NOT required — an ABSENT
        // field deserializes to false, which the FullDayOnlyGuard rejects (422) for
        // CARE_DAY/SENIOR_DAY per the D-A owner ruling ("flag false/absent → 422").
        public bool FullDayOnly { get; init; }

        // S30 / TASK-3007: optional same-day-only field. Defaulted to today by the endpoint
        // when omitted; rejected with 422 when supplied with any other value.
        public DateOnly? EffectiveFrom { get; init; }
    }

    /// <summary>
    /// PUT request body. <see cref="EffectiveFrom"/> is REQUIRED and must equal today per the
    /// same-day-only-edit validator (refinement L127, cycle 3 symmetric forbid).
    /// reset_month / accrual_model must match the predecessor (immutability guard, PLAN-s30
    /// Callout 8 + Q1 sub-fork (i) freeze); 422 otherwise.
    /// </summary>
    private sealed class UpdateEntitlementConfigRequest
    {
        public required string EntitlementType { get; init; }
        public required string AgreementCode { get; init; }
        public required string OkVersion { get; init; }
        public required decimal AnnualQuota { get; init; }
        public required string AccrualModel { get; init; }
        public required int ResetMonth { get; init; }
        public required decimal CarryoverMax { get; init; }
        public required bool ProRateByPartTime { get; init; }
        public required bool IsPerEpisode { get; init; }
        public int? MinAge { get; init; }
        public string? Description { get; init; }

        // S73 / TASK-7301 (R2 version-survival): the editor PUTs the full config shape and must
        // round-trip the flag. ABSENT deserializes to false → 422 for CARE_DAY/SENIOR_DAY (the
        // FullDayOnlyGuard) — an unrelated-field edit therefore always re-asserts TRUE.
        public bool FullDayOnly { get; init; }

        // S30 / TASK-3007: required. Validator rejects != today with 422.
        public required DateOnly EffectiveFrom { get; init; }
    }
}
