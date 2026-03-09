# StatsTid Database Schema

> Generated from docker/postgres/init.sql. Do not edit manually -- update init.sql and regenerate.
> Last generated: Sprint 15 (2026-03-09)

---

## 1. Event Store

### event_streams

Purpose: Registry of event streams (one per aggregate).

| Column | Type | Constraints |
|--------|------|-------------|
| stream_id | TEXT | PRIMARY KEY |
| created_at | TIMESTAMPTZ | NOT NULL DEFAULT NOW() |

Introduced: Sprint 2

### events

Purpose: Core append-only event log with per-stream versioning.

| Column | Type | Constraints |
|--------|------|-------------|
| global_position | BIGSERIAL | PRIMARY KEY |
| event_id | UUID | NOT NULL UNIQUE |
| stream_id | TEXT | NOT NULL, FK -> event_streams(stream_id) |
| stream_version | INT | NOT NULL |
| event_type | TEXT | NOT NULL |
| data | JSONB | NOT NULL |
| occurred_at | TIMESTAMPTZ | NOT NULL |
| stored_at | TIMESTAMPTZ | NOT NULL DEFAULT NOW() |
| actor_id | TEXT | (added S3) |
| actor_role | TEXT | (added S3) |
| correlation_id | UUID | (added S3) |

**Unique constraint**: (stream_id, stream_version)

**Indexes**:
- idx_events_stream_id ON events(stream_id)
- idx_events_event_type ON events(event_type)
- idx_events_occurred_at ON events(occurred_at)
- idx_events_actor_id ON events(actor_id) (S3)
- idx_events_correlation_id ON events(correlation_id) (S3)

Introduced: Sprint 2 (actor tracking columns added Sprint 3)

---

## 2. Outbox & Orchestration

### outbox_messages

Purpose: Guaranteed delivery outbox pattern for async messaging.

| Column | Type | Constraints |
|--------|------|-------------|
| message_id | UUID | PRIMARY KEY DEFAULT gen_random_uuid() |
| destination | TEXT | NOT NULL |
| payload | JSONB | NOT NULL |
| status | TEXT | NOT NULL DEFAULT 'pending' |
| attempt_count | INT | NOT NULL DEFAULT 0 |
| created_at | TIMESTAMPTZ | NOT NULL DEFAULT NOW() |
| last_attempt_at | TIMESTAMPTZ | |
| delivered_at | TIMESTAMPTZ | |
| error_message | TEXT | |
| idempotency_token | UUID | UNIQUE |

**Indexes**:
- idx_outbox_status ON outbox_messages(status)
- idx_outbox_created_at ON outbox_messages(created_at)
- idx_outbox_destination_status ON outbox_messages(destination, status)

Introduced: Sprint 2

### orchestrator_tasks

Purpose: Audit trail and status tracking for orchestrator-dispatched tasks.

| Column | Type | Constraints |
|--------|------|-------------|
| task_id | UUID | PRIMARY KEY DEFAULT gen_random_uuid() |
| task_type | TEXT | NOT NULL |
| status | TEXT | NOT NULL DEFAULT 'pending' |
| input_data | JSONB | |
| output_data | JSONB | |
| assigned_agent | TEXT | |
| created_at | TIMESTAMPTZ | NOT NULL DEFAULT NOW() |
| started_at | TIMESTAMPTZ | |
| completed_at | TIMESTAMPTZ | |
| error_message | TEXT | |

**Indexes**:
- idx_orch_tasks_status ON orchestrator_tasks(status)

Introduced: Sprint 2

---

## 3. Rules & Wage Types

### rule_versions

Purpose: Versioned rule definitions per OK agreement and agreement code.

| Column | Type | Constraints |
|--------|------|-------------|
| rule_id | TEXT | NOT NULL |
| ok_version | TEXT | NOT NULL |
| rule_name | TEXT | NOT NULL |
| agreement_code | TEXT | NOT NULL |
| effective_from | DATE | NOT NULL |
| effective_to | DATE | |
| created_at | TIMESTAMPTZ | NOT NULL DEFAULT NOW() |

