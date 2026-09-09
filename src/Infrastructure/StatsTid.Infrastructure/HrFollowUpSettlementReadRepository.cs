using System.Data;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Infrastructure;

/// <summary>
/// S140 / TASK-14003 (refinement B1; HR follow-up register rows HRP-005, HRP-005b, HRP-007,
/// HRP-010) — the SETTLEMENT-FAMILY read repository behind the HR follow-up surface.
///
/// <para>
/// <b>What this is for, in plain language.</b> The settlement machinery hands HR three jobs and
/// had no way to show any of them: a settlement parked for manual review, a departing employee's
/// §26 payout request, and the §21 fifth-week transfer agreement that must be recorded before
/// 31 December. This class answers "what is waiting?" for each. It is the DERIVE-AT-READ half of
/// PAT-026: every status it reports is computed fresh against the live settlement / request /
/// agreement rows — nothing is cached, and no derived flag is stored anywhere.
/// </para>
///
/// <para>
/// <b>STRICTLY READ-ONLY.</b> Every statement in this file is a SELECT. There is no INSERT,
/// UPDATE or DELETE, no outbox enqueue, no audit row, and no advisory lock — a diagnostic list
/// must never mutate the facts it reports, and taking the employee consumption lock would let a
/// read block a settlement. The <c>vacation_settlements</c>, <c>termination_payout_requests</c>
/// and <c>vacation_transfer_agreements</c> tables are read-only from here; their writers are
/// unchanged.
/// </para>
///
/// <para>
/// <b>Org scope — the column that matters.</b> Every list filters on the SUBJECT EMPLOYEE's
/// CURRENT <c>users.primary_org_id</c> (the <c>HrBackdateWorklistRepository</c> shape:
/// <c>AND (@allOrgs OR u.primary_org_id = ANY(@orgIds))</c>), never on an org column stamped onto
/// a settlement / request / agreement row at write time. A stamped org drifts the moment an
/// employee transfers, which would show the OLD organisation's HR a transferred employee and hide
/// them from the NEW one. <c>accessibleOrgIds == null</c> is the GlobalAdmin sentinel (no filter);
/// an EMPTY set is rejected by the endpoint with 403 before it reaches here, and is defended again
/// in each method as an empty result.
/// </para>
///
/// <para>
/// <b>"Today" is never read here.</b> Every business date arrives as a parameter from the
/// endpoint, which reads the injected clock ONCE per request (PAT-028). No statement below uses
/// <c>CURRENT_DATE</c> or <c>NOW()::date</c> for a business date.
/// </para>
///
/// <para>
/// <b>Unbounded by design, declared.</b> None of the three lists paginates or caps its result —
/// the same posture as the existing settlement worklists (<c>payout-pending</c>, the backdate
/// worklist), because a follow-up list that silently truncates would tell HR their queue is
/// shorter than it is, which is the one thing this surface must never do. A GlobalAdmin call
/// therefore has no ceiling, and HRP-010 additionally costs several database round trips PER
/// CANDIDATE employee (the valuation is not expressible as one statement without duplicating it).
/// If these lists ever get big enough to matter, the fix is a bounded response that SAYS it is
/// bounded — a count plus a page — not a silent LIMIT.
/// </para>
/// </summary>
public sealed class HrFollowUpSettlementReadRepository
{
    private readonly DbConnectionFactory _connectionFactory;
    private readonly VacationSettlementService _settlementService;
    private readonly ILogger<HrFollowUpSettlementReadRepository> _logger;

    /// <summary>The only §21-transferable entitlement type (Ordinal — entitlement_type is an identifier).</summary>
    public const string VacationType = "VACATION";

    /// <summary>The single stable <c>cannotCompute</c> reason code (see the contract's rationale:
    /// a code, never the exception text, because the failure messages name dated-history anchors
    /// and an error string on a response body is how a redacted date escapes).</summary>
    public const string CannotComputeReasonValuationFailed = "VALUATION_FAILED";

    /// <summary>
    /// Tolerant snapshot read — the SAME options as the §26 request endpoint's accessor
    /// (<c>TerminationPayoutRequestEndpoints.SnapshotJsonOptions</c>): Web defaults, which is
    /// camelCase-insensitive matching for the camelCase the settlement service writes.
    /// </summary>
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new(JsonSerializerDefaults.Web);

