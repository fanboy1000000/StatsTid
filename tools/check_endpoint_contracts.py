#!/usr/bin/env python3
"""Endpoint contract-test coverage gate for StatsTid (S101 / TASK-10103).

Why this exists
---------------
The "fetchEnheder" bug class recurred THREE times (S97 -> S99 -> S100): a FE
list-hook test mocks the right response envelope (vitest green) while the real
endpoint serves a different shape, so prod breaks. The lesson was written down
each time and the bug still came back -- a memory aid is not a gate. This script
is the gate the doc convention lacked.

It makes endpoint-contract-test COVERAGE a CI gate: a NEW FE-consumed
`/api/admin/*` GET endpoint that has no registered contract test (and is not
consciously exempted) FAILS CI, forcing a deliberate decision.

It does NOT check response SHAPE (that's the contract tests' job). It checks that
the coverage didn't silently lag behind the FE's admin-GET surface.

SCOPE / KNOWN BLIND SPOT (be honest about what the gate does and does NOT cover)
-------------------------------------------------------------------------------
The coverage gate enumerates only STATICALLY-INLINE `/api/admin/*` GET URLs --
those passed as a literal string or template literal directly to `apiClient.get`
or `apiFetchWithEtag` (e.g. `apiClient.get('/api/admin/enheder?...')`). A GET
whose URL is built from a `const`/path-helper and passed by reference is NOT
enumerated, so it silently evades the coverage gate. Concretely:
`apiFetchWithEtag(ELIGIBILITY_PATH(employeeId))` in
`frontend/src/hooks/useEntitlementEligibility.ts` builds the URL via a helper and
would not be seen by the enumerator. There is no LIVE coverage gap today (that
particular endpoint is a scalar read and is exempt), but the gate is NOT
"un-foolable": variable/helper-built admin-GET URLs rely on the PAT-010 inline-URL
convention rather than on this gate. To partially compensate, a SOFT secondary
scan (report-only, see `soft_helper_url_scan`) greps non-test FE source for
`/api/admin/` fragments that were NOT statically enumerated as a GET and prints a
`[warn]` per file so a helper-built admin URL is at least SURFACED for a human --
it does NOT change the exit code.

HARD checks (exit 1 on failure):
  1. Coverage: every enumerated FE admin-GET path is in the REGISTRY or the
     EXEMPT-list. A path in neither -> FAIL (add a contract test, or exempt it
     with a reason).
  2. Liveness: every REGISTRY endpoint's contract-test method name is found in
     the Contracts test files AS A METHOD DECLARATION (not merely as a bare
     identifier in a comment/string). A registered method that is absent/renamed
     -> FAIL. (Tolerated as a report-only WARNING while the Contracts test
     directory does not yet exist -- the co-dependent TASK-10102 lands the
     tests in the same sprint; once the directory exists the check is fully
     hard.)

SOFT checks (report-only WARNING -- never change the exit code):
  - Helper-built admin-URL scan (the blind-spot compensator above).
  - Dead EXEMPT entry: an EXEMPT path that matches no enumerated path (surfaced
    so a stale exemption is noticed and removed).

Usage:
    python tools/check_endpoint_contracts.py
    python tools/check_endpoint_contracts.py --list       # print the enumerated surface and exit 0
    python tools/check_endpoint_contracts.py --selftest   # prove the gate is live (must exit 1)
"""
from __future__ import annotations

import re
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
FE_SRC = REPO / "frontend" / "src"
CONTRACTS_DIR = REPO / "tests" / "StatsTid.Tests.Regression" / "Contracts"
# The co-dependent Backend slice (TASK-10102) lands the Pass-1 contract tests in
# this file. Until it exists, the liveness check is DEFERRED (report-only) so the
# Tooling slice can land first; once it exists the liveness check is fully HARD.
PASS1_TESTS = CONTRACTS_DIR / "Pass1EndpointContractTests.cs"

