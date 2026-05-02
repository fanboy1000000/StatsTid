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
    segments_jsonb
)
SELECT DISTINCT ON (manifest_id)
    manifest_id,
    period_start,
    period_end,
    employee_id,
    calculation_kind,
    boundary_cause_summary,
    created_at,
    segments_jsonb
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
