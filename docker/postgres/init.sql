-- StatsTid Event Store Schema
-- Sprint 2 initialization

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

-- Wage type mappings (versioned per OK agreement)
CREATE TABLE IF NOT EXISTS wage_type_mappings (
    time_type       TEXT        NOT NULL,
    wage_type       TEXT        NOT NULL,
    ok_version      TEXT        NOT NULL,
    agreement_code  TEXT        NOT NULL,
    description     TEXT,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (time_type, ok_version, agreement_code)
);

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
INSERT INTO wage_type_mappings (time_type, wage_type, ok_version, agreement_code, description) VALUES
    -- Normal hours (all agreements)
    ('NORMAL_HOURS', 'SLS_0110', 'OK24', 'AC', 'Normal working hours'),
    ('NORMAL_HOURS', 'SLS_0110', 'OK24', 'HK', 'Normal working hours'),
    ('NORMAL_HOURS', 'SLS_0110', 'OK24', 'PROSA', 'Normal working hours'),
    -- Overtime (HK/PROSA)
    ('OVERTIME_50', 'SLS_0210', 'OK24', 'HK', 'Overtime at 50% supplement'),
    ('OVERTIME_50', 'SLS_0210', 'OK24', 'PROSA', 'Overtime at 50% supplement'),
    ('OVERTIME_100', 'SLS_0220', 'OK24', 'HK', 'Overtime at 100% supplement'),
    ('OVERTIME_100', 'SLS_0220', 'OK24', 'PROSA', 'Overtime at 100% supplement'),
    -- Merarbejde (AC only)
    ('MERARBEJDE', 'SLS_0310', 'OK24', 'AC', 'Extra work (merarbejde)'),
    -- Supplements (HK/PROSA)
    ('EVENING_SUPPLEMENT', 'SLS_0410', 'OK24', 'HK', 'Evening supplement 17-23'),
    ('EVENING_SUPPLEMENT', 'SLS_0410', 'OK24', 'PROSA', 'Evening supplement 17-23'),
    ('NIGHT_SUPPLEMENT', 'SLS_0420', 'OK24', 'HK', 'Night supplement 23-06'),
    ('NIGHT_SUPPLEMENT', 'SLS_0420', 'OK24', 'PROSA', 'Night supplement 23-06'),
    ('WEEKEND_SUPPLEMENT', 'SLS_0430', 'OK24', 'HK', 'Weekend supplement'),
    ('WEEKEND_SUPPLEMENT', 'SLS_0430', 'OK24', 'PROSA', 'Weekend supplement'),
    ('HOLIDAY_SUPPLEMENT', 'SLS_0440', 'OK24', 'HK', 'Public holiday supplement'),
    ('HOLIDAY_SUPPLEMENT', 'SLS_0440', 'OK24', 'PROSA', 'Public holiday supplement'),
    -- Absence types (all agreements)
    ('VACATION', 'SLS_0510', 'OK24', 'AC', 'Vacation'),
    ('VACATION', 'SLS_0510', 'OK24', 'HK', 'Vacation'),
    ('VACATION', 'SLS_0510', 'OK24', 'PROSA', 'Vacation'),
    ('CARE_DAY', 'SLS_0520', 'OK24', 'AC', 'Care day'),
    ('CARE_DAY', 'SLS_0520', 'OK24', 'HK', 'Care day'),
    ('CARE_DAY', 'SLS_0520', 'OK24', 'PROSA', 'Care day'),
    ('CHILD_SICK_DAY', 'SLS_0530', 'OK24', 'AC', 'Childs 1st sick day'),
    ('CHILD_SICK_DAY', 'SLS_0530', 'OK24', 'HK', 'Childs 1st sick day'),
    ('CHILD_SICK_DAY', 'SLS_0530', 'OK24', 'PROSA', 'Childs 1st sick day'),
    ('PARENTAL_LEAVE', 'SLS_0540', 'OK24', 'AC', 'Parental leave'),
    ('PARENTAL_LEAVE', 'SLS_0540', 'OK24', 'HK', 'Parental leave'),
    ('PARENTAL_LEAVE', 'SLS_0540', 'OK24', 'PROSA', 'Parental leave'),
    ('SENIOR_DAY', 'SLS_0550', 'OK24', 'AC', 'Senior day'),
    ('SENIOR_DAY', 'SLS_0550', 'OK24', 'HK', 'Senior day'),
    ('SENIOR_DAY', 'SLS_0550', 'OK24', 'PROSA', 'Senior day'),
    ('LEAVE_WITHOUT_PAY', 'SLS_0560', 'OK24', 'AC', 'Leave without pay'),
    ('LEAVE_WITHOUT_PAY', 'SLS_0560', 'OK24', 'HK', 'Leave without pay'),
    ('LEAVE_WITHOUT_PAY', 'SLS_0560', 'OK24', 'PROSA', 'Leave without pay'),
    ('CHILD_SICK_DAY_2', 'SLS_0531', 'OK24', 'AC', 'Childs 2nd sick day'),
    ('CHILD_SICK_DAY_2', 'SLS_0531', 'OK24', 'HK', 'Childs 2nd sick day'),
    ('CHILD_SICK_DAY_2', 'SLS_0531', 'OK24', 'PROSA', 'Childs 2nd sick day'),
    ('CHILD_SICK_DAY_3', 'SLS_0532', 'OK24', 'AC', 'Childs 3rd sick day'),
    ('CHILD_SICK_DAY_3', 'SLS_0532', 'OK24', 'HK', 'Childs 3rd sick day'),
    ('CHILD_SICK_DAY_3', 'SLS_0532', 'OK24', 'PROSA', 'Childs 3rd sick day'),
    ('SPECIAL_HOLIDAY_ALLOWANCE', 'SLS_0570', 'OK24', 'AC', 'Special holiday allowance'),
    ('SPECIAL_HOLIDAY_ALLOWANCE', 'SLS_0570', 'OK24', 'HK', 'Special holiday allowance'),
    ('SPECIAL_HOLIDAY_ALLOWANCE', 'SLS_0570', 'OK24', 'PROSA', 'Special holiday allowance'),
    ('LEAVE_WITH_PAY', 'SLS_0565', 'OK24', 'AC', 'Leave with pay'),
    ('LEAVE_WITH_PAY', 'SLS_0565', 'OK24', 'HK', 'Leave with pay'),
    ('LEAVE_WITH_PAY', 'SLS_0565', 'OK24', 'PROSA', 'Leave with pay'),
    -- Flex payout
    ('FLEX_PAYOUT', 'SLS_0610', 'OK24', 'AC', 'Flex balance auto-payout'),
    ('FLEX_PAYOUT', 'SLS_0610', 'OK24', 'HK', 'Flex balance auto-payout'),
    ('FLEX_PAYOUT', 'SLS_0610', 'OK24', 'PROSA', 'Flex balance auto-payout'),
    -- On-call duty (rådighedsvagt)
    ('ON_CALL_DUTY', 'SLS_0710', 'OK24', 'AC', 'On-call duty compensation'),
    ('ON_CALL_DUTY', 'SLS_0710', 'OK24', 'HK', 'On-call duty compensation'),
    ('ON_CALL_DUTY', 'SLS_0710', 'OK24', 'PROSA', 'On-call duty compensation')