    public HrFollowUpSettlementReadRepository(
        DbConnectionFactory connectionFactory,
        VacationSettlementService settlementService,
        ILogger<HrFollowUpSettlementReadRepository> logger)
    {
        _connectionFactory = connectionFactory;
        _settlementService = settlementService;
        _logger = logger;
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════
    // HRP-005 + HRP-005b — settlements flagged for manual review (two sources, one list)
    // ═══════════════════════════════════════════════════════════════════════════════════════

    // Source `row`: a vacation_settlements row parked in PENDING_REVIEW (ADR-033 D10 — the close
    //   fails closed on the un-adjudicated remainder rather than wrongly forfeiting it). Rows
    //   already carrying review_disposition = 'DEFER' are INCLUDED and labelled. DEFER PARKS a row
    //   as suspected §22 feriehindring without RESOLVING it: the row stays PENDING_REVIEW and still
    //   needs a terminal verb — FERIEHINDRING (which the S79 slice-4 resolve path accepts TODAY) or
    //   FORFEIT. HR has looked at it; the job is not done. That is exactly what the reviewDisposition
    //   label lets the tile say, which is why these rows are labelled rather than filtered out.
    //
    // Source `event` (HRP-005b): a REFUSED termination writes NO settlement row — it emits
    //   SettlementManualReviewFlagged carrying the CONFLICTING year-end row's identity, with a
    //   NULL snapshot and ZERO flagged days. So the discriminator is the CONFLICT SHAPE of the
    //   flag, and BOTH terms are load-bearing: the leaver-deferred emit path also flags with
    //   flaggedDays = 0 when the disposable remainder happens to be 0, and is excluded only by the
    //   snapshot term. The event's identity tuple is joined back to vacation_settlements and the
    //   referenced row must still be ACTIVE (not REVERSED) — the exit from this item is the
    //   HRP-009 reversal, which flips that row.
    //
    //   The canonical `events` table is the source (the same read shape as
    //   SegmentManifestProjectionRebuilder: `data->>'key'` with CAMELCASE keys, because
    //   EventSerializer writes camelCase). Of the FOUR sites that construct this event, exactly
    //   ONE produces the conflict shape — RefuseTerminationConflictAsync; the other three always
    //   pass a non-null snapshot, so the WHERE admits only the refusal. But TWO CALL PATHS reach
    //   that one site (the in-lock pre-check and the 23505 single-settle backstop), so the SAME
    //   conflicting row can be flagged more than once. Hence the collapse: DISTINCT per (employee,
    //   type, year, sequence), age taken from the EARLIEST occurred_at — the first flag is when
    //   HR's job actually started, and a re-flag must not make an old problem look new.
    //
    //   The two sources are NOT disjoint, deliberately. The refusal flags the CONFLICTING ACTIVE
    //   row, and "active" means non-REVERSED — which includes PENDING_REVIEW. So a refused
    //   termination whose conflicting year-end row is ITSELF parked in PENDING_REVIEW yields TWO
    //   items for one tuple: a `row` item carrying the flagged remainder, and an `event` item
    //   carrying 0 days. Both are real and DIFFERENT jobs — disposition the remainder (§34 / §22),
    //   and resolve the termination conflict by reverse-then-re-settle (HRP-009) — so neither may
    //   be dropped. The predicate is the refinement's: "conflict-shaped flags whose referenced row
    //   is still active (not REVERSED)". Narrowing the event branch to `= 'SETTLED'` would collapse
    //   the pair into one item and lose the conflict signal on exactly the tuples that carry two
    //   problems at once. The ORDER BY tiebreaks on `source`, so the pair is stably ordered.
    //
    //   jsonb typing: DEFENCE, not decoration.
    //   * `data->'snapshot' IS NULL` is the correct term TODAY because EventSerializer sets
    //     DefaultIgnoreCondition = WhenWritingNull, so a null Snapshot is OMITTED from the JSON.
    //     The added `jsonb_typeof(...) = 'null'` disjunct keeps the predicate correct if that
    //     option is ever relaxed: a literal `"snapshot": null` is a JSON null, which is NOT SQL
    //     NULL — the classic jsonb trap, and it would silently admit every flagged row.
    //   * flaggedDays is compared NUMERICALLY, not against the string '0', so a decimal serialized
    //     as `0.00` still matches. The type check is inside a CASE rather than a separate AND
    //     because PostgreSQL does NOT guarantee WHERE-clause evaluation order (the planner sorts
    //     quals by cost), so a guard sitting next to a cast does not shield it — CASE does have
    //     defined evaluation order. A non-numeric payload therefore yields NULL and drops the row
    //     instead of raising `invalid input syntax for numeric` and failing the WHOLE list.
    //   * entitlementYear / sequence keep plain `::int` casts in the SELECT list, guarded by
    //     jsonb_typeof in the WHERE. Projection runs after qualification on the scan node, so
    //     non-numeric input cannot reach those casts. A numeric-but-non-integral value WOULD
    //     raise — deliberately: both fields are `int` on the event contract, so a fractional
    //     value is corrupt data, and failing loudly beats rounding a ferieår.
    //   * employeeId / entitlementType are left unguarded. They are `required string` on the
    //     event, and a missing key would yield NULL, fail the equi-join and DROP the flag — a
    //     fail-SILENT path, declared here rather than papered over: SQL offers no cheap fail-loud
    //     alternative inside a set-returning read.
    private const string SelectPendingReviewsSql =
        """
        SELECT 'row'::text            AS source,
               s.employee_id          AS employee_id,
               s.entitlement_type     AS entitlement_type,
               s.entitlement_year     AS entitlement_year,
               s.sequence             AS settlement_sequence,
               s.settlement_state     AS settlement_state,
               s.trigger              AS trigger,
               s.review_disposition   AS review_disposition,
               s.forfeit_days         AS flagged_days,
               s.version              AS version,
               s.created_at           AS age_anchor,
               u.primary_org_id       AS primary_org_id
        FROM vacation_settlements s
        JOIN users u ON u.user_id = s.employee_id
        WHERE s.settlement_state = 'PENDING_REVIEW'
          AND (@allOrgs OR u.primary_org_id = ANY(@orgIds))

        UNION ALL

        SELECT 'event'::text          AS source,
               s.employee_id          AS employee_id,
               s.entitlement_type     AS entitlement_type,
               s.entitlement_year     AS entitlement_year,
               s.sequence             AS settlement_sequence,
               s.settlement_state     AS settlement_state,
               s.trigger              AS trigger,
               s.review_disposition   AS review_disposition,
               0::numeric             AS flagged_days,
               s.version              AS version,
               f.first_occurred_at    AS age_anchor,
               u.primary_org_id       AS primary_org_id
        FROM (
            SELECT e.data->>'employeeId'                AS employee_id,
                   e.data->>'entitlementType'           AS entitlement_type,
                   (e.data->>'entitlementYear')::int    AS entitlement_year,
                   (e.data->>'sequence')::int           AS settlement_sequence,
                   MIN(e.occurred_at)                   AS first_occurred_at
            FROM events e
            WHERE e.event_type = 'SettlementManualReviewFlagged'
              AND (e.data->'snapshot' IS NULL OR jsonb_typeof(e.data->'snapshot') = 'null')
              AND (CASE WHEN jsonb_typeof(e.data->'flaggedDays') = 'number'
                        THEN (e.data->>'flaggedDays')::numeric
                   END) = 0
              AND jsonb_typeof(e.data->'entitlementYear') = 'number'
              AND jsonb_typeof(e.data->'sequence') = 'number'
            GROUP BY 1, 2, 3, 4
        ) f
        JOIN vacation_settlements s
          ON  s.employee_id      = f.employee_id
          AND s.entitlement_type = f.entitlement_type
          AND s.entitlement_year = f.entitlement_year
          AND s.sequence         = f.settlement_sequence
        JOIN users u ON u.user_id = s.employee_id
        WHERE s.settlement_state <> 'REVERSED'
          AND (@allOrgs OR u.primary_org_id = ANY(@orgIds))

        ORDER BY age_anchor ASC, employee_id ASC, entitlement_type ASC,
                 entitlement_year ASC, settlement_sequence ASC, source ASC
        """;

    /// <summary>
    /// HRP-005 + HRP-005b: every settlement awaiting manual review across the accessible-org set,
    /// OLDEST-FIRST by age anchor. <paramref name="accessibleOrgIds"/> <c>null</c> = GlobalAdmin
    /// (unrestricted); an empty set returns nothing (the endpoint 403s before reaching here).
    /// </summary>
    public async Task<IReadOnlyList<HrFollowUpPendingReviewRow>> GetPendingSettlementReviewsAsync(
        IReadOnlyCollection<string>? accessibleOrgIds, CancellationToken ct = default)
    {
        if (accessibleOrgIds is { Count: 0 })
            return Array.Empty<HrFollowUpPendingReviewRow>();

        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(SelectPendingReviewsSql, conn);
        AddOrgScopeParameters(cmd, accessibleOrgIds);

        var rows = new List<HrFollowUpPendingReviewRow>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new HrFollowUpPendingReviewRow(
                Source: reader.GetString(reader.GetOrdinal("source")),
                EmployeeId: reader.GetString(reader.GetOrdinal("employee_id")),
                EntitlementType: reader.GetString(reader.GetOrdinal("entitlement_type")),
                EntitlementYear: reader.GetInt32(reader.GetOrdinal("entitlement_year")),
                SettlementSequence: reader.GetInt32(reader.GetOrdinal("settlement_sequence")),
                SettlementState: reader.GetString(reader.GetOrdinal("settlement_state")),
                Trigger: reader.GetString(reader.GetOrdinal("trigger")),
                ReviewDisposition: GetNullableString(reader, "review_disposition"),
                FlaggedDays: reader.GetDecimal(reader.GetOrdinal("flagged_days")),
                Version: reader.GetInt64(reader.GetOrdinal("version")),
                AgeAnchor: ToUtcOffset(reader.GetFieldValue<DateTime>(reader.GetOrdinal("age_anchor"))),
                PrimaryOrgId: reader.GetString(reader.GetOrdinal("primary_org_id"))));
        }
        return rows;
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════
    // HRP-007 — settled terminations awaiting a §26 payout request
    // ═══════════════════════════════════════════════════════════════════════════════════════

