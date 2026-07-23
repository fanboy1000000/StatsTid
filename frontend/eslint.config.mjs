// SPRINT-111 / TASK-11102 — the no-`as` / no-hand-written-response-type lint for
// the typed-read hooks.
// SPRINT-112 / TASK-11203 — extended to the switched mutation/etag slice files.
// SPRINT-115 / TASK-11502 — Pass 2: `useReportingLines.ts` + `useAdmin.ts` join
// the FULL ban tier (useAdmin's legacy-GET exception CLOSED — the org-users read
// was typed in TASK-11501); `useEntitlementEligibility.ts` joins on a PARTIAL
// tier (see below).
// SPRINT-116 / TASK-11602 — Pass 3 (the approval bucket): 7 switched files join
// the FULL ban tier; `useSkema.ts` joined on a PARTIAL tier (its two legacy
// skema-family calls rode grandfathered UNTYPED ops — route-helper-pinned).
// SPRINT-120 / TASK-12001 — Pass 7 (bucket C, the employee-facing drain): the
// 7 switched hooks join the FULL ban tier, including THE useSkema GRADUATION —
// the skema month GET + save POST went typed, so BOTH S116 carve-out rules
// (`NO_GET_TYPEARG_RULE_EXCEPT_SKEMA_MONTH`,
// `NO_BODY_VERB_TYPEARG_RULE_EXCEPT_SKEMA_SAVE`) and the partial
// `TYPED_SLICE_FILES_WITH_LEGACY_SKEMA_CALLS` tier were DELETED (the S116
// promise honored). `src/lib/api.ts` (host of the switched
// `putSkemaRowPreferences`) stays UNTIERED: it is the overload-implementation
// module whose internal runtime-normalization casts are structural — the lint
// tiers guard CALL SITES, not the client's own plumbing.
// SPRINT-119 / TASK-11901 — Pass 6 bucket B: 4 switched files join the FULL
// ban tier (useConfig / useProjects hooks, the api/profileApi.ts module and
// the ProjectManagement page) — no deferred legacy call remains in any of
// them, so no partial tier is needed this pass.
// SPRINT-118 / TASK-11801 — Pass 5 bucket A: 5 switched files join the FULL
// ban tier (useAgreementConfigs / useEntitlementConfig / usePositionOverrides
// hooks + the AgreementConfigEditor / EntitlementConfigEditor pages);
// `useWageTypeMappings.ts` and `components/admin/EntitlementSection.tsx` join
// on PARTIAL tiers — each hosts ONE deferred legacy If-Match PUT whose spec
// request body REQUIRES members the FE payload does not send (binder-required
// `effectiveFrom`; the W2-pinned `fullDayOnly`) and whose addition is a barred
// request-payload change this pass, so the typed form cannot compile against
// the byte-identical legacy payload. Route-helper-pinned (the S115/S116
// precedent): `WAGE_TYPE_MAPPING_UPDATE_PATH` / `CHILD_ENTITLEMENT_PATH` are
// the sanction boundaries.
//
// This project has no general ESLint config; this flat config exists SOLELY to
// guard the OpenAPI-typed calls against re-introducing the S97→S100 "fetchEnheder"
// drift hole. It forbids, on the fully-retrofitted files:
//   1. an explicit type argument on `apiClient.get/post/put/delete<…>(…)` or
//      `apiFetchWithEtag<…>(…)` — i.e. a hand-written request/response type that
//      bypasses the spec-derived binding; and
//   2. an `as` type assertion — the response must flow through the spec types
//      (S113: the `src/lib/apiNarrow.ts` coercion module was DELETED once the
//      generated types became strict — no sanctioned assertion site remains).
//
// SCOPE NOTE (S115): `useEntitlementEligibility.ts` is fully switched EXCEPT the
// typed-contract program's FIRST flag-and-defer op — `GET /api/admin/employees/
// {id}/entitlement-eligibility/{type}` is genuinely POLYMORPHIC (its no-row
// branch OMITS keys; typing it = a wire change, PAT-010-forbidden), so its
// `apiFetchWithEtag<T>(url)` explicit-T ONE-ARGUMENT read must remain. The file
// therefore gets every ban EXCEPT the blanket etag type-argument rule, which is
// narrowed to TWO-ARGUMENT calls (every structured/mutation form carries an
// options argument; the sanctioned deferred read is the only one-argument call).
// This reuses the S112 WITH_LEGACY_GET tier pattern (useAdmin's now-closed
// `apiClient.get<T>` carve-out) with the carve-out moved to the etag rule.
//
// Run via `npm run lint`.

