#!/usr/bin/env python3
"""check_openapi_convention.py — the typed-contract CONVENTION gate (S111 / TASK-11103).

Why this exists (the durability keystone)
-----------------------------------------
S111 commits to OpenAPI as the durable FE<->backend contract. The closure is only
self-sustaining if EVERY *new* endpoint ships typed — otherwise the "fetchEnheder"
shape-mismatch bug class (S97 -> S99 -> S100) just resumes on the next untyped endpoint
(the lesson recurred 3x WITH the lesson written down because nothing CI-enforced it).

This gate forces it. It asserts that every operation in the committed docs/api/openapi.json
carries a NON-EMPTY response schema for its success (2xx) code, and — for body verbs
(POST/PUT/PATCH) — a NON-EMPTY requestBody schema. An untyped `Results.Ok(new {...})` lands
in the spec with an EMPTY `200` content (no schema) — that empty-schema-in-the-spec IS the
detection signal. A new POST that binds no request DTO lands with no requestBody schema —
also caught.

Why it rides the SPEC, not the FE calls [Step-0b BLOCKER]
--------------------------------------------------------
tools/check_endpoint_contracts.py enumerates FE `apiClient.get` URLs, so it is structurally
BLIND to a NEW backend endpoint that has no FE caller yet — which is exactly what "force every
new endpoint typed" must gate. So this gate reads the committed openapi.json (the no-DB
`--openapi` entrypoint's output), independent of whether the FE calls the endpoint.

How the three sibling gates divide the work (complementary, not duplicated)
---------------------------------------------------------------------------
  * check_openapi_convention.py  (THIS gate, Python, `docs` CI job): every NEW operation in the
        spec is TYPED (non-empty response/requestBody schema). Forward-looking convention.
  * check_endpoint_contracts.py  (Python, `docs` CI job): every FE-consumed admin GET has a
        registered contract test. FE-coverage of the existing surface. UNTOUCHED by this gate.
  * check_openapi_sync.py        (.NET regen, `build-and-test` CI job): the committed spec ==
        what the backend generates today. DRIFT of the committed spec.
The per-route spec==RUNTIME-bytes closure (array-ness/camelCase/nullable) is the xUnit
OpenApiSpecRuntimeTests gate. This gate does NOT re-check shape — only that a schema EXISTS.

The grandfather manifest (tools/openapi-convention-exempt.txt)
-------------------------------------------------------------
The existing untyped surface (130 operations as of S111 checkpoint 1) is GRANDFATHERED via a
backend-path EXEMPT manifest, generated ONCE from the current spec. The ~5 already-typed proof
reads + the 1 typed mutation are NOT exempt (they must keep their schemas). A NEW or renamed
operation that is NOT on the manifest and has an empty schema -> the gate FAILS (exit 1).

  THE MANIFEST SHRINKS, NEVER GROWS. As the retrofit (subsequent phases) types an endpoint, its
  line is removed from the manifest — and removing a line can only make the gate STRICTER (that
  operation is now enforced). Do NOT add a line to silence a new failure: type the endpoint
  instead. `--bootstrap` regenerates the whole manifest and exists ONLY for the one-time creation
  (and an audited re-baseline); it is NOT the fix for a gate failure.

Modes
  (default) / --check   every non-grandfathered operation is typed; exit 1 on any untyped one.
  --list                print the full operation surface tagged TYPED / GRANDFATHERED / FAILING.
  --bootstrap           (re)generate the manifest from the current spec. One-time / audited only.
  --selftest            prove the gate fires on a synthesized new untyped operation, and that the
                        committed spec PASSES under the real manifest. Exits 1 by design.

Pure Python — reads the committed docs/api/openapi.json (NO .NET, no Docker), so it runs in the
lightweight Python-only `docs` CI job alongside check_docs.py / check_endpoint_contracts.py.
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
COMMITTED = REPO / "docs" / "api" / "openapi.json"
MANIFEST = REPO / "tools" / "openapi-convention-exempt.txt"

HTTP_VERBS = ("get", "post", "put", "patch", "delete")
BODY_VERBS = ("post", "put", "patch")


# ---------------------------------------------------------------------------
# Spec inspection
# ---------------------------------------------------------------------------

def load_spec(path: Path = COMMITTED) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def _is_success(code: str) -> bool:
    return code.isdigit() and 200 <= int(code) < 300


def _content_has_schema(content: dict | None) -> bool:
    """True iff a `content` block declares at least one media type with a non-empty schema.
    An untyped `Results.Ok(new {...})` emits the success code with NO `content` (or an empty
    `{}` schema) — both are falsy here, which is the detection signal."""
    if not content:
        return False
    for media in content.values():
        if media.get("schema"):  # None or {} -> falsy -> not a real schema
            return True
    return False


def operation_reasons(method: str, op: dict) -> list[str]:
    """Return the list of convention violations for one operation (empty = it PASSES).

    response : no 2xx response code carries a non-empty schema  (the untyped-Ok signal).
    reqbody  : a body verb (POST/PUT/PATCH) with no non-empty requestBody schema
               (a handler binding no request DTO). A genuinely body-less action endpoint is
               the exception that belongs on the grandfather manifest."""
    reasons: list[str] = []
    responses = op.get("responses", {})
    success_codes = [c for c in responses if _is_success(c)]
    if not any(_content_has_schema(responses[c].get("content")) for c in success_codes):
        reasons.append("empty success-response schema")
    if method in BODY_VERBS:
        rb = op.get("requestBody")
        content = rb.get("content") if rb else None
        if not _content_has_schema(content):
            reasons.append("missing requestBody schema (body verb)")
    return reasons


def iter_operations(spec: dict):
    """Yield (key, method, path, op) for every HTTP operation. key = 'METHOD /path'."""
    for path, item in spec.get("paths", {}).items():
        for method in HTTP_VERBS:
            op = item.get(method)
            if op is None:
                continue
            yield f"{method.upper()} {path}", method, path, op


# ---------------------------------------------------------------------------
# The grandfather manifest
# ---------------------------------------------------------------------------

MANIFEST_HEADER = """\
# openapi-convention-exempt.txt — the typed-contract convention grandfather manifest.
#
# Each line is an operation ("METHOD /path") that PRE-DATES the S111 typed-contract
# commitment and is allowed to ship with an empty response/requestBody schema. The gate
# tools/check_openapi_convention.py reads this list: a NEW or renamed operation that is NOT
# here and has an empty schema FAILS CI — forcing it to ship typed.
#
# THIS LIST SHRINKS, NEVER GROWS. As the retrofit (subsequent phases) types an endpoint,
# delete its line — removing a line only makes the gate STRICTER (that operation is now
# enforced). Do NOT add a line to silence a new failure; type the endpoint instead.
# Regenerate ONLY via `python tools/check_openapi_convention.py --bootstrap` for the one-time
# creation / an audited re-baseline. Generated from docs/api/openapi.json. See PAT (S111).
#
# Lines starting with '#' and blank lines are ignored.
"""


def load_manifest() -> set[str]:
    if not MANIFEST.exists():
        return set()
    out: set[str] = set()
    for line in MANIFEST.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        out.add(line)
    return out


def write_manifest(keys: list[str]) -> None:
    body = MANIFEST_HEADER + "\n" + "\n".join(sorted(keys)) + "\n"
    MANIFEST.write_text(body, encoding="utf-8")


# ---------------------------------------------------------------------------
# The check
# ---------------------------------------------------------------------------

def evaluate(spec: dict, manifest: set[str]):
    """Return (failures, grandfathered_hits, typed_keys, stale_entries).

      failures           : [(key, reasons)] for an UNTYPED operation NOT on the manifest -> FAIL.
      grandfathered_hits : keys that are untyped AND on the manifest (allowed, for the count).
      typed_keys         : keys that PASS the convention (and so must NOT be on the manifest).
      stale_entries      : manifest entries that no longer match an untyped operation — either the
                           operation is gone, or it was retrofitted to typed (the entry can be
                           deleted; report-only WARNING)."""
    failures: list[tuple[str, list[str]]] = []
    grandfathered_hits: list[str] = []
    typed_keys: list[str] = []
    live_untyped: set[str] = set()

    for key, method, _path, op in iter_operations(spec):
        reasons = operation_reasons(method, op)
        if reasons:
            live_untyped.add(key)
            if key in manifest:
                grandfathered_hits.append(key)
            else:
                failures.append((key, reasons))
        else:
            typed_keys.append(key)

    stale_entries = sorted(manifest - live_untyped)
    return failures, grandfathered_hits, sorted(typed_keys), stale_entries


def do_check() -> int:
    print("== openapi typed-contract convention gate ==")
    if not COMMITTED.exists():
        print(f"[FAIL] no committed spec at {COMMITTED.relative_to(REPO).as_posix()} -- "
              "generate it with `dotnet run --project src/Backend/StatsTid.Backend.Api -- --openapi`.")
        return 1
    spec = load_spec()
    manifest = load_manifest()
    failures, grandfathered, typed, stale = evaluate(spec, manifest)

    print(f"[info] {len(typed)} typed operation(s); {len(grandfathered)} grandfathered "
          f"(manifest has {len(manifest)} entr(ies)).")

    if stale:
        # S111 Step-7a (Codex): a stale entry = a grandfathered op that is now TYPED (retrofitted)
        # or removed, so its manifest line no longer matches an untyped op. This MUST FAIL (not
        # warn): otherwise a retrofitted op whose line is left behind would be silently
        # re-grandfathered if it later LOST its schema (a false-green for exactly the endpoints
        # the gate protects). Deleting the line makes that op enforced -> the manifest only shrinks.
        print("\nSTALE MANIFEST ENTRIES (FAIL) — these grandfathered ops are now typed/removed; "
              "delete the line(s) so the op becomes enforced (the manifest only SHRINKS):")
        for key in stale:
            print(f"  - {key}")

    if failures:
        print("\nFAILURES — these operations are NOT typed and NOT grandfathered:")
        for key, reasons in sorted(failures):
            print(f"  - {key}  [{'; '.join(reasons)}]")
        print(f"\n{len(failures)} operation(s) violate the typed-contract convention.")
        print("  Fix: give the endpoint a named Contracts/ record + `.Produces<T>()` "
              "(and `.Accepts<T>()` for a body), regenerate the spec")
        print("  (`dotnet run --project src/Backend/StatsTid.Backend.Api -- --openapi` + "
              "git add docs/api/openapi.json), then re-run this gate.")
        print("  Do NOT add the endpoint to tools/openapi-convention-exempt.txt -- that manifest "
              "only SHRINKS as the retrofit types endpoints.")

    if failures or stale:
        return 1

    print(f"\n[ok] every new/non-grandfathered operation is typed "
          f"({len(typed)} typed, {len(grandfathered)} grandfathered).")
    return 0


def do_list() -> int:
    spec = load_spec()
    manifest = load_manifest()
    failures, grandfathered, typed, stale = evaluate(spec, manifest)
    failing_keys = {k for k, _ in failures}
    grand = set(grandfathered)
    print("== openapi operation surface ==")
    for key, method, _path, op in iter_operations(spec):
        if key in failing_keys:
            tag = "FAILING (untyped, NOT grandfathered)"
        elif key in grand:
            tag = "GRANDFATHERED"
        else:
            tag = "TYPED"
        print(f"  {tag:<38} {key}")
    print(f"\n  typed: {len(typed)} | grandfathered: {len(grandfathered)} | "
          f"failing: {len(failures)} | manifest: {len(manifest)} | stale: {len(stale)}")
    return 0


def do_bootstrap() -> int:
    print("== openapi convention gate: BOOTSTRAP the grandfather manifest ==")
    print("  (one-time creation / audited re-baseline ONLY -- do NOT run this to silence a "
          "new gate failure; type the endpoint instead.)")
    spec = load_spec()
    untyped = [key for key, method, _path, op in iter_operations(spec)
               if operation_reasons(method, op)]
    write_manifest(untyped)
    print(f"[ok] wrote {MANIFEST.relative_to(REPO).as_posix()} with {len(untyped)} grandfathered "
          f"operation(s) (the typed proof routes are intentionally excluded).")
    return 0


def do_selftest() -> int:
    # Prove (1) the committed spec PASSES under the real manifest, and (2) a synthesized NEW
    # untyped operation (not on the manifest) FAILS. Exits 1 by design (mirrors the sibling
    # gates' --selftest convention).
    print("== openapi convention gate SELFTEST ==")
    if not COMMITTED.exists() or not MANIFEST.exists():
        print("[FAIL] selftest: the committed spec and/or the grandfather manifest is missing.")
        return 1
    spec = load_spec()
    manifest = load_manifest()

    # (1) the real spec must pass cleanly.
    failures, grandfathered, typed, _stale = evaluate(spec, manifest)
    if failures:
        print("[FAIL] selftest: the COMMITTED spec already fails the gate "
              f"({len(failures)} untyped, non-grandfathered) -- the manifest is out of date:")
        for key, reasons in sorted(failures)[:10]:
            print(f"     {key}  [{'; '.join(reasons)}]")
        return 1
    print(f"[ok] selftest: the committed spec PASSES "
          f"({len(typed)} typed, {len(grandfathered)} grandfathered by the real manifest).")

    # (2) inject fakes that are NOT on the manifest -> must be flagged.
    perturbed = json.loads(json.dumps(spec))  # deep copy
    fake_get = "/api/admin/__selftest_new_untyped_read__"
    fake_post = "/api/admin/__selftest_new_post_without_dto__"
    perturbed["paths"][fake_get] = {
        "get": {"responses": {"200": {"description": "OK"}}}  # empty 200 -> untyped
    }
    perturbed["paths"][fake_post] = {
        "post": {"responses": {"200": {"description": "OK"}}}  # no requestBody, empty 200
    }
    f2, _g2, _t2, _s2 = evaluate(perturbed, manifest)
    flagged = {k for k, _ in f2}
    get_key = f"GET {fake_get}"
    post_key = f"POST {fake_post}"
    if get_key not in flagged or post_key not in flagged:
        print("[FAIL] selftest: a synthesized new untyped operation was NOT flagged -- "
              "the gate is broken.")
        print(f"     flagged: {sorted(flagged)}")
        return 1
    reasons = {k: r for k, r in f2}
    print(f"[ok] selftest: a new untyped GET was flagged       -> {get_key}  "
          f"[{'; '.join(reasons[get_key])}]")
    print(f"[ok] selftest: a new POST lacking a DTO was flagged -> {post_key}  "
          f"[{'; '.join(reasons[post_key])}]")
    print("[ok] selftest exits 1 by design -- the gate is live.")
    return 1


def main(argv: list[str]) -> int:
    arg = argv[0] if argv else "--check"
    if arg in ("--check", "check"):
        return do_check()
    if arg in ("--list", "list"):
        return do_list()
    if arg in ("--bootstrap", "bootstrap"):
        return do_bootstrap()
    if arg in ("--selftest", "selftest"):
        return do_selftest()
    print(f"usage: {Path(sys.argv[0]).name} [--check|--list|--bootstrap|--selftest]")
    return 2


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
