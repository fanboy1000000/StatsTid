# StatsTid Database Schema

> **GENERATED FILE — do not edit by hand.**
> Produced by `tools/generate_db_schema.py` from `docker/postgres/init.sql`.
> Update the schema in `init.sql`, then run `python tools/generate_db_schema.py`.
> CI fails (`tools/check_docs.py`) if this file drifts from init.sql.

**Total: 67 tables** (50 primary, 17 audit).

---

## schema_migrations

| Column | Type | Null | Key | Default |
|--------|------|------|-----|---------|
| migration_id | TEXT | No | PK |  |
| applied_at | TIMESTAMPTZ | No |  | NOW() |
| notes | TEXT | Yes |  |  |

## event_streams

| Column | Type | Null | Key | Default |
|--------|------|------|-----|---------|
| stream_id | TEXT | No | PK |  |
| created_at | TIMESTAMPTZ | No |  | NOW() |

## events

| Column | Type | Null | Key | Default |
|--------|------|------|-----|---------|
| global_position | BIGSERIAL | No | PK |  |
| event_id | UUID | No | UNIQUE |  |
| stream_id | TEXT | No | FK→event_streams |  |
| stream_version | INT | No |  |  |
| event_type | TEXT | No |  |  |
| data | JSONB | No |  |  |
| occurred_at | TIMESTAMPTZ | No |  |  |
| stored_at | TIMESTAMPTZ | No |  | NOW() |
| actor_id | TEXT | Yes |  |  |
| actor_role | TEXT | Yes |  |  |
| correlation_id | UUID | Yes |  |  |

**Table constraints:**
- UNIQUE (stream_id, stream_version)

**Indexes:**
- `idx_events_stream_id` on (stream_id)
- `idx_events_event_type` on (event_type)
- `idx_events_occurred_at` on (occurred_at)
- `idx_events_actor_id` on (actor_id)
- `idx_events_correlation_id` on (correlation_id)

## outbox_events

| Column | Type | Null | Key | Default |
|--------|------|------|-----|---------|
| outbox_id | BIGSERIAL | No | PK |  |
| service_id | TEXT | No |  |  |
| stream_id | TEXT | No |  |  |
| event_id | UUID | No | UNIQUE |  |
| event_type | TEXT | No |  |  |
| event_payload | JSONB | No |  |  |
| correlation_id | TEXT | Yes |  |  |
| actor_id | TEXT | Yes |  |  |
| actor_role | TEXT | Yes |  |  |
| created_at | TIMESTAMPTZ | No |  | NOW() |
| published_at | TIMESTAMPTZ | Yes |  |  |
| stream_version | INT | Yes |  |  |
| attempts | INT | No |  | 0 |
| last_error | TEXT | Yes |  |  |
| last_attempt_at | TIMESTAMPTZ | Yes |  |  |

**Indexes:**
- `idx_outbox_unpublished` on (service_id, outbox_id) WHERE published_at IS NULL
- `idx_outbox_attempts` on (service_id, attempts, last_attempt_at) WHERE published_at IS NULL AND attempts > 0
- `idx_outbox_stream` on (stream_id, outbox_id) WHERE published_at IS NULL

## outbox_messages

| Column | Type | Null | Key | Default |
|--------|------|------|-----|---------|
| message_id | UUID | No | PK | gen_random_uuid() |
| destination | TEXT | No |  |  |
| payload | JSONB | No |  |  |
| status | TEXT | No |  | 'pending' |
| attempt_count | INT | No |  | 0 |
| created_at | TIMESTAMPTZ | No |  | NOW() |
| last_attempt_at | TIMESTAMPTZ | Yes |  |  |
| delivered_at | TIMESTAMPTZ | Yes |  |  |
| error_message | TEXT | Yes |  |  |
| idempotency_token | UUID | Yes | UNIQUE |  |

**Indexes:**
- `idx_outbox_status` on (status)
- `idx_outbox_created_at` on (created_at)
- `idx_outbox_destination_status` on (destination, status)

## orchestrator_tasks

| Column | Type | Null | Key | Default |
|--------|------|------|-----|---------|
| task_id | UUID | No | PK | gen_random_uuid() |
| task_type | TEXT | No |  |  |
| status | TEXT | No |  | 'pending' |
| input_data | JSONB | Yes |  |  |
| output_data | JSONB | Yes |  |  |
| assigned_agent | TEXT | Yes |  |  |
| created_at | TIMESTAMPTZ | No |  | NOW() |
| started_at | TIMESTAMPTZ | Yes |  |  |
| completed_at | TIMESTAMPTZ | Yes |  |  |
| error_message | TEXT | Yes |  |  |

**Indexes:**
- `idx_orch_tasks_status` on (status)

## rule_versions

| Column | Type | Null | Key | Default |
|--------|------|------|-----|---------|
| rule_id | TEXT | No |  |  |
| ok_version | TEXT | No |  |  |
| rule_name | TEXT | No |  |  |
| agreement_code | TEXT | No |  |  |
| effective_from | DATE | No |  |  |
| effective_to | DATE | Yes |  |  |
| created_at | TIMESTAMPTZ | No |  | NOW() |

**Table constraints:**
- PRIMARY KEY (rule_id, ok_version, agreement_code)

## wage_type_mappings

| Column | Type | Null | Key | Default |
|--------|------|------|-----|---------|
| mapping_id | UUID | No |  | gen_random_uuid() |
| time_type | TEXT | No |  |  |
| wage_type | TEXT | No |  |  |
| ok_version | TEXT | No |  |  |
| agreement_code | TEXT | No |  |  |
| position | TEXT | No |  | '' |
| description | TEXT | Yes |  |  |
| effective_from | DATE | No |  |  |
| effective_to | DATE | Yes |  |  |
| created_at | TIMESTAMPTZ | No |  | NOW() |
| version | BIGINT | No |  | 1 |

**Table constraints:**
- PRIMARY KEY (mapping_id)

**Indexes:**
- `idx_wtm_natural_key_open` (UNIQUE) on (time_type, ok_version, agreement_code, position) WHERE effective_to IS NULL
- `idx_wtm_natural_key_history` (UNIQUE) on (time_type, ok_version, agreement_code, position, effective_from)
- `idx_wtm_natural_key_open` (UNIQUE) on (time_type, ok_version, agreement_code, position) WHERE effective_to IS NULL
- `idx_wtm_natural_key_history` (UNIQUE) on (time_type, ok_version, agreement_code, position, effective_from)

## flex_balance_snapshots

| Column | Type | Null | Key | Default |
|--------|------|------|-----|---------|
| snapshot_id | UUID | No | PK | gen_random_uuid() |
| employee_id | TEXT | No |  |  |
| period_start | DATE | No |  |  |
| period_end | DATE | No |  |  |
| balance_hours | DECIMAL | No |  |  |
| delta | DECIMAL | No |  |  |
| ok_version | TEXT | No |  |  |
| agreement_code | TEXT | No |  |  |
| created_at | TIMESTAMPTZ | No |  | NOW() |

**Indexes:**
- `idx_flex_snapshots_employee` on (employee_id)
- `idx_flex_snapshots_period` on (period_start, period_end)

## danish_public_holidays

| Column | Type | Null | Key | Default |
|--------|------|------|-----|---------|
| holiday_date | DATE | No |  |  |
| holiday_name | TEXT | No |  |  |
| ok_version | TEXT | No |  |  |

**Table constraints:**
- PRIMARY KEY (holiday_date, ok_version)

## audit_log

| Column | Type | Null | Key | Default |
|--------|------|------|-----|---------|
| log_id | BIGSERIAL | No | PK |  |
| timestamp | TIMESTAMPTZ | No |  | NOW() |
| actor_id | TEXT | Yes |  |  |
| actor_role | TEXT | Yes |  |  |
| action | TEXT | No |  |  |
| resource | TEXT | No |  |  |
| resource_id | TEXT | Yes |  |  |
| correlation_id | UUID | Yes |  |  |
| http_method | TEXT | Yes |  |  |
| http_path | TEXT | Yes |  |  |
| http_status | INT | Yes |  |  |
| result | TEXT | No |  | 'success' |
| details | JSONB | Yes |  |  |
| ip_address | TEXT | Yes |  |  |