    // FIVE predicate terms — four in the SQL below, the fifth in C# — each load-bearing:
    //  (1) trigger = 'TERMINATION'          — §26 pays a termination crystallization, nothing else.
    //  (2) settlement_state = 'SETTLED'     — a reversal VOIDs the request and CAS-marks the row
    //      REVERSED in the SAME transaction (SettlementReversalService), so this term is exactly
    //      what keeps a reversed termination off the tile. Under reverse-and-supersede the
    //      SUCCESSOR row (next sequence) is SETTLED and appears instead, until its own request is
    //      recorded — which is the correct behaviour: the successor genuinely needs one.
    //  (3) no live request                  — the partial-unique index enforces ONE non-voided
    //      request per settlement row; VOIDED_BY_REVERSAL rows stay as history, so the anti-join
    //      must exclude exactly that state (else a re-recordable row would never reappear).
    //  (4) review_disposition <> 'WAIVED'   — a waived §7 claim never gets a §26 request. Without
    //      this term a waived settlement would sit on HR's tile for ever with no action that could
    //      ever clear it.
    //
    // Why the OTHER review_disposition values need no term: MODREGNING (§7 deduct-in-full) is
    // PARKED behind the SLS dialogue and 422s at the resolve endpoint, so no row can carry it
    // today. FORFEIT on a TERMINATION row means the crystallization was over-taken (a negative
    // pre-clamp), so its snapshot's crystallizedDays is 0 and term (5) below drops it. DEFER and
    // FERIEHINDRING leave / require PENDING_REVIEW, which term (2) already excludes.
    //
    //  (5) The crystallised-days term (> 0) is applied in C# from the snapshot, using the §26
    //      endpoint's own accessor — see the method below.
    private const string SelectTerminationPayoutsUnrequestedSql =
        """
        SELECT s.employee_id, s.entitlement_type, s.entitlement_year,
               s.sequence AS settlement_sequence,
               s.snapshot::text AS snapshot_text,
               s.version, s.created_at AS age_anchor, u.primary_org_id
        FROM vacation_settlements s
        JOIN users u ON u.user_id = s.employee_id
        WHERE s.trigger = 'TERMINATION'
          AND s.settlement_state = 'SETTLED'
          AND (s.review_disposition IS NULL OR s.review_disposition <> 'WAIVED')
          AND NOT EXISTS (
                SELECT 1
                FROM termination_payout_requests r
                WHERE r.employee_id         = s.employee_id
                  AND r.entitlement_type    = s.entitlement_type
                  AND r.entitlement_year    = s.entitlement_year
                  AND r.settlement_sequence = s.sequence
                  AND r.state <> 'VOIDED_BY_REVERSAL')
          AND (@allOrgs OR u.primary_org_id = ANY(@orgIds))
        ORDER BY s.created_at ASC, s.employee_id ASC, s.entitlement_type ASC,
                 s.entitlement_year ASC, s.sequence ASC
        """;