**Primary key**: (rule_id, ok_version, agreement_code)

Introduced: Sprint 2

### wage_type_mappings

Purpose: Maps time types to SLS wage codes, versioned per OK agreement and optionally per position.

| Column | Type | Constraints |
|--------|------|-------------|
| time_type | TEXT | NOT NULL |
| wage_type | TEXT | NOT NULL |
| ok_version | TEXT | NOT NULL |
| agreement_code | TEXT | NOT NULL |
| position | TEXT | NOT NULL DEFAULT '' |
| description | TEXT | |
| created_at | TIMESTAMPTZ | NOT NULL DEFAULT NOW() |

**Primary key**: (time_type, ok_version, agreement_code, position)

Introduced: Sprint 2 (position column added Sprint 11)

---

## 4. Flex & Holidays

### flex_balance_snapshots

Purpose: Point-in-time snapshots of employee flex balances.

| Column | Type | Constraints |
|--------|------|-------------|
| snapshot_id | UUID | PRIMARY KEY DEFAULT gen_random_uuid() |
| employee_id | TEXT | NOT NULL |
| period_start | DATE | NOT NULL |
| period_end | DATE | NOT NULL |
| balance_hours | DECIMAL | NOT NULL |
| delta | DECIMAL | NOT NULL |
| ok_version | TEXT | NOT NULL |
| agreement_code | TEXT | NOT NULL |
| created_at | TIMESTAMPTZ | NOT NULL DEFAULT NOW() |

**Indexes**:
- idx_flex_snapshots_employee ON flex_balance_snapshots(employee_id)
- idx_flex_snapshots_period ON flex_balance_snapshots(period_start, period_end)

Introduced: Sprint 2

### danish_public_holidays

Purpose: Reference table for Danish public holidays (pre-computed via Computus algorithm).

| Column | Type | Constraints |
|--------|------|-------------|
| holiday_date | DATE | NOT NULL |
| holiday_name | TEXT | NOT NULL |
| ok_version | TEXT | NOT NULL |

**Primary key**: (holiday_date, ok_version)

Introduced: Sprint 2

---

## 5. Audit

### audit_log

Purpose: Append-only audit log for all HTTP requests and security events.

| Column | Type | Constraints |
|--------|------|-------------|
| log_id | BIGSERIAL | PRIMARY KEY |
| timestamp | TIMESTAMPTZ | NOT NULL DEFAULT NOW() |
| actor_id | TEXT | |
| actor_role | TEXT | |
| action | TEXT | NOT NULL |
| resource | TEXT | NOT NULL |
| resource_id | TEXT | |
| correlation_id | UUID | |
| http_method | TEXT | |
| http_path | TEXT | |
| http_status | INT | |
| result | TEXT | NOT NULL DEFAULT 'success' |
| details | JSONB | |
| ip_address | TEXT | |

**Indexes**:
- idx_audit_log_actor ON audit_log(actor_id)
- idx_audit_log_correlation ON audit_log(correlation_id)
- idx_audit_log_timestamp ON audit_log(timestamp)

Introduced: Sprint 3

---

## 6. Organizations & Users

### organizations

Purpose: Organizational hierarchy (Ministry -> Styrelse -> Afdeling -> Team) with materialized path.

| Column | Type | Constraints |
|--------|------|-------------|
| org_id | TEXT | PRIMARY KEY |
| org_name | TEXT | NOT NULL |
| org_type | TEXT | NOT NULL, CHECK IN ('MINISTRY', 'STYRELSE', 'AFDELING', 'TEAM') |
| parent_org_id | TEXT | FK -> organizations(org_id) |
| materialized_path | TEXT | NOT NULL |
| agreement_code | TEXT | NOT NULL DEFAULT 'AC' |
| ok_version | TEXT | NOT NULL DEFAULT 'OK24' |
| is_active | BOOLEAN | NOT NULL DEFAULT TRUE |
| created_at | TIMESTAMPTZ | NOT NULL DEFAULT NOW() |
| updated_at | TIMESTAMPTZ | NOT NULL DEFAULT NOW() |