**Indexes:**
- `idx_audit_log_actor` on (actor_id)
- `idx_audit_log_correlation` on (correlation_id)
- `idx_audit_log_timestamp` on (timestamp)

## organizations

| Column | Type | Null | Key | Default |
|--------|------|------|-----|---------|
| org_id | TEXT | No | PK |  |
| org_name | TEXT | No |  |  |
| org_type | TEXT | No |  |  |
| parent_org_id | TEXT | Yes | FK→organizations |  |
| materialized_path | TEXT | No |  |  |
| agreement_code | TEXT | No |  | 'AC' |
| ok_version | TEXT | No |  | 'OK24' |
| is_active | BOOLEAN | No |  | TRUE |
| created_at | TIMESTAMPTZ | No |  | NOW() |
| updated_at | TIMESTAMPTZ | No |  | NOW() |

**Indexes:**
- `idx_org_parent` on (parent_org_id)
- `idx_org_path` on (materialized_path text_pattern_ops)
- `idx_org_type` on (org_type)

## users

| Column | Type | Null | Key | Default |
|--------|------|------|-----|---------|
| user_id | TEXT | No | PK |  |
| username | TEXT | No | UNIQUE |  |
| password_hash | TEXT | No |  |  |
| display_name | TEXT | No |  |  |
| email | TEXT | Yes |  |  |
| primary_org_id | TEXT | No | FK→organizations |  |
| agreement_code | TEXT | No |  | 'AC' |
| ok_version | TEXT | No |  | 'OK24' |
| employment_category | TEXT | No |  | 'Standard' |
| is_active | BOOLEAN | No |  | TRUE |
| employment_end_date | DATE | Yes |  |  |
| end_date_deactivated | BOOLEAN | No |  | FALSE |
| version | BIGINT | No |  | 1 |
| created_at | TIMESTAMPTZ | No |  | NOW() |
| updated_at | TIMESTAMPTZ | No |  | NOW() |
| birth_date | DATE | Yes |  |  |
| employment_start_date | DATE | Yes |  |  |

**Indexes:**
- `idx_users_org` on (primary_org_id)
- `idx_users_username` on (username)

## employee_profiles

| Column | Type | Null | Key | Default |
|--------|------|------|-----|---------|
| profile_id | UUID | No | PK | gen_random_uuid() |
| employee_id | TEXT | No | FK→users |  |
| part_time_fraction | NUMERIC(4,3) | No |  | 1.000 |
| position | TEXT | Yes |  |  |
| effective_from | DATE | No |  | '0001-01-01' |
| effective_to | DATE | Yes |  |  |
| version | BIGINT | No |  | 1 |
| created_at | TIMESTAMPTZ | No |  | NOW() |
| updated_at | TIMESTAMPTZ | No |  | NOW() |
| enhed_label | TEXT | Yes |  |  |

**Indexes:**
- `idx_employee_profiles_live` (UNIQUE) on (employee_id) WHERE effective_to IS NULL
- `idx_employee_profiles_history` (UNIQUE) on (employee_id, effective_from)

## employee_profile_audit

| Column | Type | Null | Key | Default |
|--------|------|------|-----|---------|
| audit_id | BIGSERIAL | No | PK |  |
| profile_id | UUID | No |  |  |
| employee_id | TEXT | No |  |  |
| action | TEXT | No |  |  |
| previous_data | JSONB | Yes |  |  |
| new_data | JSONB | Yes |  |  |
| version_before | BIGINT | Yes |  |  |
| version_after | BIGINT | Yes |  |  |
| actor_id | TEXT | No |  |  |
| actor_role | TEXT | No |  |  |
| timestamp | TIMESTAMPTZ | No |  | NOW() |

**Indexes:**
- `idx_employee_profile_audit_profile_id` on (profile_id)
- `idx_employee_profile_audit_employee_id` on (employee_id)

## enheder

| Column | Type | Null | Key | Default |
|--------|------|------|-----|---------|
| enhed_id | UUID | No | PK | gen_random_uuid() |
| organisation_id | TEXT | No | FK→organizations |  |
| name | TEXT | No |  |  |
| deleted_at | TIMESTAMPTZ | Yes |  |  |
| version | BIGINT | No |  | 1 |
| created_at | TIMESTAMPTZ | No |  | NOW() |

