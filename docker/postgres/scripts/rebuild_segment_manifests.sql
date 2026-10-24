-- ============================================================
-- Sprint 20 / TASK-2011 — segment_manifests projection rebuild
-- ============================================================
-- Idempotent ops script that rebuilds the `segment_manifests` projection
-- by replaying every `SegmentManifestCreated` event in the event store.
--
-- Use case: projection drift recovery. If `segment_manifests` rows go out
-- of sync with the event store (e.g. table corruption, a missed event
-- during deployment, a botched manual edit), an operator runs this to
-- restore the projection from the authoritative event log.
--
-- Idempotency strategy: TRUNCATE then full replay (rather than upsert).
-- Chosen because (a) the event store is the source of truth and replay
-- is cheap relative to the small projected row count, and (b) it sweeps
-- away orphan rows that would survive an upsert.
--
-- Concurrency / maintenance-window guidance:
--   This script is intended for a maintenance window. Operators SHOULD
--   quiesce calculation traffic (PCS, retroactive corrections, replay)
--   before running. The transaction below uses SERIALIZABLE isolation,
--   which makes any concurrent SegmentManifestCreated insert into `events`
--   a loud failure (40001 serialization_failure) rather than a silent
--   miss. If the rebuild aborts with that error, the projection is
--   unchanged (TRUNCATE rolled back) — re-run after quiescing writers.
--
--   S146 (TASK-14604): the manifest event now reaches `events` through the
--   Payroll transactional outbox. Before running, quiesce writers AND let the
--   Payroll outbox drain: no row with service_id = 'payroll' and
--   stream_id LIKE 'segment-manifest-%' may be unpublished
--   (SELECT count(*) FROM outbox_events WHERE service_id = 'payroll'
--    AND stream_id LIKE 'segment-manifest-%' AND published_at IS NULL = 0).
--   A manifest still on the outbox is not yet in `events`, so a rebuild run
--   before the drain would drop its projection row.
--
-- Invocation:
--   psql -v ON_ERROR_STOP=1 -f rebuild_segment_manifests.sql
--   (or programmatically via SegmentManifestProjectionRebuilder.RebuildAsync)
--
-- The same SQL is mirrored verbatim inside the C# rebuilder (see
-- src/Infrastructure/StatsTid.Infrastructure/SegmentManifestProjectionRebuilder.cs
-- → ReplaySql constant) so test harnesses can invoke a rebuild headlessly.
-- KEEP THE TWO IN SYNC when modifying the projection logic.
--
-- Field mapping from `events.data` (camelCase JSON, ADR-005) to
-- `segment_manifests` columns:
--
--   manifest_id            <- data->>'manifestId'              (uuid)
--   period_start           <- data->>'periodStart'             (date)
--   period_end             <- data->>'periodEnd'               (date)
--   employee_id            <- data->>'employeeId'              (text — ADR-016 D10 amendment 2026-05-01)
--   calculation_kind       <- data->>'calculationKind'         (text)
--   boundary_cause_summary <- data->'boundaryCauseSummary'     (jsonb -> text[])
--   created_at             <- data->>'createdAt'               (timestamptz)
--   segments_jsonb         <- data->'segments'                 (jsonb)
--   plan_inputs_jsonb      <- data->'planInputs'               (jsonb, NULLABLE — S145 / A7)
--
-- plan_inputs_jsonb (S145 / TASK-14509, refinement A7): the frozen inputs of the calculation
-- a replay must reuse. The event SegmentManifestCreated.planInputs is the record of truth, so
-- it is copied as-is, like segments. Unlike segments there is deliberately NO COALESCE: a
-- manifest written before S145 has no frozen inputs (the key is absent) and its row must stay
-- NULL, so a replay refuses it by name (replay-manifest-without-frozen-inputs) instead of
-- reading a fabricated empty input set. NULLIF(..., 'null'::jsonb) folds an explicit JSON null
-- to SQL NULL for the same reason.
--
-- Note: `manifest_id` keeps its `::uuid` cast (column stays UUID per D10).
--       `employee_id` does NOT cast — the column is TEXT and `data->>` already
--       returns text. The earlier `::uuid` cast was removed in the D10 2026-05-01
--       amendment (forward-only schema change to align with the rest of init.sql).
--
-- Ordering: DISTINCT ON (manifest_id) + ORDER BY manifest_id, global_position DESC
-- selects the latest event per manifest_id (last-write-wins). Duplicates would only
-- occur if the same manifest were re-emitted (e.g. a buggy emitter or a replay
-- scenario) — the projection mirrors the most recent emission. The integrity
-- check below surfaces such duplicates so operators don't see a silently
-- deduped projection.
-- ============================================================

