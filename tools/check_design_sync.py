#!/usr/bin/env python3
"""
check_design_sync.py — the design-sync drift gate (code <-> Claude Design parity).

The repo's design system — the ui-kit components + the design tokens — is the single
source of truth that BOTH the shipped app (Claude Code, working in-repo) AND the
claude.ai/design "StatsTid UI Kit" project (synced via the /design-sync skill) derive
from. This gate HARD-FAILS CI when that source has changed since the last sync, so design
and code cannot silently drift apart.

HOW IT WORKS
  It hashes the in-scope source (LF-normalized so the hash is identical regardless of
  checkout line-endings) and compares it against a committed fingerprint,
  .design-sync/.synced-fingerprint.json, which is refreshed by `--update` as the final
  step of each /design-sync.

WHAT IT DOES *NOT* DO (be honest)
  CI cannot authenticate to claude.ai, so this gate CANNOT verify that the Claude Design
  upload actually happened. It verifies only that the committed fingerprint MATCHES the
  current source. The discipline is: run `--update` ONLY as the final step of a real
  /design-sync. This is the same process-trust model as check_endpoint_contracts.py
  (a contract test can be registered without being meaningful) — the gate forces the
  conscious "I re-synced" action; it does not prove the bytes reached the server.

MODES
  (default) / --check   compare source vs the fingerprint; exit 1 on ANY drift (hard-fail).
  --update              rewrite the fingerprint from the current source (after a sync).
  --selftest            prove the gate fires on injected drift (exits 1 by design).

RECOVERY when it fails
  The design system changed since the last Claude Design sync. Re-run the /design-sync
  skill (rebuild + upload to the StatsTid UI Kit project), then:
      python tools/check_design_sync.py --update
  and commit .design-sync/.synced-fingerprint.json with your change.
"""
from __future__ import annotations

import hashlib
import json
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
FINGERPRINT = REPO / ".design-sync" / ".synced-fingerprint.json"

# The code<->design parity surface — exactly what /design-sync bundles + uploads from source:
#   * the 20 ui-kit components (.tsx) + their CSS-module styles (flat under ui/; __tests__ nested → excluded)
#   * the barrel (index.ts) — adding/removing a component is a design-system change
#   * the design tokens (tokens.css) — shipped via cfg.cssEntry
#   * the sync config (config.json) — dtsPropsFor / cssEntry / overrides / componentSrcMap are
#     uploaded-bundle INPUTS; a config-only edit changes the synced .d.ts/bundle with no source
#     change, so it MUST be in scope or the gate has a hole (Step-4 finding).
# Deliberately OUT: global.css / utilities.css (not shipped — no ui module CSS @imports them and
# every var(--*) resolves to tokens.css), __tests__, and the Design-only presentation
# (.design-sync/previews/*.tsx, conventions.md — they change Design cards, not code).
UI_DIR = REPO / "frontend" / "src" / "components" / "ui"
NAMED_FILES = [
    REPO / "frontend" / "src" / "components" / "ui" / "index.ts",
    REPO / "frontend" / "src" / "styles" / "tokens.css",
    REPO / ".design-sync" / "config.json",
]


def in_scope_files() -> list[Path]:
    files: list[Path] = []
    files += sorted(UI_DIR.glob("*.tsx"))
    files += sorted(UI_DIR.glob("*.module.css"))
    files += NAMED_FILES
    return files


def file_hash(p: Path) -> str:
    # LF-normalize before hashing: there is no .gitattributes enforcing eol and the working
    # tree is mixed (some .tsx are CRLF, tokens.css is LF), so a raw byte hash would be
    # machine-dependent and CI would false-fire on the first run. Normalize CRLF/CR -> LF.
    raw = p.read_bytes()
    norm = raw.replace(b"\r\n", b"\n").replace(b"\r", b"\n")
    return hashlib.sha256(norm).hexdigest()


def current_map() -> dict[str, str]:
    out: dict[str, str] = {}
    missing: list[str] = []
    for p in in_scope_files():
        rel = p.relative_to(REPO).as_posix()
        if not p.exists():
            missing.append(rel)
            continue
        out[rel] = file_hash(p)
    if missing:
        print("== design-sync drift gate ==")
        for m in missing:
            print(f"[FAIL] expected in-scope file is missing: {m}")
        sys.exit(1)
    return out


