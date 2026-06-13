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
    -- S70 / ADR-033 slice 3a leaver columns — full semantics in the
    -- 's70-employment-end-date' annotation + legacy-ALTER block near EOF
    -- (the inline columns serve greenfield; the guarded ALTER serves legacy DBs).
    employment_end_date  DATE       NULL,
    end_date_deactivated BOOLEAN    NOT NULL DEFAULT FALSE,
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

-- Seed test organization hierarchy — two ministries for multi-tenant testing
-- Ministry 1: Finansministeriet (existing)
-- Ministry 2: Beskaeftigelsesministeriet (new — provides cross-institution isolation testing)
INSERT INTO organizations (org_id, org_name, org_type, parent_org_id, materialized_path, agreement_code, ok_version) VALUES
    ('MIN01', 'Finansministeriet', 'MINISTRY', NULL, '/MIN01/', 'AC', 'OK24'),
    ('STY01', 'Medarbejder- og Kompetencestyrelsen', 'STYRELSE', 'MIN01', '/MIN01/STY01/', 'AC', 'OK24'),
    ('STY02', 'Statens IT', 'STYRELSE', 'MIN01', '/MIN01/STY02/', 'HK', 'OK24'),
    ('STY03', 'Ekonomistyrelsen', 'STYRELSE', 'MIN01', '/MIN01/STY03/', 'AC', 'OK24'),
    ('AFD01', 'IT-Drift', 'AFDELING', 'STY02', '/MIN01/STY02/AFD01/', 'HK', 'OK24'),
    ('AFD02', 'Systemudvikling', 'AFDELING', 'STY02', '/MIN01/STY02/AFD02/', 'PROSA', 'OK24'),
    ('MIN02', 'Beskaeftigelsesministeriet', 'MINISTRY', NULL, '/MIN02/', 'AC', 'OK24'),
    ('STY04', 'Styrelsen for Arbejdsmarked og Rekruttering', 'STYRELSE', 'MIN02', '/MIN02/STY04/', 'AC', 'OK24'),
    ('STY05', 'Arbejdstilsynet', 'STYRELSE', 'MIN02', '/MIN02/STY05/', 'HK', 'OK24'),
    ('AFD03', 'Tilsyn Nord', 'AFDELING', 'STY05', '/MIN02/STY05/AFD03/', 'HK', 'OK24'),
    ('AFD04', 'Tilsyn Syd', 'AFDELING', 'STY05', '/MIN02/STY05/AFD04/', 'HK', 'OK24'),
    ('AFD05', 'Forskning og Analyse', 'AFDELING', 'STY04', '/MIN02/STY04/AFD05/', 'AC', 'OK24')
ON CONFLICT DO NOTHING;

-- Seed test users (bcrypt hashes for simple dev passwords)
-- ALL users share the same dev password: "password" (bcrypt hash below)
-- Note: These are bcrypt($2a$11$) hashes for development ONLY — never use in production
--
-- User overview:
-- ┌─────────┬──────────────────────────────┬──────────────┬─────────┬───────┐
-- │ User    │ Name                         │ Role         │ Org     │ Agr.  │
-- ├─────────┼──────────────────────────────┼──────────────┼─────────┼───────┤
-- │ admin01 │ Anna Vestergaard             │ GLOBAL_ADMIN │ MIN01   │ AC    │
-- │ admin02 │ Bo Kristensen                │ GLOBAL_ADMIN │ MIN02   │ AC    │
-- │ ladm01  │ Christine Dahl               │ LOCAL_ADMIN  │ STY02   │ HK    │
-- │ ladm02  │ Daniel Friis                 │ LOCAL_ADMIN  │ STY05   │ HK    │
-- │ hr01    │ Eva Mortensen                │ LOCAL_HR     │ MIN01   │ HK    │
-- │ hr02    │ Frederik Bak                 │ LOCAL_HR     │ MIN02   │ AC    │
-- │ mgr01   │ Gitte Holm                   │ LOCAL_LEADER │ AFD01   │ HK    │
-- │ mgr02   │ Henrik Noergaard             │ LOCAL_LEADER │ AFD03   │ HK    │
-- │ mgr03   │ Ida Soerensen                │ LOCAL_LEADER │ STY01   │ AC    │
-- │ emp001  │ Jesper Andersen              │ EMPLOYEE     │ STY01   │ AC    │
-- │ emp002  │ Karen Nielsen                │ EMPLOYEE     │ AFD01   │ HK    │
-- │ emp003  │ Lars Pedersen                │ EMPLOYEE     │ AFD02   │ PROSA │
-- │ emp004  │ Mette Hansen                 │ EMPLOYEE     │ STY03   │ AC    │
-- │ emp005  │ Niels Joergensen             │ EMPLOYEE     │ AFD01   │ HK    │
-- │ emp006  │ Olivia Madsen                │ EMPLOYEE     │ STY04   │ AC    │
-- │ emp007  │ Peter Larsen                 │ EMPLOYEE     │ AFD03   │ HK    │
-- │ emp008  │ Rikke Thomsen                │ EMPLOYEE     │ AFD04   │ HK    │
-- │ emp009  │ Soeren Jensen                │ EMPLOYEE     │ AFD05   │ AC    │
-- │ emp010  │ Tina Christensen             │ EMPLOYEE     │ AFD02   │ PROSA │
-- └─────────┴──────────────────────────────┴──────────────┴─────────┴───────┘
INSERT INTO users (user_id, username, password_hash, display_name, email, primary_org_id, agreement_code, ok_version) VALUES
    ('admin01', 'admin01', '$2a$11$9d/J80pl7VKKjtWsSqJdPuvJqBL/3sYomNGgL.TdUKq2Aw0e6k0Te', 'Anna Vestergaard', 'anna.vestergaard@fm.dk', 'MIN01', 'AC', 'OK24'),
    ('admin02', 'admin02', '$2a$11$9d/J80pl7VKKjtWsSqJdPuvJqBL/3sYomNGgL.TdUKq2Aw0e6k0Te', 'Bo Kristensen', 'bo.kristensen@bm.dk', 'MIN02', 'AC', 'OK24'),
    ('ladm01', 'ladm01', '$2a$11$9d/J80pl7VKKjtWsSqJdPuvJqBL/3sYomNGgL.TdUKq2Aw0e6k0Te', 'Christine Dahl', 'christine.dahl@statens-it.dk', 'STY02', 'HK', 'OK24'),
    ('ladm02', 'ladm02', '$2a$11$9d/J80pl7VKKjtWsSqJdPuvJqBL/3sYomNGgL.TdUKq2Aw0e6k0Te', 'Daniel Friis', 'daniel.friis@at.dk', 'STY05', 'HK', 'OK24'),
    ('hr01', 'hr01', '$2a$11$9d/J80pl7VKKjtWsSqJdPuvJqBL/3sYomNGgL.TdUKq2Aw0e6k0Te', 'Eva Mortensen', 'eva.mortensen@fm.dk', 'STY02', 'HK', 'OK24'),
    ('hr02', 'hr02', '$2a$11$9d/J80pl7VKKjtWsSqJdPuvJqBL/3sYomNGgL.TdUKq2Aw0e6k0Te', 'Frederik Bak', 'frederik.bak@bm.dk', 'STY04', 'AC', 'OK24'),
    ('mgr01', 'mgr01', '$2a$11$9d/J80pl7VKKjtWsSqJdPuvJqBL/3sYomNGgL.TdUKq2Aw0e6k0Te', 'Gitte Holm', 'gitte.holm@statens-it.dk', 'AFD01', 'HK', 'OK24'),
    ('mgr02', 'mgr02', '$2a$11$9d/J80pl7VKKjtWsSqJdPuvJqBL/3sYomNGgL.TdUKq2Aw0e6k0Te', 'Henrik Noergaard', 'henrik.noergaard@at.dk', 'AFD03', 'HK', 'OK24'),
    ('mgr03', 'mgr03', '$2a$11$9d/J80pl7VKKjtWsSqJdPuvJqBL/3sYomNGgL.TdUKq2Aw0e6k0Te', 'Ida Soerensen', 'ida.soerensen@mfk.dk', 'STY01', 'AC', 'OK24'),
    ('emp001', 'emp001', '$2a$11$9d/J80pl7VKKjtWsSqJdPuvJqBL/3sYomNGgL.TdUKq2Aw0e6k0Te', 'Jesper Andersen', 'jesper.andersen@mfk.dk', 'STY01', 'AC', 'OK24'),
    ('emp002', 'emp002', '$2a$11$9d/J80pl7VKKjtWsSqJdPuvJqBL/3sYomNGgL.TdUKq2Aw0e6k0Te', 'Karen Nielsen', 'karen.nielsen@statens-it.dk', 'AFD01', 'HK', 'OK24'),
    ('emp003', 'emp003', '$2a$11$9d/J80pl7VKKjtWsSqJdPuvJqBL/3sYomNGgL.TdUKq2Aw0e6k0Te', 'Lars Pedersen', 'lars.pedersen@statens-it.dk', 'AFD02', 'PROSA', 'OK24'),
    ('emp004', 'emp004', '$2a$11$9d/J80pl7VKKjtWsSqJdPuvJqBL/3sYomNGgL.TdUKq2Aw0e6k0Te', 'Mette Hansen', 'mette.hansen@oes.dk', 'STY03', 'AC', 'OK24'),
    ('emp005', 'emp005', '$2a$11$9d/J80pl7VKKjtWsSqJdPuvJqBL/3sYomNGgL.TdUKq2Aw0e6k0Te', 'Niels Joergensen', 'niels.joergensen@statens-it.dk', 'AFD01', 'HK', 'OK24'),
    ('emp006', 'emp006', '$2a$11$9d/J80pl7VKKjtWsSqJdPuvJqBL/3sYomNGgL.TdUKq2Aw0e6k0Te', 'Olivia Madsen', 'olivia.madsen@star.dk', 'STY04', 'AC', 'OK24'),
    ('emp007', 'emp007', '$2a$11$9d/J80pl7VKKjtWsSqJdPuvJqBL/3sYomNGgL.TdUKq2Aw0e6k0Te', 'Peter Larsen', 'peter.larsen@at.dk', 'AFD03', 'HK', 'OK24'),
    ('emp008', 'emp008', '$2a$11$9d/J80pl7VKKjtWsSqJdPuvJqBL/3sYomNGgL.TdUKq2Aw0e6k0Te', 'Rikke Thomsen', 'rikke.thomsen@at.dk', 'AFD04', 'HK', 'OK24'),
    ('emp009', 'emp009', '$2a$11$9d/J80pl7VKKjtWsSqJdPuvJqBL/3sYomNGgL.TdUKq2Aw0e6k0Te', 'Soeren Jensen', 'soeren.jensen@star.dk', 'AFD05', 'AC', 'OK24'),
    ('emp010', 'emp010', '$2a$11$9d/J80pl7VKKjtWsSqJdPuvJqBL/3sYomNGgL.TdUKq2Aw0e6k0Te', 'Tina Christensen', 'tina.christensen@statens-it.dk', 'AFD02', 'PROSA', 'OK24')
ON CONFLICT DO NOTHING;

-- Seed role assignments
-- Global Admins: admin01 (Finansministeriet), admin02 (Beskaeftigelsesministeriet)
-- Local Admins: ladm01 (Statens IT subtree), ladm02 (Arbejdstilsynet subtree)
-- Local HR: hr01 (Finansministeriet subtree), hr02 (Beskaeftigelsesministeriet subtree)
-- Local Leaders: mgr01 (IT-Drift), mgr02 (Tilsyn Nord), mgr03 (MFK)
-- Employees: emp001-010 at their respective departments
INSERT INTO role_assignments (user_id, role_id, org_id, scope_type, assigned_by) VALUES
    ('admin01', 'GLOBAL_ADMIN', NULL, 'GLOBAL', 'system'),
    ('admin02', 'GLOBAL_ADMIN', NULL, 'GLOBAL', 'system'),
    ('ladm01', 'LOCAL_ADMIN', 'STY02', 'ORG_AND_DESCENDANTS', 'admin01'),
    ('ladm02', 'LOCAL_ADMIN', 'STY05', 'ORG_AND_DESCENDANTS', 'admin02'),
    ('hr01', 'LOCAL_HR', 'MIN01', 'ORG_AND_DESCENDANTS', 'admin01'),
    ('hr02', 'LOCAL_HR', 'MIN02', 'ORG_AND_DESCENDANTS', 'admin02'),
    ('mgr01', 'LOCAL_LEADER', 'AFD01', 'ORG_AND_DESCENDANTS', 'ladm01'),
    ('mgr02', 'LOCAL_LEADER', 'AFD03', 'ORG_AND_DESCENDANTS', 'ladm02'),
    ('mgr03', 'LOCAL_LEADER', 'STY01', 'ORG_AND_DESCENDANTS', 'admin01'),
    ('emp001', 'EMPLOYEE', 'STY01', 'ORG_ONLY', 'mgr03'),
    ('emp002', 'EMPLOYEE', 'AFD01', 'ORG_ONLY', 'mgr01'),
    ('emp003', 'EMPLOYEE', 'AFD02', 'ORG_ONLY', 'ladm01'),
    ('emp004', 'EMPLOYEE', 'STY03', 'ORG_ONLY', 'admin01'),
    ('emp005', 'EMPLOYEE', 'AFD01', 'ORG_ONLY', 'mgr01'),
    ('emp006', 'EMPLOYEE', 'STY04', 'ORG_ONLY', 'hr02'),
    ('emp007', 'EMPLOYEE', 'AFD03', 'ORG_ONLY', 'mgr02'),
    ('emp008', 'EMPLOYEE', 'AFD04', 'ORG_ONLY', 'ladm02'),
    ('emp009', 'EMPLOYEE', 'AFD05', 'ORG_ONLY', 'hr02'),
    ('emp010', 'EMPLOYEE', 'AFD02', 'ORG_ONLY', 'ladm01')
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

CREATE TABLE IF NOT EXISTS user_project_selections (
    employee_id     TEXT        NOT NULL,
    project_id      UUID        NOT NULL REFERENCES projects(project_id),
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    -- S72 (TASK-7200, R4): per-user Skema row order. Inline here for greenfield;
    -- legacy DBs gain the column via the guarded 's72-skema-row-preferences-schema'
    -- block near EOF, which also backfills existing rows from the matching
    -- projects.sort_order (duplicates expected — readers tiebreak
    -- ORDER BY sort_order, project_code per SPRINT-72 R4). Placed last so the
    -- greenfield column order matches what the legacy ALTER produces.
    sort_order      INT         NOT NULL DEFAULT 0,
    PRIMARY KEY (employee_id, project_id)
);
CREATE INDEX IF NOT EXISTS idx_user_project_sel_employee ON user_project_selections(employee_id);

