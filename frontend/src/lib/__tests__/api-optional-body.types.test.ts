// S148 TASK-14838 — compile-time pin of `StructuredOptionsForOp`'s three body cases (PAT-012).
// A generated operation declaring `requestBody?:` makes `body` optional and typed; a required
// `requestBody:` stays required; no body stays `body?: undefined`. Proven under `tsc --noEmit`:
// each `@ts-expect-error` reds tsc when it becomes unused (the derivation loosened).
import { describe, it, expect } from 'vitest'
import type { StructuredOptionsForOp } from '../api'

type Json<B> = { content: { 'application/json': B } }
type Pub = { appliesFrom?: string | null }

type OptionalBodyOp = { parameters: { path?: never }; requestBody?: Json<Pub> }
type RequiredBodyOp = { parameters: { path?: never }; requestBody: Json<Pub> }
type NoBodyOp = { parameters: { path?: never }; requestBody?: never }

describe('StructuredOptionsForOp body cases (compile-time)', () => {
  it('optional-body route accepts a typed body and no body', () => {
    const withBody: StructuredOptionsForOp<OptionalBodyOp> = { body: { appliesFrom: '2026-11-09' } }
    const without: StructuredOptionsForOp<OptionalBodyOp> = {}
    // @ts-expect-error — the optional body is still typed
    const wrong: StructuredOptionsForOp<OptionalBodyOp> = { body: { appliesFrom: 5 } }
    expect([withBody, without, wrong]).toHaveLength(3)
  })

  it('required-body route still rejects a missing body', () => {
    const ok: StructuredOptionsForOp<RequiredBodyOp> = { body: { appliesFrom: '2026-11-09' } }
    // @ts-expect-error — a required body cannot be omitted
    const missing: StructuredOptionsForOp<RequiredBodyOp> = {}
    expect([ok, missing]).toHaveLength(2)
  })

  it('no-body route still rejects a body', () => {
    const ok: StructuredOptionsForOp<NoBodyOp> = {}
    // @ts-expect-error — a route with no body takes none
    const extra: StructuredOptionsForOp<NoBodyOp> = { body: { appliesFrom: '2026-11-09' } }
    expect([ok, extra]).toHaveLength(2)
  })
})