import tsParser from '@typescript-eslint/parser'

const TYPED_READ_HOOKS = [
  'src/hooks/useForest.ts',
  'src/hooks/useSearch.ts',
  'src/hooks/useRoster.ts',
]

// S112/S115 — files where EVERY apiClient/apiFetchWithEtag call is typed (no
// explicit-T fallback remains): full ban surface.
const TYPED_SLICE_FILES = [
  'src/hooks/useUnitMutations.ts',
  'src/hooks/useOrgMutations.ts',
  'src/pages/admin/editPerson/employeeProfileApi.ts',
  // S115 — the Pass-2 switched slices (useAdmin graduated from the
  // WITH_LEGACY_GET tier; useReportingLines fully drained).
  'src/hooks/useAdmin.ts',
  'src/hooks/useReportingLines.ts',
  // S116 — the Pass-3 approval-bucket switched files (TASK-11602): the 4 fully
  // drained hooks + the 3 pages hosting direct apiClient calls (incl. the
  // repaired OvertimePreApprovalManagement). useSkema.ts is NOT here — it holds
  // the two sanctioned legacy skema-family calls (see the tier below).
  'src/hooks/useApprovals.ts',
  'src/hooks/useTeamOverview.ts',
  'src/hooks/useAllocationBreakdown.ts',
  'src/hooks/useDelegation.ts',
  'src/pages/approval/MyPeriods.tsx',
  'src/pages/approval/TeamOversigt.tsx',
  'src/pages/admin/OvertimePreApprovalManagement.tsx',
  // S118 — the Pass-5 bucket-A switched files (TASK-11801): the 3 fully
  // drained hooks + the 2 admin pages hosting direct calls / error narrowing.
  // useWageTypeMappings.ts and EntitlementSection.tsx are NOT here — each
  // holds ONE sanctioned deferred legacy PUT (see the tiers below).
  'src/hooks/useAgreementConfigs.ts',
  'src/hooks/useEntitlementConfig.ts',
  'src/hooks/usePositionOverrides.ts',
  'src/pages/admin/AgreementConfigEditor.tsx',
  'src/pages/admin/EntitlementConfigEditor.tsx',
  // S119 — the Pass-6 bucket-B switched files (TASK-11901): the constraints +
  // profile + projects surface is FULLY drained (every call typed; the
  // profile PUT's flexible precondition rides the structured
  // ifMatch/ifNoneMatch options) — full tier, no carve-outs.
  'src/hooks/useConfig.ts',
  'src/api/profileApi.ts',
  'src/hooks/useProjects.ts',
  'src/pages/admin/ProjectManagement.tsx',
  // S120 — the Pass-7 bucket-C switched files (TASK-12001): the 6 fully
  // drained employee-facing hooks + the GRADUATED useSkema.ts (its S116
  // partial tier is gone — every call in the file is typed).
  'src/hooks/useTimeEntries.ts',
  'src/hooks/useFlexBalance.ts',
  'src/hooks/useBalanceSummary.ts',
  'src/hooks/useYearOverview.ts',
  'src/hooks/useCompliance.ts',
  'src/hooks/useCompensationChoice.ts',
  'src/hooks/useSkema.ts',
]

// S115 — fully switched EXCEPT the ONE deferred polymorphic explicit-T etag GET
// (see the SCOPE NOTE above): everything banned, with the etag type-argument
// ban narrowed to two-argument (structured/mutation) calls.
const TYPED_SLICE_FILES_WITH_LEGACY_ETAG_GET = ['src/hooks/useEntitlementEligibility.ts']