**Indexes:**
- `idx_enheder_active_name` (UNIQUE) on (organisation_id, lower(name) WHERE deleted_at IS NULL
- `idx_enheder_org` on (organisation_id)

## user_enheder

| Column | Type | Null | Key | Default |
|--------|------|------|-----|---------|
| user_id | TEXT | No | FK→users |  |
| enhed_id | UUID | No | FK→enheder |  |

**Table constraints:**
- PRIMARY KEY (user_id, enhed_id)

**Indexes:**
- `idx_user_enheder_enhed` on (enhed_id)

## user_agreement_codes

| Column | Type | Null | Key | Default |
|--------|------|------|-----|---------|
| assignment_id | UUID | No | PK |  |
| user_id | TEXT | No | FK→users |  |
| agreement_code | TEXT | No |  |  |
| effective_from | DATE | No |  | '0001-01-01' |
| effective_to | DATE | Yes |  |  |
| version | BIGINT | No |  | 1 |
| created_at | TIMESTAMPTZ | No |  | NOW() |
| updated_at | TIMESTAMPTZ | No |  | NOW() |

**Indexes:**
- `idx_user_agreement_codes_live` (UNIQUE) on (user_id) WHERE effective_to IS NULL
- `idx_user_agreement_codes_history` (UNIQUE) on (user_id, effective_from)

## user_agreement_codes_audit

| Column | Type | Null | Key | Default |
|--------|------|------|-----|---------|
| audit_id | BIGSERIAL | No | PK |  |
| assignment_id | UUID | No |  |  |
| user_id | TEXT | No |  |  |
| action | TEXT | No |  |  |
| previous_data | JSONB | Yes |  |  |
| new_data | JSONB | Yes |  |  |
| version_before | BIGINT | Yes |  |  |
| version_after | BIGINT | Yes |  |  |
| actor_id | TEXT | No |  |  |
| actor_role | TEXT | No |  |  |
| audit_at | TIMESTAMPTZ | No |  | NOW() |

**Indexes:**
- `idx_user_agreement_codes_audit_assignment_id` on (assignment_id)
- `idx_user_agreement_codes_audit_user_id` on (user_id)

## users_audit

| Column | Type | Null | Key | Default |
|--------|------|------|-----|---------|
| audit_id | BIGSERIAL | No | PK |  |
| user_id | TEXT | No |  |  |
| action | TEXT | No |  |  |
| previous_data | JSONB | Yes |  |  |
| new_data | JSONB | Yes |  |  |
| version_before | BIGINT | Yes |  |  |
| version_after | BIGINT | Yes |  |  |
| actor_id | TEXT | No |  |  |
| actor_role | TEXT | No |  |  |
| audit_at | TIMESTAMPTZ | No |  | NOW() |

**Indexes:**
- `idx_users_audit_user_id` on (user_id)
- `idx_users_audit_at` on (audit_at)

## roles

| Column | Type | Null | Key | Default |
|--------|------|------|-----|---------|
| role_id | TEXT | No | PK |  |
| role_name | TEXT | No |  |  |
| description | TEXT | Yes |  |  |
| hierarchy_level | INT | No |  |  |
| created_at | TIMESTAMPTZ | No |  | NOW() |

## role_assignments

| Column | Type | Null | Key | Default |
|--------|------|------|-----|---------|
| assignment_id | UUID | No | PK | gen_random_uuid() |
| user_id | TEXT | No | FK→users |  |
| role_id | TEXT | No | FK→roles |  |
| org_id | TEXT | Yes | FK→organizations |  |
| scope_type | TEXT | No |  |  |
| assigned_by | TEXT | No |  |  |
| assigned_at | TIMESTAMPTZ | No |  | NOW() |
| expires_at | TIMESTAMPTZ | Yes |  |  |
| is_active | BOOLEAN | No |  | TRUE |

**Table constraints:**
- UNIQUE (user_id, role_id, org_id)
- CONSTRAINT role_assignments_global_scope_shape CHECK ((scope_type = 'GLOBAL') = (org_id IS NULL))
- CONSTRAINT role_assignments_global_admin_requires_global CHECK (role_id <> 'GLOBAL_ADMIN' OR scope_type = 'GLOBAL')

**Indexes:**
- `idx_role_assignments_user` on (user_id)
- `idx_role_assignments_org` on (org_id)
- `idx_role_assignments_role` on (role_id)

## role_assignment_audit

| Column | Type | Null | Key | Default |
|--------|------|------|-----|---------|
| audit_id | BIGSERIAL | No | PK |  |
| assignment_id | UUID | No |  |  |
| action | TEXT | No |  |  |
| actor_id | TEXT | No |  |  |
| actor_role | TEXT | No |  |  |
| details | JSONB | Yes |  |  |
| timestamp | TIMESTAMPTZ | No |  | NOW() |

**Indexes:**
- `idx_role_audit_assignment` on (assignment_id)

## local_configurations

| Column | Type | Null | Key | Default |
|--------|------|------|-----|---------|
| config_id | UUID | No | PK | gen_random_uuid() |
| org_id | TEXT | No | FK→organizations |  |
| config_area | TEXT | No |  |  |
| config_key | TEXT | No |  |  |
| config_value | JSONB | No |  |  |
| effective_from | DATE | No |  |  |
| effective_to | DATE | Yes |  |  |
| version | INT | No |  | 1 |
| agreement_code | TEXT | No |  |  |
| ok_version | TEXT | No |  |  |
| created_by | TEXT | No |  |  |
| approved_by | TEXT | Yes |  |  |
| approved_at | TIMESTAMPTZ | Yes |  |  |
| is_active | BOOLEAN | No |  | TRUE |
| created_at | TIMESTAMPTZ | No |  | NOW() |

**Table constraints:**
- UNIQUE (org_id, config_area, config_key, effective_from, agreement_code, ok_version)

**Indexes:**
- `idx_local_config_org` on (org_id)
- `idx_local_config_area` on (config_area)

## local_configuration_audit

| Column | Type | Null | Key | Default |
|--------|------|------|-----|---------|
| audit_id | BIGSERIAL | No | PK |  |
| config_id | UUID | No |  |  |
| action | TEXT | No |  |  |
| previous_value | JSONB | Yes |  |  |
| new_value | JSONB | Yes |  |  |
| actor_id | TEXT | No |  |  |
| actor_role | TEXT | No |  |  |
| timestamp | TIMESTAMPTZ | No |  | NOW() |

**Indexes:**
- `idx_local_config_audit_config` on (config_id)

## local_agreement_profiles

| Column | Type | Null | Key | Default |
|--------|------|------|-----|---------|
| profile_id | UUID | No | PK | gen_random_uuid() |
| org_id | TEXT | No | FK→organizations |  |
| agreement_code | TEXT | No |  |  |
| ok_version | TEXT | No |  |  |
| effective_from | DATE | No |  |  |
| effective_to | DATE | Yes |  |  |
| weekly_norm_hours | NUMERIC(5,2) | Yes |  |  |
| max_flex_balance | NUMERIC(6,2) | Yes |  |  |
| flex_carryover_max | NUMERIC(6,2) | Yes |  |  |
| max_overtime_hours_per_period | NUMERIC(6,2) | Yes |  |  |
| overtime_requires_pre_approval | BOOLEAN | Yes |  |  |
| created_by | TEXT | No |  |  |
| created_at | TIMESTAMPTZ | No |  | NOW() |
| version | BIGINT | No |  | 1 |

**Indexes:**
- `uq_local_agreement_profile_active` (UNIQUE) on (org_id, agreement_code, ok_version) WHERE effective_to IS NULL
- `idx_local_agreement_profile_org` on (org_id)
- `idx_local_agreement_profile_history` on (org_id, agreement_code, ok_version, effective_from DESC)

## local_agreement_profile_audit

| Column | Type | Null | Key | Default |
|--------|------|------|-----|---------|
| audit_id | BIGSERIAL | No | PK |  |
| profile_id | UUID | No |  |  |
| action | TEXT | No |  |  |
| delta_jsonb | JSONB | No |  |  |
| actor_id | TEXT | No |  |  |
| actor_role | TEXT | No |  |  |
| timestamp | TIMESTAMPTZ | No |  | NOW() |

**Indexes:**
- `idx_local_profile_audit_profile` on (profile_id)

## approval_periods

| Column | Type | Null | Key | Default |
|--------|------|------|-----|---------|
| period_id | UUID | No | PK | gen_random_uuid() |
| employee_id | TEXT | No |  |  |
| org_id | TEXT | No | FK→organizations |  |
| period_start | DATE | No |  |  |
| period_end | DATE | No |  |  |
| period_type | TEXT | No |  |  |
| status | TEXT | No |  | 'DRAFT' |
| submitted_at | TIMESTAMPTZ | Yes |  |  |
| submitted_by | TEXT | Yes |  |  |
| approved_by | TEXT | Yes |  |  |
| approved_at | TIMESTAMPTZ | Yes |  |  |
| rejection_reason | TEXT | Yes |  |  |
| agreement_code | TEXT | No |  |  |
| ok_version | TEXT | No |  |  |
| created_at | TIMESTAMPTZ | No |  | NOW() |
| employee_approved_at | TIMESTAMPTZ | Yes |  |  |
| employee_approved_by | TEXT | Yes |  |  |
| employee_deadline | DATE | Yes |  |  |
| manager_deadline | DATE | Yes |  |  |
| designated_approver_id | TEXT | Yes |  |  |
| approval_method | TEXT | Yes |  | 'PRE_REPORTING_LINE' |

**Table constraints:**
- UNIQUE (employee_id, period_start, period_end)

**Indexes:**
- `idx_approval_employee` on (employee_id)
- `idx_approval_org` on (org_id)
- `idx_approval_status` on (status)
- `idx_approval_period` on (period_start, period_end)
- `idx_approval_employee_period_end` on (employee_id, period_end DESC)

## approval_audit

| Column | Type | Null | Key | Default |
|--------|------|------|-----|---------|
| audit_id | BIGSERIAL | No | PK |  |
| period_id | UUID | No |  |  |
| action | TEXT | No |  |  |
| actor_id | TEXT | No |  |  |
| actor_role | TEXT | No |  |  |
| comment | TEXT | Yes |  |  |
| timestamp | TIMESTAMPTZ | No |  | NOW() |

**Indexes:**
- `idx_approval_audit_period` on (period_id)

## projects

| Column | Type | Null | Key | Default |
|--------|------|------|-----|---------|
| project_id | UUID | No | PK | gen_random_uuid() |
| org_id | TEXT | No | FK→organizations |  |
| project_code | TEXT | No |  |  |
| project_name | TEXT | No |  |  |
| is_active | BOOLEAN | No |  | TRUE |
| sort_order | INT | No |  | 0 |
| created_by | TEXT | No |  |  |
| created_at | TIMESTAMPTZ | No |  | NOW() |

**Table constraints:**
- UNIQUE (org_id, project_code)

**Indexes:**
- `idx_projects_org` on (org_id)

## user_project_selections

| Column | Type | Null | Key | Default |
|--------|------|------|-----|---------|
| employee_id | TEXT | No |  |  |
| project_id | UUID | No | FK→projects |  |
| created_at | TIMESTAMPTZ | No |  | NOW() |
| sort_order | INT | No |  | 0 |

**Table constraints:**
- PRIMARY KEY (employee_id, project_id)

**Indexes:**
- `idx_user_project_sel_employee` on (employee_id)

## absence_type_visibility

| Column | Type | Null | Key | Default |
|--------|------|------|-----|---------|
| id | UUID | No | PK | gen_random_uuid() |
| org_id | TEXT | No | FK→organizations |  |
| absence_type | TEXT | No |  |  |
| is_hidden | BOOLEAN | No |  | FALSE |
| set_by | TEXT | No |  |  |
| set_at | TIMESTAMPTZ | No |  | NOW() |

**Table constraints:**
- UNIQUE (org_id, absence_type)

**Indexes:**
- `idx_absence_vis_org` on (org_id)

## positions

| Column | Type | Null | Key | Default |
|--------|------|------|-----|---------|
| position_code | TEXT | No | PK |  |
| display_label | TEXT | No |  |  |
| agreement_code | TEXT | No |  |  |
| is_active | BOOLEAN | No |  | true |
| created_at | TIMESTAMPTZ | No |  | NOW() |

## agreement_configs

| Column | Type | Null | Key | Default |
|--------|------|------|-----|---------|
| config_id | UUID | No | PK | gen_random_uuid() |
| agreement_code | TEXT | No |  |  |
| ok_version | TEXT | No |  |  |
| status | TEXT | No |  | 'DRAFT' |
| weekly_norm_hours | DECIMAL | No |  |  |
| norm_period_weeks | INT | No |  | 1 |
| norm_model | TEXT | No |  | 'WEEKLY_HOURS' |
| annual_norm_hours | DECIMAL | No |  | 1924 |
| max_flex_balance | DECIMAL | No |  |  |
| flex_carryover_max | DECIMAL | No |  |  |
| has_overtime | BOOLEAN | No |  |  |
| has_merarbejde | BOOLEAN | No |  |  |
| overtime_threshold_50 | DECIMAL | No |  | 37.0 |
| overtime_threshold_100 | DECIMAL | No |  | 40.0 |
| evening_supplement_enabled | BOOLEAN | No |  | FALSE |
| night_supplement_enabled | BOOLEAN | No |  | FALSE |
| weekend_supplement_enabled | BOOLEAN | No |  | FALSE |
| holiday_supplement_enabled | BOOLEAN | No |  | FALSE |
| evening_start | INT | No |  | 17 |
| evening_end | INT | No |  | 23 |
| night_start | INT | No |  | 23 |
| night_end | INT | No |  | 6 |
| evening_rate | DECIMAL | No |  | 1.25 |
| night_rate | DECIMAL | No |  | 1.50 |
| weekend_saturday_rate | DECIMAL | No |  | 1.50 |
| weekend_sunday_rate | DECIMAL | No |  | 2.0 |
| holiday_rate | DECIMAL | No |  | 2.0 |
| on_call_duty_enabled | BOOLEAN | No |  | FALSE |
| on_call_duty_rate | DECIMAL | No |  | 0.33 |
| call_in_work_enabled | BOOLEAN | No |  | FALSE |
| call_in_minimum_hours | DECIMAL | No |  | 3.0 |
| call_in_rate | DECIMAL | No |  | 1.0 |
| travel_time_enabled | BOOLEAN | No |  | FALSE |
| working_travel_rate | DECIMAL | No |  | 1.0 |
| non_working_travel_rate | DECIMAL | No |  | 0.5 |
| max_daily_hours | DECIMAL | No |  | 13.0 |
| minimum_rest_hours | DECIMAL | No |  | 11.0 |
| rest_period_derogation_allowed | BOOLEAN | No |  | FALSE |
| weekly_max_hours_reference_period | INT | No |  | 17 |
| voluntary_unsocial_hours_allowed | BOOLEAN | No |  | TRUE |
| default_compensation_model | TEXT | No |  | 'UDBETALING' |
| employee_compensation_choice | BOOLEAN | No |  | FALSE |
| max_overtime_hours_per_period | DECIMAL | No |  | 0 |
| overtime_requires_pre_approval | BOOLEAN | No |  | FALSE |
| created_by | TEXT | No |  | 'SYSTEM_SEED' |
| created_at | TIMESTAMPTZ | No |  | NOW() |
| updated_at | TIMESTAMPTZ | No |  | NOW() |
| published_at | TIMESTAMPTZ | Yes |  |  |
| archived_at | TIMESTAMPTZ | Yes |  |  |
| cloned_from_id | UUID | Yes | FK→agreement_configs |  |
| description | TEXT | Yes |  |  |
| version | BIGINT | No |  | 1 |

**Indexes:**
- `idx_agreement_configs_active` (UNIQUE) on (agreement_code, ok_version) WHERE status = 'ACTIVE'
- `idx_agreement_configs_code_version` on (agreement_code, ok_version)
- `idx_agreement_configs_status` on (status)

## agreement_config_audit

| Column | Type | Null | Key | Default |
|--------|------|------|-----|---------|
| audit_id | BIGSERIAL | No | PK |  |
| config_id | UUID | No |  |  |
| action | TEXT | No |  |  |
| previous_data | JSONB | Yes |  |  |
| new_data | JSONB | Yes |  |  |
| actor_id | TEXT | No |  |  |
| actor_role | TEXT | No |  |  |
| timestamp | TIMESTAMPTZ | No |  | NOW() |
| version_before | BIGINT | Yes |  |  |
| version_after | BIGINT | Yes |  |  |

**Indexes:**
- `idx_agreement_config_audit_config` on (config_id)

## compensatory_rest

| Column | Type | Null | Key | Default |
|--------|------|------|-----|---------|
| id | UUID | No | PK | gen_random_uuid() |
| employee_id | TEXT | No |  |  |
| source_date | DATE | No |  |  |
| compensatory_date | DATE | Yes |  |  |
| hours | DECIMAL | No |  |  |
| status | TEXT | No |  | 'PENDING' |
| created_at | TIMESTAMPTZ | No |  | NOW() |

**Indexes:**
- `idx_compensatory_rest_employee` on (employee_id)
- `idx_compensatory_rest_status` on (status)

## position_override_configs

| Column | Type | Null | Key | Default |
|--------|------|------|-----|---------|
| override_id | UUID | No | PK | gen_random_uuid() |
| agreement_code | TEXT | No |  |  |
| ok_version | TEXT | No |  |  |
| position_code | TEXT | No | FK→positions |  |
| status | TEXT | No |  | 'ACTIVE' |
| max_flex_balance | DECIMAL | Yes |  |  |
| flex_carryover_max | DECIMAL | Yes |  |  |
| norm_period_weeks | INT | Yes |  |  |
| weekly_norm_hours | DECIMAL | Yes |  |  |
| created_by | TEXT | No |  | 'SYSTEM_SEED' |
| created_at | TIMESTAMPTZ | No |  | NOW() |
| updated_at | TIMESTAMPTZ | No |  | NOW() |
| description | TEXT | Yes |  |  |
| version | BIGINT | No |  | 1 |

**Indexes:**
- `idx_position_override_active_unique` (UNIQUE) on (agreement_code, ok_version, position_code) WHERE status = 'ACTIVE'

## position_override_config_audit

| Column | Type | Null | Key | Default |
|--------|------|------|-----|---------|
| audit_id | BIGSERIAL | No | PK |  |
| override_id | UUID | No |  |  |
| action | TEXT | No |  |  |
| previous_data | JSONB | Yes |  |  |
| new_data | JSONB | Yes |  |  |
| actor_id | TEXT | No |  |  |
| actor_role | TEXT | No |  |  |
| timestamp | TIMESTAMPTZ | No |  | NOW() |
| version_before | BIGINT | Yes |  |  |
| version_after | BIGINT | Yes |  |  |

## wage_type_mapping_audit

| Column | Type | Null | Key | Default |
|--------|------|------|-----|---------|
| audit_id | BIGSERIAL | No | PK |  |
| time_type | TEXT | No |  |  |
| ok_version | TEXT | No |  |  |
| agreement_code | TEXT | No |  |  |
| position | TEXT | No |  | '' |
| action | TEXT | No |  |  |
| previous_data | JSONB | Yes |  |  |
| new_data | JSONB | Yes |  |  |
| actor_id | TEXT | No |  |  |
| actor_role | TEXT | No |  |  |
| timestamp | TIMESTAMPTZ | No |  | NOW() |
| version_before | BIGINT | Yes |  |  |
| version_after | BIGINT | Yes |  |  |

## entitlement_configs

| Column | Type | Null | Key | Default |
|--------|------|------|-----|---------|
| config_id | UUID | No | PK | gen_random_uuid() |
| entitlement_type | TEXT | No |  |  |
| agreement_code | TEXT | No |  |  |
| ok_version | TEXT | No |  |  |
| annual_quota | DECIMAL | No |  |  |
| accrual_model | TEXT | No |  | 'IMMEDIATE' |
| reset_month | INT | No |  | 1 |
| carryover_max | DECIMAL | No |  | 0 |
| pro_rate_by_part_time | BOOLEAN | No |  | true |
| is_per_episode | BOOLEAN | No |  | false |
| min_age | INT | Yes |  |  |
| description | TEXT | Yes |  |  |
| created_at | TIMESTAMPTZ | No |  | NOW() |
| effective_from | DATE | No |  | '0001-01-01' |
| effective_to | DATE | Yes |  |  |
| full_day_only | BOOLEAN | No |  | FALSE |
| version | BIGINT | No |  | 1 |

**Table constraints:**
- CONSTRAINT entitlement_configs_vacation_reset_month CHECK ( entitlement_type <> 'VACATION' OR reset_month = 9 )
- CONSTRAINT entitlement_configs_full_day_only_types CHECK ( entitlement_type NOT IN ('CARE_DAY', 'SENIOR_DAY') OR full_day_only )

**Indexes:**
- `idx_ec_natural_key_open` (UNIQUE) on (entitlement_type, agreement_code, ok_version) WHERE effective_to IS NULL
- `idx_ec_natural_key_history` (UNIQUE) on (entitlement_type, agreement_code, ok_version, effective_from)
- `idx_ec_natural_key_open` (UNIQUE) on (entitlement_type, agreement_code, ok_version) WHERE effective_to IS NULL
- `idx_ec_natural_key_history` (UNIQUE) on (entitlement_type, agreement_code, ok_version, effective_from)

## entitlement_config_audit

| Column | Type | Null | Key | Default |
|--------|------|------|-----|---------|
| audit_id | BIGSERIAL | No | PK |  |
| config_id | UUID | No |  |  |
| entitlement_type | TEXT | No |  |  |
| agreement_code | TEXT | No |  |  |
| ok_version | TEXT | No |  |  |
| action | TEXT | No |  |  |
| previous_data | JSONB | Yes |  |  |
| new_data | JSONB | Yes |  |  |
| version_before | BIGINT | Yes |  |  |
| version_after | BIGINT | Yes |  |  |
| actor_id | TEXT | No |  |  |
| actor_role | TEXT | No |  |  |
| timestamp | TIMESTAMPTZ | No |  | NOW() |

## entitlement_balances

| Column | Type | Null | Key | Default |
|--------|------|------|-----|---------|
| balance_id | UUID | No | PK | gen_random_uuid() |
| employee_id | TEXT | No |  |  |
| entitlement_type | TEXT | No |  |  |
| entitlement_year | INT | No |  |  |
| total_quota | DECIMAL | No |  |  |
| used | DECIMAL | No |  | 0 |
| planned | DECIMAL | No |  | 0 |
| carryover_in | DECIMAL | No |  | 0 |
| updated_at | TIMESTAMPTZ | No |  | NOW() |

**Table constraints:**
- UNIQUE (employee_id, entitlement_type, entitlement_year)

## time_entries_projection

| Column | Type | Null | Key | Default |
|--------|------|------|-----|---------|
| event_id | UUID | No | PK |  |
| employee_id | TEXT | No |  |  |
| date | DATE | No |  |  |
| hours | NUMERIC(8,4) | No |  |  |
| start_time | TIME | Yes |  |  |
| end_time | TIME | Yes |  |  |
| task_id | TEXT | Yes |  |  |
| activity_type | TEXT | Yes |  |  |
| agreement_code | TEXT | No |  |  |
| ok_version | TEXT | No |  |  |
| voluntary_unsocial_hours | BOOLEAN | No |  | false |
| occurred_at | TIMESTAMPTZ | No |  |  |
| actor_id | TEXT | Yes |  |  |
| actor_role | TEXT | Yes |  |  |
| correlation_id | UUID | Yes |  |  |
| outbox_id | BIGINT | No |  |  |

**Indexes:**
- `idx_time_entries_proj_emp_date_outbox` on (employee_id, date, outbox_id)
- `idx_time_entries_proj_emp_outbox` on (employee_id, outbox_id)

## absences_projection

| Column | Type | Null | Key | Default |
|--------|------|------|-----|---------|
| event_id | UUID | No | PK |  |
| employee_id | TEXT | No |  |  |
| date | DATE | No |  |  |
| absence_type | TEXT | No |  |  |
| hours | NUMERIC(8,4) | No |  |  |
| feriedage | NUMERIC(8,4) | Yes |  |  |
| agreement_code | TEXT | No |  |  |
| ok_version | TEXT | No |  |  |
| occurred_at | TIMESTAMPTZ | No |  |  |
| actor_id | TEXT | Yes |  |  |
| actor_role | TEXT | Yes |  |  |
| correlation_id | UUID | Yes |  |  |
| outbox_id | BIGINT | No |  |  |

**Indexes:**
- `idx_absences_proj_emp_date_outbox` on (employee_id, date, outbox_id)
- `idx_absences_proj_emp_outbox` on (employee_id, outbox_id)

## work_time_projection

| Column | Type | Null | Key | Default |
|--------|------|------|-----|---------|
| employee_id | TEXT | No |  |  |
| date | DATE | No |  |  |
| intervals | JSONB | No |  | '[]'::jsonb |
| manual_hours | NUMERIC(8,4) | No |  | 0 |
| occurred_at | TIMESTAMPTZ | No |  |  |
| actor_id | TEXT | Yes |  |  |
| actor_role | TEXT | Yes |  |  |
| correlation_id | UUID | Yes |  |  |
| outbox_id | BIGINT | No |  |  |

**Table constraints:**
- PRIMARY KEY (employee_id, date)

## overtime_balances

| Column | Type | Null | Key | Default |
|--------|------|------|-----|---------|
| balance_id | UUID | No | PK | gen_random_uuid() |
| employee_id | TEXT | No |  |  |
| agreement_code | TEXT | No |  |  |
| period_year | INT | No |  |  |
| accumulated | DECIMAL | No |  | 0 |
| paid_out | DECIMAL | No |  | 0 |
| afspadsering_used | DECIMAL | No |  | 0 |
| compensation_model | TEXT | No |  | 'UDBETALING' |
| updated_at | TIMESTAMPTZ | No |  | NOW() |

**Table constraints:**
- UNIQUE (employee_id, period_year)

**Indexes:**
- `idx_overtime_balances_employee` on (employee_id)

## overtime_pre_approvals

| Column | Type | Null | Key | Default |
|--------|------|------|-----|---------|
| id | UUID | No | PK | gen_random_uuid() |
| employee_id | TEXT | No |  |  |
| period_start | DATE | No |  |  |
| period_end | DATE | No |  |  |
| max_hours | DECIMAL | No |  |  |
| approved_by | TEXT | Yes |  |  |
| approved_at | TIMESTAMPTZ | Yes |  |  |
| status | TEXT | No |  | 'PENDING' |
| reason | TEXT | Yes |  |  |
| created_at | TIMESTAMPTZ | No |  | NOW() |
| authorization_mode | TEXT | No |  | 'PRIOR_APPROVAL' |
| necessity_reason | TEXT | Yes |  |  |
| acknowledged_at | TIMESTAMPTZ | Yes |  |  |
| acknowledged_by | TEXT | Yes |  |  |

**Indexes:**
- `idx_overtime_pre_approvals_employee` on (employee_id)
- `idx_overtime_pre_approvals_status` on (status)

## segment_manifests

| Column | Type | Null | Key | Default |
|--------|------|------|-----|---------|
| manifest_id | UUID | No | PK |  |
| period_start | DATE | No |  |  |
| period_end | DATE | No |  |  |
| employee_id | TEXT | No |  |  |
| calculation_kind | TEXT | No |  |  |
| boundary_cause_summary | TEXT[] | No |  |  |
| created_at | TIMESTAMPTZ | No |  | now() |
| segments_jsonb | JSONB | No |  |  |

**Indexes:**
- `idx_segment_manifests_employee_period` on (employee_id, period_start)
- `idx_segment_manifests_boundary_cause` on (boundary_cause_summary)

## role_config_overrides

| Column | Type | Null | Key | Default |
|--------|------|------|-----|---------|
| override_id | UUID | No | PK | gen_random_uuid() |
| employment_category | TEXT | No |  |  |
| agreement_code | TEXT | No |  |  |
| ok_version | TEXT | No |  |  |
| effective_from | DATE | No |  | '0001-01-01' |
| effective_to | DATE | Yes |  |  |
| version | BIGINT | No |  | 1 |
| merarbejde_compensation_right | TEXT | Yes |  |  |
| has_merarbejde | BOOLEAN | Yes |  |  |
| has_overtime | BOOLEAN | Yes |  |  |
| has_evening_supplement | BOOLEAN | Yes |  |  |
| has_night_supplement | BOOLEAN | Yes |  |  |
| has_weekend_supplement | BOOLEAN | Yes |  |  |
| has_holiday_supplement | BOOLEAN | Yes |  |  |
| max_flex_balance | NUMERIC(7,2) | Yes |  |  |
| flex_carryover_max | NUMERIC(7,2) | Yes |  |  |
| norm_period_weeks | INT | Yes |  |  |
| weekly_norm_hours | NUMERIC(5,2) | Yes |  |  |
| created_at | TIMESTAMPTZ | No |  | NOW() |
| created_by | TEXT | No |  |  |
| created_by_role | TEXT | No |  |  |

**Indexes:**
- `idx_role_config_overrides_live` (UNIQUE) on (employment_category, agreement_code, ok_version) WHERE effective_to IS NULL
- `idx_role_config_overrides_history` (UNIQUE) on (employment_category, agreement_code, ok_version, effective_from)

## role_config_override_audit

| Column | Type | Null | Key | Default |
|--------|------|------|-----|---------|
| audit_id | BIGSERIAL | No | PK |  |
| override_id | UUID | No | FK→role_config_overrides |  |
| action | TEXT | No |  |  |
| version_before | BIGINT | Yes |  |  |
| version_after | BIGINT | Yes |  |  |
| previous_data | JSONB | Yes |  |  |
| new_data | JSONB | Yes |  |  |
| actor_id | TEXT | No |  |  |
| actor_role | TEXT | No |  |  |
| timestamp | TIMESTAMPTZ | No |  | NOW() |

**Indexes:**
- `idx_role_config_override_audit_override` on (override_id)

## overtime_authorization_audit

| Column | Type | Null | Key | Default |
|--------|------|------|-----|---------|
| audit_id | BIGSERIAL | No | PK |  |
| pre_approval_id | UUID | No | FK→overtime_pre_approvals |  |
| action | TEXT | No |  |  |
| version_before | BIGINT | Yes |  |  |
| version_after | BIGINT | Yes |  |  |
| previous_data | JSONB | Yes |  |  |
| new_data | JSONB | Yes |  |  |
| actor_id | TEXT | No |  |  |
| actor_role | TEXT | No |  |  |
| timestamp | TIMESTAMPTZ | No |  | NOW() |

**Indexes:**
- `idx_overtime_authorization_audit_pre_approval` on (pre_approval_id)

## audit_projection

| Column | Type | Null | Key | Default |
|--------|------|------|-----|---------|
| projection_id | UUID | No | PK | gen_random_uuid() |
| event_id | UUID | No | UNIQUE |  |
| outbox_id | BIGINT | No |  |  |
| event_type | TEXT | No |  |  |
| visibility_scope | TEXT | No |  |  |
| target_org_id | TEXT | Yes | FK→organizations |  |
| target_resource_id | TEXT | Yes |  |  |
| actor_id | TEXT | Yes |  |  |
| actor_primary_org_id | TEXT | Yes |  |  |
| occurred_at | TIMESTAMPTZ | No |  |  |
| correlation_id | UUID | Yes |  |  |
| details | JSONB | No |  |  |
| projected_at | TIMESTAMPTZ | No |  | NOW() |

**Table constraints:**
- CONSTRAINT chk_target_org_required_when_tenant CHECK ( (visibility_scope = 'TENANT_TARGETED' AND target_org_id IS NOT NULL) OR (visibility_scope IN ('GLOBAL_TENANT_VISIBLE', 'GLOBAL_ADMIN_ONLY')) )

**Indexes:**
- `idx_audit_projection_target_org_time` on (target_org_id, occurred_at DESC) WHERE target_org_id IS NOT NULL
- `idx_audit_projection_global_visible` on (occurred_at DESC) WHERE visibility_scope = 'GLOBAL_TENANT_VISIBLE'
- `idx_audit_projection_actor_org_time` on (actor_primary_org_id, occurred_at DESC) WHERE actor_primary_org_id IS NOT NULL
- `idx_audit_projection_event_type_time` on (event_type, occurred_at DESC)
- `idx_audit_projection_outbox_id` on (outbox_id)

## reporting_lines

| Column | Type | Null | Key | Default |
|--------|------|------|-----|---------|
| reporting_line_id | UUID | No | PK | gen_random_uuid() |
| employee_id | TEXT | No | FK→users |  |
| manager_id | TEXT | No | FK→users |  |
| organisation_id | TEXT | No | FK→organizations |  |
| relationship | TEXT | No |  | 'PRIMARY' |
| effective_from | DATE | No |  |  |
| effective_to | DATE | Yes |  |  |
| source | TEXT | No |  | 'MANUAL' |
| version | BIGINT | No |  | 1 |
| created_by | TEXT | No |  |  |
| created_at | TIMESTAMPTZ | No |  | NOW() |
| scheduled_expiry | DATE | Yes |  |  |

**Table constraints:**
- CHECK (employee_id <> manager_id)

**Indexes:**
- `uq_reporting_line_active_primary` (UNIQUE) on (employee_id) WHERE effective_to IS NULL AND relationship = 'PRIMARY'
- `uq_reporting_line_active_acting` (UNIQUE) on (employee_id) WHERE effective_to IS NULL AND relationship = 'ACTING'
- `idx_reporting_lines_manager` on (manager_id) WHERE effective_to IS NULL
- `idx_reporting_lines_employee_history` on (employee_id, effective_from DESC)
- `idx_reporting_lines_tree_root` on (organisation_id) WHERE effective_to IS NULL

## reporting_line_audit

| Column | Type | Null | Key | Default |
|--------|------|------|-----|---------|
| audit_id | BIGSERIAL | No | PK |  |
| reporting_line_id | UUID | No |  |  |
| action | TEXT | No |  |  |
| actor_id | TEXT | No |  |  |
| correlation_id | UUID | Yes |  |  |
| version_before | BIGINT | Yes |  |  |
| version_after | BIGINT | Yes |  |  |
| metadata | JSONB | Yes |  |  |
| created_at | TIMESTAMPTZ | No |  | NOW() |

**Indexes:**
- `idx_reporting_line_audit_line` on (reporting_line_id)

## employee_entitlement_eligibility

| Column | Type | Null | Key | Default |
|--------|------|------|-----|---------|
| id | UUID | No | PK | gen_random_uuid() |
| employee_id | TEXT | No | FK→users |  |
| entitlement_type | TEXT | No |  |  |
| eligible | BOOLEAN | No |  |  |
| effective_from | DATE | No |  | '0001-01-01' |
| effective_to | DATE | Yes |  |  |
| version | BIGINT | No |  | 1 |
| created_at | TIMESTAMPTZ | No |  | NOW() |
| updated_at | TIMESTAMPTZ | No |  | NOW() |

**Indexes:**
- `idx_employee_entitlement_eligibility_live` (UNIQUE) on (employee_id, entitlement_type) WHERE effective_to IS NULL
- `idx_employee_entitlement_eligibility_history` (UNIQUE) on (employee_id, entitlement_type, effective_from)

## employee_entitlement_eligibility_audit

| Column | Type | Null | Key | Default |
|--------|------|------|-----|---------|
| audit_id | BIGSERIAL | No | PK |  |
| eligibility_id | UUID | No |  |  |
| employee_id | TEXT | No |  |  |
| action | TEXT | No |  |  |
| previous_data | JSONB | Yes |  |  |
| new_data | JSONB | Yes |  |  |
| version_before | BIGINT | Yes |  |  |
| version_after | BIGINT | Yes |  |  |
| actor_id | TEXT | No |  |  |
| actor_role | TEXT | No |  |  |
| timestamp | TIMESTAMPTZ | No |  | NOW() |

**Indexes:**
- `idx_employee_entitlement_eligibility_audit_eligibility_id` on (eligibility_id)
- `idx_employee_entitlement_eligibility_audit_employee_id` on (employee_id)

## vacation_settlements

| Column | Type | Null | Key | Default |
|--------|------|------|-----|---------|
| employee_id | TEXT | No | FK→users |  |
| entitlement_type | TEXT | No |  |  |
| entitlement_year | INT | No |  |  |
| sequence | INT | No |  |  |
| settlement_state | TEXT | No |  |  |
| trigger | TEXT | No |  |  |
| snapshot | JSONB | No |  |  |
| transfer_days | NUMERIC(6,2) | No |  | 0 |
| payout_days | NUMERIC(6,2) | No |  | 0 |
| forfeit_days | NUMERIC(6,2) | No |  | 0 |
| feriehindring_transfer_days | NUMERIC(6,2) | No |  | 0 |
| feriehindring_reason | TEXT | Yes |  |  |
| payout_reconciled_at | TIMESTAMPTZ | Yes |  |  |
| payout_reconciled_by | TEXT | Yes |  |  |
| review_disposition | TEXT | Yes |  |  |
| claim_disposition_days | NUMERIC(6,2) | Yes |  |  |
| bare_reversal_not_due | BOOLEAN | No |  | FALSE |
| version | BIGINT | No |  | 1 |
| created_at | TIMESTAMPTZ | No |  | NOW() |
| updated_at | TIMESTAMPTZ | No |  | NOW() |

**Table constraints:**
- PRIMARY KEY (employee_id, entitlement_type, entitlement_year, sequence)
- CONSTRAINT vacation_settlements_payout_reconciled_paired CHECK ( (payout_reconciled_at IS NULL AND payout_reconciled_by IS NULL) OR (payout_reconciled_at IS NOT NULL AND payout_reconciled_by IS NOT NULL) )
- CONSTRAINT vacation_settlements_nonneg_buckets CHECK ( transfer_days >= 0 AND payout_days >= 0 AND forfeit_days >= 0 AND feriehindring_transfer_days >= 0 )
- CONSTRAINT vacation_settlements_positive_counters CHECK (sequence >= 1 AND version >= 1)
- CONSTRAINT vacation_settlements_review_disposition CHECK ( review_disposition IS NULL OR review_disposition IN ('FORFEIT', 'DEFER', 'MODREGNING', 'WAIVED', 'FERIEHINDRING') )
- CONSTRAINT vacation_settlements_disposition_state CHECK ( review_disposition IS NULL OR (review_disposition = 'DEFER' AND settlement_state IN ('PENDING_REVIEW', 'REVERSED')) OR (review_disposition IN ('FORFEIT', 'MODREGNING', 'WAIVED', 'FERIEHINDRING') AND settlement_state <> 'PENDING_REVIEW') )
- CONSTRAINT vacation_settlements_bare_reversal_reversed_only CHECK ( bare_reversal_not_due = FALSE OR settlement_state = 'REVERSED' )
- CONSTRAINT vacation_settlements_claim_disposition_nonneg CHECK ( claim_disposition_days IS NULL OR claim_disposition_days >= 0 )
- CONSTRAINT vacation_settlements_claim_disposition_paired CHECK ( (claim_disposition_days IS NOT NULL) = (review_disposition IS NOT NULL AND review_disposition IN ('MODREGNING', 'WAIVED')) )
- CONSTRAINT vacation_settlements_feriehindring_paired CHECK ( (feriehindring_reason IS NOT NULL) = (review_disposition IS NOT NULL AND review_disposition = 'FERIEHINDRING') AND ( feriehindring_transfer_days = 0 OR (review_disposition IS NOT NULL AND review_disposition = 'FERIEHINDRING') ) )

**Indexes:**
- `idx_vacation_settlements_active` (UNIQUE) on (employee_id, entitlement_type, entitlement_year) WHERE settlement_state <> 'REVERSED'
- `idx_vacation_settlements_employee` on (employee_id)
- `idx_vacation_settlements_bare_reversal_marker` (UNIQUE) on (employee_id, entitlement_type, entitlement_year) WHERE bare_reversal_not_due

## vacation_transfer_agreements

| Column | Type | Null | Key | Default |
|--------|------|------|-----|---------|
| employee_id | TEXT | No | FK→users |  |
| entitlement_year | INT | No |  |  |
| entitlement_type | TEXT | No |  |  |
| transfer_days | NUMERIC(6,2) | No |  |  |
| agreement_date | DATE | No |  |  |
| recorded_by | TEXT | No |  |  |
| version | BIGINT | No |  | 1 |
| created_at | TIMESTAMPTZ | No |  | NOW() |
| updated_at | TIMESTAMPTZ | No |  | NOW() |

**Table constraints:**
- PRIMARY KEY (employee_id, entitlement_year, entitlement_type)

**Indexes:**
- `idx_vacation_transfer_agreements_employee` on (employee_id)

## vacation_settlement_audit

| Column | Type | Null | Key | Default |
|--------|------|------|-----|---------|
| audit_id | BIGSERIAL | No | PK |  |
| employee_id | TEXT | No |  |  |
| entitlement_type | TEXT | No |  |  |
| entitlement_year | INT | No |  |  |
| sequence | INT | No |  |  |
| action | TEXT | No |  |  |
| previous_data | JSONB | Yes |  |  |
| new_data | JSONB | Yes |  |  |
| version_before | BIGINT | Yes |  |  |
| version_after | BIGINT | Yes |  |  |
| actor_id | TEXT | No |  |  |
| actor_role | TEXT | No |  |  |
| audit_at | TIMESTAMPTZ | No |  | NOW() |

**Indexes:**
- `idx_vacation_settlement_audit_employee` on (employee_id)
- `idx_vacation_settlement_audit_at` on (audit_at)

## vacation_transfer_agreement_audit

| Column | Type | Null | Key | Default |
|--------|------|------|-----|---------|
| audit_id | BIGSERIAL | No | PK |  |
| employee_id | TEXT | No |  |  |
| entitlement_year | INT | No |  |  |
| entitlement_type | TEXT | No |  |  |
| action | TEXT | No |  |  |
| previous_data | JSONB | Yes |  |  |
| new_data | JSONB | Yes |  |  |
| version_before | BIGINT | Yes |  |  |
| version_after | BIGINT | Yes |  |  |
| actor_id | TEXT | No |  |  |
| actor_role | TEXT | No |  |  |
| audit_at | TIMESTAMPTZ | No |  | NOW() |

**Indexes:**
- `idx_vacation_transfer_agreement_audit_employee` on (employee_id)
- `idx_vacation_transfer_agreement_audit_at` on (audit_at)

## settlement_payroll_inbox

| Column | Type | Null | Key | Default |
|--------|------|------|-----|---------|
| source_event_id | UUID | No |  |  |
| employee_id | TEXT | Yes | FK→users |  |
| entitlement_type | TEXT | Yes |  |  |
| entitlement_year | INT | Yes |  |  |
| sequence | INT | Yes |  |  |
| bucket | TEXT | No |  |  |
| processing_status | TEXT | No |  |  |
| attempts | INT | No |  | 0 |
| last_error | TEXT | Yes |  |  |
| processed_at | TIMESTAMPTZ | Yes |  |  |
| created_at | TIMESTAMPTZ | No |  | NOW() |
| updated_at | TIMESTAMPTZ | No |  | NOW() |

**Table constraints:**
- CONSTRAINT settlement_payroll_inbox_pkey PRIMARY KEY (source_event_id, bucket)
- CONSTRAINT settlement_payroll_inbox_processing_status CHECK ( processing_status IN ('RETRY_PENDING', 'PROCESSED', 'SKIPPED_RECONCILED', 'SKIPPED_VOIDED', 'DEAD_LETTER') )

**Indexes:**
- `idx_settlement_payroll_inbox_retry_pending` on (processing_status) WHERE processing_status = 'RETRY_PENDING'
- `idx_settlement_payroll_inbox_settlement` on (employee_id, entitlement_type, entitlement_year, sequence, bucket)

## settlement_export_lines

| Column | Type | Null | Key | Default |
|--------|------|------|-----|---------|
| line_id | BIGSERIAL | No | PK |  |
| employee_id | TEXT | No | FK→users |  |
| entitlement_type | TEXT | No |  |  |
| entitlement_year | INT | No |  |  |
| sequence | INT | No |  |  |
| bucket | TEXT | No |  |  |
| wage_type | TEXT | No |  |  |
| hours | NUMERIC(8,2) | No |  |  |
| amount | NUMERIC(12,2) | No |  | 0 |
| ok_version | TEXT | No |  |  |
| agreement_code | TEXT | No |  |  |
| position | TEXT | No |  | '' |
| period_start | DATE | No |  |  |
| period_end | DATE | No |  |  |
| source_event_id | UUID | No |  |  |
| line_kind | TEXT | No |  | 'ORIGINAL' |
| reverses_line_id | BIGINT | Yes |  |  |
| created_at | TIMESTAMPTZ | No |  | NOW() |
| created_by | TEXT | No |  |  |

**Table constraints:**
- CONSTRAINT settlement_export_lines_line_kind CHECK ( line_kind IN ('ORIGINAL', 'REVERSAL') )
- CONSTRAINT settlement_export_lines_reversal_pairing CHECK ( (reverses_line_id IS NOT NULL) = (line_kind = 'REVERSAL') )
- CONSTRAINT settlement_export_lines_reverses_line_fk FOREIGN KEY (reverses_line_id) REFERENCES settlement_export_lines (line_id)

**Indexes:**
- `idx_settlement_export_lines_bucket` (UNIQUE) on (employee_id, entitlement_type, entitlement_year, sequence, bucket)
- `idx_settlement_export_lines_employee` on (employee_id)

## termination_payout_requests

| Column | Type | Null | Key | Default |
|--------|------|------|-----|---------|
| request_id | BIGSERIAL | No | PK |  |
| employee_id | TEXT | No | FK→users |  |
| entitlement_type | TEXT | No |  |  |
| entitlement_year | INT | No |  |  |
| settlement_sequence | INT | No |  |  |
| state | TEXT | No |  |  |
| request_date | DATE | No |  |  |
| recorded_by | TEXT | No |  |  |
| evidence_note | TEXT | Yes |  |  |
| version | BIGINT | No |  | 1 |
| created_at | TIMESTAMPTZ | No |  | NOW() |
| updated_at | TIMESTAMPTZ | No |  | NOW() |

**Table constraints:**
- CONSTRAINT termination_payout_requests_state CHECK ( state IN ('OPEN', 'LINE_STAGED', 'VOIDED_BY_REVERSAL') )
- CONSTRAINT termination_payout_requests_positive_version CHECK (version >= 1)
- CONSTRAINT termination_payout_requests_settlement_fk FOREIGN KEY (employee_id, entitlement_type, entitlement_year, settlement_sequence) REFERENCES vacation_settlements (employee_id, entitlement_type, entitlement_year, sequence)

**Indexes:**
- `idx_termination_payout_requests_nonvoided` (UNIQUE) on (employee_id, entitlement_type, entitlement_year, settlement_sequence) WHERE state <> 'VOIDED_BY_REVERSAL'
- `idx_termination_payout_requests_employee` on (employee_id)

## user_skema_preferences

| Column | Type | Null | Key | Default |
|--------|------|------|-----|---------|
| employee_id | TEXT | No | PK, FK→users |  |
| initialized_at | TIMESTAMPTZ | No |  | NOW() |

## user_absence_selections

| Column | Type | Null | Key | Default |
|--------|------|------|-----|---------|
| employee_id | TEXT | No | FK→users |  |
| absence_type | TEXT | No |  |  |
| sort_order | INT | No |  | 0 |

**Table constraints:**
- PRIMARY KEY (employee_id, absence_type)

## manager_vikar

| Column | Type | Null | Key | Default |
|--------|------|------|-----|---------|
| vikar_id | UUID | No | PK | gen_random_uuid() |
| absent_approver_id | TEXT | No | FK→users |  |
| vikar_user_id | TEXT | No | FK→users |  |
| until_date | DATE | No |  |  |
| reason | TEXT | No |  |  |
| organisation_id | TEXT | No | FK→organizations |  |
| version | BIGINT | No |  | 1 |
| created_by | TEXT | No |  |  |
| created_at | TIMESTAMPTZ | No |  | NOW() |
| effective_to | DATE | Yes |  |  |

**Table constraints:**
- CHECK (absent_approver_id <> vikar_user_id)

**Indexes:**
- `uq_manager_vikar_active` (UNIQUE) on (absent_approver_id) WHERE effective_to IS NULL
- `idx_manager_vikar_vikar` on (vikar_user_id) WHERE effective_to IS NULL

## payroll_export_records

| Column | Type | Null | Key | Default |
|--------|------|------|-----|---------|
| export_id | UUID | No | PK |  |
| period_id | UUID | Yes |  |  |
| employee_id | TEXT | No |  |  |
| year | INT | No |  |  |
| month | INT | No |  |  |
| exported_at | TIMESTAMPTZ | No |  | NOW() |
| original_lines | JSONB | No |  |  |
| current_effective_lines | JSONB | No |  |  |
| content_hash | TEXT | No |  |  |
| source | TEXT | No |  | 'CALCULATE_AND_EXPORT' |

**Table constraints:**
- CONSTRAINT uq_payroll_export_employee_month UNIQUE (employee_id, year, month)

**Indexes:**
- `idx_payroll_export_records_period` on (period_id)

---

## Table Summary

| # | Table | Audit? |
|---|-------|--------|
| 1 | schema_migrations | -- |
| 2 | event_streams | -- |
| 3 | events | -- |
| 4 | outbox_events | -- |
| 5 | outbox_messages | -- |
| 6 | orchestrator_tasks | -- |
| 7 | rule_versions | -- |
| 8 | wage_type_mappings | -- |
| 9 | flex_balance_snapshots | -- |
| 10 | danish_public_holidays | -- |
| 11 | audit_log | -- |
| 12 | organizations | -- |
| 13 | users | -- |
| 14 | employee_profiles | -- |
| 15 | employee_profile_audit | audit |
| 16 | enheder | -- |
| 17 | user_enheder | -- |
| 18 | user_agreement_codes | -- |
| 19 | user_agreement_codes_audit | audit |
| 20 | users_audit | audit |
| 21 | roles | -- |
| 22 | role_assignments | -- |
| 23 | role_assignment_audit | audit |
| 24 | local_configurations | -- |
| 25 | local_configuration_audit | audit |
| 26 | local_agreement_profiles | -- |
| 27 | local_agreement_profile_audit | audit |
| 28 | approval_periods | -- |
| 29 | approval_audit | audit |
| 30 | projects | -- |
| 31 | user_project_selections | -- |
| 32 | absence_type_visibility | -- |
| 33 | positions | -- |
| 34 | agreement_configs | -- |
| 35 | agreement_config_audit | audit |
| 36 | compensatory_rest | -- |
| 37 | position_override_configs | -- |
| 38 | position_override_config_audit | audit |
| 39 | wage_type_mapping_audit | audit |
| 40 | entitlement_configs | -- |
| 41 | entitlement_config_audit | audit |
| 42 | entitlement_balances | -- |
| 43 | time_entries_projection | -- |
| 44 | absences_projection | -- |
| 45 | work_time_projection | -- |
| 46 | overtime_balances | -- |
| 47 | overtime_pre_approvals | -- |
| 48 | segment_manifests | -- |
| 49 | role_config_overrides | -- |
| 50 | role_config_override_audit | audit |
| 51 | overtime_authorization_audit | audit |
| 52 | audit_projection | -- |
| 53 | reporting_lines | -- |
| 54 | reporting_line_audit | audit |
| 55 | employee_entitlement_eligibility | -- |
| 56 | employee_entitlement_eligibility_audit | audit |
| 57 | vacation_settlements | -- |
| 58 | vacation_transfer_agreements | -- |
| 59 | vacation_settlement_audit | audit |
| 60 | vacation_transfer_agreement_audit | audit |
| 61 | settlement_payroll_inbox | -- |
| 62 | settlement_export_lines | -- |
| 63 | termination_payout_requests | -- |
| 64 | user_skema_preferences | -- |
| 65 | user_absence_selections | -- |
| 66 | manager_vikar | -- |
| 67 | payroll_export_records | -- |