**Indexes**:
- idx_org_parent ON organizations(parent_org_id)
- idx_org_path ON organizations USING btree (materialized_path text_pattern_ops)
- idx_org_type ON organizations(org_type)

Introduced: Sprint 6

### users

Purpose: User accounts with org membership and agreement assignment.

| Column | Type | Constraints |
|--------|------|-------------|
| user_id | TEXT | PRIMARY KEY |
| username | TEXT | NOT NULL UNIQUE |
| password_hash | TEXT | NOT NULL |
| display_name | TEXT | NOT NULL |
| email | TEXT | |
| primary_org_id | TEXT | NOT NULL, FK -> organizations(org_id) |
| agreement_code | TEXT | NOT NULL DEFAULT 'AC' |
| ok_version | TEXT | NOT NULL DEFAULT 'OK24' |
| employment_category | TEXT | NOT NULL DEFAULT 'Standard' |
| is_active | BOOLEAN | NOT NULL DEFAULT TRUE |
| created_at | TIMESTAMPTZ | NOT NULL DEFAULT NOW() |
| updated_at | TIMESTAMPTZ | NOT NULL DEFAULT NOW() |

**Indexes**:
- idx_users_org ON users(primary_org_id)
- idx_users_username ON users(username)

Introduced: Sprint 6

### roles

Purpose: Role definitions for RBAC (5 roles with hierarchy levels).

| Column | Type | Constraints |
|--------|------|-------------|
| role_id | TEXT | PRIMARY KEY |
| role_name | TEXT | NOT NULL |
| description | TEXT | |
| hierarchy_level | INT | NOT NULL |
| created_at | TIMESTAMPTZ | NOT NULL DEFAULT NOW() |

Introduced: Sprint 6

### role_assignments

Purpose: Assigns roles to users with organizational scope.

| Column | Type | Constraints |
|--------|------|-------------|
| assignment_id | UUID | PRIMARY KEY DEFAULT gen_random_uuid() |
| user_id | TEXT | NOT NULL, FK -> users(user_id) |
| role_id | TEXT | NOT NULL, FK -> roles(role_id) |
| org_id | TEXT | FK -> organizations(org_id) |
| scope_type | TEXT | NOT NULL, CHECK IN ('GLOBAL', 'ORG_ONLY', 'ORG_AND_DESCENDANTS') |
| assigned_by | TEXT | NOT NULL |
| assigned_at | TIMESTAMPTZ | NOT NULL DEFAULT NOW() |
| expires_at | TIMESTAMPTZ | |
| is_active | BOOLEAN | NOT NULL DEFAULT TRUE |

**Unique constraint**: (user_id, role_id, org_id)

**Indexes**:
- idx_role_assignments_user ON role_assignments(user_id)
- idx_role_assignments_org ON role_assignments(org_id)
- idx_role_assignments_role ON role_assignments(role_id)

Introduced: Sprint 6

### role_assignment_audit

Purpose: Append-only audit trail for role assignment changes.

| Column | Type | Constraints |
|--------|------|-------------|
| audit_id | BIGSERIAL | PRIMARY KEY |
| assignment_id | UUID | NOT NULL |
| action | TEXT | NOT NULL, CHECK IN ('GRANTED', 'REVOKED', 'EXPIRED', 'MODIFIED') |
| actor_id | TEXT | NOT NULL |
| actor_role | TEXT | NOT NULL |
| details | JSONB | |
| timestamp | TIMESTAMPTZ | NOT NULL DEFAULT NOW() |

**Indexes**:
- idx_role_audit_assignment ON role_assignment_audit(assignment_id)

Introduced: Sprint 6