// S118 — useWageTypeMappings.ts: the list GET, the create POST and the
// If-Match DELETE are fully switched, but the If-Match PUT is a SANCTIONED
// DEFERRED legacy call: the spec `UpdateWageTypeMappingRequest` REQUIRES
// `effectiveFrom` (binder-enforced — the current FE payload's omission is a
// LIVE 400 dead-end, a NAMED DEFERRED DEFECT) and adding it is a barred
// request-payload change this pass, so the typed form cannot compile against
// the byte-identical payload. The call is pinned by its ROUTE HELPER
// (`WAGE_TYPE_MAPPING_UPDATE_PATH(...)` — the S115 ELIGIBILITY_PATH / S116
// SKEMA_*_PATH precedent): every OTHER explicit-T etag call stays banned.
const TYPED_SLICE_FILES_WITH_LEGACY_WAGE_UPDATE = ['src/hooks/useWageTypeMappings.ts']

// S118 — components/admin/EntitlementSection.tsx (a COMPONENT path,
// deliberately): the child-entitlement create POST and DELETE are fully
// switched, but the If-Match PUT is a SANCTIONED DEFERRED legacy call — the
// S118 Step-0b Reviewer W2 ruling bars wiring `fullDayOnly` (and the likewise
// missing binder-required `effectiveFrom`) into the request body this pass,
// so the typed form cannot compile against the byte-identical payload (the
// dead-end is a NAMED DEFERRED DEFECT). Pinned by `CHILD_ENTITLEMENT_PATH`.
const TYPED_SLICE_FILES_WITH_LEGACY_CHILD_ENTITLEMENT_PUT = [
  'src/components/admin/EntitlementSection.tsx',
]

const NO_AS_RULE = {
  selector: 'TSAsExpression',
  message:
    'SPRINT-111/112/113: no `as` in a typed slice file — let the strict spec-derived apiClient/apiFetchWithEtag types flow through (apiNarrow.ts was deleted in S113; use a runtime type guard for undeclared error payloads).',
}

const NO_GET_TYPEARG_RULE = {
  selector:
    "CallExpression[callee.object.name='apiClient'][callee.property.name='get'][typeArguments]",
  message:
    'SPRINT-111: no hand-written response type on `apiClient.get<…>` — the response type is DERIVED from the OpenAPI path key (the S97→S100 drift-class fix).',
}

const NO_BODY_VERB_TYPEARG_RULE = {
  selector:
    "CallExpression[callee.object.name='apiClient'][callee.property.name=/^(post|put|delete)$/][typeArguments]",
  message:
    'SPRINT-112: no hand-written type on `apiClient.post/put/delete<…>` — use the typed structured form (pathKey + { params?, query?, body? }); types are DERIVED from the OpenAPI path key.',
}

const NO_ETAG_TYPEARG_RULE = {
  selector: "CallExpression[callee.name='apiFetchWithEtag'][typeArguments]",
  message:
    'SPRINT-112: no hand-written type on `apiFetchWithEtag<…>` — use the typed structured form (pathKey + { method, params?, ifMatch?, ifNoneMatch?, body? }); types are DERIVED from the OpenAPI path key.',
}

// S115 — the eligibility-tier variant: bans EVERY explicit-T `apiFetchWithEtag`
// call EXCEPT the file's ONE sanctioned deferred polymorphic GET, pinned by its
// ROUTE HELPER (a one-argument call whose argument is `ELIGIBILITY_PATH(…)`) —
// not by call form, so a future one-argument explicit-T call on any OTHER url
// stays banned (Step-7a, both lenses).
const NO_ETAG_TYPEARG_RULE_EXCEPT_ELIGIBILITY_GET = {
  selector:
    "CallExpression[callee.name='apiFetchWithEtag'][typeArguments]:not([arguments.length=1][arguments.0.callee.name='ELIGIBILITY_PATH'])",
  message:
    'SPRINT-115: no hand-written type on `apiFetchWithEtag<…>` — use the typed spec-keyed form. Only the ONE deferred polymorphic GET (`apiFetchWithEtag<T>(ELIGIBILITY_PATH(id))`, entitlement-eligibility) may carry an explicit T.',
}

