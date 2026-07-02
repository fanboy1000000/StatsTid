// SPRINT-111 / TASK-11102 — the no-`as` / no-hand-written-response-type lint for
// the typed-read hooks.
// SPRINT-112 / TASK-11203 — extended to the switched mutation/etag slice files.
//
// This project has no general ESLint config; this flat config exists SOLELY to
// guard the OpenAPI-typed calls against re-introducing the S97→S100 "fetchEnheder"
// drift hole. It forbids, on the fully-retrofitted files:
//   1. an explicit type argument on `apiClient.get/post/put/delete<…>(…)` or
//      `apiFetchWithEtag<…>(…)` — i.e. a hand-written request/response type that
//      bypasses the spec-derived binding; and
//   2. an `as` type assertion — the response must flow through the spec types (the
//      one legitimate enum-narrowing assertion lives in `src/lib/apiNarrow.ts`,
//      which is intentionally NOT linted here).
//
// SCOPE NOTE (S112): `useAdmin.ts` is now fully switched EXCEPT ONE read —
// `GET /api/admin/organizations/{orgId}/users` is still UNDECLARED in the spec
// (`content?: never`, grandfathered), so its `apiClient.get<User[]>` explicit-T
// fallback must remain. The file therefore gets the `as`-ban + the mutation/etag
// type-argument bans, but NOT the `apiClient.get` type-argument ban (the
// TYPED_MUTATION_RULES vs FULL rules split below). `useReportingLines.ts` hosts
// the switched users-search typed read but ALSO many not-yet-typed etag calls
// (reporting-lines routes are grandfathered) — its file-level lint stays
// deferred like useAdmin's was in S111.
//
// Run via `npm run lint`.

import tsParser from '@typescript-eslint/parser'

const TYPED_READ_HOOKS = [
  'src/hooks/useForest.ts',
  'src/hooks/useSearch.ts',
  'src/hooks/useRoster.ts',
]

// S112 — files where EVERY apiClient/apiFetchWithEtag call is typed (no
// explicit-T fallback remains): full ban surface.
const TYPED_SLICE_FILES = [
  'src/hooks/useUnitMutations.ts',
  'src/hooks/useOrgMutations.ts',
  'src/pages/admin/editPerson/employeeProfileApi.ts',
]

// S112 — files fully switched EXCEPT a grandfathered `apiClient.get<T>` read
// (see the SCOPE NOTE above): everything banned but the get type-argument.
const TYPED_SLICE_FILES_WITH_LEGACY_GET = ['src/hooks/useAdmin.ts']

const NO_AS_RULE = {
  selector: 'TSAsExpression',
  message:
    "SPRINT-111/112: no `as` in a typed slice file — let the spec-derived apiClient/apiFetchWithEtag types flow through (narrowing belongs in src/lib/apiNarrow.ts).",
}

const NO_GET_TYPEARG_RULE = {
  selector:
    "CallExpression[callee.object.name='apiClient'][callee.property.name='get'][typeArguments]",
  message:
    'SPRINT-111: no hand-written response type on `apiClient.get<…>` — the response type is DERIVED from the OpenAPI path key (the S97→S100 drift-class fix).',
}

const NO_MUTATION_TYPEARG_RULES = [
  {
    selector:
      "CallExpression[callee.object.name='apiClient'][callee.property.name=/^(post|put|delete)$/][typeArguments]",
    message:
      'SPRINT-112: no hand-written type on `apiClient.post/put/delete<…>` — use the typed structured form (pathKey + { params?, query?, body? }); types are DERIVED from the OpenAPI path key.',
  },
  {
    selector: "CallExpression[callee.name='apiFetchWithEtag'][typeArguments]",
    message:
      'SPRINT-112: no hand-written type on `apiFetchWithEtag<…>` — use the typed structured form (pathKey + { method, params?, ifMatch?, body? }); types are DERIVED from the OpenAPI path key.',
  },
]

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
        ...NO_MUTATION_TYPEARG_RULES,
      ],
    },
  },
  {
    files: TYPED_SLICE_FILES_WITH_LEGACY_GET,
    languageOptions,
    rules: {
      'no-restricted-syntax': ['error', NO_AS_RULE, ...NO_MUTATION_TYPEARG_RULES],
    },
  },
]