---

## 7. Configuration

### local_configurations

Purpose: Local configuration overrides per org unit, validated against central constraints.

| Column | Type | Constraints |
|--------|------|-------------|
| config_id | UUID | PRIMARY KEY DEFAULT gen_random_uuid() |
| org_id | TEXT | NOT NULL, FK -> organizations(org_id) |
| config_area | TEXT | NOT NULL, CHECK IN ('WORKING_TIME', 'FLEX_RULES', 'ORG_STRUCTURE', 'LOCAL_AGREEMENT', 'OPERATIONAL') |
| config_key | TEXT | NOT NULL |
| config_value | JSONB | NOT NULL |
| effective_from | DATE | NOT NULL |
| effective_to | DATE | |
| version | INT | NOT NULL DEFAULT 1 |
| agreement_code | TEXT | NOT NULL |
| ok_version | TEXT | NOT NULL |
| created_by | TEXT | NOT NULL |
| approved_by | TEXT | |
| approved_at | TIMESTAMPTZ | |
| is_active | BOOLEAN | NOT NULL DEFAULT TRUE |
| created_at | TIMESTAMPTZ | NOT NULL DEFAULT NOW() |

**Unique constraint**: (org_id, config_area, config_key, effective_from, agreement_code, ok_version)

**Indexes**:
- idx_local_config_org ON local_configurations(org_id)
- idx_local_config_area ON local_configurations(config_area)

Introduced: Sprint 7

### local_configuration_audit

Purpose: Append-only audit trail for local configuration changes.

| Column | Type | Constraints |
|--------|------|-------------|
| audit_id | BIGSERIAL | PRIMARY KEY |
| config_id | UUID | NOT NULL |
| action | TEXT | NOT NULL, CHECK IN ('CREATED', 'MODIFIED', 'DEACTIVATED', 'APPROVED') |
| previous_value | JSONB | |
| new_value | JSONB | |
| actor_id | TEXT | NOT NULL |
| actor_role | TEXT | NOT NULL |
| timestamp | TIMESTAMPTZ | NOT NULL DEFAULT NOW() |

**Indexes**:
- idx_local_config_audit_config ON local_configuration_audit(config_id)

Introduced: Sprint 7

---

## 8. Approval

### approval_periods

Purpose: Period approval workflow with two-step employee/manager approval.

| Column | Type | Constraints |
|--------|------|-------------|
| period_id | UUID | PRIMARY KEY DEFAULT gen_random_uuid() |
| employee_id | TEXT | NOT NULL |
| org_id | TEXT | NOT NULL, FK -> organizations(org_id) |
| period_start | DATE | NOT NULL |
| period_end | DATE | NOT NULL |
| period_type | TEXT | NOT NULL, CHECK IN ('WEEKLY', 'MONTHLY') |
| status | TEXT | NOT NULL DEFAULT 'DRAFT', CHECK IN ('DRAFT', 'EMPLOYEE_APPROVED', 'SUBMITTED', 'APPROVED', 'REJECTED') |
| submitted_at | TIMESTAMPTZ | |
| submitted_by | TEXT | |
| approved_by | TEXT | |
| approved_at | TIMESTAMPTZ | |
| rejection_reason | TEXT | |
| agreement_code | TEXT | NOT NULL |
| ok_version | TEXT | NOT NULL |
| created_at | TIMESTAMPTZ | NOT NULL DEFAULT NOW() |
| employee_approved_at | TIMESTAMPTZ | (added S9) |
| employee_approved_by | TEXT | (added S9) |
| employee_deadline | DATE | (added S9) |
| manager_deadline | DATE | (added S9) |

**Unique constraint**: (employee_id, period_start, period_end)

**Indexes**:
- idx_approval_employee ON approval_periods(employee_id)
- idx_approval_org ON approval_periods(org_id)
- idx_approval_status ON approval_periods(status)
- idx_approval_period ON approval_periods(period_start, period_end)