# ---------------------------------------------------------------------------
# THE REGISTRY -- in-scope admin GET endpoints that MUST carry a contract test.
#
# Pass 1 (S101) = the exact 3-bug surface. Each maps to the contract-test method
# name(s) in tests/StatsTid.Tests.Regression/Contracts/Pass1EndpointContractTests.cs
# (TASK-10102, co-dependent slice). These method names are the CONTRACT between
# the Tooling slice (this lint) and the Backend slice (the tests) -- if the
# Backend slice names a method differently, update it HERE too (the liveness
# check enforces the match once the file exists).
# ---------------------------------------------------------------------------
REGISTRY: dict[str, list[str]] = {
    # GET /api/admin/organizations/tree -> { tree: OrgTreeMaoNode[] } envelope.
    # (S103 / TASK-10305: GET /api/admin/enheder was dropped with the legacy Enhed model — its
    # registry entry + nested-enhed assertions are retired; units return in S104+.)
    "/api/admin/organizations/tree": ["GetTree_IsEnvelope_MaoAndOrgNodesCarryFields"],
    # GET /api/admin/organizations -> OrgListItem[] (BARE ARRAY)
    "/api/admin/organizations": ["GetOrganizations_IsBareArray_ItemsCarryOrgFields"],
}

# ---------------------------------------------------------------------------
# THE EXEMPT-LIST -- other FE-consumed admin GETs, each CONSCIOUSLY not-yet-covered.
# path -> one-word reason. Reasons:
#   pass-2         : the approval/roster/allocation family -- richest drift surface,
#                    heavy seed, a recorded S101 follow-up (Pass 2).
#   scalar         : returns a scalar / small fixed-field read (low drift risk;
#                    no envelope/nesting to silently break).
#   single-object  : a by-id single-object read (not a list/envelope -- the
#                    fetchEnheder bug class is list/envelope drift).
#
# Bootstrapped from a one-time enumeration of the existing admin-GET surface
# (--list prints the live set). A NEW admin GET lands in NEITHER list -> FAIL,
# forcing a conscious add-test-or-exempt decision.
# ---------------------------------------------------------------------------
EXEMPT: dict[str, str] = {
    # --- list/collection reads deferred to Pass 2 (the approval/roster family) ---
    "/api/admin/position-overrides": "pass-2",
    "/api/admin/wage-type-mappings": "pass-2",
    "/api/admin/entitlement-configs": "pass-2",
    "/api/admin/users/search": "pass-2",
    "/api/admin/organizations/{}/users": "pass-2",
    "/api/admin/users/{}/roles": "pass-2",
    "/api/admin/reporting-lines/{}/reports": "pass-2",
    "/api/admin/reporting-lines/tree/{}/medarbejdere": "pass-2",
    "/api/admin/audit": "pass-2",
    # --- by-id single-object reads (not list/envelope drift surface) ---
    "/api/admin/users/{}": "single-object",
    "/api/admin/employee-profiles/{}": "single-object",
    "/api/admin/reporting-lines/{}": "single-object",
    "/api/admin/reporting-lines/{}/vikar": "single-object",
    "/api/admin/entitlement-configs/{}": "single-object",
    "/api/admin/employees/{}/birth-date": "single-object",
    "/api/admin/employees/{}/employment-start-date": "single-object",
    # NOTE: the CHILD_SICK entitlement-eligibility GET is NOT exempt-listed here:
    # it is built via the ELIGIBILITY_PATH() helper in useEntitlementEligibility.ts
    # and is therefore NOT statically enumerated (the KNOWN BLIND SPOT documented
    # in the module docstring). A dead `…/entitlement-eligibility/CHILD_SICK:scalar`
    # exempt entry was removed (it matched no enumerated path). The soft helper-URL
    # scan surfaces this file as a `[warn]`; the GET is a scalar read (low drift
    # risk), classified as a Pass-2-or-later decision if it is ever inlined.
}

# ---------------------------------------------------------------------------
# FE enumeration
# ---------------------------------------------------------------------------

# apiClient.get(<url>)  and  apiFetchWithEtag(<url>[, <init>])
# We capture the first string/template-literal argument. For apiFetchWithEtag we
# additionally inspect whether a method other than GET is declared in the call's
# init object (treat as GET only when no method or method: 'GET').
#
# Two URL forms are handled distinctly because a template literal may NEST a
# backtick inside `${ ... }` (e.g. `/x/search${q ? `?${q}` : ''}`), which a naive
# `[^`]*` would truncate mid-interpolation:
#   - plain quote ('...' / "...")  : up to the next matching plain quote
#   - template literal (`...`)      : balanced -- consume nested ${ ... } groups
#                                     (which may themselves contain backticks)
_GET_CALL = re.compile(
    r"""
    (?P<fn>apiClient\.get|apiFetchWithEtag)         # the call
    \s*(?:<[^>]*>)?                                  # optional generic <T>
    \s*\(\s*                                         # open paren
    (?:
        (?P<q>['"])(?P<plain>[^'"]*)(?P=q)           # a plain-quoted url
      |
        `(?P<tmpl>[^`]*)                             # a template-literal url:
                                                     # the STATIC PREFIX up to the
                                                     # first inner backtick (a
                                                     # nested template starts an
                                                     # interpolation -> the path
                                                     # prefix is all we need)
    )
    """,
    re.VERBOSE,
)

