// SPRINT-111 / TASK-11102 — the no-`as` / no-hand-written-response-type lint for
// the typed-read hooks.
//
// This project has no general ESLint config; this flat config exists SOLELY to
// guard the OpenAPI-typed reads against re-introducing the S97→S100 "fetchEnheder"
// drift hole. It forbids, on the fully-retrofitted single-read hook files:
//   1. an explicit type argument on `apiClient.get<…>(…)` — i.e. a hand-written
//      response type that bypasses the spec-derived binding; and
//   2. an `as` type assertion — the response must flow through the spec types (the
//      one legitimate enum-narrowing assertion lives in `src/lib/apiNarrow.ts`,
//      which is intentionally NOT linted here).
//
// SCOPE NOTE: `useAdmin.ts` hosts the org-list typed read AND several deferred
// (not-yet-retrofitted) reads/mutations (`apiClient.get<User[]>`,
// `apiFetchWithEtag<User>`, a `UserMutationError` `as`) whose retrofit is a later
// phase — so a file-level ban cannot apply there yet. Its org-list read is still
// typed + drift-guarded (`_OrgDrift`); only the file-level lint is deferred. The
// three single-read files below are fully clean and are the enforced surface.
//
// Run via `npm run lint`.

import tsParser from '@typescript-eslint/parser'

const TYPED_READ_HOOKS = [
  'src/hooks/useForest.ts',
  'src/hooks/useSearch.ts',
  'src/hooks/useRoster.ts',
]

export default [
  {
    files: TYPED_READ_HOOKS,
    languageOptions: {
      parser: tsParser,
      ecmaVersion: 'latest',
      sourceType: 'module',
    },
    rules: {
      'no-restricted-syntax': [
        'error',
        {
          selector: 'TSAsExpression',
          message:
            "SPRINT-111: no `as` in a typed-read hook — let the spec-derived `apiClient.get` type flow through (narrowing belongs in src/lib/apiNarrow.ts).",
        },
        {
          selector:
            "CallExpression[callee.object.name='apiClient'][callee.property.name='get'][typeArguments]",
          message:
            'SPRINT-111: no hand-written response type on `apiClient.get<…>` — the response type is DERIVED from the OpenAPI path key (the S97→S100 drift-class fix).',
        },
      ],
    },
  },
]