Introduced: Sprint 6 (extended Sprint 9 with employee approval and deadlines)

### approval_audit

Purpose: Append-only audit trail for period approval state transitions.

| Column | Type | Constraints |
|--------|------|-------------|
| audit_id | BIGSERIAL | PRIMARY KEY |
| period_id | UUID | NOT NULL |
| action | TEXT | NOT NULL, CHECK IN ('CREATED', 'SUBMITTED', 'APPROVED', 'REJECTED', 'REOPENED') |
| actor_id | TEXT | NOT NULL |
| actor_role | TEXT | NOT NULL |
| comment | TEXT | |
| timestamp | TIMESTAMPTZ | NOT NULL DEFAULT NOW() |

**Indexes**:
- idx_approval_audit_period ON approval_audit(period_id)

Introduced: Sprint 6 (REOPENED action added Sprint 9)

---

## 9. Skema

### projects

Purpose: Project codes configurable per org unit for time registration.

| Column | Type | Constraints |
|--------|------|-------------|
| project_id | UUID | PRIMARY KEY DEFAULT gen_random_uuid() |
| org_id | TEXT | NOT NULL, FK -> organizations(org_id) |
| project_code | TEXT | NOT NULL |
| project_name | TEXT | NOT NULL |
| is_active | BOOLEAN | NOT NULL DEFAULT TRUE |
| sort_order | INT | NOT NULL DEFAULT 0 |
| created_by | TEXT | NOT NULL |
| created_at | TIMESTAMPTZ | NOT NULL DEFAULT NOW() |

**Unique constraint**: (org_id, project_code)

**Indexes**:
- idx_projects_org ON projects(org_id)

Introduced: Sprint 9

### timer_sessions

Purpose: Check-in/check-out timer for automatic arrival/departure tracking.

| Column | Type | Constraints |
|--------|------|-------------|
| session_id | UUID | PRIMARY KEY DEFAULT gen_random_uuid() |
| employee_id | TEXT | NOT NULL |
| date | DATE | NOT NULL |
| check_in_at | TIMESTAMPTZ | NOT NULL |
| check_out_at | TIMESTAMPTZ | |
| is_active | BOOLEAN | NOT NULL DEFAULT TRUE |
| created_at | TIMESTAMPTZ | NOT NULL DEFAULT NOW() |

**Unique constraint**: (employee_id, date)

**Indexes**:
- idx_timer_employee ON timer_sessions(employee_id)
- idx_timer_active ON timer_sessions(is_active) WHERE is_active = TRUE (partial)

Introduced: Sprint 9

### absence_type_visibility

Purpose: Per-org visibility control for absence types (LocalAdmin can hide types).

| Column | Type | Constraints |
|--------|------|-------------|
| id | UUID | PRIMARY KEY DEFAULT gen_random_uuid() |
| org_id | TEXT | NOT NULL, FK -> organizations(org_id) |
| absence_type | TEXT | NOT NULL |
| is_hidden | BOOLEAN | NOT NULL DEFAULT FALSE |
| set_by | TEXT | NOT NULL |
| set_at | TIMESTAMPTZ | NOT NULL DEFAULT NOW() |

**Unique constraint**: (org_id, absence_type)

**Indexes**:
- idx_absence_vis_org ON absence_type_visibility(org_id)

Introduced: Sprint 9

---

## 10. Positions

### positions

Purpose: Position registry for AC agreement position codes.

| Column | Type | Constraints |
|--------|------|-------------|
| position_code | TEXT | PRIMARY KEY |
| display_label | TEXT | NOT NULL |
| agreement_code | TEXT | NOT NULL |
| is_active | BOOLEAN | NOT NULL DEFAULT true |
| created_at | TIMESTAMPTZ | NOT NULL DEFAULT NOW() |

Introduced: Sprint 11

---

## 11. Agreement Configs

### agreement_configs