# For apiFetchWithEtag we need the method out of the trailing init object (if any).
# Grab the slice from the call site to a reasonable bound and look for `method:`.
_METHOD_IN_INIT = re.compile(r"method\s*:\s*['\"`](?P<method>[A-Za-z]+)['\"`]")


def _normalize(url: str) -> str | None:
    """Return a normalized `/api/admin/...` path, or None if not an admin path.

    - replace `${...}` template segments (a path param) with `{}`
    - strip the query string (`?...`)

    Handles a template prefix that was truncated mid-interpolation by the capture
    (e.g. a nested-template query arg `/x/search${q ? \\`?...`): a dangling `${`
    with no closing `}` means "an interpolation begins here" -> drop from it on
    (the query/conditional segment is not part of the static path).
    """
    # Collapse balanced template-literal interpolations `${...}` -> `{}` (a path
    # segment like `/users/${id}/roles`). Repeat to catch multiple params.
    url = re.sub(r"\$\{[^}]*\}", "{}", url)
    # A remaining (unbalanced) `${` is a truncated interpolation prefix -> drop it.
    url = url.split("${", 1)[0]
    # Drop a query string (literal `?...`, e.g. `/enheder?organisationId=...`).
    url = url.split("?", 1)[0]
    if not url.startswith("/api/admin/"):
        return None
    # Defensive: drop any trailing slash (not expected, but normalize anyway).
    if url != "/api/admin/" and url.endswith("/"):
        url = url.rstrip("/")
    return url


def _iter_fe_source_files():
    """Yield FE .ts/.tsx files, EXCLUDING __tests__/ dirs and *.test.ts(x)."""
    for ext in ("*.ts", "*.tsx"):
        for p in FE_SRC.rglob(ext):
            parts = set(p.parts)
            if "__tests__" in parts:
                continue
            name = p.name
            if name.endswith(".test.ts") or name.endswith(".test.tsx"):
                continue
            yield p


def enumerate_admin_gets() -> dict[str, list[str]]:
    """Return {normalized_path: [source-site, ...]} for FE admin GETs.

    A site is `relpath:lineno`. apiFetchWithEtag calls with a non-GET method are
    skipped (write paths are out of scope).
    """
    found: dict[str, list[str]] = {}
    for path in _iter_fe_source_files():
        try:
            text = path.read_text(encoding="utf-8")
        except Exception:  # noqa: BLE001
            continue
        for m in _GET_CALL.finditer(text):
            raw = m.group("plain") if m.group("plain") is not None else m.group("tmpl")
            norm = _normalize(raw)
            if norm is None:
                continue
            if m.group("fn") == "apiFetchWithEtag":
                # Look at the init object that follows this url arg (same call).
                # Heuristic window: from the url end to the next call site or 400 chars.
                tail = text[m.end():m.end() + 400]
                mm = _METHOD_IN_INIT.search(tail)
                if mm and mm.group("method").upper() != "GET":
                    continue  # a write path (POST/PUT/DELETE) -- out of scope
            lineno = text.count("\n", 0, m.start()) + 1
            rel = path.relative_to(REPO).as_posix()
            found.setdefault(norm, []).append(f"{rel}:{lineno}")
    return found


# Any `/api/admin/...` string fragment appearing in FE source (for the soft scan).
_ADMIN_FRAGMENT = re.compile(r"['\"`](?P<frag>/api/admin/[^'\"`]*)")


