#!/usr/bin/env python3
"""
check_openapi_sync.py — the OpenAPI drift gate (committed spec <-> the live endpoint surface).

The committed docs/api/openapi.json is the SOURCE OF TRUTH the Fork-B typed FE client is generated
from (S111 / TASK-11101). This gate HARD-FAILS CI when the committed spec no longer matches what the
backend would generate TODAY — i.e. someone changed a typed endpoint / a Contracts record and forgot
to regenerate + commit the spec. Mirrors tools/check_design_sync.py's role for the design system and
tools/generate_db_schema.py / check_docs.py's generate-and-gate pattern for db-schema.md.

HOW IT WORKS
  --check (default): regenerate the spec into a TEMP file via the no-DB `--openapi` entrypoint
    (`dotnet run --project src/Backend/StatsTid.Backend.Api -- --openapi <tmp>`), parse BOTH the temp
    and the committed docs/api/openapi.json, and compare the PARSED JSON (so whitespace / key-order
    differences never false-fire — only real content drift fails). Exit 1 on any difference. This is
    DOCKER-FREE: the `--openapi` entrypoint maps the endpoints + writes the doc BEFORE the seeders /
    hosted services run, so no database is needed.
  --selftest: prove the gate fires on injected drift (no dotnet needed) — exits 1 by design.

RECOVERY when --check fails
  The committed spec is stale. Regenerate + commit it:
      dotnet run --project src/Backend/StatsTid.Backend.Api -- --openapi
      git add docs/api/openapi.json
  (and, in a later phase, re-run `npm run gen:api` to refresh frontend/src/lib/api-types.ts).

NOTE (honest framing, per the refinement): this gate proves committed-spec == regenerated-spec. The
per-route spec == RUNTIME-bytes closure is the xUnit OpenApiSpecRuntimeTests gate (it asserts the
schema matches REAL serialized responses — array-ness / camelCase / nullable). The two are complementary.
"""
from __future__ import annotations

import json
import subprocess
import sys
import tempfile
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
COMMITTED = REPO / "docs" / "api" / "openapi.json"
API_PROJECT = REPO / "src" / "Backend" / "StatsTid.Backend.Api"


def load_json(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def regenerate_to(tmp: Path) -> None:
    """Run the no-DB --openapi entrypoint, writing the spec to `tmp`. Raises on failure."""
    cmd = ["dotnet", "run", "--project", str(API_PROJECT), "--", "--openapi", str(tmp)]
    proc = subprocess.run(cmd, cwd=str(REPO), capture_output=True, text=True)
    if proc.returncode != 0 or not tmp.exists():
        print("== openapi drift gate ==")
        print("[FAIL] could not regenerate the spec via the --openapi entrypoint:")
        print(f"    command: {' '.join(cmd)}")
        sys.stdout.write(proc.stdout[-2000:])
        sys.stderr.write(proc.stderr[-2000:])
        sys.exit(2)


def diff_summary(committed: dict, regenerated: dict) -> list[str]:
    """A human-readable summary of WHERE the two specs diverge (best-effort; the equality check is
    authoritative). Surfaces added/removed/changed operations, then a generic top-level fallback."""
    out: list[str] = []
    cpaths = committed.get("paths", {})
    rpaths = regenerated.get("paths", {})
    ckeys, rkeys = set(cpaths), set(rpaths)
    for p in sorted(rkeys - ckeys):
        out.append(f"    added path:    {p}")
    for p in sorted(ckeys - rkeys):
        out.append(f"    removed path:  {p}")
    for p in sorted(ckeys & rkeys):
        if cpaths[p] != rpaths[p]:
            out.append(f"    changed path:  {p}")
    cschemas = committed.get("components", {}).get("schemas", {})
    rschemas = regenerated.get("components", {}).get("schemas", {})
    for s in sorted(set(rschemas) - set(cschemas)):
        out.append(f"    added schema:   {s}")
    for s in sorted(set(cschemas) - set(rschemas)):
        out.append(f"    removed schema: {s}")
    for s in sorted(set(cschemas) & set(rschemas)):
        if cschemas[s] != rschemas[s]:
            out.append(f"    changed schema: {s}")
    if not out:
        out.append("    (the divergence is outside paths/components — info/servers/etc.)")
    return out


def do_check() -> int:
    print("== openapi drift gate ==")
    if not COMMITTED.exists():
        print(f"[FAIL] no committed spec at {COMMITTED.relative_to(REPO).as_posix()} — "
              "generate it with `dotnet run --project src/Backend/StatsTid.Backend.Api -- --openapi`.")
        return 1

    with tempfile.TemporaryDirectory() as td:
        tmp = Path(td) / "openapi.regen.json"
        regenerate_to(tmp)
        regenerated = load_json(tmp)

    committed = load_json(COMMITTED)
    if committed == regenerated:
        npaths = len(committed.get("paths", {}))
        nschemas = len(committed.get("components", {}).get("schemas", {}))
        print(f"[ok] committed spec in sync with the live endpoint surface "
              f"({npaths} paths, {nschemas} schemas).")
        return 0

    print("[FAIL] the committed docs/api/openapi.json is STALE — it no longer matches what the "
          "backend generates. A typed endpoint / Contracts record changed without a spec regen:")
    for line in diff_summary(committed, regenerated):
        print(line)
    print("  Regenerate + commit:")
    print("    dotnet run --project src/Backend/StatsTid.Backend.Api -- --openapi   &&   git add docs/api/openapi.json")
    return 1


def do_selftest() -> int:
    # Build the committed spec, inject a drift (drop one path), and prove the comparison detects it.
    # Exits 1 by design (mirrors check_design_sync.py / check_endpoint_contracts.py --selftest).
    print("== openapi drift gate SELFTEST ==")
    if not COMMITTED.exists():
        print(f"[FAIL] selftest: no committed spec at {COMMITTED.relative_to(REPO).as_posix()}.")
        return 1
    committed = load_json(COMMITTED)
    if not committed.get("paths"):
        print("[FAIL] selftest: committed spec has no paths to perturb.")
        return 1
    drifted = json.loads(json.dumps(committed))  # deep copy
    victim = next(iter(sorted(drifted["paths"])))
    del drifted["paths"][victim]
    if committed == drifted:
        print("[FAIL] selftest: injected drift was NOT detected — the gate is broken.")
        return 1
    summary = diff_summary(drifted, committed)  # committed has the victim back → "added path: victim"
    print(f"[ok] selftest: injected drift detected (removed path: {victim}).")
    for line in summary:
        print(line)
    print("[ok] selftest exits 1 by design — the gate is live.")
    return 1


def main() -> int:
    arg = sys.argv[1] if len(sys.argv) > 1 else "--check"
    if arg in ("--check", "check"):
        return do_check()
    if arg in ("--selftest", "selftest"):
        return do_selftest()
    print(f"usage: {Path(sys.argv[0]).name} [--check|--selftest]")
    return 2


if __name__ == "__main__":
    sys.exit(main())