Purpose: Database-backed agreement rule configurations with lifecycle (DRAFT -> ACTIVE -> ARCHIVED). ADR-014.

| Column | Type | Constraints |
|--------|------|-------------|
| config_id | UUID | PRIMARY KEY DEFAULT gen_random_uuid() |
| agreement_code | TEXT | NOT NULL |
| ok_version | TEXT | NOT NULL |
| status | TEXT | NOT NULL DEFAULT 'DRAFT', CHECK IN ('DRAFT', 'ACTIVE', 'ARCHIVED') |
| weekly_norm_hours | DECIMAL | NOT NULL |
| norm_period_weeks | INT | NOT NULL DEFAULT 1 |
| norm_model | TEXT | NOT NULL DEFAULT 'WEEKLY_HOURS' |
| annual_norm_hours | DECIMAL | NOT NULL DEFAULT 1924 |
| max_flex_balance | DECIMAL | NOT NULL |
| flex_carryover_max | DECIMAL | NOT NULL |
| has_overtime | BOOLEAN | NOT NULL |
| has_merarbejde | BOOLEAN | NOT NULL |
| overtime_threshold_50 | DECIMAL | NOT NULL DEFAULT 37.0 |
| overtime_threshold_100 | DECIMAL | NOT NULL DEFAULT 40.0 |
| evening_supplement_enabled | BOOLEAN | NOT NULL DEFAULT FALSE |
| night_supplement_enabled | BOOLEAN | NOT NULL DEFAULT FALSE |
| weekend_supplement_enabled | BOOLEAN | NOT NULL DEFAULT FALSE |
| holiday_supplement_enabled | BOOLEAN | NOT NULL DEFAULT FALSE |
| evening_start | INT | NOT NULL DEFAULT 17 |
| evening_end | INT | NOT NULL DEFAULT 23 |
| night_start | INT | NOT NULL DEFAULT 23 |
| night_end | INT | NOT NULL DEFAULT 6 |
| evening_rate | DECIMAL | NOT NULL DEFAULT 1.25 |
| night_rate | DECIMAL | NOT NULL DEFAULT 1.50 |
| weekend_saturday_rate | DECIMAL | NOT NULL DEFAULT 1.50 |
| weekend_sunday_rate | DECIMAL | NOT NULL DEFAULT 2.0 |
| holiday_rate | DECIMAL | NOT NULL DEFAULT 2.0 |
| on_call_duty_enabled | BOOLEAN | NOT NULL DEFAULT FALSE |
| on_call_duty_rate | DECIMAL | NOT NULL DEFAULT 0.33 |
| call_in_work_enabled | BOOLEAN | NOT NULL DEFAULT FALSE |
| call_in_minimum_hours | DECIMAL | NOT NULL DEFAULT 3.0 |
| call_in_rate | DECIMAL | NOT NULL DEFAULT 1.0 |
| travel_time_enabled | BOOLEAN | NOT NULL DEFAULT FALSE |
| working_travel_rate | DECIMAL | NOT NULL DEFAULT 1.0 |
| non_working_travel_rate | DECIMAL | NOT NULL DEFAULT 0.5 |
| created_by | TEXT | NOT NULL DEFAULT 'SYSTEM_SEED' |
| created_at | TIMESTAMPTZ | NOT NULL DEFAULT NOW() |
| updated_at | TIMESTAMPTZ | NOT NULL DEFAULT NOW() |
| published_at | TIMESTAMPTZ | |
| archived_at | TIMESTAMPTZ | |
| cloned_from_id | UUID | FK -> agreement_configs(config_id) |
| description | TEXT | |

**Indexes**:
- idx_agreement_configs_active ON agreement_configs (agreement_code, ok_version) WHERE status = 'ACTIVE' (partial unique)
- idx_agreement_configs_code_version ON agreement_configs (agreement_code, ok_version)
- idx_agreement_configs_status ON agreement_configs (status)

Introduced: Sprint 12

### agreement_config_audit