def diff(saved: dict[str, str], current: dict[str, str]) -> dict[str, list[str]]:
    saved_keys, cur_keys = set(saved), set(current)
    return {
        "added": sorted(cur_keys - saved_keys),
        "removed": sorted(saved_keys - cur_keys),
        "changed": sorted(k for k in (saved_keys & cur_keys) if saved[k] != current[k]),
    }


def load_saved() -> dict[str, str]:
    if not FINGERPRINT.exists():
        print("== design-sync drift gate ==")
        print(f"[FAIL] no fingerprint at {FINGERPRINT.relative_to(REPO).as_posix()} — "
              "run `python tools/check_design_sync.py --update` after a /design-sync.")
        sys.exit(1)
    data = json.loads(FINGERPRINT.read_text(encoding="utf-8"))
    return data.get("hashes", {})


def write_fingerprint(current: dict[str, str]) -> None:
    payload = {
        "_comment": "design-sync drift-gate fingerprint — LF-normalized sha256 of the source "
                    "that /design-sync uploads to the StatsTid UI Kit project. Refresh with "
                    "`python tools/check_design_sync.py --update` after each sync. See PAT-011.",
        "count": len(current),
        "hashes": dict(sorted(current.items())),
    }
    FINGERPRINT.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")


def do_check() -> int:
    print("== design-sync drift gate ==")
    current = current_map()
    saved = load_saved()
    d = diff(saved, current)
    n = len(d["added"]) + len(d["removed"]) + len(d["changed"])
    if n == 0:
        print(f"[ok] design system in sync with Claude Design ({len(current)} source file(s) "
              "match the last-synced fingerprint).")
        return 0
    print(f"[FAIL] the design system changed since the last Claude Design sync "
          f"({n} file(s) drifted):")
    for k in d["changed"]:
        print(f"    changed:  {k}")
    for k in d["added"]:
        print(f"    added:    {k}")
    for k in d["removed"]:
        print(f"    removed:  {k}")
    print("  Re-run the /design-sync skill (rebuild + upload to the StatsTid UI Kit project), then:")
    print("    python tools/check_design_sync.py --update   &&   commit .design-sync/.synced-fingerprint.json")
    return 1


def do_update() -> int:
    current = current_map()
    write_fingerprint(current)
    print(f"[ok] wrote {FINGERPRINT.relative_to(REPO).as_posix()} "
          f"({len(current)} source file(s)). Commit it with your /design-sync.")
    return 0


def do_selftest() -> int:
    # Build the current map, then simulate a drifted component (mutate one hash). The diff
    # MUST report it. Exits 1 by design to prove the gate is live (mirrors
    # check_endpoint_contracts.py --selftest).
    print("== design-sync drift gate SELFTEST ==")
    current = current_map()
    if not current:
        print("[FAIL] selftest: no in-scope files found")
        return 1
    victim = next(iter(sorted(current)))
    drifted = dict(current)
    drifted[victim] = "0" * 64  # a fake changed hash
    drifted["frontend/src/components/ui/_PhantomNew.tsx"] = "f" * 64  # a fake added file
    d = diff(current, drifted)
    ok = victim in d["changed"] and "frontend/src/components/ui/_PhantomNew.tsx" in d["added"]
    if not ok:
        print("[FAIL] selftest: the diff did NOT detect injected drift — the gate is broken")
        return 1
    print(f"[ok] selftest: injected drift detected (changed: {victim}; added: _PhantomNew.tsx).")
    print("[ok] selftest exits 1 by design — the gate is live.")
    return 1


def main() -> int:
    arg = sys.argv[1] if len(sys.argv) > 1 else "--check"
    if arg in ("--check", "check"):
        return do_check()
    if arg in ("--update", "update"):
        return do_update()
    if arg in ("--selftest", "selftest"):
        return do_selftest()
    print(f"usage: {Path(sys.argv[0]).name} [--check|--update|--selftest]")
    return 2


if __name__ == "__main__":
    sys.exit(main())