    /// <summary>
    /// HRP-007: SETTLED TERMINATION settlements with a positive crystallised day-count and no live
    /// §26 request, OLDEST-FIRST by the settlement row's <c>created_at</c>.
    ///
    /// <para>
    /// The crystallised quantity is read through the SAME accessor the §26 request endpoint uses —
    /// deserialize the immutable snapshot and take <c>CrystallizedDays</c> (ADR-033 D3: COPIED,
    /// never recomputed). That is why the <c>&gt; 0</c> term lives in C# and not in the SQL: doing
    /// it as <c>snapshot-&gt;&gt;'crystallizedDays'</c> would be a SECOND implementation of the
    /// snapshot's key mapping, which is the divergence this task exists to avoid. A row whose
    /// snapshot is unparseable or carries no positive quantity is dropped and logged (there is
    /// nothing to request), never surfaced as a zero-day item.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<HrFollowUpTerminationPayoutRow>> GetTerminationPayoutsUnrequestedAsync(
        IReadOnlyCollection<string>? accessibleOrgIds, CancellationToken ct = default)
    {
        if (accessibleOrgIds is { Count: 0 })
            return Array.Empty<HrFollowUpTerminationPayoutRow>();

        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(SelectTerminationPayoutsUnrequestedSql, conn);
        AddOrgScopeParameters(cmd, accessibleOrgIds);

        var rows = new List<HrFollowUpTerminationPayoutRow>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var employeeId = reader.GetString(reader.GetOrdinal("employee_id"));
            var snapshotText = reader.GetString(reader.GetOrdinal("snapshot_text"));

            // The §26 endpoint's accessor shape, verbatim: tolerant deserialize, then the
            // snapshot's own scalars. Only SCALARS leave this method — the snapshot object never
            // enters a response closure (the SPRINT-117 pinned prohibition), and its two
            // employment-date-bearing fields (SettlementBoundaryDate, TerminationDate) never leave
            // at all: the boundary date is READ below as a precondition and then discarded.
            var requestable = ReadRequestableQuantity(snapshotText);
            if (requestable is not { } days)
            {
                // The SAME two fail-closed preconditions the §26 POST enforces as 422s:
                // a non-positive crystallizedDays (nothing to request), and a DEFAULT
                // SettlementBoundaryDate (the SPRINT-71 R11 dated-lønart anchor would be
                // unresolvable). Mirroring them here is the point of this list: an item HR cannot
                // action is worse than no item — it parks a job on the tile that no click can
                // clear, which is the same argument that excludes a WAIVED claim.
                _logger.LogDebug(
                    "HR follow-up (HRP-007): skipping {EmployeeId}/{Type}/{Year} sequence {Sequence} — " +
                    "the settlement snapshot carries no positive crystallizedDays, or no settlement " +
                    "boundary date, so the §26 request endpoint would refuse it (422).",
                    employeeId,
                    reader.GetString(reader.GetOrdinal("entitlement_type")),
                    reader.GetInt32(reader.GetOrdinal("entitlement_year")),
                    reader.GetInt32(reader.GetOrdinal("settlement_sequence")));
                continue;
            }

            rows.Add(new HrFollowUpTerminationPayoutRow(
                EmployeeId: employeeId,
                EntitlementType: reader.GetString(reader.GetOrdinal("entitlement_type")),
                EntitlementYear: reader.GetInt32(reader.GetOrdinal("entitlement_year")),
                SettlementSequence: reader.GetInt32(reader.GetOrdinal("settlement_sequence")),
                CrystallizedDays: days,
                Version: reader.GetInt64(reader.GetOrdinal("version")),
                AgeAnchor: ToUtcOffset(reader.GetFieldValue<DateTime>(reader.GetOrdinal("age_anchor"))),
                PrimaryOrgId: reader.GetString(reader.GetOrdinal("primary_org_id"))));
        }
        return rows;
    }

    /// <summary>
    /// The §26-requestable day-count from an immutable settlement snapshot, or <c>null</c> when the
    /// §26 request endpoint would refuse this row. Deliberately a shape-copy of that endpoint's own
    /// two preconditions so the list and the write agree on what is actionable:
    /// <c>CrystallizedDays &gt; 0</c> AND <c>SettlementBoundaryDate != default</c>.
    ///
    /// <para>
    /// The boundary date is examined and then DISCARDED — it is not returned, not stored on a row
    /// record, and not projected. On a TERMINATION snapshot it equals the employment end date, which
    /// no response body may carry (ADR-040 D7 as tightened for this task).
    /// </para>
    /// </summary>
    private static decimal? ReadRequestableQuantity(string snapshotJson)
    {
        if (string.IsNullOrWhiteSpace(snapshotJson))
            return null;

        VacationSettlementSnapshot? snapshot;
        try
        {
            snapshot = JsonSerializer.Deserialize<VacationSettlementSnapshot>(snapshotJson, SnapshotJsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }

        if (snapshot is null)
            return null;
        if (snapshot.CrystallizedDays is not { } days || days <= 0m)
            return null;
        if (snapshot.SettlementBoundaryDate == default)
            return null;
        return days;
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════
    // HRP-010 — the §21 stk.2 transfer agreement: the record, and who still needs one
    // ═══════════════════════════════════════════════════════════════════════════════════════

    // VACATION only, EXPLICITLY. The §21 stk.2 written transfer applies to the VACATION tranche
    // alone, and the write guard already 422s any other type — but the TABLE has no CHECK on
    // entitlement_type, so without this term a stray row would be returned under a response
    // contract that declares the value set as {VACATION}, i.e. the contract would be a liar. It
    // also means the derived §21 deadline is always computed on the VACATION geometry, never on
    // SPECIAL_HOLIDAY's 30-April one.
    private const string SelectTransferAgreementRecordSql =
        """
        SELECT entitlement_year, entitlement_type, transfer_days, agreement_date,
               recorded_by, version
        FROM vacation_transfer_agreements
        WHERE employee_id = @employeeId
          AND entitlement_type = @vacationType
        ORDER BY entitlement_year ASC, entitlement_type ASC
        """;

    /// <summary>
    /// HRP-010 (the record read): every §21 stk.2 agreement recorded for one employee, in ferieår
    /// order. The scope check binds to the employee in the endpoint, so this method takes the id
    /// only. An employee with nothing recorded returns an empty list — that is a valid answer (the
    /// law's default is §24 auto-payout), not a 404.
    /// </summary>
    public async Task<IReadOnlyList<HrFollowUpTransferAgreementRow>> GetTransferAgreementRecordAsync(
        string employeeId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(SelectTransferAgreementRecordSql, conn);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("vacationType", VacationType);

        var rows = new List<HrFollowUpTransferAgreementRow>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new HrFollowUpTransferAgreementRow(
                EntitlementYear: reader.GetInt32(0),
                EntitlementType: reader.GetString(1),
                TransferDays: reader.GetDecimal(2),
                AgreementDate: reader.GetFieldValue<DateOnly>(3),
                RecordedBy: reader.GetString(4),
                Version: reader.GetInt64(5)));
        }
        return rows;
    }

    // The candidate POPULATION for the §21 list. Four terms, all exclusions with a reason:
    //
    //  (a) org scope on the subject's CURRENT primary_org_id (see the class banner).
    //  (b) the employment window covers @today. The end date is the LAST employed day, so
    //      `employment_end_date >= @today` is exactly the domain's own non-leaver test
    //      (VacationSettlementService's leaver fork keys on `endDate < CopenhagenToday()`), which
    //      is why the "exclude leavers" requirement needs no separate term: a leaver's days went
    //      through the TERMINATION / §26 path, and listing them would invite HR to record a §21
    //      agreement for days already disposed of. NO is_active predicate — is_active is a LOGIN
    //      fact, not a data-visibility fact (ADR-040 D3, the SEC-047 adjudication), and a manually
    //      suspended employee still accrues holiday.
    //  (c) no ACTIVE (non-REVERSED) vacation_settlements row for the EXACT tuple
    //      (employee, VACATION, entitlement_year = E) — that ferieår is already settled, so its
    //      days are disposed of and there is nothing left to agree. Other years are irrelevant.
    //  (d) no recorded §21 agreement for the EXACT tuple (employee, VACATION, E) — HR has already
    //      done the job for THIS ferieår. An agreement for a different year does not exclude.
    //
    // NOTE (product observation, deliberately NOT "fixed" here): (d) is a BINARY test, per the
    // spec — an employee who has an agreement for FEWER days than their under-cap tranche drops
    // off the list. Recording a partial agreement is legal, so a "partially agreed" state may
    // deserve its own surface; that is a product decision, not this read's to invent.
    private const string SelectTransferAgreementCandidatesSql =
        """
        SELECT u.user_id, u.primary_org_id
        FROM users u
        WHERE (@allOrgs OR u.primary_org_id = ANY(@orgIds))
          AND (u.employment_start_date IS NULL OR u.employment_start_date <= @today)
          AND (u.employment_end_date   IS NULL OR u.employment_end_date   >= @today)
          AND NOT EXISTS (
                SELECT 1
                FROM vacation_settlements s
                WHERE s.employee_id      = u.user_id
                  AND s.entitlement_type = @vacationType
                  AND s.entitlement_year = @entitlementYear
                  AND s.settlement_state <> 'REVERSED')
          AND NOT EXISTS (
                SELECT 1
                FROM vacation_transfer_agreements t
                WHERE t.employee_id      = u.user_id
                  AND t.entitlement_type = @vacationType
                  AND t.entitlement_year = @entitlementYear)
        ORDER BY u.user_id ASC
        """;

    /// <summary>
    /// HRP-010 (the list read): employees in scope who still appear to need a §21 stk.2 agreement
    /// for ferieår <paramref name="entitlementYear"/> — the year the CALLER resolved as the one
    /// whose §21 deadline is this 31 December (never derived from a clock read in here).
    ///
    /// <para>
    /// <b>How the day-count is obtained — and the rule that must not be broken.</b> For each
    /// candidate the repository calls
    /// <see cref="VacationSettlementService.ValuateForReadAsync"/> — the settlement service's own
    /// READ-ONLY entry point onto the very code the settlement writers use — and then
    /// <see cref="VacationSettlementService.Partition"/> to take <c>UnderCap</c>: the §21/§24
    /// tranche, <c>min(disposable, carryover_max)</c> — the untaken ferieår remainder capped at
    /// the statutory fifth-week ceiling, i.e. the part a §21 agreement could carry forward (days
    /// ABOVE the cap are the §34 forfeiture-candidate bucket, not transferable). An employee is
    /// listed iff <c>UnderCap &gt; 0</c>. There is deliberately NO local re-implementation of
    /// "remaining days": a legal quantity computed twice diverges, and the divergence shows up as
    /// a wrong number in front of an employee.
    /// </para>
    ///
    /// <para>
    /// <b>Per-employee failure isolation.</b> The valuation FAILS CLOSED (ADR-033 D10): missing
    /// dated agreement-code, entitlement-config or employee-profile history at the ferieår start
    /// throws rather than valuing against today's live data. Each candidate therefore gets its OWN
    /// short read transaction, and a throw is caught, logged with the detail, and reported as a
    /// <c>cannotCompute</c> entry carrying only the employee id, the org and a stable reason code.
    /// One employee with broken history must not empty the tile for everyone — and must not vanish
    /// from it either.
    /// </para>
    ///
    /// <para>
    /// <b>Cost, declared.</b> This iterates candidates and values them one at a time (the
    /// refinement's "correct first, fast later"). The valuation is not expressible as one SQL
    /// statement without re-implementing it, which is the one thing forbidden here.
    /// </para>
    /// </summary>
    public async Task<HrFollowUpTransferAgreementNeededResult> GetTransferAgreementsNeededAsync(
        IReadOnlyCollection<string>? accessibleOrgIds,
        DateOnly today,
        int entitlementYear,
        CancellationToken ct = default)
    {
        if (accessibleOrgIds is { Count: 0 })
            return HrFollowUpTransferAgreementNeededResult.Empty;

        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);

        var candidates = new List<(string EmployeeId, string PrimaryOrgId)>();
        await using (var cmd = new NpgsqlCommand(SelectTransferAgreementCandidatesSql, conn))
        {
            AddOrgScopeParameters(cmd, accessibleOrgIds);
            cmd.Parameters.Add(new NpgsqlParameter("today", NpgsqlDbType.Date) { Value = today });
            cmd.Parameters.Add(new NpgsqlParameter("entitlementYear", NpgsqlDbType.Integer) { Value = entitlementYear });
            cmd.Parameters.Add(new NpgsqlParameter("vacationType", NpgsqlDbType.Text) { Value = VacationType });

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                candidates.Add((reader.GetString(0), reader.GetString(1)));
        }

        var needed = new List<HrFollowUpTransferAgreementNeededRow>();
        var cannotCompute = new List<HrFollowUpTransferAgreementCannotComputeRow>();

        foreach (var (employeeId, primaryOrgId) in candidates)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                // A transaction PER CANDIDATE. What it actually buys, precisely:
                //  * a per-candidate ABORT boundary. If a statement errors, only THIS candidate's
                //    transaction is aborted; the shared connection is usable for the next one. The
                //    failure isolation itself comes from the try/catch below — the transaction is
                //    what keeps the connection from being left in a failed-transaction state.
                //  * ReadCommitted pinned explicitly (PAT-015 discipline), matching what the
                //    settlement writers run under, so the reused capture code sees the isolation
                //    level it was written for.
                // What it does NOT buy, stated so nobody reads more into it: it is NOT a consistent
                // snapshot of the valuation. ReadCommitted gives each statement its own snapshot,
                // and several reads inside the capture (the dated agreement code, the entitlement
                // config, the employee profile) are self-managed and open their OWN connections
                // OUTSIDE this transaction. A settlement committing mid-loop can therefore tear one
                // employee's figure. That is acceptable and already declared on the wire: this list
                // is projection-based until the year-end close makes it exact. RepeatableRead was
                // considered and rejected — it would only tighten the minority of reads that are
                // in-transaction, buying partial consistency at the cost of diverging from the
                // writers' isolation level.
                // The transaction only READS and is ROLLED BACK — nothing here can write.
                await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

                var snapshot = await _settlementService.ValuateForReadAsync(
                    conn, tx, employeeId, VacationType, entitlementYear, ct);

                // The ONE §21/§24 partition (ADR-033 D5) — a pure function of the snapshot.
                var partition = VacationSettlementService.Partition(snapshot);

                await tx.RollbackAsync(ct);

                if (partition.UnderCap > 0m)
                {
                    needed.Add(new HrFollowUpTransferAgreementNeededRow(
                        EmployeeId: employeeId,
                        PrimaryOrgId: primaryOrgId,
                        UnderCapDays: partition.UnderCap,
                        CarryoverMax: snapshot.CarryoverMax));
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Fail-closed per employee (ADR-033 D10). The DETAIL goes to the log; the wire
                // carries only a stable code — the throw messages name dated-history anchors, and
                // an exception string on a response body is how a redacted date escapes.
                _logger.LogWarning(ex,
                    "HR follow-up (HRP-010): could not value the §21 tranche for {EmployeeId} " +
                    "(ferieår {EntitlementYear}, VACATION). The employee is reported as " +
                    "cannot-compute; the rest of the list is unaffected.",
                    employeeId, entitlementYear);
                cannotCompute.Add(new HrFollowUpTransferAgreementCannotComputeRow(
                    EmployeeId: employeeId,
                    PrimaryOrgId: primaryOrgId,
                    Reason: CannotComputeReasonValuationFailed));
            }
        }

        return new HrFollowUpTransferAgreementNeededResult(needed, cannotCompute);
    }

    // ── shared helpers ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The org-scope bind, identical in shape to <c>HrBackdateWorklistRepository</c>:
    /// <c>@allOrgs</c> short-circuits the predicate for the GlobalAdmin null sentinel, and
    /// <c>@orgIds</c> is a parameterised text array (never interpolated).
    /// </summary>
    private static void AddOrgScopeParameters(NpgsqlCommand cmd, IReadOnlyCollection<string>? accessibleOrgIds)
    {
        cmd.Parameters.Add(new NpgsqlParameter("allOrgs", NpgsqlDbType.Boolean) { Value = accessibleOrgIds is null });
        cmd.Parameters.Add(new NpgsqlParameter("orgIds", NpgsqlDbType.Array | NpgsqlDbType.Text)
        {
            Value = accessibleOrgIds?.ToArray() ?? Array.Empty<string>(),
        });
    }

    private static string? GetNullableString(NpgsqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    /// <summary>
    /// Npgsql returns <c>timestamptz</c> as a UTC <see cref="DateTime"/>; wrap it as a
    /// <see cref="DateTimeOffset"/> at zero offset so the wire value is unambiguous (the
    /// <c>HrBackdateWorklistRepository</c> convention).
    /// </summary>
    private static DateTimeOffset ToUtcOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}