Purpose: Append-only audit trail for agreement config lifecycle changes.

| Column | Type | Constraints |
|--------|------|-------------|
| audit_id | BIGSERIAL | PRIMARY KEY |
| config_id | UUID | NOT NULL |
| action | TEXT | NOT NULL, CHECK IN ('CREATED', 'UPDATED', 'PUBLISHED', 'ARCHIVED', 'CLONED') |
| previous_data | JSONB | |
| new_data | JSONB | |
| actor_id | TEXT | NOT NULL |
| actor_role | TEXT | NOT NULL |
| timestamp | TIMESTAMPTZ | NOT NULL DEFAULT NOW() |

**Indexes**:
- idx_agreement_config_audit_config ON agreement_config_audit(config_id)

Introduced: Sprint 12

---

## 12. Position Overrides

### position_override_configs

Purpose: Database-backed position override configurations (partial config overrides per position).

| Column | Type | Constraints |
|--------|------|-------------|
| override_id | UUID | PRIMARY KEY DEFAULT gen_random_uuid() |
| agreement_code | TEXT | NOT NULL |
| ok_version | TEXT | NOT NULL |
| position_code | TEXT | NOT NULL, FK -> positions(position_code) |
| status | TEXT | NOT NULL DEFAULT 'ACTIVE', CHECK IN ('ACTIVE', 'INACTIVE') |
| max_flex_balance | DECIMAL | |
| flex_carryover_max | DECIMAL | |
| norm_period_weeks | INT | |
| weekly_norm_hours | DECIMAL | |
| created_by | TEXT | NOT NULL DEFAULT 'SYSTEM_SEED' |
| created_at | TIMESTAMPTZ | NOT NULL DEFAULT NOW() |
| updated_at | TIMESTAMPTZ | NOT NULL DEFAULT NOW() |
| description | TEXT | |

**Indexes**:
- idx_position_override_active_unique ON position_override_configs (agreement_code, ok_version, position_code) WHERE status = 'ACTIVE' (partial unique)

Introduced: Sprint 14

### position_override_config_audit

Purpose: Append-only audit trail for position override config changes.

| Column | Type | Constraints |
|--------|------|-------------|
| audit_id | BIGSERIAL | PRIMARY KEY |
| override_id | UUID | NOT NULL |
| action | TEXT | NOT NULL, CHECK IN ('CREATED', 'UPDATED', 'ACTIVATED', 'DEACTIVATED') |
| previous_data | JSONB | |
| new_data | JSONB | |
| actor_id | TEXT | NOT NULL |
| actor_role | TEXT | NOT NULL |
| timestamp | TIMESTAMPTZ | NOT NULL DEFAULT NOW() |

Introduced: Sprint 14

### wage_type_mapping_audit

Purpose: Append-only audit trail for wage type mapping changes.

| Column | Type | Constraints |
|--------|------|-------------|
| audit_id | BIGSERIAL | PRIMARY KEY |
| time_type | TEXT | NOT NULL |
| ok_version | TEXT | NOT NULL |
| agreement_code | TEXT | NOT NULL |
| position | TEXT | NOT NULL DEFAULT '' |
| action | TEXT | NOT NULL, CHECK IN ('CREATED', 'UPDATED', 'DELETED') |
| previous_data | JSONB | |
| new_data | JSONB | |
| actor_id | TEXT | NOT NULL |
| actor_role | TEXT | NOT NULL |
| timestamp | TIMESTAMPTZ | NOT NULL DEFAULT NOW() |

Introduced: Sprint 14

---

## 13. Entitlements

### entitlement_configs

Purpose: Entitlement type definitions per agreement and OK version (vacation, care days, etc.).