// S118 — the useWageTypeMappings-tier variant: bans EVERY explicit-T
// `apiFetchWithEtag` call EXCEPT the file's ONE sanctioned deferred PUT,
// pinned by its ROUTE HELPER (the first argument is a call to
// `WAGE_TYPE_MAPPING_UPDATE_PATH`) AND by its exact call shape (Step-7a
// tightening: two arguments, options-object literal whose FIRST property is
// `method: 'PUT'`) — fail-closed: an explicit-T etag call on any other url,
// a raw-literal url that bypasses the helper, OR any other method/arity on
// the SAME helper stays banned.
const NO_ETAG_TYPEARG_RULE_EXCEPT_WAGE_UPDATE = {
  selector:
    "CallExpression[callee.name='apiFetchWithEtag'][typeArguments]:not([arguments.length=2][arguments.0.callee.name='WAGE_TYPE_MAPPING_UPDATE_PATH'][arguments.1.type='ObjectExpression'][arguments.1.properties.0.key.name='method'][arguments.1.properties.0.value.value='PUT'])",
  message:
    'SPRINT-118: no hand-written type on `apiFetchWithEtag<…>` — use the typed spec-keyed form. Only the ONE sanctioned deferred wage-type-mapping update (`apiFetchWithEtag<T>(WAGE_TYPE_MAPPING_UPDATE_PATH(), …)`, a barred-payload-change deferral) may carry an explicit T.',
}

// S118 — the EntitlementSection-tier variant: same shape, pinned to
// `CHILD_ENTITLEMENT_PATH` (the deferred child-entitlement PUT — the W2
// ruling's named deferred defect) AND to the exact PUT call shape (Step-7a
// tightening — `CHILD_ENTITLEMENT_PATH` is also the child DELETE route; the
// method/arity pin keeps a future non-PUT reuse banned).
const NO_ETAG_TYPEARG_RULE_EXCEPT_CHILD_ENTITLEMENT_PUT = {
  selector:
    "CallExpression[callee.name='apiFetchWithEtag'][typeArguments]:not([arguments.length=2][arguments.0.callee.name='CHILD_ENTITLEMENT_PATH'][arguments.1.type='ObjectExpression'][arguments.1.properties.0.key.name='method'][arguments.1.properties.0.value.value='PUT'])",
  message:
    'SPRINT-118: no hand-written type on `apiFetchWithEtag<…>` — use the typed spec-keyed form. Only the ONE sanctioned deferred child-entitlement update (`apiFetchWithEtag<T>(CHILD_ENTITLEMENT_PATH(…), …)`, the W2-ruling deferral) may carry an explicit T.',
}

const languageOptions = {
  parser: tsParser,
  ecmaVersion: 'latest',
  sourceType: 'module',
}

export default [
  {
    files: [...TYPED_READ_HOOKS, ...TYPED_SLICE_FILES],
    languageOptions,
    rules: {
      'no-restricted-syntax': [
        'error',
        NO_AS_RULE,
        NO_GET_TYPEARG_RULE,
        NO_BODY_VERB_TYPEARG_RULE,
        NO_ETAG_TYPEARG_RULE,
      ],
    },
  },
  {
    files: TYPED_SLICE_FILES_WITH_LEGACY_ETAG_GET,
    languageOptions,
    rules: {
      'no-restricted-syntax': [
        'error',
        NO_AS_RULE,
        NO_GET_TYPEARG_RULE,
        NO_BODY_VERB_TYPEARG_RULE,
        NO_ETAG_TYPEARG_RULE_EXCEPT_ELIGIBILITY_GET,
      ],
    },
  },
  {
    files: TYPED_SLICE_FILES_WITH_LEGACY_WAGE_UPDATE,
    languageOptions,
    rules: {
      'no-restricted-syntax': [
        'error',
        NO_AS_RULE,
        NO_GET_TYPEARG_RULE,
        NO_BODY_VERB_TYPEARG_RULE,
        NO_ETAG_TYPEARG_RULE_EXCEPT_WAGE_UPDATE,
      ],
    },
  },
  {
    files: TYPED_SLICE_FILES_WITH_LEGACY_CHILD_ENTITLEMENT_PUT,
    languageOptions,
    rules: {
      'no-restricted-syntax': [
        'error',
        NO_AS_RULE,
        NO_GET_TYPEARG_RULE,
        NO_BODY_VERB_TYPEARG_RULE,
        NO_ETAG_TYPEARG_RULE_EXCEPT_CHILD_ENTITLEMENT_PUT,
      ],
    },
  },
]