// ── storage-side row records (Infrastructure) — the endpoint projects these onto the wire ────

/// <summary>HRP-005 / HRP-005b storage row. <c>Source</c> is <c>row</c> or <c>event</c>.</summary>
public sealed record HrFollowUpPendingReviewRow(
    string Source,
    string EmployeeId,
    string EntitlementType,
    int EntitlementYear,
    int SettlementSequence,
    string SettlementState,
    string Trigger,
    string? ReviewDisposition,
    decimal FlaggedDays,
    long Version,
    DateTimeOffset AgeAnchor,
    string PrimaryOrgId);

/// <summary>
/// HRP-007 storage row. <c>CrystallizedDays</c> is the snapshot's own value, copied — the
/// snapshot OBJECT is deliberately not carried past the repository (SPRINT-117 prohibition), and
/// neither is any employment date from it.
/// </summary>
public sealed record HrFollowUpTerminationPayoutRow(
    string EmployeeId,
    string EntitlementType,
    int EntitlementYear,
    int SettlementSequence,
    decimal CrystallizedDays,
    long Version,
    DateTimeOffset AgeAnchor,
    string PrimaryOrgId);

/// <summary>HRP-010 storage row for the RECORD read (one recorded §21 agreement).</summary>
public sealed record HrFollowUpTransferAgreementRow(
    int EntitlementYear,
    string EntitlementType,
    decimal TransferDays,
    DateOnly AgreementDate,
    string RecordedBy,
    long Version);

/// <summary>HRP-010 storage row for the LIST read — one employee who still needs an agreement.</summary>
public sealed record HrFollowUpTransferAgreementNeededRow(
    string EmployeeId,
    string PrimaryOrgId,
    decimal UnderCapDays,
    decimal CarryoverMax);

/// <summary>HRP-010: one employee whose §21 tranche could not be valued (fail-closed, isolated).</summary>
public sealed record HrFollowUpTransferAgreementCannotComputeRow(
    string EmployeeId,
    string PrimaryOrgId,
    string Reason);

/// <summary>
/// HRP-010 list result — the valued candidates PLUS the employees the valuation could not reach.
/// Both halves travel together so the endpoint can never report a count without also reporting
/// what is missing from it.
/// </summary>
public sealed record HrFollowUpTransferAgreementNeededResult(
    IReadOnlyList<HrFollowUpTransferAgreementNeededRow> Needed,
    IReadOnlyList<HrFollowUpTransferAgreementCannotComputeRow> CannotCompute)
{
    public static HrFollowUpTransferAgreementNeededResult Empty { get; } = new(
        Array.Empty<HrFollowUpTransferAgreementNeededRow>(),
        Array.Empty<HrFollowUpTransferAgreementCannotComputeRow>());
}