BEGIN;

-- Loud-failure mode for concurrent writers: a concurrent SegmentManifestCreated
-- insert during the rebuild produces 40001 (serialization_failure) instead of
-- a silently-missed row. Operators retry after quiescing writers.
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

-- Capture pre-rebuild row count for delta visibility.
DO $$
DECLARE
    previous_rows BIGINT;
BEGIN
    SELECT COUNT(*) INTO previous_rows FROM segment_manifests;
    RAISE NOTICE 'rebuild_segment_manifests: pre-rebuild row count = %', previous_rows;
END $$;

TRUNCATE TABLE segment_manifests;

INSERT INTO segment_manifests (
    manifest_id,
    period_start,
    period_end,
    employee_id,
    calculation_kind,
    boundary_cause_summary,
    created_at,
    segments_jsonb,
    plan_inputs_jsonb
)
SELECT DISTINCT ON (manifest_id)
    manifest_id,
    period_start,
    period_end,
    employee_id,
    calculation_kind,
    boundary_cause_summary,
    created_at,
    segments_jsonb,
    plan_inputs_jsonb
FROM (
    SELECT
        (data->>'manifestId')::uuid                     AS manifest_id,
        (data->>'periodStart')::date                    AS period_start,
        (data->>'periodEnd')::date                      AS period_end,
        data->>'employeeId'                             AS employee_id,
        data->>'calculationKind'                        AS calculation_kind,
        ARRAY(
            SELECT jsonb_array_elements_text(
                COALESCE(data->'boundaryCauseSummary', '[]'::jsonb)
            )
        )                                               AS boundary_cause_summary,
        (data->>'createdAt')::timestamptz               AS created_at,
        COALESCE(data->'segments', '[]'::jsonb)         AS segments_jsonb,
        NULLIF(data->'planInputs', 'null'::jsonb)       AS plan_inputs_jsonb,
        global_position
    FROM events
    WHERE event_type = 'SegmentManifestCreated'
) src
ORDER BY manifest_id, global_position DESC;
-- DISTINCT ON + ORDER BY (manifest_id, global_position DESC) gives
-- last-write-wins per manifest_id without violating the projection's
-- PRIMARY KEY (manifest_id) constraint.

-- Integrity check: surface duplicate manifest_id emissions so operators see
-- (and can triage) any projection-dedupe that happened. By ADR-016 D10
-- construction duplicates should be impossible; if this count is > 0, a
-- buggy emitter is likely the cause and ops should investigate.
DO $$
DECLARE
    duplicate_count BIGINT;
    sample_ids      UUID[];
    rebuilt_rows    BIGINT;
BEGIN
    -- events has no physical manifest_id column; extract from data JSON.
    SELECT COUNT(*) INTO duplicate_count
    FROM (
        SELECT (data->>'manifestId')::uuid AS manifest_id
        FROM events
        WHERE event_type = 'SegmentManifestCreated'
        GROUP BY (data->>'manifestId')::uuid
        HAVING COUNT(*) > 1
    ) AS duplicates;

    IF duplicate_count > 0 THEN
        SELECT ARRAY_AGG(manifest_id)
        INTO sample_ids
        FROM (
            SELECT (data->>'manifestId')::uuid AS manifest_id
            FROM events
            WHERE event_type = 'SegmentManifestCreated'
            GROUP BY (data->>'manifestId')::uuid
            HAVING COUNT(*) > 1
            ORDER BY (data->>'manifestId')::uuid
            LIMIT 5
        ) AS d;

        RAISE WARNING 'rebuild_segment_manifests: % manifest_id values had duplicate SegmentManifestCreated events (last-write-wins applied). First up to 5: %',
            duplicate_count, sample_ids;
    END IF;

    SELECT COUNT(*) INTO rebuilt_rows FROM segment_manifests;
    RAISE NOTICE 'rebuild_segment_manifests: rebuilt row count = %', rebuilt_rows;
END $$;

COMMIT;