ON CONFLICT DO NOTHING;

-- Seed OK26 wage type mappings (identical to OK24 for now)
INSERT INTO wage_type_mappings (time_type, wage_type, ok_version, agreement_code, description) VALUES
    ('NORMAL_HOURS', 'SLS_0110', 'OK26', 'AC', 'Normal working hours'),
    ('NORMAL_HOURS', 'SLS_0110', 'OK26', 'HK', 'Normal working hours'),
    ('NORMAL_HOURS', 'SLS_0110', 'OK26', 'PROSA', 'Normal working hours'),
    ('OVERTIME_50', 'SLS_0210', 'OK26', 'HK', 'Overtime at 50% supplement'),
    ('OVERTIME_50', 'SLS_0210', 'OK26', 'PROSA', 'Overtime at 50% supplement'),
    ('OVERTIME_100', 'SLS_0220', 'OK26', 'HK', 'Overtime at 100% supplement'),
    ('OVERTIME_100', 'SLS_0220', 'OK26', 'PROSA', 'Overtime at 100% supplement'),
    ('MERARBEJDE', 'SLS_0310', 'OK26', 'AC', 'Extra work (merarbejde)'),
    ('EVENING_SUPPLEMENT', 'SLS_0410', 'OK26', 'HK', 'Evening supplement 17-23'),
    ('EVENING_SUPPLEMENT', 'SLS_0410', 'OK26', 'PROSA', 'Evening supplement 17-23'),
    ('NIGHT_SUPPLEMENT', 'SLS_0420', 'OK26', 'HK', 'Night supplement 23-06'),
    ('NIGHT_SUPPLEMENT', 'SLS_0420', 'OK26', 'PROSA', 'Night supplement 23-06'),
    ('WEEKEND_SUPPLEMENT', 'SLS_0430', 'OK26', 'HK', 'Weekend supplement'),
    ('WEEKEND_SUPPLEMENT', 'SLS_0430', 'OK26', 'PROSA', 'Weekend supplement'),
    ('HOLIDAY_SUPPLEMENT', 'SLS_0440', 'OK26', 'HK', 'Public holiday supplement'),
    ('HOLIDAY_SUPPLEMENT', 'SLS_0440', 'OK26', 'PROSA', 'Public holiday supplement'),
    ('VACATION', 'SLS_0510', 'OK26', 'AC', 'Vacation'),
    ('VACATION', 'SLS_0510', 'OK26', 'HK', 'Vacation'),
    ('VACATION', 'SLS_0510', 'OK26', 'PROSA', 'Vacation'),
    ('CARE_DAY', 'SLS_0520', 'OK26', 'AC', 'Care day'),
    ('CARE_DAY', 'SLS_0520', 'OK26', 'HK', 'Care day'),
    ('CARE_DAY', 'SLS_0520', 'OK26', 'PROSA', 'Care day'),
    ('CHILD_SICK_DAY', 'SLS_0530', 'OK26', 'AC', 'Childs 1st sick day'),
    ('CHILD_SICK_DAY', 'SLS_0530', 'OK26', 'HK', 'Childs 1st sick day'),
    ('CHILD_SICK_DAY', 'SLS_0530', 'OK26', 'PROSA', 'Childs 1st sick day'),
    ('PARENTAL_LEAVE', 'SLS_0540', 'OK26', 'AC', 'Parental leave'),
    ('PARENTAL_LEAVE', 'SLS_0540', 'OK26', 'HK', 'Parental leave'),
    ('PARENTAL_LEAVE', 'SLS_0540', 'OK26', 'PROSA', 'Parental leave'),
    ('SENIOR_DAY', 'SLS_0550', 'OK26', 'AC', 'Senior day'),
    ('SENIOR_DAY', 'SLS_0550', 'OK26', 'HK', 'Senior day'),
    ('SENIOR_DAY', 'SLS_0550', 'OK26', 'PROSA', 'Senior day'),
    ('LEAVE_WITHOUT_PAY', 'SLS_0560', 'OK26', 'AC', 'Leave without pay'),
    ('LEAVE_WITHOUT_PAY', 'SLS_0560', 'OK26', 'HK', 'Leave without pay'),
    ('LEAVE_WITHOUT_PAY', 'SLS_0560', 'OK26', 'PROSA', 'Leave without pay'),
    ('CHILD_SICK_DAY_2', 'SLS_0531', 'OK26', 'AC', 'Childs 2nd sick day'),
    ('CHILD_SICK_DAY_2', 'SLS_0531', 'OK26', 'HK', 'Childs 2nd sick day'),
    ('CHILD_SICK_DAY_2', 'SLS_0531', 'OK26', 'PROSA', 'Childs 2nd sick day'),
    ('CHILD_SICK_DAY_3', 'SLS_0532', 'OK26', 'AC', 'Childs 3rd sick day'),
    ('CHILD_SICK_DAY_3', 'SLS_0532', 'OK26', 'HK', 'Childs 3rd sick day'),
    ('CHILD_SICK_DAY_3', 'SLS_0532', 'OK26', 'PROSA', 'Childs 3rd sick day'),
    ('SPECIAL_HOLIDAY_ALLOWANCE', 'SLS_0570', 'OK26', 'AC', 'Special holiday allowance'),
    ('SPECIAL_HOLIDAY_ALLOWANCE', 'SLS_0570', 'OK26', 'HK', 'Special holiday allowance'),
    ('SPECIAL_HOLIDAY_ALLOWANCE', 'SLS_0570', 'OK26', 'PROSA', 'Special holiday allowance'),
    ('LEAVE_WITH_PAY', 'SLS_0565', 'OK26', 'AC', 'Leave with pay'),
    ('LEAVE_WITH_PAY', 'SLS_0565', 'OK26', 'HK', 'Leave with pay'),
    ('LEAVE_WITH_PAY', 'SLS_0565', 'OK26', 'PROSA', 'Leave with pay'),
    ('FLEX_PAYOUT', 'SLS_0610', 'OK26', 'AC', 'Flex balance auto-payout'),
    ('FLEX_PAYOUT', 'SLS_0610', 'OK26', 'HK', 'Flex balance auto-payout'),
    ('FLEX_PAYOUT', 'SLS_0610', 'OK26', 'PROSA', 'Flex balance auto-payout'),
    -- On-call duty (rådighedsvagt)
    ('ON_CALL_DUTY', 'SLS_0710', 'OK26', 'AC', 'On-call duty compensation'),
    ('ON_CALL_DUTY', 'SLS_0710', 'OK26', 'HK', 'On-call duty compensation'),
    ('ON_CALL_DUTY', 'SLS_0710', 'OK26', 'PROSA', 'On-call duty compensation')
