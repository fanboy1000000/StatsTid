#!/usr/bin/env python3
"""check_openapi_convention.py — the typed-contract CONVENTION gate (S111 / TASK-11103).

Why this exists (the durability keystone)
-----------------------------------------
S111 commits to OpenAPI as the durable FE<->backend contract. The closure is only
self-sustaining if EVERY *new* endpoint ships typed — otherwise the "fetchEnheder"
shape-mismatch bug class (S97 -> S99 -> S100) just resumes on the next untyped endpoint
(the lesson recurred 3x WITH the lesson written down because nothing CI-enforced it).

This gate forces it. It asserts that every operation in the committed docs/api/openapi.json
carries a NON-EMPTY response schema for its success (2xx) code — or, per the S112
owner-ratified "declared-204" amendment, declares `204` as its ONLY success response with no
content (`.Produces(204)` replaces Swashbuckle's inferred empty `200`, so a declared-204-only
op is the typed statement "this intentionally has no body") — and, for body verbs
(POST/PUT/PATCH), a NON-EMPTY requestBody schema. An untyped `Results.Ok(new {...})` lands
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
# S116 owner-ratified "declared body-less POST" amendment (the request-side analog of the
# S112 declared-204 rule): a route-param-only action op (e.g. POST /approve) legitimately
# binds NO request DTO. Such an op is compliant IFF it is EXPLICITLY listed here AND its
# response is typed. The detection signal is preserved: an UNLISTED body verb with no
# requestBody schema still FAILS (the "forgot the request DTO" tripwire), and a listed op
# whose response is untyped still FAILS (liveness). Stale declarations (op gone, op grew a
# body, or not a body verb) FAIL like stale manifest lines.
BODYLESS_DECLARED = REPO / "tools" / "openapi-bodyless-declared.txt"

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

    response : PASSES when EITHER some 2xx response carries a non-empty content schema (the
               untyped-Ok signal, inverted), OR the operation's ONLY success (2xx) response is
               a DECLARED `204` with no content — the S112 owner-ratified "declared-204"
               amendment: `.Produces(204)` replaces Swashbuckle's inferred empty `200`, so a
               declared-204-only op is the typed statement "this intentionally has no body".
               A body-less endpoint therefore no longer belongs on the grandfather manifest —
               declare the 204 instead (supersedes the S111 guidance). STRICTNESS preserved:
               an op with NO success code at all still FAILS, and a MIXED op (an inferred
               empty `200` PLUS the `204`) still FAILS — "only success is 204" is load-bearing.
    reqbody  : a body verb (POST/PUT/PATCH) with no non-empty requestBody schema
               (a handler binding no request DTO)."""
    reasons: list[str] = []
    responses = op.get("responses", {})
    success_codes = [c for c in responses if _is_success(c)]
    typed_body = any(_content_has_schema(responses[c].get("content")) for c in success_codes)
    declared_204_only = (
        success_codes == ["204"]
        and not _content_has_schema(responses["204"].get("content"))
    )
    if not (typed_body or declared_204_only):
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


def load_bodyless_declared() -> set[str]:
    """The S116 declared body-less list — same 'METHOD /path' line format as the manifest."""
    if not BODYLESS_DECLARED.exists():
        return set()
    out: set[str] = set()
    for line in BODYLESS_DECLARED.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        out.add(line)
    return out


# ---------------------------------------------------------------------------
# The check
# ---------------------------------------------------------------------------

