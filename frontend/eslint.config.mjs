// SPRINT-111 / TASK-11102 — the no-`as` / no-hand-written-response-type lint for
// the typed-read hooks.
// SPRINT-112 / TASK-11203 — extended to the switched mutation/etag slice files.
// SPRINT-115 / TASK-11502 — Pass 2: `useReportingLines.ts` + `useAdmin.ts` join
// the FULL ban tier (useAdmin's legacy-GET exception CLOSED — the org-users read
// was typed in TASK-11501); `useEntitlementEligibility.ts` joins on a PARTIAL
// tier (see below).
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
]

// S115 — fully switched EXCEPT the ONE deferred polymorphic explicit-T etag GET
// (see the SCOPE NOTE above): everything banned, with the etag type-argument
// ban narrowed to two-argument (structured/mutation) calls.
const TYPED_SLICE_FILES_WITH_LEGACY_ETAG_GET = ['src/hooks/useEntitlementEligibility.ts']

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
]
