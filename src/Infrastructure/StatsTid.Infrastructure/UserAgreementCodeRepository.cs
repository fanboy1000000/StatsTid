using Npgsql;
using StatsTid.Infrastructure.Temporal;
using StatsTid.SharedKernel.Exceptions;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Infrastructure;

/// <summary>
/// S34 / TASK-3402 — Phase 4e versioned-history store for the per-user agreement-code
/// assignment (ADR-023 D2 option (b)). Mirrors the S29 <see cref="WageTypeMappingRepository"/>
/// / S33 <see cref="EmployeeProfileRepository"/> bitemporal shape: each row carries
/// <c>effective_from</c> + <c>effective_to</c> under end-exclusive <c>[from, to)</c>
/// semantics (ADR-018 D9) and a monotonic <c>version</c> column for ADR-019 admin-strict
/// If-Match optimistic concurrency. The partial-unique-index
/// <c>idx_user_agreement_codes_live</c> enforces "at most one live (open) row per user".
///
/// <para>
/// <b>Canonical-write contract.</b> All writes to <c>user_agreement_codes</c> MUST flow
/// through this repository. <c>users.agreement_code</c> is a denormalized cache for
/// live-only consumers (JWT mint via <see cref="GetCurrentAsync"/>, current-row reads by
/// Skema/Overtime/Compliance endpoints) meaning "the agreement as of TODAY". <b>Since S138 /
/// TASK-13801 the cache write happens INSIDE <see cref="SupersedeAndCreateAsync"/></b>, sourced
/// from the row covering today (never from the request) and bumping <c>users.version</c> — the
/// ONE client concurrency token of the agreement-code aggregate; the calling endpoint no longer
/// writes the cache itself (pre-S138 it did, in the same tx — TASK-3407). Past-period readers
/// MUST route through <see cref="GetByUserIdAtAsync"/> — never read <c>users.agreement_code</c>
/// for replay-sensitive paths (payroll export effective-date lookup, PCS planner snapshot
/// resolution). Enforced by D-tests asserting cache-canonical agreement after PUT (TASK-3414).
/// </para>
///
/// <para>
/// <b>Step 0b BLOCKER 1 absorption — no SoftDelete method.</b> Soft-delete is
/// semantically meaningless for <c>agreement_code</c>: every user must have an
/// agreement at all times; admins change it, never NULL it. The bitemporal
/// <c>effective_to</c> column stays in the schema (TASK-3401) for the Case C
/// predecessor-close path of <see cref="SupersedeAndCreateAsync"/>; the public
/// repository surface does not expose a soft-delete method.
/// </para>
///
/// <para>
/// <b>Atomic-outbox contract (ADR-018 D5).</b> <see cref="SupersedeAndCreateAsync"/> is
/// a <c>(conn, tx)</c> overload — the endpoint owns the transaction, threading audit +
/// outbox writes into the same atomic unit. Read methods are self-managed.
/// </para>
/// </summary>
public sealed class UserAgreementCodeRepository
{
    private readonly DbConnectionFactory _dbFactory;

    public UserAgreementCodeRepository(DbConnectionFactory dbFactory)
    {
        _dbFactory = dbFactory;
    }

    // ------------------------------------------------------------------
    // Reads — self-managed connections. Live + as-of + with-version flavors.
    // ------------------------------------------------------------------

