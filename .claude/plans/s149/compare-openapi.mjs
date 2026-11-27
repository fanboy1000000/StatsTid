// S149 (PLAN-s149.md, AC-O2) — the allow-listed parsed comparison of two OpenAPI documents.
//
// Why it exists: Swashbuckle 6.6.2 -> 10.3.1 regenerates docs/api/openapi.json through a rewritten
// ResponseStrictTypesFilter. CI's drift gate (tools/check_openapi_sync.py) only proves "committed ==
// regenerated", which a regenerate-and-commit makes true by construction. This script proves the
// regenerated document says the same thing as S148's close (df582c20), apart from an enumerated
// allow-list, so no field silently loses `nullable`, `required`, `enum` or a `$ref`.
//
// Usage (repo root; Node 24; Python is not installed locally, so this is Node, not a tools/*.py gate):
//   git show df582c20:docs/api/openapi.json > <scratch>/openapi.base.json
//   node .claude/plans/s149/compare-openapi.mjs <scratch>/openapi.base.json <the new openapi.json>
// Exit 0: equal after the allow-list. Exit 1: every differing JSON pointer is printed. Exit 2: usage.
//
// The allow-list is the plan's (AL-1 ... ). Adding an entry is an Orchestrator ruling recorded in the
// sprint log, never a silent edit of this file.
//
// Array rule (PLAN-s149.md P-3; Step 0b cycle 1 N7, cycle 2 Codex W). Only the JSON Schema `required` keyword
// is compared as a set: a `required` array whose parent is a schema, known by its position (a value of
// components.schemas or of `properties`, an `items`, `not` or `additionalProperties`, an element of
// `allOf`/`oneOf`/`anyOf`, or a `schema` under a parameter, header or media type). Its members must be
// strings, compared without coercion; a non-string member, an added or removed member and a duplicate are
// each reported. Every other array is order-sensitive, including anything inside `example`, `examples`,
// `default` and `enum`, because order there can reach frontend/src/lib/api-types.ts.

import { readFileSync } from 'node:fs';

const ALLOW = [
  // AL-1: the OpenAPI version string. OpenAPI.NET 2.x writes its own 3.0.x patch number.
  '/openapi',
];

function load(path) {
  return JSON.parse(readFileSync(path, 'utf8').replace(/^﻿/, ''));
}

function del(doc, pointer) {
  const parts = pointer.split('/').slice(1).map(p => p.replace(/~1/g, '/').replace(/~0/g, '~'));
  let node = doc;
  for (let i = 0; i < parts.length - 1; i++) {
    if (node == null || typeof node !== 'object') return;
    node = node[parts[i]];
  }
  if (node != null && typeof node === 'object') delete node[parts[parts.length - 1]];
}

const esc = k => String(k).replace(/~/g, '~0').replace(/\//g, '~1');

// Where a child sits, given its parent's context and its key. Contexts: doc, components, other (structural
// OpenAPI), schemaMap (name -> schema), schema, schemaList (array of schemas), payload (plain data).
function childContext(ctx, key) {
  switch (ctx) {
    case 'doc': return key === 'components' ? 'components' : 'other';
    case 'components': return key === 'schemas' ? 'schemaMap' : 'other';
    case 'other':
      if (key === 'schema') return 'schema';
      if (key === 'example' || key === 'examples') return 'payload';
      return 'other';
    case 'schemaMap': return 'schema';
    case 'schemaList': return 'schema';
    case 'schema':
      if (key === 'properties') return 'schemaMap';
      if (key === 'items' || key === 'not' || key === 'additionalProperties') return 'schema';
      if (key === 'allOf' || key === 'oneOf' || key === 'anyOf') return 'schemaList';
      if (key === 'required') return 'required';
      return 'payload';
    default: return 'payload';
  }
}

function compareRequired(a, b, at, out) {
  for (const [side, arr] of [['base', a], ['new', b]]) {
    arr.forEach((m, i) => {
      if (typeof m !== 'string') out.push(`${at}/${i}: non-string required member (${side}): ${JSON.stringify(m)}`);
    });
  }
  const sa = a.filter(m => typeof m === 'string');
  const sb = b.filter(m => typeof m === 'string');
  const A = new Set(sa), B = new Set(sb);
  for (const m of A) if (!B.has(m)) out.push(`${at}: required member removed: ${m}`);
  for (const m of B) if (!A.has(m)) out.push(`${at}: required member added: ${m}`);
  if (A.size !== sa.length) out.push(`${at}: duplicate required member (base)`);
  if (B.size !== sb.length) out.push(`${at}: duplicate required member (new)`);
}

function diff(a, b, at, out, ctx) {
  if (a === b) return;
  const ta = Array.isArray(a) ? 'array' : a === null ? 'null' : typeof a;
  const tb = Array.isArray(b) ? 'array' : b === null ? 'null' : typeof b;
  if (ta !== tb) { out.push(`${at || '/'}: ${ta} -> ${tb}`); return; }
  if (ta === 'array' && ctx === 'required') { compareRequired(a, b, at, out); return; }
  if (ta === 'array') {
    const elementCtx = ctx === 'schemaList' ? 'schema' : ctx === 'other' ? 'other' : 'payload';
    if (a.length !== b.length) out.push(`${at || '/'}: length ${a.length} -> ${b.length}`);
    for (let i = 0; i < Math.min(a.length, b.length); i++) diff(a[i], b[i], `${at}/${i}`, out, elementCtx);
    return;
  }
  if (ta === 'object') {
    for (const k of new Set([...Object.keys(a), ...Object.keys(b)])) {
      if (!(k in b)) out.push(`${at}/${esc(k)}: removed`);
      else if (!(k in a)) out.push(`${at}/${esc(k)}: added`);
      else diff(a[k], b[k], `${at}/${esc(k)}`, out, childContext(ctx, k));
    }
    return;
  }
  out.push(`${at || '/'}: ${JSON.stringify(a)} -> ${JSON.stringify(b)}`);
}

const [basePath, newPath] = process.argv.slice(2);
if (!basePath || !newPath) {
  console.error('usage: node compare-openapi.mjs <base openapi.json> <new openapi.json>');
  process.exit(2);
}
const base = load(basePath);
const next = load(newPath);
for (const p of ALLOW) { del(base, p); del(next, p); }
const out = [];
diff(base, next, '', out, 'doc');
console.log(`allow-list: ${ALLOW.join(', ')}`);
if (out.length === 0) {
  console.log(`[ok] equal after the allow-list (${Object.keys(base.paths ?? {}).length} paths, ` +
    `${Object.keys(base.components?.schemas ?? {}).length} schemas)`);
  process.exit(0);
}
console.log(`[DIFF] ${out.length} difference(s) outside the allow-list:`);
for (const line of out) console.log(`  ${line}`);
process.exit(1);