def evaluate(spec: dict, manifest: set[str], bodyless_declared: set[str] | None = None):
    """Return (failures, grandfathered_hits, typed_keys, stale_entries, bodyless_stale).

      failures           : [(key, reasons)] for an UNTYPED operation NOT on the manifest -> FAIL.
      grandfathered_hits : keys that are untyped AND on the manifest (allowed, for the count).
      typed_keys         : keys that PASS the convention (and so must NOT be on the manifest).
      stale_entries      : manifest entries that no longer match an untyped operation — either the
                           operation is gone, or it was retrofitted to typed (the entry can be
                           deleted; report-only WARNING).
      bodyless_stale     : declared body-less entries that are no longer valid declarations —
                           the op is gone, is not a body verb, or now HAS a requestBody schema
                           (the declaration would mask the detection signal) -> FAIL.

    S116 declared-bodyless semantics: for a key in `bodyless_declared`, ONLY the
    'missing requestBody schema' reason is waived — an untyped RESPONSE on a declared op
    still fails (liveness), and an UNDECLARED body verb with no requestBody still fails."""
    bodyless_declared = bodyless_declared or set()
    failures: list[tuple[str, list[str]]] = []
    grandfathered_hits: list[str] = []
    typed_keys: list[str] = []
    live_untyped: set[str] = set()
    seen_keys: dict[str, tuple[str, dict]] = {}

    for key, method, _path, op in iter_operations(spec):
        seen_keys[key] = (method, op)
        reasons = operation_reasons(method, op)
        if key in bodyless_declared:
            reasons = [r for r in reasons if r != "missing requestBody schema (body verb)"]
        if reasons:
            live_untyped.add(key)
            # A DECLARED body-less op is never eligible for grandfathering (S116 Step-7a,
            # Codex): the declaration's liveness rule (untyped response -> RED) must not be
            # maskable by a retained manifest line — double membership would accept exactly
            # the declared+untyped case the rule keeps red.
            if key in manifest and key not in bodyless_declared:
                grandfathered_hits.append(key)
            else:
                failures.append((key, reasons))
        else:
            typed_keys.append(key)

    stale_entries = sorted(manifest - live_untyped)

    bodyless_stale: list[tuple[str, str]] = []
    for key in sorted(bodyless_declared):
        if key in manifest:
            bodyless_stale.append((key, "ALSO on the grandfather manifest — a declared "
                                        "body-less op cannot be grandfathered; delete one "
                                        "of the two lines"))
        if key not in seen_keys:
            bodyless_stale.append((key, "operation no longer exists in the spec"))
            continue
        method, op = seen_keys[key]
        if method not in BODY_VERBS:
            bodyless_stale.append((key, "not a body verb — the declaration is meaningless"))
            continue
        rb = op.get("requestBody")
        if _content_has_schema(rb.get("content") if rb else None):
            bodyless_stale.append((key, "the operation now HAS a requestBody schema — "
                                        "delete the declaration (it would mask the tripwire)"))

    return failures, grandfathered_hits, sorted(typed_keys), stale_entries, bodyless_stale


def do_check() -> int:
    print("== openapi typed-contract convention gate ==")
    if not COMMITTED.exists():
        print(f"[FAIL] no committed spec at {COMMITTED.relative_to(REPO).as_posix()} -- "
              "generate it with `dotnet run --project src/Backend/StatsTid.Backend.Api -- --openapi`.")
        return 1
    spec = load_spec()
    manifest = load_manifest()
    bodyless = load_bodyless_declared()
    failures, grandfathered, typed, stale, bodyless_stale = evaluate(spec, manifest, bodyless)

    print(f"[info] {len(typed)} typed operation(s); {len(grandfathered)} grandfathered "
          f"(manifest has {len(manifest)} entr(ies)); "
          f"{len(bodyless)} declared body-less.")

    if bodyless_stale:
        print("\nSTALE BODY-LESS DECLARATIONS (FAIL) — delete/fix the line(s) in "
              f"{BODYLESS_DECLARED.relative_to(REPO).as_posix()}:")
        for key, why in bodyless_stale:
            print(f"  - {key}  [{why}]")

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

    if failures or stale or bodyless_stale:
        return 1

    print(f"\n[ok] every new/non-grandfathered operation is typed "
          f"({len(typed)} typed, {len(grandfathered)} grandfathered).")
    return 0