def soft_helper_url_scan(found: dict[str, list[str]]) -> list[str]:
    """Report-only scan for the KNOWN BLIND SPOT (a helper/variable-built admin GET).

    Greps non-test FE source for `/api/admin/` string fragments that were NOT
    statically enumerated as a GET call (see `enumerate_admin_gets`). Such a
    fragment is most often a write path (POST/PUT/DELETE -- correctly out of
    coverage scope) or a path-HELPER constant feeding `apiClient.get` /
    `apiFetchWithEtag` by reference (e.g. `ELIGIBILITY_PATH(id)`). The latter is
    the blind spot: it evades the coverage gate. We cannot reliably tell the two
    apart by static fragment alone, so we SURFACE every non-enumerated admin
    fragment as a `[warn]` for a human to classify -- this NEVER changes the exit
    code. Returns a list of warning lines (already deduped, sorted by site)."""
    # The normalized admin-GET paths the hard enumerator already saw. A fragment
    # whose normalized path is one of these is ALREADY covered as a GET (the hard
    # check owns it) -- not a blind-spot candidate. We surface only NORMALIZED
    # paths that NEVER appear as an enumerated GET, which is exactly the
    # blind-spot signal: a helper/variable-built GET the enumerator could not see
    # (plus write-only paths, harmless to surface and worth a human glance).
    enumerated_paths = set(found.keys())
    warns: list[str] = []
    seen: set[str] = set()  # one warning per (normalized) admin path
    for path in _iter_fe_source_files():
        try:
            text = path.read_text(encoding="utf-8")
        except Exception:  # noqa: BLE001
            continue
        rel = path.relative_to(REPO).as_posix()
        for m in _ADMIN_FRAGMENT.finditer(text):
            frag = m.group("frag")
            norm = _normalize(frag)
            if norm is None:
                continue
            if norm in enumerated_paths or norm in seen:
                continue
            seen.add(norm)
            lineno = text.count("\n", 0, m.start()) + 1
            warns.append(
                f"admin path referenced but not statically enumerated as a GET "
                f"-- classify if it is a list GET: {rel}:{lineno}  ({norm})"
            )
    return sorted(warns)


def _contract_test_blob() -> str | None:
    """Concatenated text of all .cs files under the Contracts dir, or None if the
    Pass-1 contract-test FILE does not yet exist (co-dependent TASK-10102 not yet
    landed). Keying on the test file -- not merely the directory -- means a
    helper-only directory (`ContractAssert.cs` present, the tests pending) still
    defers, rather than hard-failing mid-sprint."""
    if not PASS1_TESTS.exists():
        return None
    blob = []
    for p in CONTRACTS_DIR.rglob("*.cs"):
        try:
            blob.append(p.read_text(encoding="utf-8"))
        except Exception:  # noqa: BLE001
            continue
    return "\n".join(blob)


# ---------------------------------------------------------------------------
# Checks
# ---------------------------------------------------------------------------

def run_checks(found: dict[str, list[str]],
               registry: dict[str, list[str]],
               exempt: dict[str, str]) -> tuple[list[str], list[str]]:
    """Return (failures, warnings)."""
    failures: list[str] = []
    warnings: list[str] = []

    # --- Check 1: coverage -- every enumerated path is registered or exempt. ---
    uncovered = sorted(p for p in found if p not in registry and p not in exempt)
    if uncovered:
        for p in uncovered:
            sites = ", ".join(found[p])
            failures.append(
                f"uncovered admin GET {p} (add a contract test -> registry, "
                f"or exempt with a reason)  [{sites}]"
            )
    else:
        print(f"[ok] coverage: all {len(found)} enumerated FE admin-GET path(s) "
              f"are registered ({len(registry)}) or exempt ({len(exempt)})")

    # --- Check 2: liveness -- every registered method exists in the test files. ---
    blob = _contract_test_blob()
    if blob is None:
        warnings.append(
            "Pass-1 contract-test file not present yet "
            f"({PASS1_TESTS.relative_to(REPO).as_posix()}) -- liveness check "
            "deferred until TASK-10102 lands the Pass1 contract tests "
            "(this becomes a HARD failure once the file exists)"
        )
    else:
        missing: list[str] = []
        for path, methods in registry.items():
            for method in methods:
                # Match the method as a C# METHOD DECLARATION, not a bare
                # identifier -- a stale comment/string carrying the old name must
                # NOT fool the liveness check (a renamed test would still "pass"
                # if its old name lingered in a doc-comment). Require the
                # `... Task <name>(` declaration form (xUnit `[Fact] async Task
                # Foo()` / `public async Task Foo()` / plain `Task Foo()`).
                decl = r"\bTask\s+" + re.escape(method) + r"\s*\("
                if not re.search(decl, blob):
                    missing.append(f"{path} -> {method}")
        if missing:
            for entry in missing:
                path, method = entry.split(" -> ", 1)
                failures.append(
                    f"registered endpoint {path} has no contract test {method} "
                    f"(in {CONTRACTS_DIR.relative_to(REPO).as_posix()}/)"
                )
        else:
            n = sum(len(v) for v in registry.values())
            print(f"[ok] liveness: all {n} registered contract-test method(s) "
                  f"found in {CONTRACTS_DIR.relative_to(REPO).as_posix()}/")

    # --- Soft check A: dead EXEMPT entries (matched no enumerated path). ---
    # An exempt entry that never matches an enumerated path is stale (the FE call
    # was removed/renamed, or it was helper-built and never enumerable). Surface
    # it so it is noticed and removed -- report-only, never fails CI.
    dead_exempt = sorted(p for p in exempt if p not in found)
    if dead_exempt:
        for p in dead_exempt:
            warnings.append(
                f"dead EXEMPT entry {p} (reason '{exempt[p]}') matches no enumerated "
                f"FE admin-GET path -- remove it, or it is helper-built (the known "
                f"blind spot) and should not be exempt-listed"
            )
    else:
        print(f"[ok] exempt: all {len(exempt)} EXEMPT entr(ies) match an enumerated path")

    # --- Soft check B: helper-built admin-URL scan (the known blind spot). ---
    helper_warns = soft_helper_url_scan(found)
    if helper_warns:
        warnings.extend(helper_warns)
    else:
        print("[ok] blind-spot scan: no non-enumerated /api/admin/ source fragments")

    return failures, warnings