| Column | Type | Constraints |
|--------|------|-------------|
| config_id | UUID | PRIMARY KEY DEFAULT gen_random_uuid() |
| entitlement_type | TEXT | NOT NULL |
| agreement_code | TEXT | NOT NULL |
| ok_version | TEXT | NOT NULL |
| annual_quota | DECIMAL | NOT NULL |
| accrual_model | TEXT | NOT NULL DEFAULT 'IMMEDIATE' |
| reset_month | INT | NOT NULL DEFAULT 1 |
| carryover_max | DECIMAL | NOT NULL DEFAULT 0 |
| pro_rate_by_part_time | BOOLEAN | NOT NULL DEFAULT true |
| is_per_episode | BOOLEAN | NOT NULL DEFAULT false |
| min_age | INT | |
| description | TEXT | |
| created_at | TIMESTAMPTZ | NOT NULL DEFAULT NOW() |

**Unique constraint**: (entitlement_type, agreement_code, ok_version)

Introduced: Sprint 15

### entitlement_balances

Purpose: Per-employee entitlement balance tracking (used, planned, carryover).

| Column | Type | Constraints |
|--------|------|-------------|
| balance_id | UUID | PRIMARY KEY DEFAULT gen_random_uuid() |
| employee_id | TEXT | NOT NULL |
| entitlement_type | TEXT | NOT NULL |
| entitlement_year | INT | NOT NULL |
| total_quota | DECIMAL | NOT NULL |
| used | DECIMAL | NOT NULL DEFAULT 0 |
| planned | DECIMAL | NOT NULL DEFAULT 0 |
| carryover_in | DECIMAL | NOT NULL DEFAULT 0 |
| updated_at | TIMESTAMPTZ | NOT NULL DEFAULT NOW() |

**Unique constraint**: (employee_id, entitlement_type, entitlement_year)

Introduced: Sprint 15

---

## Table Summary

| # | Table | Domain | Sprint | Audit Table |
|---|-------|--------|--------|-------------|
| 1 | event_streams | Event Store | S2 | -- |
| 2 | events | Event Store | S2 (S3) | -- |
| 3 | outbox_messages | Outbox | S2 | -- |
| 4 | orchestrator_tasks | Orchestration | S2 | -- |
| 5 | rule_versions | Rules | S2 | -- |
| 6 | wage_type_mappings | Wage Types | S2 (S11) | wage_type_mapping_audit (S14) |
| 7 | flex_balance_snapshots | Flex | S2 | -- |
| 8 | danish_public_holidays | Holidays | S2 | -- |
| 9 | audit_log | Audit | S3 | -- (is audit) |
| 10 | organizations | Org | S6 | -- |
| 11 | users | Users | S6 | -- |
| 12 | roles | RBAC | S6 | -- |
| 13 | role_assignments | RBAC | S6 | role_assignment_audit (S6) |
| 14 | role_assignment_audit | RBAC Audit | S6 | -- (is audit) |
| 15 | local_configurations | Config | S7 | local_configuration_audit (S7) |
| 16 | local_configuration_audit | Config Audit | S7 | -- (is audit) |
| 17 | approval_periods | Approval | S6 (S9) | approval_audit (S6) |
| 18 | approval_audit | Approval Audit | S6 (S9) | -- (is audit) |
| 19 | projects | Skema | S9 | -- |
| 20 | timer_sessions | Skema | S9 | -- |
| 21 | absence_type_visibility | Skema | S9 | -- |
| 22 | positions | Positions | S11 | -- |
| 23 | agreement_configs | Agreement Configs | S12 | agreement_config_audit (S12) |
| 24 | agreement_config_audit | Agreement Config Audit | S12 | -- (is audit) |
| 25 | position_override_configs | Position Overrides | S14 | position_override_config_audit (S14) |
| 26 | position_override_config_audit | Position Override Audit | S14 | -- (is audit) |
| 27 | wage_type_mapping_audit | Wage Type Audit | S14 | -- (is audit) |
| 28 | entitlement_configs | Entitlements | S15 | -- |
| 29 | entitlement_balances | Entitlements | S15 | -- |

**Total: 29 tables** (11 audit tables, 18 primary tables)