-- SPRINT 56 (TASK-5602B): timer feature retired. The check-in/out timer write path
-- (the /api/timer/* endpoints + TimerSessionRepository) was removed; self-recorded work
-- time now lives in work_time_projection (above). This DROP is idempotent — a no-op on a
-- fresh DB (table never created) and a cleanup on existing deployments. Co-landed with the
-- TASK-5605 code removal after a grep-zero check that no code references timer_sessions.
-- NOTE: TimerCheckedIn/TimerCheckedOut events are RETAINED in EventSerializer for historical
-- event replay/backfill (append-only store) — only the table + live write path are dropped.
DROP TABLE IF EXISTS timer_sessions;

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
    effective_to            DATE,
    -- S73 / TASK-7301 (SPRINT-73 R2, owner ruling D-A 2026-06-13): full-day-only day-shape flag.
    -- TRUE ⇒ a registration of this entitlement type must equal the day's ADR-032 consumption
    -- basis EXACTLY (the Skema save guard enforces it). Config-driven so the rule is versioned
    -- and evented like every other entitlement-policy field. Greenfield CARE_DAY/SENIOR_DAY
    -- seeds below carry TRUE inline; legacy DBs gain the column + backfill via the guarded
    -- 's73-full-day-only-schema' segment at the end of this file.
    full_day_only           BOOLEAN     NOT NULL DEFAULT FALSE,
    -- S68 Step-7a Codex c2 (B1 soundness) — the Danish vacation year is STATUTORILY fixed at
    -- 1 Sep – 31 Aug (samtidighedsferie, LBK 230/2021); the entire §21/§24 settlement boundary
    -- (31 Dec of the ferieår-end year) is built on reset_month = 9 for VACATION. A VACATION config
    -- with any other reset_month is legally malformed and would let the settlement poller (which
    -- reads the live config) diverge from the dated-snapshot valuation. Pin it at the data layer so
    -- the "uniform 9" invariant holds for every fresh-DB write path (endpoint, seeder, direct SQL);
    -- the legacy-DB upgrade path lands the same CHECK via the schema_migrations-guarded ALTER block
    -- 's68-vacation-reset-month-check' below (CREATE TABLE IF NOT EXISTS is a no-op on a legacy DB).
    -- Other types (CARE_DAY/SENIOR_DAY = calendar-year reset_month 1) are unconstrained.
    CONSTRAINT entitlement_configs_vacation_reset_month CHECK (
        entitlement_type <> 'VACATION' OR reset_month = 9
    ),
    -- S73 / TASK-7301 (SPRINT-73 R2 construction-enforcement, the S68-B1 lesson): the D-A
    -- owner ruling "CARE_DAY + SENIOR_DAY are FULL-DAY-ONLY" is a PRODUCT RULE, not a default —
    -- an admin config write (endpoint, seeder, direct SQL) must not be able to silently
    -- un-rule it. The base CREATE bakes the CHECK for greenfield DBs; the legacy path lands
    -- the SAME named constraint via the 's73-full-day-only-schema' DO-block (CREATE TABLE IF
    -- NOT EXISTS is a no-op on a legacy DB). Flipping this later is a deliberate schema/owner
    -- change, mirroring the S68 VACATION reset_month enforcement above.
    CONSTRAINT entitlement_configs_full_day_only_types CHECK (
        entitlement_type NOT IN ('CARE_DAY', 'SENIOR_DAY') OR full_day_only
    )
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

-- =========================================================================
-- S73 / TASK-7301 (SPRINT-73 R2, owner ruling D-A 2026-06-13) — the FULL-DAY-ONLY
--   day-shape flag on entitlement_configs (CARE_DAY + SENIOR_DAY are whole days,
--   "hele dage"; CHILD_SICK stays hours-based).
--
-- PLACEMENT (deliberate — differs from the at-EOF S68/S72 precedents): this guarded
-- segment sits BEFORE the seed INSERT below because the seed now references the new
-- full_day_only column inline (the R2 ordering pin: the CHECK must never reject
-- init.sql's own seeds). On a LEGACY DB the base CREATE TABLE IF NOT EXISTS is a
-- no-op, so without this block the seed INSERT would 42703 on the missing column —
-- the column must land first in file order.
--
-- 3-path idempotent (the s68-vacation-reset-month-check shape):
--   • greenfield first apply — the ALTER no-ops against the inline column, the
--     backfill touches zero rows (the seeds have not been inserted yet; they carry
--     TRUE inline below), and the DROP-then-ADD re-lands the same named CHECK the
--     base CREATE already baked;
--   • legacy first apply — the column lands, the type-keyed backfill flips every
--     existing CARE_DAY/SENIOR_DAY row (ALL agreement/OK pairs) to TRUE, and ONLY
--     THEN is the constraint added (remediate-then-constrain, the S68 precedent —
--     the R2 ordering pin: backfill UPDATE BEFORE ADD CONSTRAINT);
--   • any re-apply — the schema_migrations ledger short-circuits.
--
-- The S73-FULL-DAY-ONLY-SEGMENT markers are extracted VERBATIM by
-- FullDayOnlyMigrationTests (the S71/S72 harness pattern: the test runs this exact
-- segment against a reconstructed pre-S73 schema, twice) — keep the marker lines
-- intact and keep all S73 DDL between them.
-- =========================================================================
-- S73-FULL-DAY-ONLY-SEGMENT-BEGIN
DO $$
BEGIN
    INSERT INTO schema_migrations (migration_id, notes)
    VALUES ('s73-full-day-only-schema', 'SPRINT-73 R2 / owner ruling D-A: entitlement_configs.full_day_only BOOLEAN NOT NULL DEFAULT FALSE + type-keyed backfill (CARE_DAY + SENIOR_DAY -> TRUE, all agreement/OK pairs) + the entitlement_configs_full_day_only_types construction-enforcement CHECK (remediate-then-constrain).')
    ON CONFLICT (migration_id) DO NOTHING;

    IF NOT FOUND THEN
        RETURN;
    END IF;

    -- Legacy path: the inline column on the base CREATE cannot reach a pre-S73 DB.
    ALTER TABLE entitlement_configs
    ADD COLUMN IF NOT EXISTS full_day_only BOOLEAN NOT NULL DEFAULT FALSE;

    -- Backfill BEFORE the constraint (R2 ordering pin): type-keyed across ALL
    -- agreement/OK pairs — history rows included, so dated reads at any asOf
    -- resolve the D-A rule consistently.
    UPDATE entitlement_configs
    SET full_day_only = TRUE
    WHERE entitlement_type IN ('CARE_DAY', 'SENIOR_DAY')
      AND NOT full_day_only;

    -- Construction-enforcement (R2, the S68-B1 uniform-by-construction lesson):
    -- DROP-then-ADD covers both greenfield (where the base CREATE already baked
    -- the CHECK) and legacy (where it cannot exist yet).
    ALTER TABLE entitlement_configs
    DROP CONSTRAINT IF EXISTS entitlement_configs_full_day_only_types;

    ALTER TABLE entitlement_configs
    ADD CONSTRAINT entitlement_configs_full_day_only_types
    CHECK (entitlement_type NOT IN ('CARE_DAY', 'SENIOR_DAY') OR full_day_only);
END
$$;
-- S73-FULL-DAY-ONLY-SEGMENT-END

-- Seed entitlement configs: AC/HK/PROSA × OK24/OK26 × 5 types = 30 rows
-- S30 TASK-3006 (ADR-021 D3): seed-INSERT rewrite to include effective_from
-- + ON CONFLICT on (natural_key, effective_from) targeting idx_ec_natural_key_history
-- so the block is idempotent across `docker compose down -v && up` re-runs (S29
-- TASK-2906 / ADR-020 D3 precedent on wage_type_mappings at init.sql:1335,1353).
-- Anchor '0001-01-01' matches the base CREATE TABLE default at L1129 — sentinel
-- "pre-launch" effective_from, NOT a real agreement-start date.
-- S73 / TASK-7301 (R2 ordering pin, Step-0b c2): the seed INSERTs carry full_day_only INLINE —
-- the entitlement_configs_full_day_only_types CHECK must never reject init.sql's own seeds.
-- CARE_DAY + SENIOR_DAY rows (ALL agreement/OK pairs, AC variants included) carry TRUE per the
-- D-A owner ruling; every other type carries FALSE (CHILD_SICK stays hours-based).
INSERT INTO entitlement_configs (entitlement_type, agreement_code, ok_version, annual_quota, accrual_model, reset_month, carryover_max, pro_rate_by_part_time, is_per_episode, min_age, description, effective_from, full_day_only) VALUES
    -- VACATION: 25 days, reset September, carryover 5
    -- S60 / ADR-030: accrual_model = MONTHLY_ACCRUAL (samtidighedsferie, ~2,08 d/md).
    -- Sentinel reseed (NOT supersession) — preserves ADR-021 D5 invariant (no new effective_from row).
    -- S63 / ADR-031: pro_rate_by_part_time = false — flat day-count per Ferieloven §5 (sentinel reseed, NOT supersession — preserves ADR-021 D5 invariant)
    ('VACATION', 'AC', 'OK24', 25, 'MONTHLY_ACCRUAL', 9, 5, false, false, NULL, 'Ferie – 25 dage', '0001-01-01', false),
    ('VACATION', 'AC', 'OK26', 25, 'MONTHLY_ACCRUAL', 9, 5, false, false, NULL, 'Ferie – 25 dage', '0001-01-01', false),
    ('VACATION', 'HK', 'OK24', 25, 'MONTHLY_ACCRUAL', 9, 5, false, false, NULL, 'Ferie – 25 dage', '0001-01-01', false),
    ('VACATION', 'HK', 'OK26', 25, 'MONTHLY_ACCRUAL', 9, 5, false, false, NULL, 'Ferie – 25 dage', '0001-01-01', false),
    ('VACATION', 'PROSA', 'OK24', 25, 'MONTHLY_ACCRUAL', 9, 5, false, false, NULL, 'Ferie – 25 dage', '0001-01-01', false),
    ('VACATION', 'PROSA', 'OK26', 25, 'MONTHLY_ACCRUAL', 9, 5, false, false, NULL, 'Ferie – 25 dage', '0001-01-01', false),
    -- SPECIAL_HOLIDAY: 5 days, reset September, no carryover
    -- S60 / ADR-030: MONTHLY_ACCRUAL (~0,42 d/md); no forskud (ferieaftale §13 stk.4) enforced in rule engine.
    -- S63 / ADR-031: pro_rate_by_part_time = false — flat day-count per Ferieloven §5 (sentinel reseed, NOT supersession — preserves ADR-021 D5 invariant)
    ('SPECIAL_HOLIDAY', 'AC', 'OK24', 5, 'MONTHLY_ACCRUAL', 9, 0, false, false, NULL, 'Særlige feriedage – 5 dage', '0001-01-01', false),
    ('SPECIAL_HOLIDAY', 'AC', 'OK26', 5, 'MONTHLY_ACCRUAL', 9, 0, false, false, NULL, 'Særlige feriedage – 5 dage', '0001-01-01', false),
    ('SPECIAL_HOLIDAY', 'HK', 'OK24', 5, 'MONTHLY_ACCRUAL', 9, 0, false, false, NULL, 'Særlige feriedage – 5 dage', '0001-01-01', false),
    ('SPECIAL_HOLIDAY', 'HK', 'OK26', 5, 'MONTHLY_ACCRUAL', 9, 0, false, false, NULL, 'Særlige feriedage – 5 dage', '0001-01-01', false),
    ('SPECIAL_HOLIDAY', 'PROSA', 'OK24', 5, 'MONTHLY_ACCRUAL', 9, 0, false, false, NULL, 'Særlige feriedage – 5 dage', '0001-01-01', false),
    ('SPECIAL_HOLIDAY', 'PROSA', 'OK26', 5, 'MONTHLY_ACCRUAL', 9, 0, false, false, NULL, 'Særlige feriedage – 5 dage', '0001-01-01', false),
    -- CARE_DAY: 2 days, reset January, no carryover, not pro-rated
    -- S73 / TASK-7301 (D-A): full_day_only = TRUE — omsorgsdage are whole days ("hele dage").
    ('CARE_DAY', 'AC', 'OK24', 2, 'IMMEDIATE', 1, 0, false, false, NULL, 'Omsorgsdage – 2 dage', '0001-01-01', true),
    ('CARE_DAY', 'AC', 'OK26', 2, 'IMMEDIATE', 1, 0, false, false, NULL, 'Omsorgsdage – 2 dage', '0001-01-01', true),
    ('CARE_DAY', 'HK', 'OK24', 2, 'IMMEDIATE', 1, 0, false, false, NULL, 'Omsorgsdage – 2 dage', '0001-01-01', true),
    ('CARE_DAY', 'HK', 'OK26', 2, 'IMMEDIATE', 1, 0, false, false, NULL, 'Omsorgsdage – 2 dage', '0001-01-01', true),
    ('CARE_DAY', 'PROSA', 'OK24', 2, 'IMMEDIATE', 1, 0, false, false, NULL, 'Omsorgsdage – 2 dage', '0001-01-01', true),
    ('CARE_DAY', 'PROSA', 'OK26', 2, 'IMMEDIATE', 1, 0, false, false, NULL, 'Omsorgsdage – 2 dage', '0001-01-01', true),
    -- CHILD_SICK: AC=1, HK=2, PROSA=3, per-episode (stays hours-based per D-A — full_day_only FALSE)
    ('CHILD_SICK', 'AC', 'OK24', 1, 'IMMEDIATE', 1, 0, false, true, NULL, 'Barn syg – 1 dag per episode', '0001-01-01', false),
    ('CHILD_SICK', 'AC', 'OK26', 1, 'IMMEDIATE', 1, 0, false, true, NULL, 'Barn syg – 1 dag per episode', '0001-01-01', false),
    ('CHILD_SICK', 'HK', 'OK24', 2, 'IMMEDIATE', 1, 0, false, true, NULL, 'Barn syg – 2 dage per episode', '0001-01-01', false),
    ('CHILD_SICK', 'HK', 'OK26', 2, 'IMMEDIATE', 1, 0, false, true, NULL, 'Barn syg – 2 dage per episode', '0001-01-01', false),
    ('CHILD_SICK', 'PROSA', 'OK24', 3, 'IMMEDIATE', 1, 0, false, true, NULL, 'Barn syg – 3 dage per episode', '0001-01-01', false),
    ('CHILD_SICK', 'PROSA', 'OK26', 3, 'IMMEDIATE', 1, 0, false, true, NULL, 'Barn syg – 3 dage per episode', '0001-01-01', false),
    -- SENIOR_DAY: 2 days/year for age 62+ (S37 TASK-3703 Bug #3 absorption 2026-05-21, Path B seed-side fix
    -- per interim-expert decision; previously paired-broken with quota=0 + min_age=60). Bug-with-no-past-impact.
    -- S73 / TASK-7301 (D-A): full_day_only = TRUE — seniordage are whole days ("hele dage").
    ('SENIOR_DAY', 'AC',    'OK24', 2, 'IMMEDIATE', 1, 0, false, false, 62, 'Seniordage – kræver alder 62+', '0001-01-01', true),
    ('SENIOR_DAY', 'AC',    'OK26', 2, 'IMMEDIATE', 1, 0, false, false, 62, 'Seniordage – kræver alder 62+', '0001-01-01', true),
    ('SENIOR_DAY', 'HK',    'OK24', 2, 'IMMEDIATE', 1, 0, false, false, 62, 'Seniordage – kræver alder 62+', '0001-01-01', true),
    ('SENIOR_DAY', 'HK',    'OK26', 2, 'IMMEDIATE', 1, 0, false, false, 62, 'Seniordage – kræver alder 62+', '0001-01-01', true),
    ('SENIOR_DAY', 'PROSA', 'OK24', 2, 'IMMEDIATE', 1, 0, false, false, 62, 'Seniordage – kræver alder 62+', '0001-01-01', true),
    ('SENIOR_DAY', 'PROSA', 'OK26', 2, 'IMMEDIATE', 1, 0, false, false, 62, 'Seniordage – kræver alder 62+', '0001-01-01', true),
    -- S37 TASK-3701 Bug #1 absorption: AC variants (AC_RESEARCH + AC_TEACHING) mirror AC base values
    -- per interim-expert decision 2026-05-21. Bug-with-no-past-impact under pre-launch posture.
    -- VACATION inherits Ferieloven (universal); other 4 inherit AC overenskomst by structural inheritance.
    -- S60 / ADR-030: MONTHLY_ACCRUAL reseed for the AC-variant codes that exist ONLY in init.sql
    -- (DefaultEntitlementConfigs factory covers AC/HK/PROSA only — see TASK-6003). Sentinel reseed, NOT supersession.
    -- S73 / TASK-7301 (D-A): the AC-variant CARE_DAY/SENIOR_DAY rows mirror the base TRUE flag.
    ('VACATION',        'AC_RESEARCH', 'OK24', 25, 'MONTHLY_ACCRUAL', 9, 5, false, false, NULL, 'Ferie – 25 dage',                  '0001-01-01', false),
    ('VACATION',        'AC_RESEARCH', 'OK26', 25, 'MONTHLY_ACCRUAL', 9, 5, false, false, NULL, 'Ferie – 25 dage',                  '0001-01-01', false),
    ('VACATION',        'AC_TEACHING', 'OK24', 25, 'MONTHLY_ACCRUAL', 9, 5, false, false, NULL, 'Ferie – 25 dage',                  '0001-01-01', false),
    ('VACATION',        'AC_TEACHING', 'OK26', 25, 'MONTHLY_ACCRUAL', 9, 5, false, false, NULL, 'Ferie – 25 dage',                  '0001-01-01', false),
    ('SPECIAL_HOLIDAY', 'AC_RESEARCH', 'OK24',  5, 'MONTHLY_ACCRUAL', 9, 0, false, false, NULL, 'Særlige feriedage – 5 dage',       '0001-01-01', false),
    ('SPECIAL_HOLIDAY', 'AC_RESEARCH', 'OK26',  5, 'MONTHLY_ACCRUAL', 9, 0, false, false, NULL, 'Særlige feriedage – 5 dage',       '0001-01-01', false),
    ('SPECIAL_HOLIDAY', 'AC_TEACHING', 'OK24',  5, 'MONTHLY_ACCRUAL', 9, 0, false, false, NULL, 'Særlige feriedage – 5 dage',       '0001-01-01', false),
    ('SPECIAL_HOLIDAY', 'AC_TEACHING', 'OK26',  5, 'MONTHLY_ACCRUAL', 9, 0, false, false, NULL, 'Særlige feriedage – 5 dage',       '0001-01-01', false),
    ('CARE_DAY',        'AC_RESEARCH', 'OK24',  2, 'IMMEDIATE', 1, 0, false, false, NULL, 'Omsorgsdage – 2 dage',             '0001-01-01', true),
    ('CARE_DAY',        'AC_RESEARCH', 'OK26',  2, 'IMMEDIATE', 1, 0, false, false, NULL, 'Omsorgsdage – 2 dage',             '0001-01-01', true),
    ('CARE_DAY',        'AC_TEACHING', 'OK24',  2, 'IMMEDIATE', 1, 0, false, false, NULL, 'Omsorgsdage – 2 dage',             '0001-01-01', true),
    ('CARE_DAY',        'AC_TEACHING', 'OK26',  2, 'IMMEDIATE', 1, 0, false, false, NULL, 'Omsorgsdage – 2 dage',             '0001-01-01', true),
    ('CHILD_SICK',      'AC_RESEARCH', 'OK24',  1, 'IMMEDIATE', 1, 0, false, true,  NULL, 'Barn syg – 1 dag per episode',     '0001-01-01', false),
    ('CHILD_SICK',      'AC_RESEARCH', 'OK26',  1, 'IMMEDIATE', 1, 0, false, true,  NULL, 'Barn syg – 1 dag per episode',     '0001-01-01', false),
    ('CHILD_SICK',      'AC_TEACHING', 'OK24',  1, 'IMMEDIATE', 1, 0, false, true,  NULL, 'Barn syg – 1 dag per episode',     '0001-01-01', false),
    ('CHILD_SICK',      'AC_TEACHING', 'OK26',  1, 'IMMEDIATE', 1, 0, false, true,  NULL, 'Barn syg – 1 dag per episode',     '0001-01-01', false),
    ('SENIOR_DAY',      'AC_RESEARCH', 'OK24',  2, 'IMMEDIATE', 1, 0, false, false, 62,   'Seniordage – kræver alder 62+',    '0001-01-01', true),
    ('SENIOR_DAY',      'AC_RESEARCH', 'OK26',  2, 'IMMEDIATE', 1, 0, false, false, 62,   'Seniordage – kræver alder 62+',    '0001-01-01', true),
    ('SENIOR_DAY',      'AC_TEACHING', 'OK24',  2, 'IMMEDIATE', 1, 0, false, false, 62,   'Seniordage – kræver alder 62+',    '0001-01-01', true),
    ('SENIOR_DAY',      'AC_TEACHING', 'OK26',  2, 'IMMEDIATE', 1, 0, false, false, 62,   'Seniordage – kræver alder 62+',    '0001-01-01', true)
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
    -- S66 / ADR-032 D2: the AUTHORITATIVE per-absence consumption record (feriedage),
    -- computed at booking via the dated per-day system norm (D1) and recorded verbatim —
    -- all read surfaces sum this column; no re-derivation, replay-deterministic. NULLABLE:
    -- pre-S66 rows carry NULL until backfilled to the convention in force when written
    -- (hours / 7.4, rounded 4dp — recorded once, never revalued). Revaluation on a
    -- fullDayHours-affecting profile change (D4) overwrites this value in-tx.
    feriedage                   NUMERIC(8,4)    NULL,
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
-- SPRINT 56 (TASK-5602A): Self-recorded work-time projection
-- ============================================================
-- Read-path projection for "Arbejdstid" self-recorded work time, fed by WorkTimeRegistered
-- (ADR-018 D13 sync-in-tx). Unlike time_entries_projection (append-per-event), this is a
-- LATEST-WINS aggregate per (employee_id, date): re-saving a day emits a NEW superseding event,
-- and the projection holds only the latest state. outbox_id carries event ordering so a stale
-- event never overwrites a newer one (apply only when incoming outbox_id >= stored).
CREATE TABLE IF NOT EXISTS work_time_projection (
    employee_id     TEXT            NOT NULL,
    date            DATE            NOT NULL,
    intervals       JSONB           NOT NULL DEFAULT '[]'::jsonb,  -- [{ "start":"HH:mm", "end":"HH:mm" }, ...]
    manual_hours    NUMERIC(8,4)    NOT NULL DEFAULT 0,            -- "Tilføj timer" direct daily hours
    occurred_at     TIMESTAMPTZ     NOT NULL,
    actor_id        TEXT,
    actor_role      TEXT,
    correlation_id  UUID,
    outbox_id       BIGINT          NOT NULL,                      -- event ordering / latest-wins guard
    PRIMARY KEY (employee_id, date)
);
-- PK (employee_id, date) already serves month-range reads (employee_id + date BETWEEN).

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
-- S68 / ADR-033 slice 1a (Step-7a Codex c2 B1) — VACATION reset_month must be 9.
--   The Danish vacation year is statutorily fixed at 1 Sep – 31 Aug (samtidighedsferie,
--   LBK 230/2021); the §21/§24 settlement boundary (31 Dec of the ferieår-end year) is
--   built on reset_month = 9 for VACATION, and the close poller reads the LIVE config's
--   reset_month while settlement valuation reads the DATED closed-year config — a non-9
--   VACATION reset_month would let the two diverge. The base CREATE TABLE bakes the CHECK
--   for greenfield DBs; `CREATE TABLE IF NOT EXISTS` is a no-op on a legacy DB, so this
--   schema_migrations-guarded ALTER lands the SAME CHECK there (the legacy backstop the
--   inline CHECK alone cannot reach). Any pre-existing non-9 VACATION row is a
--   legally-malformed config that should never have been accepted — remediated to 9 (the
--   only lawful value) BEFORE the ADD so the constraint validates. Idempotent (ledger-guarded
--   + DROP-then-ADD covers both greenfield, where the CREATE already baked it, and legacy).
-- =========================================================================
DO $$
BEGIN
    INSERT INTO schema_migrations (migration_id, notes)
    VALUES ('s68-vacation-reset-month-check', 'ADR-033: VACATION reset_month pinned to 9 (statutory ferieår); §21/§24 boundary soundness')
    ON CONFLICT (migration_id) DO NOTHING;

    IF NOT FOUND THEN
        RETURN;
    END IF;

    UPDATE entitlement_configs
    SET reset_month = 9
    WHERE entitlement_type = 'VACATION' AND reset_month <> 9;

    ALTER TABLE entitlement_configs
    DROP CONSTRAINT IF EXISTS entitlement_configs_vacation_reset_month;

    ALTER TABLE entitlement_configs
    ADD CONSTRAINT entitlement_configs_vacation_reset_month
    CHECK (entitlement_type <> 'VACATION' OR reset_month = 9);
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

-- =========================================================================
-- S43 / ADR-026 D1 — audit_projection table (Phase 4e audit visibility surface).
--
-- 3rd projection table after time_entries_projection + absences_projection
-- (S27 sync-in-tx pattern per ADR-018 D13). Path C event-projection per
-- ADR-026 (path A/B intentionally avoided to prevent the cross-process issues
-- that derailed ADR-024).
--
-- 13 columns matching ADR-026 D1 schema at L40-65. visibility_scope CHECK
-- constraint enforces the 3-tier semantic at insert time; the companion
-- chk_target_org_required_when_tenant CHECK constraint enforces that
-- TENANT_TARGETED rows MUST have target_org_id NOT NULL (and global rows
-- may have target_org_id NULL).
--
-- 5 partial indexes covering: (a) target-org-scoped queries; (b) global-
-- tenant-visible scans; (c) actor-org-scoped queries (per ADR-026 D5
-- secondary scope-by-actor pattern); (d) event-type+time queries (audit
-- search-by-type); (e) outbox_id backfill ordering per ADR-018 D13.
--
-- Naming: idx_* convention (75/75 existing indexes use idx_*); diverges
-- from ADR-026 L60-64 ix_* text per Step 4 cycle 1 absorption.
--
-- Sub-Sprint 1 (S43) ships schema + repo + interface + registry + backfill.
-- Sub-Sprint 2 (S44) wires 6-mapper exemplar family (Org/User/RoleAssignment).
-- Sub-Sprint 2b/c (S44b/c) wires remaining ~47 mapper families.
-- Sub-Sprint 2f (S44f) ships GET /api/admin/audit + AuditLogView.tsx.
-- Sub-Sprint 3 (S45) lands cutover-dependent Phase E tests #1, #3, #4.
-- =========================================================================

CREATE TABLE IF NOT EXISTS audit_projection (
    projection_id            UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    event_id                 UUID         NOT NULL UNIQUE,
    outbox_id                BIGINT       NOT NULL,
    event_type               TEXT         NOT NULL,
    visibility_scope         TEXT         NOT NULL CHECK (visibility_scope IN ('TENANT_TARGETED', 'GLOBAL_TENANT_VISIBLE', 'GLOBAL_ADMIN_ONLY')),
    target_org_id            TEXT         NULL REFERENCES organizations(org_id),
    target_resource_id       TEXT         NULL,
    actor_id                 TEXT         NULL,
    actor_primary_org_id     TEXT         NULL,
    occurred_at              TIMESTAMPTZ  NOT NULL,
    correlation_id           UUID         NULL,
    details                  JSONB        NOT NULL,
    projected_at             TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    CONSTRAINT chk_target_org_required_when_tenant
        CHECK (
            (visibility_scope = 'TENANT_TARGETED'      AND target_org_id IS NOT NULL) OR
            (visibility_scope IN ('GLOBAL_TENANT_VISIBLE', 'GLOBAL_ADMIN_ONLY'))
        )
);

-- Target-org-scoped queries (LocalAdmin scope-by-target primary path).
CREATE INDEX IF NOT EXISTS idx_audit_projection_target_org_time
    ON audit_projection (target_org_id, occurred_at DESC)
    WHERE target_org_id IS NOT NULL;

-- Global-tenant-visible scans (any-org-admin can see).
CREATE INDEX IF NOT EXISTS idx_audit_projection_global_visible
    ON audit_projection (occurred_at DESC)
    WHERE visibility_scope = 'GLOBAL_TENANT_VISIBLE';

-- Actor-org-scoped queries (LocalAdmin scope-by-actor secondary path per ADR-026 D5).
CREATE INDEX IF NOT EXISTS idx_audit_projection_actor_org_time
    ON audit_projection (actor_primary_org_id, occurred_at DESC)
    WHERE actor_primary_org_id IS NOT NULL;

-- Event-type+time queries (audit search-by-type).
CREATE INDEX IF NOT EXISTS idx_audit_projection_event_type_time
    ON audit_projection (event_type, occurred_at DESC);

-- Backfill ordering per ADR-018 D13 (outbox_id is the canonical ordering column).
CREATE INDEX IF NOT EXISTS idx_audit_projection_outbox_id
    ON audit_projection (outbox_id);

-- Ledger entry for s43-d1 per a0e30ed governance (ADRs project sprint numbers; actual execution at S43).
DO $$
BEGIN
    INSERT INTO schema_migrations (migration_id, notes)
    VALUES ('s43-d1-audit-projection-table', 'ADR-026 D1: audit_projection table + 5 partial indexes + chk_target_org_required_when_tenant CHECK; Sub-Sprint 1 plumbing per path C event-projection')
    ON CONFLICT (migration_id) DO NOTHING;
END
$$;

-- =========================================================================
-- S48 / ADR-027 — Reporting-Line Hierarchy (Migration Phase 1)
--   Temporal reporting_lines table complementing ADR-008 org hierarchy.
--   Tree boundary: per MINISTRY/STYRELSE (tree_root_org_id).
--   Relationships: PRIMARY (one per employee) + ACTING (vikarierende leder).
--   Pattern: follows local_agreement_profiles (ADR-017 D1) temporal model.
-- =========================================================================

CREATE TABLE IF NOT EXISTS reporting_lines (
    reporting_line_id   UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    employee_id         TEXT        NOT NULL REFERENCES users(user_id),
    manager_id          TEXT        NOT NULL REFERENCES users(user_id),
    tree_root_org_id    TEXT        NOT NULL REFERENCES organizations(org_id),
    relationship        TEXT        NOT NULL DEFAULT 'PRIMARY'
                        CHECK (relationship IN ('PRIMARY', 'ACTING')),
    effective_from      DATE        NOT NULL,
    effective_to        DATE,
    source              TEXT        NOT NULL DEFAULT 'MANUAL'
                        CHECK (source IN ('MANUAL', 'HR_IMPORT')),
    version             BIGINT      NOT NULL DEFAULT 1,
    created_by          TEXT        NOT NULL,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CHECK (employee_id <> manager_id)
);

-- ADR-027 D1 / ADR-017 D1 pattern: at most one active PRIMARY per employee.
CREATE UNIQUE INDEX IF NOT EXISTS uq_reporting_line_active_primary
    ON reporting_lines (employee_id)
    WHERE effective_to IS NULL AND relationship = 'PRIMARY';

-- At most one active ACTING per employee (one acting-manager at a time).
CREATE UNIQUE INDEX IF NOT EXISTS uq_reporting_line_active_acting
    ON reporting_lines (employee_id)
    WHERE effective_to IS NULL AND relationship = 'ACTING';

-- Lookup: "who are my direct reports?" (active lines by manager).
CREATE INDEX IF NOT EXISTS idx_reporting_lines_manager
    ON reporting_lines (manager_id)
    WHERE effective_to IS NULL;

-- Lookup: history for an employee (reverse chronological).
CREATE INDEX IF NOT EXISTS idx_reporting_lines_employee_history
    ON reporting_lines (employee_id, effective_from DESC);

-- Tree-scoped queries (per styrelse/ministry).
CREATE INDEX IF NOT EXISTS idx_reporting_lines_tree_root
    ON reporting_lines (tree_root_org_id)
    WHERE effective_to IS NULL;

-- Audit trail for reporting-line lifecycle events.
CREATE TABLE IF NOT EXISTS reporting_line_audit (
    audit_id            BIGSERIAL   PRIMARY KEY,
    reporting_line_id   UUID        NOT NULL,
    action              TEXT        NOT NULL CHECK (action IN (
        'ASSIGNED', 'SUPERSEDED', 'ACTING_ASSIGNED', 'ACTING_ENDED',
        'BULK_IMPORTED', 'MANAGER_DEACTIVATED'
    )),
    actor_id            TEXT        NOT NULL,
    correlation_id      UUID,
    version_before      BIGINT,
    version_after       BIGINT,
    metadata            JSONB,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_reporting_line_audit_line
    ON reporting_line_audit(reporting_line_id);

-- ── Seed reporting lines (12 PRIMARY + 1 ACTING = 13 rows across 7 trees) ──
-- Tree roots have NO reporting line (they are root by absence of a row).
-- tree_root_org_id derivation: nearest MINISTRY or STYRELSE ancestor.
INSERT INTO reporting_lines (employee_id, manager_id, tree_root_org_id, relationship, effective_from, source, created_by) VALUES
    -- Tree STY01 (Medarbejder- og Kompetencestyrelsen): root = mgr03
    ('emp001', 'mgr03',  'STY01', 'PRIMARY', '2024-01-01', 'MANUAL', 'SYSTEM'),
    -- Tree STY02 (Statens IT): root = ladm01
    ('hr01',   'ladm01', 'STY02', 'PRIMARY', '2024-01-01', 'MANUAL', 'SYSTEM'),
    ('mgr01',  'ladm01', 'STY02', 'PRIMARY', '2024-01-01', 'MANUAL', 'SYSTEM'),
    ('emp002', 'mgr01',  'STY02', 'PRIMARY', '2024-01-01', 'MANUAL', 'SYSTEM'),
    ('emp005', 'mgr01',  'STY02', 'PRIMARY', '2024-01-01', 'MANUAL', 'SYSTEM'),
    ('emp003', 'ladm01', 'STY02', 'PRIMARY', '2024-01-01', 'MANUAL', 'SYSTEM'),
    ('emp010', 'ladm01', 'STY02', 'PRIMARY', '2024-01-01', 'MANUAL', 'SYSTEM'),
    -- Tree STY04 (Styrelsen for Arbejdsmarked og Rekruttering): root = hr02
    ('emp006', 'hr02',   'STY04', 'PRIMARY', '2024-01-01', 'MANUAL', 'SYSTEM'),
    ('emp009', 'hr02',   'STY04', 'PRIMARY', '2024-01-01', 'MANUAL', 'SYSTEM'),
    -- Tree STY05 (Arbejdstilsynet): root = ladm02
    ('mgr02',  'ladm02', 'STY05', 'PRIMARY', '2024-01-01', 'MANUAL', 'SYSTEM'),
    ('emp007', 'mgr02',  'STY05', 'PRIMARY', '2024-01-01', 'MANUAL', 'SYSTEM'),
    ('emp008', 'ladm02', 'STY05', 'PRIMARY', '2024-01-01', 'MANUAL', 'SYSTEM'),
    -- Trees MIN01/MIN02/STY03: single-person roots (admin01, admin02, emp004) — no lines needed.
    -- ACTING line: emp002 has acting manager ladm01 (simulating mgr01 on vacation)
    ('emp002', 'ladm01', 'STY02', 'ACTING',  '2024-06-01', 'MANUAL', 'SYSTEM')
ON CONFLICT DO NOTHING;

-- Ledger entry for S48
DO $$
BEGIN
    INSERT INTO schema_migrations (migration_id, notes)
    VALUES ('s48-d1-reporting-lines-table', 'ADR-027 D1: reporting_lines + reporting_line_audit tables, 5 indexes (2 partial-unique + 3 lookup), 13 seed rows (12 PRIMARY + 1 ACTING)')
    ON CONFLICT (migration_id) DO NOTHING;
END
$$;

-- =========================================================================
-- S49 / ADR-027 Phase 2+3 — Approval routing columns on approval_periods
--   designated_approver_id: who SHOULD have approved (per reporting line)
--   approval_method: how the approver was determined
-- =========================================================================

ALTER TABLE approval_periods ADD COLUMN IF NOT EXISTS designated_approver_id TEXT;
ALTER TABLE approval_periods ADD COLUMN IF NOT EXISTS approval_method TEXT DEFAULT 'PRE_REPORTING_LINE'
    CHECK (approval_method IN ('DESIGNATED_MANAGER', 'ORG_SCOPE_FALLBACK', 'ACTING_MANAGER', 'PRE_REPORTING_LINE'));

DO $$
BEGIN
    INSERT INTO schema_migrations (migration_id, notes)
    VALUES ('s49-d1-approval-routing-columns', 'ADR-027 Phase 3: designated_approver_id + approval_method on approval_periods; existing rows get PRE_REPORTING_LINE default')
    ON CONFLICT (migration_id) DO NOTHING;
END
$$;

-- =========================================================================
-- S50 / ADR-027 Phase 4 — Enforcement Toggle
--   Per-tree settings: PREFERRED (default) or REQUIRED (soft enforcement).
--   explicit_fallback_confirmation tracks when a non-designated approver
--   explicitly confirmed the org-scope fallback under REQUIRED mode.
-- =========================================================================

CREATE TABLE IF NOT EXISTS reporting_line_tree_settings (
    tree_root_org_id    TEXT        PRIMARY KEY REFERENCES organizations(org_id),
    enforcement_mode    TEXT        NOT NULL DEFAULT 'PREFERRED'
                        CHECK (enforcement_mode IN ('PREFERRED', 'REQUIRED')),
    version             BIGINT      NOT NULL DEFAULT 1,
    updated_by          TEXT        NOT NULL,
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

ALTER TABLE approval_periods ADD COLUMN IF NOT EXISTS explicit_fallback_confirmation BOOLEAN DEFAULT FALSE;

DO $$
BEGIN
    INSERT INTO schema_migrations (migration_id, notes)
    VALUES ('s50-d1-enforcement-toggle', 'ADR-027 Phase 4: reporting_line_tree_settings table + explicit_fallback_confirmation on approval_periods')
    ON CONFLICT (migration_id) DO NOTHING;
END
$$;

-- =========================================================================
-- S51 — Self-Service Acting-Manager Delegation
--   scheduled_expiry: auto-close ACTING lines when delegation expires.
--   source constraint expanded: SELF_DELEGATION for manager-initiated delegation.
-- =========================================================================

ALTER TABLE reporting_lines ADD COLUMN IF NOT EXISTS scheduled_expiry DATE;
ALTER TABLE reporting_lines DROP CONSTRAINT IF EXISTS reporting_lines_source_check;
ALTER TABLE reporting_lines ADD CONSTRAINT reporting_lines_source_check
    CHECK (source IN ('MANUAL', 'HR_IMPORT', 'SELF_DELEGATION'));

DO $$
BEGIN
    INSERT INTO schema_migrations (migration_id, notes)
    VALUES ('s51-d1-delegation-schema', 'ADR-027 S51: scheduled_expiry column + SELF_DELEGATION source on reporting_lines')
    ON CONFLICT (migration_id) DO NOTHING;
END
$$;

-- ── S52 seed data for S49-S51 features ──

-- Enforcement: STY02 (Statens IT) uses REQUIRED mode — all employees have PRIMARY lines.
INSERT INTO reporting_line_tree_settings (tree_root_org_id, enforcement_mode, version, updated_by, updated_at)
VALUES ('STY02', 'REQUIRED', 1, 'SYSTEM', NOW())
ON CONFLICT DO NOTHING;

-- Self-delegation example: mgr01 (Gitte Holm) delegated to ladm01 (Christine Dahl)
-- for vacation until 2026-07-01. Creates ACTING lines for mgr01's PRIMARY reports
-- (emp002, emp005) with scheduled_expiry.
-- Note: emp002 already has a MANUAL ACTING line to ladm01 — skip (admin takes precedence).
-- emp005 gets the SELF_DELEGATION ACTING line.
INSERT INTO reporting_lines (employee_id, manager_id, tree_root_org_id, relationship, effective_from, source, created_by, scheduled_expiry)
VALUES ('emp005', 'ladm01', 'STY02', 'ACTING', '2026-06-01', 'SELF_DELEGATION', 'mgr01', '2026-07-01')
ON CONFLICT DO NOTHING;

-- =========================================================================
-- S59 / ADR-029 — Per-employee entitlement eligibility (child-sick) store.
--   6th application of the established versioned-config/dating pattern (S29
--   wage_type_mappings, S30 entitlement_configs, S31 employee_profiles,
--   S34 user_agreement_codes, S40 role_config_overrides). Surrogate UUID PK
--   + effective_from / effective_to / partial-unique "live" index / history-
--   unique index / version — matching employee_profiles (L480-501) so dated
--   reads are deterministic (ADR-019/ADR-020) and the non-overlap invariant
--   is enforced by the DB, not the application.
--
-- Eligibility is keyed by (employee_id, entitlement_type). The STORAGE is
-- generic (any entitlement_type string) per the refinement; the write API,
-- admin UI, and enforcement are restricted to CHILD_SICK this sprint
-- (SENIOR_DAY is fully age-derived via DOB and is NEVER stored here — see
-- the users.birth_date block below and ADR-029). No entitlement_type CHECK
-- constraint at the DB layer by design — the scope guard lives at the
-- endpoint (TASK-5906), mirroring how role_config_overrides keeps category
-- validation out of the schema.
--
-- Default = ineligible / absent-row = ineligible (opt-in, refinement R1):
-- the repository resolves a missing row to ineligible, so NO production
-- backfill / seed of CHILD_SICK rows is required (pre-prod reseed). The
-- actor is captured in the audit row (actor_id/actor_role), matching the
-- employee_profiles precedent which carries no created_by on the live row.
-- =========================================================================
CREATE TABLE IF NOT EXISTS employee_entitlement_eligibility (
    id                  UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    employee_id         TEXT        NOT NULL REFERENCES users(user_id),
    entitlement_type    TEXT        NOT NULL,
    eligible            BOOLEAN     NOT NULL,
    effective_from      DATE        NOT NULL DEFAULT '0001-01-01',
    effective_to        DATE        NULL,
    version             BIGINT      NOT NULL DEFAULT 1,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Partial-unique "live" index: at most one open (effective_to IS NULL) row
-- per (employee_id, entitlement_type) — the non-overlap invariant for dated
-- reads (ADR-019/020). Mirrors idx_employee_profiles_live (L494-496).
CREATE UNIQUE INDEX IF NOT EXISTS idx_employee_entitlement_eligibility_live
    ON employee_entitlement_eligibility (employee_id, entitlement_type)
    WHERE effective_to IS NULL;

-- History-unique index: at most one row per (employee_id, entitlement_type,
-- effective_from) — supports supersession INSERTs at distinct effective_from
-- values. Mirrors idx_employee_profiles_history (L500-501).
CREATE UNIQUE INDEX IF NOT EXISTS idx_employee_entitlement_eligibility_history
    ON employee_entitlement_eligibility (employee_id, entitlement_type, effective_from);

-- employee_entitlement_eligibility_audit — mirrors employee_profile_audit
-- shape (L510-522): JSONB previous_data/new_data row snapshots,
-- version_before/version_after, actor_id/actor_role, and the full 4-value
-- action CHECK up-front (S59 emits CREATED/UPDATED; SUPERSEDED/DELETED
-- reserved for the dated-history path without a future schema change). No FK
-- on the eligibility id because supersession creates FK-invalidating
-- histories (same rationale as employee_profile_audit).
CREATE TABLE IF NOT EXISTS employee_entitlement_eligibility_audit (
    audit_id        BIGSERIAL    PRIMARY KEY,
    eligibility_id  UUID         NOT NULL,
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
CREATE INDEX IF NOT EXISTS idx_employee_entitlement_eligibility_audit_eligibility_id
    ON employee_entitlement_eligibility_audit (eligibility_id);
CREATE INDEX IF NOT EXISTS idx_employee_entitlement_eligibility_audit_employee_id
    ON employee_entitlement_eligibility_audit (employee_id);

-- =========================================================================
-- S59 / ADR-029 (amends ADR-025 D3) — users.birth_date (DOB).
--   Real (no longer placeholder) GDPR-sensitive PII column on the person
--   record. Senior-day eligibility is derived = age-as-of(absence.Date) >=
--   config.MinAge(62); the Backend computes the integer age and passes it to
--   the rule engine — DOB itself never crosses the rule-engine boundary.
--   birth_date is NULLABLE (unknown DOB ⇒ fail-closed for SENIOR_DAY,
--   enforced in the Backend/rule-engine, not the schema). RBAC: never
--   exposed to non-HR / Employee payloads / JWT / export (TASK-5909).
--   Erasure (Article 17): DOB NULL-out ships WITH ADR-025 D3, NOT this
--   sprint — explicitly deferred-with-D3 (named compliance gap, ADR-029).
--
-- Audit: users_audit (L612-623) stores full-row JSONB snapshots in
-- previous_data/new_data — it does NOT use an explicit per-column list, so
-- birth_date is captured automatically with no audit-table change required
-- (the /api/admin/users write path serialises the whole row, incl.
-- birth_date, into new_data). Confirmed: no users_audit extension needed.
--
-- ADD COLUMN IF NOT EXISTS at file scope (idempotent, mirrors the S49/S50/
-- S51 ALTER precedent) carries the column onto legacy databases that the
-- base `CREATE TABLE IF NOT EXISTS users` (L456) skips.
-- =========================================================================
ALTER TABLE users ADD COLUMN IF NOT EXISTS birth_date DATE NULL;

DO $$
BEGIN
    INSERT INTO schema_migrations (migration_id, notes)
    VALUES ('s59-d1-entitlement-eligibility-and-dob', 'ADR-029: employee_entitlement_eligibility + _audit (per-employee CHILD_SICK eligibility, dated/versioned/ADR-026-audited) + users.birth_date DOB column (ADR-025 D3 amend; erasure deferred). No production seed (opt-in, absent-row=ineligible); no SENIOR_DAY eligibility row (age-derived).')
    ON CONFLICT (migration_id) DO NOTHING;
END
$$;

-- =========================================================================
-- S60 / ADR-030 — users.employment_start_date (HR-managed hire date).
--   Pro-rates monthly vacation accrual (samtidighedsferie) for mid-ferieår
--   hires: earned-to-date + still-accruable compute from
--   max(ferieårStart, employmentStart). It is an explicit, NON-dated pure
--   input to the rule engine's earnedToDate() (never read ambiently); an HR
--   correction fixes a wrong fact and re-derives uniformly across all dates
--   (NOT a versioned/bitemporal policy change — see ADR-030).
--   employment_start_date is NULLABLE: unknown hire date ⇒ full-ferieår
--   fallback (NOT fail-closed — a missing hire date must not wrongly DENY
--   already-earned vacation; opposite polarity from birth_date, which gates
--   eligibility). RBAC: never exposed to non-HR / Employee payloads / JWT /
--   export (TASK-6006/6008).
--
-- Audit: users_audit (L612-623) stores full-row JSONB snapshots in
-- previous_data/new_data — it uses NO explicit per-column list, so
-- employment_start_date is captured automatically with no audit-table change
-- required (the /api/admin/users write path serialises the whole row, incl.
-- employment_start_date, into new_data). Confirmed: no users_audit extension
-- needed (same as the S59 birth_date precedent).
--
-- ADD COLUMN IF NOT EXISTS at file scope (idempotent, mirrors the S59
-- birth_date ALTER) carries the column onto legacy databases that the base
-- `CREATE TABLE IF NOT EXISTS users` (L456) skips.
-- =========================================================================
ALTER TABLE users ADD COLUMN IF NOT EXISTS employment_start_date DATE NULL;

-- S60 / ADR-030 — activate MONTHLY_ACCRUAL on EXISTING/legacy databases too.
-- The entitlement_configs seed above uses ON CONFLICT (...) DO NOTHING, so on a
-- non-fresh DB the pre-existing IMMEDIATE VACATION/SPECIAL_HOLIDAY rows would
-- never flip and monthly accrual would activate only on fresh bootstrap (Step-7a
-- Codex BLOCKER). This idempotent UPDATE flips every IMMEDIATE VACATION/
-- SPECIAL_HOLIDAY row to MONTHLY_ACCRUAL — across all agreement codes and any
-- history rows — so the whole (entitlement_type, agreement_code, ok_version)
-- family agrees on accrual_model (preserves the ADR-021 D5 invariant; no new row).
UPDATE entitlement_configs
   SET accrual_model = 'MONTHLY_ACCRUAL'
 WHERE entitlement_type IN ('VACATION', 'SPECIAL_HOLIDAY')
   AND accrual_model = 'IMMEDIATE';

DO $$
BEGIN
    INSERT INTO schema_migrations (migration_id, notes)
    VALUES ('s60-d1-monthly-accrual-and-employment-start', 'ADR-030: activate MONTHLY_ACCRUAL for VACATION + SPECIAL_HOLIDAY 0001-01-01 sentinels across all 5 agreement codes (AC/HK/PROSA/AC_RESEARCH/AC_TEACHING) via sentinel reseed (NOT supersession — preserves ADR-021 D5 invariant); CARE_DAY/CHILD_SICK/SENIOR_DAY stay IMMEDIATE. Adds users.employment_start_date (HR-managed hire date, nullable, full-ferieår fallback; pure non-dated input to earnedToDate; users_audit JSONB snapshot captures it, no audit change). Payroll consequences (§8/§7) out of scope.')
    ON CONFLICT (migration_id) DO NOTHING;
END
$$;

-- S63 / ADR-031 — flat vacation day-count on EXISTING/legacy databases too.
-- The entitlement_configs seed above uses ON CONFLICT (...) DO NOTHING, so on a
-- non-fresh DB the pre-existing pro_rate_by_part_time=true VACATION/SPECIAL_HOLIDAY
-- rows would never flip (S60 precedent). Type-keyed (NOT agreement-code-keyed) so it
-- also covers the AC_RESEARCH/AC_TEACHING variant rows that exist ONLY in this seed
-- (DefaultEntitlementConfigs emits AC/HK/PROSA only) — and any history rows, so the
-- whole (entitlement_type, agreement_code, ok_version) family agrees (ADR-021 D5;
-- no new row). Day-count is fraction-independent per Ferieloven §5 stk.1; part-time
-- affects consumption (§6 stk.2 — deferred to S64, launch-blocking) and monetary
-- value only. Classified bug-with-no-past-impact (ADR-024 D3): pre-launch, no past
-- periods, no recompute.
UPDATE entitlement_configs
   SET pro_rate_by_part_time = false
 WHERE entitlement_type IN ('VACATION', 'SPECIAL_HOLIDAY')
   AND pro_rate_by_part_time = true;

DO $$
BEGIN
    INSERT INTO schema_migrations (migration_id, notes)
    VALUES ('s63-adr031-flat-vacation-daycount', 'ADR-031: vacation day-count is part-time-fraction-independent (Ferieloven §5) — pro_rate_by_part_time=false for all VACATION + SPECIAL_HOLIDAY rows across all 5 agreement codes (AC/HK/PROSA/AC_RESEARCH/AC_TEACHING) + both OK versions, sentinel reseed + type-keyed idempotent UPDATE (S60 pattern). Supersedes ADR-030 D8 premise; §6 stk.2 consumption deferred to S64 (launch-blocking). IMMEDIATE types unchanged.')
    ON CONFLICT (migration_id) DO NOTHING;
END
$$;

-- =========================================================================
-- S66 / ADR-032 D2 — absences_projection.feriedage on EXISTING/legacy DBs.
--   The base `CREATE TABLE IF NOT EXISTS absences_projection` (L1531) is a
--   no-op on a non-fresh DB, so the new column must be added post-hoc with
--   ADD COLUMN IF NOT EXISTS (idempotent; mirrors the S59/S60 users-column
--   ALTER precedent). feriedage is the authoritative per-absence consumption
--   record (the dated per-day-norm day-equivalent computed at booking, D1).
--   Legacy rows predate the payload field, so they are backfilled ONCE to the
--   convention in force when they were written — hours / 7.4 (flat StandardDay
--   hours), rounded 4dp — matching ProjectionBackfillService's null-payload
--   materialization so a from-events rebuild is byte-identical to the upgraded
--   table. Recorded once, never silently revalued (revaluation is D4-event-
--   driven only). The UPDATE is idempotent via `WHERE feriedage IS NULL`:
--   re-running touches no already-valued row (live post-S66 writes always
--   supply feriedage, so they are never NULL and never overwritten here).
-- =========================================================================
ALTER TABLE absences_projection ADD COLUMN IF NOT EXISTS feriedage NUMERIC(8,4);

UPDATE absences_projection
   SET feriedage = ROUND(hours / 7.4, 4)
 WHERE feriedage IS NULL;

DO $$
BEGIN
    INSERT INTO schema_migrations (migration_id, notes)
    VALUES ('s66-adr032-absences-feriedage', 'ADR-032 D2: absences_projection.feriedage NUMERIC(8,4) NULL — authoritative per-absence consumption record (dated per-day-norm day-equivalent computed at booking, D1). ADD COLUMN IF NOT EXISTS for legacy DBs + idempotent backfill of NULL rows to hours/7.4 rounded 4dp (the pre-S66 convention; matches ProjectionBackfillService null-payload materialization for replay parity). Recorded once; revaluation is D4-event-driven (EntitlementBalanceRevalued).')
    ON CONFLICT (migration_id) DO NOTHING;
END
$$;

-- =========================================================================
-- SPRINT 68 / ADR-033 — Vacation Settlement (slice 1a)
--   Greenfield schema for the year-end / termination vacation-settlement
--   identity + state machine (D5), the §21 written transfer-agreement record
--   (D8), and their two append-only audit tables (ADR-019 D8 version-transition
--   shape). No production data exists; these tables are brand-new in S68 so the
--   version-transition + paired-nullability columns are baked directly into the
--   base CREATE (no S25-style follow-up ALTER). The snapshot jsonb is opaque to
--   the DB (TASK-6802 owns its shape). FK employee_id → users(user_id) (TEXT)
--   matches every existing employee-keyed table (employee_profiles L482,
--   user_agreement_codes L547). version is the ADR-019 If-Match row-version.
-- =========================================================================

-- vacation_settlements — settlement identity + state machine (ADR-033 D5).
-- Composite PK (employee_id, entitlement_type, entitlement_year, sequence) so
-- reversal histories coexist (REVERSED predecessors stay alongside the new
-- ACTIVE sequence). The partial-unique index below enforces exactly-one
-- non-REVERSED row per (employee, type, year) — the ADR-018 D8 live-row pattern
-- (mirrors idx_employee_profiles_live / idx_user_agreement_codes_live, here
-- discriminated on settlement_state rather than effective_to IS NULL).
CREATE TABLE IF NOT EXISTS vacation_settlements (
    employee_id             TEXT          NOT NULL REFERENCES users(user_id),
    entitlement_type        TEXT          NOT NULL,
    entitlement_year        INT           NOT NULL,
    sequence                INT           NOT NULL,
    settlement_state        TEXT          NOT NULL CHECK (settlement_state IN ('PENDING_REVIEW', 'SETTLED', 'REVERSED')),
    trigger                 TEXT          NOT NULL CHECK (trigger IN ('YEAR_END', 'TERMINATION')),
    snapshot                JSONB         NOT NULL,
    transfer_days           NUMERIC(6,2)  NOT NULL DEFAULT 0,
    payout_days             NUMERIC(6,2)  NOT NULL DEFAULT 0,
    forfeit_days            NUMERIC(6,2)  NOT NULL DEFAULT 0,
    payout_reconciled_at    TIMESTAMPTZ   NULL,
    payout_reconciled_by    TEXT          NULL,
    -- review_disposition value set + state coupling live in the NAMED table
    -- constraints below (S71 slice 3b widened them; the named form is shared by
    -- the greenfield CREATE and the 's71-slice3b-termination-emission-schema'
    -- legacy block near EOF so both paths converge on identical constraint names).
    review_disposition      TEXT          NULL,
    -- S71 slice 3b (SPRINT-71 R5): the §7 modregning / waiver resolved claim
    -- quantity, recorded in its OWN column so a §7-deducted or waived termination
    -- claim never reads as §34 forfeiture (forfeit_days). Non-null exactly when
    -- review_disposition is MODREGNING/WAIVED (paired CHECK below).
    claim_disposition_days  NUMERIC(6,2)  NULL,
    -- S71 slice 3b (SPRINT-71 R3): durable bare-reversal not-due marker. TRUE only
    -- on a REVERSED row (CHECK below). The SettlementCloseService Step-B anti-join
    -- treats a tuple holding a marker row as not-due, so a bare-reversed tuple is
    -- never re-enumerated. NO 3b operation clears the marker (bare reversal is
    -- TERMINAL in 3b; marker-clearing + the g+1 revival are the REHIRE/recovery
    -- follow-up's first obligation).
    bare_reversal_not_due   BOOLEAN       NOT NULL DEFAULT FALSE,
    version                 BIGINT        NOT NULL DEFAULT 1,
    created_at              TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    updated_at              TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    PRIMARY KEY (employee_id, entitlement_type, entitlement_year, sequence),
    CONSTRAINT vacation_settlements_payout_reconciled_paired CHECK (
        (payout_reconciled_at IS NULL AND payout_reconciled_by IS NULL)
        OR (payout_reconciled_at IS NOT NULL AND payout_reconciled_by IS NOT NULL)
    ),
    -- Step-7a Codex W4 — DB-level integrity floors (defence-in-depth; the service/endpoint
    -- already clamp, but a malformed direct write must not produce a legally-impossible row).
    -- Bucket day-counts are never negative (a settlement disposes a non-negative remainder).
    CONSTRAINT vacation_settlements_nonneg_buckets CHECK (
        transfer_days >= 0 AND payout_days >= 0 AND forfeit_days >= 0
    ),
    -- sequence/version are 1-based counters (first settlement = sequence 1, version 1).
    CONSTRAINT vacation_settlements_positive_counters CHECK (sequence >= 1 AND version >= 1),
    -- Disposition value set (S71 slice 3b R5 widened the S68 FORFEIT/DEFER pair with
    -- MODREGNING — §7 stk.1 deduct-in-full — and WAIVED — waive-in-full). NAMED (not an
    -- inline column CHECK) so the legacy DROP/re-ADD path converges on the same name.
    CONSTRAINT vacation_settlements_review_disposition CHECK (
        review_disposition IS NULL
        OR review_disposition IN ('FORFEIT', 'DEFER', 'MODREGNING', 'WAIVED')
    ),
    -- State/disposition coupling: a DEFER outcome (suspected §22-feriehindring) leaves the row
    -- PENDING_REVIEW until slice 4 models the impediment — BUT a DEFER-marked row may be
    -- REVERSED with its DEFER history PRESERVED (S71 slice 3b R5: reversal never destroys the
    -- marker), so DEFER admits PENDING_REVIEW or REVERSED while DEFER+SETTLED stays impossible.
    -- FORFEIT/MODREGNING/WAIVED each RESOLVED the review, so none can coexist with
    -- PENDING_REVIEW (they ride SETTLED, and survive on REVERSED rows for history).
    CONSTRAINT vacation_settlements_disposition_state CHECK (
        review_disposition IS NULL
        OR (review_disposition = 'DEFER' AND settlement_state IN ('PENDING_REVIEW', 'REVERSED'))
        OR (review_disposition IN ('FORFEIT', 'MODREGNING', 'WAIVED') AND settlement_state <> 'PENDING_REVIEW')
    ),
    -- S71 slice 3b (R3): the bare-reversal marker is meaningful only on a REVERSED row.
    CONSTRAINT vacation_settlements_bare_reversal_reversed_only CHECK (
        bare_reversal_not_due = FALSE OR settlement_state = 'REVERSED'
    ),
    -- S71 slice 3b (R5): the resolved claim quantity is a non-negative day-count …
    CONSTRAINT vacation_settlements_claim_disposition_nonneg CHECK (
        claim_disposition_days IS NULL OR claim_disposition_days >= 0
    ),
    -- … and is recorded EXACTLY when the disposition is MODREGNING/WAIVED (bidirectional:
    -- a §7/waived claim without its quantity would lose the legal record — the quantity
    -- must live HERE, never in forfeit_days; and a quantity without a claim disposition
    -- is meaningless). The IS NOT NULL guard keeps the comparison NULL-safe.
    CONSTRAINT vacation_settlements_claim_disposition_paired CHECK (
        (claim_disposition_days IS NOT NULL)
        = (review_disposition IS NOT NULL AND review_disposition IN ('MODREGNING', 'WAIVED'))
    )
);

-- Partial-unique-index: at most one ACTIVE (non-REVERSED) settlement per
-- (employee, type, year). ADR-018 D8 single-active live-row pattern; REVERSED
-- rows are excluded so a reversal can be superseded by a fresh sequence.
CREATE UNIQUE INDEX IF NOT EXISTS idx_vacation_settlements_active
    ON vacation_settlements (employee_id, entitlement_type, entitlement_year)
    WHERE settlement_state <> 'REVERSED';

CREATE INDEX IF NOT EXISTS idx_vacation_settlements_employee
    ON vacation_settlements (employee_id);

-- vacation_transfer_agreements — the §21 stk.2 written transfer-agreement
-- record (ADR-033 D8). One agreement per (employee, year, type). The §21 stk.2
-- 31-Dec deadline is enforced in the ENDPOINT via a business-clock comparison,
-- NOT a DB CHECK (agreement_date is recorded as-stated). recorded_by is the HR
-- actor (TEXT = users.user_id, not a declared FK — parallels the actor_id audit columns).
CREATE TABLE IF NOT EXISTS vacation_transfer_agreements (
    employee_id         TEXT          NOT NULL REFERENCES users(user_id),
    entitlement_year    INT           NOT NULL,
    entitlement_type    TEXT          NOT NULL,
    transfer_days       NUMERIC(6,2)  NOT NULL CHECK (transfer_days >= 0),
    agreement_date      DATE          NOT NULL,
    recorded_by         TEXT          NOT NULL,
    version             BIGINT        NOT NULL DEFAULT 1,
    created_at          TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    PRIMARY KEY (employee_id, entitlement_year, entitlement_type)
);

CREATE INDEX IF NOT EXISTS idx_vacation_transfer_agreements_employee
    ON vacation_transfer_agreements (employee_id);

-- vacation_settlement_audit — append-only audit for vacation_settlements.
-- Mirrors entitlement_config_audit (L1382) / user_agreement_codes_audit (L573):
-- BIGSERIAL audit_id PK, action CHECK enum (all 4 values up-front for forward-
-- compat — S68 emits CREATED/UPDATED; REVERSED maps to UPDATED, DELETED reserved),
-- previous_data/new_data JSONB, version_before/version_after (ADR-019 D8 version-
-- transition columns), actor_id/actor_role, audit_at (S34-era column name). No FK
-- on the settlement key because reversal histories are FK-stable but the audit
-- stream must survive any future row deletion (precedent: audit tables carry no FK).
CREATE TABLE IF NOT EXISTS vacation_settlement_audit (
    audit_id            BIGSERIAL     PRIMARY KEY,
    employee_id         TEXT          NOT NULL,
    entitlement_type    TEXT          NOT NULL,
    entitlement_year    INT           NOT NULL,
    sequence            INT           NOT NULL,
    action              TEXT          NOT NULL CHECK (action IN ('CREATED', 'UPDATED', 'DELETED', 'SUPERSEDED')),
    previous_data       JSONB         NULL,
    new_data            JSONB         NULL,
    version_before      BIGINT        NULL,
    version_after       BIGINT        NULL,
    actor_id            TEXT          NOT NULL,
    actor_role          TEXT          NOT NULL,
    audit_at            TIMESTAMPTZ   NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_vacation_settlement_audit_employee
    ON vacation_settlement_audit (employee_id);
CREATE INDEX IF NOT EXISTS idx_vacation_settlement_audit_at
    ON vacation_settlement_audit (audit_at);

-- vacation_transfer_agreement_audit — append-only audit for
-- vacation_transfer_agreements. Same convention as vacation_settlement_audit.
CREATE TABLE IF NOT EXISTS vacation_transfer_agreement_audit (
    audit_id            BIGSERIAL     PRIMARY KEY,
    employee_id         TEXT          NOT NULL,
    entitlement_year    INT           NOT NULL,
    entitlement_type    TEXT          NOT NULL,
    action              TEXT          NOT NULL CHECK (action IN ('CREATED', 'UPDATED', 'DELETED', 'SUPERSEDED')),
    previous_data       JSONB         NULL,
    new_data            JSONB         NULL,
    version_before      BIGINT        NULL,
    version_after       BIGINT        NULL,
    actor_id            TEXT          NOT NULL,
    actor_role          TEXT          NOT NULL,
    audit_at            TIMESTAMPTZ   NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_vacation_transfer_agreement_audit_employee
    ON vacation_transfer_agreement_audit (employee_id);
CREATE INDEX IF NOT EXISTS idx_vacation_transfer_agreement_audit_at
    ON vacation_transfer_agreement_audit (audit_at);

-- schema_migrations ledger entry — documentary for S68 greenfield-only, forward-
-- compat marker if init.sql ever runs against an older database.
DO $$
BEGIN
    INSERT INTO schema_migrations (migration_id, notes)
    VALUES ('s68-adr033-vacation-settlement', 'ADR-033 D5/D8: vacation_settlements (composite PK (employee_id, entitlement_type, entitlement_year, sequence) + partial-unique-active index WHERE settlement_state <> REVERSED — ADR-018 D8 single-active live-row pattern; settlement_state/trigger/review_disposition CHECK enums; per-bucket transfer/payout/forfeit_days; paired-nullable payout_reconciled_at/by CHECK; ADR-019 If-Match version) + vacation_transfer_agreements (§21 written record, PK (employee_id, entitlement_year, entitlement_type); 31-Dec deadline enforced in endpoint not DB) + two append-only audit tables (ADR-019 D8 version-transition columns, mirror entitlement_config_audit/user_agreement_codes_audit). Greenfield only — no production data.')
    ON CONFLICT (migration_id) DO NOTHING;
END
$$;

-- =========================================================================
-- SPRINT 69 / ADR-033 — Vacation Settlement §24 Payroll staging (slice 1b)
--   Greenfield schema for the durable §24 year-end auto-payout staging
--   pipeline. The S69 Payroll emitter CONSUMES the slice-1a VacationAutoPaidOut
--   event and, in ONE transaction, writes (1) a consumer inbox/checkpoint row —
--   the authoritative exactly-once dedup keyed by the source event_id (ADR-033
--   D4 consumer-checkpoint) — and (2) one immutable, money-free staged export
--   line (ADR-033 D5 sequence/bucket; D7 the line carries the wage-type natural-
--   key components ok_version/agreement_code/position). The line is MONEY-FREE:
--   hours holds the §24 day-count (PayoutDays) and amount is pinned to 0 at the
--   schema level (ADR-033 D1 — SLS owns ALL kroner incl. the rate). These tables
--   are brand-new in S69 so CREATE TABLE IF NOT EXISTS is legacy-safe (the table
--   does not exist on a pre-existing DB → it is created on re-apply); no S25-style
--   follow-up ALTER guard is needed (that case applies only to columns/CHECKs on
--   an EXISTING table). FK employee_id → users(user_id) (TEXT) matches every
--   existing employee-keyed table. source_event_id references events.event_id
--   (L23 event_id UUID NOT NULL UNIQUE) CONCEPTUALLY — the consumed
--   VacationAutoPaidOut — but carries no declared FK (the event store is a
--   separate bounded context / cross-process boundary; parallels the audit
--   actor_id columns). Delivery is staged-only this sprint: NO mutable
--   delivery_status column (Step-0b W2 — outbound delivery state is a future
--   slice's separate concern; the row's existence == "staged").
-- =========================================================================

-- settlement_payroll_inbox — consumer inbox/checkpoint (ADR-033 D4). The
-- AUTHORITATIVE exactly-once dedup: PK = (source_event_id, bucket) — S71 slice 3b
-- (SPRINT-71 R7) widened the S69 single-column source_event_id PK so ONE consumed
-- event may legally checkpoint MULTIPLE buckets (the SettlementReversed consumer
-- compensates every staged bucket of the reversed settlement row, one checkpoint
-- per (event, bucket), all written atomically in ONE tx — no partial subset can
-- commit). A redelivered event collides per bucket and the consumer is a no-op.
-- processing_status drives a retry lifecycle: RETRY_PENDING is the ONLY
-- non-terminal status; the lifecycle is RETRY_PENDING -> {PROCESSED |
-- SKIPPED_RECONCILED | SKIPPED_VOIDED | DEAD_LETTER} and writes move MONOTONICALLY
-- toward a terminal status (a terminal row is never reopened — ADR-033 D4 /
-- Sprint-69 Step-0b C2-B1/C3-B1/C4-B1; SPRINT-71 R7 extends the monotonic rule
-- per bucket AND at event level). SKIPPED_RECONCILED is the reconcile XOR claim
-- outcome (the operator marked payout_reconciled_at before the emitter claimed
-- the bucket → no line is staged). SKIPPED_VOIDED (S71/R6/R9) records the
-- under-lock active-settlement re-check outcome: the settlement row was REVERSED
-- (or the §26 request VOIDED) before this consumer staged → no line, terminal.
-- DEAD_LETTER parks an exhausted-retry/poison/collision row for operator
-- inspection (last_error carries the cause).
--
-- The '_EVENT' sentinel bucket (SPRINT-71 R7): EVENT-level rows that are not
-- bucket-scoped key at bucket = '_EVENT'. A TERMINAL '_EVENT' row (DEAD_LETTER —
-- poison/collision/retry-budget) is mutually exclusive with real-bucket
-- checkpoints and suppresses ALL subsequent processing of that source event
-- (bucket-keyed status reads MUST treat it as covering every bucket); transient
-- failure diagnostics (RETRY_PENDING attempts/last_error) ALSO key at '_EVENT'
-- (non-terminal) and are PROMOTED to the terminal status when the event later
-- completes (the event-level monotonic completion). A SettlementReversed event
-- with nothing to compensate writes a terminal '_EVENT' PROCESSED no-op
-- checkpoint.
--
-- The settlement-identity columns (employee_id, entitlement_type, entitlement_year,
-- sequence) are NULLABLE — ONLY to permit a poison/parse-failure DEAD_LETTER
-- row. When EventSerializer.Deserialize of a settlement event payload throws, the
-- event has NO recoverable identity, so the emitter dead-letters it at
-- (source_event_id, '_EVENT') (status DEAD_LETTER + last_error; identity columns
-- NULL) to mark it terminal and stop the poll from re-selecting it forever (S69
-- Step-7a FIX 1; bucket is NOT NULL since S71 — the poison row carries the
-- '_EVENT' sentinel instead of NULL). EVERY normal (claim/skip/retry) inbox write
-- still populates all four identity columns; the nullable employee_id FK is
-- enforced on non-null values only. The `sequence` column carries the line's
-- EXPORT sequence (SPRINT-71 R1/R2: original lines = the odd settlement-row
-- sequence 2g−1; compensating reversal lines = the even export sequence 2g).
CREATE TABLE IF NOT EXISTS settlement_payroll_inbox (
    source_event_id     UUID          NOT NULL,      -- events.event_id of the consumed settlement event; (source_event_id, bucket) is the authoritative consumer dedup
    employee_id         TEXT          NULL REFERENCES users(user_id),   -- NULL only on a poison/parse-failure DEAD_LETTER row (no recoverable identity)
    entitlement_type    TEXT          NULL,          -- NULL only on a poison DEAD_LETTER row
    entitlement_year    INT           NULL,          -- NULL only on a poison DEAD_LETTER row
    sequence            INT           NULL,          -- the EXPORT sequence (R1/R2); NULL only on a poison DEAD_LETTER row
    bucket              TEXT          NOT NULL,      -- 'AUTO_PAYOUT_24' / 'TERMINATION_PAYOUT_26' for bucket checkpoints; '_EVENT' for event-level rows (poison, diagnostics, no-op)
    processing_status   TEXT          NOT NULL,
    attempts            INT           NOT NULL DEFAULT 0 CHECK (attempts >= 0),
    last_error          TEXT          NULL,
    processed_at        TIMESTAMPTZ   NULL,
    created_at          TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    CONSTRAINT settlement_payroll_inbox_pkey PRIMARY KEY (source_event_id, bucket),
    CONSTRAINT settlement_payroll_inbox_processing_status CHECK (
        processing_status IN ('RETRY_PENDING', 'PROCESSED', 'SKIPPED_RECONCILED', 'SKIPPED_VOIDED', 'DEAD_LETTER')
    )
);

-- Retry-selection driver: the poll scans only RETRY_PENDING rows (the single
-- non-terminal status), so a partial index keeps the working set hot and small.
CREATE INDEX IF NOT EXISTS idx_settlement_payroll_inbox_retry_pending
    ON settlement_payroll_inbox (processing_status)
    WHERE processing_status = 'RETRY_PENDING';

-- Settlement-bucket lookup (correlate an inbox row back to its settlement key).
CREATE INDEX IF NOT EXISTS idx_settlement_payroll_inbox_settlement
    ON settlement_payroll_inbox (employee_id, entitlement_type, entitlement_year, sequence, bucket);

-- settlement_export_lines — the durable, IMMUTABLE, money-free staged §24 payout
-- line (ADR-033 D5/D7; B5). One line per settlement bucket: the UNIQUE business
-- key (employee_id, entitlement_type, entitlement_year, sequence, bucket) is the
-- authoritative LINE dedup (the inbox PK dedups the consumed EVENT; this UNIQUE
-- dedups the staged LINE — both guards are load-bearing). wage_type is the
-- resolved sentinel lønart (SLS_TBD_S24 this sprint — dated ADR-020 config data,
-- swappable later). hours holds the §24 day-count (PayoutDays); amount is pinned
-- to 0 by CHECK — the MONEY-FREE invariant (ADR-033 D1; SLS owns the rate). The
-- line carries the full wage-type natural-key components ok_version/agreement_code/
-- position (ADR-033 D7) captured in the slice-1a snapshot so replay is
-- deterministic (no live mapping lookup). source_event_id is the originating
-- VacationAutoPaidOut event_id (collision verification, C2-B2). The row is
-- IMMUTABLE and staged-only this sprint — its existence == "staged"; there is
-- deliberately NO mutable delivery_status column (Step-0b W2).
CREATE TABLE IF NOT EXISTS settlement_export_lines (
    line_id             BIGSERIAL     PRIMARY KEY,
    employee_id         TEXT          NOT NULL REFERENCES users(user_id),
    entitlement_type    TEXT          NOT NULL,
    entitlement_year    INT           NOT NULL,
    sequence            INT           NOT NULL,
    bucket              TEXT          NOT NULL,       -- 'AUTO_PAYOUT_24'
    wage_type           TEXT          NOT NULL,       -- the resolved sentinel lønart (SLS_TBD_S24 this sprint)
    hours               NUMERIC(8,2)  NOT NULL CHECK (hours >= 0),                    -- the §24 day-count (PayoutDays); the line is money-free
    amount              NUMERIC(12,2) NOT NULL DEFAULT 0 CHECK (amount = 0),          -- money-free pinned at schema level (ADR-033 D1; SLS owns kroner)
    ok_version          TEXT          NOT NULL,
    agreement_code      TEXT          NOT NULL,
    position            TEXT          NOT NULL DEFAULT '',
    period_start        DATE          NOT NULL,
    period_end          DATE          NOT NULL,
    source_event_id     UUID          NOT NULL,       -- the originating settlement event_id (collision verification, C2-B2)
    -- S71 slice 3b (SPRINT-71 R8): reversal-line discrimination. A REVERSAL line is the
    -- compensating entry for exactly one earlier line — it copies the compensated line's
    -- mapping/period/quantity, points at it via reverses_line_id (the source event id
    -- identifies the reversal EVENT, not the original LINE — the FK is the unambiguous
    -- reference) and uses the R1 even export sequence. hours stays >= 0: direction is
    -- line_kind + SLS-side semantics, NEVER a negative quantity. Pre-S71 rows backfill
    -- line_kind='ORIGINAL' via the DEFAULT (legacy path: the
    -- 's71-slice3b-termination-emission-schema' block near EOF). Originals are never
    -- mutated or deleted (R9/P3) — reversal is purely additive.
    line_kind           TEXT          NOT NULL DEFAULT 'ORIGINAL',
    reverses_line_id    BIGINT        NULL,
    created_at          TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    created_by          TEXT          NOT NULL,       -- the emitter identity
    CONSTRAINT settlement_export_lines_line_kind CHECK (
        line_kind IN ('ORIGINAL', 'REVERSAL')
    ),
    -- A line carries a reverses pointer IFF it is a REVERSAL line (line_kind is NOT NULL,
    -- so the equality is never NULL-ambiguous on the right side).
    CONSTRAINT settlement_export_lines_reversal_pairing CHECK (
        (reverses_line_id IS NOT NULL) = (line_kind = 'REVERSAL')
    ),
    CONSTRAINT settlement_export_lines_reverses_line_fk
        FOREIGN KEY (reverses_line_id) REFERENCES settlement_export_lines (line_id)
);

-- Authoritative LINE dedup: at most one §24 line per settlement bucket.
CREATE UNIQUE INDEX IF NOT EXISTS idx_settlement_export_lines_bucket
    ON settlement_export_lines (employee_id, entitlement_type, entitlement_year, sequence, bucket);

CREATE INDEX IF NOT EXISTS idx_settlement_export_lines_employee
    ON settlement_export_lines (employee_id);

-- schema_migrations ledger entry — documentary for S69 greenfield-only, forward-
-- compat marker if init.sql ever runs against an older database.
DO $$
BEGIN
    INSERT INTO schema_migrations (migration_id, notes)
    VALUES ('s69-adr033-slice1b-payroll-staging', 'ADR-033 D4/D5/D7: settlement_payroll_inbox (consumer inbox/checkpoint, PK source_event_id = events.event_id of the consumed VacationAutoPaidOut — authoritative exactly-once dedup; processing_status CHECK {RETRY_PENDING, PROCESSED, SKIPPED_RECONCILED, DEAD_LETTER} with RETRY_PENDING the sole non-terminal status and writes monotonic toward terminal; the settlement-identity columns employee_id/entitlement_type/entitlement_year/sequence/bucket are NULLABLE ONLY to permit a poison/parse-failure DEAD_LETTER row keyed solely by source_event_id, S69 Step-7a FIX 1 — normal rows populate all five; attempts>=0; partial index on processing_status WHERE RETRY_PENDING) + settlement_export_lines (immutable money-free staged §24 payout line; UNIQUE business key (employee_id, entitlement_type, entitlement_year, sequence, bucket) = authoritative line dedup; hours holds the day-count (PayoutDays); amount pinned to 0 by CHECK = money-free invariant (ADR-033 D1, SLS owns the rate); carries wage_type + the wage-type natural-key components ok_version/agreement_code/position (D7); NO mutable delivery_status column — staged-only, Step-0b W2). Greenfield only — no production data.')
    ON CONFLICT (migration_id) DO NOTHING;
END
$$;

-- =========================================================================
-- S69 / TASK-6903 — §24 vacation-settlement auto-payout wage-type mapping
--   (ADR-033 slice 1b). The settlement_export_lines.wage_type for an §24
--   AUTO_PAYOUT_24 bucket resolves through wage_type_mappings on the new
--   time_type VACATION_SETTLEMENT_PAYOUT. This sprint the resolved lønart is a
--   PLACEHOLDER sentinel (SLS_TBD_S24) — the real SLS code AND the line format
--   are UNVERIFIED, deferred to a future SLS-dialogue task. The outbound
--   delivery guard (sibling task) fail-closed REFUSES to deliver any line whose
--   wage_type is the sentinel, so a wrong real payout cannot escape (one of the
--   triple-locks; the others are the D13 go-live gate + delivery disabled this
--   sprint). Swapping the real code in later is a one-row ADR-020 effective-dated
--   change (a new open row supersedes this one) — NOT a rebuild (owner insight).
--
-- ADR-020 versioned natural key (time_type, ok_version, agreement_code, position):
--   position = '' (national; ADR-033 D12 — §24 settlement is GLOBAL, does not
--   vary by institution; the natural key still records the position axis).
--   effective_from = '2020-01-01' + effective_to NULL (open row) mirrors every
--   existing wage_type_mappings seed (the partial-unique-open-row form).
--
-- Coverage MUST mirror the existing VACATION → SLS_0510 (agreement_code,
--   ok_version) set EXACTLY so §24 auto-payout resolves for every agreement/OK a
--   vacation employee can be under — that set is 10 pairs: {AC, HK, PROSA,
--   AC_RESEARCH, AC_TEACHING} × {OK24, OK26} (AC_RESEARCH/AC_TEACHING are the
--   AC sub-position agreement_codes seeded for academic variants, L1031-1097).
--
-- schema_migrations-guarded + idempotent (NOT a bare INSERT — a bare INSERT
--   would dupe/error on a legacy DB re-apply; Step-0b W3). The guard short-
--   circuits on re-apply; the inner ON CONFLICT DO NOTHING is the belt-and-
--   suspenders the other seeds use. Greenfield + legacy converge identically.
-- =========================================================================
DO $$
BEGIN
    INSERT INTO schema_migrations (migration_id, notes)
    VALUES ('s69-s24-settlement-wage-type', 'ADR-033 slice 1b — §24 VACATION_SETTLEMENT_PAYOUT -> placeholder SLS_TBD_S24, ADR-020 versioned (effective-dated open row), position '''' national (D12 settlement GLOBAL), covering the full existing-VACATION agreement/OK matrix {AC,HK,PROSA,AC_RESEARCH,AC_TEACHING}x{OK24,OK26}; sentinel refused by the outbound delivery guard; real SLS code+line format deferred to SLS dialogue (D1 money-free, the line carries a day-count).')
    ON CONFLICT (migration_id) DO NOTHING;

    IF NOT FOUND THEN
        RETURN;
    END IF;

    INSERT INTO wage_type_mappings (time_type, wage_type, ok_version, agreement_code, position, description, effective_from) VALUES
        ('VACATION_SETTLEMENT_PAYOUT', 'SLS_TBD_S24', 'OK24', 'AC',          '', 'PLACEHOLDER - §24 vacation auto-payout (Ferielov §24); real SLS code TBD via SLS dialogue', '2020-01-01'),
        ('VACATION_SETTLEMENT_PAYOUT', 'SLS_TBD_S24', 'OK24', 'HK',          '', 'PLACEHOLDER - §24 vacation auto-payout (Ferielov §24); real SLS code TBD via SLS dialogue', '2020-01-01'),
        ('VACATION_SETTLEMENT_PAYOUT', 'SLS_TBD_S24', 'OK24', 'PROSA',       '', 'PLACEHOLDER - §24 vacation auto-payout (Ferielov §24); real SLS code TBD via SLS dialogue', '2020-01-01'),
        ('VACATION_SETTLEMENT_PAYOUT', 'SLS_TBD_S24', 'OK24', 'AC_RESEARCH', '', 'PLACEHOLDER - §24 vacation auto-payout (Ferielov §24); real SLS code TBD via SLS dialogue', '2020-01-01'),
        ('VACATION_SETTLEMENT_PAYOUT', 'SLS_TBD_S24', 'OK24', 'AC_TEACHING', '', 'PLACEHOLDER - §24 vacation auto-payout (Ferielov §24); real SLS code TBD via SLS dialogue', '2020-01-01'),
        ('VACATION_SETTLEMENT_PAYOUT', 'SLS_TBD_S24', 'OK26', 'AC',          '', 'PLACEHOLDER - §24 vacation auto-payout (Ferielov §24); real SLS code TBD via SLS dialogue', '2020-01-01'),
        ('VACATION_SETTLEMENT_PAYOUT', 'SLS_TBD_S24', 'OK26', 'HK',          '', 'PLACEHOLDER - §24 vacation auto-payout (Ferielov §24); real SLS code TBD via SLS dialogue', '2020-01-01'),
        ('VACATION_SETTLEMENT_PAYOUT', 'SLS_TBD_S24', 'OK26', 'PROSA',       '', 'PLACEHOLDER - §24 vacation auto-payout (Ferielov §24); real SLS code TBD via SLS dialogue', '2020-01-01'),
        ('VACATION_SETTLEMENT_PAYOUT', 'SLS_TBD_S24', 'OK26', 'AC_RESEARCH', '', 'PLACEHOLDER - §24 vacation auto-payout (Ferielov §24); real SLS code TBD via SLS dialogue', '2020-01-01'),
        ('VACATION_SETTLEMENT_PAYOUT', 'SLS_TBD_S24', 'OK26', 'AC_TEACHING', '', 'PLACEHOLDER - §24 vacation auto-payout (Ferielov §24); real SLS code TBD via SLS dialogue', '2020-01-01')
    ON CONFLICT (time_type, ok_version, agreement_code, position, effective_from) DO NOTHING;
END
$$;

-- =========================================================================
-- S70 / ADR-033 slice 3a — users.employment_end_date + users.end_date_deactivated
--   (the leaver model; SPRINT-70 TASK-7000, pinned rules R1 + R11).
--
-- employment_end_date (DATE NULL) — HR-managed end-of-employment date.
--   NULL = employed / no end date set. Semantics: the LAST day employed — the
--   deactivation lifecycle flips is_active=false when the Copenhagen business
--   date > employment_end_date (R1/R2; enforced in the Backend lifecycle +
--   SettlementCloseService Step A, never in the schema). Like
--   employment_start_date (S60/ADR-030) it is an explicit, NON-dated pure
--   input (never read ambiently); an HR correction fixes a wrong fact (NOT a
--   versioned/bitemporal policy change). RBAC: never exposed in JWT /
--   Employee payloads / export. GDPR (Article 17): erasure deferred WITH
--   ADR-025 D3 (the S59 birth_date / S60 employment_start_date precedent
--   annotation; D3 Part B not yet built — field joins the D3 erasure column
--   set, recorded at sprint close).
--
-- end_date_deactivated (BOOLEAN NOT NULL DEFAULT FALSE) — deactivation
--   PROVENANCE flag (SPRINT-70 R1). This column exists because is_active has
--   TWO writers: the end-date lifecycle AND the pre-existing admin soft-delete
--   (AdminEndpoints user PUT). Set TRUE only by the end-date lifecycle when IT
--   flips is_active=false (same-tx past-dated set, or the Step-A poller flip);
--   the admin soft-delete second writer NEVER touches it. Clearing the end
--   date reactivates ONLY when this flag is TRUE (then resets it to FALSE) —
--   clearing an end date on a manually-deactivated user clears the date but
--   does NOT reactivate (R1c/R1d).
--
-- Audit: users_audit (L617-628) stores full-row JSONB snapshots in
-- previous_data/new_data — it uses NO explicit per-column list, so both new
-- columns are captured automatically with no audit-table change required
-- (same as the S59 birth_date / S60 employment_start_date precedent).
--
-- The base `CREATE TABLE IF NOT EXISTS users` (L456) bakes both columns for
-- greenfield databases but is a no-op on a legacy DB (the S68 lesson) — this
-- schema_migrations-guarded ALTER block (s68-vacation-reset-month-check guard
-- pattern) is what upgrades pre-existing databases. ADD COLUMN IF NOT EXISTS
-- keeps the ALTERs themselves idempotent (belt-and-suspenders on top of the
-- ledger guard). No CHECK constraints, no indexes (none pinned by the plan).
-- =========================================================================
DO $$
BEGIN
    INSERT INTO schema_migrations (migration_id, notes)
    VALUES ('s70-employment-end-date', 'ADR-033 slice 3a (SPRINT-70 R1/R11): users.employment_end_date DATE NULL (HR-managed; LAST day employed; lifecycle deactivates when Copenhagen business date > end date; non-dated pure input; never in JWT/Employee payloads/export; erasure deferred WITH ADR-025 D3) + users.end_date_deactivated BOOLEAN NOT NULL DEFAULT FALSE (deactivation provenance — is_active has two writers; set only by the end-date lifecycle; clear-end-date reactivates only when TRUE). users_audit full-row JSONB captures both, no audit change.')
    ON CONFLICT (migration_id) DO NOTHING;

    IF NOT FOUND THEN
        RETURN;
    END IF;

    ALTER TABLE users ADD COLUMN IF NOT EXISTS employment_end_date DATE NULL;

    ALTER TABLE users ADD COLUMN IF NOT EXISTS end_date_deactivated BOOLEAN NOT NULL DEFAULT FALSE;
END
$$;

-- =========================================================================
-- SPRINT 71 / ADR-033 slice 3b — TERMINATION payroll emission + reversal
--   infrastructure schema (SPRINT-71 TASK-7100; pinned rules R3/R5/R6/R8).
--
-- Four schema units:
--   (1) termination_payout_requests — the §26 anmodning record (R6). Ferieloven
--       §26 stk.1 pays "efter anmodning": the REQUEST, not the settlement,
--       drives the staged SLS_TBD_S26 line. Keyed to the EXACT settlement row
--       via a composite FK onto vacation_settlements' PK (employee_id,
--       entitlement_type, entitlement_year, sequence) — never the bare year
--       tuple (R2: requests/CAS key on the SETTLEMENT sequence; consumers
--       derive the EXPORT sequence per R1). Lifecycle OPEN -> LINE_STAGED ->
--       VOIDED_BY_REVERSAL (D-D: no external-payment variant in 3b; D-E:
--       reversal VOIDs the open request in the same tx and HR re-records
--       against the new settlement row). The partial-unique index pins ONE
--       non-voided request per settlement row. Brand-new table, so the
--       top-level CREATE TABLE IF NOT EXISTS is legacy-safe by construction
--       (the S69 settlement_payroll_inbox precedent) — only the EXISTING-table
--       changes ride the guarded DO block below.
--   (2) vacation_settlements.bare_reversal_not_due (R3) — durable not-due
--       marker for a bare reversal (reverse WITHOUT a superseding settlement).
--       CHECK: TRUE only on REVERSED rows. The Step-B enumeration anti-join
--       treats a marker-holding tuple as not-due, so a bare-reversed tuple is
--       never re-enumerated; nothing in 3b clears the marker (terminal — the
--       REHIRE/recovery follow-up owns marker-clearing + the R1 g+1 revival).
--   (3) vacation_settlements review-disposition widening (R5) — MODREGNING
--       (§7 stk.1 deduct-in-full) and WAIVED (waive-in-full) join FORFEIT/DEFER;
--       the disposition/state coupling now lets a DEFER-marked row be REVERSED
--       with its DEFER history preserved; claim_disposition_days records the
--       §7/waiver resolved quantity in its own column (never in forfeit_days —
--       a §7/waived claim must never read as §34 forfeiture).
--   (4) settlement_export_lines reversal shape (R8) — immutable
--       line_kind ORIGINAL/REVERSAL (existing rows legacy-backfill to ORIGINAL
--       via the DEFAULT) + reverses_line_id self-FK, paired by CHECK
--       (reverses_line_id IS NOT NULL ⟺ line_kind = 'REVERSAL').
--
-- All four are baked into the base CREATEs above for greenfield databases; the
-- schema_migrations-guarded DO block below is what upgrades a pre-S71 database
-- (the S68 reset-month-check / S70 employment-end-date pattern, 3-path
-- idempotent: fresh inline DDL authoritative; guarded ALTERs cover legacy;
-- re-run is a ledger-guarded no-op). CHECK changes use DROP-IF-EXISTS +
-- re-ADD with PINNED constraint names so greenfield and legacy converge on
-- identical names; every widened CHECK is a strict superset of its
-- predecessor, so the re-ADD validates legacy rows without remediation.
--
-- The S71-SLICE3B-SEGMENT markers below are extracted VERBATIM by
-- Slice3bLegacyMigrationTests (the migration-idempotence harness runs this
-- exact segment against a reconstructed pre-S71 schema, twice) — keep the
-- marker lines intact and keep all S71 DDL between them.
-- =========================================================================
-- S71-SLICE3B-SEGMENT-BEGIN

-- termination_payout_requests — the §26 payout-request record (R6/R2).
-- request_id is a surrogate PK (BIGSERIAL, the settlement_export_lines.line_id
-- convention) because VOIDED history rows coexist with a later non-voided
-- request for the same settlement row — uniqueness of the LIVE request is the
-- partial-unique index below, not the PK. employee_id additionally FKs
-- users(user_id) directly (every employee-keyed table's convention) on top of
-- the composite settlement FK. request_date is the as-stated HR-recorded
-- anmodning date (DATE, the vacation_transfer_agreements.agreement_date
-- evidence convention — created_at records insertion time); recorded_by is the
-- HR actor (TEXT = users.user_id, not a declared FK — parallels the audit
-- actor_id columns); evidence_note is free-text request evidence. version is
-- the ADR-019 If-Match/CAS row-version. state has NO default — every writer
-- states the lifecycle state explicitly.
CREATE TABLE IF NOT EXISTS termination_payout_requests (
    request_id              BIGSERIAL     PRIMARY KEY,
    employee_id             TEXT          NOT NULL REFERENCES users(user_id),
    entitlement_type        TEXT          NOT NULL,
    entitlement_year        INT           NOT NULL,
    settlement_sequence     INT           NOT NULL,
    state                   TEXT          NOT NULL,
    request_date            DATE          NOT NULL,
    recorded_by             TEXT          NOT NULL,
    evidence_note           TEXT          NULL,
    version                 BIGINT        NOT NULL DEFAULT 1,
    created_at              TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    updated_at              TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    CONSTRAINT termination_payout_requests_state CHECK (
        state IN ('OPEN', 'LINE_STAGED', 'VOIDED_BY_REVERSAL')
    ),
    CONSTRAINT termination_payout_requests_positive_version CHECK (version >= 1),
    CONSTRAINT termination_payout_requests_settlement_fk
        FOREIGN KEY (employee_id, entitlement_type, entitlement_year, settlement_sequence)
        REFERENCES vacation_settlements (employee_id, entitlement_type, entitlement_year, sequence)
);

-- ONE non-voided request per settlement row (R6). VOIDED_BY_REVERSAL rows are
-- excluded so HR can re-record after a reversal (D-E) while the voided history
-- stays on the table.
CREATE UNIQUE INDEX IF NOT EXISTS idx_termination_payout_requests_nonvoided
    ON termination_payout_requests (employee_id, entitlement_type, entitlement_year, settlement_sequence)
    WHERE state <> 'VOIDED_BY_REVERSAL';

CREATE INDEX IF NOT EXISTS idx_termination_payout_requests_employee
    ON termination_payout_requests (employee_id);

-- schema_migrations-guarded legacy upgrade block (the s68-vacation-reset-month-check
-- / s70-employment-end-date guard pattern). Ordering discipline (SPRINT-71 W1):
-- additive defaulted/nullable COLUMNS land first (existing rows stay valid), CHECKs
-- land after their columns, and every widened CHECK admits every row its predecessor
-- admitted — no intermediate state violates a constraint and no remediation UPDATE
-- is needed. On a greenfield DB this block still runs once (first apply inserts the
-- ledger row): the ADD COLUMN IF NOT EXISTS calls no-op and the DROP/re-ADD
-- constraint pairs recreate the identical baked-in constraints — convergent.
DO $$
BEGIN
    INSERT INTO schema_migrations (migration_id, notes)
    VALUES ('s71-slice3b-termination-emission-schema', 'ADR-033 slice 3b (SPRINT-71 TASK-7100, R3/R5/R6/R8): termination_payout_requests (§26 anmodning record; composite FK onto vacation_settlements PK; state OPEN/LINE_STAGED/VOIDED_BY_REVERSAL; partial-unique ONE non-voided request per settlement row; ADR-019 version) + vacation_settlements.bare_reversal_not_due BOOLEAN NOT NULL DEFAULT FALSE (R3 durable bare-reversal not-due marker, CHECK TRUE only on REVERSED rows) + review_disposition CHECK widened with MODREGNING/WAIVED + disposition-state CHECK widened so a DEFER-marked row can be REVERSED with DEFER history preserved + claim_disposition_days NUMERIC(6,2) NULL (the §7/waiver resolved day-count — never left in forfeit_days; CHECK non-negative + non-null IFF review_disposition MODREGNING/WAIVED) + settlement_export_lines.line_kind ORIGINAL/REVERSAL (existing rows backfill ORIGINAL via DEFAULT) + reverses_line_id BIGINT NULL self-FK to line_id with pairing CHECK (non-null IFF REVERSAL). The request table itself is created by the top-level CREATE TABLE IF NOT EXISTS (brand-new, legacy-safe by construction); this block carries the EXISTING-table ALTERs for pre-S71 databases.')
    ON CONFLICT (migration_id) DO NOTHING;

    IF NOT FOUND THEN
        RETURN;
    END IF;

    -- ── vacation_settlements (R3 + R5) ─────────────────────────────────────
    -- Columns first. Both are additive (nullable / defaulted): legacy rows
    -- backfill claim_disposition_days=NULL + bare_reversal_not_due=FALSE,
    -- which every CHECK added below admits.
    ALTER TABLE vacation_settlements
    ADD COLUMN IF NOT EXISTS claim_disposition_days NUMERIC(6,2) NULL;

    ALTER TABLE vacation_settlements
    ADD COLUMN IF NOT EXISTS bare_reversal_not_due BOOLEAN NOT NULL DEFAULT FALSE;

    -- The S68 base CREATE carried review_disposition's value set as an INLINE
    -- column CHECK, which PostgreSQL auto-named vacation_settlements_review_disposition_check
    -- on a legacy DB; the S71 greenfield CREATE bakes the widened NAMED form.
    -- DROP both forms, re-ADD the named widened form — both paths converge on
    -- the pinned name. Widened set is a superset (FORFEIT/DEFER still valid).
    ALTER TABLE vacation_settlements
    DROP CONSTRAINT IF EXISTS vacation_settlements_review_disposition_check;

    ALTER TABLE vacation_settlements
    DROP CONSTRAINT IF EXISTS vacation_settlements_review_disposition;

    ALTER TABLE vacation_settlements
    ADD CONSTRAINT vacation_settlements_review_disposition
    CHECK (
        review_disposition IS NULL
        OR review_disposition IN ('FORFEIT', 'DEFER', 'MODREGNING', 'WAIVED')
    );

    -- Widened coupling: DEFER now admits REVERSED (history preserved on
    -- reversal); MODREGNING/WAIVED behave like FORFEIT (resolved — never
    -- PENDING_REVIEW). Superset of the S68 form: DEFER∧PENDING_REVIEW and
    -- FORFEIT∧¬PENDING_REVIEW both still pass, so legacy rows validate.
    ALTER TABLE vacation_settlements
    DROP CONSTRAINT IF EXISTS vacation_settlements_disposition_state;

    ALTER TABLE vacation_settlements
    ADD CONSTRAINT vacation_settlements_disposition_state
    CHECK (
        review_disposition IS NULL
        OR (review_disposition = 'DEFER' AND settlement_state IN ('PENDING_REVIEW', 'REVERSED'))
        OR (review_disposition IN ('FORFEIT', 'MODREGNING', 'WAIVED') AND settlement_state <> 'PENDING_REVIEW')
    );

    ALTER TABLE vacation_settlements
    DROP CONSTRAINT IF EXISTS vacation_settlements_bare_reversal_reversed_only;

    ALTER TABLE vacation_settlements
    ADD CONSTRAINT vacation_settlements_bare_reversal_reversed_only
    CHECK (bare_reversal_not_due = FALSE OR settlement_state = 'REVERSED');

    ALTER TABLE vacation_settlements
    DROP CONSTRAINT IF EXISTS vacation_settlements_claim_disposition_nonneg;

    ALTER TABLE vacation_settlements
    ADD CONSTRAINT vacation_settlements_claim_disposition_nonneg
    CHECK (claim_disposition_days IS NULL OR claim_disposition_days >= 0);

    ALTER TABLE vacation_settlements
    DROP CONSTRAINT IF EXISTS vacation_settlements_claim_disposition_paired;

    ALTER TABLE vacation_settlements
    ADD CONSTRAINT vacation_settlements_claim_disposition_paired
    CHECK (
        (claim_disposition_days IS NOT NULL)
        = (review_disposition IS NOT NULL AND review_disposition IN ('MODREGNING', 'WAIVED'))
    );

    -- ── settlement_export_lines (R8) ───────────────────────────────────────
    -- line_kind's DEFAULT 'ORIGINAL' is the legacy backfill: every pre-S71 row
    -- (all of them §24 originals) becomes ORIGINAL with reverses_line_id NULL,
    -- which the pairing CHECK admits.
    ALTER TABLE settlement_export_lines
    ADD COLUMN IF NOT EXISTS line_kind TEXT NOT NULL DEFAULT 'ORIGINAL';

    ALTER TABLE settlement_export_lines
    ADD COLUMN IF NOT EXISTS reverses_line_id BIGINT NULL;

    ALTER TABLE settlement_export_lines
    DROP CONSTRAINT IF EXISTS settlement_export_lines_line_kind;

    ALTER TABLE settlement_export_lines
    ADD CONSTRAINT settlement_export_lines_line_kind
    CHECK (line_kind IN ('ORIGINAL', 'REVERSAL'));

    ALTER TABLE settlement_export_lines
    DROP CONSTRAINT IF EXISTS settlement_export_lines_reverses_line_fk;

    ALTER TABLE settlement_export_lines
    ADD CONSTRAINT settlement_export_lines_reverses_line_fk
    FOREIGN KEY (reverses_line_id) REFERENCES settlement_export_lines (line_id);

    ALTER TABLE settlement_export_lines
    DROP CONSTRAINT IF EXISTS settlement_export_lines_reversal_pairing;

    ALTER TABLE settlement_export_lines
    ADD CONSTRAINT settlement_export_lines_reversal_pairing
    CHECK ((reverses_line_id IS NOT NULL) = (line_kind = 'REVERSAL'));
END
$$;

-- W3 (SPRINT-71 Step-5a cycle-1) — one-marker-per-tuple backstop for the R3
-- bare-reversal marker: at most ONE bare_reversal_not_due = TRUE row per
-- (employee, type, year) tuple. R3's "EXACTLY ONE latest reversed row carries
-- the marker" splits in two: the AT-MOST-ONE half is DB-enforced here; the
-- CROSS-ROW half ("a marker never coexists with a later active row" — the
-- Step-B anti-join would wrongly suppress a live tuple) stays FLOW-enforced in
-- 3b (the marker is terminal: no 3b operation clears it and no settle path can
-- insert past it; Slice3bSchemaConstraintTests pins the boundary) — the
-- REHIRE/recovery follow-up owns the stronger backstop. Deliberately placed
-- AFTER the guarded DO block: on a legacy DB the column exists by the time
-- this runs; on a greenfield DB it is baked into the base CREATE; on an
-- already-upgraded DB (ledger row present, the DO block short-circuits)
-- IF NOT EXISTS still lands the index — idempotent on all three paths.
CREATE UNIQUE INDEX IF NOT EXISTS idx_vacation_settlements_bare_reversal_marker
    ON vacation_settlements (employee_id, entitlement_type, entitlement_year)
    WHERE bare_reversal_not_due;
-- S71-SLICE3B-SEGMENT-END

-- =========================================================================
-- SPRINT 71 / TASK-7105 — settlement_payroll_inbox composite-key migration
--   (SPRINT-71 R7) + the §26 termination-payout wage-type seed (R11).
--
-- (1) Inbox PK (source_event_id) → (source_event_id, bucket). The S71
--     SettlementReversed consumer compensates EVERY staged bucket of a reversed
--     settlement row — one checkpoint per (event, bucket), all in ONE tx — so
--     the S69 single-column PK can no longer hold. R7 migration order, VERBATIM:
--       0. PRE-FLIGHT validate-or-abort (SPRINT-71 Step-5a cycle-1 W1): the
--          poison discriminator below relies on a writer CONVENTION the S69
--          schema never ENFORCED (every identity column independently nullable,
--          no poison-shape CHECK) — so FIRST every NULL-bucket row is validated
--          to be EITHER all-identity-NULL (the canonical poison shape) OR
--          fully-identity-populated (the canonical normal shape); ANY hybrid
--          row RAISEs EXCEPTION naming the offending source_event_id(s) and the
--          whole block (ledger row included) rolls back for manual remediation
--          — fail-closed beats silent misclassification;
--       1. backfill poison NULL buckets → the '_EVENT' sentinel (poison rows are
--          the ONLY rows whose identity columns are NULL — S69 Step-7a FIX 1);
--       2. backfill any remaining NULL-bucket row → 'AUTO_PAYOUT_24' (defensive:
--          every S69-era normal row already carries the §24 bucket — the §24
--          emitter was the sole writer — but the rule is pinned, not assumed);
--       3. bucket SET NOT NULL;
--       4. drop the old single-column PK;
--       5. add the composite PK (source_event_id, bucket) under the SAME pinned
--          name, so greenfield and legacy converge;
--     plus the processing_status CHECK widened with SKIPPED_VOIDED (the R6/R9
--     under-lock voided-skip terminal; drop the S69 inline auto-name AND the
--     pinned name, re-ADD the pinned name — the 7100 convention; the widened
--     set is a strict superset, so legacy rows validate with no remediation).
--
-- (2) R11 seed: ONE new time_type VACATION_TERMINATION_PAYOUT → the placeholder
--     sentinel SLS_TBD_S26 (Ferielov §26 stk.1 payout efter anmodning), exactly
--     the s69-s24-settlement-wage-type 10-pair shape. NO §7 seeds — the
--     SLS_TBD_S7 / VACATION_TERMINATION_DEDUCTION rows are PARKED with the
--     gate-(i) waiver-only branch (the SLS-dialogue task owns them).
--
-- The S71-INBOX-SEGMENT markers fence the migration DO-block for
-- SettlementInboxMigrationTests (the Slice3bLegacyMigrationTests verbatim-
-- extraction harness pattern): the test runs THIS exact segment against a
-- reconstructed pre-S71 (S69-shape) inbox, twice. Keep the marker lines intact
-- and keep the inbox-migration DDL between them. (The seed block lives OUTSIDE
-- the markers — the migration harness's reconstructed schema has no
-- wage_type_mappings table.)
-- =========================================================================
-- S71-INBOX-SEGMENT-BEGIN
DO $$
DECLARE
    s71_hybrid_inbox_ids TEXT;
BEGIN
    INSERT INTO schema_migrations (migration_id, notes)
    VALUES ('s71-inbox-composite-bucket-key', 'ADR-033 slice 3b (SPRINT-71 TASK-7105, R7): settlement_payroll_inbox PK (source_event_id) -> (source_event_id, bucket) so one consumed event may checkpoint multiple buckets (the SettlementReversed consumer, atomic per-event multi-bucket completion). R7 order: PRE-FLIGHT validate-or-abort on every NULL-bucket row (Step-5a cycle-1 W1 — all-identity-NULL poison XOR fully-identity-populated normal; any hybrid shape aborts the whole block naming the offending source_event_ids, fail-closed); poison NULL buckets backfilled to the ''_EVENT'' sentinel (identity-NULL rows, S69 Step-7a FIX 1); remaining NULL buckets backfilled to AUTO_PAYOUT_24 (all S69-era normal rows are §24); bucket NOT NULL; old PK dropped; composite PK added under the same pinned name. processing_status CHECK widened with SKIPPED_VOIDED (R6/R9 under-lock voided-skip terminal; strict superset, no remediation). ''_EVENT'' semantics: terminal ''_EVENT'' DEAD_LETTER is event-level and covers every bucket; transient diagnostics (RETRY_PENDING) also key at ''_EVENT'' and are promoted on completion (monotonic per bucket AND event level).')
    ON CONFLICT (migration_id) DO NOTHING;

    IF NOT FOUND THEN
        RETURN;
    END IF;

    -- R7 step 0 — PRE-FLIGHT validate-or-abort (SPRINT-71 Step-5a cycle-1 W1).
    -- The step-1/step-2 discriminator (employee_id IS NULL ⇒ poison) encodes a
    -- writer CONVENTION the S69 schema never enforced. Validate it BEFORE
    -- classifying: every NULL-bucket row must be EITHER all-identity-NULL (the
    -- canonical poison shape) OR fully-identity-populated (the canonical normal
    -- shape). Any hybrid row aborts the migration loudly (the exception rolls
    -- back the ENTIRE block, ledger row included — nothing is mutated) for
    -- manual remediation; fail-closed beats silent misclassification.
    SELECT string_agg(source_event_id::text, ', ' ORDER BY source_event_id)
      INTO s71_hybrid_inbox_ids
      FROM settlement_payroll_inbox
     WHERE bucket IS NULL
       AND NOT (
                (employee_id IS NULL AND entitlement_type IS NULL
                 AND entitlement_year IS NULL AND sequence IS NULL)
             OR (employee_id IS NOT NULL AND entitlement_type IS NOT NULL
                 AND entitlement_year IS NOT NULL AND sequence IS NOT NULL)
           );
    IF s71_hybrid_inbox_ids IS NOT NULL THEN
        RAISE EXCEPTION 's71-inbox-composite-bucket-key pre-flight: settlement_payroll_inbox holds NULL-bucket row(s) in a HYBRID identity shape (neither the all-identity-NULL poison shape nor the fully-identity-populated normal shape) - refusing to classify them; manually remediate source_event_id(s): %', s71_hybrid_inbox_ids;
    END IF;

    -- R7 step 1: poison/parse-failure rows (the ONLY rows with NULL identity
    -- columns) move from bucket NULL to the '_EVENT' sentinel.
    UPDATE settlement_payroll_inbox
       SET bucket = '_EVENT', updated_at = NOW()
     WHERE bucket IS NULL AND employee_id IS NULL;

    -- R7 step 2: any remaining NULL-bucket row is an S69-era normal row — the
    -- §24 emitter was the sole S69 writer, so the §24 bucket is the backfill.
    UPDATE settlement_payroll_inbox
       SET bucket = 'AUTO_PAYOUT_24', updated_at = NOW()
     WHERE bucket IS NULL;

    -- R7 step 3 (no-op on greenfield where the base CREATE already pins it).
    ALTER TABLE settlement_payroll_inbox
    ALTER COLUMN bucket SET NOT NULL;

    -- R7 steps 4+5: the PK swap. Same pinned constraint name on both paths
    -- (greenfield re-creates the identical composite PK — convergent; no FK
    -- references this PK, so the drop is dependency-free). Uniqueness of the
    -- new key is guaranteed (the old key was source_event_id alone).
    ALTER TABLE settlement_payroll_inbox
    DROP CONSTRAINT IF EXISTS settlement_payroll_inbox_pkey;

    ALTER TABLE settlement_payroll_inbox
    ADD CONSTRAINT settlement_payroll_inbox_pkey PRIMARY KEY (source_event_id, bucket);

    -- processing_status CHECK widened with SKIPPED_VOIDED. The S69 base CREATE
    -- carried it as an INLINE column CHECK (auto-named
    -- settlement_payroll_inbox_processing_status_check on a legacy DB); the S71
    -- greenfield CREATE bakes the widened NAMED form. DROP both forms, re-ADD
    -- the named widened form — both paths converge on the pinned name.
    ALTER TABLE settlement_payroll_inbox
    DROP CONSTRAINT IF EXISTS settlement_payroll_inbox_processing_status_check;

    ALTER TABLE settlement_payroll_inbox
    DROP CONSTRAINT IF EXISTS settlement_payroll_inbox_processing_status;

    ALTER TABLE settlement_payroll_inbox
    ADD CONSTRAINT settlement_payroll_inbox_processing_status
    CHECK (processing_status IN ('RETRY_PENDING', 'PROCESSED', 'SKIPPED_RECONCILED', 'SKIPPED_VOIDED', 'DEAD_LETTER'));
END
$$;
-- S71-INBOX-SEGMENT-END

-- =========================================================================
-- S71 / TASK-7105 — §26 termination vacation-payout wage-type mapping
--   (SPRINT-71 R11; ADR-033 slice 3b). The settlement_export_lines.wage_type
--   for a TERMINATION_PAYOUT_26 bucket resolves through wage_type_mappings on
--   the new time_type VACATION_TERMINATION_PAYOUT. The resolved lønart is a
--   PLACEHOLDER sentinel (SLS_TBD_S26) — the real SLS code AND the line format
--   are UNVERIFIED, deferred to the SLS-dialogue task. The existing outbound
--   delivery guard (PayrollExportService, the SLS_TBD_ prefix refusal) already
--   rejects it unconditionally — the 3b deliverable is coverage TESTS, not new
--   guard code (R11). Swapping the real code in later is a one-row ADR-020
--   effective-dated change (the S69 owner-insight precedent).
--
-- ADR-020 versioned natural key + coverage: position = '' (national; the §26
--   payout is statutory, not institution-varying), effective_from 2020-01-01
--   open row, the EXACT s69-s24-settlement-wage-type 10-pair matrix
--   {AC, HK, PROSA, AC_RESEARCH, AC_TEACHING} × {OK24, OK26}.
--
-- NO §7 rows: SLS_TBD_S7 / VACATION_TERMINATION_DEDUCTION are PARKED with the
--   gate-(i) waiver-only branch (the SLS answer determines the payload/lønart
--   shape) — seeding them now would bake an unverified contract.
-- =========================================================================
DO $$
BEGIN
    INSERT INTO schema_migrations (migration_id, notes)
    VALUES ('s71-s26-termination-wage-type', 'ADR-033 slice 3b (SPRINT-71 R11) — §26 VACATION_TERMINATION_PAYOUT -> placeholder SLS_TBD_S26, ADR-020 versioned (effective-dated open row), position '''' national, covering the s69-s24 10-pair agreement/OK matrix {AC,HK,PROSA,AC_RESEARCH,AC_TEACHING}x{OK24,OK26}; sentinel refused by the existing SLS_TBD_ outbound delivery guard (coverage tests, no new guard code); real SLS code+line format deferred to SLS dialogue (D1 money-free, the line carries a day-count). NO SLS_TBD_S7 seeds — the §7 verb is PARKED per the gate-(i) waiver-only branch.')
    ON CONFLICT (migration_id) DO NOTHING;

    IF NOT FOUND THEN
        RETURN;
    END IF;

    INSERT INTO wage_type_mappings (time_type, wage_type, ok_version, agreement_code, position, description, effective_from) VALUES
        ('VACATION_TERMINATION_PAYOUT', 'SLS_TBD_S26', 'OK24', 'AC',          '', 'PLACEHOLDER - §26 termination vacation payout efter anmodning (Ferielov §26 stk.1); real SLS code TBD via SLS dialogue', '2020-01-01'),
        ('VACATION_TERMINATION_PAYOUT', 'SLS_TBD_S26', 'OK24', 'HK',          '', 'PLACEHOLDER - §26 termination vacation payout efter anmodning (Ferielov §26 stk.1); real SLS code TBD via SLS dialogue', '2020-01-01'),
        ('VACATION_TERMINATION_PAYOUT', 'SLS_TBD_S26', 'OK24', 'PROSA',       '', 'PLACEHOLDER - §26 termination vacation payout efter anmodning (Ferielov §26 stk.1); real SLS code TBD via SLS dialogue', '2020-01-01'),
        ('VACATION_TERMINATION_PAYOUT', 'SLS_TBD_S26', 'OK24', 'AC_RESEARCH', '', 'PLACEHOLDER - §26 termination vacation payout efter anmodning (Ferielov §26 stk.1); real SLS code TBD via SLS dialogue', '2020-01-01'),
        ('VACATION_TERMINATION_PAYOUT', 'SLS_TBD_S26', 'OK24', 'AC_TEACHING', '', 'PLACEHOLDER - §26 termination vacation payout efter anmodning (Ferielov §26 stk.1); real SLS code TBD via SLS dialogue', '2020-01-01'),
        ('VACATION_TERMINATION_PAYOUT', 'SLS_TBD_S26', 'OK26', 'AC',          '', 'PLACEHOLDER - §26 termination vacation payout efter anmodning (Ferielov §26 stk.1); real SLS code TBD via SLS dialogue', '2020-01-01'),
        ('VACATION_TERMINATION_PAYOUT', 'SLS_TBD_S26', 'OK26', 'HK',          '', 'PLACEHOLDER - §26 termination vacation payout efter anmodning (Ferielov §26 stk.1); real SLS code TBD via SLS dialogue', '2020-01-01'),
        ('VACATION_TERMINATION_PAYOUT', 'SLS_TBD_S26', 'OK26', 'PROSA',       '', 'PLACEHOLDER - §26 termination vacation payout efter anmodning (Ferielov §26 stk.1); real SLS code TBD via SLS dialogue', '2020-01-01'),
        ('VACATION_TERMINATION_PAYOUT', 'SLS_TBD_S26', 'OK26', 'AC_RESEARCH', '', 'PLACEHOLDER - §26 termination vacation payout efter anmodning (Ferielov §26 stk.1); real SLS code TBD via SLS dialogue', '2020-01-01'),
        ('VACATION_TERMINATION_PAYOUT', 'SLS_TBD_S26', 'OK26', 'AC_TEACHING', '', 'PLACEHOLDER - §26 termination vacation payout efter anmodning (Ferielov §26 stk.1); real SLS code TBD via SLS dialogue', '2020-01-01')
    ON CONFLICT (time_type, ok_version, agreement_code, position, effective_from) DO NOTHING;
END
$$;

-- =========================================================================
-- SPRINT 72 / TASK-7200 — Skema-redesign per-user row preferences (R4).
--
-- Three schema units:
--   (1) user_skema_preferences — the R4 configured-state CONTAINER. A row here
--       means the employee has explicitly configured their Skema rows; from
--       then on user_project_selections / user_absence_selections are
--       AUTHORITATIVE EVEN WHEN EMPTY. No container row → today's fallback
--       (all org projects / all filtered absence types) stays in effect.
--       Brand-new table, so the top-level CREATE TABLE IF NOT EXISTS is
--       legacy-safe by construction (the S69 settlement_payroll_inbox / S71
--       termination_payout_requests precedent).
--   (2) user_project_selections.sort_order — per-user row order for the
--       project rows. Baked inline into the base CREATE above (SPRINT 9
--       section) for greenfield; the guarded DO block below upgrades a
--       pre-S72 database and one-shot-backfills legacy rows from the matching
--       projects.sort_order so a pre-S72 employee's rows keep their familiar
--       org order on first render. Duplicate values are EXPECTED and fine —
--       readers order deterministically with ORDER BY sort_order,
--       project_code (R4); the modal's reorder writes a dense reindex.
--   (3) user_absence_selections — the per-user absence-row analog (visible
--       type + order). Same brand-new-table reasoning as (1). absence_type is
--       the TEXT code used across the system (the absence_type_visibility
--       convention) — deliberately NOT FK-bound: the absence-type catalog is
--       config/eligibility-derived, not a table. Stale selections never
--       resurrect org-hidden/ineligible types — VISIBLE = catalog ∩
--       selections is computed server-side (R4).
--
-- These are VIEW preferences, not domain state (R4): plain un-evented rows
-- per the ProjectRepository selection precedent — no event family, no audit
-- projection, no ADR-019 version column.
--
-- No supporting indexes beyond the PKs: every read path is keyed by
-- employee_id, which is user_skema_preferences' whole PK and leads
-- user_absence_selections' composite PK; user_project_selections already
-- carries idx_user_project_sel_employee.
--
-- The S72-ROW-PREFERENCES-SEGMENT markers below are extracted VERBATIM by
-- SkemaRowPreferencesLegacyMigrationTests (the S71 Slice3bLegacyMigrationTests
-- harness pattern: the test runs this exact segment against a reconstructed
-- pre-S72 schema, twice) — keep the marker lines intact and keep all S72 DDL
-- between them.
-- =========================================================================
-- S72-ROW-PREFERENCES-SEGMENT-BEGIN

-- user_skema_preferences — the R4 configured-state container. One row per
-- employee, created the first time the employee saves row preferences;
-- initialized_at records when the configured state began.
CREATE TABLE IF NOT EXISTS user_skema_preferences (
    employee_id     TEXT        PRIMARY KEY REFERENCES users(user_id),
    initialized_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- user_absence_selections — per-user visible absence rows + order. The
-- composite PK (employee_id, absence_type) is also the read path (the GET
-- reads by employee_id, the leading PK column) — no further index.
CREATE TABLE IF NOT EXISTS user_absence_selections (
    employee_id     TEXT        NOT NULL REFERENCES users(user_id),
    absence_type    TEXT        NOT NULL,
    sort_order      INT         NOT NULL DEFAULT 0,
    PRIMARY KEY (employee_id, absence_type)
);

-- schema_migrations-guarded legacy upgrade block (the s71-slice3b /
-- s70-employment-end-date guard pattern, 3-path idempotent): on a greenfield
-- DB this block runs once — the ADD COLUMN no-ops against the inline column
-- and the backfill touches zero rows (user_project_selections has no seed
-- rows); on a legacy DB it lands the column + the one-shot backfill; a re-run
-- is a ledger-guarded no-op, so the backfill can never clobber orderings the
-- employee has since chosen in the modal.
DO $$
BEGIN
    INSERT INTO schema_migrations (migration_id, notes)
    VALUES ('s72-skema-row-preferences-schema', 'Skema redesign (SPRINT-72 TASK-7200, R4): user_skema_preferences configured-state container (employee_id PK -> users(user_id), initialized_at) + user_project_selections.sort_order INT NOT NULL DEFAULT 0 (per-user row order; legacy rows one-shot-backfilled from the matching projects.sort_order — duplicates expected, readers tiebreak ORDER BY sort_order, project_code) + user_absence_selections (employee_id -> users(user_id), absence_type, sort_order; PK (employee_id, absence_type)). View preferences, not domain state: plain un-evented rows per the ProjectRepository selection precedent (no event family, no audit projection, no ADR-019 version). The two new tables are created by the top-level CREATE TABLE IF NOT EXISTS (legacy-safe by construction); this block carries the EXISTING-table ALTER + the one-shot backfill for pre-S72 databases.')
    ON CONFLICT (migration_id) DO NOTHING;

    IF NOT FOUND THEN
        RETURN;
    END IF;

    -- Additive defaulted column — every existing reader/writer is unaffected
    -- (the TASK-7200 caller census: the explicit-column INSERT in
    -- ProjectRepository.AddSelectionAsync takes the DEFAULT; the SELECT/JOIN/
    -- DELETE sites never reference the new column).
    ALTER TABLE user_project_selections
    ADD COLUMN IF NOT EXISTS sort_order INT NOT NULL DEFAULT 0;

    -- One-shot legacy backfill: seed each selection's per-user order from the
    -- org-level projects.sort_order. Duplicates are expected (selected
    -- projects may share an org sort_order) — reads tiebreak deterministically
    -- per R4. Ledger-guarded: runs exactly once, never on re-apply.
    UPDATE user_project_selections ups
       SET sort_order = p.sort_order
      FROM projects p
     WHERE p.project_id = ups.project_id;
END
$$;
-- S72-ROW-PREFERENCES-SEGMENT-END