def print_surface(found: dict[str, list[str]],
                  registry: dict[str, list[str]],
                  exempt: dict[str, str]) -> None:
    print("== Enumerated FE admin-GET surface ==")
    for p in sorted(found):
        if p in registry:
            tag = "REGISTRY  -> " + ", ".join(registry[p])
        elif p in exempt:
            tag = f"EXEMPT[{exempt[p]}]"
        else:
            tag = "UNCOVERED"
        sites = ", ".join(found[p])
        print(f"  {p:<58} {tag}")
        print(f"  {'':<58}   ({sites})")
    print(f"\n  total enumerated: {len(found)} | "
          f"registry: {len(registry)} | exempt: {len(exempt)}")


# ---------------------------------------------------------------------------
# Self-test -- prove the gate is LIVE (an unregistered admin GET -> exit 1).
# ---------------------------------------------------------------------------

def selftest() -> int:
    print("== self-test: an unregistered admin GET must FAIL the gate ==")
    # Inject a synthetic enumerated path that is in NEITHER the registry NOR exempt.
    fake_path = "/api/admin/__selftest_unregistered__"
    found = {fake_path: ["<selftest-injected>:0"]}
    failures, _warnings = run_checks(found, REGISTRY, EXEMPT)
    if failures and any(fake_path in f for f in failures):
        print(f"[ok] self-test: the injected path {fake_path} was correctly "
              f"flagged -> the gate is live.")
        for f in failures:
            print(f"     would-FAIL: {f}")
        return 0
    print("[FAIL] self-test: the injected unregistered path was NOT flagged -- "
          "the gate is NOT live!")
    return 1


# ---------------------------------------------------------------------------
# main
# ---------------------------------------------------------------------------

def main(argv: list[str]) -> int:
    if "--selftest" in argv:
        # The self-test asserts the gate fires; it must itself exit NON-ZERO so a
        # CI/manual run can confirm "an unregistered endpoint -> failure". A 0 here
        # means the self-test logic ran cleanly; we then flip to 1 to demonstrate.
        rc = selftest()
        if rc != 0:
            return rc  # the self-test machinery itself broke
        # Demonstrate the live failure by exiting non-zero with the injected path.
        print("\n(self-test mode exits non-zero by design to prove the gate "
              "rejects an unregistered endpoint)")
        return 1

    found = enumerate_admin_gets()

    if "--list" in argv:
        print_surface(found, REGISTRY, EXEMPT)
        return 0

    print("== StatsTid endpoint contract-test coverage check ==")
    failures, warnings = run_checks(found, REGISTRY, EXEMPT)

    if warnings:
        print("\nWARNINGS (report-only):")
        for w in warnings:
            print(f"  - {w}")
    if failures:
        print("\nFAILURES:")
        for f in failures:
            print(f"  - {f}")
        print(f"\n{len(failures)} hard check(s) failed.")
        return 1
    print("\nAll hard checks passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