def do_list() -> int:
    spec = load_spec()
    manifest = load_manifest()
    failures, grandfathered, typed, stale, _bodyless_stale = evaluate(
        spec, manifest, load_bodyless_declared())
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
    bodyless = load_bodyless_declared()
    failures, grandfathered, typed, _stale, _bs = evaluate(spec, manifest, bodyless)
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
    f2, _g2, _t2, _s2, _bs2 = evaluate(perturbed, manifest, bodyless)
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

    # (3) the S112 declared-204 amendment: a declared-204-ONLY op must be ACCEPTED (typed —
    # `.Produces(204)` is the "intentionally no body" statement), and an op with NO declared
    # success code at all must still be REJECTED.
    fake_del = "/api/admin/__selftest_declared_204_only_delete__"
    fake_nosuccess = "/api/admin/__selftest_no_success_code__"
    perturbed["paths"][fake_del] = {
        "delete": {"responses": {"204": {"description": "No Content"}}}  # only-success=204 -> typed
    }
    perturbed["paths"][fake_nosuccess] = {
        "get": {"responses": {"404": {"description": "Not Found"}}}  # no 2xx at all -> untyped
    }
    f3, _g3, _t3, _s3, _bs3 = evaluate(perturbed, manifest, bodyless)
    flagged3 = {k for k, _ in f3}
    del_key = f"DELETE {fake_del}"
    nosuccess_key = f"GET {fake_nosuccess}"
    if del_key in flagged3:
        reasons3 = {k: r for k, r in f3}
        print("[FAIL] selftest: a declared-204-only operation WAS flagged -- the S112 "
              f"declared-204 amendment is broken.  [{'; '.join(reasons3[del_key])}]")
        return 1
    if nosuccess_key not in flagged3:
        print("[FAIL] selftest: an operation with NO success code was NOT flagged -- "
              "the gate is broken.")
        return 1
    print(f"[ok] selftest: a declared-204-only DELETE was ACCEPTED -> {del_key}")
    print(f"[ok] selftest: an op with NO success code was flagged  -> {nosuccess_key}  "
          f"[{'; '.join(dict(f3)[nosuccess_key])}]")

    # (4) the S116 declared body-less POST amendment, all four directions:
    #     declared + typed response  -> ACCEPTED (the waiver works)
    #     declared + untyped response -> REJECTED (liveness — the waiver covers ONLY reqbody)
    #     undeclared body-less POST   -> REJECTED (case (2) above already proves the tripwire)
    #     stale declaration (op grew a body / op gone) -> bodyless_stale FAIL
    fake_action = "/api/admin/__selftest_declared_bodyless_action__"
    fake_action_untyped = "/api/admin/__selftest_declared_bodyless_untyped__"
    fake_gone = "/api/admin/__selftest_declared_bodyless_gone__"
    schema_ref = {"application/json": {"schema": {"$ref": "#/components/schemas/__x__"}}}
    perturbed["paths"][fake_action] = {
        "post": {"responses": {"200": {"description": "OK", "content": schema_ref}}}
    }
    perturbed["paths"][fake_action_untyped] = {
        "post": {"responses": {"200": {"description": "OK"}}}  # untyped response, no body
    }
    synthetic_declared = {
        f"POST {fake_action}",
        f"POST {fake_action_untyped}",
        f"POST {fake_gone}",  # not in the spec -> stale
    }
    f4, _g4, t4, _s4, bs4 = evaluate(perturbed, manifest, bodyless | synthetic_declared)
    flagged4 = {k for k, _ in f4}
    action_key = f"POST {fake_action}"
    untyped_key = f"POST {fake_action_untyped}"
    gone_key = f"POST {fake_gone}"
    if action_key not in t4:
        print("[FAIL] selftest: a DECLARED body-less POST with a typed response was NOT "
              "accepted -- the S116 amendment is broken.")
        return 1
    if untyped_key not in flagged4:
        print("[FAIL] selftest: a DECLARED body-less POST with an UNTYPED response was NOT "
              "flagged -- the declaration waives too much.")
        return 1
    if gone_key not in {k for k, _ in bs4}:
        print("[FAIL] selftest: a STALE body-less declaration was NOT flagged.")
        return 1
    print(f"[ok] selftest: a declared body-less POST (typed response) was ACCEPTED -> {action_key}")
    print(f"[ok] selftest: a declared body-less POST with an UNTYPED response stays RED -> "
          f"{untyped_key}  [{'; '.join(dict(f4)[untyped_key])}]")
    print(f"[ok] selftest: a stale body-less declaration was flagged -> {gone_key}")

    # (5) S116 Step-7a (Codex): DOUBLE MEMBERSHIP must not mask the liveness rule — an op
    #     that is declared body-less AND on the grandfather manifest, with an UNTYPED
    #     response, must (a) fail rather than count as grandfathered, and (b) flag the
    #     double membership as a stale declaration.
    f5, g5, _t5, _s5, bs5 = evaluate(perturbed, manifest | {untyped_key}, bodyless | synthetic_declared)
    if untyped_key in g5:
        print("[FAIL] selftest: a declared+manifest-listed body-less op with an untyped "
              "response was accepted as GRANDFATHERED -- the manifest masks the liveness rule.")
        return 1
    if untyped_key not in {k for k, _ in f5}:
        print("[FAIL] selftest: the declared+manifest-listed untyped op did not FAIL.")
        return 1
    if untyped_key not in {k for k, _ in bs5}:
        print("[FAIL] selftest: double membership (declared + manifest) was NOT flagged stale.")
        return 1
    print(f"[ok] selftest: declared+manifest double membership stays RED (not grandfathered, "
          f"flagged stale) -> {untyped_key}")

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