    /// <summary>
    /// S34 / TASK-3402 — dated lookup of the agreement code in effect for
    /// <paramref name="userId"/> on <paramref name="asOfDate"/>. Returns the
    /// <c>agreement_code</c> of the row whose history window
    /// <c>[effective_from, effective_to)</c> contains <paramref name="asOfDate"/>, or
    /// <c>null</c> if no row covers that date for the user.
    ///
    /// <para>
    /// <b>End-exclusive predicate</b> per ADR-018 D9: a row with
    /// <c>effective_to = '2026-06-01'</c> covers <c>2026-05-31</c> but NOT
    /// <c>2026-06-01</c> — boundary days belong to the successor. Live rows
    /// (<c>effective_to IS NULL</c>) cover every day from <c>effective_from</c> forward.
    /// </para>
    ///
    /// <para>
    /// <b>This is the canonical past-period source.</b> Payroll export effective-date
    /// lookup (ADR-018 D14 export-time pattern) and PCS planner snapshot resolution
    /// MUST route through this method — never read <c>users.agreement_code</c> for
    /// replay-sensitive paths.
    /// </para>
    /// </summary>
    public async Task<string?> GetByUserIdAtAsync(
        string userId, DateOnly asOfDate, CancellationToken ct = default)
    {
        await using var conn = _dbFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT agreement_code
            FROM user_agreement_codes
            WHERE user_id = @userId
              AND effective_from <= @asOfDate
              AND (effective_to IS NULL OR effective_to > @asOfDate)
            """, conn);
        cmd.Parameters.AddWithValue("userId", userId);
        cmd.Parameters.AddWithValue("asOfDate", asOfDate);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is null or DBNull ? null : (string)result;
    }

    /// <summary>
    /// S34 / TASK-3402 — convenience read of the live row's <c>agreement_code</c> for
    /// <paramref name="userId"/>. Returns <c>null</c> when no live row exists.
    ///
    /// <para>
    /// Consumed by JWT mint (TASK-3406 sign-in path) and by live-only endpoint reads
    /// (Skema/Overtime/Compliance "today's" agreement). Equivalent to
    /// <see cref="GetByUserIdAtAsync"/> with <c>asOfDate = today</c> but reads the
    /// partial-unique-index <c>idx_user_agreement_codes_live</c> directly for a tighter
    /// query plan.
    /// </para>
    /// </summary>
    public async Task<string?> GetCurrentAsync(
        string userId, CancellationToken ct = default)
    {
        await using var conn = _dbFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT agreement_code
            FROM user_agreement_codes
            WHERE user_id = @userId AND effective_to IS NULL
            """, conn);
        cmd.Parameters.AddWithValue("userId", userId);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is null or DBNull ? null : (string)result;
    }

    /// <summary>
    /// S34 / TASK-3402 — atomic row + version read of the live row for
    /// <paramref name="userId"/>, used by admin GET handlers (TASK-3407) to stamp the
    /// ETag from the same snapshot whose data they serialize. Returns <c>null</c> when
    /// no live row exists.
    ///
    /// <para>
    /// Mirrors S33 <c>EmployeeProfileRepository.GetByEmployeeIdWithVersionAsync</c>
    /// (Step 7a P2 fix): reading agreement_code and version in separate statements
    /// opens a concurrency window where the response can carry stale data with a newer
    /// ETag and the next admin edit would silently overwrite the racing change.
    /// </para>
    /// </summary>
    public async Task<(string AgreementCode, long Version)?> GetCurrentWithVersionAsync(
        string userId, CancellationToken ct = default)
    {
        await using var conn = _dbFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT agreement_code, version
            FROM user_agreement_codes
            WHERE user_id = @userId AND effective_to IS NULL
            """, conn);
        cmd.Parameters.AddWithValue("userId", userId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return (reader.GetString(0), reader.GetInt64(1));
    }

    // ------------------------------------------------------------------
    // Writes — atomic-outbox (conn, tx) overload only (ADR-018 D5).
    // ADR-020 D2 3-case routing under SELECT ... FOR UPDATE.
    // ------------------------------------------------------------------

    /// <summary>
    /// S34 / TASK-3402, generalized by S138 / TASK-13801 (ADR-040 D8 as amended 2026-09-02) — the
    /// canonical dated write for a user's agreement-code assignment: records a change AT ANY
    /// PAST-OR-TODAY DATE, splitting the row that covers that date. Routing is the pure
    /// <see cref="TemporalWriteRouter"/> (case table A / B' / C' / E / G / T; DB-free matrix tests)
    /// applied to the LOCK-HELD snapshot of the whole timeline (ADR-020 D2: the decision is made on
    /// the locked rows, never on a pre-lock read). The sibling
    /// <see cref="EmployeeProfileRepository.SupersedeAndCreateAsync"/> carries the shared
    /// plain-language contract; what differs here is the TOKEN and the CACHE.
    ///
    /// <para>
    /// <b>One concurrency token per aggregate — for agreement codes it is <c>users.version</c>.</b>
    /// The client never sees <c>user_agreement_codes.version</c> (no GET stamps it as an ETag);
    /// what the admin holds is the users ETag, which the users PUT already validates before calling
    /// this method. So EVERY timeline write here — including a history-only correction that leaves
    /// the code as of today unchanged — bumps <c>users.version</c> atomically with the agreement-row
    /// write (see the cache rule below), and the endpoint stamps
    /// <see cref="SaveUserAgreementCodeResult.UsersVersionAfter"/> as the new ETag and writes the
    /// <c>users_audit</c> row for that transition. Two admins backdating against the same users
    /// ETag serialize and the second gets a 412. The agreement rows' own <c>version</c> column keeps
    /// its repository-internal per-row semantics (a content edit bumps the edited row; a successor
    /// inherits <c>predecessor + 1</c>) because <c>user_agreement_codes_audit.version_before/after</c>
    /// narrate that row version — its existing contract. <paramref name="expectedVersion"/> keeps
    /// its pre-S138 defence-in-depth meaning: the OPEN row's version the caller observed under its
    /// own lock (the users PUT passes the FOR-UPDATE'd predecessor version); a mismatch is still 412.
    /// </para>
    ///
    /// <para>
    /// <b>Cache rule.</b> <c>users.agreement_code</c> means "the agreement as of TODAY" (it feeds
    /// the login token and ~200 live-only reads). After the row write this method re-reads the row
    /// covering today and writes <c>UPDATE users SET agreement_code = &lt;that code&gt;, version =
    /// version + 1</c> — never the REQUEST value, so a historical-only correction leaves the cached
    /// code untouched by construction while still moving the token. For a today-covering write the
    /// effect is byte-identical to the pre-S138 endpoint cache write (the new code as of today).
    /// Not gated on <c>is_active</c> — HR corrects departed employees' history. The repository does
    /// not know the actor, so it returns the users version pair + old/new cached value for the
    /// endpoint's <c>users_audit</c> row.
    /// </para>
    ///
    /// <para>
    /// <b>Same-values no-op (the S23 shape).</b> A request whose code equals the COVERING row's is
    /// a no-op decided inside the lock after the If-Match check: no row, no version bump (neither
    /// table), <see cref="SaveUserAgreementCodeResult.IsNoOp"/> set. A backdated code equal to
    /// TODAY's code still writes (it changes history) — the pre-S138 endpoint predicate "equal to
    /// the live code ⇒ skip" is exactly the silent no-op this closes (S138 Reviewer discovery 8).
    /// </para>
    ///
    /// <para>
    /// <b>Locking, futures, the employment floor, T.</b> As for the profile writer: one
    /// <c>SELECT … FOR UPDATE</c> over the whole timeline, open row first
    /// (<see cref="LockTimelineAsync"/>), so writers serialize on the open row and gap-inserters on
    /// the history rows; <c>from &gt; today</c> is refused before any lock (owner ruling: future-
    /// dating is Increment 4); the caller-supplied <c>req.EmploymentStartDate</c> floors the date
    /// (date-free refusal); case T re-creates an open row at the repository level. The unique
    /// indexes stay the collision backstop — every INSERT path re-throws 23505 as
    /// <see cref="ConcurrentSeedConflictException"/> (the S35 catch, generalized from Case A).
    /// </para>
    ///
    /// <para>
    /// <b>Atomic-outbox contract (ADR-018 D5).</b> Caller owns the transaction (ReadCommitted or
    /// stricter); this method writes <c>user_agreement_codes</c> and <c>users</c>. The endpoint
    /// emits audit + outbox rows in the same tx from the result
    /// (<see cref="SaveUserAgreementCodeResult.Outcome"/> / <see cref="SaveUserAgreementCodeResult.Kind"/>,
    /// the covering pre-image, the new interval, the users version pair).
    /// </para>
    /// </summary>
    /// <exception cref="TemporalWriteRejectedException">
    /// <see cref="TemporalWriteRejection.FutureDated"/> when <paramref name="req"/><c>.EffectiveFrom</c>
    /// is after today (UTC); <see cref="TemporalWriteRejection.PrecedesEmploymentStart"/> when the
    /// caller supplied <c>req.EmploymentStartDate</c> and the date precedes it. Raised before any
    /// lock; the endpoint maps to a date-free 422.
    /// </exception>
    /// <exception cref="OptimisticConcurrencyException">
    /// <paramref name="expectedVersion"/> non-null and (a) no open row exists
    /// (<c>ActualVersion = null</c>) or (b) the open row's <c>version</c> differs. Endpoint maps to 412.
    /// </exception>
    /// <exception cref="ConcurrentSeedConflictException">
    /// An INSERT lost a race on <c>idx_user_agreement_codes_live</c> / <c>idx_user_agreement_codes_history</c>
    /// (unique-violation 23505). The transaction is aborted; refresh and retry. Endpoint maps to 409.
    /// </exception>
    public async Task<SaveUserAgreementCodeResult> SupersedeAndCreateAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        UserAgreementCodeSupersedeRequest req, long? expectedVersion,
        CancellationToken ct = default)
    {
        // "Today" is UTC — the endpoints' validators use the same clock.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // 0. Pure refusals BEFORE any lock — nothing to roll back, nothing to contend on.
        if (TemporalWriteRouter.IsFutureDated(req.EffectiveFrom, today))
            throw new TemporalWriteRejectedException(TemporalWriteRejection.FutureDated, "agreement-code");
        if (TemporalWriteRouter.PrecedesEmploymentStart(req.EffectiveFrom, req.EmploymentStartDate))
            throw new TemporalWriteRejectedException(TemporalWriteRejection.PrecedesEmploymentStart, "agreement-code");

        // 1. Lock the whole timeline (open row first).
        var timeline = await LockTimelineAsync(conn, tx, req.UserId, ct);
        var live = timeline.FirstOrDefault(r => r.EffectiveTo is null);

        // 2. Repository-internal If-Match on the open row (defence in depth — the CLIENT token is
        //    users.version, validated by the endpoint before it calls us).
        if (expectedVersion is not null)
        {
            if (live is null)
            {
                throw new OptimisticConcurrencyException(
                    $"No live agreement-code assignment exists for user_id='{req.UserId}', " +
                    $"but caller sent If-Match: \"{expectedVersion.Value}\"; refresh and retry.",
                    expectedVersion: expectedVersion,
                    actualVersion: null);
            }
            if (live.Version != expectedVersion.Value)
            {
                throw new OptimisticConcurrencyException(
                    $"User agreement-code assignment version is {live.Version}, but caller sent " +
                    $"If-Match: \"{expectedVersion.Value}\"; refresh and retry.",
                    expectedVersion: expectedVersion,
                    actualVersion: live.Version);
            }
        }

        // 3. Route on the locked snapshot (pure); match the anchor back by start date.
        var decision = TemporalWriteRouter.Decide(
            timeline.Select(r => new TemporalInterval(r.EffectiveFrom, r.EffectiveTo)),
            req.EffectiveFrom, today);
        if (decision.Case == TemporalWriteCase.RejectedFutureDated)
        {
            // Unreachable after step 0; kept so the router remains the single authority.
            throw new TemporalWriteRejectedException(TemporalWriteRejection.FutureDated, "agreement-code");
        }
        var anchor = decision.Anchor is { } anchorInterval
            ? timeline.Single(r => r.EffectiveFrom == anchorInterval.From)
            : null;

        // 4. Same-values no-op — against the COVERING row, inside the lock, after If-Match.
        //
        // S138 Step-5a (Codex WARNING, absorbed): the no-op MUST NOT swallow a zero-width
        // reopen. After a same-day create + soft-delete the anchor is a `[from, from)` row
        // covering NO dates; recreating at that date with the SAME agreement code still has
        // real work to do — re-extend the row (the router's ReopensZeroWidthAnchor decision,
        // ADR-020 D2 Case C). Equality is about VALUES; this branch is about COVERAGE.
        if (anchor is not null
            && !decision.ReopensZeroWidthAnchor
            && string.Equals(anchor.AgreementCode, req.AgreementCode, StringComparison.Ordinal))
        {
            return new SaveUserAgreementCodeResult(anchor.AssignmentId, anchor.Version, SaveUserAgreementCodeOutcome.NoOp)
            {
                Kind = TemporalWriteKind.NoOp,
                IsNoOp = true,
                NewEffectiveFrom = anchor.EffectiveFrom,
                NewEffectiveTo = anchor.EffectiveTo,
                Covering = anchor,
            };
        }

        // 5. Execute the case. Every INSERT path shares the unique-violation backstop.
        SaveUserAgreementCodeResult result;
        try
        {
            result = decision.Case switch
            {
                TemporalWriteCase.Create or TemporalWriteCase.InsertTrailing
                    => await ExecuteOpenInsertAsync(conn, tx, req, decision, timeline, ct),
                TemporalWriteCase.UpdateInPlace
                    => await ExecuteUpdateInPlaceAsync(conn, tx, req, decision, anchor!, ct),
                TemporalWriteCase.SplitCovering
                    => await ExecuteSplitAsync(conn, tx, req, decision, anchor!, ct),
                TemporalWriteCase.InsertBeforeFirst or TemporalWriteCase.InsertInGap
                    => await ExecuteGapInsertAsync(conn, tx, req, decision, ct),
                _ => throw new InvalidOperationException($"Unhandled TemporalWriteCase '{decision.Case}'."),
            };
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            throw new ConcurrentSeedConflictException(req.UserId);
        }

        // 6. Cache + token — users.agreement_code follows the row covering TODAY (never the
        //    request); users.version moves on every timeline write.
        var cache = await RefreshAgreementCodeCacheAsync(conn, tx, req.UserId, today, ct);
        return result with
        {
            UsersVersionBefore = cache.VersionBefore,
            UsersVersionAfter = cache.VersionAfter,
            PreviousAgreementCodeCache = cache.PreviousValue,
            NewAgreementCodeCache = cache.NewValue,
        };
    }

    // ------------------------------------------------------------------
    // Private helpers — the S138 timeline lock, the four case executors, the row primitives
    // (insert / update-in-place / close) and the users cache + token write. Mirrors
    // EmployeeProfileRepository's S138 shape; the differences (per-row version semantics, the
    // unconditional users.version bump) are the two aggregates' different token contracts.
    // ------------------------------------------------------------------

    /// <summary>
    /// Locks EVERY row of the user's agreement-code timeline via <c>SELECT … FOR UPDATE</c>,
    /// open row first then history ascending (PostgreSQL locks in output order, so all writers
    /// share one lock order: no deadlock between two of them, and gap-inserters serialize on the
    /// history rows). Re-entrant with the users PUT's own FOR-UPDATE pre-read of the open row.
    /// Returns an empty list when the user has no rows at all (case A).
    /// </summary>
    private static async Task<IReadOnlyList<UserAgreementCodeRowPreImage>> LockTimelineAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string userId, CancellationToken ct)
    {
        await using var lockCmd = new NpgsqlCommand(
            """
            SELECT assignment_id, agreement_code, effective_from, effective_to, version
            FROM user_agreement_codes
            WHERE user_id = @userId
            ORDER BY (effective_to IS NULL) DESC, effective_from
            FOR UPDATE
            """, conn, tx);
        lockCmd.Parameters.AddWithValue("userId", userId);
        var rows = new List<UserAgreementCodeRowPreImage>();
        await using var reader = await lockCmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new UserAgreementCodeRowPreImage(
                AssignmentId: reader.GetGuid(0),
                AgreementCode: reader.GetString(1),
                EffectiveFrom: reader.GetFieldValue<DateOnly>(2),
                EffectiveTo: reader.IsDBNull(3) ? null : reader.GetFieldValue<DateOnly>(3),
                Version: reader.GetInt64(4)));
        }
        return rows;
    }

    /// <summary>
    /// Cases A and T — INSERT the (only) open row at max(version over the user's rows) + 1
    /// (1 on an empty timeline — the S34 Case-A baseline).
    /// </summary>
    private static async Task<SaveUserAgreementCodeResult> ExecuteOpenInsertAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, UserAgreementCodeSupersedeRequest req,
        TemporalWriteDecision decision, IReadOnlyList<UserAgreementCodeRowPreImage> timeline,
        CancellationToken ct)
    {
        var nextVersion = timeline.Count == 0 ? 1L : timeline.Max(r => r.Version) + 1;
        var (newId, newVersion) = await InsertRowAsync(
            conn, tx, req, decision.NewEffectiveFrom, decision.NewEffectiveTo, nextVersion, ct);
        return new SaveUserAgreementCodeResult(newId, newVersion, SaveUserAgreementCodeOutcome.Created)
        {
            Kind = TemporalWriteRouter.KindOf(decision),
            NewEffectiveFrom = decision.NewEffectiveFrom,
            NewEffectiveTo = decision.NewEffectiveTo,
        };
    }

    /// <summary>
    /// Case B' — UPDATE the row that starts on the requested date (open or history); its own
    /// version bumps (a content edit of that row — the audit narrates before → after).
    /// </summary>
    private static async Task<SaveUserAgreementCodeResult> ExecuteUpdateInPlaceAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, UserAgreementCodeSupersedeRequest req,
        TemporalWriteDecision decision, UserAgreementCodeRowPreImage anchor, CancellationToken ct)
    {
        var (id, version) = await UpdateRowAsync(conn, tx, req, anchor.AssignmentId, decision.NewEffectiveTo, ct);
        return new SaveUserAgreementCodeResult(id, version, SaveUserAgreementCodeOutcome.Updated)
        {
            Kind = TemporalWriteKind.Updated,
            NewEffectiveFrom = decision.NewEffectiveFrom,
            NewEffectiveTo = decision.NewEffectiveTo,
            Covering = anchor,
        };
    }

    /// <summary>
    /// Case C' — close the covering row at the requested date (version untouched: a close is
    /// lifecycle, not a content edit) and INSERT <c>[from, covering.oldTo)</c> at
    /// <c>covering.Version + 1</c> — the successor inherits the predecessor's version + 1 whether
    /// the covering row was open (the S34 Case C rule) or history (the audit chain keeps narrating
    /// "predecessor v → successor v+1").
    /// </summary>
    private static async Task<SaveUserAgreementCodeResult> ExecuteSplitAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, UserAgreementCodeSupersedeRequest req,
        TemporalWriteDecision decision, UserAgreementCodeRowPreImage anchor, CancellationToken ct)
    {
        await ClosePredecessorAsync(conn, tx, anchor.AssignmentId, decision.NewEffectiveFrom, ct);
        var (newId, newVersion) = await InsertRowAsync(
            conn, tx, req, decision.NewEffectiveFrom, decision.NewEffectiveTo, anchor.Version + 1, ct);
        return new SaveUserAgreementCodeResult(newId, newVersion, SaveUserAgreementCodeOutcome.Superseded)
        {
            Kind = TemporalWriteRouter.KindOf(decision),
            NewEffectiveFrom = decision.NewEffectiveFrom,
            NewEffectiveTo = decision.NewEffectiveTo,
            Covering = anchor,
        };
    }

    /// <summary>
    /// Cases E and G — INSERT a history row into a gap at version 1 (no predecessor); nothing is closed.
    /// </summary>
    private static async Task<SaveUserAgreementCodeResult> ExecuteGapInsertAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, UserAgreementCodeSupersedeRequest req,
        TemporalWriteDecision decision, CancellationToken ct)
    {
        var (newId, newVersion) = await InsertRowAsync(
            conn, tx, req, decision.NewEffectiveFrom, decision.NewEffectiveTo, 1L, ct);
        return new SaveUserAgreementCodeResult(newId, newVersion, SaveUserAgreementCodeOutcome.Inserted)
        {
            Kind = TemporalWriteRouter.KindOf(decision),
            NewEffectiveFrom = decision.NewEffectiveFrom,
            NewEffectiveTo = decision.NewEffectiveTo,
        };
    }

    /// <summary>
    /// INSERT one row with the caller-supplied interval and version. <c>assignment_id</c> is
    /// generated client-side (S29 WTM + S33 precedent) so the endpoint can put it in the outbox
    /// event body. The two unique indexes are the collision backstop; the caller translates 23505.
    /// </summary>
    private static async Task<(Guid AssignmentId, long Version)> InsertRowAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, UserAgreementCodeSupersedeRequest req,
        DateOnly effectiveFrom, DateOnly? effectiveTo, long version, CancellationToken ct)
    {
        var newAssignmentId = Guid.NewGuid();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO user_agreement_codes (
                assignment_id, user_id, agreement_code,
                effective_from, effective_to, version)
            VALUES (
                @assignmentId, @userId, @agreementCode,
                @effectiveFrom, @effectiveTo, @version)
            RETURNING assignment_id, version
            """, conn, tx);
        cmd.Parameters.AddWithValue("assignmentId", newAssignmentId);
        cmd.Parameters.AddWithValue("userId", req.UserId);
        cmd.Parameters.AddWithValue("agreementCode", req.AgreementCode);
        cmd.Parameters.AddWithValue("effectiveFrom", effectiveFrom);
        cmd.Parameters.Add(new NpgsqlParameter("effectiveTo", NpgsqlTypes.NpgsqlDbType.Date)
        {
            Value = effectiveTo is { } to ? to : DBNull.Value,
        });
        cmd.Parameters.AddWithValue("version", version);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            // Defense-in-depth — INSERT ... RETURNING always yields one row on success.
            throw new InvalidOperationException(
                $"InsertRowAsync produced no row for user_id='{req.UserId}' " +
                $"at effective_from='{effectiveFrom:yyyy-MM-dd}'.");
        }
        return (reader.GetGuid(0), reader.GetInt64(1));
    }

    /// <summary>
    /// Case B' — UPDATE the (locked) row that starts on the requested date: refresh
    /// <c>agreement_code</c>, set <c>effective_to</c> to the decided end (unchanged for a normal
    /// in-place edit; re-extended for a zero-width row — the ADR-020 D2 Case C reopen), bump
    /// <c>version</c>, stamp <c>updated_at</c>. <c>assignment_id</c> and <c>effective_from</c> are
    /// immutable across in-place edits.
    /// </summary>
    private static async Task<(Guid AssignmentId, long Version)> UpdateRowAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, UserAgreementCodeSupersedeRequest req,
        Guid assignmentId, DateOnly? effectiveTo, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE user_agreement_codes SET
                agreement_code = @agreementCode,
                effective_to = @effectiveTo,
                version = version + 1,
                updated_at = NOW()
            WHERE assignment_id = @assignmentId
            RETURNING assignment_id, version
            """, conn, tx);
        cmd.Parameters.AddWithValue("assignmentId", assignmentId);
        cmd.Parameters.AddWithValue("agreementCode", req.AgreementCode);
        cmd.Parameters.Add(new NpgsqlParameter("effectiveTo", NpgsqlTypes.NpgsqlDbType.Date)
        {
            Value = effectiveTo is { } to ? to : DBNull.Value,
        });
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            // Defense-in-depth — unreachable while FOR UPDATE holds the lock.
            throw new InvalidOperationException(
                $"UpdateRowAsync produced no row for assignment_id='{assignmentId}'; " +
                "FOR UPDATE invariant violated.");
        }
        return (reader.GetGuid(0), reader.GetInt64(1));
    }

    /// <summary>
    /// Case C' — close the covering row by stamping <c>effective_to = closeDate</c> under
    /// end-exclusive semantics (ADR-018 D9 — its history window becomes
    /// <c>[effective_from, closeDate)</c>). The version column is NOT bumped: a close is a
    /// lifecycle event, not a content edit (mirrors S22 ArchiveProfileAsync + S29 WTM CloseRowAsync
    /// + S33 EmployeeProfile ClosePredecessorAsync). Caller must already hold the timeline lock.
    /// </summary>
    private static async Task ClosePredecessorAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid assignmentId, DateOnly closeDate, CancellationToken ct)
    {
        await using var closeCmd = new NpgsqlCommand(
            "UPDATE user_agreement_codes SET effective_to = @closeDate WHERE assignment_id = @assignmentId",
            conn, tx);
        closeCmd.Parameters.AddWithValue("closeDate", closeDate);
        closeCmd.Parameters.AddWithValue("assignmentId", assignmentId);
        await closeCmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// The cache + token write (see <see cref="SupersedeAndCreateAsync"/>): re-read the code of the
    /// row covering TODAY after the write, lock the users row, then
    /// <c>UPDATE users SET agreement_code = &lt;today's code&gt;, version = version + 1</c> — the
    /// version bump is UNCONDITIONAL (every timeline write moves the client token), the cached
    /// value only changes when today's row changed. If no row covers today (cannot happen for
    /// agreement codes, which have no soft-delete, but guarded) the cached value is kept via
    /// COALESCE. Not gated on <c>is_active</c>.
    /// </summary>
    private static async Task<(long VersionBefore, long VersionAfter, string PreviousValue, string NewValue)>
        RefreshAgreementCodeCacheAsync(
            NpgsqlConnection conn, NpgsqlTransaction tx, string userId, DateOnly today, CancellationToken ct)
    {
        string? todayCode;
        await using (var todayCmd = new NpgsqlCommand(
            """
            SELECT agreement_code
            FROM user_agreement_codes
            WHERE user_id = @userId
              AND effective_from <= @today
              AND (effective_to IS NULL OR effective_to > @today)
            """, conn, tx))
        {
            todayCmd.Parameters.AddWithValue("userId", userId);
            todayCmd.Parameters.AddWithValue("today", today);
            var scalar = await todayCmd.ExecuteScalarAsync(ct);
            todayCode = scalar is null || scalar is DBNull ? null : (string)scalar;
        }

        string cachedCode;
        long cachedVersion;
        await using (var usersCmd = new NpgsqlCommand(
            """
            SELECT agreement_code, version
            FROM users
            WHERE user_id = @userId
            FOR UPDATE
            """, conn, tx))
        {
            usersCmd.Parameters.AddWithValue("userId", userId);
            await using var reader = await usersCmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                // The user_id FK guarantees the users row; reaching here is a programming error.
                throw new InvalidOperationException(
                    $"users row for user_id='{userId}' not found while refreshing the agreement_code cache.");
            }
            cachedCode = reader.GetString(0);
            cachedVersion = reader.GetInt64(1);
        }

        await using var updateCmd = new NpgsqlCommand(
            """
            UPDATE users
               SET agreement_code = COALESCE(@agreementCode, agreement_code),
                   version = version + 1,
                   updated_at = NOW()
             WHERE user_id = @userId
            RETURNING agreement_code, version
            """, conn, tx);
        updateCmd.Parameters.AddWithValue("userId", userId);
        updateCmd.Parameters.Add(new NpgsqlParameter("agreementCode", NpgsqlTypes.NpgsqlDbType.Text)
        {
            Value = (object?)todayCode ?? DBNull.Value,
        });
        await using var updated = await updateCmd.ExecuteReaderAsync(ct);
        if (!await updated.ReadAsync(ct))
        {
            throw new InvalidOperationException(
                $"users cache write for user_id='{userId}' matched no row; FOR UPDATE invariant violated.");
        }
        return (cachedVersion, updated.GetInt64(1), cachedCode, updated.GetString(0));
    }
}

// ------------------------------------------------------------------
// Request + result records — colocated with the repository per S33 EmployeeProfile +
// S29 WTM precedent.
// ------------------------------------------------------------------

/// <summary>
/// S34 / TASK-3402 — payload for
/// <see cref="UserAgreementCodeRepository.SupersedeAndCreateAsync"/>. Drives the routing via the
/// explicit <see cref="EffectiveFrom"/> date (the endpoint reads the clock — no clock dependency
/// in the repo for the DATE; seeders + admin-POST + admin-PUT supply it directly).
///
/// <para>
/// <b>S138 / TASK-13801 addition (trailing, defaulted — every 3-argument construction compiles
/// unchanged).</b> <see cref="EmploymentStartDate"/> is the caller-supplied employment-start
/// floor: when set, a date before it is refused with
/// <see cref="Temporal.TemporalWriteRejection.PrecedesEmploymentStart"/> (date-free). Caller-
/// supplied rather than read from <c>users</c> because the admin user-create POST legitimately
/// writes the first row at today for a hire whose start date is in the future.
/// </para>
/// </summary>
public sealed record UserAgreementCodeSupersedeRequest(
    string UserId,
    string AgreementCode,
    DateOnly EffectiveFrom,
    DateOnly? EmploymentStartDate = null);

/// <summary>
/// S138 / TASK-13801 — the PRE-IMAGE of one <c>user_agreement_codes</c> row as it stood under the
/// lock before the write. Carried on <see cref="SaveUserAgreementCodeResult.Covering"/> so the
/// endpoint sources audit <c>previous_data</c>, the mutation predicate and the Superseded event's
/// predecessor fields from the row actually touched — never from the open row (for a backdate the
/// two differ). <see cref="EffectiveTo"/> null = it was the open row.
/// </summary>
public sealed record UserAgreementCodeRowPreImage(
    Guid AssignmentId,
    string AgreementCode,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    long Version);

/// <summary>
/// S34 / TASK-3402 — result of
/// <see cref="UserAgreementCodeRepository.SupersedeAndCreateAsync"/>. <see cref="Outcome"/>
/// discriminates the coarse, event-oriented branch so the endpoint emits the correct event type
/// (<c>UserAgreementCodeChanged</c> always on a real write; <c>UserAgreementCodeSuperseded</c>
/// additionally on C') and stamps the right audit <c>action</c>; the S138 members below carry what
/// a temporal write additionally needs. All S138 members are init-only with defaults — the
/// 3-argument construction and every existing reader compile unchanged.
/// </summary>
/// <param name="AssignmentId">The <c>assignment_id</c> of the row this call produced or edited: a
/// fresh UUID for every INSERT case, the anchor's id for an in-place edit or a no-op.</param>
/// <param name="Version">The post-write <c>version</c> column value on the row identified by
/// <see cref="AssignmentId"/> — the repository-internal ROW version that
/// <c>user_agreement_codes_audit.version_after</c> records (its existing contract). A / T → max + 1
/// (1 on an empty timeline); B' → <c>prior + 1</c>; C' → <c>covering.Version + 1</c>; E / G → 1;
/// no-op → the covering row's unchanged version. This is NOT the client token — that is
/// <see cref="UsersVersionAfter"/>.</param>
/// <param name="Outcome">Which branch the call routed through.</param>
public sealed record SaveUserAgreementCodeResult(
    Guid AssignmentId,
    long Version,
    SaveUserAgreementCodeOutcome Outcome)
{
    /// <summary>S138 — the fine-grained case (A / B' / C'-open / C'-history / E / G / T / no-op).</summary>
    public TemporalWriteKind Kind { get; init; } = DefaultKind(Outcome);

    /// <summary>S138 — true when the request equalled the covering row and nothing was written
    /// (no row, no version bump on either table); the endpoint skips audit/events/worklist.</summary>
    public bool IsNoOp { get; init; }

    /// <summary>S138 — start of the interval the write produced / edited (the request date; for a
    /// no-op the covering row's start).</summary>
    public DateOnly? NewEffectiveFrom { get; init; }

    /// <summary>S138 — end (exclusive) of that interval; <c>null</c> = the row is open. For C' this
    /// is where the covering row USED to end — the worklist interval is
    /// <c>[NewEffectiveFrom, NewEffectiveTo)</c>.</summary>
    public DateOnly? NewEffectiveTo { get; init; }

    /// <summary>S138 — the covering row's pre-image (B' / C' / no-op); <c>null</c> for A / E / G / T.</summary>
    public UserAgreementCodeRowPreImage? Covering { get; init; }

    /// <summary>S138 — <c>users.version</c> before the token/cache write (the client token the
    /// caller validated); <c>null</c> only on a no-op.</summary>
    public long? UsersVersionBefore { get; init; }

    /// <summary>S138 — <c>users.version</c> after the write — the NEW client token the endpoint
    /// stamps as ETag; <c>null</c> only on a no-op.</summary>
    public long? UsersVersionAfter { get; init; }

    /// <summary>S138 — the <c>users.agreement_code</c> value before the write; <c>null</c> only on a no-op.</summary>
    public string? PreviousAgreementCodeCache { get; init; }

    /// <summary>S138 — the <c>users.agreement_code</c> value after the write (the row covering
    /// today's code — equal to the previous value after a historical-only correction);
    /// <c>null</c> only on a no-op.</summary>
    public string? NewAgreementCodeCache { get; init; }

    /// <summary>S138 — true when this write touched the <c>users</c> row (every non-no-op write
    /// does; the endpoint then owes a <c>users_audit</c> row for the version transition).</summary>
    public bool UsersCacheWritten => UsersVersionAfter is not null;

    private static TemporalWriteKind DefaultKind(SaveUserAgreementCodeOutcome outcome) => outcome switch
    {
        SaveUserAgreementCodeOutcome.Created => TemporalWriteKind.Created,
        SaveUserAgreementCodeOutcome.Updated => TemporalWriteKind.Updated,
        SaveUserAgreementCodeOutcome.Superseded => TemporalWriteKind.Superseded,
        SaveUserAgreementCodeOutcome.Inserted => TemporalWriteKind.InsertedInGap,
        SaveUserAgreementCodeOutcome.NoOp => TemporalWriteKind.NoOp,
        _ => TemporalWriteKind.Created,
    };
}

/// <summary>
/// S34 / TASK-3402 — routing discriminator, read by the endpoints to map each case to its outbox
/// event type and audit <c>action</c>; extended additively in S138 / TASK-13801:
/// <list type="bullet">
///   <item><description><see cref="Created"/> → <c>UserAgreementCodeAssigned</c> / CREATED (a new
///     OPEN row with no predecessor closed: case A, and case T after a trailing gap).</description></item>
///   <item><description><see cref="Updated"/> → <c>UserAgreementCodeChanged</c> / UPDATED (case B':
///     the row starting on the date edited in place — open or history).</description></item>
///   <item><description><see cref="Superseded"/> → <c>UserAgreementCodeChanged</c> +
///     <c>UserAgreementCodeSuperseded</c> / SUPERSEDED (case C': covering row closed at the date +
///     a new row <c>[from, oldTo)</c>; <c>NewEffectiveTo</c> tells open from history).</description></item>
///   <item><description><see cref="Inserted"/> (S138) → <c>UserAgreementCodeChanged</c> / CREATED
///     (cases E / G: a history row inserted into a gap; nothing closed).</description></item>
///   <item><description><see cref="NoOp"/> (S138) → nothing emitted (the S23 shape).</description></item>
/// </list>
/// </summary>
public enum SaveUserAgreementCodeOutcome
{
    /// <summary>Case A (empty timeline) or T (trailing gap): INSERT produced a brand-new open
    /// assignment row.</summary>
    Created,
    /// <summary>Case B': a row started on the requested date; UPDATE-in-place with version bump
    /// (<c>assignment_id</c> and <c>effective_from</c> unchanged).</summary>
    Updated,
    /// <summary>Case C': the covering row was closed at end-exclusive
    /// <c>effective_to = request.EffectiveFrom</c> (version unchanged) and a new row inserted at
    /// <c>covering.Version + 1</c> for the remainder of its old interval.</summary>
    Superseded,
    /// <summary>S138 — cases E / G: a history row was inserted into a gap; no row was closed.</summary>
    Inserted,
    /// <summary>S138 — the request equalled the covering row; nothing was written.</summary>
    NoOp,
}