ON CONFLICT DO NOTHING;

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
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_users_org ON users(primary_org_id);
CREATE INDEX IF NOT EXISTS idx_users_username ON users(username);

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
CREATE TABLE IF NOT EXISTS local_configuration_audit (
    audit_id            BIGSERIAL   PRIMARY KEY,
    config_id           UUID        NOT NULL,
    action              TEXT        NOT NULL CHECK (action IN ('CREATED', 'MODIFIED', 'DEACTIVATED', 'APPROVED')),
    previous_value      JSONB,
    new_value           JSONB,
    actor_id            TEXT        NOT NULL,
    actor_role          TEXT        NOT NULL,
    timestamp           TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_local_config_audit_config ON local_configuration_audit(config_id);

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
    ('admin01', 'admin01', '$2a$10$xJwL5v7GpxDAMkGGmfHiOONUGHRYiW6rQ3r5yt1FKfpKBneP8Jwm2', 'Global Administrator', 'admin@statstid.dk', 'MIN01', 'AC', 'OK24'),
    ('hr01', 'hr01', '$2a$10$xJwL5v7GpxDAMkGGmfHiOONUGHRYiW6rQ3r5yt1FKfpKBneP8Jwm2', 'HR Medarbejder', 'hr@statens-it.dk', 'STY02', 'HK', 'OK24'),
    ('mgr01', 'mgr01', '$2a$10$xJwL5v7GpxDAMkGGmfHiOONUGHRYiW6rQ3r5yt1FKfpKBneP8Jwm2', 'Team Leder', 'leder@statens-it.dk', 'AFD01', 'HK', 'OK24'),
    ('emp001', 'emp001', '$2a$10$xJwL5v7GpxDAMkGGmfHiOONUGHRYiW6rQ3r5yt1FKfpKBneP8Jwm2', 'AC Medarbejder', 'emp.ac@mfk.dk', 'STY01', 'AC', 'OK24'),
    ('emp002', 'emp002', '$2a$10$xJwL5v7GpxDAMkGGmfHiOONUGHRYiW6rQ3r5yt1FKfpKBneP8Jwm2', 'HK Medarbejder', 'emp.hk@statens-it.dk', 'AFD01', 'HK', 'OK24'),
    ('emp003', 'emp003', '$2a$10$xJwL5v7GpxDAMkGGmfHiOONUGHRYiW6rQ3r5yt1FKfpKBneP8Jwm2', 'PROSA Medarbejder', 'emp.prosa@statens-it.dk', 'AFD02', 'PROSA', 'OK24'),
    ('ladm01', 'ladm01', '$2a$10$xJwL5v7GpxDAMkGGmfHiOONUGHRYiW6rQ3r5yt1FKfpKBneP8Jwm2', 'Lokal Administrator', 'lokal.admin@statens-it.dk', 'STY02', 'HK', 'OK24')
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
