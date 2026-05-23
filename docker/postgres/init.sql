-- StatsTid Event Store Schema
-- Sprint 2 initialization

-- =========================================================================
-- S22 / ADR-018 — schema_migrations ledger (must precede any DO $$ block
-- that depends on it; cycle-3 review N-3 ordering invariant).
-- =========================================================================
CREATE TABLE IF NOT EXISTS schema_migrations (
    migration_id  TEXT         PRIMARY KEY,
    applied_at    TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    notes         TEXT         NULL
);

-- Event streams table
CREATE TABLE IF NOT EXISTS event_streams (
    stream_id       TEXT        PRIMARY KEY,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Core events table with per-stream versioning
CREATE TABLE IF NOT EXISTS events (
    global_position BIGSERIAL   PRIMARY KEY,
    event_id        UUID        NOT NULL UNIQUE,
    stream_id       TEXT        NOT NULL REFERENCES event_streams(stream_id),
    stream_version  INT         NOT NULL,
    event_type      TEXT        NOT NULL,
    data            JSONB       NOT NULL,
    occurred_at     TIMESTAMPTZ NOT NULL,
    stored_at       TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (stream_id, stream_version)
);

CREATE INDEX IF NOT EXISTS idx_events_stream_id ON events(stream_id);
CREATE INDEX IF NOT EXISTS idx_events_event_type ON events(event_type);
CREATE INDEX IF NOT EXISTS idx_events_occurred_at ON events(occurred_at);

-- =========================================================================
-- S22 / ADR-018 — outbox_events: transactional outbox for state-change +
-- event-store atomicity. State-change endpoints INSERT here inside the
-- caller's tx via IEventStore.EnqueueAsync; OutboxPublisher BackgroundService
-- drains rows in service-id-scoped FIFO order to the canonical event store.
-- =========================================================================
CREATE TABLE IF NOT EXISTS outbox_events (
    outbox_id        BIGSERIAL    PRIMARY KEY,
    service_id       TEXT         NOT NULL,
    stream_id        TEXT         NOT NULL,
    event_id         UUID         NOT NULL UNIQUE,
    event_type       TEXT         NOT NULL,
    event_payload    JSONB        NOT NULL,
    correlation_id   TEXT         NULL,
    actor_id         TEXT         NULL,
    actor_role       TEXT         NULL,
    created_at       TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    published_at     TIMESTAMPTZ  NULL,
    stream_version   INT          NULL,
    attempts         INT          NOT NULL DEFAULT 0,
    last_error       TEXT         NULL,
    last_attempt_at  TIMESTAMPTZ  NULL
);

CREATE INDEX IF NOT EXISTS idx_outbox_unpublished
    ON outbox_events (service_id, outbox_id)
    WHERE published_at IS NULL;

CREATE INDEX IF NOT EXISTS idx_outbox_attempts
    ON outbox_events (service_id, attempts, last_attempt_at)
    WHERE published_at IS NULL AND attempts > 0;

CREATE INDEX IF NOT EXISTS idx_outbox_stream
    ON outbox_events (stream_id, outbox_id)
    WHERE published_at IS NULL;

-- Outbox messages for guaranteed delivery pattern
CREATE TABLE IF NOT EXISTS outbox_messages (
    message_id          UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    destination         TEXT        NOT NULL,
    payload             JSONB       NOT NULL,
    status              TEXT        NOT NULL DEFAULT 'pending',
    attempt_count       INT         NOT NULL DEFAULT 0,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    last_attempt_at     TIMESTAMPTZ,
    delivered_at        TIMESTAMPTZ,
    error_message       TEXT,
    idempotency_token   UUID        UNIQUE
);

CREATE INDEX IF NOT EXISTS idx_outbox_status ON outbox_messages(status);
CREATE INDEX IF NOT EXISTS idx_outbox_created_at ON outbox_messages(created_at);
CREATE INDEX IF NOT EXISTS idx_outbox_destination_status ON outbox_messages(destination, status);

-- Orchestrator task audit trail
CREATE TABLE IF NOT EXISTS orchestrator_tasks (
    task_id         UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    task_type       TEXT        NOT NULL,
    status          TEXT        NOT NULL DEFAULT 'pending',
    input_data      JSONB,
    output_data     JSONB,
    assigned_agent  TEXT,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    started_at      TIMESTAMPTZ,
    completed_at    TIMESTAMPTZ,
    error_message   TEXT
);

CREATE INDEX IF NOT EXISTS idx_orch_tasks_status ON orchestrator_tasks(status);

-- Rule versions (versioned per OK agreement + agreement code)
CREATE TABLE IF NOT EXISTS rule_versions (
    rule_id         TEXT        NOT NULL,
    ok_version      TEXT        NOT NULL,
    rule_name       TEXT        NOT NULL,
    agreement_code  TEXT        NOT NULL,
    effective_from  DATE        NOT NULL,
    effective_to    DATE,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (rule_id, ok_version, agreement_code)
);

-- Wage type mappings (versioned per OK agreement, optionally position-specific).
-- S29 / ADR-020: surrogate UUID PK + effective-dating columns baked into the
-- schema so greenfield bootstrap is single-pass. The migration block further
-- down in this file remains idempotent on top of this shape (each ALTER is
-- guarded by IF NOT EXISTS / DROP IF EXISTS) and is the path for legacy
-- environments still on the pre-S29 composite-PK schema.
CREATE TABLE IF NOT EXISTS wage_type_mappings (
    mapping_id      UUID        NOT NULL DEFAULT gen_random_uuid(),
    time_type       TEXT        NOT NULL,
    wage_type       TEXT        NOT NULL,
    ok_version      TEXT        NOT NULL,
    agreement_code  TEXT        NOT NULL,
    position        TEXT        NOT NULL DEFAULT '',
    description     TEXT,
    effective_from  DATE        NOT NULL,
    effective_to    DATE,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (mapping_id)
);

-- ADR-020 D2: at most one open row per natural key (S21 D2.1 partial-unique pattern).
CREATE UNIQUE INDEX IF NOT EXISTS idx_wtm_natural_key_open
    ON wage_type_mappings (time_type, ok_version, agreement_code, position)
    WHERE effective_to IS NULL;

-- ADR-020 D3 conflict target: forbids duplicate history rows on (natural_key, effective_from).
CREATE UNIQUE INDEX IF NOT EXISTS idx_wtm_natural_key_history
    ON wage_type_mappings (time_type, ok_version, agreement_code, position, effective_from);

-- Flex balance snapshots
CREATE TABLE IF NOT EXISTS flex_balance_snapshots (
    snapshot_id     UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    employee_id     TEXT        NOT NULL,
    period_start    DATE        NOT NULL,
    period_end      DATE        NOT NULL,
    balance_hours   DECIMAL     NOT NULL,
    delta           DECIMAL     NOT NULL,
    ok_version      TEXT        NOT NULL,
    agreement_code  TEXT        NOT NULL,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_flex_snapshots_employee ON flex_balance_snapshots(employee_id);
CREATE INDEX IF NOT EXISTS idx_flex_snapshots_period ON flex_balance_snapshots(period_start, period_end);

-- Danish public holidays (cache / reference)
CREATE TABLE IF NOT EXISTS danish_public_holidays (
    holiday_date    DATE        NOT NULL,
    holiday_name    TEXT        NOT NULL,
    ok_version      TEXT        NOT NULL,
    PRIMARY KEY (holiday_date, ok_version)
);

-- ============================================================
-- SEED DATA
-- ============================================================

-- Seed OK24 rule versions (4 rules x 3 agreements)
INSERT INTO rule_versions (rule_id, ok_version, rule_name, agreement_code, effective_from) VALUES
    ('NORM_CHECK_37H', 'OK24', 'Weekly Norm Check 37 Hours', 'AC', '2024-04-01'),
    ('NORM_CHECK_37H', 'OK24', 'Weekly Norm Check 37 Hours', 'HK', '2024-04-01'),
    ('NORM_CHECK_37H', 'OK24', 'Weekly Norm Check 37 Hours', 'PROSA', '2024-04-01'),
    ('SUPPLEMENT_CALC', 'OK24', 'Supplement Calculation', 'AC', '2024-04-01'),
    ('SUPPLEMENT_CALC', 'OK24', 'Supplement Calculation', 'HK', '2024-04-01'),
    ('SUPPLEMENT_CALC', 'OK24', 'Supplement Calculation', 'PROSA', '2024-04-01'),
    ('OVERTIME_CALC', 'OK24', 'Overtime / Merarbejde Calculation', 'AC', '2024-04-01'),
    ('OVERTIME_CALC', 'OK24', 'Overtime / Merarbejde Calculation', 'HK', '2024-04-01'),
    ('OVERTIME_CALC', 'OK24', 'Overtime / Merarbejde Calculation', 'PROSA', '2024-04-01'),
    ('ABSENCE_CALC', 'OK24', 'Absence Calculation', 'AC', '2024-04-01'),
    ('ABSENCE_CALC', 'OK24', 'Absence Calculation', 'HK', '2024-04-01'),
    ('ABSENCE_CALC', 'OK24', 'Absence Calculation', 'PROSA', '2024-04-01'),
    ('ON_CALL_DUTY', 'OK24', 'On-Call Duty Calculation', 'AC', '2024-04-01'),
    ('ON_CALL_DUTY', 'OK24', 'On-Call Duty Calculation', 'HK', '2024-04-01'),
    ('ON_CALL_DUTY', 'OK24', 'On-Call Duty Calculation', 'PROSA', '2024-04-01')
ON CONFLICT DO NOTHING;

-- Seed OK26 rule versions (5 rules x 3 agreements)
INSERT INTO rule_versions (rule_id, ok_version, rule_name, agreement_code, effective_from) VALUES
    ('NORM_CHECK_37H', 'OK26', 'Weekly Norm Check 37 Hours', 'AC', '2026-04-01'),
    ('NORM_CHECK_37H', 'OK26', 'Weekly Norm Check 37 Hours', 'HK', '2026-04-01'),
    ('NORM_CHECK_37H', 'OK26', 'Weekly Norm Check 37 Hours', 'PROSA', '2026-04-01'),
    ('SUPPLEMENT_CALC', 'OK26', 'Supplement Calculation', 'AC', '2026-04-01'),
    ('SUPPLEMENT_CALC', 'OK26', 'Supplement Calculation', 'HK', '2026-04-01'),
    ('SUPPLEMENT_CALC', 'OK26', 'Supplement Calculation', 'PROSA', '2026-04-01'),
    ('OVERTIME_CALC', 'OK26', 'Overtime / Merarbejde Calculation', 'AC', '2026-04-01'),
    ('OVERTIME_CALC', 'OK26', 'Overtime / Merarbejde Calculation', 'HK', '2026-04-01'),
    ('OVERTIME_CALC', 'OK26', 'Overtime / Merarbejde Calculation', 'PROSA', '2026-04-01'),
    ('ABSENCE_CALC', 'OK26', 'Absence Calculation', 'AC', '2026-04-01'),
    ('ABSENCE_CALC', 'OK26', 'Absence Calculation', 'HK', '2026-04-01'),
    ('ABSENCE_CALC', 'OK26', 'Absence Calculation', 'PROSA', '2026-04-01'),
    ('ON_CALL_DUTY', 'OK26', 'On-Call Duty Calculation', 'AC', '2026-04-01'),
    ('ON_CALL_DUTY', 'OK26', 'On-Call Duty Calculation', 'HK', '2026-04-01'),
    ('ON_CALL_DUTY', 'OK26', 'On-Call Duty Calculation', 'PROSA', '2026-04-01')
ON CONFLICT DO NOTHING;

-- Seed OK24 wage type mappings
INSERT INTO wage_type_mappings (time_type, wage_type, ok_version, agreement_code, description, effective_from) VALUES
    -- Normal hours (all agreements)
    ('NORMAL_HOURS', 'SLS_0110', 'OK24', 'AC', 'Normal working hours', '2020-01-01'),
    ('NORMAL_HOURS', 'SLS_0110', 'OK24', 'HK', 'Normal working hours', '2020-01-01'),
    ('NORMAL_HOURS', 'SLS_0110', 'OK24', 'PROSA', 'Normal working hours', '2020-01-01'),
    -- Overtime (HK/PROSA)
    ('OVERTIME_50', 'SLS_0210', 'OK24', 'HK', 'Overtime at 50% supplement', '2020-01-01'),
    ('OVERTIME_50', 'SLS_0210', 'OK24', 'PROSA', 'Overtime at 50% supplement', '2020-01-01'),
    ('OVERTIME_100', 'SLS_0220', 'OK24', 'HK', 'Overtime at 100% supplement', '2020-01-01'),
    ('OVERTIME_100', 'SLS_0220', 'OK24', 'PROSA', 'Overtime at 100% supplement', '2020-01-01'),
    -- Merarbejde (AC only)
    ('MERARBEJDE', 'SLS_0310', 'OK24', 'AC', 'Extra work (merarbejde)', '2020-01-01'),
    -- Supplements (HK/PROSA)
    ('EVENING_SUPPLEMENT', 'SLS_0410', 'OK24', 'HK', 'Evening supplement 17-23', '2020-01-01'),
    ('EVENING_SUPPLEMENT', 'SLS_0410', 'OK24', 'PROSA', 'Evening supplement 17-23', '2020-01-01'),
    ('NIGHT_SUPPLEMENT', 'SLS_0420', 'OK24', 'HK', 'Night supplement 23-06', '2020-01-01'),
    ('NIGHT_SUPPLEMENT', 'SLS_0420', 'OK24', 'PROSA', 'Night supplement 23-06', '2020-01-01'),
    ('WEEKEND_SUPPLEMENT', 'SLS_0430', 'OK24', 'HK', 'Weekend supplement', '2020-01-01'),
    ('WEEKEND_SUPPLEMENT', 'SLS_0430', 'OK24', 'PROSA', 'Weekend supplement', '2020-01-01'),
    ('HOLIDAY_SUPPLEMENT', 'SLS_0440', 'OK24', 'HK', 'Public holiday supplement', '2020-01-01'),
    ('HOLIDAY_SUPPLEMENT', 'SLS_0440', 'OK24', 'PROSA', 'Public holiday supplement', '2020-01-01'),
    -- Absence types (all agreements)
    ('VACATION', 'SLS_0510', 'OK24', 'AC', 'Vacation', '2020-01-01'),
    ('VACATION', 'SLS_0510', 'OK24', 'HK', 'Vacation', '2020-01-01'),
    ('VACATION', 'SLS_0510', 'OK24', 'PROSA', 'Vacation', '2020-01-01'),
    ('CARE_DAY', 'SLS_0520', 'OK24', 'AC', 'Care day', '2020-01-01'),
    ('CARE_DAY', 'SLS_0520', 'OK24', 'HK', 'Care day', '2020-01-01'),
    ('CARE_DAY', 'SLS_0520', 'OK24', 'PROSA', 'Care day', '2020-01-01'),
    ('CHILD_SICK_DAY', 'SLS_0530', 'OK24', 'AC', 'Childs 1st sick day', '2020-01-01'),
    ('CHILD_SICK_DAY', 'SLS_0530', 'OK24', 'HK', 'Childs 1st sick day', '2020-01-01'),
    ('CHILD_SICK_DAY', 'SLS_0530', 'OK24', 'PROSA', 'Childs 1st sick day', '2020-01-01'),
    ('PARENTAL_LEAVE', 'SLS_0540', 'OK24', 'AC', 'Parental leave', '2020-01-01'),
    ('PARENTAL_LEAVE', 'SLS_0540', 'OK24', 'HK', 'Parental leave', '2020-01-01'),
    ('PARENTAL_LEAVE', 'SLS_0540', 'OK24', 'PROSA', 'Parental leave', '2020-01-01'),
    ('SENIOR_DAY', 'SLS_0550', 'OK24', 'AC', 'Senior day', '2020-01-01'),
    ('SENIOR_DAY', 'SLS_0550', 'OK24', 'HK', 'Senior day', '2020-01-01'),
    ('SENIOR_DAY', 'SLS_0550', 'OK24', 'PROSA', 'Senior day', '2020-01-01'),
    ('LEAVE_WITHOUT_PAY', 'SLS_0560', 'OK24', 'AC', 'Leave without pay', '2020-01-01'),
    ('LEAVE_WITHOUT_PAY', 'SLS_0560', 'OK24', 'HK', 'Leave without pay', '2020-01-01'),
    ('LEAVE_WITHOUT_PAY', 'SLS_0560', 'OK24', 'PROSA', 'Leave without pay', '2020-01-01'),
    ('CHILD_SICK_DAY_2', 'SLS_0531', 'OK24', 'AC', 'Childs 2nd sick day', '2020-01-01'),
    ('CHILD_SICK_DAY_2', 'SLS_0531', 'OK24', 'HK', 'Childs 2nd sick day', '2020-01-01'),
    ('CHILD_SICK_DAY_2', 'SLS_0531', 'OK24', 'PROSA', 'Childs 2nd sick day', '2020-01-01'),
    ('CHILD_SICK_DAY_3', 'SLS_0532', 'OK24', 'AC', 'Childs 3rd sick day', '2020-01-01'),
    ('CHILD_SICK_DAY_3', 'SLS_0532', 'OK24', 'HK', 'Childs 3rd sick day', '2020-01-01'),
    ('CHILD_SICK_DAY_3', 'SLS_0532', 'OK24', 'PROSA', 'Childs 3rd sick day', '2020-01-01'),
    ('SPECIAL_HOLIDAY_ALLOWANCE', 'SLS_0570', 'OK24', 'AC', 'Special holiday allowance', '2020-01-01'),
    ('SPECIAL_HOLIDAY_ALLOWANCE', 'SLS_0570', 'OK24', 'HK', 'Special holiday allowance', '2020-01-01'),
    ('SPECIAL_HOLIDAY_ALLOWANCE', 'SLS_0570', 'OK24', 'PROSA', 'Special holiday allowance', '2020-01-01'),
    ('LEAVE_WITH_PAY', 'SLS_0565', 'OK24', 'AC', 'Leave with pay', '2020-01-01'),
    ('LEAVE_WITH_PAY', 'SLS_0565', 'OK24', 'HK', 'Leave with pay', '2020-01-01'),
    ('LEAVE_WITH_PAY', 'SLS_0565', 'OK24', 'PROSA', 'Leave with pay', '2020-01-01'),
    -- Flex payout
    ('FLEX_PAYOUT', 'SLS_0610', 'OK24', 'AC', 'Flex balance auto-payout', '2020-01-01'),
    ('FLEX_PAYOUT', 'SLS_0610', 'OK24', 'HK', 'Flex balance auto-payout', '2020-01-01'),
    ('FLEX_PAYOUT', 'SLS_0610', 'OK24', 'PROSA', 'Flex balance auto-payout', '2020-01-01'),
    -- On-call duty (rådighedsvagt)
    ('ON_CALL_DUTY', 'SLS_0710', 'OK24', 'AC', 'On-call duty compensation', '2020-01-01'),
    ('ON_CALL_DUTY', 'SLS_0710', 'OK24', 'HK', 'On-call duty compensation', '2020-01-01'),
    ('ON_CALL_DUTY', 'SLS_0710', 'OK24', 'PROSA', 'On-call duty compensation', '2020-01-01'),
    -- Call-in work (tilkald) — HK/PROSA enabled, AC disabled but mapped for completeness
    ('CALL_IN_WORK', 'SLS_0810', 'OK24', 'AC', 'Call-in work compensation', '2020-01-01'),
    ('CALL_IN_WORK', 'SLS_0810', 'OK24', 'HK', 'Call-in work compensation', '2020-01-01'),
    ('CALL_IN_WORK', 'SLS_0810', 'OK24', 'PROSA', 'Call-in work compensation', '2020-01-01'),
    -- Travel time (rejsetid)
    ('TRAVEL_WORK', 'SLS_0820', 'OK24', 'AC', 'Working travel time', '2020-01-01'),
    ('TRAVEL_WORK', 'SLS_0820', 'OK24', 'HK', 'Working travel time', '2020-01-01'),
    ('TRAVEL_WORK', 'SLS_0820', 'OK24', 'PROSA', 'Working travel time', '2020-01-01'),
    ('TRAVEL_NON_WORK', 'SLS_0830', 'OK24', 'AC', 'Non-working travel time', '2020-01-01'),
    ('TRAVEL_NON_WORK', 'SLS_0830', 'OK24', 'HK', 'Non-working travel time', '2020-01-01'),
    ('TRAVEL_NON_WORK', 'SLS_0830', 'OK24', 'PROSA', 'Non-working travel time', '2020-01-01')
ON CONFLICT (time_type, ok_version, agreement_code, position, effective_from) DO NOTHING;

-- Seed OK26 wage type mappings (identical to OK24 for now)
INSERT INTO wage_type_mappings (time_type, wage_type, ok_version, agreement_code, description, effective_from) VALUES
    ('NORMAL_HOURS', 'SLS_0110', 'OK26', 'AC', 'Normal working hours', '2020-01-01'),
    ('NORMAL_HOURS', 'SLS_0110', 'OK26', 'HK', 'Normal working hours', '2020-01-01'),
    ('NORMAL_HOURS', 'SLS_0110', 'OK26', 'PROSA', 'Normal working hours', '2020-01-01'),
    ('OVERTIME_50', 'SLS_0210', 'OK26', 'HK', 'Overtime at 50% supplement', '2020-01-01'),
    ('OVERTIME_50', 'SLS_0210', 'OK26', 'PROSA', 'Overtime at 50% supplement', '2020-01-01'),
    ('OVERTIME_100', 'SLS_0220', 'OK26', 'HK', 'Overtime at 100% supplement', '2020-01-01'),
    ('OVERTIME_100', 'SLS_0220', 'OK26', 'PROSA', 'Overtime at 100% supplement', '2020-01-01'),
    ('MERARBEJDE', 'SLS_0310', 'OK26', 'AC', 'Extra work (merarbejde)', '2020-01-01'),
    ('EVENING_SUPPLEMENT', 'SLS_0410', 'OK26', 'HK', 'Evening supplement 17-23', '2020-01-01'),
    ('EVENING_SUPPLEMENT', 'SLS_0410', 'OK26', 'PROSA', 'Evening supplement 17-23', '2020-01-01'),
    ('NIGHT_SUPPLEMENT', 'SLS_0420', 'OK26', 'HK', 'Night supplement 23-06', '2020-01-01'),
    ('NIGHT_SUPPLEMENT', 'SLS_0420', 'OK26', 'PROSA', 'Night supplement 23-06', '2020-01-01'),
    ('WEEKEND_SUPPLEMENT', 'SLS_0430', 'OK26', 'HK', 'Weekend supplement', '2020-01-01'),
    ('WEEKEND_SUPPLEMENT', 'SLS_0430', 'OK26', 'PROSA', 'Weekend supplement', '2020-01-01'),
    ('HOLIDAY_SUPPLEMENT', 'SLS_0440', 'OK26', 'HK', 'Public holiday supplement', '2020-01-01'),
    ('HOLIDAY_SUPPLEMENT', 'SLS_0440', 'OK26', 'PROSA', 'Public holiday supplement', '2020-01-01'),
    ('VACATION', 'SLS_0510', 'OK26', 'AC', 'Vacation', '2020-01-01'),
    ('VACATION', 'SLS_0510', 'OK26', 'HK', 'Vacation', '2020-01-01'),
    ('VACATION', 'SLS_0510', 'OK26', 'PROSA', 'Vacation', '2020-01-01'),
    ('CARE_DAY', 'SLS_0520', 'OK26', 'AC', 'Care day', '2020-01-01'),
    ('CARE_DAY', 'SLS_0520', 'OK26', 'HK', 'Care day', '2020-01-01'),
    ('CARE_DAY', 'SLS_0520', 'OK26', 'PROSA', 'Care day', '2020-01-01'),
    ('CHILD_SICK_DAY', 'SLS_0530', 'OK26', 'AC', 'Childs 1st sick day', '2020-01-01'),
    ('CHILD_SICK_DAY', 'SLS_0530', 'OK26', 'HK', 'Childs 1st sick day', '2020-01-01'),
    ('CHILD_SICK_DAY', 'SLS_0530', 'OK26', 'PROSA', 'Childs 1st sick day', '2020-01-01'),
    ('PARENTAL_LEAVE', 'SLS_0540', 'OK26', 'AC', 'Parental leave', '2020-01-01'),
    ('PARENTAL_LEAVE', 'SLS_0540', 'OK26', 'HK', 'Parental leave', '2020-01-01'),
    ('PARENTAL_LEAVE', 'SLS_0540', 'OK26', 'PROSA', 'Parental leave', '2020-01-01'),
    ('SENIOR_DAY', 'SLS_0550', 'OK26', 'AC', 'Senior day', '2020-01-01'),
    ('SENIOR_DAY', 'SLS_0550', 'OK26', 'HK', 'Senior day', '2020-01-01'),
    ('SENIOR_DAY', 'SLS_0550', 'OK26', 'PROSA', 'Senior day', '2020-01-01'),
    ('LEAVE_WITHOUT_PAY', 'SLS_0560', 'OK26', 'AC', 'Leave without pay', '2020-01-01'),
    ('LEAVE_WITHOUT_PAY', 'SLS_0560', 'OK26', 'HK', 'Leave without pay', '2020-01-01'),
    ('LEAVE_WITHOUT_PAY', 'SLS_0560', 'OK26', 'PROSA', 'Leave without pay', '2020-01-01'),
    ('CHILD_SICK_DAY_2', 'SLS_0531', 'OK26', 'AC', 'Childs 2nd sick day', '2020-01-01'),
    ('CHILD_SICK_DAY_2', 'SLS_0531', 'OK26', 'HK', 'Childs 2nd sick day', '2020-01-01'),
    ('CHILD_SICK_DAY_2', 'SLS_0531', 'OK26', 'PROSA', 'Childs 2nd sick day', '2020-01-01'),
    ('CHILD_SICK_DAY_3', 'SLS_0532', 'OK26', 'AC', 'Childs 3rd sick day', '2020-01-01'),
    ('CHILD_SICK_DAY_3', 'SLS_0532', 'OK26', 'HK', 'Childs 3rd sick day', '2020-01-01'),
    ('CHILD_SICK_DAY_3', 'SLS_0532', 'OK26', 'PROSA', 'Childs 3rd sick day', '2020-01-01'),
    ('SPECIAL_HOLIDAY_ALLOWANCE', 'SLS_0570', 'OK26', 'AC', 'Special holiday allowance', '2020-01-01'),
    ('SPECIAL_HOLIDAY_ALLOWANCE', 'SLS_0570', 'OK26', 'HK', 'Special holiday allowance', '2020-01-01'),
    ('SPECIAL_HOLIDAY_ALLOWANCE', 'SLS_0570', 'OK26', 'PROSA', 'Special holiday allowance', '2020-01-01'),
    ('LEAVE_WITH_PAY', 'SLS_0565', 'OK26', 'AC', 'Leave with pay', '2020-01-01'),
    ('LEAVE_WITH_PAY', 'SLS_0565', 'OK26', 'HK', 'Leave with pay', '2020-01-01'),
    ('LEAVE_WITH_PAY', 'SLS_0565', 'OK26', 'PROSA', 'Leave with pay', '2020-01-01'),
    ('FLEX_PAYOUT', 'SLS_0610', 'OK26', 'AC', 'Flex balance auto-payout', '2020-01-01'),
    ('FLEX_PAYOUT', 'SLS_0610', 'OK26', 'HK', 'Flex balance auto-payout', '2020-01-01'),
    ('FLEX_PAYOUT', 'SLS_0610', 'OK26', 'PROSA', 'Flex balance auto-payout', '2020-01-01'),
    -- On-call duty (rådighedsvagt)
    ('ON_CALL_DUTY', 'SLS_0710', 'OK26', 'AC', 'On-call duty compensation', '2020-01-01'),
    ('ON_CALL_DUTY', 'SLS_0710', 'OK26', 'HK', 'On-call duty compensation', '2020-01-01'),
    ('ON_CALL_DUTY', 'SLS_0710', 'OK26', 'PROSA', 'On-call duty compensation', '2020-01-01'),
    -- Call-in work (tilkald)
    ('CALL_IN_WORK', 'SLS_0810', 'OK26', 'AC', 'Call-in work compensation', '2020-01-01'),
    ('CALL_IN_WORK', 'SLS_0810', 'OK26', 'HK', 'Call-in work compensation', '2020-01-01'),
    ('CALL_IN_WORK', 'SLS_0810', 'OK26', 'PROSA', 'Call-in work compensation', '2020-01-01'),
    -- Travel time (rejsetid)
    ('TRAVEL_WORK', 'SLS_0820', 'OK26', 'AC', 'Working travel time', '2020-01-01'),
    ('TRAVEL_WORK', 'SLS_0820', 'OK26', 'HK', 'Working travel time', '2020-01-01'),
    ('TRAVEL_WORK', 'SLS_0820', 'OK26', 'PROSA', 'Working travel time', '2020-01-01'),
    ('TRAVEL_NON_WORK', 'SLS_0830', 'OK26', 'AC', 'Non-working travel time', '2020-01-01'),
    ('TRAVEL_NON_WORK', 'SLS_0830', 'OK26', 'HK', 'Non-working travel time', '2020-01-01'),
    ('TRAVEL_NON_WORK', 'SLS_0830', 'OK26', 'PROSA', 'Non-working travel time', '2020-01-01')
ON CONFLICT (time_type, ok_version, agreement_code, position, effective_from) DO NOTHING;

-- Seed Danish public holidays 2024-2026 (computed via Computus algorithm)
-- 2024: Easter = March 31
INSERT INTO danish_public_holidays (holiday_date, holiday_name, ok_version) VALUES
    ('2024-01-01', 'Nytaarsdag', 'OK24'),
    ('2024-03-28', 'Skaertorsdag', 'OK24'),
    ('2024-03-29', 'Langfredag', 'OK24'),
    ('2024-03-31', 'Paaskedag', 'OK24'),
    ('2024-04-01', '2. Paaskedag', 'OK24'),
    ('2024-05-09', 'Kristi Himmelfartsdag', 'OK24'),
    ('2024-05-19', 'Pinsedag', 'OK24'),
    ('2024-05-20', '2. Pinsedag', 'OK24'),
    ('2024-06-05', 'Grundlovsdag', 'OK24'),
    ('2024-12-25', 'Juledag', 'OK24'),
    ('2024-12-26', '2. Juledag', 'OK24')
ON CONFLICT DO NOTHING;

-- 2025: Easter = April 20
INSERT INTO danish_public_holidays (holiday_date, holiday_name, ok_version) VALUES
    ('2025-01-01', 'Nytaarsdag', 'OK24'),
    ('2025-04-17', 'Skaertorsdag', 'OK24'),
    ('2025-04-18', 'Langfredag', 'OK24'),
    ('2025-04-20', 'Paaskedag', 'OK24'),
    ('2025-04-21', '2. Paaskedag', 'OK24'),
    ('2025-05-29', 'Kristi Himmelfartsdag', 'OK24'),
    ('2025-06-05', 'Grundlovsdag', 'OK24'),
    ('2025-06-08', 'Pinsedag', 'OK24'),
    ('2025-06-09', '2. Pinsedag', 'OK24'),
    ('2025-12-25', 'Juledag', 'OK24'),
    ('2025-12-26', '2. Juledag', 'OK24')
ON CONFLICT DO NOTHING;

-- 2026: Easter = April 5
INSERT INTO danish_public_holidays (holiday_date, holiday_name, ok_version) VALUES
    ('2026-01-01', 'Nytaarsdag', 'OK26'),
    ('2026-04-02', 'Skaertorsdag', 'OK26'),
    ('2026-04-03', 'Langfredag', 'OK26'),
    ('2026-04-05', 'Paaskedag', 'OK26'),
    ('2026-04-06', '2. Paaskedag', 'OK26'),
    ('2026-05-14', 'Kristi Himmelfartsdag', 'OK26'),
    ('2026-05-24', 'Pinsedag', 'OK26'),
    ('2026-05-25', '2. Pinsedag', 'OK26'),
    ('2026-06-05', 'Grundlovsdag', 'OK26'),
    ('2026-12-25', 'Juledag', 'OK26'),
    ('2026-12-26', '2. Juledag', 'OK26')
ON CONFLICT DO NOTHING;

-- ============================================================
-- SPRINT 3: Actor tracking and audit log
-- ============================================================

-- Add actor tracking columns to events table
ALTER TABLE events ADD COLUMN IF NOT EXISTS actor_id TEXT;
ALTER TABLE events ADD COLUMN IF NOT EXISTS actor_role TEXT;
ALTER TABLE events ADD COLUMN IF NOT EXISTS correlation_id UUID;
CREATE INDEX IF NOT EXISTS idx_events_actor_id ON events(actor_id);
CREATE INDEX IF NOT EXISTS idx_events_correlation_id ON events(correlation_id);

-- Append-only audit log
CREATE TABLE IF NOT EXISTS audit_log (
    log_id          BIGSERIAL    PRIMARY KEY,
    timestamp       TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    actor_id        TEXT,
    actor_role      TEXT,
    action          TEXT         NOT NULL,
    resource        TEXT         NOT NULL,
    resource_id     TEXT,
    correlation_id  UUID,
    http_method     TEXT,
    http_path       TEXT,
    http_status     INT,
    result          TEXT         NOT NULL DEFAULT 'success',
    details         JSONB,
    ip_address      TEXT
);
CREATE INDEX IF NOT EXISTS idx_audit_log_actor ON audit_log(actor_id);
CREATE INDEX IF NOT EXISTS idx_audit_log_correlation ON audit_log(correlation_id);
CREATE INDEX IF NOT EXISTS idx_audit_log_timestamp ON audit_log(timestamp);

-- ============================================================
-- SPRINT 6: RBAC, Organizational Hierarchy, Period Approval
-- ============================================================

-- Organization hierarchy (Ministry -> Styrelse -> Afdeling -> Team)
CREATE TABLE IF NOT EXISTS organizations (
    org_id              TEXT        PRIMARY KEY,
    org_name            TEXT        NOT NULL,
    org_type            TEXT        NOT NULL CHECK (org_type IN ('MINISTRY', 'STYRELSE', 'AFDELING', 'TEAM')),
    parent_org_id       TEXT        REFERENCES organizations(org_id),
    materialized_path   TEXT        NOT NULL,
    agreement_code      TEXT        NOT NULL DEFAULT 'AC',
    ok_version          TEXT        NOT NULL DEFAULT 'OK24',
    is_active           BOOLEAN     NOT NULL DEFAULT TRUE,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_org_parent ON organizations(parent_org_id);
CREATE INDEX IF NOT EXISTS idx_org_path ON organizations USING btree (materialized_path text_pattern_ops);
CREATE INDEX IF NOT EXISTS idx_org_type ON organizations(org_type);

-- Users (replaces hardcoded test users)
CREATE TABLE IF NOT EXISTS users (
    user_id             TEXT        PRIMARY KEY,
    username            TEXT        NOT NULL UNIQUE,
    password_hash       TEXT        NOT NULL,
    display_name        TEXT        NOT NULL,
    email               TEXT,
    primary_org_id      TEXT        NOT NULL REFERENCES organizations(org_id),
    agreement_code      TEXT        NOT NULL DEFAULT 'AC',
    ok_version          TEXT        NOT NULL DEFAULT 'OK24',
    employment_category TEXT        NOT NULL DEFAULT 'Standard',
    is_active           BOOLEAN     NOT NULL DEFAULT TRUE,
    version             BIGINT      NOT NULL DEFAULT 1,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_users_org ON users(primary_org_id);
CREATE INDEX IF NOT EXISTS idx_users_username ON users(username);

-- ── Phase 4d-3 Part 1 (Sprint 31): employee_profiles authoritative store ──
-- Surrogate UUID PK + pre-baked versioning columns (effective_from / effective_to /
-- partial-unique-index / history-unique-index / version) so S32 needs ZERO schema
-- migration to start emitting closed predecessors. S31 reads/writes only live rows
-- (effective_to IS NULL); S32 activates the multi-row history path. Mirrors S29
-- wage_type_mappings precedent.
CREATE TABLE IF NOT EXISTS employee_profiles (
    profile_id          UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    employee_id         TEXT        NOT NULL REFERENCES users(user_id),
    weekly_norm_hours   NUMERIC(5,2) NOT NULL,
    part_time_fraction  NUMERIC(4,3) NOT NULL DEFAULT 1.000,
    position            TEXT        NULL,
    effective_from      DATE        NOT NULL DEFAULT '0001-01-01',
    effective_to        DATE        NULL,
    version             BIGINT      NOT NULL DEFAULT 1,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Partial-unique-index: at most one live (open) row per employee. Forward-compat
-- for S32 supersession routing (Case B closes predecessor + opens new live row).
CREATE UNIQUE INDEX IF NOT EXISTS idx_employee_profiles_live
    ON employee_profiles (employee_id)
    WHERE effective_to IS NULL;

-- History-unique-index: at most one row per (employee_id, effective_from). Forward-
-- compat for S32 history INSERTs at distinct effective_from values.
CREATE UNIQUE INDEX IF NOT EXISTS idx_employee_profiles_history
    ON employee_profiles (employee_id, effective_from);

-- employee_profile_audit (singular; mirrors wage_type_mapping_audit post-S25
-- shape). version_before + version_after baked into the base CREATE (NOT a
-- separate ALTER) because this table is brand-new in S31. action CHECK includes
-- all 4 values (CREATED/UPDATED/DELETED/SUPERSEDED) up-front even though S31
-- only emits CREATED + UPDATED — S32 will emit SUPERSEDED + DELETED without
-- schema change. No FK on profile_id because supersession + soft-delete create
-- FK-invalidating histories.
CREATE TABLE IF NOT EXISTS employee_profile_audit (
    audit_id        BIGSERIAL    PRIMARY KEY,
    profile_id      UUID         NOT NULL,
    employee_id     TEXT         NOT NULL,
    action          TEXT         NOT NULL CHECK (action IN ('CREATED','UPDATED','DELETED','SUPERSEDED')),
    previous_data   JSONB        NULL,
    new_data        JSONB        NULL,
    version_before  BIGINT       NULL,
    version_after   BIGINT       NULL,
    actor_id        TEXT         NOT NULL,
    actor_role      TEXT         NOT NULL,
    timestamp       TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_employee_profile_audit_profile_id ON employee_profile_audit (profile_id);
CREATE INDEX IF NOT EXISTS idx_employee_profile_audit_employee_id ON employee_profile_audit (employee_id);

-- schema_migrations ledger entry — documentary for S31 greenfield-only, forward-
-- compat marker if init.sql ever runs against an older database.
INSERT INTO schema_migrations (migration_id, applied_at)
    VALUES ('s31-d3-employee-profile-store', NOW())
    ON CONFLICT (migration_id) DO NOTHING;

-- ── Phase 4e (Sprint 34): user_agreement_codes versioned-history store ──
-- ADR-023 D2 option (b): per-user agreement-code history table so payroll
-- export can perform an effective-date lookup (mirroring S29 wage_type_mappings
-- ADR-018 D14 export-time pattern) instead of relying on the live scalar
-- `users.agreement_code`. 4th application of the established versioned-config
-- pattern (S29 wage_type_mappings, S30 entitlement_configs, S31 employee_profiles).
-- Surrogate UUID PK + effective_from / effective_to / partial-unique-index /
-- history-unique-index / version. S34 activates the full 3-case routing
-- (ADR-020 D2): Case A INSERT v=1 (no live row), Case B same-day UPDATE-
-- in-place (v→v+1), Case C cross-day supersede (close predecessor +
-- INSERT successor at predecessor.Version+1 per S33 Step 7a P1 ETag-
-- monotonicity refinement). Backfill seeder writes 'effective_from = 0001-01-01'
-- (history-covering anchor) so first PUT-after-seed Case-C-supersedes cleanly.
CREATE TABLE IF NOT EXISTS user_agreement_codes (
    assignment_id    UUID         PRIMARY KEY,
    user_id          TEXT         NOT NULL REFERENCES users(user_id),
    agreement_code   TEXT         NOT NULL,
    effective_from   DATE         NOT NULL DEFAULT '0001-01-01',
    effective_to     DATE         NULL,
    version          BIGINT       NOT NULL DEFAULT 1,
    created_at       TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_at       TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

-- Partial-unique-index: at most one live (open) row per user. Forward-compat
-- for supersession routing (Case B closes predecessor + opens new live row).
CREATE UNIQUE INDEX IF NOT EXISTS idx_user_agreement_codes_live
    ON user_agreement_codes (user_id) WHERE effective_to IS NULL;

-- History-unique-index: at most one row per (user_id, effective_from). Forward-
-- compat for history INSERTs at distinct effective_from values.
CREATE UNIQUE INDEX IF NOT EXISTS idx_user_agreement_codes_history
    ON user_agreement_codes (user_id, effective_from);

-- user_agreement_codes_audit — mirrors employee_profile_audit shape.
-- version_before + version_after baked into the base CREATE (NOT a separate
-- ALTER) because this table is brand-new in S34. action CHECK includes all 4
-- values (CREATED/UPDATED/DELETED/SUPERSEDED) up-front; DELETED reserved for
-- future (no DELETE path in S34 — SoftDelete dropped per Step 0b BLOCKER 1
-- absorption — harmless dead enum, parallels employee_profile_audit). No FK
-- on assignment_id because supersession creates FK-invalidating histories.
CREATE TABLE IF NOT EXISTS user_agreement_codes_audit (
    audit_id          BIGSERIAL    PRIMARY KEY,
    assignment_id     UUID         NOT NULL,
    user_id           TEXT         NOT NULL,
    action            TEXT         NOT NULL CHECK (action IN ('CREATED','UPDATED','DELETED','SUPERSEDED')),
    previous_data     JSONB        NULL,
    new_data          JSONB        NULL,
    version_before    BIGINT       NULL,
    version_after     BIGINT       NULL,
    actor_id          TEXT         NOT NULL,
    actor_role        TEXT         NOT NULL,
    audit_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_user_agreement_codes_audit_assignment_id
    ON user_agreement_codes_audit (assignment_id);
CREATE INDEX IF NOT EXISTS idx_user_agreement_codes_audit_user_id
    ON user_agreement_codes_audit (user_id);

-- schema_migrations ledger entry — documentary for S34 greenfield-only, forward-
-- compat marker if init.sql ever runs against an older database.
INSERT INTO schema_migrations (migration_id, applied_at)
    VALUES ('s34-d1-user-agreement-codes', NOW())
    ON CONFLICT (migration_id) DO NOTHING;

-- ── Phase 4e (Sprint 35): users.version + users_audit ──
-- ADR-018 D7 row-version + If-Match optimistic concurrency on /api/admin/users.
-- 4th application of the established versioned-config pattern (S29 wage_type_mappings,
-- S30 entitlement_configs, S31 employee_profiles, S34 user_agreement_codes). users.version
-- is baked into the base users CREATE (above) — IF NOT EXISTS guards make that a no-op
-- on legacy databases where the users row already exists; the guarded ALTER block at the
-- bottom of this file (S35 / D1) carries the ADD COLUMN path that the IF NOT EXISTS CREATE
-- cannot reach on upgrade. action CHECK enum includes all 4 values
-- (CREATED/UPDATED/DELETED/SUPERSEDED) up-front for forward-compat even though users has
-- no supersession lifecycle today (PUT updates in place; agreement-code supersession lives
-- on the separate user_agreement_codes_audit stream). Matches precedent CHECK enum at
-- employee_profile_audit (L514) + user_agreement_codes_audit (L577). Column name
-- `audit_at` follows the S34 era convention (user_agreement_codes_audit at L584), NOT the
-- S31 era `timestamp` (employee_profile_audit at L521). No FK on user_id because
-- supersession + deletion need to leave audit rows untouched.
CREATE TABLE IF NOT EXISTS users_audit (
    audit_id          BIGSERIAL    PRIMARY KEY,
    user_id           TEXT         NOT NULL,
    action            TEXT         NOT NULL CHECK (action IN ('CREATED','UPDATED','DELETED','SUPERSEDED')),
    previous_data     JSONB        NULL,
    new_data          JSONB        NULL,
    version_before    BIGINT       NULL,
    version_after     BIGINT       NULL,
    actor_id          TEXT         NOT NULL,
    actor_role        TEXT         NOT NULL,
    audit_at          TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_users_audit_user_id ON users_audit(user_id);
CREATE INDEX IF NOT EXISTS idx_users_audit_at ON users_audit(audit_at);

-- schema_migrations ledger row owned by the guarded ALTER block at the bottom of the
-- file (S35 / D1). Inserting the ledger row here would short-circuit the ALTER's
-- `IF NOT FOUND THEN RETURN` guard and leave legacy databases without users.version.

-- Role definitions (5 roles)
CREATE TABLE IF NOT EXISTS roles (
    role_id             TEXT        PRIMARY KEY,
    role_name           TEXT        NOT NULL,
    description         TEXT,
    hierarchy_level     INT         NOT NULL,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Role assignments with organizational scope
CREATE TABLE IF NOT EXISTS role_assignments (
    assignment_id       UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id             TEXT        NOT NULL REFERENCES users(user_id),
    role_id             TEXT        NOT NULL REFERENCES roles(role_id),
    org_id              TEXT        REFERENCES organizations(org_id),
    scope_type          TEXT        NOT NULL CHECK (scope_type IN ('GLOBAL', 'ORG_ONLY', 'ORG_AND_DESCENDANTS')),
    assigned_by         TEXT        NOT NULL,
    assigned_at         TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    expires_at          TIMESTAMPTZ,
    is_active           BOOLEAN     NOT NULL DEFAULT TRUE,
    UNIQUE (user_id, role_id, org_id)
);
CREATE INDEX IF NOT EXISTS idx_role_assignments_user ON role_assignments(user_id);
CREATE INDEX IF NOT EXISTS idx_role_assignments_org ON role_assignments(org_id);
CREATE INDEX IF NOT EXISTS idx_role_assignments_role ON role_assignments(role_id);

-- Role assignment audit trail (append-only)
CREATE TABLE IF NOT EXISTS role_assignment_audit (
    audit_id            BIGSERIAL   PRIMARY KEY,
    assignment_id       UUID        NOT NULL,
    action              TEXT        NOT NULL CHECK (action IN ('GRANTED', 'REVOKED', 'EXPIRED', 'MODIFIED')),
    actor_id            TEXT        NOT NULL,
    actor_role          TEXT        NOT NULL,
    details             JSONB,
    timestamp           TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_role_audit_assignment ON role_assignment_audit(assignment_id);

-- Local configuration overrides (validated against central constraints)
CREATE TABLE IF NOT EXISTS local_configurations (
    config_id           UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    org_id              TEXT        NOT NULL REFERENCES organizations(org_id),
    config_area         TEXT        NOT NULL CHECK (config_area IN (
        'WORKING_TIME', 'FLEX_RULES', 'ORG_STRUCTURE', 'LOCAL_AGREEMENT', 'OPERATIONAL'
    )),
    config_key          TEXT        NOT NULL,
    config_value        JSONB       NOT NULL,
    effective_from      DATE        NOT NULL,
    effective_to        DATE,
    version             INT         NOT NULL DEFAULT 1,
    agreement_code      TEXT        NOT NULL,
    ok_version          TEXT        NOT NULL,
    created_by          TEXT        NOT NULL,
    approved_by         TEXT,
    approved_at         TIMESTAMPTZ,
    is_active           BOOLEAN     NOT NULL DEFAULT TRUE,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (org_id, config_area, config_key, effective_from, agreement_code, ok_version)
);
CREATE INDEX IF NOT EXISTS idx_local_config_org ON local_configurations(org_id);
CREATE INDEX IF NOT EXISTS idx_local_config_area ON local_configurations(config_area);

-- Local configuration audit trail (append-only)
-- S21 ADR-017 D8: extended action vocabulary for legacy-row migration to profile model.
--   DROPPED_DUPLICATE_AT_MIGRATION  -- legacy row collapsed into a sibling profile (dup of another key)
--   DROPPED_INFORMATIONAL           -- legacy row carried no overridable value (e.g. notes-only)
--   DROPPED_UNKNOWN_KEY             -- legacy row referenced a config_key not in the profile schema
--   MIGRATED_FROM_LEGACY            -- legacy row absorbed into a local_agreement_profiles row
CREATE TABLE IF NOT EXISTS local_configuration_audit (
    audit_id            BIGSERIAL   PRIMARY KEY,
    config_id           UUID        NOT NULL,
    action              TEXT        NOT NULL CHECK (action IN (
        'CREATED', 'MODIFIED', 'DEACTIVATED', 'APPROVED',
        'DROPPED_DUPLICATE_AT_MIGRATION', 'DROPPED_INFORMATIONAL',
        'DROPPED_UNKNOWN_KEY', 'MIGRATED_FROM_LEGACY'
    )),
    previous_value      JSONB,
    new_value           JSONB,
    actor_id            TEXT        NOT NULL,
    actor_role          TEXT        NOT NULL,
    timestamp           TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_local_config_audit_config ON local_configuration_audit(config_id);

-- =====================================================================
-- S21 ADR-017: Local agreement configuration as a profile
-- =====================================================================
-- Replaces the per-key patch bag in local_configurations with a typed
-- profile row per (org_id, agreement_code, ok_version) effective period.
-- Schema-level invariant: at most one currently-active profile per
-- (org, agreement_code, ok_version) -- enforced via partial unique index
-- on effective_to IS NULL (ADR-017 D1).
CREATE TABLE IF NOT EXISTS local_agreement_profiles (
    profile_id                          UUID            PRIMARY KEY DEFAULT gen_random_uuid(),
    org_id                              TEXT            NOT NULL REFERENCES organizations(org_id),
    agreement_code                      TEXT            NOT NULL,
    ok_version                          TEXT            NOT NULL,
    effective_from                      DATE            NOT NULL,
    effective_to                        DATE,
    -- Overridable fields: NULL means "inherit central agreement value"
    weekly_norm_hours                   NUMERIC(5,2),
    max_flex_balance                    NUMERIC(6,2),
    flex_carryover_max                  NUMERIC(6,2),
    max_overtime_hours_per_period       NUMERIC(6,2),
    overtime_requires_pre_approval      BOOLEAN,
    -- Metadata
    created_by                          TEXT            NOT NULL,
    created_at                          TIMESTAMPTZ     NOT NULL DEFAULT NOW()
);
-- ADR-017 D1: at most one active (effective_to IS NULL) profile per scope.
CREATE UNIQUE INDEX IF NOT EXISTS uq_local_agreement_profile_active
    ON local_agreement_profiles (org_id, agreement_code, ok_version)
    WHERE effective_to IS NULL;
CREATE INDEX IF NOT EXISTS idx_local_agreement_profile_org
    ON local_agreement_profiles(org_id);
-- For GET .../history reads: list profiles in reverse chronological order.
CREATE INDEX IF NOT EXISTS idx_local_agreement_profile_history
    ON local_agreement_profiles (org_id, agreement_code, ok_version, effective_from DESC);

-- ADR-017 D8: append-only audit trail for profile lifecycle events.
-- Mirrors a new event type registered by TASK-2103 (DEP-003 EventSerializer).
CREATE TABLE IF NOT EXISTS local_agreement_profile_audit (
    audit_id        BIGSERIAL       PRIMARY KEY,
    profile_id      UUID            NOT NULL,
    action          TEXT            NOT NULL CHECK (action IN (
        'CREATED', 'SUPERSEDED', 'DEACTIVATED', 'MIGRATED_FROM_LEGACY'
    )),
    delta_jsonb     JSONB           NOT NULL,
    actor_id        TEXT            NOT NULL,
    actor_role      TEXT            NOT NULL,
    timestamp       TIMESTAMPTZ     NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_local_profile_audit_profile
    ON local_agreement_profile_audit(profile_id);

-- Period approval workflow
CREATE TABLE IF NOT EXISTS approval_periods (
    period_id           UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    employee_id         TEXT        NOT NULL,
    org_id              TEXT        NOT NULL REFERENCES organizations(org_id),
    period_start        DATE        NOT NULL,
    period_end          DATE        NOT NULL,
    period_type         TEXT        NOT NULL CHECK (period_type IN ('WEEKLY', 'MONTHLY')),
    status              TEXT        NOT NULL DEFAULT 'DRAFT' CHECK (status IN ('DRAFT', 'SUBMITTED', 'APPROVED', 'REJECTED')),
    submitted_at        TIMESTAMPTZ,
    submitted_by        TEXT,
    approved_by         TEXT,
    approved_at         TIMESTAMPTZ,
    rejection_reason    TEXT,
    agreement_code      TEXT        NOT NULL,
    ok_version          TEXT        NOT NULL,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (employee_id, period_start, period_end)
);
CREATE INDEX IF NOT EXISTS idx_approval_employee ON approval_periods(employee_id);
CREATE INDEX IF NOT EXISTS idx_approval_org ON approval_periods(org_id);
CREATE INDEX IF NOT EXISTS idx_approval_status ON approval_periods(status);
CREATE INDEX IF NOT EXISTS idx_approval_period ON approval_periods(period_start, period_end);

-- Approval audit trail (append-only)
CREATE TABLE IF NOT EXISTS approval_audit (
    audit_id            BIGSERIAL   PRIMARY KEY,
    period_id           UUID        NOT NULL,
    action              TEXT        NOT NULL CHECK (action IN ('CREATED', 'SUBMITTED', 'APPROVED', 'REJECTED', 'REOPENED')),
    actor_id            TEXT        NOT NULL,
    actor_role          TEXT        NOT NULL,
    comment             TEXT,
    timestamp           TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_approval_audit_period ON approval_audit(period_id);

-- ============================================================
-- SPRINT 6 SEED DATA: Roles, Organizations, Test Users
-- ============================================================

-- Seed role definitions
INSERT INTO roles (role_id, role_name, description, hierarchy_level) VALUES
    ('GLOBAL_ADMIN', 'Global Administrator', 'Manages state agreements, operates across all organizations', 1),
    ('LOCAL_ADMIN', 'Local Administrator', 'Configures local settings within central agreement constraints', 2),
    ('LOCAL_HR', 'Local HR Employee', 'Views/edits employee time registrations, org statistics', 3),
    ('LOCAL_LEADER', 'Local Leader', 'Approves/rejects time registration periods, team oversight', 4),
    ('EMPLOYEE', 'Employee', 'Registers own time, views own data', 5)
ON CONFLICT DO NOTHING;

-- Seed test organization hierarchy (Finansministeriet example)
INSERT INTO organizations (org_id, org_name, org_type, parent_org_id, materialized_path, agreement_code, ok_version) VALUES
    ('MIN01', 'Finansministeriet', 'MINISTRY', NULL, '/MIN01/', 'AC', 'OK24'),
    ('STY01', 'Medarbejder- og Kompetencestyrelsen', 'STYRELSE', 'MIN01', '/MIN01/STY01/', 'AC', 'OK24'),
    ('STY02', 'Statens IT', 'STYRELSE', 'MIN01', '/MIN01/STY02/', 'HK', 'OK24'),
    ('STY03', 'Ekonomistyrelsen', 'STYRELSE', 'MIN01', '/MIN01/STY03/', 'AC', 'OK24'),
    ('AFD01', 'IT-Drift', 'AFDELING', 'STY02', '/MIN01/STY02/AFD01/', 'HK', 'OK24'),
    ('AFD02', 'Systemudvikling', 'AFDELING', 'STY02', '/MIN01/STY02/AFD02/', 'PROSA', 'OK24')
ON CONFLICT DO NOTHING;

-- Seed test users (bcrypt hashes for simple dev passwords)
-- ALL users share the same dev password: "password" (bcrypt hash below)
-- admin01/password, ladm01/password, hr01/password, mgr01/password, emp001-003/password
-- Note: These are bcrypt($2a$10$) hashes for development ONLY — never use in production
INSERT INTO users (user_id, username, password_hash, display_name, email, primary_org_id, agreement_code, ok_version) VALUES
    ('admin01', 'admin01', '$2a$11$9d/J80pl7VKKjtWsSqJdPuvJqBL/3sYomNGgL.TdUKq2Aw0e6k0Te', 'Global Administrator', 'admin@statstid.dk', 'MIN01', 'AC', 'OK24'),
    ('hr01', 'hr01', '$2a$11$9d/J80pl7VKKjtWsSqJdPuvJqBL/3sYomNGgL.TdUKq2Aw0e6k0Te', 'HR Medarbejder', 'hr@statens-it.dk', 'STY02', 'HK', 'OK24'),
    ('mgr01', 'mgr01', '$2a$11$9d/J80pl7VKKjtWsSqJdPuvJqBL/3sYomNGgL.TdUKq2Aw0e6k0Te', 'Team Leder', 'leder@statens-it.dk', 'AFD01', 'HK', 'OK24'),
    ('emp001', 'emp001', '$2a$11$9d/J80pl7VKKjtWsSqJdPuvJqBL/3sYomNGgL.TdUKq2Aw0e6k0Te', 'AC Medarbejder', 'emp.ac@mfk.dk', 'STY01', 'AC', 'OK24'),
    ('emp002', 'emp002', '$2a$11$9d/J80pl7VKKjtWsSqJdPuvJqBL/3sYomNGgL.TdUKq2Aw0e6k0Te', 'HK Medarbejder', 'emp.hk@statens-it.dk', 'AFD01', 'HK', 'OK24'),
    ('emp003', 'emp003', '$2a$11$9d/J80pl7VKKjtWsSqJdPuvJqBL/3sYomNGgL.TdUKq2Aw0e6k0Te', 'PROSA Medarbejder', 'emp.prosa@statens-it.dk', 'AFD02', 'PROSA', 'OK24'),
    ('ladm01', 'ladm01', '$2a$11$9d/J80pl7VKKjtWsSqJdPuvJqBL/3sYomNGgL.TdUKq2Aw0e6k0Te', 'Lokal Administrator', 'lokal.admin@statens-it.dk', 'STY02', 'HK', 'OK24')
ON CONFLICT DO NOTHING;

-- Seed role assignments
-- admin01: Global Admin (covers everything)
-- ladm01: Local Admin for Statens IT (covers STY02 + descendants)
-- hr01: Local HR for Finansministeriet subtree (centralized HR in child org covering ministry)
-- mgr01: Local Leader for IT-Drift department
-- emp001-003: Employees at their respective orgs
INSERT INTO role_assignments (user_id, role_id, org_id, scope_type, assigned_by) VALUES
    ('admin01', 'GLOBAL_ADMIN', NULL, 'GLOBAL', 'system'),
    ('ladm01', 'LOCAL_ADMIN', 'STY02', 'ORG_AND_DESCENDANTS', 'admin01'),
    ('hr01', 'LOCAL_HR', 'MIN01', 'ORG_AND_DESCENDANTS', 'admin01'),
    ('mgr01', 'LOCAL_LEADER', 'AFD01', 'ORG_AND_DESCENDANTS', 'ladm01'),
    ('emp001', 'EMPLOYEE', 'STY01', 'ORG_ONLY', 'admin01'),
    ('emp002', 'EMPLOYEE', 'AFD01', 'ORG_ONLY', 'mgr01'),
    ('emp003', 'EMPLOYEE', 'AFD02', 'ORG_ONLY', 'ladm01')
ON CONFLICT DO NOTHING;

-- ============================================================
-- SPRINT 9: Skema tables
-- ============================================================

CREATE TABLE IF NOT EXISTS projects (
    project_id      UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    org_id          TEXT        NOT NULL REFERENCES organizations(org_id),
    project_code    TEXT        NOT NULL,
    project_name    TEXT        NOT NULL,
    is_active       BOOLEAN     NOT NULL DEFAULT TRUE,
    sort_order      INT         NOT NULL DEFAULT 0,
    created_by      TEXT        NOT NULL,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (org_id, project_code)
);
CREATE INDEX IF NOT EXISTS idx_projects_org ON projects(org_id);

CREATE TABLE IF NOT EXISTS timer_sessions (
    session_id      UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    employee_id     TEXT        NOT NULL,
    date            DATE        NOT NULL,
    check_in_at     TIMESTAMPTZ NOT NULL,
    check_out_at    TIMESTAMPTZ,
    is_active       BOOLEAN     NOT NULL DEFAULT TRUE,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_timer_employee ON timer_sessions(employee_id);
CREATE INDEX IF NOT EXISTS idx_timer_active ON timer_sessions(is_active) WHERE is_active = TRUE;

CREATE TABLE IF NOT EXISTS absence_type_visibility (
    id              UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    org_id          TEXT        NOT NULL REFERENCES organizations(org_id),
    absence_type    TEXT        NOT NULL,
    is_hidden       BOOLEAN     NOT NULL DEFAULT FALSE,
    set_by          TEXT        NOT NULL,
    set_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (org_id, absence_type)
);
CREATE INDEX IF NOT EXISTS idx_absence_vis_org ON absence_type_visibility(org_id);

-- Alter approval_periods for employee self-approval workflow
ALTER TABLE approval_periods ADD COLUMN IF NOT EXISTS employee_approved_at TIMESTAMPTZ;
ALTER TABLE approval_periods ADD COLUMN IF NOT EXISTS employee_approved_by TEXT;
ALTER TABLE approval_periods ADD COLUMN IF NOT EXISTS employee_deadline DATE;
ALTER TABLE approval_periods ADD COLUMN IF NOT EXISTS manager_deadline DATE;
ALTER TABLE approval_periods DROP CONSTRAINT IF EXISTS approval_periods_status_check;
ALTER TABLE approval_periods ADD CONSTRAINT approval_periods_status_check
    CHECK (status IN ('DRAFT', 'EMPLOYEE_APPROVED', 'SUBMITTED', 'APPROVED', 'REJECTED'));

-- ============================================================
-- SPRINT 7 SEED DATA: Local Configurations
-- ============================================================

-- Seed test local configuration overrides for Statens IT (STY02)
-- These demonstrate local config within central constraints
INSERT INTO local_configurations (config_id, org_id, config_area, config_key, config_value, effective_from, agreement_code, ok_version, created_by) VALUES
    ('a0000001-0000-0000-0000-000000000001', 'STY02', 'FLEX_RULES', 'MaxFlexBalance', '"80.0"', '2024-01-01', 'HK', 'OK24', 'ladm01'),
    ('a0000001-0000-0000-0000-000000000002', 'STY02', 'WORKING_TIME', 'PlanningStartDay', '"MONDAY"', '2024-01-01', 'HK', 'OK24', 'ladm01'),
    ('a0000001-0000-0000-0000-000000000003', 'AFD01', 'OPERATIONAL', 'ApprovalCutoffDay', '"25"', '2024-01-01', 'HK', 'OK24', 'ladm01')
ON CONFLICT DO NOTHING;

-- ============================================================
-- SPRINT 9 SEED DATA: SICK_DAY wage type + projects
-- ============================================================

-- SICK_DAY wage type mappings (all 3 agreements x 2 OK versions)
INSERT INTO wage_type_mappings (time_type, wage_type, ok_version, agreement_code, description, effective_from) VALUES
    ('SICK_DAY', 'SLS_0540', 'OK24', 'AC', 'Sick day', '2020-01-01'),
    ('SICK_DAY', 'SLS_0540', 'OK24', 'HK', 'Sick day', '2020-01-01'),
    ('SICK_DAY', 'SLS_0540', 'OK24', 'PROSA', 'Sick day', '2020-01-01'),
    ('SICK_DAY', 'SLS_0540', 'OK26', 'AC', 'Sick day', '2020-01-01'),
    ('SICK_DAY', 'SLS_0540', 'OK26', 'HK', 'Sick day', '2020-01-01'),
    ('SICK_DAY', 'SLS_0540', 'OK26', 'PROSA', 'Sick day', '2020-01-01')
ON CONFLICT (time_type, ok_version, agreement_code, position, effective_from) DO NOTHING;

-- Sample projects for test orgs
INSERT INTO projects (org_id, project_code, project_name, sort_order, created_by) VALUES
    ('AFD01', 'DRIFT-01', 'Daglig drift', 1, 'ladm01'),
    ('AFD01', 'PROJ-ALPHA', 'Projekt Alpha', 2, 'ladm01'),
    ('AFD01', 'PROJ-BETA', 'Projekt Beta', 3, 'ladm01'),
    ('AFD02', 'SYSDEV-01', 'Systemudvikling', 1, 'ladm01'),
    ('AFD02', 'VEDL-01', 'Vedligeholdelse', 2, 'ladm01')
ON CONFLICT DO NOTHING;

-- ============================================================
-- SPRINT 11: Position Registry
-- ============================================================

CREATE TABLE IF NOT EXISTS positions (
    position_code   TEXT        PRIMARY KEY,
    display_label   TEXT        NOT NULL,
    agreement_code  TEXT        NOT NULL,
    is_active       BOOLEAN     NOT NULL DEFAULT true,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Seed initial AC position codes
INSERT INTO positions (position_code, display_label, agreement_code) VALUES
    ('DEPARTMENT_HEAD', 'Kontorchef', 'AC'),
    ('RESEARCHER', 'Forsker', 'AC'),
    ('SPECIALIST', 'Specialkonsulent', 'AC'),
    ('TEACHING_STAFF', 'Undervisningspersonale', 'AC')
ON CONFLICT DO NOTHING;

-- ============================================================
-- SPRINT 11 SEED DATA: Academic agreement wage type mappings
-- ============================================================

-- S37 TASK-3702 Bug #2 absorption (2026-05-21): AC variants wage_type_mappings rewritten to mirror AC base
-- per interim-expert decision. Resolves: (1) CHILD_SICK_1 phantom rows (rule engine emits CHILD_SICK_DAY
-- per AbsenceRule.cs:112-114, not CHILD_SICK_1, so prior AC variant child-sick events silently dropped);
-- (2) MERARBEJDE → SLS_0210 collision with HK/PROSA OVERTIME_50 → SLS_0210 (now both flow to distinct codes);
-- (3) 4 other divergent SLS codes (CARE_DAY/SENIOR_DAY/LEAVE_WITH_PAY/LEAVE_WITHOUT_PAY) realigned to AC base.
-- Bug-with-no-past-impact under pre-launch posture. See SR-AC_RESEARCH-OK24-006 + SR-AC_TEACHING-OK24-006.

-- AC_RESEARCH OK24
INSERT INTO wage_type_mappings (time_type, wage_type, ok_version, agreement_code, description, effective_from) VALUES
    ('NORMAL_HOURS',      'SLS_0110', 'OK24', 'AC_RESEARCH', 'Normal hours',                       '2020-01-01'),
    ('MERARBEJDE',        'SLS_0310', 'OK24', 'AC_RESEARCH', 'Merarbejde (extra work)',            '2020-01-01'),
    ('VACATION',          'SLS_0510', 'OK24', 'AC_RESEARCH', 'Vacation',                           '2020-01-01'),
    ('SICK_DAY',          'SLS_0540', 'OK24', 'AC_RESEARCH', 'Sick day',                           '2020-01-01'),
    ('CARE_DAY',          'SLS_0520', 'OK24', 'AC_RESEARCH', 'Care day (omsorgsdage)',             '2020-01-01'),
    ('CHILD_SICK_DAY',    'SLS_0530', 'OK24', 'AC_RESEARCH', 'Childs 1st sick day',                '2020-01-01'),
    ('CHILD_SICK_DAY_2',  'SLS_0531', 'OK24', 'AC_RESEARCH', 'Childs 2nd sick day',                '2020-01-01'),
    ('CHILD_SICK_DAY_3',  'SLS_0532', 'OK24', 'AC_RESEARCH', 'Childs 3rd sick day',                '2020-01-01'),
    ('SENIOR_DAY',        'SLS_0550', 'OK24', 'AC_RESEARCH', 'Senior day',                         '2020-01-01'),
    ('LEAVE_WITH_PAY',    'SLS_0565', 'OK24', 'AC_RESEARCH', 'Leave with pay',                     '2020-01-01'),
    ('LEAVE_WITHOUT_PAY', 'SLS_0560', 'OK24', 'AC_RESEARCH', 'Leave without pay',                  '2020-01-01'),
    ('TRAVEL_WORK',       'SLS_0820', 'OK24', 'AC_RESEARCH', 'Travel time (working)',              '2020-01-01'),
    ('TRAVEL_NON_WORK',   'SLS_0830', 'OK24', 'AC_RESEARCH', 'Travel time (non-working)',          '2020-01-01')
ON CONFLICT (time_type, ok_version, agreement_code, position, effective_from) DO NOTHING;

-- AC_RESEARCH OK26
INSERT INTO wage_type_mappings (time_type, wage_type, ok_version, agreement_code, description, effective_from) VALUES
    ('NORMAL_HOURS',      'SLS_0110', 'OK26', 'AC_RESEARCH', 'Normal hours',                       '2020-01-01'),
    ('MERARBEJDE',        'SLS_0310', 'OK26', 'AC_RESEARCH', 'Merarbejde (extra work)',            '2020-01-01'),
    ('VACATION',          'SLS_0510', 'OK26', 'AC_RESEARCH', 'Vacation',                           '2020-01-01'),
    ('SICK_DAY',          'SLS_0540', 'OK26', 'AC_RESEARCH', 'Sick day',                           '2020-01-01'),
    ('CARE_DAY',          'SLS_0520', 'OK26', 'AC_RESEARCH', 'Care day (omsorgsdage)',             '2020-01-01'),
    ('CHILD_SICK_DAY',    'SLS_0530', 'OK26', 'AC_RESEARCH', 'Childs 1st sick day',                '2020-01-01'),
    ('CHILD_SICK_DAY_2',  'SLS_0531', 'OK26', 'AC_RESEARCH', 'Childs 2nd sick day',                '2020-01-01'),
    ('CHILD_SICK_DAY_3',  'SLS_0532', 'OK26', 'AC_RESEARCH', 'Childs 3rd sick day',                '2020-01-01'),
    ('SENIOR_DAY',        'SLS_0550', 'OK26', 'AC_RESEARCH', 'Senior day',                         '2020-01-01'),
    ('LEAVE_WITH_PAY',    'SLS_0565', 'OK26', 'AC_RESEARCH', 'Leave with pay',                     '2020-01-01'),
    ('LEAVE_WITHOUT_PAY', 'SLS_0560', 'OK26', 'AC_RESEARCH', 'Leave without pay',                  '2020-01-01'),
    ('TRAVEL_WORK',       'SLS_0820', 'OK26', 'AC_RESEARCH', 'Travel time (working)',              '2020-01-01'),
    ('TRAVEL_NON_WORK',   'SLS_0830', 'OK26', 'AC_RESEARCH', 'Travel time (non-working)',          '2020-01-01')
ON CONFLICT (time_type, ok_version, agreement_code, position, effective_from) DO NOTHING;

-- AC_TEACHING OK24
INSERT INTO wage_type_mappings (time_type, wage_type, ok_version, agreement_code, description, effective_from) VALUES
    ('NORMAL_HOURS',      'SLS_0110', 'OK24', 'AC_TEACHING', 'Normal hours',                       '2020-01-01'),
    ('MERARBEJDE',        'SLS_0310', 'OK24', 'AC_TEACHING', 'Merarbejde (extra work)',            '2020-01-01'),
    ('VACATION',          'SLS_0510', 'OK24', 'AC_TEACHING', 'Vacation',                           '2020-01-01'),
    ('SICK_DAY',          'SLS_0540', 'OK24', 'AC_TEACHING', 'Sick day',                           '2020-01-01'),
    ('CARE_DAY',          'SLS_0520', 'OK24', 'AC_TEACHING', 'Care day (omsorgsdage)',             '2020-01-01'),
    ('CHILD_SICK_DAY',    'SLS_0530', 'OK24', 'AC_TEACHING', 'Childs 1st sick day',                '2020-01-01'),
    ('CHILD_SICK_DAY_2',  'SLS_0531', 'OK24', 'AC_TEACHING', 'Childs 2nd sick day',                '2020-01-01'),
    ('CHILD_SICK_DAY_3',  'SLS_0532', 'OK24', 'AC_TEACHING', 'Childs 3rd sick day',                '2020-01-01'),
    ('SENIOR_DAY',        'SLS_0550', 'OK24', 'AC_TEACHING', 'Senior day',                         '2020-01-01'),
    ('LEAVE_WITH_PAY',    'SLS_0565', 'OK24', 'AC_TEACHING', 'Leave with pay',                     '2020-01-01'),
    ('LEAVE_WITHOUT_PAY', 'SLS_0560', 'OK24', 'AC_TEACHING', 'Leave without pay',                  '2020-01-01'),
    ('TRAVEL_WORK',       'SLS_0820', 'OK24', 'AC_TEACHING', 'Travel time (working)',              '2020-01-01'),
    ('TRAVEL_NON_WORK',   'SLS_0830', 'OK24', 'AC_TEACHING', 'Travel time (non-working)',          '2020-01-01')
ON CONFLICT (time_type, ok_version, agreement_code, position, effective_from) DO NOTHING;

-- AC_TEACHING OK26
INSERT INTO wage_type_mappings (time_type, wage_type, ok_version, agreement_code, description, effective_from) VALUES
    ('NORMAL_HOURS',      'SLS_0110', 'OK26', 'AC_TEACHING', 'Normal hours',                       '2020-01-01'),
    ('MERARBEJDE',        'SLS_0310', 'OK26', 'AC_TEACHING', 'Merarbejde (extra work)',            '2020-01-01'),
    ('VACATION',          'SLS_0510', 'OK26', 'AC_TEACHING', 'Vacation',                           '2020-01-01'),
    ('SICK_DAY',          'SLS_0540', 'OK26', 'AC_TEACHING', 'Sick day',                           '2020-01-01'),
    ('CARE_DAY',          'SLS_0520', 'OK26', 'AC_TEACHING', 'Care day (omsorgsdage)',             '2020-01-01'),
    ('CHILD_SICK_DAY',    'SLS_0530', 'OK26', 'AC_TEACHING', 'Childs 1st sick day',                '2020-01-01'),
    ('CHILD_SICK_DAY_2',  'SLS_0531', 'OK26', 'AC_TEACHING', 'Childs 2nd sick day',                '2020-01-01'),
    ('CHILD_SICK_DAY_3',  'SLS_0532', 'OK26', 'AC_TEACHING', 'Childs 3rd sick day',                '2020-01-01'),
    ('SENIOR_DAY',        'SLS_0550', 'OK26', 'AC_TEACHING', 'Senior day',                         '2020-01-01'),
    ('LEAVE_WITH_PAY',    'SLS_0565', 'OK26', 'AC_TEACHING', 'Leave with pay',                     '2020-01-01'),
    ('LEAVE_WITHOUT_PAY', 'SLS_0560', 'OK26', 'AC_TEACHING', 'Leave without pay',                  '2020-01-01'),
    ('TRAVEL_WORK',       'SLS_0820', 'OK26', 'AC_TEACHING', 'Travel time (working)',              '2020-01-01'),
    ('TRAVEL_NON_WORK',   'SLS_0830', 'OK26', 'AC_TEACHING', 'Travel time (non-working)',          '2020-01-01')
ON CONFLICT (time_type, ok_version, agreement_code, position, effective_from) DO NOTHING;

-- ============================================================
-- SPRINT 12: Agreement Configuration Management (ADR-014)
-- ============================================================

-- Agreement configs table — single source of truth for all agreement rule configs
CREATE TABLE IF NOT EXISTS agreement_configs (
    config_id               UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    agreement_code          TEXT        NOT NULL,
    ok_version              TEXT        NOT NULL,
    status                  TEXT        NOT NULL DEFAULT 'DRAFT' CHECK (status IN ('DRAFT', 'ACTIVE', 'ARCHIVED')),
    -- Norm & Flex
    weekly_norm_hours       DECIMAL     NOT NULL,
    norm_period_weeks       INT         NOT NULL DEFAULT 1,
    norm_model              TEXT        NOT NULL DEFAULT 'WEEKLY_HOURS',
    annual_norm_hours       DECIMAL     NOT NULL DEFAULT 1924,
    max_flex_balance        DECIMAL     NOT NULL,
    flex_carryover_max      DECIMAL     NOT NULL,
    -- Overtime
    has_overtime            BOOLEAN     NOT NULL,
    has_merarbejde          BOOLEAN     NOT NULL,
    overtime_threshold_50   DECIMAL     NOT NULL DEFAULT 37.0,
    overtime_threshold_100  DECIMAL     NOT NULL DEFAULT 40.0,
    -- Supplements
    evening_supplement_enabled  BOOLEAN NOT NULL DEFAULT FALSE,
    night_supplement_enabled    BOOLEAN NOT NULL DEFAULT FALSE,
    weekend_supplement_enabled  BOOLEAN NOT NULL DEFAULT FALSE,
    holiday_supplement_enabled  BOOLEAN NOT NULL DEFAULT FALSE,
    evening_start           INT         NOT NULL DEFAULT 17,
    evening_end             INT         NOT NULL DEFAULT 23,
    night_start             INT         NOT NULL DEFAULT 23,
    night_end               INT         NOT NULL DEFAULT 6,
    evening_rate            DECIMAL     NOT NULL DEFAULT 1.25,
    night_rate              DECIMAL     NOT NULL DEFAULT 1.50,
    weekend_saturday_rate   DECIMAL     NOT NULL DEFAULT 1.50,
    weekend_sunday_rate     DECIMAL     NOT NULL DEFAULT 2.0,
    holiday_rate            DECIMAL     NOT NULL DEFAULT 2.0,
    -- On-call
    on_call_duty_enabled    BOOLEAN     NOT NULL DEFAULT FALSE,
    on_call_duty_rate       DECIMAL     NOT NULL DEFAULT 0.33,
    call_in_work_enabled    BOOLEAN     NOT NULL DEFAULT FALSE,
    call_in_minimum_hours   DECIMAL     NOT NULL DEFAULT 3.0,
    call_in_rate            DECIMAL     NOT NULL DEFAULT 1.0,
    -- Travel
    travel_time_enabled     BOOLEAN     NOT NULL DEFAULT FALSE,
    working_travel_rate     DECIMAL     NOT NULL DEFAULT 1.0,
    non_working_travel_rate DECIMAL     NOT NULL DEFAULT 0.5,
    -- Working time compliance (Sprint 16)
    max_daily_hours                 DECIMAL     NOT NULL DEFAULT 13.0,
    minimum_rest_hours              DECIMAL     NOT NULL DEFAULT 11.0,
    rest_period_derogation_allowed  BOOLEAN     NOT NULL DEFAULT FALSE,
    weekly_max_hours_reference_period INT       NOT NULL DEFAULT 17,
    voluntary_unsocial_hours_allowed BOOLEAN    NOT NULL DEFAULT TRUE,
    -- Overtime governance & compensation (Sprint 17)
    default_compensation_model      TEXT        NOT NULL DEFAULT 'UDBETALING',
    employee_compensation_choice    BOOLEAN     NOT NULL DEFAULT FALSE,
    max_overtime_hours_per_period    DECIMAL     NOT NULL DEFAULT 0,
    overtime_requires_pre_approval  BOOLEAN     NOT NULL DEFAULT FALSE,
    -- Metadata
    created_by              TEXT        NOT NULL DEFAULT 'SYSTEM_SEED',
    created_at              TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    published_at            TIMESTAMPTZ,
    archived_at             TIMESTAMPTZ,
    cloned_from_id          UUID        REFERENCES agreement_configs(config_id),
    description             TEXT
);

-- Only one ACTIVE per (agreement_code, ok_version)
CREATE UNIQUE INDEX IF NOT EXISTS idx_agreement_configs_active
    ON agreement_configs (agreement_code, ok_version) WHERE status = 'ACTIVE';

CREATE INDEX IF NOT EXISTS idx_agreement_configs_code_version
    ON agreement_configs (agreement_code, ok_version);
CREATE INDEX IF NOT EXISTS idx_agreement_configs_status
    ON agreement_configs (status);

-- Agreement config audit trail (append-only)
CREATE TABLE IF NOT EXISTS agreement_config_audit (
    audit_id        BIGSERIAL   PRIMARY KEY,
    config_id       UUID        NOT NULL,
    action          TEXT        NOT NULL CHECK (action IN ('CREATED', 'UPDATED', 'PUBLISHED', 'ARCHIVED', 'CLONED')),
    previous_data   JSONB,
    new_data        JSONB,
    actor_id        TEXT        NOT NULL,
    actor_role      TEXT        NOT NULL,
    timestamp       TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_agreement_config_audit_config
    ON agreement_config_audit(config_id);

-- Compensatory rest tracking (Sprint 16: Working Time Compliance)
CREATE TABLE IF NOT EXISTS compensatory_rest (
    id                  UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    employee_id         TEXT        NOT NULL,
    source_date         DATE        NOT NULL,
    compensatory_date   DATE,
    hours               DECIMAL     NOT NULL,
    status              TEXT        NOT NULL DEFAULT 'PENDING' CHECK (status IN ('PENDING', 'GRANTED', 'EXPIRED')),
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_compensatory_rest_employee
    ON compensatory_rest(employee_id);
CREATE INDEX IF NOT EXISTS idx_compensatory_rest_status
    ON compensatory_rest(status);

-- Seed 10 agreement configs from CentralAgreementConfigs (status=ACTIVE)
-- AC OK24
INSERT INTO agreement_configs (agreement_code, ok_version, status, weekly_norm_hours, norm_period_weeks, norm_model, annual_norm_hours, max_flex_balance, flex_carryover_max, has_overtime, has_merarbejde, overtime_threshold_50, overtime_threshold_100, evening_supplement_enabled, night_supplement_enabled, weekend_supplement_enabled, holiday_supplement_enabled, evening_start, evening_end, night_start, night_end, evening_rate, night_rate, weekend_saturday_rate, weekend_sunday_rate, holiday_rate, on_call_duty_enabled, on_call_duty_rate, call_in_work_enabled, call_in_minimum_hours, call_in_rate, travel_time_enabled, working_travel_rate, non_working_travel_rate, max_daily_hours, minimum_rest_hours, rest_period_derogation_allowed, weekly_max_hours_reference_period, voluntary_unsocial_hours_allowed, default_compensation_model, employee_compensation_choice, max_overtime_hours_per_period, overtime_requires_pre_approval, created_by, published_at, description)
VALUES
('AC', 'OK24', 'ACTIVE', 37.0, 1, 'WEEKLY_HOURS', 1924, 150.0, 150.0, FALSE, TRUE, 37.0, 40.0, FALSE, FALSE, FALSE, FALSE, 17, 23, 23, 6, 1.25, 1.50, 1.50, 2.0, 2.0, FALSE, 0.33, FALSE, 3.0, 1.0, TRUE, 1.0, 0.5, 13.0, 11.0, FALSE, 17, TRUE, 'AFSPADSERING', FALSE, 0, FALSE, 'SYSTEM_SEED', NOW(), 'AC OK24 — Standard akademiker agreement')
ON CONFLICT DO NOTHING;

-- AC OK26
INSERT INTO agreement_configs (agreement_code, ok_version, status, weekly_norm_hours, norm_period_weeks, norm_model, annual_norm_hours, max_flex_balance, flex_carryover_max, has_overtime, has_merarbejde, overtime_threshold_50, overtime_threshold_100, evening_supplement_enabled, night_supplement_enabled, weekend_supplement_enabled, holiday_supplement_enabled, evening_start, evening_end, night_start, night_end, evening_rate, night_rate, weekend_saturday_rate, weekend_sunday_rate, holiday_rate, on_call_duty_enabled, on_call_duty_rate, call_in_work_enabled, call_in_minimum_hours, call_in_rate, travel_time_enabled, working_travel_rate, non_working_travel_rate, max_daily_hours, minimum_rest_hours, rest_period_derogation_allowed, weekly_max_hours_reference_period, voluntary_unsocial_hours_allowed, default_compensation_model, employee_compensation_choice, max_overtime_hours_per_period, overtime_requires_pre_approval, created_by, published_at, description)
VALUES
('AC', 'OK26', 'ACTIVE', 37.0, 1, 'WEEKLY_HOURS', 1924, 150.0, 150.0, FALSE, TRUE, 37.0, 40.0, FALSE, FALSE, FALSE, FALSE, 17, 23, 23, 6, 1.25, 1.50, 1.50, 2.0, 2.0, FALSE, 0.33, FALSE, 3.0, 1.0, TRUE, 1.0, 0.5, 13.0, 11.0, FALSE, 17, TRUE, 'AFSPADSERING', FALSE, 0, FALSE, 'SYSTEM_SEED', NOW(), 'AC OK26 — Standard akademiker agreement')
ON CONFLICT DO NOTHING;

-- HK OK24
INSERT INTO agreement_configs (agreement_code, ok_version, status, weekly_norm_hours, norm_period_weeks, norm_model, annual_norm_hours, max_flex_balance, flex_carryover_max, has_overtime, has_merarbejde, overtime_threshold_50, overtime_threshold_100, evening_supplement_enabled, night_supplement_enabled, weekend_supplement_enabled, holiday_supplement_enabled, evening_start, evening_end, night_start, night_end, evening_rate, night_rate, weekend_saturday_rate, weekend_sunday_rate, holiday_rate, on_call_duty_enabled, on_call_duty_rate, call_in_work_enabled, call_in_minimum_hours, call_in_rate, travel_time_enabled, working_travel_rate, non_working_travel_rate, max_daily_hours, minimum_rest_hours, rest_period_derogation_allowed, weekly_max_hours_reference_period, voluntary_unsocial_hours_allowed, default_compensation_model, employee_compensation_choice, max_overtime_hours_per_period, overtime_requires_pre_approval, created_by, published_at, description)
VALUES
('HK', 'OK24', 'ACTIVE', 37.0, 1, 'WEEKLY_HOURS', 1924, 100.0, 100.0, TRUE, FALSE, 37.0, 40.0, TRUE, TRUE, TRUE, TRUE, 17, 23, 23, 6, 1.25, 1.50, 1.50, 2.0, 2.0, TRUE, 0.33, TRUE, 3.0, 1.0, TRUE, 1.0, 0.5, 13.0, 11.0, TRUE, 17, TRUE, 'AFSPADSERING', TRUE, 0, FALSE, 'SYSTEM_SEED', NOW(), 'HK OK24 — Handels- og kontorfunktionærer')
ON CONFLICT DO NOTHING;

-- HK OK26
INSERT INTO agreement_configs (agreement_code, ok_version, status, weekly_norm_hours, norm_period_weeks, norm_model, annual_norm_hours, max_flex_balance, flex_carryover_max, has_overtime, has_merarbejde, overtime_threshold_50, overtime_threshold_100, evening_supplement_enabled, night_supplement_enabled, weekend_supplement_enabled, holiday_supplement_enabled, evening_start, evening_end, night_start, night_end, evening_rate, night_rate, weekend_saturday_rate, weekend_sunday_rate, holiday_rate, on_call_duty_enabled, on_call_duty_rate, call_in_work_enabled, call_in_minimum_hours, call_in_rate, travel_time_enabled, working_travel_rate, non_working_travel_rate, max_daily_hours, minimum_rest_hours, rest_period_derogation_allowed, weekly_max_hours_reference_period, voluntary_unsocial_hours_allowed, default_compensation_model, employee_compensation_choice, max_overtime_hours_per_period, overtime_requires_pre_approval, created_by, published_at, description)
VALUES
('HK', 'OK26', 'ACTIVE', 37.0, 1, 'WEEKLY_HOURS', 1924, 100.0, 100.0, TRUE, FALSE, 37.0, 40.0, TRUE, TRUE, TRUE, TRUE, 17, 23, 23, 6, 1.25, 1.50, 1.50, 2.0, 2.0, TRUE, 0.33, TRUE, 3.0, 1.0, TRUE, 1.0, 0.5, 13.0, 11.0, TRUE, 17, TRUE, 'AFSPADSERING', TRUE, 0, FALSE, 'SYSTEM_SEED', NOW(), 'HK OK26 — Handels- og kontorfunktionærer')
ON CONFLICT DO NOTHING;

-- PROSA OK24
INSERT INTO agreement_configs (agreement_code, ok_version, status, weekly_norm_hours, norm_period_weeks, norm_model, annual_norm_hours, max_flex_balance, flex_carryover_max, has_overtime, has_merarbejde, overtime_threshold_50, overtime_threshold_100, evening_supplement_enabled, night_supplement_enabled, weekend_supplement_enabled, holiday_supplement_enabled, evening_start, evening_end, night_start, night_end, evening_rate, night_rate, weekend_saturday_rate, weekend_sunday_rate, holiday_rate, on_call_duty_enabled, on_call_duty_rate, call_in_work_enabled, call_in_minimum_hours, call_in_rate, travel_time_enabled, working_travel_rate, non_working_travel_rate, max_daily_hours, minimum_rest_hours, rest_period_derogation_allowed, weekly_max_hours_reference_period, voluntary_unsocial_hours_allowed, default_compensation_model, employee_compensation_choice, max_overtime_hours_per_period, overtime_requires_pre_approval, created_by, published_at, description)
VALUES
('PROSA', 'OK24', 'ACTIVE', 37.0, 1, 'WEEKLY_HOURS', 1924, 120.0, 120.0, TRUE, FALSE, 37.0, 40.0, TRUE, TRUE, TRUE, TRUE, 17, 23, 23, 6, 1.25, 1.50, 1.50, 2.0, 2.0, TRUE, 0.33, TRUE, 3.0, 1.0, TRUE, 1.0, 0.5, 13.0, 11.0, TRUE, 17, TRUE, 'AFSPADSERING', TRUE, 0, FALSE, 'SYSTEM_SEED', NOW(), 'PROSA OK24 — IT-faglig organisation')
ON CONFLICT DO NOTHING;

-- PROSA OK26
INSERT INTO agreement_configs (agreement_code, ok_version, status, weekly_norm_hours, norm_period_weeks, norm_model, annual_norm_hours, max_flex_balance, flex_carryover_max, has_overtime, has_merarbejde, overtime_threshold_50, overtime_threshold_100, evening_supplement_enabled, night_supplement_enabled, weekend_supplement_enabled, holiday_supplement_enabled, evening_start, evening_end, night_start, night_end, evening_rate, night_rate, weekend_saturday_rate, weekend_sunday_rate, holiday_rate, on_call_duty_enabled, on_call_duty_rate, call_in_work_enabled, call_in_minimum_hours, call_in_rate, travel_time_enabled, working_travel_rate, non_working_travel_rate, max_daily_hours, minimum_rest_hours, rest_period_derogation_allowed, weekly_max_hours_reference_period, voluntary_unsocial_hours_allowed, default_compensation_model, employee_compensation_choice, max_overtime_hours_per_period, overtime_requires_pre_approval, created_by, published_at, description)
VALUES
('PROSA', 'OK26', 'ACTIVE', 37.0, 1, 'WEEKLY_HOURS', 1924, 120.0, 120.0, TRUE, FALSE, 37.0, 40.0, TRUE, TRUE, TRUE, TRUE, 17, 23, 23, 6, 1.25, 1.50, 1.50, 2.0, 2.0, TRUE, 0.33, TRUE, 3.0, 1.0, TRUE, 1.0, 0.5, 13.0, 11.0, TRUE, 17, TRUE, 'AFSPADSERING', TRUE, 0, FALSE, 'SYSTEM_SEED', NOW(), 'PROSA OK26 — IT-faglig organisation')
ON CONFLICT DO NOTHING;

-- AC_RESEARCH OK24
INSERT INTO agreement_configs (agreement_code, ok_version, status, weekly_norm_hours, norm_period_weeks, norm_model, annual_norm_hours, max_flex_balance, flex_carryover_max, has_overtime, has_merarbejde, overtime_threshold_50, overtime_threshold_100, evening_supplement_enabled, night_supplement_enabled, weekend_supplement_enabled, holiday_supplement_enabled, evening_start, evening_end, night_start, night_end, evening_rate, night_rate, weekend_saturday_rate, weekend_sunday_rate, holiday_rate, on_call_duty_enabled, on_call_duty_rate, call_in_work_enabled, call_in_minimum_hours, call_in_rate, travel_time_enabled, working_travel_rate, non_working_travel_rate, max_daily_hours, minimum_rest_hours, rest_period_derogation_allowed, weekly_max_hours_reference_period, voluntary_unsocial_hours_allowed, default_compensation_model, employee_compensation_choice, max_overtime_hours_per_period, overtime_requires_pre_approval, created_by, published_at, description)
VALUES
('AC_RESEARCH', 'OK24', 'ACTIVE', 37.0, 1, 'ANNUAL_ACTIVITY', 1924, 150.0, 150.0, FALSE, TRUE, 37.0, 40.0, FALSE, FALSE, FALSE, FALSE, 17, 23, 23, 6, 1.25, 1.50, 1.50, 2.0, 2.0, FALSE, 0.33, FALSE, 3.0, 1.0, TRUE, 1.0, 0.5, 13.0, 11.0, FALSE, 17, TRUE, 'AFSPADSERING', FALSE, 0, FALSE, 'SYSTEM_SEED', NOW(), 'AC_RESEARCH OK24 — Researchers (annual norm 1924h)')
ON CONFLICT DO NOTHING;

-- AC_RESEARCH OK26
INSERT INTO agreement_configs (agreement_code, ok_version, status, weekly_norm_hours, norm_period_weeks, norm_model, annual_norm_hours, max_flex_balance, flex_carryover_max, has_overtime, has_merarbejde, overtime_threshold_50, overtime_threshold_100, evening_supplement_enabled, night_supplement_enabled, weekend_supplement_enabled, holiday_supplement_enabled, evening_start, evening_end, night_start, night_end, evening_rate, night_rate, weekend_saturday_rate, weekend_sunday_rate, holiday_rate, on_call_duty_enabled, on_call_duty_rate, call_in_work_enabled, call_in_minimum_hours, call_in_rate, travel_time_enabled, working_travel_rate, non_working_travel_rate, max_daily_hours, minimum_rest_hours, rest_period_derogation_allowed, weekly_max_hours_reference_period, voluntary_unsocial_hours_allowed, default_compensation_model, employee_compensation_choice, max_overtime_hours_per_period, overtime_requires_pre_approval, created_by, published_at, description)
VALUES
('AC_RESEARCH', 'OK26', 'ACTIVE', 37.0, 1, 'ANNUAL_ACTIVITY', 1924, 150.0, 150.0, FALSE, TRUE, 37.0, 40.0, FALSE, FALSE, FALSE, FALSE, 17, 23, 23, 6, 1.25, 1.50, 1.50, 2.0, 2.0, FALSE, 0.33, FALSE, 3.0, 1.0, TRUE, 1.0, 0.5, 13.0, 11.0, FALSE, 17, TRUE, 'AFSPADSERING', FALSE, 0, FALSE, 'SYSTEM_SEED', NOW(), 'AC_RESEARCH OK26 — Researchers (annual norm 1924h)')
ON CONFLICT DO NOTHING;

-- AC_TEACHING OK24
INSERT INTO agreement_configs (agreement_code, ok_version, status, weekly_norm_hours, norm_period_weeks, norm_model, annual_norm_hours, max_flex_balance, flex_carryover_max, has_overtime, has_merarbejde, overtime_threshold_50, overtime_threshold_100, evening_supplement_enabled, night_supplement_enabled, weekend_supplement_enabled, holiday_supplement_enabled, evening_start, evening_end, night_start, night_end, evening_rate, night_rate, weekend_saturday_rate, weekend_sunday_rate, holiday_rate, on_call_duty_enabled, on_call_duty_rate, call_in_work_enabled, call_in_minimum_hours, call_in_rate, travel_time_enabled, working_travel_rate, non_working_travel_rate, max_daily_hours, minimum_rest_hours, rest_period_derogation_allowed, weekly_max_hours_reference_period, voluntary_unsocial_hours_allowed, default_compensation_model, employee_compensation_choice, max_overtime_hours_per_period, overtime_requires_pre_approval, created_by, published_at, description)
VALUES
('AC_TEACHING', 'OK24', 'ACTIVE', 37.0, 1, 'ANNUAL_ACTIVITY', 1680, 150.0, 150.0, FALSE, TRUE, 37.0, 40.0, FALSE, FALSE, FALSE, FALSE, 17, 23, 23, 6, 1.25, 1.50, 1.50, 2.0, 2.0, FALSE, 0.33, FALSE, 3.0, 1.0, TRUE, 1.0, 0.5, 13.0, 11.0, FALSE, 17, TRUE, 'AFSPADSERING', FALSE, 0, FALSE, 'SYSTEM_SEED', NOW(), 'AC_TEACHING OK24 — Teaching staff (1680h annual norm)')
ON CONFLICT DO NOTHING;

-- AC_TEACHING OK26
INSERT INTO agreement_configs (agreement_code, ok_version, status, weekly_norm_hours, norm_period_weeks, norm_model, annual_norm_hours, max_flex_balance, flex_carryover_max, has_overtime, has_merarbejde, overtime_threshold_50, overtime_threshold_100, evening_supplement_enabled, night_supplement_enabled, weekend_supplement_enabled, holiday_supplement_enabled, evening_start, evening_end, night_start, night_end, evening_rate, night_rate, weekend_saturday_rate, weekend_sunday_rate, holiday_rate, on_call_duty_enabled, on_call_duty_rate, call_in_work_enabled, call_in_minimum_hours, call_in_rate, travel_time_enabled, working_travel_rate, non_working_travel_rate, max_daily_hours, minimum_rest_hours, rest_period_derogation_allowed, weekly_max_hours_reference_period, voluntary_unsocial_hours_allowed, default_compensation_model, employee_compensation_choice, max_overtime_hours_per_period, overtime_requires_pre_approval, created_by, published_at, description)
VALUES
('AC_TEACHING', 'OK26', 'ACTIVE', 37.0, 1, 'ANNUAL_ACTIVITY', 1680, 150.0, 150.0, FALSE, TRUE, 37.0, 40.0, FALSE, FALSE, FALSE, FALSE, 17, 23, 23, 6, 1.25, 1.50, 1.50, 2.0, 2.0, FALSE, 0.33, FALSE, 3.0, 1.0, TRUE, 1.0, 0.5, 13.0, 11.0, FALSE, 17, TRUE, 'AFSPADSERING', FALSE, 0, FALSE, 'SYSTEM_SEED', NOW(), 'AC_TEACHING OK26 — Teaching staff (1680h annual norm)')
ON CONFLICT DO NOTHING;

-- ============================================================
-- SPRINT 11 SEED DATA: NORM_DEVIATION wage type
-- ============================================================

-- NORM_DEVIATION wage type mappings (merarbejde from norm surplus, AC agreements only)
INSERT INTO wage_type_mappings (time_type, wage_type, ok_version, agreement_code, description, effective_from) VALUES
    ('NORM_DEVIATION', 'SLS_0150', 'OK24', 'AC', 'Norm deviation (merarbejde from norm surplus)', '2020-01-01'),
    ('NORM_DEVIATION', 'SLS_0150', 'OK24', 'AC_RESEARCH', 'Norm deviation (merarbejde from norm surplus)', '2020-01-01'),
    ('NORM_DEVIATION', 'SLS_0150', 'OK24', 'AC_TEACHING', 'Norm deviation (merarbejde from norm surplus)', '2020-01-01'),
    ('NORM_DEVIATION', 'SLS_0150', 'OK26', 'AC', 'Norm deviation (merarbejde from norm surplus)', '2020-01-01'),
    ('NORM_DEVIATION', 'SLS_0150', 'OK26', 'AC_RESEARCH', 'Norm deviation (merarbejde from norm surplus)', '2020-01-01'),
    ('NORM_DEVIATION', 'SLS_0150', 'OK26', 'AC_TEACHING', 'Norm deviation (merarbejde from norm surplus)', '2020-01-01')
ON CONFLICT (time_type, ok_version, agreement_code, position, effective_from) DO NOTHING;

-- ============================================================
-- SPRINT 14: Position Override Configs + Audit Tables
-- ============================================================

CREATE TABLE IF NOT EXISTS position_override_configs (
    override_id         UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    agreement_code      TEXT        NOT NULL,
    ok_version          TEXT        NOT NULL,
    position_code       TEXT        NOT NULL REFERENCES positions(position_code),
    status              TEXT        NOT NULL DEFAULT 'ACTIVE' CHECK (status IN ('ACTIVE', 'INACTIVE')),
    max_flex_balance    DECIMAL,
    flex_carryover_max  DECIMAL,
    norm_period_weeks   INT,
    weekly_norm_hours   DECIMAL,
    created_by          TEXT        NOT NULL DEFAULT 'SYSTEM_SEED',
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    description         TEXT
);

-- Only one ACTIVE override per (agreement_code, ok_version, position_code)
CREATE UNIQUE INDEX IF NOT EXISTS idx_position_override_active_unique
    ON position_override_configs (agreement_code, ok_version, position_code)
    WHERE status = 'ACTIVE';

CREATE TABLE IF NOT EXISTS position_override_config_audit (
    audit_id        BIGSERIAL   PRIMARY KEY,
    override_id     UUID        NOT NULL,
    action          TEXT        NOT NULL CHECK (action IN ('CREATED', 'UPDATED', 'ACTIVATED', 'DEACTIVATED')),
    previous_data   JSONB,
    new_data        JSONB,
    actor_id        TEXT        NOT NULL,
    actor_role      TEXT        NOT NULL,
    timestamp       TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS wage_type_mapping_audit (
    audit_id        BIGSERIAL   PRIMARY KEY,
    time_type       TEXT        NOT NULL,
    ok_version      TEXT        NOT NULL,
    agreement_code  TEXT        NOT NULL,
    position        TEXT        NOT NULL DEFAULT '',
    -- S29: SUPERSEDED added inline for greenfield (migration block widens legacy DBs to match).
    action          TEXT        NOT NULL CHECK (action IN ('CREATED', 'UPDATED', 'DELETED', 'SUPERSEDED')),
    previous_data   JSONB,
    new_data        JSONB,
    actor_id        TEXT        NOT NULL,
    actor_role      TEXT        NOT NULL,
    timestamp       TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Seed position overrides from PositionOverrideConfigs.cs (4 overrides)
INSERT INTO position_override_configs (agreement_code, ok_version, position_code, status, max_flex_balance, norm_period_weeks, created_by, description) VALUES
    ('AC', 'OK24', 'DEPARTMENT_HEAD', 'ACTIVE', 200.0, 4, 'SYSTEM_SEED', 'Kontorchef: higher flex cap, 4-week norm'),
    ('AC', 'OK26', 'DEPARTMENT_HEAD', 'ACTIVE', 200.0, 4, 'SYSTEM_SEED', 'Kontorchef: higher flex cap, 4-week norm'),
    ('AC', 'OK24', 'RESEARCHER', 'ACTIVE', NULL, 4, 'SYSTEM_SEED', 'Forsker: 4-week norm period'),
    ('AC', 'OK26', 'RESEARCHER', 'ACTIVE', NULL, 4, 'SYSTEM_SEED', 'Forsker: 4-week norm period')
ON CONFLICT DO NOTHING;

-- ============================================================
-- SPRINT 15: Entitlement Management Tables
-- ============================================================

-- S30 / ADR-021: effective-dating columns + partial-unique-index pattern baked
-- into the schema so greenfield bootstrap is single-pass. The migration block
-- further down in this file remains idempotent on top of this shape (each
-- ALTER is guarded by IF NOT EXISTS / DROP CONSTRAINT IF EXISTS) and is the
-- path for legacy environments still on the pre-S30 single-row schema.
-- `version` column is added by the S25 / ADR-019 migration block below.
CREATE TABLE IF NOT EXISTS entitlement_configs (
    config_id               UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    entitlement_type        TEXT        NOT NULL,
    agreement_code          TEXT        NOT NULL,
    ok_version              TEXT        NOT NULL,
    annual_quota            DECIMAL     NOT NULL,
    accrual_model           TEXT        NOT NULL DEFAULT 'IMMEDIATE',
    reset_month             INT         NOT NULL DEFAULT 1,
    carryover_max           DECIMAL     NOT NULL DEFAULT 0,
    pro_rate_by_part_time   BOOLEAN     NOT NULL DEFAULT true,
    is_per_episode          BOOLEAN     NOT NULL DEFAULT false,
    min_age                 INT,
    description             TEXT,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    effective_from          DATE        NOT NULL DEFAULT '0001-01-01',
    effective_to            DATE
);

-- ADR-021 D2: at most one open row per natural key (S21 D2.1 partial-unique pattern).
CREATE UNIQUE INDEX IF NOT EXISTS idx_ec_natural_key_open
    ON entitlement_configs (entitlement_type, agreement_code, ok_version)
    WHERE effective_to IS NULL;

-- ADR-021 D3 conflict target: forbids duplicate history rows on (natural_key, effective_from).
CREATE UNIQUE INDEX IF NOT EXISTS idx_ec_natural_key_history
    ON entitlement_configs (entitlement_type, agreement_code, ok_version, effective_from);

-- ADR-021: audit table for entitlement_configs admin CRUD. Mirrors
-- wage_type_mapping_audit (singular) post-S25 migration shape — version_before
-- + version_after baked directly into the base CREATE (NOT a separate S25-style
-- ALTER) because this table is brand-new in S30. action CHECK includes
-- SUPERSEDED inline (S29 wage_type_mapping_audit precedent). No FK on
-- config_id because supersession + soft-delete create FK-invalidating histories.
CREATE TABLE IF NOT EXISTS entitlement_config_audit (
    audit_id            BIGSERIAL   PRIMARY KEY,
    config_id           UUID        NOT NULL,
    entitlement_type    TEXT        NOT NULL,
    agreement_code      TEXT        NOT NULL,
    ok_version          TEXT        NOT NULL,
    action              TEXT        NOT NULL CHECK (action IN ('CREATED', 'UPDATED', 'DELETED', 'SUPERSEDED')),
    previous_data       JSONB,
    new_data            JSONB,
    version_before      BIGINT      NULL,
    version_after       BIGINT      NULL,
    actor_id            TEXT        NOT NULL,
    actor_role          TEXT        NOT NULL,
    timestamp           TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS entitlement_balances (
    balance_id              UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    employee_id             TEXT        NOT NULL,
    entitlement_type        TEXT        NOT NULL,
    entitlement_year        INT         NOT NULL,
    total_quota             DECIMAL     NOT NULL,
    used                    DECIMAL     NOT NULL DEFAULT 0,
    planned                 DECIMAL     NOT NULL DEFAULT 0,
    carryover_in            DECIMAL     NOT NULL DEFAULT 0,
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (employee_id, entitlement_type, entitlement_year)
);

-- Seed entitlement configs: AC/HK/PROSA × OK24/OK26 × 5 types = 30 rows
-- S30 TASK-3006 (ADR-021 D3): seed-INSERT rewrite to include effective_from
-- + ON CONFLICT on (natural_key, effective_from) targeting idx_ec_natural_key_history
-- so the block is idempotent across `docker compose down -v && up` re-runs (S29
-- TASK-2906 / ADR-020 D3 precedent on wage_type_mappings at init.sql:1335,1353).
-- Anchor '0001-01-01' matches the base CREATE TABLE default at L1129 — sentinel
-- "pre-launch" effective_from, NOT a real agreement-start date.
INSERT INTO entitlement_configs (entitlement_type, agreement_code, ok_version, annual_quota, accrual_model, reset_month, carryover_max, pro_rate_by_part_time, is_per_episode, min_age, description, effective_from) VALUES
    -- VACATION: 25 days, reset September, carryover 5
    ('VACATION', 'AC', 'OK24', 25, 'IMMEDIATE', 9, 5, true, false, NULL, 'Ferie – 25 dage', '0001-01-01'),
    ('VACATION', 'AC', 'OK26', 25, 'IMMEDIATE', 9, 5, true, false, NULL, 'Ferie – 25 dage', '0001-01-01'),
    ('VACATION', 'HK', 'OK24', 25, 'IMMEDIATE', 9, 5, true, false, NULL, 'Ferie – 25 dage', '0001-01-01'),
    ('VACATION', 'HK', 'OK26', 25, 'IMMEDIATE', 9, 5, true, false, NULL, 'Ferie – 25 dage', '0001-01-01'),
    ('VACATION', 'PROSA', 'OK24', 25, 'IMMEDIATE', 9, 5, true, false, NULL, 'Ferie – 25 dage', '0001-01-01'),
    ('VACATION', 'PROSA', 'OK26', 25, 'IMMEDIATE', 9, 5, true, false, NULL, 'Ferie – 25 dage', '0001-01-01'),
    -- SPECIAL_HOLIDAY: 5 days, reset September, no carryover
    ('SPECIAL_HOLIDAY', 'AC', 'OK24', 5, 'IMMEDIATE', 9, 0, true, false, NULL, 'Særlige feriedage – 5 dage', '0001-01-01'),
    ('SPECIAL_HOLIDAY', 'AC', 'OK26', 5, 'IMMEDIATE', 9, 0, true, false, NULL, 'Særlige feriedage – 5 dage', '0001-01-01'),
    ('SPECIAL_HOLIDAY', 'HK', 'OK24', 5, 'IMMEDIATE', 9, 0, true, false, NULL, 'Særlige feriedage – 5 dage', '0001-01-01'),
    ('SPECIAL_HOLIDAY', 'HK', 'OK26', 5, 'IMMEDIATE', 9, 0, true, false, NULL, 'Særlige feriedage – 5 dage', '0001-01-01'),
    ('SPECIAL_HOLIDAY', 'PROSA', 'OK24', 5, 'IMMEDIATE', 9, 0, true, false, NULL, 'Særlige feriedage – 5 dage', '0001-01-01'),
    ('SPECIAL_HOLIDAY', 'PROSA', 'OK26', 5, 'IMMEDIATE', 9, 0, true, false, NULL, 'Særlige feriedage – 5 dage', '0001-01-01'),
    -- CARE_DAY: 2 days, reset January, no carryover, not pro-rated
    ('CARE_DAY', 'AC', 'OK24', 2, 'IMMEDIATE', 1, 0, false, false, NULL, 'Omsorgsdage – 2 dage', '0001-01-01'),
    ('CARE_DAY', 'AC', 'OK26', 2, 'IMMEDIATE', 1, 0, false, false, NULL, 'Omsorgsdage – 2 dage', '0001-01-01'),
    ('CARE_DAY', 'HK', 'OK24', 2, 'IMMEDIATE', 1, 0, false, false, NULL, 'Omsorgsdage – 2 dage', '0001-01-01'),
    ('CARE_DAY', 'HK', 'OK26', 2, 'IMMEDIATE', 1, 0, false, false, NULL, 'Omsorgsdage – 2 dage', '0001-01-01'),
    ('CARE_DAY', 'PROSA', 'OK24', 2, 'IMMEDIATE', 1, 0, false, false, NULL, 'Omsorgsdage – 2 dage', '0001-01-01'),
    ('CARE_DAY', 'PROSA', 'OK26', 2, 'IMMEDIATE', 1, 0, false, false, NULL, 'Omsorgsdage – 2 dage', '0001-01-01'),
    -- CHILD_SICK: AC=1, HK=2, PROSA=3, per-episode
    ('CHILD_SICK', 'AC', 'OK24', 1, 'IMMEDIATE', 1, 0, false, true, NULL, 'Barn syg – 1 dag per episode', '0001-01-01'),
    ('CHILD_SICK', 'AC', 'OK26', 1, 'IMMEDIATE', 1, 0, false, true, NULL, 'Barn syg – 1 dag per episode', '0001-01-01'),
    ('CHILD_SICK', 'HK', 'OK24', 2, 'IMMEDIATE', 1, 0, false, true, NULL, 'Barn syg – 2 dage per episode', '0001-01-01'),
    ('CHILD_SICK', 'HK', 'OK26', 2, 'IMMEDIATE', 1, 0, false, true, NULL, 'Barn syg – 2 dage per episode', '0001-01-01'),
    ('CHILD_SICK', 'PROSA', 'OK24', 3, 'IMMEDIATE', 1, 0, false, true, NULL, 'Barn syg – 3 dage per episode', '0001-01-01'),
    ('CHILD_SICK', 'PROSA', 'OK26', 3, 'IMMEDIATE', 1, 0, false, true, NULL, 'Barn syg – 3 dage per episode', '0001-01-01'),
    -- SENIOR_DAY: 2 days/year for age 62+ (S37 TASK-3703 Bug #3 absorption 2026-05-21, Path B seed-side fix
    -- per interim-expert decision; previously paired-broken with quota=0 + min_age=60). Bug-with-no-past-impact.
    ('SENIOR_DAY', 'AC',    'OK24', 2, 'IMMEDIATE', 1, 0, false, false, 62, 'Seniordage – kræver alder 62+', '0001-01-01'),
    ('SENIOR_DAY', 'AC',    'OK26', 2, 'IMMEDIATE', 1, 0, false, false, 62, 'Seniordage – kræver alder 62+', '0001-01-01'),
    ('SENIOR_DAY', 'HK',    'OK24', 2, 'IMMEDIATE', 1, 0, false, false, 62, 'Seniordage – kræver alder 62+', '0001-01-01'),
    ('SENIOR_DAY', 'HK',    'OK26', 2, 'IMMEDIATE', 1, 0, false, false, 62, 'Seniordage – kræver alder 62+', '0001-01-01'),
    ('SENIOR_DAY', 'PROSA', 'OK24', 2, 'IMMEDIATE', 1, 0, false, false, 62, 'Seniordage – kræver alder 62+', '0001-01-01'),
    ('SENIOR_DAY', 'PROSA', 'OK26', 2, 'IMMEDIATE', 1, 0, false, false, 62, 'Seniordage – kræver alder 62+', '0001-01-01'),
    -- S37 TASK-3701 Bug #1 absorption: AC variants (AC_RESEARCH + AC_TEACHING) mirror AC base values
    -- per interim-expert decision 2026-05-21. Bug-with-no-past-impact under pre-launch posture.
    -- VACATION inherits Ferieloven (universal); other 4 inherit AC overenskomst by structural inheritance.
    ('VACATION',        'AC_RESEARCH', 'OK24', 25, 'IMMEDIATE', 9, 5, true,  false, NULL, 'Ferie – 25 dage',                  '0001-01-01'),
    ('VACATION',        'AC_RESEARCH', 'OK26', 25, 'IMMEDIATE', 9, 5, true,  false, NULL, 'Ferie – 25 dage',                  '0001-01-01'),
    ('VACATION',        'AC_TEACHING', 'OK24', 25, 'IMMEDIATE', 9, 5, true,  false, NULL, 'Ferie – 25 dage',                  '0001-01-01'),
    ('VACATION',        'AC_TEACHING', 'OK26', 25, 'IMMEDIATE', 9, 5, true,  false, NULL, 'Ferie – 25 dage',                  '0001-01-01'),
    ('SPECIAL_HOLIDAY', 'AC_RESEARCH', 'OK24',  5, 'IMMEDIATE', 9, 0, true,  false, NULL, 'Særlige feriedage – 5 dage',       '0001-01-01'),
    ('SPECIAL_HOLIDAY', 'AC_RESEARCH', 'OK26',  5, 'IMMEDIATE', 9, 0, true,  false, NULL, 'Særlige feriedage – 5 dage',       '0001-01-01'),
    ('SPECIAL_HOLIDAY', 'AC_TEACHING', 'OK24',  5, 'IMMEDIATE', 9, 0, true,  false, NULL, 'Særlige feriedage – 5 dage',       '0001-01-01'),
    ('SPECIAL_HOLIDAY', 'AC_TEACHING', 'OK26',  5, 'IMMEDIATE', 9, 0, true,  false, NULL, 'Særlige feriedage – 5 dage',       '0001-01-01'),
    ('CARE_DAY',        'AC_RESEARCH', 'OK24',  2, 'IMMEDIATE', 1, 0, false, false, NULL, 'Omsorgsdage – 2 dage',             '0001-01-01'),
    ('CARE_DAY',        'AC_RESEARCH', 'OK26',  2, 'IMMEDIATE', 1, 0, false, false, NULL, 'Omsorgsdage – 2 dage',             '0001-01-01'),
    ('CARE_DAY',        'AC_TEACHING', 'OK24',  2, 'IMMEDIATE', 1, 0, false, false, NULL, 'Omsorgsdage – 2 dage',             '0001-01-01'),
    ('CARE_DAY',        'AC_TEACHING', 'OK26',  2, 'IMMEDIATE', 1, 0, false, false, NULL, 'Omsorgsdage – 2 dage',             '0001-01-01'),
    ('CHILD_SICK',      'AC_RESEARCH', 'OK24',  1, 'IMMEDIATE', 1, 0, false, true,  NULL, 'Barn syg – 1 dag per episode',     '0001-01-01'),
    ('CHILD_SICK',      'AC_RESEARCH', 'OK26',  1, 'IMMEDIATE', 1, 0, false, true,  NULL, 'Barn syg – 1 dag per episode',     '0001-01-01'),
    ('CHILD_SICK',      'AC_TEACHING', 'OK24',  1, 'IMMEDIATE', 1, 0, false, true,  NULL, 'Barn syg – 1 dag per episode',     '0001-01-01'),
    ('CHILD_SICK',      'AC_TEACHING', 'OK26',  1, 'IMMEDIATE', 1, 0, false, true,  NULL, 'Barn syg – 1 dag per episode',     '0001-01-01'),
    ('SENIOR_DAY',      'AC_RESEARCH', 'OK24',  2, 'IMMEDIATE', 1, 0, false, false, 62,   'Seniordage – kræver alder 62+',    '0001-01-01'),
    ('SENIOR_DAY',      'AC_RESEARCH', 'OK26',  2, 'IMMEDIATE', 1, 0, false, false, 62,   'Seniordage – kræver alder 62+',    '0001-01-01'),
    ('SENIOR_DAY',      'AC_TEACHING', 'OK24',  2, 'IMMEDIATE', 1, 0, false, false, 62,   'Seniordage – kræver alder 62+',    '0001-01-01'),
    ('SENIOR_DAY',      'AC_TEACHING', 'OK26',  2, 'IMMEDIATE', 1, 0, false, false, 62,   'Seniordage – kræver alder 62+',    '0001-01-01')
ON CONFLICT (entitlement_type, agreement_code, ok_version, effective_from) DO NOTHING;

-- ============================================================
-- SPRINT 27 (Phase 4c.6): Sync-in-tx Read Projections
-- ============================================================
-- Projection tables for TimeEntryRegistered + AbsenceRegistered events,
-- written inside the same transaction that appends to `events` + `outbox_events`
-- so that GET endpoints satisfy read-your-write after a successful POST
-- (fixes the regression that caused S26 TASK-2604/TASK-2606 reverts).
--
-- Field set is a strict superset of (a) what the source events carry per
-- DomainEventBase + TimeEntryRegistered/AbsenceRegistered, and (b) what the
-- 5 read-side endpoints currently materialize from `IEventStore.ReadStreamAsync`
-- filters: SkemaEndpoints month GET, TimeEndpoints entries+absences GETs,
-- BalanceEndpoints summary, ComplianceEndpoints period.
--
-- `outbox_id BIGINT NOT NULL` is sourced from `outbox_events.outbox_id`
-- (BIGSERIAL) via INSERT ... RETURNING outbox_id in the same transaction.
-- It is GLOBAL (not per-service-monotonic) — per-stream/per-service ordering
-- comes from publisher-side filtering on (service_id, outbox_id).
-- Per-employee monotonic ordering is preserved by the `(employee_id, outbox_id)`
-- index, used by the no-date-filter Time GETs.

CREATE TABLE IF NOT EXISTS time_entries_projection (
    event_id                    UUID            PRIMARY KEY,
    employee_id                 TEXT            NOT NULL,
    date                        DATE            NOT NULL,
    hours                       NUMERIC(8,4)    NOT NULL,  -- S27 Step 7a P2 fix: 4 decimal places preserves sub-centesimal hours (e.g., 7.375h = 22.5min); RequestValidator accepts arbitrary decimal in (0,24], canonical events keep full precision, projection must too
    start_time                  TIME,
    end_time                    TIME,
    task_id                     TEXT,
    activity_type               TEXT,
    agreement_code              TEXT            NOT NULL,
    ok_version                  TEXT            NOT NULL,
    voluntary_unsocial_hours    BOOLEAN         NOT NULL DEFAULT false,
    occurred_at                 TIMESTAMPTZ     NOT NULL,
    actor_id                    TEXT,
    actor_role                  TEXT,
    correlation_id              UUID,
    outbox_id                   BIGINT          NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_time_entries_proj_emp_date_outbox
    ON time_entries_projection(employee_id, date, outbox_id);
CREATE INDEX IF NOT EXISTS idx_time_entries_proj_emp_outbox
    ON time_entries_projection(employee_id, outbox_id);

CREATE TABLE IF NOT EXISTS absences_projection (
    event_id                    UUID            PRIMARY KEY,
    employee_id                 TEXT            NOT NULL,
    date                        DATE            NOT NULL,
    absence_type                TEXT            NOT NULL,
    hours                       NUMERIC(8,4)    NOT NULL,  -- S27 Step 7a P2 fix: see time_entries_projection.hours comment
    agreement_code              TEXT            NOT NULL,
    ok_version                  TEXT            NOT NULL,
    occurred_at                 TIMESTAMPTZ     NOT NULL,
    actor_id                    TEXT,
    actor_role                  TEXT,
    correlation_id              UUID,
    outbox_id                   BIGINT          NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_absences_proj_emp_date_outbox
    ON absences_projection(employee_id, date, outbox_id);
CREATE INDEX IF NOT EXISTS idx_absences_proj_emp_outbox
    ON absences_projection(employee_id, outbox_id);

-- ============================================================
-- SPRINT 17: Overtime Governance & Compensation Model
-- ============================================================

-- Overtime balance tracking (separate from flex balance)
CREATE TABLE IF NOT EXISTS overtime_balances (
    balance_id          UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    employee_id         TEXT        NOT NULL,
    agreement_code      TEXT        NOT NULL,
    period_year         INT         NOT NULL,
    accumulated         DECIMAL     NOT NULL DEFAULT 0,
    paid_out            DECIMAL     NOT NULL DEFAULT 0,
    afspadsering_used   DECIMAL     NOT NULL DEFAULT 0,
    compensation_model  TEXT        NOT NULL DEFAULT 'UDBETALING',
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (employee_id, period_year)
);
CREATE INDEX IF NOT EXISTS idx_overtime_balances_employee
    ON overtime_balances(employee_id);

-- Overtime pre-approval tracking (workflow gate)
CREATE TABLE IF NOT EXISTS overtime_pre_approvals (
    id                  UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    employee_id         TEXT        NOT NULL,
    period_start        DATE        NOT NULL,
    period_end          DATE        NOT NULL,
    max_hours           DECIMAL     NOT NULL,
    approved_by         TEXT,
    approved_at         TIMESTAMPTZ,
    status              TEXT        NOT NULL DEFAULT 'PENDING' CHECK (status IN ('PENDING', 'APPROVED', 'REJECTED')),
    reason              TEXT,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_overtime_pre_approvals_employee
    ON overtime_pre_approvals(employee_id);
CREATE INDEX IF NOT EXISTS idx_overtime_pre_approvals_status
    ON overtime_pre_approvals(status);

-- Sprint 17: Compensation-specific wage type mappings (OK24)
INSERT INTO wage_type_mappings (time_type, wage_type, ok_version, agreement_code, description, effective_from) VALUES
    -- Overtime payout vs afspadsering (HK/PROSA)
    ('OVERTIME_50_PAYOUT', 'SLS_0211', 'OK24', 'HK', 'Overtime 50% — monetary payout', '2020-01-01'),
    ('OVERTIME_50_PAYOUT', 'SLS_0211', 'OK24', 'PROSA', 'Overtime 50% — monetary payout', '2020-01-01'),
    ('OVERTIME_50_AFSPADSERING', 'SLS_0212', 'OK24', 'HK', 'Overtime 50% — time-off compensation', '2020-01-01'),
    ('OVERTIME_50_AFSPADSERING', 'SLS_0212', 'OK24', 'PROSA', 'Overtime 50% — time-off compensation', '2020-01-01'),
    ('OVERTIME_100_PAYOUT', 'SLS_0221', 'OK24', 'HK', 'Overtime 100% — monetary payout', '2020-01-01'),
    ('OVERTIME_100_PAYOUT', 'SLS_0221', 'OK24', 'PROSA', 'Overtime 100% — monetary payout', '2020-01-01'),
    ('OVERTIME_100_AFSPADSERING', 'SLS_0222', 'OK24', 'HK', 'Overtime 100% — time-off compensation', '2020-01-01'),
    ('OVERTIME_100_AFSPADSERING', 'SLS_0222', 'OK24', 'PROSA', 'Overtime 100% — time-off compensation', '2020-01-01'),
    -- Merarbejde payout vs afspadsering (AC)
    ('MERARBEJDE_PAYOUT', 'SLS_0311', 'OK24', 'AC', 'Merarbejde — monetary payout', '2020-01-01'),
    ('MERARBEJDE_AFSPADSERING', 'SLS_0312', 'OK24', 'AC', 'Merarbejde — time-off compensation', '2020-01-01'),
    ('MERARBEJDE_PAYOUT', 'SLS_0311', 'OK24', 'AC_RESEARCH', 'Merarbejde — monetary payout', '2020-01-01'),
    ('MERARBEJDE_AFSPADSERING', 'SLS_0312', 'OK24', 'AC_RESEARCH', 'Merarbejde — time-off compensation', '2020-01-01'),
    ('MERARBEJDE_PAYOUT', 'SLS_0311', 'OK24', 'AC_TEACHING', 'Merarbejde — monetary payout', '2020-01-01'),
    ('MERARBEJDE_AFSPADSERING', 'SLS_0312', 'OK24', 'AC_TEACHING', 'Merarbejde — time-off compensation', '2020-01-01')
ON CONFLICT (time_type, ok_version, agreement_code, position, effective_from) DO NOTHING;

-- Sprint 17: Compensation-specific wage type mappings (OK26)
INSERT INTO wage_type_mappings (time_type, wage_type, ok_version, agreement_code, description, effective_from) VALUES
    ('OVERTIME_50_PAYOUT', 'SLS_0211', 'OK26', 'HK', 'Overtime 50% — monetary payout', '2020-01-01'),
    ('OVERTIME_50_PAYOUT', 'SLS_0211', 'OK26', 'PROSA', 'Overtime 50% — monetary payout', '2020-01-01'),
    ('OVERTIME_50_AFSPADSERING', 'SLS_0212', 'OK26', 'HK', 'Overtime 50% — time-off compensation', '2020-01-01'),
    ('OVERTIME_50_AFSPADSERING', 'SLS_0212', 'OK26', 'PROSA', 'Overtime 50% — time-off compensation', '2020-01-01'),
    ('OVERTIME_100_PAYOUT', 'SLS_0221', 'OK26', 'HK', 'Overtime 100% — monetary payout', '2020-01-01'),
    ('OVERTIME_100_PAYOUT', 'SLS_0221', 'OK26', 'PROSA', 'Overtime 100% — monetary payout', '2020-01-01'),
    ('OVERTIME_100_AFSPADSERING', 'SLS_0222', 'OK26', 'HK', 'Overtime 100% — time-off compensation', '2020-01-01'),
    ('OVERTIME_100_AFSPADSERING', 'SLS_0222', 'OK26', 'PROSA', 'Overtime 100% — time-off compensation', '2020-01-01'),
    ('MERARBEJDE_PAYOUT', 'SLS_0311', 'OK26', 'AC', 'Merarbejde — monetary payout', '2020-01-01'),
    ('MERARBEJDE_AFSPADSERING', 'SLS_0312', 'OK26', 'AC', 'Merarbejde — time-off compensation', '2020-01-01'),
    ('MERARBEJDE_PAYOUT', 'SLS_0311', 'OK26', 'AC_RESEARCH', 'Merarbejde — monetary payout', '2020-01-01'),
    ('MERARBEJDE_AFSPADSERING', 'SLS_0312', 'OK26', 'AC_RESEARCH', 'Merarbejde — time-off compensation', '2020-01-01'),
    ('MERARBEJDE_PAYOUT', 'SLS_0311', 'OK26', 'AC_TEACHING', 'Merarbejde — monetary payout', '2020-01-01'),
    ('MERARBEJDE_AFSPADSERING', 'SLS_0312', 'OK26', 'AC_TEACHING', 'Merarbejde — time-off compensation', '2020-01-01')
ON CONFLICT (time_type, ok_version, agreement_code, position, effective_from) DO NOTHING;

-- ============================================================
-- SPRINT 20: Temporal Period Handling — Segment Manifest Framework (ADR-016)
-- ============================================================

-- Segment manifests: persistent record of how a calculation period was split at
-- effective-date boundaries before rule evaluation. One row per segmented calculation.
-- Audit linkage to specific export lines lives in audit_log.payload_jsonb.manifest_id
-- and in CalculationResult.ManifestId (event payload) — NOT in a payroll_export_lines
-- column, since payroll_export_lines is an in-memory C# model, not a DB table
-- (D10 amendment 2026-04-29 during Phase 1).
CREATE TABLE IF NOT EXISTS segment_manifests (
    manifest_id             UUID        PRIMARY KEY,
    period_start            DATE        NOT NULL,
    period_end              DATE        NOT NULL,
    employee_id             TEXT        NOT NULL,
    -- Allowed values: 'forward-calc' | 'retroactive-correction' | 'replay'
    -- No CHECK constraint — string enum is enforced in C# (project convention, per ADR-002)
    calculation_kind        TEXT        NOT NULL,
    -- Deduped list of boundary cause labels that triggered segmentation.
    -- Examples: 'OkTransition', 'AgreementConfigPromotion', 'PositionOverrideEffective',
    --           'EuWtdRulesetVersion', 'EntitlementPolicyChange', 'EmployeeProfileChange'
    boundary_cause_summary  TEXT[]      NOT NULL,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    -- Full segment detail: array of {segmentStart, segmentEnd, okVersion, configSnapshot, ...}
    -- Shape is defined and owned by the Payroll / PeriodPlanner domain (S20 implementation tasks)
    segments_jsonb          JSONB       NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_segment_manifests_employee_period
    ON segment_manifests (employee_id, period_start);

CREATE INDEX IF NOT EXISTS idx_segment_manifests_boundary_cause
    ON segment_manifests USING GIN (boundary_cause_summary);

-- =========================================================================
-- S22 / ADR-018 — one-shot guarded migration for D7 + D8 + D9.
--   D7: row-version column on local_agreement_profiles
--   D8: convert end-inclusive effective_to to end-exclusive (+1 day shift)
--   D9: extend audit-action enum to include MODIFIED
-- Guarded by schema_migrations ledger so re-runs of init.sql are idempotent.
-- All referenced tables exist by this point in the file.
-- =========================================================================
DO $$
BEGIN
    INSERT INTO schema_migrations (migration_id, notes)
    VALUES ('s22-d7-d8-d9', 'ADR-018: row-version + end-exclusive + MODIFIED audit action')
    ON CONFLICT (migration_id) DO NOTHING;

    IF NOT FOUND THEN
        RETURN;
    END IF;

    -- D7: row-version column. Existing rows get DEFAULT 1 automatically.
    ALTER TABLE local_agreement_profiles
    ADD COLUMN IF NOT EXISTS version BIGINT NOT NULL DEFAULT 1;

    -- D8: convert end-inclusive effective_to to end-exclusive.
    UPDATE local_agreement_profiles
    SET effective_to = effective_to + INTERVAL '1 day'
    WHERE effective_to IS NOT NULL;

    -- D9: extend audit-action enum to MODIFIED.
    ALTER TABLE local_agreement_profile_audit
    DROP CONSTRAINT IF EXISTS local_agreement_profile_audit_action_check;

    ALTER TABLE local_agreement_profile_audit
    ADD CONSTRAINT local_agreement_profile_audit_action_check
    CHECK (action IN ('CREATED', 'MODIFIED', 'SUPERSEDED', 'DEACTIVATED', 'MIGRATED_FROM_LEGACY'));
END
$$;

-- =========================================================================
-- S25 / ADR-019 (pending) — one-shot guarded migration for D2.2.
--   Part A: row-version BIGINT on 4 admin-config tables (agreement_configs,
--           position_override_configs, wage_type_mappings, entitlement_configs).
--   Part B: version-transition columns (version_before / version_after) on
--           3 admin-config audit tables.
-- Mirrors the s22-d7-d8-d9 pattern. Guarded by schema_migrations ledger so
-- re-runs of init.sql are idempotent. All referenced tables exist by this
-- point in the file.
-- =========================================================================
DO $$
BEGIN
    INSERT INTO schema_migrations (migration_id, notes)
    VALUES ('s25-d2-2-version', 'ADR-019 (pending): row-version on admin-config surfaces + audit version-transition columns')
    ON CONFLICT (migration_id) DO NOTHING;

    IF NOT FOUND THEN
        RETURN;
    END IF;

    -- Part A: row-version BIGINT on 4 admin-config tables.
    -- Existing rows backfill to version=1 via DEFAULT.
    ALTER TABLE agreement_configs
    ADD COLUMN IF NOT EXISTS version BIGINT NOT NULL DEFAULT 1;

    ALTER TABLE position_override_configs
    ADD COLUMN IF NOT EXISTS version BIGINT NOT NULL DEFAULT 1;

    ALTER TABLE wage_type_mappings
    ADD COLUMN IF NOT EXISTS version BIGINT NOT NULL DEFAULT 1;

    -- entitlement_configs: schema-only per ADR-019. Admin CRUD endpoints
    -- (and v3 (conn, tx, expectedVersion) overload) are wired in a future
    -- sprint when admin CRUD becomes motivated.
    ALTER TABLE entitlement_configs
    ADD COLUMN IF NOT EXISTS version BIGINT NOT NULL DEFAULT 1;

    -- Part B: audit version-transition columns on 3 audit tables.
    -- Nullable — old audit rows + ForcedRollbackHarness v2 audit calls leave
    -- them NULL; v3 audit overload populates the version pair.
    ALTER TABLE agreement_config_audit
    ADD COLUMN IF NOT EXISTS version_before BIGINT NULL,
    ADD COLUMN IF NOT EXISTS version_after BIGINT NULL;

    ALTER TABLE position_override_config_audit
    ADD COLUMN IF NOT EXISTS version_before BIGINT NULL,
    ADD COLUMN IF NOT EXISTS version_after BIGINT NULL;

    ALTER TABLE wage_type_mapping_audit
    ADD COLUMN IF NOT EXISTS version_before BIGINT NULL,
    ADD COLUMN IF NOT EXISTS version_after BIGINT NULL;
END
$$;

-- =========================================================================
-- S29 / ADR-020 D1+D2+D3 — wage-type-mapping effective-dating + supersession.
--   Step (a): add mapping_id (UUID surrogate PK) + effective_from + effective_to;
--             backfill existing rows; set NOT NULL on mapping_id + effective_from.
--   Step (b): drop composite PK; add PK on mapping_id; add partial-unique-index
--             on natural key WHERE effective_to IS NULL (S21 D2.1 pattern) +
--             full unique on (natural_key, effective_from) for ADR-020 D3
--             ON CONFLICT DO NOTHING conflict target.
--   Step (c): widen wage_type_mapping_audit.action CHECK to add SUPERSEDED
--             (S22 local_agreement_profile_audit precedent).
-- Guarded by schema_migrations ledger so re-runs of init.sql are idempotent.
-- =========================================================================
DO $$
BEGIN
    INSERT INTO schema_migrations (migration_id, notes)
    VALUES ('s29-d1-wtm-effective-dating', 'ADR-020: wage_type_mappings effective-dating + supersession + audit SUPERSEDED action')
    ON CONFLICT (migration_id) DO NOTHING;

    IF NOT FOUND THEN
        RETURN;
    END IF;

    -- Step (a): add columns nullable, backfill, then enforce NOT NULL on the
    -- two columns that must always be set. effective_to stays nullable —
    -- NULL means "currently open / unbounded above."
    ALTER TABLE wage_type_mappings
    ADD COLUMN IF NOT EXISTS mapping_id     UUID,
    ADD COLUMN IF NOT EXISTS effective_from DATE,
    ADD COLUMN IF NOT EXISTS effective_to   DATE;

    UPDATE wage_type_mappings
    SET mapping_id = gen_random_uuid(),
        effective_from = DATE '2020-01-01'
    WHERE mapping_id IS NULL OR effective_from IS NULL;

    ALTER TABLE wage_type_mappings
    ALTER COLUMN mapping_id     SET NOT NULL,
    ALTER COLUMN effective_from SET NOT NULL;

    -- Step (b): swap composite PK for surrogate UUID PK + add the two indexes.
    -- The PK swap requires drop-then-add; constraint name is the implicit
    -- table-name-based default ("wage_type_mappings_pkey").
    ALTER TABLE wage_type_mappings
    DROP CONSTRAINT IF EXISTS wage_type_mappings_pkey;

    ALTER TABLE wage_type_mappings
    ADD CONSTRAINT wage_type_mappings_pkey PRIMARY KEY (mapping_id);

    -- At most one open row per natural key (current-row uniqueness).
    CREATE UNIQUE INDEX IF NOT EXISTS idx_wtm_natural_key_open
        ON wage_type_mappings (time_type, ok_version, agreement_code, position)
        WHERE effective_to IS NULL;

    -- ADR-020 D3 conflict target: forbids duplicate history rows on the same
    -- natural key + effective_from tuple, including across closed predecessors.
    CREATE UNIQUE INDEX IF NOT EXISTS idx_wtm_natural_key_history
        ON wage_type_mappings (time_type, ok_version, agreement_code, position, effective_from);

    -- Step (c): widen audit-action CHECK to include SUPERSEDED. CHECK
    -- constraints don't support IF NOT EXISTS on ADD; use conditional
    -- DROP-if-exists + ADD with a v2 name (mirrors S22 D9 audit-action
    -- widening pattern at local_agreement_profile_audit).
    ALTER TABLE wage_type_mapping_audit
    DROP CONSTRAINT IF EXISTS wage_type_mapping_audit_action_check;

    ALTER TABLE wage_type_mapping_audit
    DROP CONSTRAINT IF EXISTS wage_type_mapping_audit_action_check_v2;

    ALTER TABLE wage_type_mapping_audit
    ADD CONSTRAINT wage_type_mapping_audit_action_check_v2
    CHECK (action IN ('CREATED', 'UPDATED', 'DELETED', 'SUPERSEDED'));
END
$$;

-- =========================================================================
-- S30 / ADR-021 D1+D2+D3 — entitlement-configs effective-dating + supersession.
--   Step (a): add effective_from + effective_to to entitlement_configs;
--             backfill existing rows; set NOT NULL on effective_from.
--   Step (b): drop the legacy composite UNIQUE constraint
--             (entitlement_type, agreement_code, ok_version); add
--             partial-unique-index on natural key WHERE effective_to IS NULL
--             (S21 D2.1 pattern) + full unique on (natural_key, effective_from)
--             for ADR-021 D3 ON CONFLICT DO NOTHING conflict target.
-- entitlement_config_audit table is brand-new in S30 — created by the base
-- CREATE TABLE block above with version_before/version_after + SUPERSEDED
-- action pre-baked, so no audit-table ALTER is required here.
-- Guarded by schema_migrations ledger so re-runs of init.sql are idempotent.
-- =========================================================================
DO $$
BEGIN
    INSERT INTO schema_migrations (migration_id, notes)
    VALUES ('s30-d2-ec-effective-dating', 'ADR-021: entitlement_configs effective-dating + supersession + new audit table')
    ON CONFLICT (migration_id) DO NOTHING;

    IF NOT FOUND THEN
        RETURN;
    END IF;

    -- Step (a): add effective-dating columns. effective_from gets DEFAULT
    -- '0001-01-01' so existing rows backfill automatically (matches the
    -- greenfield CREATE TABLE default; sentinel pre-launch anchor per
    -- PLAN-s30 exclusions table). effective_to stays nullable —
    -- NULL means "currently open / unbounded above."
    ALTER TABLE entitlement_configs
    ADD COLUMN IF NOT EXISTS effective_from DATE NOT NULL DEFAULT '0001-01-01',
    ADD COLUMN IF NOT EXISTS effective_to   DATE;

    -- Step (b): drop the legacy composite UNIQUE constraint that prevented
    -- multiple history rows per natural key. Default constraint name follows
    -- PostgreSQL convention "<table>_<col1>_<col2>_..._key" for inline UNIQUE,
    -- but PostgreSQL truncates identifiers to 63 chars. The full conventional
    -- name (..._ok_version_key) is 65 chars and truncates to 63 chars dropping
    -- the trailing "key" suffix. We DROP both forms to handle any legacy DB.
    ALTER TABLE entitlement_configs
    DROP CONSTRAINT IF EXISTS entitlement_configs_entitlement_type_agreement_code_ok_version_key;

    ALTER TABLE entitlement_configs
    DROP CONSTRAINT IF EXISTS entitlement_configs_entitlement_type_agreement_code_ok_version_;

    -- At most one open row per natural key (current-row uniqueness).
    CREATE UNIQUE INDEX IF NOT EXISTS idx_ec_natural_key_open
        ON entitlement_configs (entitlement_type, agreement_code, ok_version)
        WHERE effective_to IS NULL;

    -- ADR-021 D3 conflict target: forbids duplicate history rows on the same
    -- natural key + effective_from tuple, including across closed predecessors.
    CREATE UNIQUE INDEX IF NOT EXISTS idx_ec_natural_key_history
        ON entitlement_configs (entitlement_type, agreement_code, ok_version, effective_from);
END
$$;

-- =========================================================================
-- S35 / D1 — users.version row-version column for ADR-018 D7 row-version +
--   If-Match optimistic concurrency on /api/admin/users (ADR-019 D2).
-- The base CREATE TABLE block (above, L456-470) bakes `version BIGINT NOT NULL
-- DEFAULT 1` into greenfield databases. On upgrade, `CREATE TABLE IF NOT EXISTS`
-- skips the existing users row → the column would otherwise never land. This
-- block carries the explicit ADD COLUMN path that the IF NOT EXISTS CREATE
-- cannot reach on legacy databases. Mirrors the S22/S25 ALTER pattern.
-- users_audit (above, L610-623) is a new-in-S35 table whose `CREATE TABLE IF
-- NOT EXISTS` is sufficient for any database state — no ALTER required for
-- the audit table itself.
--
-- Step 7a cycle 1 absorption (Codex BLOCKER-1): closed the production upgrade
-- gap that the greenfield-only ledger insert previously masked.
-- Step 7a cycle 2 absorption (Codex BLOCKER-1 cycle-2 missed-facts): the
-- ALTER is now UNCONDITIONAL (above the IF NOT FOUND guard) so it repairs
-- DBs that ran the pre-cycle-1 form of init.sql against a legacy schema —
-- such DBs hold the s35-d1-users-version-and-audit ledger row WITHOUT
-- users.version. Putting ALTER above the guard makes it idempotent across
-- all states (ADD COLUMN IF NOT EXISTS is a no-op when the column already
-- exists). The ledger INSERT + guard still bound any FUTURE one-shot
-- repair work to a single run.
-- =========================================================================
DO $$
BEGIN
    -- Unconditional repair — runs whether or not the ledger row already
    -- exists. Idempotent via ADD COLUMN IF NOT EXISTS. Existing rows
    -- backfill to version=1 via DEFAULT (matches S22 backfill shape).
    ALTER TABLE users
    ADD COLUMN IF NOT EXISTS version BIGINT NOT NULL DEFAULT 1;

    INSERT INTO schema_migrations (migration_id, notes)
    VALUES ('s35-d1-users-version-and-audit', 'ADR-018 D7 / ADR-019 D2: row-version + If-Match optimistic concurrency on /api/admin/users + users_audit')
    ON CONFLICT (migration_id) DO NOTHING;

    IF NOT FOUND THEN
        RETURN;
    END IF;

    -- Future one-shot work for this migration goes here (none today —
    -- the ALTER above is the entire migration body, lifted out of the
    -- guard for repair idempotency).
END
$$;

-- =========================================================================
-- S40 / ADR-024 D1 + D2 — role_config_overrides table (role-within-agreement
--   layer between agreement_configs and position_override_configs in the
--   ConfigResolutionService chain — S41 cutover wires it up). 5th versioned-
--   config table after WTM (S29) / EntitlementConfig (S30) /
--   EmployeeProfile (S31) / UserAgreementCode (S34).
--
-- Schema per ADR-024 L38: 6 boolean disablers + tri-state
-- merarbejde_compensation_right (D2) + 4 quantitative nullable overrides.
-- Composite natural key (employment_category, agreement_code, ok_version).
--
-- Audit columns follow agreement_config_audit pattern (actor_id, actor_role,
-- timestamp) at init.sql:1116-1125. Version-transition columns per ADR-019 D8.
-- =========================================================================

CREATE TABLE IF NOT EXISTS role_config_overrides (
    override_id              UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    employment_category      TEXT         NOT NULL,
    agreement_code           TEXT         NOT NULL,
    ok_version               TEXT         NOT NULL,
    effective_from           DATE         NOT NULL DEFAULT '0001-01-01',
    effective_to             DATE         NULL,   -- end-exclusive; NULL = open
    version                  BIGINT       NOT NULL DEFAULT 1,
    -- D2 tri-state
    merarbejde_compensation_right TEXT    NULL CHECK (merarbejde_compensation_right IN ('CONTRACTUAL', 'DISCRETIONARY', 'NONE')),
    -- 6 Boolean disablers per ADR-024 L38 (NULL = inherit from agreement_configs)
    has_merarbejde           BOOLEAN      NULL,
    has_overtime             BOOLEAN      NULL,
    has_evening_supplement   BOOLEAN      NULL,
    has_night_supplement     BOOLEAN      NULL,
    has_weekend_supplement   BOOLEAN      NULL,
    has_holiday_supplement   BOOLEAN      NULL,
    -- Quantitative nullable overrides per ADR-024 L38 (NULL = inherit from agreement_configs)
    -- Explicit precision per S27 Step 7a cycle-2 lossy-NUMERIC absorption.
    max_flex_balance         NUMERIC(7,2) NULL,
    flex_carryover_max       NUMERIC(7,2) NULL,
    norm_period_weeks        INT          NULL,
    weekly_norm_hours        NUMERIC(5,2) NULL,
    -- Audit metadata
    created_at               TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    created_by               TEXT         NOT NULL,
    created_by_role          TEXT         NOT NULL
);

-- Partial-unique-index: only one ACTIVE row per natural key (D2.1 pattern from S21).
CREATE UNIQUE INDEX IF NOT EXISTS idx_role_config_overrides_live
    ON role_config_overrides (employment_category, agreement_code, ok_version)
    WHERE effective_to IS NULL;

-- History-unique-index: one row per natural key per effective_from
-- (ADR-021 D3 conflict-target pattern).
CREATE UNIQUE INDEX IF NOT EXISTS idx_role_config_overrides_history
    ON role_config_overrides (employment_category, agreement_code, ok_version, effective_from);

-- Audit table mirrors agreement_config_audit shape + ADR-019 D8 version-transition columns.
CREATE TABLE IF NOT EXISTS role_config_override_audit (
    audit_id                 BIGSERIAL    PRIMARY KEY,
    override_id              UUID         NOT NULL REFERENCES role_config_overrides(override_id),
    action                   TEXT         NOT NULL CHECK (action IN ('CREATED', 'UPDATED', 'SUPERSEDED', 'SOFT_DELETED')),
    version_before           BIGINT       NULL,  -- per ADR-019 D8
    version_after            BIGINT       NULL,
    previous_data            JSONB        NULL,
    new_data                 JSONB        NULL,
    actor_id                 TEXT         NOT NULL,
    actor_role               TEXT         NOT NULL,
    timestamp                TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_role_config_override_audit_override
    ON role_config_override_audit(override_id);

-- Ledger entry for s40-d1.
DO $$
BEGIN
    INSERT INTO schema_migrations (migration_id, notes)
    VALUES ('s40-d1-role-config-overrides', 'ADR-024 D1 + D2: role-within-agreement layer + tri-state MerarbejdeCompensationRight + audit')
    ON CONFLICT (migration_id) DO NOTHING;
END
$$;

-- S40 / TASK-4005 — Greenfield seed for role_config_overrides.
-- 4 AC strata × 2 OK versions = 8 rows. Standard category NOT seeded — ConfigResolutionService
-- falls through to agreement_configs when no row matches.
-- Tri-state values per ADR-024 L46-50 (PROVISIONAL pending Phase B):
--   Fuldmægtig       → CONTRACTUAL (documents the encoded default explicitly)
--   Specialkonsulent → DISCRETIONARY (PROVISIONAL pending Phase B cirkulær cite)
--   Chefkonsulent    → NONE         (PROVISIONAL pending Phase B cirkulær cite)
--   Kontorchef       → NONE         (PROVISIONAL pending Phase B cirkulær cite)
-- All other override columns (booleans + quantitative) stay NULL → inherit from agreement_configs.
-- effective_from = '0001-01-01' per the history-covering anchor convention (post-S33
-- EmployeeProfileSeeder pattern — first PUT after seed triggers Case C cross-day cleanly).
-- Source-register bug_correction_history annotations land at sprint close (TASK-4007).
INSERT INTO role_config_overrides (employment_category, agreement_code, ok_version, effective_from, merarbejde_compensation_right, created_by, created_by_role) VALUES
    ('Fuldmægtig',       'AC', 'OK24', '0001-01-01', 'CONTRACTUAL',   'SYSTEM_SEED', 'SYSTEM_SEED'),
    ('Fuldmægtig',       'AC', 'OK26', '0001-01-01', 'CONTRACTUAL',   'SYSTEM_SEED', 'SYSTEM_SEED'),
    ('Specialkonsulent', 'AC', 'OK24', '0001-01-01', 'DISCRETIONARY', 'SYSTEM_SEED', 'SYSTEM_SEED'),
    ('Specialkonsulent', 'AC', 'OK26', '0001-01-01', 'DISCRETIONARY', 'SYSTEM_SEED', 'SYSTEM_SEED'),
    ('Chefkonsulent',    'AC', 'OK24', '0001-01-01', 'NONE',          'SYSTEM_SEED', 'SYSTEM_SEED'),
    ('Chefkonsulent',    'AC', 'OK26', '0001-01-01', 'NONE',          'SYSTEM_SEED', 'SYSTEM_SEED'),
    ('Kontorchef',       'AC', 'OK24', '0001-01-01', 'NONE',          'SYSTEM_SEED', 'SYSTEM_SEED'),
    ('Kontorchef',       'AC', 'OK26', '0001-01-01', 'NONE',          'SYSTEM_SEED', 'SYSTEM_SEED')
ON CONFLICT DO NOTHING;

-- =========================================================================
-- S40 / ADR-024 D7 — overtime_pre_approvals extension + overtime_authorization_audit.
-- Adds 4 columns to overtime_pre_approvals (S17 introduction at L1504) and
-- creates a new companion audit table. Per ADR-024 D7 L211-216:
--   - authorization_mode CHECK ('PRIOR_APPROVAL', 'POST_HOC_NECESSITY')
--   - necessity_reason (required when mode=POST_HOC_NECESSITY at endpoint layer)
--   - acknowledged_at + acknowledged_by (for post-hoc necessity acknowledgments)
-- New endpoint POST /api/overtime-pre-approvals/{id}/acknowledge-necessity
-- lands in S41 cutover; S40 ships schema only.
--
-- FK target verified: overtime_pre_approvals(id) per init.sql:1504-1515
-- (Step 0b cycle 1 absorption — earlier draft incorrectly used pre_approval_id).
-- Audit shape follows agreement_config_audit pattern at init.sql:1116-1125.
-- =========================================================================

-- Idempotent ALTER for the existing overtime_pre_approvals table.
ALTER TABLE overtime_pre_approvals
    ADD COLUMN IF NOT EXISTS authorization_mode TEXT NOT NULL DEFAULT 'PRIOR_APPROVAL' CHECK (authorization_mode IN ('PRIOR_APPROVAL', 'POST_HOC_NECESSITY')),
    ADD COLUMN IF NOT EXISTS necessity_reason TEXT NULL,
    ADD COLUMN IF NOT EXISTS acknowledged_at TIMESTAMPTZ NULL,
    ADD COLUMN IF NOT EXISTS acknowledged_by TEXT NULL;

CREATE TABLE IF NOT EXISTS overtime_authorization_audit (
    audit_id                 BIGSERIAL    PRIMARY KEY,
    pre_approval_id          UUID         NOT NULL REFERENCES overtime_pre_approvals(id),
    action                   TEXT         NOT NULL CHECK (action IN ('CREATED', 'UPDATED', 'NECESSITY_ACKNOWLEDGED')),
    version_before           BIGINT       NULL,
    version_after            BIGINT       NULL,
    previous_data            JSONB        NULL,
    new_data                 JSONB        NULL,
    actor_id                 TEXT         NOT NULL,
    actor_role               TEXT         NOT NULL,
    timestamp                TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_overtime_authorization_audit_pre_approval
    ON overtime_authorization_audit(pre_approval_id);

-- Ledger entry for s40-d7.
DO $$
BEGIN
    INSERT INTO schema_migrations (migration_id, notes)
    VALUES ('s40-d7-overtime-authorization-extension', 'ADR-024 D7: overtime_pre_approvals authorization_mode + necessity acknowledgment columns + audit table')
    ON CONFLICT (migration_id) DO NOTHING;
END
$$;
